using static Razorshave.Cli.Transpiler.Transpiler;

namespace Razorshave.Transpiler.Tests;

/// <summary>
/// Regression suite for the equality/pattern emitter: C# <c>==</c> promotes
/// to JS <c>===</c> (avoid <c>"5" == 5</c> coercion) except against
/// <c>null</c>, where loose <c>==</c> is correct (C# has no <c>undefined</c>,
/// so "kein Wert" should catch JS-side undefined too). <c>is null</c>
/// / <c>is not null</c> patterns map to the same loose null-check.
/// </summary>
public sealed class EqualityAndPatternTests
{
    private const string RazorGenHeader = """
        using global::Microsoft.AspNetCore.Components;
        using global::Microsoft.AspNetCore.Components.Rendering;
        namespace Fixtures;
        """;

    [Fact]
    public void NonNull_equality_emits_strict()
    {
        var source = $$"""
            {{RazorGenHeader}}
            public partial class Eq : ComponentBase
            {
                private int count = 0;
                private bool Use() => count == 5;
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;
        var js = Transpile(source);
        Assert.Contains("this.count === 5", js);
        Assert.DoesNotContain("this.count == 5", js);
    }

    [Fact]
    public void NonNull_inequality_emits_strict()
    {
        var source = $$"""
            {{RazorGenHeader}}
            public partial class Eq : ComponentBase
            {
                private int count = 0;
                private bool Use() => count != 5;
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;
        var js = Transpile(source);
        Assert.Contains("this.count !== 5", js);
    }

    [Fact]
    public void Equality_against_null_stays_loose()
    {
        var source = $$"""
            {{RazorGenHeader}}
            public partial class Eq : ComponentBase
            {
                private string? label = null;
                private bool Use() => label == null;
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;
        var js = Transpile(source);
        Assert.Contains("this.label == null", js);
        Assert.DoesNotContain("=== null", js);
    }

    [Fact]
    public void Is_null_pattern_maps_to_loose_null_check()
    {
        var source = $$"""
            {{RazorGenHeader}}
            public partial class Eq : ComponentBase
            {
                private string? label = null;
                private bool Use() => label is null;
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;
        var js = Transpile(source);
        Assert.Contains("this.label == null", js);
    }

    [Fact]
    public void Is_not_null_pattern_maps_to_loose_not_null_check()
    {
        var source = $$"""
            {{RazorGenHeader}}
            public partial class Eq : ComponentBase
            {
                private string? label = null;
                private bool Use() => label is not null;
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;
        var js = Transpile(source);
        Assert.Contains("this.label != null", js);
    }

    [Fact]
    public void Multiple_Client_classes_in_one_file_each_transpile_individually()
    {
        // Regression: BuildCommand used FirstOrDefault, so declaring two
        // [Client] services in one file silently dropped the second. The new
        // loop hits every class; we verify by transpiling each explicitly
        // here (BuildCommand's loop walks the file-level; the per-class
        // transpile is what it ultimately calls).
        var source = """
            using global::Microsoft.AspNetCore.Components;
            namespace Fixtures;
            [Client] public class ServiceA { public void Ping() { } }
            [Client] public class ServiceB { public void Pong() { } }
            """;
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        var classes = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>()
            .ToList();
        Assert.Equal(2, classes.Count);

        var jsA = TranspileClientClass(tree, classes[0]);
        var jsB = TranspileClientClass(tree, classes[1]);

        // Each transpile should emit exactly its own class, not the sibling.
        Assert.Contains("class ServiceA", jsA);
        Assert.DoesNotContain("class ServiceB", jsA);

        Assert.Contains("class ServiceB", jsB);
        Assert.DoesNotContain("class ServiceA", jsB);
    }
}
