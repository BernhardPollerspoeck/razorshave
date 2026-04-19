using Microsoft.Build.Framework;

using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Razorshave.Cli;

/// <summary>
/// MSBuild task that invokes <see cref="BuildCommand"/> after a project has
/// been built. The accompanying <c>build/Razorshave.Cli.targets</c> file
/// wires this up to run <c>AfterTargets="Build"</c> when the Razorshave
/// configuration is active, so the full developer workflow is
/// <c>dotnet build -c Razorshave</c>.
/// </summary>
/// <remarks>
/// The task passes <c>skipDotnetBuild: true</c> to <see cref="BuildCommand.Run"/>
/// because MSBuild has already produced <c>.razor.g.cs</c> by the time this
/// target fires — another nested <c>dotnet build</c> would recurse.
/// </remarks>
public sealed class RazorshaveTranspileTask : MSBuildTask
{
    [Required]
    public string ProjectDirectory { get; set; } = "";

    public string Configuration { get; set; } = "Debug";

    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.High, $"Razorshave: transpiling {ProjectDirectory} (-c {Configuration})");

        try
        {
            var exit = BuildCommand.Run(ProjectDirectory, skipDotnetBuild: true, configuration: Configuration);
            if (exit != 0)
            {
                Log.LogError($"Razorshave build failed with exit code {exit}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: true);
            return false;
        }
    }
}
