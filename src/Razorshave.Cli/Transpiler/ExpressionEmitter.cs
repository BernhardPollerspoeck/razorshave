using System.Text;

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
                Emit(assign.Left, sb, ctx);
                sb.Append(' ').Append(assign.OperatorToken.Text).Append(' ');
                Emit(assign.Right, sb, ctx);
                break;

            case MemberAccessExpressionSyntax mae:
                Emit(mae.Expression, sb, ctx);
                sb.Append('.').Append(NameConventions.ToCamelCase(mae.Name.Identifier.Text));
                break;

            case InvocationExpressionSyntax inv:
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

            case ParenthesizedExpressionSyntax paren:
                sb.Append('(');
                Emit(paren.Expression, sb, ctx);
                sb.Append(')');
                break;

            default:
                sb.Append("/* TODO: ").Append(expr.Kind()).Append(" */ null");
                break;
        }
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
