using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
                // representation verbatim. The bare `default` keyword is its
                // own LiteralExpression kind — it has no JS counterpart, but
                // "kein Wert" → `null` matches C#-reference-type default and
                // is the right fallback for value-types bound to nullable JS.
                if (lit.IsKind(SyntaxKind.DefaultLiteralExpression))
                {
                    sb.Append("null");
                }
                else if (lit.Token.Value is string s)
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

            case GenericNameSyntax gen:
                // `Get<TResponse>(url)` — drop the type-arg list, emit the
                // name the same way a non-generic identifier would. Generic
                // inference happens at C# compile time; by the time we hit JS
                // there's no runtime type to preserve.
                EmitIdentifier(gen.Identifier.Text, sb, ctx);
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
                sb.Append(' ').Append(MapBinaryOperator(bin)).Append(' ');
                Emit(bin.Right, sb, ctx);
                break;

            case IsPatternExpressionSyntax isPat:
                EmitIsPattern(isPat, sb, ctx);
                break;

            case ConditionalAccessExpressionSyntax cond:
                // `a?.b.c` → `a?.b.c`. JS's optional-chaining `?.` matches
                // C#'s null-propagation: short-circuit to undefined when the
                // receiver is null. The `?` prefix is emitted here; the
                // MemberBinding / ElementBinding inside WhenNotNull supplies
                // the `.b` / `[i]` continuation via their own cases below.
                Emit(cond.Expression, sb, ctx);
                sb.Append('?');
                Emit(cond.WhenNotNull, sb, ctx);
                break;

            case MemberBindingExpressionSyntax mb:
                // Only reached from inside a ConditionalAccess.WhenNotNull
                // (standalone is a syntax error in C#). The leading `?` was
                // already written by the ConditionalAccess case; here we
                // just add `.name` — routed through ToCamelCase so property
                // conventions (Length → length, Name → name) stay consistent
                // with the plain member-access path.
                sb.Append('.').Append(NameConventions.ToCamelCase(mb.Name.Identifier.Text));
                break;

            case ElementBindingExpressionSyntax eb:
                // Same story as MemberBinding but for indexers (`?[i]`).
                sb.Append('[');
                if (eb.ArgumentList.Arguments.Count > 0)
                    Emit(eb.ArgumentList.Arguments[0].Expression, sb, ctx);
                sb.Append(']');
                break;

            case TypeOfExpressionSyntax tof:
                // No direct JS equivalent — user code that reaches `typeof(T)`
                // usually wants a type identity string. Emit the stripped
                // type name as a quoted literal so equality checks against
                // other typeof() results still work. Not a true Type — enough
                // for Blazor's route-and-parameter-typing conventions.
                sb.Append(EncodeJsString(NameConventions.StripQualifiers(tof.Type.ToString())));
                break;

            case DefaultExpressionSyntax:
                // `default(T)` — we don't look up T. Null is the correct
                // default for reference types and for C# value types that the
                // user ends up comparing loosely (0 == null is false, null ==
                // null is true); the small number of places where the user
                // needs the actual 0 / false / "" will have to be rewritten
                // explicitly. The Analyzer keeps this on the allowlist so
                // the compile doesn't lie about what shipped.
                sb.Append("null");
                break;

            case AssignmentExpressionSyntax assign:
                if (TryEmitEventSubscription(assign, sb, ctx)) break;
                Emit(assign.Left, sb, ctx);
                sb.Append(' ').Append(assign.OperatorToken.Text).Append(' ');
                Emit(assign.Right, sb, ctx);
                break;

            case MemberAccessExpressionSyntax mae:
                if (StaticMemberRewrites.TryRewriteMemberAccess(mae, sb, ctx)) break;
                Emit(mae.Expression, sb, ctx);
                sb.Append('.').Append(ResolveMemberName(mae, ctx));
                break;

            case InvocationExpressionSyntax inv:
                // `nameof(x)` is a compile-time string in C#; JS has no
                // counterpart. Emit the target's simple name as a string
                // literal — the common use-case is logging, and it matches
                // what Roslyn would produce.
                if (inv.Expression is IdentifierNameSyntax nameofId
                    && nameofId.Identifier.Text == "nameof"
                    && inv.ArgumentList.Arguments.Count == 1)
                {
                    var argText = inv.ArgumentList.Arguments[0].Expression.ToString();
                    var simple = NameConventions.StripQualifiers(argText);
                    var dot = simple.LastIndexOf('.');
                    sb.Append(EncodeJsString(dot >= 0 ? simple.Substring(dot + 1) : simple));
                    break;
                }
                if (StaticMemberRewrites.TryRewriteInvocation(inv, sb, ctx)) break;
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

            case SimpleLambdaExpressionSyntax simple:
                // `x => expr` → `(x) => expr`. JS arrow functions are a clean
                // 1:1 mapping for C# lambdas; only block bodies need special
                // handling (StatementEmitter), and those only appear in
                // RenderFragment delegates which have their own emitter path.
                {
                    var param = simple.Parameter.Identifier.Text;
                    sb.Append('(').Append(param).Append(") => ");
                    ctx.PushLocalScope([param]);
                    try { EmitLambdaBody(simple.Body, sb, ctx); }
                    finally { ctx.PopLocalScope(); }
                }
                break;

            case ParenthesizedLambdaExpressionSyntax paren2:
                {
                    var parms = paren2.ParameterList.Parameters
                        .Select(p => p.Identifier.Text).ToArray();
                    sb.Append('(');
                    for (var i = 0; i < parms.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(parms[i]);
                    }
                    sb.Append(") => ");
                    ctx.PushLocalScope(parms);
                    try { EmitLambdaBody(paren2.Body, sb, ctx); }
                    finally { ctx.PopLocalScope(); }
                }
                break;

            case ParenthesizedExpressionSyntax paren:
                sb.Append('(');
                Emit(paren.Expression, sb, ctx);
                sb.Append(')');
                break;

            case ConditionalExpressionSyntax cond:
                // C# and JS ternary share exact syntax — direct one-to-one
                // mapping. Each operand still goes through Emit so nested
                // rewrites (static members, object initialisers) apply.
                Emit(cond.Condition, sb, ctx);
                sb.Append(" ? ");
                Emit(cond.WhenTrue, sb, ctx);
                sb.Append(" : ");
                Emit(cond.WhenFalse, sb, ctx);
                break;

            case ObjectCreationExpressionSyntax oc:
                // `new TodoItem { Id = x, Text = y }` → `{ id: x, text: y }`.
                // We intentionally drop the type name — records and POCO-style
                // classes emit as plain JS object literals at runtime. Keys
                // match the C# property names, camel-cased so they line up
                // with how the rest of the emitter renames identifiers.
                // Positional `new TodoItem(x, y)` is not supported here yet
                // — records in M0 use object initialisers.
                if (oc.Initializer is not null)
                {
                    EmitObjectInitializer(oc.Initializer, sb, ctx);
                }
                else
                {
                    sb.Append("{}");
                }
                break;

            case ImplicitObjectCreationExpressionSyntax implicitOc:
                // `new() { Id = x }` — the target type is implicit, emission
                // is identical to the explicit form.
                if (implicitOc.Initializer is not null)
                {
                    EmitObjectInitializer(implicitOc.Initializer, sb, ctx);
                }
                else
                {
                    sb.Append("{}");
                }
                break;

            case ElementAccessExpressionSyntax ea:
                // `arr[i]` → `arr[i]`. Indexers on dictionaries, strings, and
                // custom indexer properties map the same way; user-defined
                // indexers that do something clever will break, but that's
                // out of M0 scope.
                Emit(ea.Expression, sb, ctx);
                sb.Append('[');
                for (var i = 0; i < ea.ArgumentList.Arguments.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    Emit(ea.ArgumentList.Arguments[i].Expression, sb, ctx);
                }
                sb.Append(']');
                break;

            case CastExpressionSyntax cast:
                // JS has no explicit cast — preserve the value. For integer
                // narrowing casts we wrap in Math.trunc so `(int)1.9 === 1`
                // matches .NET semantics. Floating-point and widening casts
                // are already no-ops.
                var castType = cast.Type.ToString();
                // Integer narrowing: wrap in Math.trunc so `(int)1.9 === 1`
                // matches .NET semantics. `char` is an integer in C# (UTF-16
                // code unit); `nint`/`nuint` are the native-width variants.
                if (castType is "int" or "long" or "short" or "byte" or "sbyte"
                             or "uint" or "ulong" or "ushort" or "nint" or "nuint"
                             or "char")
                {
                    sb.Append("Math.trunc(");
                    Emit(cast.Expression, sb, ctx);
                    sb.Append(')');
                }
                else
                {
                    Emit(cast.Expression, sb, ctx);
                }
                break;

            case ArrayCreationExpressionSyntax arr:
                // `new T[n]` → `new Array(n)`. The Rank-specifier's size
                // argument becomes the JS Array constructor arg. `new T[]`
                // with no size → empty array.
                if (arr.Type.RankSpecifiers.Count > 0 && arr.Type.RankSpecifiers[0].Sizes.Count > 0
                    && arr.Type.RankSpecifiers[0].Sizes[0] is not OmittedArraySizeExpressionSyntax)
                {
                    sb.Append("new Array(");
                    Emit(arr.Type.RankSpecifiers[0].Sizes[0], sb, ctx);
                    sb.Append(')');
                }
                else if (arr.Initializer is not null)
                {
                    sb.Append('[');
                    for (var i = 0; i < arr.Initializer.Expressions.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        Emit(arr.Initializer.Expressions[i], sb, ctx);
                    }
                    sb.Append(']');
                }
                else
                {
                    sb.Append("[]");
                }
                break;

            case ImplicitArrayCreationExpressionSyntax implicitArr:
                // `new[] { 1, 2, 3 }` → `[1, 2, 3]`.
                sb.Append('[');
                for (var i = 0; i < implicitArr.Initializer.Expressions.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    Emit(implicitArr.Initializer.Expressions[i], sb, ctx);
                }
                sb.Append(']');
                break;

            case CollectionExpressionSyntax coll:
                // `[1, 2, 3]` C# collection literal → same in JS.
                sb.Append('[');
                for (var i = 0; i < coll.Elements.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    if (coll.Elements[i] is ExpressionElementSyntax ee)
                    {
                        Emit(ee.Expression, sb, ctx);
                    }
                }
                sb.Append(']');
                break;

            case InterpolatedStringExpressionSyntax interp:
                // C# `$"hello {x}"` → template literal `` `hello ${x}` ``.
                sb.Append('`');
                foreach (var content in interp.Contents)
                {
                    switch (content)
                    {
                        case InterpolatedStringTextSyntax txt:
                            // Escape ` and ${ so content doesn't close the literal early.
                            sb.Append(txt.TextToken.ValueText
                                .Replace("\\", "\\\\")
                                .Replace("`", "\\`")
                                .Replace("${", "\\${"));
                            break;
                        case InterpolationSyntax interpHole:
                            sb.Append("${");
                            Emit(interpHole.Expression, sb, ctx);
                            sb.Append('}');
                            break;
                    }
                }
                sb.Append('`');
                break;

            case SwitchExpressionSyntax swe:
                EmitSwitchExpression(swe, sb, ctx);
                break;

            case WithExpressionSyntax with:
                // `record with { Done = !x.Done }` → `{ ...record, done: !x.done }`.
                // The spread preserves all existing keys, then the initialiser
                // overwrites the ones the user listed.
                sb.Append("{ ...");
                Emit(with.Expression, sb, ctx);
                foreach (var assignExpr in with.Initializer.Expressions)
                {
                    if (assignExpr is AssignmentExpressionSyntax a && a.Left is IdentifierNameSyntax leftId)
                    {
                        sb.Append(", ").Append(NameConventions.ToCamelCase(leftId.Identifier.Text)).Append(": ");
                        Emit(a.Right, sb, ctx);
                    }
                }
                sb.Append(" }");
                break;

            default:
                sb.Append("/* TODO: ").Append(expr.Kind()).Append(" */ null");
                break;
        }
    }

    // `Store.OnChange += StateHasChanged` / `Store.OnChange -= StateHasChanged`
    // rewrites via the runtime's `_bound(name)` helper. We use SemanticModel
    // to confirm the LHS's member is an `event` so ordinary += assignments
    // (e.g. `counter += 1`) keep their native JS form. The same bound handler
    // reference is used on +/−, so unsubscribe actually cancels the subscribe.
    private static bool TryEmitEventSubscription(AssignmentExpressionSyntax assign, StringBuilder sb, EmitContext ctx)
    {
        var kind = assign.Kind();
        if (kind != SyntaxKind.AddAssignmentExpression && kind != SyntaxKind.SubtractAssignmentExpression)
        {
            return false;
        }
        if (assign.Left is not MemberAccessExpressionSyntax mae) return false;

        if (ctx.Model.GetSymbolInfo(mae).Symbol is not Microsoft.CodeAnalysis.IEventSymbol eventSym)
        {
            return false;
        }

        // `OnChange` → subscribe `onChange` / unsubscribe `offChange`. The
        // event naming convention (leading `On`) lets us form a natural
        // off-pair by swapping the prefix. Without the `On` prefix we fall
        // back to prepending raw `off`.
        var eventName = eventSym.Name;
        string subscribe, unsubscribe;
        if (eventName.StartsWith("On", StringComparison.Ordinal) && eventName.Length > 2)
        {
            subscribe = NameConventions.ToCamelCase(eventName);
            unsubscribe = "off" + eventName[2..];
        }
        else
        {
            subscribe = NameConventions.ToCamelCase(eventName);
            unsubscribe = "off" + eventName;
        }

        Emit(mae.Expression, sb, ctx);
        sb.Append('.').Append(kind == SyntaxKind.AddAssignmentExpression ? subscribe : unsubscribe).Append('(');
        EmitBoundHandler(assign.Right, sb, ctx);
        sb.Append(')');
        return true;
    }

    // Normalises a handler reference (`StateHasChanged` or `this.StateHasChanged`)
    // into `this._bound('stateHasChanged')` so add/remove use the same ref.
    // Lambdas are passed through verbatim — the user took responsibility.
    private static void EmitBoundHandler(ExpressionSyntax handler, StringBuilder sb, EmitContext ctx)
    {
        string? methodName = handler switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax mae when mae.Expression is ThisExpressionSyntax
                => mae.Name.Identifier.Text,
            _ => null,
        };
        if (methodName is not null)
        {
            sb.Append("this._bound('").Append(NameConventions.ToCamelCase(methodName)).Append("')");
            return;
        }
        Emit(handler, sb, ctx);
    }

    // `expr switch { pattern => result, _ => default }` → nested ternary.
    // Supports constant patterns (`5`), relational patterns (`< 10`, `>= 0`),
    // and the discard (`_`) as final arm. Anything fancier (type patterns,
    // tuple patterns, property patterns) falls through as TODO-null.
    private static void EmitSwitchExpression(SwitchExpressionSyntax swe, StringBuilder sb, EmitContext ctx)
    {
        var arms = swe.Arms;
        if (arms.Count == 0) { sb.Append("undefined"); return; }

        var subject = new StringBuilder();
        Emit(swe.GoverningExpression, subject, ctx);
        var subjectJs = subject.ToString();

        for (var i = 0; i < arms.Count; i++)
        {
            var arm = arms[i];
            var last = i == arms.Count - 1;
            var condJs = SwitchPatternCondition(arm.Pattern, arm.WhenClause, subjectJs, ctx);

            if (condJs is null)
            {
                // Discard / irrefutable pattern — emit as the fallthrough value.
                Emit(arm.Expression, sb, ctx);
                return;
            }

            sb.Append(condJs).Append(" ? ");
            Emit(arm.Expression, sb, ctx);
            sb.Append(" : ");
            if (last)
            {
                // No default arm (no `_`) — JS fallthrough is undefined.
                sb.Append("undefined");
            }
        }
    }

    // Build a JS boolean expression that tests whether `subject` matches the
    // given C# pattern. Returns null when the pattern is irrefutable (the
    // discard `_` or a bare `var x` binding — last-arm catch-all).
    private static string? SwitchPatternCondition(PatternSyntax pattern, WhenClauseSyntax? when, string subjectJs, EmitContext ctx)
    {
        string? core = pattern switch
        {
            DiscardPatternSyntax => null,
            VarPatternSyntax => null,
            ConstantPatternSyntax cp => BuildConstantMatch(subjectJs, cp.Expression, ctx),
            RelationalPatternSyntax rp => BuildRelationalMatch(subjectJs, rp, ctx),
            _ => "false /* TODO: unsupported switch pattern */",
        };

        if (when is null) return core;
        var whenSb = new StringBuilder();
        Emit(when.Condition, whenSb, ctx);
        return core is null ? $"({whenSb})" : $"({core} && {whenSb})";
    }

    private static string BuildConstantMatch(string subjectJs, ExpressionSyntax constExpr, EmitContext ctx)
    {
        var value = new StringBuilder();
        Emit(constExpr, value, ctx);
        return $"{subjectJs} === {value}";
    }

    private static string BuildRelationalMatch(string subjectJs, RelationalPatternSyntax rp, EmitContext ctx)
    {
        var op = rp.OperatorToken.Text;
        var value = new StringBuilder();
        Emit(rp.Expression, value, ctx);
        return $"{subjectJs} {op} {value}";
    }

    // Maps a C# binary operator to its JS equivalent.
    // Equality (`==`/`!=`) promotes to strict (`===`/`!==`) to avoid JS type
    // coercion — `"5" == 5` is true in JS but a compile error in C#. The one
    // exception is null-comparisons, where loose (`==`/`!=`) is the better
    // match: C# has no `undefined`, so "kein Wert" in C# should also catch
    // the JS-side `undefined` that creeps in from optional fields.
    private static string MapBinaryOperator(BinaryExpressionSyntax bin)
    {
        var kind = bin.Kind();
        var isEq = kind == SyntaxKind.EqualsExpression;
        var isNeq = kind == SyntaxKind.NotEqualsExpression;
        if (!isEq && !isNeq) return bin.OperatorToken.Text;
        if (IsNullLiteral(bin.Left) || IsNullLiteral(bin.Right)) return bin.OperatorToken.Text;
        return isEq ? "===" : "!==";
    }

    private static bool IsNullLiteral(ExpressionSyntax expr)
        => expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression);


    // Emits an `is`-pattern expression. Only `is null` and `is not null` are
    // supported — they map to JS `== null` / `!= null` (loose, matches C#
    // "kein Wert" semantics). Other patterns (type, declaration, property)
    // fall through as TODO-null until we add proper emitters; the analyzer
    // (RZS2001) keeps them from slipping into production silently.
    private static void EmitIsPattern(IsPatternExpressionSyntax isPat, StringBuilder sb, EmitContext ctx)
    {
        switch (isPat.Pattern)
        {
            case ConstantPatternSyntax cp when IsNullLiteral(cp.Expression):
                Emit(isPat.Expression, sb, ctx);
                sb.Append(" == null");
                return;
            case UnaryPatternSyntax { Pattern: ConstantPatternSyntax cp } up
                when up.OperatorToken.IsKind(SyntaxKind.NotKeyword) && IsNullLiteral(cp.Expression):
                Emit(isPat.Expression, sb, ctx);
                sb.Append(" != null");
                return;
            default:
                sb.Append("/* TODO: unsupported is-pattern ").Append(isPat.Pattern.Kind()).Append(" */ null");
                return;
        }
    }

    private static void EmitLambdaBody(CSharpSyntaxNode body, StringBuilder sb, EmitContext ctx)
    {
        switch (body)
        {
            case ExpressionSyntax exprBody:
                Emit(exprBody, sb, ctx);
                return;
            case BlockSyntax blockBody:
                sb.Append('{');
                // RenderFragment-style lambdas with __builder bodies never reach
                // here (RenderTreeEmitter intercepts them). For user-written
                // block lambdas we delegate to StatementEmitter with a one-space
                // indent to keep the output readable in a single line.
                foreach (var stmt in blockBody.Statements)
                {
                    var inner = new StringBuilder();
                    StatementEmitter.Emit(stmt, inner, ctx, indent: " ");
                    sb.Append(inner.ToString().TrimEnd('\n'));
                }
                sb.Append(" }");
                return;
            default:
                sb.Append("/* TODO: lambda body ").Append(body.Kind()).Append(" */ null");
                return;
        }
    }

    // Emits `{ key1: value1, key2: value2 }` from an ObjectInitializerExpression
    // whose children are assignment expressions. Keys are camel-cased so they
    // match the rest of the transpiler's member naming.
    private static void EmitObjectInitializer(InitializerExpressionSyntax init, StringBuilder sb, EmitContext ctx)
    {
        sb.Append("{ ");
        var first = true;
        foreach (var expr in init.Expressions)
        {
            if (expr is AssignmentExpressionSyntax assign && assign.Left is IdentifierNameSyntax leftId)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(NameConventions.ToCamelCase(leftId.Identifier.Text)).Append(": ");
                Emit(assign.Right, sb, ctx);
            }
        }
        sb.Append(" }");
    }

    // Pick the JS property name for a C# member access. Normally we just
    // camelCase the declared name, but if the property carries
    // `[JsonPropertyName("x")]` we honour that — this lets user code read
    // snake_case JSON (e.g. open-meteo's `temperature_2m_max`) through
    // PascalCase C# properties without a manual mapping layer.
    private static string ResolveMemberName(MemberAccessExpressionSyntax mae, EmitContext ctx)
    {
        var symbol = ctx.Model.GetSymbolInfo(mae).Symbol;
        if (symbol is IPropertySymbol prop)
        {
            foreach (var attr in prop.GetAttributes())
            {
                var attrName = attr.AttributeClass?.Name;
                if (attrName == "JsonPropertyNameAttribute"
                    && attr.ConstructorArguments.Length > 0
                    && attr.ConstructorArguments[0].Value is string jsonName)
                {
                    return jsonName;
                }
            }
        }
        return NameConventions.ToCamelCase(mae.Name.Identifier.Text);
    }

    private static void EmitIdentifier(string name, StringBuilder sb, EmitContext ctx)
    {
        // Local scopes (lambda params, for-loop vars, primary-ctor params
        // inside the ctor body) shadow class members. A `class X { int item;
        // ... items.Select(item => item.Name) }` must emit the lambda's
        // `item` as bare `item`, NOT `this.item`. The rewrite-to-this path
        // only applies to bare identifiers that resolve to class-level
        // members at the current scope.
        if (ctx.IsLocallyShadowed(name))
        {
            sb.Append(name);
            return;
        }
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
