namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Name transformations between C# conventions (PascalCase for public members,
/// camelCase for private fields) and JavaScript conventions (camelCase for
/// instance members).
/// </summary>
internal static class NameConventions
{
    /// <summary>Razor-SourceGenerator-Methode die wir als <c>render()</c> emittieren.</summary>
    public const string RazorBuildRenderTreeMethod = "BuildRenderTree";

    /// <summary>4-space indent für Method-Bodies im emittierten JS.</summary>
    public const string MethodBodyIndent = "    ";

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
    /// Generic type arguments are preserved — <c>IStore&lt;Todo&gt;</c> stays
    /// <c>IStore&lt;Todo&gt;</c> because callers use it as a DI-service key.
    /// Use <see cref="StripGenerics"/> on top when the bare class name is needed.
    /// </summary>
    public static string StripQualifiers(string qualifiedName)
    {
        var lastDot = qualifiedName.LastIndexOf('.');
        return lastDot < 0 ? qualifiedName : qualifiedName[(lastDot + 1)..];
    }

    /// <summary>
    /// <c>List&lt;Todo&gt;</c> → <c>List</c>. Removes any generic type-argument
    /// suffix while keeping the rest of the name intact. Often paired with
    /// <see cref="StripQualifiers"/> to turn a fully-qualified generic type
    /// into its simple class name.
    /// </summary>
    public static string StripGenerics(string name)
    {
        var gen = name.IndexOf('<');
        return gen < 0 ? name : name[..gen];
    }
}
