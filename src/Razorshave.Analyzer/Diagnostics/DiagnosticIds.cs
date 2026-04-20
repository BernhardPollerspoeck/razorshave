namespace Razorshave.Analyzer.Diagnostics;

/// <summary>
/// Central registry of Razorshave diagnostic IDs. All analyzer-emitted diagnostics
/// use the <c>RZS</c> prefix; the numeric ranges are reserved per category.
/// </summary>
internal static class DiagnosticIds
{
    // 1xxx — ecosystem / allowlist violations
    public const string UnsupportedSymbol = "RZS1001";

    // 2xxx — unsupported language features
    //
    // These fire BEFORE the transpiler would emit `/* TODO: <Kind> */ null`
    // at runtime — the point is to make the silent fallback impossible by
    // turning it into a visible compile-time diagnostic.
    public const string UnsupportedExpression = "RZS2001";
    public const string UnsupportedStatement = "RZS2002";

    // 3xxx — component / render-tree issues (reserved)
}
