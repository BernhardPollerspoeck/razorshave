using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Razorshave.Analyzer;

/// <summary>
/// Skeleton analyzer to prove the project builds and registers with Roslyn.
/// Real diagnostics (RZS1001+) will be declared here as the allowlist rules are
/// implemented. When the first rule is added, re-enable release tracking
/// (<c>AnalyzerReleases.Shipped.md</c> / <c>AnalyzerReleases.Unshipped.md</c>).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PlaceholderAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray<DiagnosticDescriptor>.Empty;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
    }
}
