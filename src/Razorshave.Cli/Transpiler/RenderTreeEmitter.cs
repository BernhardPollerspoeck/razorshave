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
/// Two cooperating stacks model Razor's stateful output:
/// <list type="bullet">
///   <item><c>frameStack</c> tracks the current element / component — each
///     <c>__builder.OpenX</c> pushes, each <c>CloseX</c> pops and emits the
///     completed <c>h(tag, props, …)</c> as a child of the enclosing frame.</item>
///   <item><c>destinationStack</c> tracks where the next child goes. At
///     steady state this is the top frame's child-ops list, but entering an
///     <c>if</c> / <c>foreach</c> body redirects additions into a sub-list
///     (the conditional's <c>then</c> / <c>else</c> or the loop's body) so we
///     preserve control flow rather than flattening it.</item>
/// </list>
/// </para>
/// <para>
/// A frame whose children are all direct literals emits inline varargs —
/// <c>h(tag, props, a, b, c)</c> — preserving readable output for the common
/// case. A frame carrying any <c>if</c> / <c>foreach</c> op instead emits an
/// IIFE that accumulates into an array, so dynamic child counts work without
/// reshaping the overall expression tree. Top-level <c>render()</c> uses the
/// same distinction but inlines the accumulator into the method body because
/// it is already inside a function scope.
/// </para>
/// <para>
/// RenderFragment lambdas (<c>__builder2</c>) recurse through
/// <see cref="EmitBuilderBlock"/> with a different builder name.
/// </para>
/// </remarks>
internal static class RenderTreeEmitter
{
    private const string ReturnIndent = "    ";

    public static void Emit(MethodDeclarationSyntax buildRenderTree, StringBuilder sb, EmitContext ctx)
    {
        sb.Append(ClassEmitter.Indent).Append("render() {\n");

        var ops = buildRenderTree.Body is { } block
            ? EmitBuilderBlock(block, builderName: "__builder", ctx)
            : [];

        EmitTopLevelReturn(ops, sb);

        sb.Append(ClassEmitter.Indent).Append("}\n");
    }

    /// <summary>
    /// Walks a block that writes to <paramref name="builderName"/> and returns
    /// the ops accumulated at the root destination.
    /// </summary>
    private static List<ChildOp> EmitBuilderBlock(BlockSyntax block, string builderName, EmitContext ctx)
    {
        var rootOps = new List<ChildOp>();

        // Only real element/component frames live on frameStack. The "current
        // child destination" is a separate stack so it can redirect into an
        // if/foreach sub-list without touching element nesting.
        var frameStack = new Stack<Frame>();
        var destStack = new Stack<List<ChildOp>>();
        destStack.Push(rootOps);

        WalkStatements(block.Statements, builderName, frameStack, destStack, ctx);

        return rootOps;
    }

    private static void WalkStatements(
        IEnumerable<StatementSyntax> stmts,
        string builderName,
        Stack<Frame> frameStack,
        Stack<List<ChildOp>> destStack,
        EmitContext ctx)
    {
        foreach (var stmt in stmts)
        {
            HandleStatement(stmt, builderName, frameStack, destStack, ctx);
        }
    }

    private static void HandleStatement(
        StatementSyntax stmt,
        string builderName,
        Stack<Frame> frameStack,
        Stack<List<ChildOp>> destStack,
        EmitContext ctx)
    {
        switch (stmt)
        {
            case ExpressionStatementSyntax { Expression: InvocationExpressionSyntax inv }
                when inv.Expression is MemberAccessExpressionSyntax mae
                  && mae.Expression is IdentifierNameSyntax id
                  && id.Identifier.Text == builderName:
                HandleBuilderCall(inv, builderName, frameStack, destStack, ctx);
                return;

            case IfStatementSyntax ifStmt:
                HandleIf(ifStmt, builderName, frameStack, destStack, ctx);
                return;

            case ForEachStatementSyntax feStmt:
                HandleForEach(feStmt, builderName, frameStack, destStack, ctx);
                return;

            case BlockSyntax block:
                WalkStatements(block.Statements, builderName, frameStack, destStack, ctx);
                return;

            default:
                destStack.Peek().Add(new LiteralOp($"/* TODO: unsupported render stmt: {stmt.Kind()} */"));
                return;
        }
    }

