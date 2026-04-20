namespace Razorshave.Analyzer.Tests;

/// <summary>
/// Verifies that the analyzer flags C# constructs the transpiler silently
/// emits as TODO-null — the whole point of RZS2001/RZS2002 is to make
/// those silent fallbacks visible at edit time.
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
    public void Flags_try_catch_inside_ComponentBase_subclass()
    {
        var source = ComponentHeader + """
            public class MyPage : ComponentBase
            {
                public void Use() {
                    try { Console.WriteLine("x"); }
                    catch { }
                }
            }
            """;
        var diags = AnalyzerRunner.Run(new UnsupportedLanguageFeatureAnalyzer(), source);
        Assert.Contains(diags, d => d.Id == "RZS2002" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("TryStatement"));
    }

    [Fact]
    public void Flags_throw_statement_inside_Client_class()
    {
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
        // `throw` is a ThrowStatement (statement form) that the transpiler
        // can't emit.
        Assert.Contains(diags, d => d.Id == "RZS2002" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("ThrowStatement"));
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
