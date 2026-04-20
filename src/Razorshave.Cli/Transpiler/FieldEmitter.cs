using System.Text;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Emits a single C# field declaration as one or more JS class fields.
/// Initializers are routed through <see cref="ExpressionEmitter"/> so static
/// rewrites (e.g. <c>string.Empty</c> → <c>""</c>) apply uniformly — a raw
/// <c>.ToString()</c> would emit source text that references undefined JS
/// globals and crash at class instantiation.
/// </summary>
/// <remarks>
/// A <see cref="FieldDeclarationSyntax"/> may declare multiple variables
/// (<c>private int a = 0, b = 1;</c>). Each gets its own JS line.
/// Missing initializers emit <c>= null</c> — behavioural parity with C#'s
/// <c>default(T)</c> on reference and nullable types is good enough for M0;
/// a SemanticModel-aware default-for-value-types pass can come later.
/// </remarks>
internal static class FieldEmitter
{
    public static void Emit(FieldDeclarationSyntax field, StringBuilder sb, EmitContext ctx)
    {
        foreach (var variable in field.Declaration.Variables)
        {
            var name = variable.Identifier.Text;

            sb.Append(ClassEmitter.Indent).Append(name).Append(" = ");
            if (variable.Initializer is not null)
            {
                ExpressionEmitter.Emit(variable.Initializer.Value, sb, ctx);
            }
            else
            {
                sb.Append("null");
            }
            sb.Append(";\n");
        }
    }
}
