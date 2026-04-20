using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Emits the JavaScript equivalent of a single Razor component class:
/// the class header (`export class X extends Component`), each supported
/// member in source order, and the closing brace.
/// </summary>
internal static class ClassEmitter
{
    public const string Indent = "  ";

    public static void Emit(ClassDeclarationSyntax component, SemanticModel model, StringBuilder sb)
    {
        var name = component.Identifier.Text;
        var jsBase = ComponentClassifier.MapBaseToJs(component);
        var injects = CollectInjects(component);
        var ctx = new EmitContext
        {
            ClassMembers = CollectMemberNames(component),
            Model = model,
        };

        sb.Append("export class ").Append(name).Append(" extends ").Append(jsBase).Append(" {\n");
        EmitMembers(component, ctx, injects, sb);
        sb.Append("}\n");
    }

    /// <summary>
    /// Emit a non-Razor class — a <c>[Client]</c>-marked service such as an
    /// <c>ApiClient</c> subclass. Base class is taken from the source (first
    /// entry in the base list that isn't an interface); interfaces are dropped
    /// because JS has no nominal interface concept. No <c>_injects</c>
    /// manifest — these classes are registered as-is in the container.
    /// </summary>
    public static void EmitPlain(ClassDeclarationSyntax cls, SemanticModel model, StringBuilder sb)
    {
        var name = cls.Identifier.Text;
        var baseName = GetBaseClassName(cls);
        var ctx = new EmitContext
        {
            ClassMembers = CollectMemberNames(cls),
            Model = model,
        };

        sb.Append("export class ").Append(name);
        if (baseName is not null) sb.Append(" extends ").Append(baseName);
        sb.Append(" {\n");
        EmitPrimaryConstructor(cls, ctx, sb);
        EmitMembers(cls, ctx, injects: [], sb);
        sb.Append("}\n");
    }

