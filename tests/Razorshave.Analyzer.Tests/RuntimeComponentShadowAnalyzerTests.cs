namespace Razorshave.Analyzer.Tests;

/// <summary>
/// Verifies that <c>RZS3001</c> fires when a user component shares a name
/// with a runtime-provided one — left unchecked, the HeaderEmitter would
/// silently drop the user's import, so their class shadowed by the runtime
/// version is a classic silent-fail.
/// </summary>
public sealed class RuntimeComponentShadowAnalyzerTests
{
    private const string Header = """
        using Microsoft.AspNetCore.Components;
        """;

    [Fact]
    public void Flags_user_NavLink_inheriting_ComponentBase()
    {
        var source = Header + """
            public class NavLink : ComponentBase { }
            """;
        var diags = AnalyzerRunner.Run(new RuntimeComponentShadowAnalyzer(), source);
        Assert.Contains(diags, d => d.Id == "RZS3001" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("NavLink"));
    }

    [Fact]
    public void Flags_user_Router_inheriting_ComponentBase()
    {
        var source = Header + """
            public class Router : ComponentBase { }
            """;
        var diags = AnalyzerRunner.Run(new RuntimeComponentShadowAnalyzer(), source);
        Assert.Contains(diags, d => d.Id == "RZS3001" && d.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("Router"));
    }

    [Fact]
    public void Does_not_flag_user_NavLink_that_is_not_a_component()
    {
        // A regular class named NavLink that doesn't inherit ComponentBase
        // isn't a Razor component, so the transpiler never emits a header
        // entry for it — no shadow, no diagnostic.
        var source = Header + """
            public class NavLink { }
            """;
        var diags = AnalyzerRunner.Run(new RuntimeComponentShadowAnalyzer(), source);
        Assert.DoesNotContain(diags, d => d.Id == "RZS3001");
    }

    [Fact]
    public void Does_not_flag_uniquely_named_components()
    {
        var source = Header + """
            public class MyNavLink : ComponentBase { }
            public class CustomRouter : ComponentBase { }
            """;
        var diags = AnalyzerRunner.Run(new RuntimeComponentShadowAnalyzer(), source);
        Assert.DoesNotContain(diags, d => d.Id == "RZS3001");
    }
}
