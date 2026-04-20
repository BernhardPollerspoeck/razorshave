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
}
