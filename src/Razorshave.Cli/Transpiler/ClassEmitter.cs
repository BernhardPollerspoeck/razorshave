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
            ClassMembers = CollectMemberNames(component, model),
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
            ClassMembers = CollectMemberNames(cls, model),
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

                case MethodDeclarationSyntax method when method.Identifier.Text == NameConventions.RazorBuildRenderTreeMethod:
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
        // `this.x = x` assignments below. Push them onto the local-scope
        // stack so `super(http)` emits as bare `http`, not `this.http`
        // (which would trigger TDZ because `this` isn't available before super).
        var primaryParamNames = parameters.Select(p => p.Identifier.Text).ToArray();
        ctx.PushLocalScope(primaryParamNames);
        try
        {
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
                ExpressionEmitter.Emit(baseArgs.Arguments[i].Expression, sb, ctx);
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
        finally { ctx.PopLocalScope(); }
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
            var simple = NameConventions.StripGenerics(
                NameConventions.StripQualifiers(baseType.Type.ToString()));

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
    private static HashSet<string> CollectMemberNames(ClassDeclarationSyntax component, SemanticModel model)
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

        AddInheritedMembers(component, names, model);

        return names;
    }

    // Walk the base-class chain via SemanticModel and collect every public /
    // protected member name. User code references inherited members as bare
    // identifiers (e.g. `Get<T>(url)` from ApiClient, `StateHasChanged()`
    // from ComponentBase); without knowing they're inherited, the emitter
    // would leave them as global references that crash at load.
    //
    // Using SemanticModel reflection means `ApiClient.cs` stays the single
    // source of truth — add a new `Patch` method there and the transpiler
    // automatically rewrites `Patch` to `this.patch`, no sync required.
    private static void AddInheritedMembers(ClassDeclarationSyntax component, HashSet<string> names, SemanticModel model)
    {
        if (model.GetDeclaredSymbol(component) is not INamedTypeSymbol classSymbol)
        {
            // SemanticModel couldn't resolve the class — references probably
            // missing. Log so the degradation is visible; this is a silent-
            // fail-style gap otherwise (inherited members not recognised
            // → wrong JS emitted).
            Console.Error.WriteLine($"razorshave: SemanticModel could not resolve {component.Identifier.Text}; inherited-member rewrites will miss.");
            return;
        }

        for (var baseSym = classSymbol.BaseType;
             baseSym is not null && baseSym.SpecialType != SpecialType.System_Object;
             baseSym = baseSym.BaseType)
        {
            foreach (var member in baseSym.GetMembers())
            {
                // Only kinds the user would reference as bare identifiers in
                // method bodies. Constructors, destructors, operators, and
                // the implicit backing-field of an auto-property aren't
                // callable as `ThisName()` in user code.
                if (member.IsImplicitlyDeclared) continue;
                if (member is not (IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol or IFieldSymbol or IEventSymbol))
                    continue;
                if (member.DeclaredAccessibility is not (Accessibility.Public
                        or Accessibility.Protected or Accessibility.ProtectedOrInternal))
                    continue;
                names.Add(member.Name);
            }
        }
    }
}