    private static void HandleIf(
        IfStatementSyntax ifStmt,
        string builderName,
        Stack<Frame> frameStack,
        Stack<List<ChildOp>> destStack,
        EmitContext ctx)
    {
        var cond = EmitExpression(ifStmt.Condition, ctx);
        var ifOp = new IfOp(cond);
        destStack.Peek().Add(ifOp);

        destStack.Push(ifOp.Then);
        HandleStatement(ifStmt.Statement, builderName, frameStack, destStack, ctx);
        destStack.Pop();

        if (ifStmt.Else is { } elseClause)
        {
            destStack.Push(ifOp.Else);
            HandleStatement(elseClause.Statement, builderName, frameStack, destStack, ctx);
            destStack.Pop();
        }
    }

    private static void HandleForEach(
        ForEachStatementSyntax feStmt,
        string builderName,
        Stack<Frame> frameStack,
        Stack<List<ChildOp>> destStack,
        EmitContext ctx)
    {
        var iterable = EmitExpression(feStmt.Expression, ctx);
        var loopOp = new LoopOp(feStmt.Identifier.Text, iterable);
        destStack.Peek().Add(loopOp);

        destStack.Push(loopOp.Body);
        HandleStatement(feStmt.Statement, builderName, frameStack, destStack, ctx);
        destStack.Pop();
    }

    private static void HandleBuilderCall(
        InvocationExpressionSyntax inv,
        string builderName,
        Stack<Frame> frameStack,
        Stack<List<ChildOp>> destStack,
        EmitContext ctx)
    {
        var methodName = ((MemberAccessExpressionSyntax)inv.Expression).Name;
        var args = inv.ArgumentList;
        var name = methodName.Identifier.Text;

        switch (name)
        {
            case "OpenElement":
            {
                var newFrame = new Frame(FrameKind.Element, args.Arguments[1].Expression.ToString());
                frameStack.Push(newFrame);
                destStack.Push(newFrame.ChildrenOps);
                return;
            }

            case "OpenComponent":
            {
                var typeName = methodName is GenericNameSyntax g
                    ? StripGlobalAndNamespace(g.TypeArgumentList.Arguments[0].ToString())
                    : "UnknownComponent";
                var newFrame = new Frame(FrameKind.Component, typeName);
                frameStack.Push(newFrame);
                destStack.Push(newFrame.ChildrenOps);
                return;
            }

            case "CloseElement":
            case "CloseComponent":
                if (frameStack.Count == 0)
                {
                    destStack.Peek().Add(new LiteralOp("/* TODO: unbalanced Close */"));
                    return;
                }
                var frame = frameStack.Pop();
                destStack.Pop();
                destStack.Peek().Add(new LiteralOp(EmitFrame(frame)));
                return;

            case "AddAttribute":
            case "AddComponentParameter":
            {
                var propName = ((LiteralExpressionSyntax)args.Arguments[1].Expression).Token.ValueText;
                frameStack.Peek().Props.Add((propName, ResolveAttributeValue(inv, ctx)));
                return;
            }

            case "AddContent":
                destStack.Peek().Add(new LiteralOp(EmitExpression(args.Arguments[1].Expression, ctx)));
                return;

            case "AddMarkupContent":
                destStack.Peek().Add(new LiteralOp($"markup({args.Arguments[1].Expression})"));
                return;

            default:
                destStack.Peek().Add(new LiteralOp($"/* TODO: {builderName}.{name} */"));
                return;
        }
    }

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

    private static bool TryEmitRenderFragment(ExpressionSyntax valueExpr, EmitContext ctx, out string js)
    {
        js = "";
        if (valueExpr is not CastExpressionSyntax cast) return false;
        if (StripGlobalAndNamespace(cast.Type.ToString()) != "RenderFragment") return false;

        var inner = cast.Expression;
        while (inner is ParenthesizedExpressionSyntax paren) inner = paren.Expression;

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
        if (builderName is null || body is null) return false;

        var ops = EmitBuilderBlock(body, builderName, ctx);
        js = $"() => {EmitOpsAsArrayExpression(ops)}";
        return true;
    }

