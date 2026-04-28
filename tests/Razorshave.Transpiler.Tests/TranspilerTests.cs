using static Razorshave.Cli.Transpiler.Transpiler;

namespace Razorshave.Transpiler.Tests;

public sealed class TranspilerTests
{
    [Fact]
    public Task Counter()
    {
        var js = Transpile(FixtureHelper.ReadInput("counter"));

        return Verifier.Verify(js, extension: "js")
            .UseDirectory(FixtureHelper.GetDirectory("counter"))
            .UseFileName("Output");
    }

    [Fact]
    public Task Weather()
    {
        var js = Transpile(FixtureHelper.ReadInput("weather"));

        return Verifier.Verify(js, extension: "js")
            .UseDirectory(FixtureHelper.GetDirectory("weather"))
            .UseFileName("Output");
    }

    [Fact]
    public Task MainLayout()
    {
        var js = Transpile(FixtureHelper.ReadInput("mainlayout"));

        return Verifier.Verify(js, extension: "js")
            .UseDirectory(FixtureHelper.GetDirectory("mainlayout"))
            .UseFileName("Output");
    }

    // Razor's source generator routes static PageTitle text through
    // AddMarkupContent (rather than AddContent) when it contains characters
    // it classifies as markup-relevant — non-ASCII like em-dash or €,
    // HTML entities, embedded inline tags. This fixture pins that emission
    // so any future SG behaviour drift is caught here, and so the runtime's
    // text extractor stays under regression coverage for the markup-vnode
    // path.
    [Fact]
    public Task MarkupTitle()
    {
        var js = Transpile(FixtureHelper.ReadInput("markuptitle"));

        return Verifier.Verify(js, extension: "js")
            .UseDirectory(FixtureHelper.GetDirectory("markuptitle"))
            .UseFileName("Output");
    }
}
