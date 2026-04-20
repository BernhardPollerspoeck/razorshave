using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

using Razorshave.Analyzer.Diagnostics;

namespace Razorshave.Analyzer;

/// <summary>
/// Fires <c>RZS3001</c> when the user declares a Razor component class whose
/// simple name matches one of the runtime-provided components (NavLink,
/// Router, PageTitle). The transpiler's <c>HeaderEmitter</c> filters these
/// names out of the per-file user-component import list — the runtime
/// version wins. That means a user's custom <c>&lt;NavLink&gt;</c> silently
/// resolves to the runtime one: their render(), state, and event handlers
/// are never reached. The diagnostic turns that silent shadow into a build
/// error so the user gets one choice, not two.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RuntimeComponentShadowAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.RuntimeComponentShadow,
        title: "Component name shadows a Razorshave runtime component",
        messageFormat: "'{0}' is also the name of a runtime-provided component — the transpiler will bind uses to the runtime version, not this class. Rename this component to avoid the silent shadow.",
        category: "Razorshave.Transpiler",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Known runtime components: NavLink, Router, PageTitle. User code that declares a class with one of these names still compiles, but the HeaderEmitter removes it from user-component imports, so the transpiled SPA ends up importing the runtime symbol instead.");

    // Keep in sync with HeaderEmitter.RuntimeComponents. When a new runtime
    // component ships, add its name both there and here.
    private static readonly HashSet<string> RuntimeComponentNames = new(StringComparer.Ordinal)
    {
        "NavLink", "Router", "PageTitle",
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeClass, SymbolKind.NamedType);
    }

    private static void AnalyzeClass(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol type) return;
        if (!RuntimeComponentNames.Contains(type.Name)) return;
        if (!InheritsComponentBase(type)) return;

        foreach (var syntaxRef in type.DeclaringSyntaxReferences)
        {
            var node = syntaxRef.GetSyntax(context.CancellationToken);
            if (node is not ClassDeclarationSyntax classNode) continue;
            context.ReportDiagnostic(Diagnostic.Create(
                Rule, classNode.Identifier.GetLocation(), type.Name));
        }
    }

    private static bool InheritsComponentBase(INamedTypeSymbol type)
    {
        for (var baseSym = type.BaseType; baseSym is not null; baseSym = baseSym.BaseType)
        {
            if (baseSym.Name is "ComponentBase" or "LayoutComponentBase") return true;
        }
        return false;
    }
}
