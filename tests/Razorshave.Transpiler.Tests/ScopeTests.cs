using static Razorshave.Cli.Transpiler.Transpiler;

namespace Razorshave.Transpiler.Tests;

/// <summary>
/// Regression suite for <c>EmitContext.IsLocallyShadowed</c> — the scope stack
/// that keeps lambda parameters, for-loop variables and primary-constructor
/// parameters from being rewritten to <c>this.X</c> when they happen to share
/// a name with a class member.
/// </summary>
public sealed class ScopeTests
{
    private const string RazorGenHeader = """
        using global::Microsoft.AspNetCore.Components;
        using global::Microsoft.AspNetCore.Components.Rendering;
        namespace Fixtures;
        """;

    [Fact]
    public void Lambda_parameter_shadows_class_field()
    {
        // Field `item` and lambda parameter `item` both exist. Without the
        // scope-stack the lambda body emitted `this.item.name`, pointing at
        // the class field. With it, the lambda-local `item` wins.
        var source = $$"""
            {{RazorGenHeader}}
            public partial class Shadow : ComponentBase
            {
                private string item = "class-field";
                private void Use()
                {
                    var result = new[] { "a", "b" }.Select(item => item.Length);
                }
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;

        var js = Transpile(source);

        // `item.Length` inside the lambda should emit as `item.length`, not
        // `this.item.length`. The outer `new[] { ... }` has no `item` so
        // searching for `this.item` is enough — it should not appear.
        Assert.DoesNotContain("this.item.length", js);
        Assert.Contains("item.length", js);
    }

    [Fact]
    public void ForLoop_variable_shadows_class_field()
    {
        var source = $$"""
            {{RazorGenHeader}}
            public partial class Shadow : ComponentBase
            {
                private int i = 42;
                private void Use()
                {
                    for (var i = 0; i < 3; i++) { var _ = i; }
                }
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;

        var js = Transpile(source);

        // Loop header + body references to `i` must stay bare.
        Assert.Contains("for (let i = 0; i < 3; i++)", js);
        Assert.DoesNotContain("this.i.", js);
    }

    [Fact]
    public void Uppercase_instance_variable_does_not_trigger_static_rewrite()
    {
        // Regression for StaticMemberRewrites.TryGetStaticReceiver silent
        // fail: without SemanticModel the heuristic matched any uppercase
        // identifier as a static type, so `Things.Count()` on an instance
        // field `List<T> Things` would get rewritten to `Things.length` —
        // wrong because Things is `this.things` (a field access, not a
        // static type).
        var source = $$"""
            {{RazorGenHeader}}
            public partial class Shadow : ComponentBase
            {
                private System.Collections.Generic.List<int> Things = new();
                private int CountThem() => Things.Count;
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;

        var js = Transpile(source);

        // `Things.Count` is property access (no parens) — should emit
        // `this.things.count` (camelCase of Count via member access).
        Assert.Contains("this.things.count", js);
        // And must NOT have been misread as a static `.Count()` call
        // that the rewrite would have turned into `.length`.
        Assert.DoesNotContain("Things.length", js);
    }

    [Fact]
    public void Primary_ctor_parameter_stays_bare_in_super_call()
    {
        // Regression for the TDZ crash: `super(http)` where `http` is a primary
        // constructor parameter. Before the scope-stack this emitted
        // `super(this.http)` — TDZ error because `this` is unavailable before
        // the super() call returns.
        var source = $$"""
            using global::Microsoft.AspNetCore.Components;
            using Razorshave.Abstractions;
            namespace Fixtures;
            [Client]
            public sealed class MyApi(System.Net.Http.HttpClient http) : ApiClient(http)
            {
            }
            """;

        var js = TranspileClientClass(source);

        Assert.Contains("super(http)", js);
        Assert.DoesNotContain("super(this.http)", js);
    }
}
