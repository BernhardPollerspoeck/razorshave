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

    /// <summary>
    /// True when the class carries a <c>[Client]</c> attribute — the marker
    /// that flags a regular C# class (typically an <c>ApiClient</c> subclass
    /// or service) for transpilation into the SPA bundle and auto-registration
    /// in the JS DI container. Distinct from <see cref="IsRazorComponent"/> —
    /// components use <c>[Inject]</c>/<c>@page</c> instead.
    /// </summary>
    public static bool IsClientClass(ClassDeclarationSyntax node)
    {
        foreach (var attrList in node.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var simple = StripQualifiers(attr.Name.ToString());
                if (simple is "Client" or "ClientAttribute") return true;
            }
        }
        return false;
    }

    public static string MapBaseToJs(ClassDeclarationSyntax node)
        => GetBaseName(node) switch
        {
            "LayoutComponentBase" => "LayoutComponent",
            _ => "Component",
        };

    /// <summary>
    /// Returns the first interface (I-prefixed base) the class implements,
    /// stripped of namespace qualifiers. Used as the DI key when auto-
    /// registering a <c>[Client]</c> class — matches the <c>@inject I&lt;Name&gt;</c>
    /// conventions on the Razor side.
    /// </summary>
    public static IEnumerable<string> EnumerateInterfaces(ClassDeclarationSyntax node)
    {
        if (node.BaseList is null) yield break;
        // Skip the first entry only when it's clearly a class (non-I-prefixed
        // in canonical casing). Interfaces in .NET conventionally start with I,
        // a heuristic we lean on here because the syntax tree doesn't carry
        // symbol info at parse time.
        var first = true;
        foreach (var baseType in node.BaseList.Types)
        {
            var name = StripQualifiers(baseType.Type.ToString());
            // Drop generic parameters: `IEnumerable<T>` → `IEnumerable`.
            var gen = name.IndexOf('<');
            if (gen >= 0) name = name[..gen];

            if (first && (name.Length == 0 || name[0] != 'I' || name.Length < 2 || !char.IsUpper(name[1])))
            {
                first = false;
                continue; // base class, not an interface
            }
            first = false;

            if (name.Length >= 2 && name[0] == 'I' && char.IsUpper(name[1]))
            {
                yield return name;
            }
        }
    }

    private static string StripQualifiers(string qualified)
    {
        var lastDot = qualified.LastIndexOf('.');
        return lastDot < 0 ? qualified : qualified[(lastDot + 1)..];
    }

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
