using Microsoft.Build.Framework;

using Razorshave.Cli.Transpiler;

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

    /// <summary>
    /// URL prefix every emitted asset is rooted at. Default <c>/</c>
    /// (root deployment). Set to e.g. <c>/myapp/</c> when serving the
    /// SPA under a sub-path; the build emits <c>/myapp/main.X.js</c>
    /// instead of <c>/main.X.js</c> so deep-link requests still find
    /// the bundle.
    /// </summary>
    public string BasePath { get; set; } = "/";

    /// <summary>
    /// Static <c>&lt;title&gt;</c> for the generated <c>index.html</c>.
    /// Per-page <c>&lt;PageTitle&gt;</c> components override this at
    /// runtime; this is what the user (and crawlers / share cards) see
    /// before JS boots.
    /// </summary>
    public string Title { get; set; } = "";

    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.High, $"Razorshave: transpiling {ProjectDirectory} (-c {Configuration}, base {BasePath})");

        try
        {
            var exit = BuildCommand.Run(
                ProjectDirectory,
                skipDotnetBuild: true,
                configuration: Configuration,
                basePath: BasePath,
                title: Title);
            if (exit != 0)
            {
                Log.LogError($"Razorshave build failed with exit code {exit}");
                return false;
            }
            return true;
        }
        catch (TranspilerException tex)
        {
            // Surface unsupported-syntax failures as a properly-formatted
            // MSBuild diagnostic (file + line + column + RZS code) so the IDE
            // can squiggle / click-to-source rather than dumping a raw .NET
            // stack trace at the user.
            Log.LogError(
                subcategory: null,
                errorCode: "RZS9001",
                helpKeyword: null,
                file: tex.SourceFile,
                lineNumber: tex.Line,
                columnNumber: tex.Column,
                endLineNumber: 0,
                endColumnNumber: 0,
                message: tex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: true);
            return false;
        }
    }
}
