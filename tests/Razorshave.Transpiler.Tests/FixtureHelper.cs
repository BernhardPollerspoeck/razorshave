using System.Runtime.CompilerServices;

namespace Razorshave.Transpiler.Tests;

/// <summary>
/// Locates the on-disk fixture folder for a given fixture name. Uses
/// <see cref="CallerFilePathAttribute"/> so the resolved path points at the
/// source tree (where Input.razor/Input.g.cs/Output.verified.js live and are
/// committed), not at the test binary's output directory.
/// </summary>
internal static class FixtureHelper
{
    public static string GetDirectory(string name, [CallerFilePath] string callerPath = "")
    {
        var testRoot = Path.GetDirectoryName(callerPath)!;
        return Path.Combine(testRoot, "Fixtures", name);
    }

    public static string ReadInput(string name, [CallerFilePath] string callerPath = "")
    {
        var testRoot = Path.GetDirectoryName(callerPath)!;
        var path = Path.Combine(testRoot, "Fixtures", name, "Input.g.cs");
        return File.ReadAllText(path);
    }
}
