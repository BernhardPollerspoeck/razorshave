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

    // 3xxx — component / render-tree issues
    //
    // RZS3001: user-declared component name shadows a runtime-provided one
    // (NavLink, Router, PageTitle). The HeaderEmitter filters known runtime
    // components from user-import lines, so without this diagnostic the
    // user's custom <NavLink> would silently resolve to the runtime NavLink
    // instead of their own class.
    public const string RuntimeComponentShadow = "RZS3001";
}
