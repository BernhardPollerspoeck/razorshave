using System.Text;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Emits a single C# expression as its JavaScript equivalent.
/// </summary>
/// <remarks>
/// Scope for 5.5:
/// literals, identifiers (with <c>this.</c>-rewrite on class members), <c>this</c>,
/// prefix/postfix unary, binary, assignment (including compound forms),
/// member-access, invocation, and <c>await</c>. Everything else lands in the
/// default branch as <c>/* TODO: &lt;Kind&gt; */ null</c> so snapshot tests stay
/// readable and reveal what still needs to be handled.
/// </remarks>
internal static class ExpressionEmitter
{
    public static void Emit(ExpressionSyntax expr, StringBuilder sb, EmitContext ctx)
    {
        switch (expr)
        {
            case LiteralExpressionSyntax lit:
                // C# string literals (including verbatim @"…" with embedded
                // newlines) must be re-emitted as valid JS strings. Non-string
                // literals (int, bool, null, float) preserve their source
                // representation verbatim.
                if (lit.Token.Value is string s)
                {
                    sb.Append(EncodeJsString(s));
                }
                else
                {
                    sb.Append(lit.Token.Text);
                }
                break;

            case ThisExpressionSyntax:
                sb.Append("this");
                break;

            case IdentifierNameSyntax id:
                EmitIdentifier(id.Identifier.Text, sb, ctx);
                break;

            case AliasQualifiedNameSyntax alias:
                // `global::Microsoft` → emit just `Microsoft`; JS has no alias syntax.
                if (alias.Name is IdentifierNameSyntax aliasName)
                {
                    EmitIdentifier(aliasName.Identifier.Text, sb, ctx);
                }
                else
                {
                    Emit(alias.Name, sb, ctx);
                }
                break;

            case PostfixUnaryExpressionSyntax post:
                Emit(post.Operand, sb, ctx);
                sb.Append(post.OperatorToken.Text);
                break;

            case PrefixUnaryExpressionSyntax pre:
                sb.Append(pre.OperatorToken.Text);
                Emit(pre.Operand, sb, ctx);
                break;

            case BinaryExpressionSyntax bin:
                Emit(bin.Left, sb, ctx);
                sb.Append(' ').Append(bin.OperatorToken.Text).Append(' ');
                Emit(bin.Right, sb, ctx);
                break;

            case AssignmentExpressionSyntax assign:
                if (TryEmitEventSubscription(assign, sb, ctx)) break;
                Emit(assign.Left, sb, ctx);
                sb.Append(' ').Append(assign.OperatorToken.Text).Append(' ');
                Emit(assign.Right, sb, ctx);
                break;

            case MemberAccessExpressionSyntax mae:
                if (StaticMemberRewrites.TryRewriteMemberAccess(mae, sb, ctx)) break;
                Emit(mae.Expression, sb, ctx);
                sb.Append('.').Append(NameConventions.ToCamelCase(mae.Name.Identifier.Text));
                break;

            case InvocationExpressionSyntax inv:
                if (StaticMemberRewrites.TryRewriteInvocation(inv, sb, ctx)) break;
                Emit(inv.Expression, sb, ctx);
                sb.Append('(');
                for (var i = 0; i < inv.ArgumentList.Arguments.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    Emit(inv.ArgumentList.Arguments[i].Expression, sb, ctx);
                }
                sb.Append(')');
                break;

            case AwaitExpressionSyntax aw:
                sb.Append("await ");
                Emit(aw.Expression, sb, ctx);
                break;

            case SimpleLambdaExpressionSyntax simple:
                // `x => expr` → `(x) => expr`. JS arrow functions are a clean
                // 1:1 mapping for C# lambdas; only block bodies need special
                // handling (StatementEmitter), and those only appear in
                // RenderFragment delegates which have their own emitter path.
                sb.Append('(').Append(simple.Parameter.Identifier.Text).Append(") => ");
                EmitLambdaBody(simple.Body, sb, ctx);
                break;

            case ParenthesizedLambdaExpressionSyntax paren2:
                sb.Append('(');
                for (var i = 0; i < paren2.ParameterList.Parameters.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(paren2.ParameterList.Parameters[i].Identifier.Text);
                }
                sb.Append(") => ");
                EmitLambdaBody(paren2.Body, sb, ctx);
                break;

            case ParenthesizedExpressionSyntax paren:
                sb.Append('(');
                Emit(paren.Expression, sb, ctx);
                sb.Append(')');
                break;

            case ConditionalExpressionSyntax cond:
                // C# and JS ternary share exact syntax — direct one-to-one
                // mapping. Each operand still goes through Emit so nested
                // rewrites (static members, object initialisers) apply.
                Emit(cond.Condition, sb, ctx);
                sb.Append(" ? ");
                Emit(cond.WhenTrue, sb, ctx);
                sb.Append(" : ");
                Emit(cond.WhenFalse, sb, ctx);
                break;

            case ObjectCreationExpressionSyntax oc:
                // `new TodoItem { Id = x, Text = y }` → `{ id: x, text: y }`.
                // We intentionally drop the type name — records and POCO-style
                // classes emit as plain JS object literals at runtime. Keys
                // match the C# property names, camel-cased so they line up
                // with how the rest of the emitter renames identifiers.
                // Positional `new TodoItem(x, y)` is not supported here yet
                // — records in M0 use object initialisers.
                if (oc.Initializer is not null)
                {
                    EmitObjectInitializer(oc.Initializer, sb, ctx);
                }
                else
                {
                    sb.Append("{}");
                }
                break;

            case ImplicitObjectCreationExpressionSyntax implicitOc:
                // `new() { Id = x }` — the target type is implicit, emission
                // is identical to the explicit form.
                if (implicitOc.Initializer is not null)
                {
                    EmitObjectInitializer(implicitOc.Initializer, sb, ctx);
                }
                else
                {
                    sb.Append("{}");
                }
                break;

            case WithExpressionSyntax with:
                // `record with { Done = !x.Done }` → `{ ...record, done: !x.done }`.
                // The spread preserves all existing keys, then the initialiser
                // overwrites the ones the user listed.
                sb.Append("{ ...");
                Emit(with.Expression, sb, ctx);
                foreach (var assignExpr in with.Initializer.Expressions)
                {
                    if (assignExpr is AssignmentExpressionSyntax a && a.Left is IdentifierNameSyntax leftId)
                    {
                        sb.Append(", ").Append(NameConventions.ToCamelCase(leftId.Identifier.Text)).Append(": ");
                        Emit(a.Right, sb, ctx);
                    }
                }
                sb.Append(" }");
                break;

            default:
                sb.Append("/* TODO: ").Append(expr.Kind()).Append(" */ null");
                break;
        }
    }

