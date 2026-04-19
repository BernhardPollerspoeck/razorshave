using Microsoft.CodeAnalysis;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// State threaded through the emitter chain.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><see cref="ClassMembers"/> lets <see cref="ExpressionEmitter"/>
///     rewrite bare identifiers into <c>this.&lt;name&gt;</c>.</item>
///   <item><see cref="Model"/> backs the semantic-aware specialisations —
///     notably the <c>AddAttribute&lt;T&gt;</c> event-handler detection in
///     <see cref="RenderTreeEmitter"/>. Null-tolerant callers should treat a
///     missing symbol as "skip the specialisation, fall back to syntax".</item>
/// </list>
/// </remarks>
internal sealed class EmitContext
{
    public required IReadOnlySet<string> ClassMembers { get; init; }

    public required SemanticModel Model { get; init; }
}
