namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Name transformations between C# conventions (PascalCase for public members,
/// camelCase for private fields) and JavaScript conventions (camelCase for
/// instance members).
/// </summary>
internal static class NameConventions
{
    /// <summary>
    /// Lower-cases the first character when it is upper-case; otherwise returns
    /// the input unchanged. <c>IncrementCount</c> → <c>incrementCount</c>,
    /// <c>currentCount</c> → <c>currentCount</c>.
    /// </summary>
    public static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0]))
        {
            return name;
        }
        return string.Create(name.Length, name, (span, src) =>
        {
            span[0] = char.ToLowerInvariant(src[0]);
            src.AsSpan(1).CopyTo(span[1..]);
        });
    }

    /// <summary>
    /// <c>global::Microsoft.AspNetCore.Components.Web.PageTitle</c> → <c>PageTitle</c>.
    /// Strips any namespace qualifiers and <c>global::</c> alias prefix.
    /// </summary>
    public static string StripQualifiers(string qualifiedName)
    {
        var lastDot = qualifiedName.LastIndexOf('.');
        return lastDot < 0 ? qualifiedName : qualifiedName[(lastDot + 1)..];
    }
}