    // `Store.OnChange += StateHasChanged` / `Store.OnChange -= StateHasChanged`
    // rewrites via the runtime's `_bound(name)` helper. We use SemanticModel
    // to confirm the LHS's member is an `event` so ordinary += assignments
    // (e.g. `counter += 1`) keep their native JS form. The same bound handler
    // reference is used on +/−, so unsubscribe actually cancels the subscribe.
    private static bool TryEmitEventSubscription(AssignmentExpressionSyntax assign, StringBuilder sb, EmitContext ctx)
    {
        var kind = assign.Kind();
        if (kind != SyntaxKind.AddAssignmentExpression && kind != SyntaxKind.SubtractAssignmentExpression)
        {
            return false;
        }
        if (assign.Left is not MemberAccessExpressionSyntax mae) return false;

        if (ctx.Model.GetSymbolInfo(mae).Symbol is not Microsoft.CodeAnalysis.IEventSymbol eventSym)
        {
            return false;
        }

        // `OnChange` → subscribe `onChange` / unsubscribe `offChange`. The
        // event naming convention (leading `On`) lets us form a natural
        // off-pair by swapping the prefix. Without the `On` prefix we fall
        // back to prepending raw `off`.
        var eventName = eventSym.Name;
        string subscribe, unsubscribe;
        if (eventName.StartsWith("On", StringComparison.Ordinal) && eventName.Length > 2)
        {
            subscribe = NameConventions.ToCamelCase(eventName);
            unsubscribe = "off" + eventName[2..];
        }
        else
        {
            subscribe = NameConventions.ToCamelCase(eventName);
            unsubscribe = "off" + eventName;
        }

        Emit(mae.Expression, sb, ctx);
        sb.Append('.').Append(kind == SyntaxKind.AddAssignmentExpression ? subscribe : unsubscribe).Append('(');
        EmitBoundHandler(assign.Right, sb, ctx);
        sb.Append(')');
        return true;
    }