    private static string EmitExpression(ExpressionSyntax expr, EmitContext ctx)
    {
        var sb = new StringBuilder();
        ExpressionEmitter.Emit(expr, sb, ctx);
        return sb.ToString();
    }

    /// <summary>
    /// Emit a frame as <c>h(tag, props, …)</c>. All-literal children use inline
    /// varargs; any control-flow op switches to an IIFE accumulator.
    /// </summary>
    private static string EmitFrame(Frame frame)
    {
        var sb = new StringBuilder();
        sb.Append("h(").Append(frame.Tag).Append(", ");
        EmitPropsObject(frame.Props, sb);

        if (frame.ChildrenOps.Count == 0)
        {
            sb.Append(')');
            return sb.ToString();
        }

        if (frame.ChildrenOps.TrueForAll(op => op is LiteralOp))
        {
            foreach (var op in frame.ChildrenOps)
            {
                sb.Append(", ").Append(((LiteralOp)op).Js);
            }
            sb.Append(')');
            return sb.ToString();
        }

        sb.Append(", ").Append(EmitDynamicChildrenExpression(frame.ChildrenOps));
        sb.Append(')');
        return sb.ToString();
    }

    private static void EmitPropsObject(List<(string Name, string Value)> props, StringBuilder sb)
    {
        if (props.Count == 0)
        {
            sb.Append("{}");
            return;
        }
        sb.Append("{ ");
        for (var i = 0; i < props.Count; i++)
        {
            var (n, v) = props[i];
            sb.Append('\'').Append(n).Append("': ").Append(v);
            if (i < props.Count - 1) sb.Append(", ");
        }
        sb.Append(" }");
    }

