using static Razorshave.Cli.Transpiler.Transpiler;

namespace Razorshave.Transpiler.Tests;

public sealed class ClassSkeletonTests
{
    [Fact]
    public Task Counter_EmitsClassSkeleton()
    {
        var source = FixtureHelper.ReadInput("counter");
        var js = Transpile(source);

        return Verifier.Verify(js, extension: "js")
            .UseDirectory(FixtureHelper.GetDirectory("counter"))
            .UseFileName("Output");
    }

    [Fact]
    public Task Weather_EmitsClassSkeleton()
    {
        var source = FixtureHelper.ReadInput("weather");
        var js = Transpile(source);

        return Verifier.Verify(js, extension: "js")
            .UseDirectory(FixtureHelper.GetDirectory("weather"))
            .UseFileName("Output");
    }

    [Fact]
    public Task MainLayout_EmitsClassSkeleton()
    {
        var source = FixtureHelper.ReadInput("mainlayout");
        var js = Transpile(source);

        return Verifier.Verify(js, extension: "js")
            .UseDirectory(FixtureHelper.GetDirectory("mainlayout"))
            .UseFileName("Output");
    }
}