    // Normalises a handler reference (`StateHasChanged` or `this.StateHasChanged`)
    // into `this._bound('stateHasChanged')` so add/remove use the same ref.
    // Lambdas are passed through verbatim — the user took responsibility.
    private static void EmitBoundHandler(ExpressionSyntax handler, StringBuilder sb, EmitContext ctx)
    {
        string? methodName = handler switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax mae when mae.Expression is ThisExpressionSyntax
                => mae.Name.Identifier.Text,
            _ => null,
        };
        if (methodName is not null)
        {
            sb.Append("this._bound('").Append(NameConventions.ToCamelCase(methodName)).Append("')");
            return;
        }
        Emit(handler, sb, ctx);
    }

    private static void EmitLambdaBody(CSharpSyntaxNode body, StringBuilder sb, EmitContext ctx)
    {
        switch (body)
        {
            case ExpressionSyntax exprBody:
                Emit(exprBody, sb, ctx);
                return;
            case BlockSyntax blockBody:
                sb.Append('{');
                // RenderFragment-style lambdas with __builder bodies never reach
                // here (RenderTreeEmitter intercepts them). For user-written
                // block lambdas we delegate to StatementEmitter with a one-space
                // indent to keep the output readable in a single line.
                foreach (var stmt in blockBody.Statements)
                {
                    var inner = new StringBuilder();
                    StatementEmitter.Emit(stmt, inner, ctx, indent: " ");
                    sb.Append(inner.ToString().TrimEnd('\n'));
                }
                sb.Append(" }");
                return;
            default:
                sb.Append("/* TODO: lambda body ").Append(body.Kind()).Append(" */ null");
                return;
        }
    }

    // Emits `{ key1: value1, key2: value2 }` from an ObjectInitializerExpression
    // whose children are assignment expressions. Keys are camel-cased so they
    // match the rest of the transpiler's member naming.
    private static void EmitObjectInitializer(InitializerExpressionSyntax init, StringBuilder sb, EmitContext ctx)
    {
        sb.Append("{ ");
        var first = true;
        foreach (var expr in init.Expressions)
        {
            if (expr is AssignmentExpressionSyntax assign && assign.Left is IdentifierNameSyntax leftId)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(NameConventions.ToCamelCase(leftId.Identifier.Text)).Append(": ");
                Emit(assign.Right, sb, ctx);
            }
        }
        sb.Append(" }");
    }

    private static void EmitIdentifier(string name, StringBuilder sb, EmitContext ctx)
    {
        if (ctx.ClassMembers.Contains(name))
        {
            sb.Append("this.").Append(NameConventions.ToCamelCase(name));
        }
        else
        {
            sb.Append(name);
        }
    }

    /// <summary>
    /// Encode a string as a JS double-quoted literal. Escapes control chars,
    /// quote, and backslash; leaves printable ASCII (including HTML-sensitive
    /// characters like <c>&lt;</c>) untouched so the emitted source stays
    /// readable and stable for snapshot tests.
    /// </summary>
    private static string EncodeJsString(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (c < 32 || c == '\u2028' || c == '\u2029')
                    {
                        sb.Append(System.Globalization.CultureInfo.InvariantCulture,
                            $"\\u{(int)c:X4}");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
