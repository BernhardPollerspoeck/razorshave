using static Razorshave.Cli.Transpiler.Transpiler;

namespace Razorshave.Transpiler.Tests;

/// <summary>
/// Covers the try / catch / finally / throw emitters in
/// <c>StatementEmitter</c>. JavaScript only allows one catch clause per
/// try, so multiple C# catches collapse into a single
/// <c>catch (__e) { if-chain }</c>; these tests pin down the chain shape
/// so a regression doesn't silently swallow exceptions.
/// </summary>
public sealed class TryCatchTests
{
    private const string RazorGenHeader = """
        using System;
        using global::Microsoft.AspNetCore.Components;
        using global::Microsoft.AspNetCore.Components.Rendering;
        namespace Fixtures;
        """;

    [Fact]
    public void Try_catch_finally_emits_native_js_blocks()
    {
        var source = $$"""
            {{RazorGenHeader}}
            public partial class TryC : ComponentBase
            {
                private string log = "";
                private void Use()
                {
                    try { log = "in"; }
                    catch (Exception ex) { log = ex.Message; }
                    finally { log = "done"; }
                }
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;

        var js = Transpile(source);

        Assert.Contains("try {", js);
        Assert.Contains("} catch (__e) {", js);
        Assert.Contains("} finally {", js);
        // `catch (Exception ex)` is the catch-all in C# — instanceof is
        // intentionally elided so JS-thrown strings/POJOs still hit the handler.
        Assert.DoesNotContain("instanceof Exception", js);
        Assert.Contains("let ex = __e;", js);
    }

    [Fact]
    public void Typed_catch_emits_instanceof_guard_and_rethrows_when_unmatched()
    {
        var source = $$"""
            {{RazorGenHeader}}
            public class FormatException2 : Exception { }
            public partial class TryC : ComponentBase
            {
                private string log = "";
                private void Use()
                {
                    try { log = "in"; }
                    catch (FormatException2 fe) { log = "fmt"; }
                }
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;

        var js = Transpile(source);

        Assert.Contains("if (__e instanceof FormatException2)", js);
        Assert.Contains("let fe = __e;", js);
        // No bare catch-all → unmatched exceptions must re-throw, otherwise
        // a typed `catch` accidentally becomes a catch-all on the JS side.
        Assert.Contains("throw __e", js);
    }

    [Fact]
    public void Multiple_catches_chain_with_else_if()
    {
        var source = $$"""
            {{RazorGenHeader}}
            public class FooEx : Exception { }
            public class BarEx : Exception { }
            public partial class TryC : ComponentBase
            {
                private string log = "";
                private void Use()
                {
                    try { log = "in"; }
                    catch (FooEx) { log = "foo"; }
                    catch (BarEx) { log = "bar"; }
                }
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;

        var js = Transpile(source);

        Assert.Contains("if (__e instanceof FooEx)", js);
        Assert.Contains("else if (__e instanceof BarEx)", js);
    }

    [Fact]
    public void When_clause_runs_after_binding_and_rethrows_on_false()
    {
        var source = $$"""
            {{RazorGenHeader}}
            public class CodedEx : Exception { public int Code; }
            public partial class TryC : ComponentBase
            {
                private string log = "";
                private void Use()
                {
                    try { log = "in"; }
                    catch (CodedEx e) when (e.Code == 42) { log = "match"; }
                }
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;

        var js = Transpile(source);

        // Bind first, THEN evaluate the filter — the when-clause references
        // the named exception by its C# identifier.
        var bindIdx = js.IndexOf("let e = __e;", StringComparison.Ordinal);
        var filterIdx = js.IndexOf("if (!(", StringComparison.Ordinal);
        Assert.True(bindIdx >= 0 && filterIdx >= 0, "both binding and filter must be present");
        Assert.True(bindIdx < filterIdx, "binding must precede the when-filter");
        Assert.Contains("throw __e", js);
    }

    [Fact]
    public void Bare_catch_without_other_catches_emits_directly_in_outer_catch()
    {
        var source = $$"""
            {{RazorGenHeader}}
            public partial class TryC : ComponentBase
            {
                private string log = "";
                private void Use()
                {
                    try { log = "in"; }
                    catch { log = "boom"; }
                }
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;

        var js = Transpile(source);

        Assert.Contains("} catch (__e) {", js);
        // Bare catch as the only handler shouldn't introduce an `if (true)`
        // wrapper or an else-rethrow — that's noise the user didn't write.
        Assert.DoesNotContain("if (true)", js);
        Assert.DoesNotContain("else { throw __e", js);
    }

    [Fact]
    public void Throw_statement_emits_throw_with_expression()
    {
        var source = $$"""
            {{RazorGenHeader}}
            public partial class TryC : ComponentBase
            {
                private void Boom() { throw new Exception("boom"); }
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;

        var js = Transpile(source);

        // Object-initialiser path turns `new Exception("boom")` into `{}`
        // (positional args aren't supported yet) — that's a separate gap;
        // here we only care that `throw` itself reaches the JS output.
        Assert.Contains("throw ", js);
    }

    [Fact]
    public void Bare_throw_inside_catch_rethrows_the_caught_identifier()
    {
        var source = $$"""
            {{RazorGenHeader}}
            public partial class TryC : ComponentBase
            {
                private void Use()
                {
                    try { }
                    catch { throw; }
                }
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;

        var js = Transpile(source);

        // A bare `throw;` re-throws the JS exception captured by the
        // outer `catch (__e)` — we resolve to that synthetic name.
        Assert.Contains("throw __e;", js);
    }

    [Fact]
    public void Try_finally_without_catch_omits_catch_block()
    {
        var source = $$"""
            {{RazorGenHeader}}
            public partial class TryC : ComponentBase
            {
                private void Use()
                {
                    try { }
                    finally { }
                }
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;

        var js = Transpile(source);

        Assert.Contains("try {", js);
        Assert.Contains("} finally {", js);
        Assert.DoesNotContain("catch", js);
    }
}
