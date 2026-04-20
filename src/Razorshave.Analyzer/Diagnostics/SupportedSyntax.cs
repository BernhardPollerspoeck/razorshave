using System.Collections.Immutable;

using Microsoft.CodeAnalysis.CSharp;

namespace Razorshave.Analyzer.Diagnostics;

/// <summary>
/// The allowlist of C# syntax kinds the Razorshave transpiler can currently
/// emit as valid JavaScript. Any kind outside these sets makes
/// <see cref="UnsupportedLanguageFeatureAnalyzer"/> fire a diagnostic so
/// the user sees the gap at edit-time rather than discovering silent
/// <c>/* TODO */ null</c> in the transpiled output at runtime.
/// </summary>
/// <remarks>
/// Kept explicit and inline rather than auto-derived from the transpiler
/// to match the "no silent drift" principle: when the transpiler gains a
/// new case, you add the kind here AND ship a test covering it. An
/// alignment test in <c>Razorshave.Transpiler.Tests</c> would be the next
/// upgrade — for v0.1 the allowlist is manually curated and small enough
/// to inspect.
/// </remarks>
internal static class SupportedSyntax
{
    /// <summary>
    /// Expression kinds <c>ExpressionEmitter.Emit</c> has explicit cases
    /// for. Everything else (user-code <c>typeof</c>, <c>nameof</c>,
    /// pattern-matching beyond <c>is</c>-via-ternary, etc.) falls through
    /// to the transpiler's TODO-null branch.
    /// </summary>
    public static readonly ImmutableHashSet<SyntaxKind> Expressions =
        ImmutableHashSet.CreateRange(new[]
        {
            // literals
            SyntaxKind.NumericLiteralExpression,
            SyntaxKind.StringLiteralExpression,
            SyntaxKind.CharacterLiteralExpression,
            SyntaxKind.TrueLiteralExpression,
            SyntaxKind.FalseLiteralExpression,
            SyntaxKind.NullLiteralExpression,
            SyntaxKind.DefaultLiteralExpression,     // `default` bare
            SyntaxKind.InterpolatedStringExpression,
            // identifiers
            SyntaxKind.IdentifierName,
            SyntaxKind.GenericName,
            SyntaxKind.AliasQualifiedName,
            SyntaxKind.QualifiedName,
            SyntaxKind.PredefinedType,
            SyntaxKind.ThisExpression,
            // unary + binary arithmetic / logic
            SyntaxKind.UnaryMinusExpression, SyntaxKind.UnaryPlusExpression,
            SyntaxKind.LogicalNotExpression, SyntaxKind.BitwiseNotExpression,
            SyntaxKind.PreIncrementExpression, SyntaxKind.PreDecrementExpression,
            SyntaxKind.PostIncrementExpression, SyntaxKind.PostDecrementExpression,
            SyntaxKind.AddExpression, SyntaxKind.SubtractExpression,
            SyntaxKind.MultiplyExpression, SyntaxKind.DivideExpression,
            SyntaxKind.ModuloExpression,
            SyntaxKind.LeftShiftExpression, SyntaxKind.RightShiftExpression,
            SyntaxKind.BitwiseAndExpression, SyntaxKind.BitwiseOrExpression,
            SyntaxKind.ExclusiveOrExpression,
            SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression,
            SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression,
            SyntaxKind.LessThanExpression, SyntaxKind.LessThanOrEqualExpression,
            SyntaxKind.GreaterThanExpression, SyntaxKind.GreaterThanOrEqualExpression,
            SyntaxKind.CoalesceExpression,              // `a ?? b`
            // assignment family
            SyntaxKind.SimpleAssignmentExpression,
            SyntaxKind.AddAssignmentExpression, SyntaxKind.SubtractAssignmentExpression,
            SyntaxKind.MultiplyAssignmentExpression, SyntaxKind.DivideAssignmentExpression,
            SyntaxKind.ModuloAssignmentExpression,
            SyntaxKind.LeftShiftAssignmentExpression, SyntaxKind.RightShiftAssignmentExpression,
            SyntaxKind.AndAssignmentExpression, SyntaxKind.OrAssignmentExpression,
            SyntaxKind.ExclusiveOrAssignmentExpression,
            // member access / invocation / indexing
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxKind.InvocationExpression,
            SyntaxKind.ElementAccessExpression,
            SyntaxKind.RangeExpression,          // `a..b` inside `arr[a..b]`
            SyntaxKind.IndexExpression,          // `^n` inside `arr[^n]`
            SyntaxKind.ConditionalAccessExpression,  // `a?.b.c`
            SyntaxKind.MemberBindingExpression,      // `.b` inside `a?.b`
            SyntaxKind.ElementBindingExpression,     // `[i]` inside `a?[i]`
            SyntaxKind.CoalesceAssignmentExpression, // `a ??= b`
            // type/default/nameof — all land in ExpressionEmitter with the
            // documented approximation (typeof → string literal, default(T)
            // → null, nameof → simple-name string). Allowlist keeps them
            // from double-flagging inside IsPattern/SwitchExpression walks.
            SyntaxKind.TypeOfExpression,
            SyntaxKind.DefaultExpression,
            // control
            SyntaxKind.ConditionalExpression,            // ternary
            SyntaxKind.ParenthesizedExpression,
            SyntaxKind.AwaitExpression,
            SyntaxKind.CastExpression,
            // object construction
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression,
            SyntaxKind.ObjectInitializerExpression,
            SyntaxKind.ArrayCreationExpression,
            SyntaxKind.ImplicitArrayCreationExpression,
            SyntaxKind.ArrayInitializerExpression,
            SyntaxKind.CollectionExpression,
            SyntaxKind.SpreadElement,
            SyntaxKind.WithExpression,
            SyntaxKind.WithInitializerExpression,
            // lambdas
            SyntaxKind.SimpleLambdaExpression,
            SyntaxKind.ParenthesizedLambdaExpression,
            // switch-expression (arms emit through same machinery)
            SyntaxKind.SwitchExpression,
            SyntaxKind.SwitchExpressionArm,
            SyntaxKind.ConstantPattern,
            SyntaxKind.RelationalPattern,
            SyntaxKind.DiscardPattern,
            SyntaxKind.VarPattern,
            SyntaxKind.WhenClause,
            // is-pattern: ExpressionEmitter.EmitIsPattern handles the null
            // forms (`x is null`, `x is not null`); other pattern shapes still
            // fall through to TODO-null inside that emitter, but the kinds
            // themselves are allowed at walk time so the analyzer doesn't
            // double-flag them before EmitIsPattern gets to diagnose.
            SyntaxKind.IsPatternExpression,
            SyntaxKind.NotPattern,
            // misc
            SyntaxKind.InterpolatedStringText,
            SyntaxKind.Interpolation,
            SyntaxKind.Argument,
            SyntaxKind.ExpressionElement,
        });

    /// <summary>
    /// Statement kinds <c>StatementEmitter.Emit</c> knows how to write.
    /// </summary>
    public static readonly ImmutableHashSet<SyntaxKind> Statements =
        ImmutableHashSet.CreateRange(new[]
        {
            SyntaxKind.Block,
            SyntaxKind.ExpressionStatement,
            SyntaxKind.LocalDeclarationStatement,
            SyntaxKind.ReturnStatement,
            SyntaxKind.IfStatement,
            SyntaxKind.ElseClause,
            SyntaxKind.ForStatement,
            SyntaxKind.ForEachStatement,
            SyntaxKind.VariableDeclaration,
            SyntaxKind.VariableDeclarator,
            SyntaxKind.EqualsValueClause,
        });
}
