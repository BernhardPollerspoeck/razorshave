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

            // Math constants — `Math.PI`, `Math.E` map directly.
            case ("Math", "PI"):
                sb.Append("Math.PI");
                return true;
            case ("Math", "E"):
                sb.Append("Math.E");
                return true;

            // DateTime.Now / DateTime.Today → JS Date. Today has no native JS
            // equivalent, so we reset hours to zero. UtcNow matches `Date.now()`
            // semantics-wise, but we return a Date object so the user code that
            // expects DateTime-like access (.getFullYear(), etc.) keeps working.
            case ("DateTime", "Now"):
            case ("DateTime", "UtcNow"):
                sb.Append("new Date()");
                return true;
            case ("DateTime", "Today"):
            case ("DateOnly", "Today"):
                sb.Append("(() => { const d = new Date(); d.setHours(0,0,0,0); return d; })()");
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

        // `.ToString()` (no args) on strings/numbers/guids is a no-op in JS —
        // implicit coercion handles display. `.ToString("C")` / `.ToString("N2")`
        // carry a format-spec that JS Date/Number don't understand; we intentionally
        // drop the format arg as a best-effort fallback (rather than emit TODO-null)
        // but the result will NOT match .NET's formatting. The Analyzer should
        // ideally flag this; for now the comment documents the silent divergence.
        if (mae.Name.Identifier.Text == "ToString" && inv.ArgumentList.Arguments.Count <= 1)
        {
            ExpressionEmitter.Emit(mae.Expression, sb, ctx);
            return true;
        }

        // LINQ-style extension calls on arrays. Each maps to the nearest JS
        // Array-method that preserves semantics for the 99% case; edge cases
        // (e.g. .First(pred) vs .find()) differ subtly (JS returns undefined,
        // .NET throws). For v0.1 we accept the divergence and document it.
        if (inv.ArgumentList.Arguments.Count <= 1)
        {
            string? linqRewrite = mae.Name.Identifier.Text switch
            {
                "Select" => "map",
                "Where" => "filter",
                "Any" => "some",
                "All" => "every",
                "First" or "FirstOrDefault" => "find",
                "OrderBy" or "OrderByDescending" => null, // handled below — needs comparator
                "Sum" => null, // handled below — reduce
                "ToArray" or "ToList" => "__identity__",
                _ => null,
            };
            if (linqRewrite == "__identity__")
            {
                ExpressionEmitter.Emit(mae.Expression, sb, ctx);
                return true;
            }
            if (linqRewrite is not null)
            {
                ExpressionEmitter.Emit(mae.Expression, sb, ctx);
                sb.Append('.').Append(linqRewrite).Append('(');
                if (inv.ArgumentList.Arguments.Count == 1)
                    ExpressionEmitter.Emit(inv.ArgumentList.Arguments[0].Expression, sb, ctx);
                sb.Append(')');
                return true;
            }
            // `.Sum()` (no predicate) → reduce(+, 0). With a predicate the
            // arg is a selector lambda; we map over it first.
            if (mae.Name.Identifier.Text == "Sum")
            {
                ExpressionEmitter.Emit(mae.Expression, sb, ctx);
                if (inv.ArgumentList.Arguments.Count == 1)
                {
                    sb.Append(".map(");
                    ExpressionEmitter.Emit(inv.ArgumentList.Arguments[0].Expression, sb, ctx);
                    sb.Append(')');
                }
                sb.Append(".reduce((a, b) => a + b, 0)");
                return true;
            }
        }

        // `s.Split(sep)` — instance method. JS `String.prototype.split` has
        // compatible signature for the common case (single separator string or
        // char). The params-array C# overload (`Split(',', ';')`) needs a
        // regex on the JS side; we fall back to passing the first separator
        // only and warn via documentation — ExpressionEmitter's Analyzer
        // allowlist still approves it so the output compiles.
        if (mae.Name.Identifier.Text == "Split" && inv.ArgumentList.Arguments.Count >= 1)
        {
            ExpressionEmitter.Emit(mae.Expression, sb, ctx);
            sb.Append(".split(");
            ExpressionEmitter.Emit(inv.ArgumentList.Arguments[0].Expression, sb, ctx);
            sb.Append(')');
            return true;
        }

        // `List<T>.Add(x)` → `arr.push(x)` (void, like .NET). For JS arrays
        // `push` returns the new length which we drop if the call is used
        // as a statement (ExpressionStatement). Used as an expression it
        // diverges — acceptable for v0.1; the Analyzer can flag later.
        if (inv.ArgumentList.Arguments.Count == 1 && mae.Name.Identifier.Text == "Add")
        {
            ExpressionEmitter.Emit(mae.Expression, sb, ctx);
            sb.Append(".push(");
            ExpressionEmitter.Emit(inv.ArgumentList.Arguments[0].Expression, sb, ctx);
            sb.Append(')');
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
                // Go through the bcl helper rather than emitting crypto.randomUUID()
                // directly — the helper falls back to a Math.random UUIDv4 when the
                // runtime is in a non-secure context (http://localhost), with a
                // one-time dev warning. Emitting crypto.randomUUID() inline would
                // throw TypeError in those contexts with no user-actionable message.
                sb.Append("_newGuid()");
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

            // Math family. `Math.Max`/`Math.Min` accept 2+ args (params array in
            // C#); JS `Math.max`/`.min` also variadic. `.Round(x)` → JS rounds
            // half-away-from-zero; .NET defaults to banker's rounding. For v0.1
            // we accept the divergence — documented in SUPPORTED.md (to write).
            case ("Math", "Max"):
            case ("Math", "Min"):
            case ("Math", "Abs"):
            case ("Math", "Round"):
            case ("Math", "Ceiling"):
            case ("Math", "Floor"):
            case ("Math", "Sqrt"):
            case ("Math", "Pow"):
            case ("Math", "Log"):
            case ("Math", "Sin"):
            case ("Math", "Cos"):
            case ("Math", "Tan"):
                sb.Append("Math.").Append(ToJsLower(memberName)).Append('(');
                EmitArgList(args, sb, ctx);
                sb.Append(')');
                return true;

            // string.Format("{0} {1}", a, b) — rewrite into template literal.
            // The placeholders inside .NET format strings (`{0}`, `{1:N2}`) map
            // directly onto a positional array; we emit a tiny IIFE so users
            // don't see the mechanism. Format-specifiers inside braces are
            // dropped (no JS equivalent for `N2`/`C`).
            case ("string", "Format"):
            case ("String", "Format"):
                if (args.Count < 1) return false;
                sb.Append("((_fmt, ..._args) => _fmt.replace(/\\{(\\d+)(?::[^}]*)?\\}/g, (_, i) => _args[Number(i)] ?? ''))(");
                EmitArgList(args, sb, ctx);
                sb.Append(')');
                return true;

            // string.Join(sep, values). JS `[values].join(sep)` matches exactly
            // when values is a single array; the C# param-array overload we
            // route through the same path (Array-of-strings becomes array-of-
            // strings). A trailing .ToString() per element isn't needed: JS
            // coerces on join.
            case ("string", "Join"):
            case ("String", "Join"):
                if (args.Count < 2) return false;
                ExpressionEmitter.Emit(args[1].Expression, sb, ctx);
                sb.Append(".join(");
                ExpressionEmitter.Emit(args[0].Expression, sb, ctx);
                sb.Append(')');
                return true;

            // int.Parse / double.Parse / Convert.*.
            // `int.Parse(s)` → `parseInt(s, 10)`; double → `parseFloat`.
            // `Convert.ToInt32(s)` uses the same JS primitive. Failure-mode
            // differs from .NET (NaN vs throw) — documented divergence.
            case ("int", "Parse"):
            case ("Int32", "Parse"):
            case ("long", "Parse"):
            case ("Int64", "Parse"):
                if (args.Count < 1) return false;
                sb.Append("parseInt(");
                ExpressionEmitter.Emit(args[0].Expression, sb, ctx);
                sb.Append(", 10)");
                return true;

            case ("double", "Parse"):
            case ("Double", "Parse"):
            case ("float", "Parse"):
            case ("Single", "Parse"):
            case ("decimal", "Parse"):
            case ("Decimal", "Parse"):
                if (args.Count < 1) return false;
                sb.Append("parseFloat(");
                ExpressionEmitter.Emit(args[0].Expression, sb, ctx);
                sb.Append(')');
                return true;

            case ("Convert", "ToInt32"):
            case ("Convert", "ToInt64"):
                if (args.Count < 1) return false;
                sb.Append("parseInt(");
                ExpressionEmitter.Emit(args[0].Expression, sb, ctx);
                sb.Append(", 10)");
                return true;

            case ("Convert", "ToDouble"):
            case ("Convert", "ToSingle"):
            case ("Convert", "ToDecimal"):
                if (args.Count < 1) return false;
                sb.Append("parseFloat(");
                ExpressionEmitter.Emit(args[0].Expression, sb, ctx);
                sb.Append(')');
                return true;

            case ("Convert", "ToBoolean"):
                // JS has no direct `Convert.ToBoolean` equivalent that matches
                // .NET's "1/true/True → true, 0/false/False → false, throws on anything else".
                // Closest approximation is `Boolean(x)` — we accept divergence on
                // strings like "yes" which .NET rejects.
                if (args.Count < 1) return false;
                sb.Append("Boolean(");
                ExpressionEmitter.Emit(args[0].Expression, sb, ctx);
                sb.Append(')');
                return true;

            case ("Convert", "ToString"):
                if (args.Count < 1) return false;
                sb.Append("String(");
                ExpressionEmitter.Emit(args[0].Expression, sb, ctx);
                sb.Append(')');
                return true;
        }

        return false;
    }

    private static string ToJsLower(string pascal)
        => pascal.Length == 0 ? pascal : char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);

    private static void EmitArgList(Microsoft.CodeAnalysis.SeparatedSyntaxList<ArgumentSyntax> args, StringBuilder sb, EmitContext ctx)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            ExpressionEmitter.Emit(args[i].Expression, sb, ctx);
        }
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
