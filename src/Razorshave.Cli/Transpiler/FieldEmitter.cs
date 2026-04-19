using System.Text;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Emits a single C# field declaration as one or more JS class fields.
/// </summary>
/// <remarks>
/// Scope (5.4):
/// <list type="bullet">
///   <item>A <see cref="FieldDeclarationSyntax"/> may declare multiple
///     variables (<c>private int a = 0, b = 1;</c>). Each gets its own JS line.</item>
///   <item>Initializer present → emit as-is (literal text). Only tested against
///     simple literals so far; complex expressions will need their own walker.</item>
///   <item>No initializer → emit <c>= null</c> for behavioural parity with C#'s
///     default(T) on reference and nullable types. Primitive-default edge cases
///     (e.g. an uninitialised <c>int</c>) are acceptable noise in M0 — revisit
///     when a fixture forces the question.</item>
/// </list>
/// </remarks>
internal static class FieldEmitter
{
    public static void Emit(FieldDeclarationSyntax field, StringBuilder sb)
    {
        foreach (var variable in field.Declaration.Variables)
        {
            var name = variable.Identifier.Text;
            var initializer = variable.Initializer?.Value.ToString() ?? "null";

            sb.Append(ClassEmitter.Indent)
              .Append(name).Append(" = ").Append(initializer).Append(";\n");
        }
    }
}
