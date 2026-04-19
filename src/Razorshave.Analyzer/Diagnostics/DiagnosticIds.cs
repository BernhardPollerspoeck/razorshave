namespace Razorshave.Analyzer.Diagnostics;

/// <summary>
/// Central registry of Razorshave diagnostic IDs. All analyzer-emitted diagnostics
/// use the <c>RZS</c> prefix; the numeric ranges are reserved per category.
/// </summary>
internal static class DiagnosticIds
{
    // 1xxx — ecosystem / allowlist violations
    public const string UnsupportedSymbol = "RZS1001";

    // 2xxx — unsupported language features (reserved)

    // 3xxx — component / render-tree issues (reserved)
}
