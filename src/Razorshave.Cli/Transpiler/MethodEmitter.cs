using System.Text;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Emits a C# method declaration as a JS class method.
/// </summary>
/// <remarks>
/// <para>
/// Scope for 5.5: renames <c>PascalCase</c> method identifiers to
/// <c>camelCase</c>, preserves <c>async</c>, drops access modifiers and return
/// types (JS has neither), emits each parameter by name only, and delegates the
/// body to <see cref="StatementEmitter"/>.
/// </para>
/// <para>
/// <c>BuildRenderTree</c> is deliberately skipped: it is rewritten into
/// <c>render()</c> by a separate walker in step 5.6, not emitted as an
/// ordinary method.
/// </para>
/// </remarks>
internal static class MethodEmitter
{
    private const string BodyIndent = "    ";

    public static void Emit(MethodDeclarationSyntax method, StringBuilder sb, EmitContext ctx)
    {
        if (method.Identifier.Text == "BuildRenderTree")
        {
            return;
        }

        var name = NameConventions.ToCamelCase(method.Identifier.Text);
        var isAsync = method.Modifiers.Any(m => m.ValueText == "async");
        var parameters = string.Join(", ", method.ParameterList.Parameters.Select(p => p.Identifier.Text));

        sb.Append(ClassEmitter.Indent);
        if (isAsync) sb.Append("async ");
        sb.Append(name).Append('(').Append(parameters).Append(") {\n");

        if (method.Body is not null)
        {
            foreach (var stmt in method.Body.Statements)
            {
                StatementEmitter.Emit(stmt, sb, ctx, BodyIndent);
            }
        }
        else if (method.ExpressionBody is not null)
        {
            // `=> expr` — emit as `return expr;`
            sb.Append(BodyIndent).Append("return ");
            ExpressionEmitter.Emit(method.ExpressionBody.Expression, sb, ctx);
            sb.Append(";\n");
        }

        sb.Append(ClassEmitter.Indent).Append("}\n");
    }
}
