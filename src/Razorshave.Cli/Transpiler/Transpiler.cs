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
    public static string Transpile(string source, IReadOnlyList<MetadataReference>? additionalReferences = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tree = CSharpSyntaxTree.ParseText(source);
        var references = additionalReferences is { Count: > 0 }
            ? MetadataReferenceLoader.SharedFramework().Concat(additionalReferences).ToArray()
            : MetadataReferenceLoader.SharedFramework();
        var compilation = CSharpCompilation.Create(
            assemblyName: "Razorshave.Transpile",
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);

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
    /// Transpile a non-Razor, <c>[Client]</c>-marked class (a service or
    /// ApiClient subclass). Like <see cref="Transpile"/> but selects the
    /// class by attribute rather than base type, and emits the plain class
    /// form (no <c>_injects</c> manifest, no <c>extends Component</c>).
    /// Returns an empty string when no matching class is present.
    /// </summary>
    public static string TranspileClientClass(string source, IReadOnlyList<MetadataReference>? additionalReferences = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tree = CSharpSyntaxTree.ParseText(source);
        var references = additionalReferences is { Count: > 0 }
            ? MetadataReferenceLoader.SharedFramework().Concat(additionalReferences).ToArray()
            : MetadataReferenceLoader.SharedFramework();
        var compilation = CSharpCompilation.Create(
            assemblyName: "Razorshave.Transpile",
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);

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
}
