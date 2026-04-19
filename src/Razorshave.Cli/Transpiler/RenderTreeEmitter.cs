using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Walks a Razor-generated <c>BuildRenderTree</c> method and emits an
/// equivalent <c>render()</c> method that returns a VDOM tree built from
/// <c>h(...)</c> calls.
/// </summary>
/// <remarks>
/// <para>
/// Razor emits element/component hierarchy as a flat sequence of stateful
/// <c>__builder.OpenX / AddX / CloseX</c> calls. This emitter reconstructs the
/// hierarchy by maintaining a frame stack: <c>Open</c> pushes, <c>AddAttribute</c>
/// appends a prop to the top frame, <c>AddContent</c> appends a child, and
/// <c>Close</c> pops — at which point the frame is rendered as a complete
/// <c>h(tag, props, …children)</c> expression and added as a child to its
/// parent.
/// </para>
/// <para>
/// The walker is re-entrant: a <c>RenderFragment</c> passed to AddAttribute is
/// a lambda with its own <c>__builderN</c> parameter. <see cref="EmitBuilderBlock"/>
/// takes the builder identifier as a parameter, so the same logic handles the
/// top-level <c>__builder</c> and any nested fragment builder.
/// </para>
/// </remarks>
internal static class RenderTreeEmitter
{
    private const string ReturnIndent = "    ";

    public static void Emit(MethodDeclarationSyntax buildRenderTree, StringBuilder sb, EmitContext ctx)
    {
        sb.Append(ClassEmitter.Indent).Append("render() {\n");

        var children = buildRenderTree.Body is { } block
            ? EmitBuilderBlock(block, builderName: "__builder", ctx)
            : [];

        EmitReturn(children, sb);

        sb.Append(ClassEmitter.Indent).Append("}\n");
    }

    /// <summary>
    /// Walks a block that writes to <paramref name="builderName"/> and returns
    /// the list of top-level JS child expressions produced.
    /// </summary>
    private static List<string> EmitBuilderBlock(BlockSyntax block, string builderName, EmitContext ctx)
    {
        var root = new Frame(FrameKind.Root, tag: null);
        var stack = new Stack<Frame>();
        stack.Push(root);

        foreach (var stmt in block.Statements)
        {
            HandleStatement(stmt, builderName, stack, ctx);
        }

        return root.Children;
    }

    private static void HandleStatement(StatementSyntax stmt, string builderName, Stack<Frame> stack, EmitContext ctx)
    {
        if (stmt is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv }
            && inv.Expression is MemberAccessExpressionSyntax mae
            && mae.Expression is IdentifierNameSyntax id
            && id.Identifier.Text == builderName)
        {
            HandleBuilderCall(inv, builderName, stack, ctx);
            return;
        }

