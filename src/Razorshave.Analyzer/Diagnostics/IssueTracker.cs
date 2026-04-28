namespace Razorshave.Analyzer.Diagnostics;

/// <summary>
/// Where users should file issues for unsupported Razorshave constructs.
/// Diagnostics that fire because Razorshave cannot transpile a piece of C#
/// (RZS2001 / RZS2002 / RZS2003) append <see cref="Tail"/> to their message
/// so every report carries the same call-to-action.
/// </summary>
/// <remarks>
/// Diagnostics about user-fixable mistakes (RZS3001 — runtime-component
/// shadow) deliberately do NOT include this tail: there is no Razorshave
/// gap to report, the user just needs to rename their class.
/// </remarks>
internal static class IssueTracker
{
    public const string Url = "https://github.com/BernhardPollerspoeck/razorshave/issues/new";

    public const string Tail =
        " Please file an issue at " + Url +
        " with a minimal reproduction so this gap can be closed.";
}
