using System.Text;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Emits a single C# statement as JavaScript, honouring the indent of its
/// enclosing scope.
/// </summary>
/// <remarks>
/// Scope for 5.5: <c>ExpressionStatement</c>, <c>LocalDeclarationStatement</c>,
/// <c>ReturnStatement</c>, and <c>Block</c>. Everything else emits a
/// <c>// TODO: &lt;Kind&gt;</c> line so snapshots stay readable.
/// </remarks>
internal static class StatementEmitter
{
    public static void Emit(StatementSyntax stmt, StringBuilder sb, EmitContext ctx, string indent)
    {
        switch (stmt)
        {
            case ExpressionStatementSyntax es:
                sb.Append(indent);
                ExpressionEmitter.Emit(es.Expression, sb, ctx);
                sb.Append(";\n");
                break;

            case LocalDeclarationStatementSyntax local:
                foreach (var v in local.Declaration.Variables)
                {
                    sb.Append(indent).Append("let ").Append(v.Identifier.Text);
                    if (v.Initializer is not null)
                    {
                        sb.Append(" = ");
                        ExpressionEmitter.Emit(v.Initializer.Value, sb, ctx);
                    }
                    sb.Append(";\n");
                }
                break;

            case ReturnStatementSyntax ret:
                sb.Append(indent).Append("return");
                if (ret.Expression is not null)
                {
                    sb.Append(' ');
                    ExpressionEmitter.Emit(ret.Expression, sb, ctx);
                }
                sb.Append(";\n");
                break;

            case BlockSyntax block:
                foreach (var inner in block.Statements)
                {
                    Emit(inner, sb, ctx, indent);
                }
                break;

            default:
                sb.Append(indent).Append("// TODO: unsupported statement '").Append(stmt.Kind()).Append("'\n");
                break;
        }
    }
}
