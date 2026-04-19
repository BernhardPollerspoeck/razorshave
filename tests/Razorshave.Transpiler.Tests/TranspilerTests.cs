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
}
