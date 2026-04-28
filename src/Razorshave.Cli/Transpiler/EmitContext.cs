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

    // Stack of local-scope names that currently shadow class members.
    // Lambda parameters, `for`-loop variables and primary-constructor
    // parameters (inside the ctor body) all push a frame here so the
    // identifier-rewrite logic in ExpressionEmitter can skip the
    // `this.X` rewrite for names bound locally.
    //
    // Empty at class-body scope. Frames are popped in reverse order by
    // the emitter that pushed them.
    private readonly Stack<IReadOnlyCollection<string>> _localScopes = new();

    public void PushLocalScope(IReadOnlyCollection<string> names) => _localScopes.Push(names);

    public void PopLocalScope() => _localScopes.Pop();

    /// <summary>
    /// True when <paramref name="name"/> is shadowed by a local binding in
    /// any enclosing scope — a lambda parameter, a for-loop variable, or
    /// the primary-constructor's parameter list (before the `this.X = X`
    /// assignments have run).
    /// </summary>
    public bool IsLocallyShadowed(string name)
    {
        foreach (var frame in _localScopes)
        {
            if (frame.Contains(name)) return true;
        }
        return false;
    }

    // Stack of synthetic catch-identifier names. Pushed when entering a
    // catch-clause body, popped on exit. A bare `throw;` (with no expression)
    // looks up the innermost identifier so it can re-throw the JS exception
    // captured by the enclosing `catch (__e) { ... }`.
    private readonly Stack<string> _catchIdentifiers = new();

    public void PushCatchIdentifier(string name) => _catchIdentifiers.Push(name);

    public void PopCatchIdentifier() => _catchIdentifiers.Pop();

    /// <summary>
    /// Returns the innermost catch-identifier when the emitter is currently
    /// inside a catch-clause body. <c>throw;</c> (no expression) re-throws
    /// against this identifier.
    /// </summary>
    public bool TryPeekCatchIdentifier(out string name)
    {
        if (_catchIdentifiers.Count > 0)
        {
            name = _catchIdentifiers.Peek();
            return true;
        }
        name = "";
        return false;
    }
}
