using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Rewrites calls and field accesses on well-known BCL types into their JS
/// equivalents. Without this, expressions like <c>string.Empty</c> or
/// <c>Guid.NewGuid()</c> would emit as literal JS that references undefined
/// globals (<c>string</c>, <c>Guid</c>) and crash at load time.
/// </summary>
/// <remarks>
/// Matching is syntactic (identifier + predefined-type text) rather than
/// semantic. That means a user class happening to be called <c>Guid</c> would
/// also match — acceptable trade-off at M0, and the <see cref="EmitContext"/>
/// is threaded through so a future semantic upgrade stays surgical.
/// </remarks>
internal static class StaticMemberRewrites
{
    /// <summary>
    /// If <paramref name="mae"/> is a bare member access on a known static
    /// type (e.g. <c>string.Empty</c>), write the JS equivalent to
    /// <paramref name="sb"/> and return <c>true</c>. Otherwise return
    /// <c>false</c> so the caller can fall through to generic emission.
    /// </summary>
    public static bool TryRewriteMemberAccess(MemberAccessExpressionSyntax mae, StringBuilder sb, EmitContext ctx)
    {
        if (!TryGetStaticReceiver(mae.Expression, ctx, out var typeName)) return false;
        var memberName = mae.Name.Identifier.Text;

        switch ((typeName, memberName))
        {
            case ("string", "Empty"):
            case ("String", "Empty"):
                sb.Append("\"\"");
                return true;
        }

        return false;
    }

    /// <summary>
    /// If <paramref name="inv"/> is a call to a known static method (e.g.
    /// <c>string.IsNullOrWhiteSpace(x)</c>, <c>Guid.NewGuid()</c>), write the
    /// JS equivalent to <paramref name="sb"/> and return <c>true</c>.
    /// Otherwise return <c>false</c>.
    /// </summary>
    public static bool TryRewriteInvocation(InvocationExpressionSyntax inv, StringBuilder sb, EmitContext ctx)
    {
        if (inv.Expression is not MemberAccessExpressionSyntax mae) return false;

        // LINQ `.Count()` on any receiver → `.length`. Kept syntactic so it
        // also picks up user-defined enumerables whose JS representation is an
        // array. The property form `.Count` (no parens) hits IStore<T>.count
        // directly, which is already correct.
        if (inv.ArgumentList.Arguments.Count == 0 && mae.Name.Identifier.Text == "Count")
        {
            ExpressionEmitter.Emit(mae.Expression, sb, ctx);
            sb.Append(".length");
            return true;
        }

        // `.ToString()` on strings/numbers/guids is a no-op in JS (implicit
        // coercion handles display). Drop the call so `crypto.randomUUID().toString()`
        // collapses cleanly and doesn't invoke JS's Object.prototype.toString.
        if (inv.ArgumentList.Arguments.Count == 0 && mae.Name.Identifier.Text == "ToString")
        {
            ExpressionEmitter.Emit(mae.Expression, sb, ctx);
            return true;
        }

        // Date/time formatting helpers. `DateOnly` and `DateTime` both expose
        // a family of `.ToShortDateString()` / `.ToLongTimeString()` methods.
        // We rewrite via `DateOnly.Parse` → JS `Date`, so these formatters
        // land on a real Date instance. `toLocaleDateString()` is the nearest
        // JS equivalent; the user's browser locale decides formatting, same
        // as .NET's current culture.
        if (inv.ArgumentList.Arguments.Count == 0)
        {
            string? jsMethod = mae.Name.Identifier.Text switch
            {
                "ToShortDateString" => "toLocaleDateString",
                "ToLongDateString" => "toLocaleDateString",
                "ToShortTimeString" => "toLocaleTimeString",
                "ToLongTimeString" => "toLocaleTimeString",
                _ => null,
            };
            if (jsMethod is not null)
            {
                ExpressionEmitter.Emit(mae.Expression, sb, ctx);
                sb.Append('.').Append(jsMethod).Append("()");
                return true;
            }
        }

        if (!TryGetStaticReceiver(mae.Expression, ctx, out var typeName)) return false;
        var memberName = mae.Name.Identifier.Text;
        var args = inv.ArgumentList.Arguments;

        switch ((typeName, memberName))
        {
            case ("string", "IsNullOrWhiteSpace"):
            case ("String", "IsNullOrWhiteSpace"):
                if (args.Count != 1) return false;
                // Route through runtime helper so the argument expression
                // evaluates exactly once — an inline `x == null || x.trim()`
                // would emit the expression twice and run side-effects twice.
                sb.Append("_isNullOrWhiteSpace(");
                ExpressionEmitter.Emit(args[0].Expression, sb, ctx);
                sb.Append(')');
                return true;

            case ("string", "IsNullOrEmpty"):
            case ("String", "IsNullOrEmpty"):
                if (args.Count != 1) return false;
                sb.Append("_isNullOrEmpty(");
                ExpressionEmitter.Emit(args[0].Expression, sb, ctx);
                sb.Append(')');
                return true;

            case ("Guid", "NewGuid"):
                sb.Append("crypto.randomUUID()");
                return true;

            case ("DateOnly", "Parse"):
            case ("DateTime", "Parse"):
            case ("DateTimeOffset", "Parse"):
                // Accepts ISO-8601-ish strings — JS `new Date(x)` handles the
                // same inputs. We drop trailing CultureInfo args; the JS Date
                // constructor is locale-insensitive for parseable ISO input.
                if (args.Count < 1) return false;
                sb.Append("new Date(");
                ExpressionEmitter.Emit(args[0].Expression, sb, ctx);
                sb.Append(')');
                return true;
        }

        return false;
    }

    // Resolves <paramref name="expr"/> to a static-receiver type name.
    // PredefinedTypeSyntax (`string`, `int`, …) returns its keyword directly.
    // For bare identifiers we consult the SemanticModel: match only when the
    // symbol is actually a type, not a user variable. Without the semantic
    // check, code like `MyVar.Count()` where `MyVar` is an instance variable
    // would be silently mis-rewritten by our `.Count()` → `.length` rule.
    private static bool TryGetStaticReceiver(ExpressionSyntax expr, EmitContext ctx, out string typeName)
    {
        if (expr is PredefinedTypeSyntax pt)
        {
            typeName = pt.Keyword.Text;
            return true;
        }
        if (expr is IdentifierNameSyntax id)
        {
            var symbol = ctx.Model.GetSymbolInfo(id).Symbol;
            if (symbol is INamedTypeSymbol nts)
            {
                typeName = nts.Name;
                return true;
            }
            // Fallback for contexts where SemanticModel can't resolve (e.g. a
            // missing reference): use the uppercase-heuristic as a last-resort
            // guess, but only for types we actually rewrite — avoids false
            // positives on arbitrary instance variables. Anything we don't
            // handle ends up in the switch default → no-op.
            if (symbol is null
                && id.Identifier.Text.Length > 0
                && char.IsUpper(id.Identifier.Text[0]))
            {
                typeName = id.Identifier.Text;
                return true;
            }
        }
        typeName = "";
        return false;
    }
}