    /// <summary>
    /// Emit a list of ops as a JS expression that produces an array.
    /// Used for RenderFragment body and nested dynamic-children IIFEs.
    /// </summary>
    private static string EmitOpsAsArrayExpression(List<ChildOp> ops)
    {
        if (ops.Count == 0) return "[]";
        if (ops.TrueForAll(op => op is LiteralOp))
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (var i = 0; i < ops.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(((LiteralOp)ops[i]).Js);
            }
            sb.Append(']');
            return sb.ToString();
        }
        return EmitDynamicChildrenExpression(ops);
    }

    /// <summary>
    /// Emit a list of ops as an IIFE that returns the accumulated array.
    /// Always parenthesised so it can sit in an expression position
    /// (e.g., as an <c>h(...)</c> argument).
    /// </summary>
    private static string EmitDynamicChildrenExpression(List<ChildOp> ops)
    {
        var sb = new StringBuilder();
        sb.Append("(() => { const _c = []; ");
        foreach (var op in ops)
        {
            EmitOpIntoAccumulator(op, "_c", sb);
        }
        sb.Append("return _c; })()");
        return sb.ToString();
    }

    private static void EmitOpIntoAccumulator(ChildOp op, string acc, StringBuilder sb)
    {
        switch (op)
        {
            case LiteralOp lit:
                sb.Append(acc).Append(".push(").Append(lit.Js).Append("); ");
                return;

            case IfOp ifOp:
                sb.Append("if (").Append(ifOp.Cond).Append(") { ");
                foreach (var o in ifOp.Then) EmitOpIntoAccumulator(o, acc, sb);
                sb.Append('}');
                if (ifOp.Else.Count > 0)
                {
                    sb.Append(" else { ");
                    foreach (var o in ifOp.Else) EmitOpIntoAccumulator(o, acc, sb);
                    sb.Append('}');
                }
                sb.Append(' ');
                return;

            case LoopOp loop:
                sb.Append("for (const ").Append(loop.VarName).Append(" of ").Append(loop.Iterable).Append(") { ");
                foreach (var o in loop.Body) EmitOpIntoAccumulator(o, acc, sb);
                sb.Append("} ");
                return;
        }
    }

    /// <summary>
    /// Render-method top-level return: inline array for all-literal, multi-line
    /// accumulator block when control-flow ops are present. Slightly different
    /// from the nested case because we're already inside a function body.
    /// </summary>
    private static void EmitTopLevelReturn(List<ChildOp> rootOps, StringBuilder sb)
    {
        if (rootOps.Count == 0)
        {
            sb.Append(ReturnIndent).Append("return null;\n");
            return;
        }

        if (rootOps.TrueForAll(op => op is LiteralOp))
        {
            if (rootOps.Count == 1)
            {
                sb.Append(ReturnIndent).Append("return ").Append(((LiteralOp)rootOps[0]).Js).Append(";\n");
                return;
            }
            sb.Append(ReturnIndent).Append("return [\n");
            for (var i = 0; i < rootOps.Count; i++)
            {
                sb.Append(ReturnIndent).Append("  ").Append(((LiteralOp)rootOps[i]).Js);
                if (i < rootOps.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append(ReturnIndent).Append("];\n");
            return;
        }

        // Dynamic top-level: accumulate inline, no IIFE wrapper needed.
        sb.Append(ReturnIndent).Append("const _c = [];\n");
        foreach (var op in rootOps)
        {
            EmitOpAtTopLevel(op, "_c", sb, ReturnIndent);
        }
        sb.Append(ReturnIndent).Append("return _c;\n");
    }

    private static void EmitOpAtTopLevel(ChildOp op, string acc, StringBuilder sb, string indent)
    {
        switch (op)
        {
            case LiteralOp lit:
                sb.Append(indent).Append(acc).Append(".push(").Append(lit.Js).Append(");\n");
                return;

            case IfOp ifOp:
                sb.Append(indent).Append("if (").Append(ifOp.Cond).Append(") {\n");
                foreach (var o in ifOp.Then) EmitOpAtTopLevel(o, acc, sb, indent + "  ");
                sb.Append(indent).Append('}');
                if (ifOp.Else.Count > 0)
                {
                    sb.Append(" else {\n");
                    foreach (var o in ifOp.Else) EmitOpAtTopLevel(o, acc, sb, indent + "  ");
                    sb.Append(indent).Append('}');
                }
                sb.Append('\n');
                return;

            case LoopOp loop:
                sb.Append(indent).Append("for (const ").Append(loop.VarName).Append(" of ").Append(loop.Iterable).Append(") {\n");
                foreach (var o in loop.Body) EmitOpAtTopLevel(o, acc, sb, indent + "  ");
                sb.Append(indent).Append("}\n");
                return;
        }
    }

    /// <summary>
    /// <c>global::Microsoft.AspNetCore.Components.Web.PageTitle</c> → <c>PageTitle</c>.
    /// </summary>
    private static string StripGlobalAndNamespace(string qualified)
    {
        var lastDot = qualified.LastIndexOf('.');
        return lastDot < 0 ? qualified : qualified[(lastDot + 1)..];
    }

    private enum FrameKind { Element, Component }

    private sealed class Frame(FrameKind kind, string? tag)
    {
        public FrameKind Kind { get; } = kind;
        public string? Tag { get; } = tag;
        public List<(string Name, string Value)> Props { get; } = [];
        public List<ChildOp> ChildrenOps { get; } = [];
    }

    // Child-production ops — what to emit into the current frame's children list.
    private abstract class ChildOp { }
    private sealed class LiteralOp(string js) : ChildOp { public string Js { get; } = js; }
    private sealed class IfOp(string cond) : ChildOp
    {
        public string Cond { get; } = cond;
        public List<ChildOp> Then { get; } = [];
        public List<ChildOp> Else { get; } = [];
    }
    private sealed class LoopOp(string varName, string iterable) : ChildOp
    {
        public string VarName { get; } = varName;
        public string Iterable { get; } = iterable;
        public List<ChildOp> Body { get; } = [];
    }
}
