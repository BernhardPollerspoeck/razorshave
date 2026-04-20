using static Razorshave.Cli.Transpiler.Transpiler;

namespace Razorshave.Transpiler.Tests;

/// <summary>
/// Feature-matrix for the StaticMemberRewrites BCL bridge. Each row verifies
/// one C# BCL member emits the JS equivalent we commit to — so the "silent
/// TypeError at runtime" class of failure can't slip back in by accident
/// when ExpressionEmitter changes.
/// </summary>
public sealed class BclRewriteTests
{
    private const string RazorGenHeader = """
        using global::System;
        using global::System.Collections.Generic;
        using global::System.Linq;
        using global::Microsoft.AspNetCore.Components;
        using global::Microsoft.AspNetCore.Components.Rendering;
        namespace Fixtures;
        """;

    private static string TranspileBody(string bodyCsharp)
    {
        var source = $$"""
            {{RazorGenHeader}}
            public partial class Bcl : ComponentBase
            {
                {{bodyCsharp}}
                protected override void BuildRenderTree(RenderTreeBuilder __builder) { }
            }
            """;
        return Transpile(source);
    }

    // --- Math ---
    [Fact] public void MathMax()   => Assert.Contains("Math.max(1, 2)",   TranspileBody("private int Run() => Math.Max(1, 2);"));
    [Fact] public void MathMin()   => Assert.Contains("Math.min(3, 4)",   TranspileBody("private int Run() => Math.Min(3, 4);"));
    [Fact] public void MathAbs()   => Assert.Contains("Math.abs(-7)",     TranspileBody("private int Run() => Math.Abs(-7);"));
    [Fact] public void MathRound() => Assert.Contains("Math.round(1.5)",  TranspileBody("private double Run() => Math.Round(1.5);"));
    [Fact] public void MathSqrt()  => Assert.Contains("Math.sqrt(9)",     TranspileBody("private double Run() => Math.Sqrt(9);"));
    [Fact] public void MathPI()    => Assert.Contains("Math.PI",          TranspileBody("private double pi = Math.PI;"));

    // --- string static ---
    [Fact] public void StringEmpty() => Assert.Contains("\"\"", TranspileBody("private string s = string.Empty;"));
    [Fact]
    public void StringFormat()
    {
        var js = TranspileBody("private string Run() => string.Format(\"{0}+{1}\", 1, 2);");
        // Inline IIFE — we don't pin the exact formatter shape, only that the
        // literal indexes are preserved so the caller sees the expected layout.
        Assert.Contains("\"{0}+{1}\"", js);
        Assert.Contains(".replace(", js);
    }
    [Fact]
    public void StringJoin()
    {
        var js = TranspileBody("private string Run() => string.Join(\",\", new[] { \"a\", \"b\" });");
        Assert.Contains(".join(\",\")", js);
    }

    // --- string instance ---
    [Fact]
    public void StringSplit()
    {
        var js = TranspileBody("private string[] Run(string s) => s.Split(\",\");");
        Assert.Contains(".split(\",\")", js);
    }

    // --- Parse / Convert ---
    [Fact] public void IntParse()     => Assert.Contains("parseInt(s, 10)",    TranspileBody("private int Run(string s) => int.Parse(s);"));
    [Fact] public void DoubleParse()  => Assert.Contains("parseFloat(s)",      TranspileBody("private double Run(string s) => double.Parse(s);"));
    [Fact] public void ConvertInt32() => Assert.Contains("parseInt(s, 10)",    TranspileBody("private int Run(string s) => Convert.ToInt32(s);"));
    [Fact] public void ConvertDouble()=> Assert.Contains("parseFloat(s)",      TranspileBody("private double Run(string s) => Convert.ToDouble(s);"));
    [Fact] public void ConvertBool()  => Assert.Contains("Boolean(s)",         TranspileBody("private bool Run(string s) => Convert.ToBoolean(s);"));

    // --- DateTime ---
    [Fact] public void DateTimeNow()   => Assert.Contains("new Date()",                  TranspileBody("private DateTime dt = DateTime.Now;"));
    [Fact] public void DateTimeUtcNow()=> Assert.Contains("new Date()",                  TranspileBody("private DateTime dt = DateTime.UtcNow;"));
    [Fact] public void DateTimeToday()
    {
        var js = TranspileBody("private DateTime dt = DateTime.Today;");
        Assert.Contains("setHours(0,0,0,0)", js);
    }

    // --- LINQ ---
    [Fact]
    public void LinqSelect()
    {
        var js = TranspileBody("private int[] Run(int[] xs) => xs.Select(x => x * 2).ToArray();");
        Assert.Contains(".map((x) => x * 2)", js);
        // ToArray is an identity — no `.toArray()` call on the JS side.
        Assert.DoesNotContain(".toArray()", js);
    }
    [Fact]
    public void LinqWhere()
    {
        var js = TranspileBody("private int[] Run(int[] xs) => xs.Where(x => x > 0).ToArray();");
        Assert.Contains(".filter((x) => x > 0)", js);
    }
    [Fact]
    public void LinqAny()
    {
        var js = TranspileBody("private bool Run(int[] xs) => xs.Any(x => x > 5);");
        Assert.Contains(".some((x) => x > 5)", js);
    }
    [Fact]
    public void LinqAll()
    {
        var js = TranspileBody("private bool Run(int[] xs) => xs.All(x => x > 0);");
        Assert.Contains(".every((x) => x > 0)", js);
    }
    [Fact]
    public void LinqFirstOrDefault()
    {
        var js = TranspileBody("private int Run(int[] xs) => xs.FirstOrDefault(x => x == 3);");
        Assert.Contains(".find((x) => x === 3)", js);
    }
    [Fact]
    public void LinqSum()
    {
        var js = TranspileBody("private int Run(int[] xs) => xs.Sum();");
        Assert.Contains(".reduce((a, b) => a + b, 0)", js);
    }
    [Fact]
    public void LinqSumWithSelector()
    {
        var js = TranspileBody("private int Run(int[] xs) => xs.Sum(x => x * 2);");
        Assert.Contains(".map((x) => x * 2)", js);
        Assert.Contains(".reduce((a, b) => a + b, 0)", js);
    }

    // --- List<T> ---
    [Fact]
    public void ListAdd()
    {
        var js = TranspileBody("private void Run(List<int> xs) { xs.Add(42); }");
        // `xs` is the method parameter, stays bare.
        Assert.Contains("xs.push(42)", js);
    }

    // --- ToArray / ToList are identity (JS arrays already match) ---
    [Fact]
    public void ToArrayIsIdentity()
    {
        var js = TranspileBody("private int[] Run(int[] xs) => xs.ToArray();");
        Assert.DoesNotContain(".toArray()", js);
        // Emitted as `return xs;` — ToArray drops to identity. `xs` is the
        // method parameter, not a class field, so it's NOT rewritten to
        // `this.xs`.
        Assert.Contains("return xs", js);
    }
    [Fact]
    public void ToListIsIdentity()
    {
        var js = TranspileBody("private List<int> Run(int[] xs) => xs.ToList();");
        Assert.DoesNotContain(".toList()", js);
    }

    // --- Guid ---
    [Fact]
    public void GuidNewGuid() => Assert.Contains("_newGuid()", TranspileBody("private string id = Guid.NewGuid().ToString();"));
}
