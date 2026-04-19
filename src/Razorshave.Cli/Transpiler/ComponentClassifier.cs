using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Tests whether a given <see cref="ClassDeclarationSyntax"/> is a Razor
/// component we want to transpile and maps its C# base class to the matching
/// runtime JS class name.
/// </summary>
internal static class ComponentClassifier
{
    public static bool IsRazorComponent(ClassDeclarationSyntax node)
        => GetBaseName(node) is "ComponentBase" or "LayoutComponentBase";

    public static string MapBaseToJs(ClassDeclarationSyntax node)
        => GetBaseName(node) switch
        {
            "LayoutComponentBase" => "LayoutComponent",
            _ => "Component",
        };

    /// <summary>
    /// Returns the simple name of the first base type on <paramref name="node"/>,
    /// stripping any <c>global::</c> prefix and namespace qualifiers, or
    /// <c>null</c> if the class has no base list.
    /// </summary>
    private static string? GetBaseName(ClassDeclarationSyntax node)
    {
        if (node.BaseList is null || node.BaseList.Types.Count == 0)
        {
            return null;
        }

        var first = node.BaseList.Types[0].Type.ToString();
        var lastDot = first.LastIndexOf('.');
        return lastDot < 0 ? first : first[(lastDot + 1)..];
    }
}
