namespace Razorshave.Cli.Transpiler;

/// <summary>
/// State threaded through the emitter chain. Currently only carries the set of
/// declared member names of the enclosing component class so
/// <see cref="ExpressionEmitter"/> can rewrite bare identifiers (<c>currentCount</c>)
/// into member-access (<c>this.currentCount</c>).
/// </summary>
/// <remarks>
/// Syntax-based resolution is enough for M0: a name that matches a class member
/// becomes <c>this.&lt;name&gt;</c>, everything else stays bare. Locals that
/// happen to shadow a member name will therefore emit incorrectly — acceptable
/// for M0, but the fix is to swap in SemanticModel-based resolution.
/// </remarks>
internal sealed class EmitContext
{
    /// <summary>
    /// Simple names of every field / property / method declared directly on
    /// the component class — what <see cref="ExpressionEmitter"/> treats as a
    /// <c>this.</c>-prefixed reference.
    /// </summary>
    public required IReadOnlySet<string> ClassMembers { get; init; }
}
