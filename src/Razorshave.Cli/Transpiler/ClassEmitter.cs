using System.Text;

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

    public static void Emit(ClassDeclarationSyntax component, StringBuilder sb)
    {
        var name = component.Identifier.Text;
        var jsBase = ComponentClassifier.MapBaseToJs(component);

        sb.Append("export class ").Append(name).Append(" extends ").Append(jsBase).Append(" {\n");

        foreach (var member in component.Members)
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                    FieldEmitter.Emit(field, sb);
                    break;

                // Methods, properties, nested classes, constructors are handled
                // by later walker stages (5.5+). Silently ignored here so the
                // skeleton builds incrementally.
                default:
                    break;
            }
        }

        sb.Append("}\n");
    }
}
