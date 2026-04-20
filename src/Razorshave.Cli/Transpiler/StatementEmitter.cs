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

            case IfStatementSyntax ifStmt:
                sb.Append(indent).Append("if (");
                ExpressionEmitter.Emit(ifStmt.Condition, sb, ctx);
                sb.Append(") {\n");
                EmitStatementBody(ifStmt.Statement, sb, ctx, indent);
                sb.Append(indent).Append('}');
                if (ifStmt.Else is { } elseClause)
                {
                    sb.Append(" else ");
                    if (elseClause.Statement is IfStatementSyntax)
                    {
                        // chained else-if → emit the sub-if without a wrapper
                        // block so the generated code reads `else if (...) {`.
                        var sub = new StringBuilder();
                        Emit(elseClause.Statement, sub, ctx, indent: "");
                        sb.Append(sub.ToString().TrimStart().TrimEnd('\n'));
                    }
                    else
                    {
                        sb.Append("{\n");
                        EmitStatementBody(elseClause.Statement, sb, ctx, indent);
                        sb.Append(indent).Append('}');
                    }
                }
                sb.Append('\n');
                break;

            case ForStatementSyntax forStmt:
                sb.Append(indent).Append("for (");
                if (forStmt.Declaration is not null)
                {
                    sb.Append("let ");
                    var vars = forStmt.Declaration.Variables;
                    for (var i = 0; i < vars.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(vars[i].Identifier.Text);
                        if (vars[i].Initializer is not null)
                        {
                            sb.Append(" = ");
                            ExpressionEmitter.Emit(vars[i].Initializer!.Value, sb, ctx);
                        }
                    }
                }
                sb.Append("; ");
                if (forStmt.Condition is not null) ExpressionEmitter.Emit(forStmt.Condition, sb, ctx);
                sb.Append("; ");
                for (var i = 0; i < forStmt.Incrementors.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    ExpressionEmitter.Emit(forStmt.Incrementors[i], sb, ctx);
                }
                sb.Append(") {\n");
                EmitStatementBody(forStmt.Statement, sb, ctx, indent);
                sb.Append(indent).Append("}\n");
                break;

            case ForEachStatementSyntax feStmt:
                sb.Append(indent).Append("for (const ").Append(feStmt.Identifier.Text).Append(" of ");
                ExpressionEmitter.Emit(feStmt.Expression, sb, ctx);
                sb.Append(") {\n");
                EmitStatementBody(feStmt.Statement, sb, ctx, indent);
                sb.Append(indent).Append("}\n");
                break;

            default:
                sb.Append(indent).Append("// TODO: unsupported statement '").Append(stmt.Kind()).Append("'\n");
                break;
        }
    }

    // Expands a single statement body with one extra level of indent. Accepts
    // both block and non-block forms so `if (x) y;` emits as
    // `if (x) {\n  y;\n}` without the caller having to peek at Statement kind.
    private static void EmitStatementBody(StatementSyntax body, StringBuilder sb, EmitContext ctx, string parentIndent)
    {
        var inner = parentIndent + "  ";
        if (body is BlockSyntax block)
        {
            foreach (var s in block.Statements) Emit(s, sb, ctx, inner);
        }
        else
        {
            Emit(body, sb, ctx, inner);
        }
    }
}