    private static void EmitMembers(
        ClassDeclarationSyntax cls,
        EmitContext ctx,
        List<(string JsName, string ServiceKey)> injects,
        StringBuilder sb)
    {

        // Surface [Inject] properties as a static manifest the runtime reads
        // when constructing the component. Non-inject members keep their
        // normal emission path.
        EmitInjectManifest(injects, sb);

        foreach (var member in cls.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    FieldEmitter.Emit(field, sb, ctx);
                    break;

                case PropertyDeclarationSyntax prop when HasInjectAttribute(prop):
                    // Already surfaced via the _injects manifest — nothing else to emit.
                    break;

                case PropertyDeclarationSyntax prop:
                    PropertyEmitter.Emit(prop, sb, ctx);
                    break;

                case MethodDeclarationSyntax method when method.Identifier.Text == "BuildRenderTree":
                    RenderTreeEmitter.Emit(method, sb, ctx);
                    break;

                case MethodDeclarationSyntax method:
                    MethodEmitter.Emit(method, sb, ctx);
                    break;

                // Nested classes and constructors are deferred to later stages.
                default:
                    break;
            }
        }
    }

    // Primary constructors (C# 12) — `class X(HttpClient http) : Base(http)`.
    // Emits a JS `constructor(...) { super(...); this.param = param; }` that
    // both forwards to the base class and captures parameters as instance
    // fields so method bodies that reference them as bare identifiers resolve.
    // When no primary constructor is declared we emit nothing and let the JS
    // class synth a default.
    private static void EmitPrimaryConstructor(ClassDeclarationSyntax cls, EmitContext ctx, StringBuilder sb)
    {
        if (cls.ParameterList is null) return;
        var parameters = cls.ParameterList.Parameters;
        if (parameters.Count == 0 && GetPrimaryBaseArguments(cls) is null) return;

        // Inside the constructor, primary-ctor parameters are local variables,
        // not `this.<name>` members — those are only populated after the
        // `this.x = x` assignments below. So we emit base arguments with a
        // scope that doesn't rewrite the param names (otherwise `super(http)`
        // would become `super(this.http)` and blow up with TDZ on `this`).
        var primaryParamSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in parameters) primaryParamSet.Add(p.Identifier.Text);
        var ctorMembers = new HashSet<string>(ctx.ClassMembers, StringComparer.Ordinal);
        ctorMembers.ExceptWith(primaryParamSet);
        var ctorCtx = new EmitContext { ClassMembers = ctorMembers, Model = ctx.Model };

        sb.Append(Indent).Append("constructor(");
        for (var i = 0; i < parameters.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(parameters[i].Identifier.Text);
        }
        sb.Append(") {\n");

        // `: Base(x, y)` in the base list translates to super(x, y).
        var baseArgs = GetPrimaryBaseArguments(cls);
        if (baseArgs is not null)
        {
            sb.Append("    super(");
            for (var i = 0; i < baseArgs.Arguments.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                ExpressionEmitter.Emit(baseArgs.Arguments[i].Expression, sb, ctorCtx);
            }
            sb.Append(");\n");
        }
        else if (cls.BaseList is not null && cls.BaseList.Types.Count > 0)
        {
            // Base class present but no args — still call super() so the JS
            // class hierarchy doesn't trip over "Must call super constructor".
            if (GetBaseClassName(cls) is not null)
            {
                sb.Append("    super();\n");
            }
        }

        foreach (var p in parameters)
        {
            var n = p.Identifier.Text;
            sb.Append("    this.").Append(n).Append(" = ").Append(n).Append(";\n");
        }
        sb.Append(Indent).Append("}\n");
    }

    private static ArgumentListSyntax? GetPrimaryBaseArguments(ClassDeclarationSyntax cls)
    {
        if (cls.BaseList is null) return null;
        foreach (var baseType in cls.BaseList.Types)
        {
            if (baseType is PrimaryConstructorBaseTypeSyntax primaryBase)
            {
                return primaryBase.ArgumentList;
            }
        }
        return null;
    }

    private static string? GetBaseClassName(ClassDeclarationSyntax cls)
    {
        if (cls.BaseList is null) return null;
        foreach (var baseType in cls.BaseList.Types)
        {
            var raw = baseType.Type.ToString();
            var lastDot = raw.LastIndexOf('.');
            var simple = lastDot < 0 ? raw : raw[(lastDot + 1)..];
            var gen = simple.IndexOf('<');
            if (gen >= 0) simple = simple[..gen];

            // Heuristic: a leading `I` followed by an uppercase letter marks
            // an interface (`IWeatherApi`). Everything else is treated as
            // the class's base class.
            if (simple.Length >= 2 && simple[0] == 'I' && char.IsUpper(simple[1])) continue;
            return simple;
        }
        return null;
    }

    private static void EmitInjectManifest(List<(string JsName, string ServiceKey)> injects, StringBuilder sb)
    {
        if (injects.Count == 0) return;

        sb.Append(Indent).Append("static _injects = { ");
        for (var i = 0; i < injects.Count; i++)
        {
            var (jsName, serviceKey) = injects[i];
            sb.Append('\'').Append(jsName).Append("': '").Append(serviceKey).Append('\'');
            if (i < injects.Count - 1) sb.Append(", ");
        }
        sb.Append(" };\n");
    }

    private static List<(string JsName, string ServiceKey)> CollectInjects(ClassDeclarationSyntax component)
    {
        var injects = new List<(string JsName, string ServiceKey)>();
        foreach (var member in component.Members)
        {
            if (member is PropertyDeclarationSyntax prop && HasInjectAttribute(prop))
            {
                injects.Add((
                    NameConventions.ToCamelCase(prop.Identifier.Text),
                    NameConventions.StripQualifiers(prop.Type.ToString())
                ));
            }
        }
        return injects;
    }

    private static bool HasInjectAttribute(PropertyDeclarationSyntax property)
    {
        foreach (var attrList in property.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var simple = NameConventions.StripQualifiers(attr.Name.ToString());
                if (simple == "Inject" || simple == "InjectAttribute")
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Names of every field-variable, auto-property, and method directly declared on the
    /// component class. Used by <see cref="ExpressionEmitter"/> to rewrite bare
    /// identifiers into <c>this.&lt;name&gt;</c> member access.
    /// </summary>
    private static HashSet<string> CollectMemberNames(ClassDeclarationSyntax component)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in component.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    foreach (var v in field.Declaration.Variables)
                        names.Add(v.Identifier.Text);
                    break;

                case PropertyDeclarationSyntax prop:
                    names.Add(prop.Identifier.Text);
                    break;

                case MethodDeclarationSyntax method:
                    names.Add(method.Identifier.Text);
                    break;
            }
        }

        // Primary-constructor parameters are in scope inside method bodies as
        // bare identifiers. We hoist them to `this.<param>` fields in the
        // generated constructor, so the identifier-rewrite path in
        // ExpressionEmitter needs to see them here too — otherwise a method
        // that reads `http` would emit a bare `http` reference that blows up.
        if (component.ParameterList is not null)
        {
            foreach (var p in component.ParameterList.Parameters)
            {
                names.Add(p.Identifier.Text);
            }
        }

        AddInheritedMembers(component, names);

        return names;
    }

    private static void AddInheritedMembers(ClassDeclarationSyntax component, HashSet<string> names)
    {
        if (component.BaseList is null) return;

        var baseName = component.BaseList.Types[0].Type.ToString();

        // ComponentBase / LayoutComponentBase inherit StateHasChanged. Without
        // this entry a bare `StateHasChanged` identifier in user Razor code
        // would emit as a global reference and crash with "is not defined".
        // Non-Razor classes (e.g. `[Client]` ApiClient subclasses) skip this —
        // they have their own base and no StateHasChanged.
        if (baseName.EndsWith("ComponentBase", StringComparison.Ordinal)
            || baseName.EndsWith("LayoutComponentBase", StringComparison.Ordinal))
        {
            names.Add("StateHasChanged");
        }
        if (baseName.EndsWith("LayoutComponentBase", StringComparison.Ordinal))
        {
            names.Add("Body");
        }

        // ApiClient subclasses use the protected HTTP verbs; the runtime
        // exposes them as lowercase instance methods (get, post, put, delete).
        // Flagging them as inherited members lets ExpressionEmitter rewrite
        // a bare `Get<T>(url)` call to `this.get(url)` — without this the
        // reference leaks out as a global `Get` that crashes at load.
        if (baseName.EndsWith("ApiClient", StringComparison.Ordinal))
        {
            names.Add("Get");
            names.Add("Post");
            names.Add("Put");
            names.Add("Delete");
            names.Add("ConfigureRequestAsync");
            names.Add("HandleResponseAsync");
            names.Add("HttpClient");
        }
    }
}
