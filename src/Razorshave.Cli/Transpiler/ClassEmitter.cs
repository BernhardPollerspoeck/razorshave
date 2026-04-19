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
        var ctx = new EmitContext
        {
            ClassMembers = CollectMemberNames(component),
            Model = model,
        };

        sb.Append("export class ").Append(name).Append(" extends ").Append(jsBase).Append(" {\n");

        foreach (var member in component.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    FieldEmitter.Emit(field, sb);
                    break;

                case MethodDeclarationSyntax method when method.Identifier.Text == "BuildRenderTree":
                    RenderTreeEmitter.Emit(method, sb, ctx);
                    break;

                case MethodDeclarationSyntax method:
                    MethodEmitter.Emit(method, sb, ctx);
                    break;

                // Properties, nested classes, constructors are handled by later
                // walker stages. Silently ignored here so the skeleton builds
                // incrementally.
                default:
                    break;
            }
        }

        sb.Append("}\n");
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

        // Syntax-only resolution can't see inherited members. Hard-code the few
        // user code actually references. Swap this for SemanticModel lookup
        // once the base-class symbol is available.
        AddInheritedMembers(component, names);

        return names;
    }

    private static void AddInheritedMembers(ClassDeclarationSyntax component, HashSet<string> names)
    {
        if (component.BaseList is null) return;

        var baseName = component.BaseList.Types[0].Type.ToString();
        if (baseName.EndsWith("LayoutComponentBase", StringComparison.Ordinal))
        {
            names.Add("Body"); // RenderFragment? inherited from LayoutComponentBase
        }
    }
}
