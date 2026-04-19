using System.Text;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Entry point for transpiling a single Razor-generated C# source file into the
/// equivalent JavaScript module.
/// </summary>
/// <remarks>
/// The entry point only parses the source and dispatches to the emitters — all
/// formatting decisions (indentation, brace style, member ordering) live in the
/// individual emitters (<see cref="ClassEmitter"/>, <see cref="FieldEmitter"/>, …).
/// </remarks>
public static class Transpiler
{
    /// <summary>
    /// Transpile a single Razor-generated C# source string to its JavaScript
    /// equivalent. Returns an empty string when the source contains no
    /// recognised component class.
    /// </summary>
    public static string Transpile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tree = CSharpSyntaxTree.ParseText(source);
        var component = tree.GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(ComponentClassifier.IsRazorComponent);

        if (component is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        ClassEmitter.Emit(component, sb);
        return sb.ToString();
    }
}
