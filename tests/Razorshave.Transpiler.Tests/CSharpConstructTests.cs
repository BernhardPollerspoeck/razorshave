using static Razorshave.Cli.Transpiler.Transpiler;

namespace Razorshave.Transpiler.Tests;

/// <summary>
/// Feature-matrix for C# language constructs the transpiler promises. Every
/// row in this file is either a construct the analyzer's allowlist approves
/// (so users don't hit a silent RZS2001) or a construct with a documented
/// approximation (typeof/default/nameof). Adding a case here means the
/// emitter has a home for it; removing or breaking the contract must be a
/// deliberate change with the test updated in the same commit.
/// </summary>
public sealed class CSharpConstructTests
{
    private const string RazorGenHeader = """
        using global::System;
        using global::Microsoft.AspNetCore.Components;
        using global::Microsoft.AspNetCore.Components.Rendering;
        namespace Fixtures;
        """;

    private static string TranspileBody(string bodyCsharp)
    {
        var source = $$"""
            {{RazorGenHeader}}
            public partial class Cx : ComponentBase
            {
                {{bodyCsharp}}
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;
        return Transpile(source);
    }

    // --- ?? / ??= ---
    [Fact]
    public void Coalesce_preserves_operator()
    {
        // JS `??` matches C# semantics closely enough: both return the right
        // operand when the left is null. JS also treats undefined as "missing";
        // that's a superset, not a divergence for typical Blazor code.
        var js = TranspileBody("private string Run(string? s) => s ?? \"x\";");
        Assert.Contains("s ?? \"x\"", js);
    }

    [Fact]
    public void CoalesceAssignment_preserves_operator()
    {
        var js = TranspileBody("private void Run() { label ??= \"default\"; } private string? label;");
        Assert.Contains("this.label ??= \"default\"", js);
    }

    // --- ?. (null-conditional) ---
    [Fact]
    public void ConditionalAccess_single_hop()
    {
        var js = TranspileBody("private int? Run(string? s) => s?.Length;");
        Assert.Contains("s?.length", js);
    }

    [Fact]
    public void ConditionalAccess_chain()
    {
        var js = TranspileBody("private string? Run(object? o) => o?.ToString()?.ToLower();");
        // `.ToString()` is a no-op (dropped by StaticMemberRewrites), so the
        // chain shortens to `o?.toLower()` in practice. The test just asserts
        // that the `?.` survives the chain without becoming `.`.
        Assert.Contains("?.", js);
    }

    [Fact]
    public void ConditionalElementAccess()
    {
        var js = TranspileBody("private int? Run(int[]? xs) => xs?[0];");
        Assert.Contains("xs?[0]", js);
    }

    // --- nameof ---
    [Fact]
    public void NameOf_emits_simple_name_string()
    {
        var js = TranspileBody("private string Run(int count) => nameof(count);");
        Assert.Contains("\"count\"", js);
    }

    [Fact]
    public void NameOf_strips_qualifiers_to_last_segment()
    {
        // `nameof(System.DateTime.Now)` should produce "Now" — Roslyn does
        // the same; we mirror the behaviour with a simple suffix strip.
        var js = TranspileBody("private string Run() => nameof(System.DateTime.Now);");
        Assert.Contains("\"Now\"", js);
    }

    // --- typeof(T) ---
    [Fact]
    public void TypeOf_emits_type_name_as_string()
    {
        // Best-effort fallback — JS has no Type. Emit as the stripped type
        // name in quotes so equality comparisons between typeof()-results
        // still work. Matches what Blazor's [Route] conventions expect.
        var js = TranspileBody("private string Run() => typeof(string).ToString();");
        Assert.Contains("\"string\"", js);
    }

    // --- default(T) / default ---
    [Fact]
    public void Default_of_T_emits_null()
    {
        var js = TranspileBody("private string? Run() => default(string);");
        Assert.Contains("return null", js);
    }

    [Fact]
    public void BareDefault_literal_emits_null()
    {
        // `default` (no type) — emitted as LiteralExpressionSyntax of kind
        // `DefaultLiteralExpression`. Without the null-rewrite the raw
        // token text would emit as a bare `default` and fail JS parsing.
        var js = TranspileBody("private string? Run() => default;");
        Assert.Contains("return null", js);
    }

    // --- Collection-spread ---
    [Fact]
    public void Collection_spread_maps_to_js_spread()
    {
        // C# `..a` (single dot spread) becomes JS `...a` (triple dot).
        // Verified against two source arrays plus a literal element in the
        // middle so the emitter's element-sequencing is also covered.
        var js = TranspileBody("private int[] Run(int[] a, int[] b) => [..a, 42, ..b];");
        Assert.Contains("[...a, 42, ...b]", js);
    }

    // --- Range / Index ---
    [Fact]
    public void Range_index_maps_to_slice()
    {
        // `arr[1..3]` → `arr.slice(1, 3)`. Bare right-hand side.
        var js = TranspileBody("private int[] Run(int[] arr) => arr[1..3];");
        Assert.Contains("arr.slice(1, 3)", js);
    }

    [Fact]
    public void Range_with_implicit_start_defaults_to_zero()
    {
        // `arr[..n]` — omitted left operand. `.slice` needs a concrete
        // start; we emit 0 so the call stays well-formed.
        var js = TranspileBody("private int[] Run(int[] arr, int n) => arr[..n];");
        Assert.Contains("arr.slice(0, n)", js);
    }

    [Fact]
    public void Range_with_from_end_right_bound_subtracts_from_length()
    {
        // `arr[..^2]` — right operand is `^2` (from-end). JS `.slice` has no
        // from-end notation, so we emit `arr.length - 2` explicitly. The
        // receiver is re-emitted into the length expression — repeated
        // evaluation is fine here because it's a bare identifier.
        var js = TranspileBody("private int[] Run(int[] arr) => arr[..^2];");
        Assert.Contains("arr.slice(0, arr.length - 2)", js);
    }

    [Fact]
    public void FromEnd_index_maps_to_at_negative()
    {
        // `arr[^1]` → `arr.at(-1)`. `.at()` is the JS-native way to fetch
        // from-end without bounds math; behaves identically to C# for
        // non-empty sources.
        var js = TranspileBody("private int Run(int[] arr) => arr[^1];");
        Assert.Contains("arr.at(-1)", js);
    }

    // --- List.Remove ---
    [Fact]
    public void ListRemove_routes_through_bcl_helper()
    {
        // `xs.Remove(x)` → `_listRemove(xs, x)` so the arguments evaluate
        // exactly once even when they carry side-effects. Returns bool
        // (.NET contract) — the helper does indexOf + splice.
        var js = TranspileBody("private bool Run(System.Collections.Generic.List<int> xs, int x) => xs.Remove(x);");
        Assert.Contains("_listRemove(xs, x)", js);
    }
}
