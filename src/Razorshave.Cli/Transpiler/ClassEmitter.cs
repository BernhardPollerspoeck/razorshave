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

        // Surface [Inject] properties as a static manifest the runtime reads
        // when constructing the component. Non-inject members keep their
        // normal emission path.
        EmitInjectManifest(injects, sb);

        foreach (var member in component.Members)
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

        sb.Append("}\n");
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

        AddInheritedMembers(component, names);

        return names;
    }

    private static void AddInheritedMembers(ClassDeclarationSyntax component, HashSet<string> names)
    {
        // Every Razor ComponentBase/LayoutComponentBase inherits StateHasChanged.
        // Without this entry, a bare `StateHasChanged` identifier in user code
        // would emit as a global reference and crash with "is not defined".
        names.Add("StateHasChanged");

        if (component.BaseList is null) return;

        var baseName = component.BaseList.Types[0].Type.ToString();
        if (baseName.EndsWith("LayoutComponentBase", StringComparison.Ordinal))
        {
            names.Add("Body");
        }
    }
}
