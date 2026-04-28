using System.Text;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Emits a single C# statement as JavaScript, honouring the indent of its
/// enclosing scope.
/// </summary>
/// <remarks>
/// Supported kinds match <c>SupportedSyntax.Statements</c> in the analyzer
/// allowlist; anything outside that set throws a
/// <see cref="TranspilerException"/> so the build scheppert instead of
/// emitting silent placeholder JavaScript. The analyzer (RZS2002) is the
/// primary gate — the throw is the last-line safety net for cases where
/// the analyzer has a gap.
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
            {
                var loopVars = forStmt.Declaration?.Variables
                    .Select(v => v.Identifier.Text).ToArray() ?? [];
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
                // Condition + incrementors read the loop variable as a bare
                // identifier; push its scope so `this.i` doesn't get emitted
                // if a class member happens to be named `i`.
                ctx.PushLocalScope(loopVars);
                try
                {
                    if (forStmt.Condition is not null) ExpressionEmitter.Emit(forStmt.Condition, sb, ctx);
                    sb.Append("; ");
                    for (var i = 0; i < forStmt.Incrementors.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        ExpressionEmitter.Emit(forStmt.Incrementors[i], sb, ctx);
                    }
                    sb.Append(") {\n");
                    EmitStatementBody(forStmt.Statement, sb, ctx, indent);
                }
                finally { ctx.PopLocalScope(); }
                sb.Append(indent).Append("}\n");
                break;
            }

            case ForEachStatementSyntax feStmt:
            {
                var iterVar = feStmt.Identifier.Text;
                sb.Append(indent).Append("for (const ").Append(iterVar).Append(" of ");
                ExpressionEmitter.Emit(feStmt.Expression, sb, ctx);
                sb.Append(") {\n");
                ctx.PushLocalScope([iterVar]);
                try { EmitStatementBody(feStmt.Statement, sb, ctx, indent); }
                finally { ctx.PopLocalScope(); }
                sb.Append(indent).Append("}\n");
                break;
            }

            case TryStatementSyntax tryStmt:
                EmitTry(tryStmt, sb, ctx, indent);
                break;

            case ThrowStatementSyntax throwStmt:
                sb.Append(indent).Append("throw");
                if (throwStmt.Expression is not null)
                {
                    sb.Append(' ');
                    ExpressionEmitter.Emit(throwStmt.Expression, sb, ctx);
                }
                else if (ctx.TryPeekCatchIdentifier(out var caughtId))
                {
                    // Bare `throw;` in a catch block — re-throw the JS exception
                    // captured by the enclosing `catch (__e) { ... }` wrapper.
                    sb.Append(' ').Append(caughtId);
                }
                // A bare `throw;` outside any catch is a C# compile error
                // (CS0156) — Roslyn reports it before us, so we emit `throw;`
                // verbatim and let the prior diagnostic carry the message.
                sb.Append(";\n");
                break;

            default:
                throw TranspilerException.Unsupported(stmt,
                    $"the C# statement '{stmt.Kind()}' (the analyzer should have caught this before transpile)");
        }
    }

    // Emits a try / catch / finally block. JavaScript only allows a single
    // catch clause, so multiple C# catches collapse into one outer
    // `catch (__e) { ... }` plus an if/else-if chain that filters by
    // `__e instanceof <Type>` (and the optional `when (...)` filter).
    //
    // `catch (Exception)` and `catch (System.Exception)` deliberately drop
    // the instanceof test: in C# they catch every throw, including thrown
    // strings or POJOs — emitting `instanceof Exception` would falsely
    // exclude them from the JS-side handler.
    private static void EmitTry(TryStatementSyntax tryStmt, StringBuilder sb, EmitContext ctx, string indent)
    {
        sb.Append(indent).Append("try {\n");
        var inner = indent + "  ";
        foreach (var s in tryStmt.Block.Statements) Emit(s, sb, ctx, inner);
        sb.Append(indent).Append('}');

        if (tryStmt.Catches.Count > 0)
        {
            const string CaughtParam = "__e";
            sb.Append(" catch (").Append(CaughtParam).Append(") {\n");
            EmitCatchChain(tryStmt.Catches, CaughtParam, sb, ctx, inner);
            sb.Append(indent).Append('}');
        }

        if (tryStmt.Finally is { } fin)
        {
            sb.Append(" finally {\n");
            foreach (var s in fin.Block.Statements) Emit(s, sb, ctx, inner);
            sb.Append(indent).Append('}');
        }

        sb.Append('\n');
    }

    private static void EmitCatchChain(
        IReadOnlyList<CatchClauseSyntax> catches,
        string caughtParam,
        StringBuilder sb,
        EmitContext ctx,
        string indent)
    {
        // Bare `catch { ... }` (no declaration, no filter) is the catch-all.
        // Emit it last as the trailing `else` so other typed catches keep
        // their priority order from the C# source.
        var bareCatch = catches.FirstOrDefault(c => c.Declaration is null && c.Filter is null);

        var first = true;
        foreach (var clause in catches)
        {
            if (clause == bareCatch) continue;

            string? typeFilter = null;
            string? boundName = null;

            if (clause.Declaration is { } decl)
            {
                var typeName = NameConventions.StripQualifiers(decl.Type.ToString());
                if (typeName != "Exception")
                {
                    typeFilter = $"{caughtParam} instanceof {typeName}";
                }
                if (decl.Identifier.ValueText.Length > 0)
                {
                    boundName = decl.Identifier.ValueText;
                }
            }

            sb.Append(indent).Append(first ? "if (" : "else if (");
            first = false;
            sb.Append(typeFilter ?? "true").Append(") {\n");

            var body = indent + "  ";
            if (boundName is not null)
            {
                sb.Append(body).Append("let ").Append(boundName)
                  .Append(" = ").Append(caughtParam).Append(";\n");
            }
            if (clause.Filter is { } filter)
            {
                // The when-clause runs after the binding so it can reference
                // the named exception. A failing filter re-throws so a later
                // typed catch (or an outer try) can take over — that's the
                // C# semantics, not "fall through into the next handler".
                sb.Append(body).Append("if (!(");
                ExpressionEmitter.Emit(filter.FilterExpression, sb, ctx);
                sb.Append(")) throw ").Append(caughtParam).Append(";\n");
            }

            var pushedScope = false;
            if (boundName is not null)
            {
                ctx.PushLocalScope([boundName]);
                pushedScope = true;
            }
            ctx.PushCatchIdentifier(caughtParam);
            try
            {
                foreach (var s in clause.Block.Statements) Emit(s, sb, ctx, body);
            }
            finally
            {
                ctx.PopCatchIdentifier();
                if (pushedScope) ctx.PopLocalScope();
            }
            sb.Append(indent).Append("}\n");
        }

        if (bareCatch is not null)
        {
            ctx.PushCatchIdentifier(caughtParam);
            try
            {
                if (first)
                {
                    // Only catch is bare — no wrapping if/else, just emit the
                    // body directly inside the outer `catch (__e) { ... }`.
                    foreach (var s in bareCatch.Block.Statements) Emit(s, sb, ctx, indent);
                }
                else
                {
                    sb.Append(indent).Append("else {\n");
                    var body = indent + "  ";
                    foreach (var s in bareCatch.Block.Statements) Emit(s, sb, ctx, body);
                    sb.Append(indent).Append("}\n");
                }
            }
            finally { ctx.PopCatchIdentifier(); }
        }
        else if (!first)
        {
            // No catch-all — re-throw if no typed handler matched, so an
            // outer try (or the runtime) sees the original exception.
            sb.Append(indent).Append("else { throw ").Append(caughtParam).Append("; }\n");
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
