using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli;

/// <summary>
/// Build-time extraction of Razor routing metadata from a component's
/// <c>.razor.g.cs</c> — the route patterns from every <c>[Route("…")]</c>
/// attribute on a component class, and (from Routes.razor specifically) the
/// <c>DefaultLayout</c> and <c>NotFoundPage</c> wiring the Blazor template
/// sets on the framework <c>Router</c> / <c>RouteView</c>.
/// </summary>
/// <remarks>
/// Works on the syntax tree directly — no SemanticModel required. Razor wraps
/// the <c>AddComponentParameter</c> parameter name in a <c>nameof(...)</c>
/// call (so the generated code tracks property renames) and the value in a
/// <c>RuntimeHelpers.TypeCheck&lt;Type&gt;(typeof(…))</c>; the extractor
/// peels both wrappers and normalises qualified type names.
/// </remarks>
internal static class RouteExtractor
{
    public sealed record RoutesConfig(string? DefaultLayout, string? NotFound)
    {
        public static RoutesConfig Empty { get; } = new(null, null);
    }

    /// <summary>Returns the <c>[Route("pattern")]</c> patterns on the given component class.</summary>
    public static IReadOnlyList<string> ExtractRoutePatterns(ClassDeclarationSyntax component)
    {
        var patterns = new List<string>();
        foreach (var attrList in component.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var simpleName = StripQualifiers(attr.Name.ToString());
                if (simpleName is not ("Route" or "RouteAttribute")) continue;

                var args = attr.ArgumentList?.Arguments;
                if (args is null || args.Value.Count == 0) continue;

                if (args.Value[0].Expression is LiteralExpressionSyntax { Token.Value: string s })
                {
                    patterns.Add(s);
                }
            }
        }
        return patterns;
    }

    /// <summary>
    /// Extracts the <c>DefaultLayout</c> and <c>NotFoundPage</c> component
    /// names from a Routes.razor.g.cs tree. Tolerates Razor's indirection:
    /// <c>AddComponentParameter(seq, nameof(RouteView.DefaultLayout), RuntimeHelpers.TypeCheck&lt;Type&gt;(typeof(MainLayout)))</c>.
    /// </summary>
    public static RoutesConfig ExtractRoutesConfig(SyntaxTree tree)
    {
        string? defaultLayout = null;
        string? notFound = null;

        foreach (var inv in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is not MemberAccessExpressionSyntax mae) continue;
            if (mae.Name.Identifier.Text != "AddComponentParameter") continue;

            var args = inv.ArgumentList.Arguments;
            if (args.Count < 3) continue;

            var paramName = TryExtractParamName(args[1].Expression);
            if (paramName is null) continue;

            if (paramName != "DefaultLayout" && paramName != "NotFoundPage") continue;

            var typeName = TryExtractTypeOfTarget(args[2].Expression);
            if (typeName is null) continue;

            if (paramName == "DefaultLayout") defaultLayout = typeName;
            else if (paramName == "NotFoundPage") notFound = typeName;
        }

        return new RoutesConfig(defaultLayout, notFound);
    }

    /// <summary>
    /// <c>"DefaultLayout"</c> literal or <c>nameof(RouteView.DefaultLayout)</c>
    /// → <c>"DefaultLayout"</c>.
    /// </summary>
    private static string? TryExtractParamName(ExpressionSyntax expr)
    {
        if (expr is LiteralExpressionSyntax { Token.Value: string s })
        {
            return s;
        }

        if (expr is InvocationExpressionSyntax inv
            && inv.Expression is IdentifierNameSyntax id
            && id.Identifier.Text == "nameof"
            && inv.ArgumentList.Arguments.Count > 0)
        {
            return SimpleNameOf(inv.ArgumentList.Arguments[0].Expression);
        }

        return null;
    }

    private static string? SimpleNameOf(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax mae => mae.Name.Identifier.Text,
        _ => null,
    };

    /// <summary>
    /// Finds the <c>typeof(X)</c> buried in a value expression — directly or
    /// wrapped in <c>RuntimeHelpers.TypeCheck&lt;T&gt;(typeof(X))</c> — and
    /// returns the simple name of X.
    /// </summary>
    private static string? TryExtractTypeOfTarget(ExpressionSyntax expr)
    {
        var typeOf = expr is TypeOfExpressionSyntax direct
            ? direct
            : expr.DescendantNodes().OfType<TypeOfExpressionSyntax>().FirstOrDefault();
        return typeOf is null ? null : StripQualifiers(typeOf.Type.ToString());
    }

    private static string StripQualifiers(string qualified)
    {
        var lastDot = qualified.LastIndexOf('.');
        return lastDot < 0 ? qualified : qualified[(lastDot + 1)..];
    }
}
