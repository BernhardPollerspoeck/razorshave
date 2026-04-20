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
}
