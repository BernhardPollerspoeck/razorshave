using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Entry point for transpiling a single Razor-generated C# source file into the
/// equivalent JavaScript module.
/// </summary>
/// <remarks>
/// The entry point parses the source, builds a minimal compilation so that
/// <see cref="SemanticModel"/>-aware emitters (event-handler detection,
/// allowlist checks) can resolve symbols against the .NET shared framework,
/// and dispatches to the individual emitters. All formatting decisions live in
/// the emitters themselves.
/// </remarks>
public static class Transpiler
{
    /// <summary>
    /// Transpile a single Razor-generated C# source string to its JavaScript
    /// equivalent. Returns an empty string when the source contains no
    /// recognised component class.
    /// </summary>
    public static string Transpile(string source, IReadOnlyList<MetadataReference>? references = null, string? globalUsings = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var (tree, model) = BuildCompilation(source, references, globalUsings);

        var component = tree.GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(ComponentClassifier.IsRazorComponent);

        if (component is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        HeaderEmitter.Emit(sb, component);
        ClassEmitter.Emit(component, model, sb);
        return sb.ToString();
    }

    /// <summary>
    /// Transpile a specific component class from an already-parsed tree.
    /// Used when a single Razor file produces more than one component class
    /// (rare — Razor's source generator typically emits one class per
    /// <c>.razor</c>, but code-behind files can declare additional partial
    /// or sibling components). Caller owns the tree; the class node must
    /// belong to it or the SemanticModel lookup throws.
    /// </summary>
    public static string Transpile(SyntaxTree tree, ClassDeclarationSyntax component, IReadOnlyList<MetadataReference>? references = null, string? globalUsings = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(component);

        var model = BuildCompilationFromTree(tree, references, globalUsings);

        var sb = new StringBuilder();
        HeaderEmitter.Emit(sb, component);
        ClassEmitter.Emit(component, model, sb);
        return sb.ToString();
    }

    /// <summary>
    /// Transpile a non-Razor, <c>[Client]</c>-marked class (a service or
    /// ApiClient subclass). Like <c>Transpile</c> but selects the
    /// class by attribute rather than base type, and emits the plain class
    /// form (no <c>_injects</c> manifest, no <c>extends Component</c>).
    /// Returns an empty string when no matching class is present.
    /// </summary>
    public static string TranspileClientClass(string source, IReadOnlyList<MetadataReference>? references = null, string? globalUsings = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var (tree, model) = BuildCompilation(source, references, globalUsings);

        var cls = tree.GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(ComponentClassifier.IsClientClass);

        if (cls is null) return string.Empty;

        var sb = new StringBuilder();
        HeaderEmitter.Emit(sb, cls);
        ClassEmitter.EmitPlain(cls, model, sb);
        return sb.ToString();
    }

    /// <summary>
    /// Same as <see cref="TranspileClientClass(string, IReadOnlyList{MetadataReference}?, string?)"/>
    /// but accepts an already-parsed <see cref="SyntaxTree"/> so callers that
    /// have already scanned the file for classification don't pay the parse
    /// cost twice. Pairs with <see cref="BuildCompilationFromTree"/>.
    /// </summary>
    public static string TranspileClientClass(SyntaxTree tree, IReadOnlyList<MetadataReference>? references = null, string? globalUsings = null)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var cls = tree.GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(ComponentClassifier.IsClientClass);
        if (cls is null) return string.Empty;

        return TranspileClientClass(tree, cls, references, globalUsings);
    }

    /// <summary>
    /// Transpile a specific <c>[Client]</c> class from an already-parsed tree.
    /// Used when a single file contains more than one <c>[Client]</c> class
    /// and each needs its own output module — BuildCommand iterates over the
    /// matching classes and calls this overload per class so every service
    /// lands in <c>dist/&lt;Name&gt;.js</c> instead of only the first.
    /// </summary>
    public static string TranspileClientClass(SyntaxTree tree, ClassDeclarationSyntax cls, IReadOnlyList<MetadataReference>? references = null, string? globalUsings = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(cls);

        var model = BuildCompilationFromTree(tree, references, globalUsings);

        var sb = new StringBuilder();
        HeaderEmitter.Emit(sb, cls);
        ClassEmitter.EmitPlain(cls, model, sb);
        return sb.ToString();
    }

    private static SemanticModel BuildCompilationFromTree(SyntaxTree tree, IReadOnlyList<MetadataReference>? references, string? globalUsings)
    {
        var trees = new List<SyntaxTree> { tree };
        if (!string.IsNullOrEmpty(globalUsings))
        {
            trees.Add(CSharpSyntaxTree.ParseText(globalUsings));
        }
        var refs = references is { Count: > 0 }
            ? references
            : MetadataReferenceLoader.SharedFramework();
        var compilation = CSharpCompilation.Create(
            assemblyName: "Razorshave.Transpile",
            syntaxTrees: trees,
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation.GetSemanticModel(tree);
    }

    // Shared compilation-building path for both entry points. When the caller
    // passes an explicit `references` list it's used as-is (typical for
    // BuildCommand which pre-builds the list once per build). When omitted
    // — e.g. from unit tests — we fall back to the cached shared-framework
    // references so direct-from-source transpile calls stay fast.
    //
    // `globalUsings` is the text of the project's `<Name>.GlobalUsings.g.cs`
    // file — the implicit `global using` declarations MSBuild generates for
    // Web SDK projects. Without them, unqualified types like `HttpClient`
    // fail to resolve in the source file and SemanticModel returns null
    // for every expression that depends on them (silently breaking every
    // attribute-aware rewrite downstream).
    private static (SyntaxTree Tree, SemanticModel Model) BuildCompilation(string source, IReadOnlyList<MetadataReference>? references, string? globalUsings)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var trees = new List<SyntaxTree> { tree };
        if (!string.IsNullOrEmpty(globalUsings))
        {
            trees.Add(CSharpSyntaxTree.ParseText(globalUsings));
        }
        var refs = references is { Count: > 0 }
            ? references
            : MetadataReferenceLoader.SharedFramework();
        var compilation = CSharpCompilation.Create(
            assemblyName: "Razorshave.Transpile",
            syntaxTrees: trees,
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return (tree, compilation.GetSemanticModel(tree));
    }
}
