using System.Text;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Emits a C# property declaration as a JS class field. Auto-properties with
/// or without an explicit initializer are supported today; getter/setter and
/// expression-bodied forms are deferred.
/// </summary>
/// <remarks>
/// <para>
/// In JavaScript class-field syntax, <c>name = value;</c> covers what a Blazor
/// auto-property needs. We do not emit accessor pairs because Razorshave does
/// not currently leverage them — a future expansion can add a real
/// <c>get/set</c> path when a fixture forces it.
/// </para>
/// <para>
/// Properties flagged with <c>[Inject]</c> are handled by
/// <see cref="ClassEmitter"/> separately — they land in a static
/// <c>_injects</c> manifest so the runtime knows what to resolve, and never
/// reach this emitter.
/// </para>
/// </remarks>
internal static class PropertyEmitter
{
    public static void Emit(PropertyDeclarationSyntax property, StringBuilder sb)
    {
        var name = NameConventions.ToCamelCase(property.Identifier.Text);

        var initializer = property.Initializer?.Value.ToString()
            ?? DefaultForType(property.Type);

        sb.Append(ClassEmitter.Indent)
          .Append(name).Append(" = ").Append(initializer).Append(";\n");
    }

    // No Roslyn type info here — a syntax-only best-effort. Reference-typed
    // fields and nullable value types get null; value-type primitives that are
    // uninitialised keep the null fallback too, which is sloppy but acceptable
    // noise in M0. A later SemanticModel-aware pass can emit proper defaults.
    private static string DefaultForType(TypeSyntax type)
    {
        _ = type;
        return "null";
    }
}
