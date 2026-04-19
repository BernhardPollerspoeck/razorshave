using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Entry point for transpiling a single Razor-generated C# source file into the
/// equivalent JavaScript module.
/// </summary>
/// <remarks>
/// This is the pre-M0 first-walker stage (RAZORSHAVE-BOOTSTRAP.md step 5.3):
/// it recognises the component's class declaration and emits an empty JS class
/// skeleton. Fields, methods and render-tree are wired up in later steps.
/// </remarks>
public static class Transpiler
{
    /// <summary>
    /// Transpile a single Razor-generated C# source string to its JavaScript
    /// equivalent. Returns an empty string when the source does not contain
    /// a Razor component class.
    /// </summary>
    public static string Transpile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var component = root
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(IsRazorComponent);

        if (component is null)
        {
            return string.Empty;
        }

        var name = component.Identifier.Text;
        var jsBase = MapBaseClass(component);

        // Keep the body on separate lines so later walkers can drop members in
        // without reshaping the skeleton.
        return $"export class {name} extends {jsBase} {{\n}}\n";
    }

    private static bool IsRazorComponent(ClassDeclarationSyntax node)
        => TryGetBaseTypeName(node) is { } baseName
           && (baseName == "ComponentBase" || baseName == "LayoutComponentBase");

    private static string MapBaseClass(ClassDeclarationSyntax node)
        => TryGetBaseTypeName(node) switch
        {
            "LayoutComponentBase" => "LayoutComponent",
            "ComponentBase" or _ => "Component",
        };

    /// <summary>
    /// Returns the simple name of the first base type in <paramref name="node"/>'s
    /// base list, stripping any <c>global::</c> prefix and namespace qualifiers.
    /// </summary>
    private static string? TryGetBaseTypeName(ClassDeclarationSyntax node)
    {
        if (node.BaseList is null || node.BaseList.Types.Count == 0)
        {
            return null;
        }

        var first = node.BaseList.Types[0].Type.ToString();
        // `global::Microsoft.AspNetCore.Components.ComponentBase` → `ComponentBase`
        var lastDot = first.LastIndexOf('.');
        return lastDot < 0 ? first : first[(lastDot + 1)..];
    }
}