        // Control flow (if/foreach), local declarations, nested-builder references
        // etc. aren't handled here — later walker stages own those.
        stack.Peek().Children.Add($"/* TODO: unsupported render stmt: {stmt.Kind()} */");
    }

    private static void HandleBuilderCall(
        InvocationExpressionSyntax inv,
        string builderName,
        Stack<Frame> stack,
        EmitContext ctx)
    {
        var methodName = ((MemberAccessExpressionSyntax)inv.Expression).Name;
        var args = inv.ArgumentList;
        var name = methodName.Identifier.Text;

        switch (name)
        {
            case "OpenElement":
            {
                // __builder.OpenElement(seq, "tag")
                var tag = args.Arguments[1].Expression.ToString(); // preserves quotes
                stack.Push(new Frame(FrameKind.Element, tag));
                break;
            }

            case "OpenComponent":
            {
                // __builder.OpenComponent<T>(seq) — type from generic args on methodName
                var typeName = methodName is GenericNameSyntax g
                    ? StripGlobalAndNamespace(g.TypeArgumentList.Arguments[0].ToString())
                    : "UnknownComponent";
                stack.Push(new Frame(FrameKind.Component, typeName));
                break;
            }

            case "CloseElement":
            case "CloseComponent":
            {
                if (stack.Count <= 1)
                {
                    stack.Peek().Children.Add("/* TODO: unbalanced Close */");
                    break;
                }
                var frame = stack.Pop();
                stack.Peek().Children.Add(EmitFrame(frame));
                break;
            }

            case "AddAttribute":
            case "AddComponentParameter":
            {
                var propName = ((LiteralExpressionSyntax)args.Arguments[1].Expression).Token.ValueText;
                stack.Peek().Props.Add((propName, ResolveAttributeValue(inv, ctx)));
                break;
            }

            case "AddContent":
            {
                var contentJs = EmitExpression(args.Arguments[1].Expression, ctx);
                stack.Peek().Children.Add(contentJs);
                break;
            }

            case "AddMarkupContent":
            {
                var rawHtml = args.Arguments[1].Expression.ToString();
                stack.Peek().Children.Add($"markup({rawHtml})");
                break;
            }

            default:
            {
                stack.Peek().Children.Add($"/* TODO: {builderName}.{name} */");
                break;
            }
        }
    }

    /// <summary>
    /// Emits the value argument of an AddAttribute / AddComponentParameter call.
    /// Handles the three special cases in priority order — 2-arg marker, generic
    /// <c>AddAttribute&lt;T&gt;</c> event-handler overload, RenderFragment-cast-over-lambda —
    /// and falls back to <see cref="ExpressionEmitter"/> for everything else.
    /// </summary>
    private static string ResolveAttributeValue(InvocationExpressionSyntax addAttrInv, EmitContext ctx)
    {
        var args = addAttrInv.ArgumentList.Arguments;
        if (args.Count < 3)
        {
            return "\"\"";
        }

        var valueExpr = args[2].Expression;
        if (TryEmitEventHandler(addAttrInv, valueExpr, ctx, out var eventJs))
        {
            return eventJs;
        }
        if (TryEmitRenderFragment(valueExpr, ctx, out var fragmentJs))
        {
            return fragmentJs;
        }
        return EmitExpression(valueExpr, ctx);
    }

    /// <summary>
    /// Recognises the event-handler form of <c>AddAttribute</c>:
    /// <c>__builder.AddAttribute&lt;TEventArgs&gt;(seq, "onclick", EventCallback.Factory.Create&lt;TEventArgs&gt;(this, Handler))</c>.
    /// </summary>
    /// <remarks>
    /// Detection is semantic: the outer <c>AddAttribute</c>'s resolved symbol is
    /// generic exactly for this overload, and its single type argument is the
    /// EventArgs type Razor inferred from the inner <c>EventCallback&lt;T&gt;</c>.
    /// </remarks>
    private static bool TryEmitEventHandler(
        InvocationExpressionSyntax addAttributeInv,
        ExpressionSyntax valueExpr,
        EmitContext ctx,
        out string handlerJs)
    {
        handlerJs = "";

        if (ctx.Model.GetSymbolInfo(addAttributeInv).Symbol is not IMethodSymbol m
            || !m.IsGenericMethod
            || m.Name != "AddAttribute"
            || m.TypeArguments.Length == 0)
        {
            return false;
        }

        var eventArgsType = m.TypeArguments[0].Name;

        if (valueExpr is not InvocationExpressionSyntax createInv
            || createInv.ArgumentList.Arguments.Count < 2)
        {
            return false;
        }
        var handlerExpr = createInv.ArgumentList.Arguments[1].Expression;

        var resolvedHandler = new StringBuilder();
        ExpressionEmitter.Emit(handlerExpr, resolvedHandler, ctx);

        handlerJs = $"(e) => {resolvedHandler}(new {eventArgsType}(e))";
        return true;
    }

    /// <summary>
    /// Recognises an inline RenderFragment: <c>(RenderFragment)((__builderN) => { … })</c>
    /// and recursively walks its body with <c>__builderN</c> as the active builder.
    /// Emits a JS arrow function returning the child array.
    /// </summary>
    /// <remarks>
    /// Only the non-generic <c>RenderFragment</c> form is handled here. The
    /// typed variant <c>RenderFragment&lt;T&gt;</c> (seen in Routes.razor as
    /// <c>Router.Found</c>) has a two-level lambda shape and is deferred — the
    /// Router special-case in 5.12 will unpack it directly.
    /// </remarks>
    private static bool TryEmitRenderFragment(ExpressionSyntax valueExpr, EmitContext ctx, out string js)
    {
        js = "";

        if (valueExpr is not CastExpressionSyntax cast)
        {
            return false;
        }

        if (StripGlobalAndNamespace(cast.Type.ToString()) != "RenderFragment")
        {
            return false;
        }

        // Peel off parens around the lambda
        var inner = cast.Expression;
        while (inner is ParenthesizedExpressionSyntax paren)
        {
            inner = paren.Expression;
        }

        var (builderName, body) = inner switch
        {
            SimpleLambdaExpressionSyntax sl when sl.Body is BlockSyntax slBlock
                => (sl.Parameter.Identifier.Text, slBlock),

            ParenthesizedLambdaExpressionSyntax pl
                when pl.ParameterList.Parameters.Count == 1
                  && pl.Body is BlockSyntax plBlock
                => (pl.ParameterList.Parameters[0].Identifier.Text, plBlock),

            _ => (null, null),
        };
        if (builderName is null || body is null)
        {
            return false;
        }

        var children = EmitBuilderBlock(body, builderName, ctx);
        js = children.Count switch
        {
            0 => "() => []",
            1 => $"() => [{children[0]}]",
            _ => "() => [" + string.Join(", ", children) + "]",
        };
        return true;
    }

    private static string EmitExpression(ExpressionSyntax expr, EmitContext ctx)
    {
        var sb = new StringBuilder();
        ExpressionEmitter.Emit(expr, sb, ctx);
        return sb.ToString();
    }

    private static string EmitFrame(Frame frame)
    {
        var sb = new StringBuilder();
        sb.Append("h(").Append(frame.Tag);

        sb.Append(", ");
        if (frame.Props.Count == 0)
        {
            sb.Append("{}");
        }
        else
        {
            sb.Append("{ ");
            for (var i = 0; i < frame.Props.Count; i++)
            {
                var (n, v) = frame.Props[i];
                sb.Append('\'').Append(n).Append("': ").Append(v);
                if (i < frame.Props.Count - 1) sb.Append(", ");
            }
            sb.Append(" }");
        }

        foreach (var child in frame.Children)
        {
            sb.Append(", ").Append(child);
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static void EmitReturn(List<string> children, StringBuilder sb)
    {
        if (children.Count == 0)
        {
            sb.Append(ReturnIndent).Append("return null;\n");
            return;
        }
        if (children.Count == 1)
        {
            sb.Append(ReturnIndent).Append("return ").Append(children[0]).Append(";\n");
            return;
        }

        sb.Append(ReturnIndent).Append("return [\n");
        for (var i = 0; i < children.Count; i++)
        {
            sb.Append(ReturnIndent).Append("  ").Append(children[i]);
            if (i < children.Count - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append(ReturnIndent).Append("];\n");
    }

    /// <summary>
    /// <c>global::Microsoft.AspNetCore.Components.Web.PageTitle</c> → <c>PageTitle</c>.
    /// </summary>
    private static string StripGlobalAndNamespace(string qualified)
    {
        var lastDot = qualified.LastIndexOf('.');
        return lastDot < 0 ? qualified : qualified[(lastDot + 1)..];
    }

    private enum FrameKind { Root, Element, Component }

    private sealed class Frame
    {
        public Frame(FrameKind kind, string? tag)
        {
            Kind = kind;
            Tag = tag;
        }

        public FrameKind Kind { get; }
        public string? Tag { get; }
        public List<(string Name, string Value)> Props { get; } = [];
        public List<string> Children { get; } = [];
    }
}
