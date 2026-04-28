namespace Razorshave.Analyzer.Tests;

/// <summary>
/// Verifies that the analyzer flags C# constructs the transpiler cannot
/// emit — the whole point of RZS2001/RZS2002/RZS2003 is to make the gap
/// visible at edit time as a red squiggle, before the transpiler hits its
/// safety-net throw at build time.
/// </summary>
public sealed class UnsupportedLanguageFeatureAnalyzerTests
{
    private const string ComponentHeader = """
        using System;
        using Microsoft.AspNetCore.Components;
        using Microsoft.AspNetCore.Components.Rendering;
        """;

    [Fact]
    public void Does_not_flag_regular_classes_outside_razorshave_scope()
    {
        // No ComponentBase subclass, no [Client] attribute → the analyzer
        // shouldn't care what's inside.
        var source = ComponentHeader + """
            public class PlainOldThing
            {
                public void Use() {
                    try { throw new Exception("boom"); }
                    catch (Exception ex) { Console.WriteLine(ex.Message); }
                }
            }
            """;
        var diags = AnalyzerRunner.Run(new UnsupportedLanguageFeatureAnalyzer(), source);
        Assert.Empty(diags.Where(d => d.Id is "RZS2001" or "RZS2002"));
    }

    [Fact]
    public void Does_not_flag_try_catch_inside_ComponentBase_subclass()
    {
        // try/catch/finally is supported: collapses into a single JS
        // `catch (__e) { ... if-chain ... }` plus optional finally.
        var source = ComponentHeader + """
            public class MyPage : ComponentBase
            {
                public void Use() {
                    try { Console.WriteLine("x"); }
                    catch (Exception ex) { Console.WriteLine(ex.Message); }
                    finally { Console.WriteLine("done"); }
                }
            }
            """;
        var diags = AnalyzerRunner.Run(new UnsupportedLanguageFeatureAnalyzer(), source);
        Assert.Empty(diags.Where(d => d.Id is "RZS2001" or "RZS2002" or "RZS2003"));
    }

    [Fact]
    public void Does_not_flag_throw_statement_inside_Client_class()
    {
        // ThrowStatement is supported — the transpiler emits `throw <expr>;`
        // directly, with bare `throw;` re-throwing the JS exception captured
        // by the enclosing catch.
        var source = ComponentHeader + """
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class ClientAttribute : Attribute { }

            [Client]
            public class MyService
            {
                public void Use() { throw new Exception("boom"); }
            }
            """;
        var diags = AnalyzerRunner.Run(new UnsupportedLanguageFeatureAnalyzer(), source);
        Assert.Empty(diags.Where(d => d.Id is "RZS2001" or "RZS2002" or "RZS2003"));
    }

    [Fact]
    public void Flags_unsupported_pattern_kind_in_switch_expression()
    {
        // A logical-or pattern (`> 0 or < -10`) inside a switch-expression arm
        // is a BinaryPatternSyntax — not on the pattern allowlist yet (only
        // ConstantPattern, RelationalPattern, DiscardPattern, VarPattern, and
        // NotPattern are supported). The pattern walker has to fire even
        // though the outer SwitchExpression is allowed, because the inner
        // pattern is a separate PatternSyntax.
        var source = ComponentHeader + """
            public class MyPage : ComponentBase
            {
                public string Use(int x) => x switch { > 0 or < -10 => "edge", _ => "mid" };
            }
            """;
        var diags = AnalyzerRunner.Run(new UnsupportedLanguageFeatureAnalyzer(), source);
        Assert.Contains(diags, d => d.Id == "RZS2003");
    }

    [Fact]
    public void Issue_link_appears_in_diagnostic_message()
    {
        // Every RZS200x diagnostic must point users to the issue tracker —
        // that's the whole call-to-action.
        var source = ComponentHeader + """
            public class MyPage : ComponentBase
            {
                public unsafe void Use() { var mem = stackalloc int[4]; }
            }
            """;
        var diags = AnalyzerRunner.Run(new UnsupportedLanguageFeatureAnalyzer(), source);
        var diag = Assert.Single(diags.Where(d => d.Id == "RZS2001"));
        var msg = diag.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("https://github.com/BernhardPollerspoeck/razorshave/issues/new", msg);
    }

    [Fact]
    public void Flags_stackalloc_expression_inside_component()
    {
        // `typeof(string)` moved onto the allowlist in Q-Batch 12 (with a
        // documented approximation: emits the simple type name as string).
        // A stackalloc-expression still has no JS counterpart and is a clean
        // fallback example for "analyzer flags C# kinds without an emitter".
        var source = ComponentHeader + """
            public class MyPage : ComponentBase
            {
                public unsafe void Use() { var mem = stackalloc int[4]; }
            }
            """;
        var diags = AnalyzerRunner.Run(new UnsupportedLanguageFeatureAnalyzer(), source);
        Assert.Contains(diags, d => d.Id == "RZS2001" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("StackAllocArrayCreationExpression"));
    }

    [Fact]
    public void Flags_constructs_inside_generated_code_when_class_is_a_component()
    {
        // Razor compiles .razor → .razor.g.cs with a `<auto-generated>` header;
        // the default `GeneratedCodeAnalysisFlags.None` makes Roslyn skip those
        // files silently. Without `Analyze | ReportDiagnostics` our analyzer
        // would let every @code-block unsupported construct slip through — the
        // bug the manual screenshot check caught. Assert that generated-code
        // diagnostics DO surface when the class qualifies as a transpile
        // target (ComponentBase subclass).
        //
        // Uses `stackalloc` (RZS2001) — try/catch is no longer a fallback
        // example because the transpiler now supports it.
        var source = """
            // <auto-generated/>
            #pragma warning disable 1591
            using System;
            using Microsoft.AspNetCore.Components;
            using Microsoft.AspNetCore.Components.Rendering;

            public class MyPage : ComponentBase
            {
                public unsafe void Use() { var mem = stackalloc int[4]; }
            }
            """;
        var diags = AnalyzerRunner.Run(new UnsupportedLanguageFeatureAnalyzer(), source);
        Assert.Contains(diags, d => d.Id == "RZS2001" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("StackAllocArrayCreationExpression"));
    }

    [Fact]
    public void Does_not_flag_supported_expressions()
    {
        // Covers the most common Razor-code shapes: member access, ternary,
        // lambda, for-loop, switch-expression, object initializer.
        // Nullable and array type annotations (`string?`, `int[]`) must not
        // trigger — they're type metadata, not emitted expressions.
        var source = ComponentHeader + """
            public class MyPage : ComponentBase
            {
                private int count;
                private string? label;
                private int[] arr = new int[0];
                public void Use() {
                    var even = count % 2 == 0;
                    var text = even ? "yes" : "no";
                    for (var i = 0; i < count; i++) { label = i.ToString(); }
                    var summary = count switch { 0 => "none", < 5 => "few", _ => "many" };
                    arr = new[] { 1, 2, 3 };
                }
            }
            """;
        var diags = AnalyzerRunner.Run(new UnsupportedLanguageFeatureAnalyzer(), source);
        var ours = diags.Where(d => d.Id is "RZS2001" or "RZS2002").ToList();
        // Any flagged kind here is either a gap in the allowlist or a real
        // transpiler miss we should fix first.
        Assert.Empty(ours);
    }
}
