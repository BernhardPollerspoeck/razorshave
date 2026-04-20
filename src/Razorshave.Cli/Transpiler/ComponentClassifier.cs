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
                var simple = NameConventions.StripQualifiers(attr.Name.ToString());
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
    /// Returns the interfaces (I-prefixed bases) the class implements,
    /// stripped of namespace qualifiers and generic parameters. Used as the
    /// DI key when auto-registering a <c>[Client]</c> class — matches the
    /// <c>@inject I&lt;Name&gt;</c> conventions on the Razor side.
    /// </summary>
    /// <remarks>
    /// Heuristic: a name starting with <c>I</c> followed by an uppercase
    /// letter (`IWeatherApi`, `IStore`, `ILogger`) is treated as an
    /// interface; everything else (`ApiClient`, `ComponentBase`) is treated
    /// as a class. This works for 99%+ of .NET code but can misidentify a
    /// user class named e.g. <c>IPAddress</c>. Upgrading to SemanticModel
    /// would cost a compilation at this call site and is only worth it
    /// when we hit a real misclassification in practice.
    /// </remarks>
    public static IEnumerable<string> EnumerateInterfaces(ClassDeclarationSyntax node)
    {
        if (node.BaseList is null) yield break;
        foreach (var baseType in node.BaseList.Types)
        {
            var name = NameConventions.StripGenerics(
                NameConventions.StripQualifiers(baseType.Type.ToString()));

            if (name.Length >= 2 && name[0] == 'I' && char.IsUpper(name[1]))
            {
                yield return name;
            }
        }
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
        return NameConventions.StripQualifiers(node.BaseList.Types[0].Type.ToString());
    }
}
