using Microsoft.CodeAnalysis;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Thrown by the transpiler when it encounters C# / Razor codegen it cannot
/// translate to JavaScript. Carries the source location so the MSBuild task
/// can surface it as a properly-formatted error in the IDE / build output.
/// </summary>
/// <remarks>
/// <para>
/// In the steady state every user-side construct that Razorshave cannot
/// handle is rejected up-front by <c>UnsupportedLanguageFeatureAnalyzer</c>
/// (RZS2001 / RZS2002 / RZS2003). Reaching this exception means either:
/// </para>
/// <list type="bullet">
/// <item>The analyzer has a gap (e.g. a new C# pattern shape we haven't added
/// to <c>SupportedSyntax</c>); or</item>
/// <item>The Razor source generator emitted a <c>BuildRenderTree</c> shape
/// the <see cref="RenderTreeEmitter"/> hasn't been taught to consume.</item>
/// </list>
/// <para>
/// Either way the fix is on the Razorshave side, not the user's, so the
/// error message points to the issue tracker and asks for a repro instead
/// of suggesting a workaround.
/// </para>
/// </remarks>
internal sealed class TranspilerException : Exception
{
    /// <summary>
    /// Where users should report unsupported syntax. Kept as a constant so
    /// every emitter site formats the message the same way and the URL
    /// changes in exactly one place.
    /// </summary>
    public const string IssueUrl = "https://github.com/BernhardPollerspoeck/razorshave/issues/new";

    /// <summary>
    /// The originating source file (mapped through Razor's <c>#line</c>
    /// pragmas back to the <c>.razor</c> when applicable). Empty when the
    /// node has no usable location — the MSBuild task tolerates that.
    /// </summary>
    public string SourceFile { get; }

    /// <summary>1-based line number suitable for MSBuild diagnostics.</summary>
    public int Line { get; }

    /// <summary>1-based column number suitable for MSBuild diagnostics.</summary>
    public int Column { get; }

    private TranspilerException(string file, int line, int column, string message)
        : base(message)
    {
        SourceFile = file;
        Line = line;
        Column = column;
    }

    /// <summary>
    /// Build an exception for an unsupported construct anchored at
    /// <paramref name="node"/>. <paramref name="description"/> should fit
    /// into the sentence "Razorshave cannot transpile {description}." —
    /// e.g. <c>"the C# statement 'TryStatement'"</c>.
    /// </summary>
    public static TranspilerException Unsupported(SyntaxNode node, string description)
    {
        var span = node.GetLocation().GetMappedLineSpan();
        var file = span.IsValid ? span.Path : "";
        // GetMappedLineSpan returns 0-based positions; MSBuild diagnostics
        // and editor squiggles speak 1-based.
        var line = span.IsValid ? span.StartLinePosition.Line + 1 : 0;
        var column = span.IsValid ? span.StartLinePosition.Character + 1 : 0;

        var message =
            $"Razorshave cannot transpile {description}. " +
            $"This is a Razorshave bug — please file an issue at {IssueUrl} " +
            "with a minimal reproduction of the affected component or method.";

        return new TranspilerException(file, line, column, message);
    }
}
