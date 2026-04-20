using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Razorshave.Analyzer.Tests;

/// <summary>
/// Minimal harness for running our analyzer against a snippet of C# source
/// and inspecting the diagnostics it emits. Heavier than hand-rolled but
/// lighter than the full Microsoft.CodeAnalysis.Testing stack — good
/// enough for asserting "flagged / not flagged" behaviour.
/// </summary>
internal static class AnalyzerRunner
{
    // Shared-framework references so SemanticModel can resolve `ComponentBase`
    // and friends. Cached because loading 300+ DLLs per test is wasteful.
    private static ImmutableArray<MetadataReference>? _references;

    private static ImmutableArray<MetadataReference> References()
    {
        if (_references.HasValue) return _references.Value;
        var refs = ImmutableArray.CreateBuilder<MetadataReference>();
        var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
        {
            try { refs.Add(MetadataReference.CreateFromFile(dll)); }
            catch { /* locked or unreadable, skip */ }
        }
        // AspNetCore.App provides ComponentBase — pick matching major version.
        var sharedRoot = Path.GetDirectoryName(Path.GetDirectoryName(runtimeDir));
        if (sharedRoot is not null)
        {
            var aspnetRoot = Path.Combine(sharedRoot, "Microsoft.AspNetCore.App");
            if (Directory.Exists(aspnetRoot))
            {
                var dir = Directory.GetDirectories(aspnetRoot).OrderBy(d => d).LastOrDefault();
                if (dir is not null)
                {
                    foreach (var dll in Directory.GetFiles(dir, "*.dll"))
                    {
                        try { refs.Add(MetadataReference.CreateFromFile(dll)); }
                        catch { /* skip */ }
                    }
                }
            }
        }
        _references = refs.ToImmutable();
        return _references.Value;
    }

    public static ImmutableArray<Diagnostic> Run(DiagnosticAnalyzer analyzer, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAsm",
            [tree],
            References(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        var diags = withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
        return diags;
    }
}
