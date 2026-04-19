using System.Text;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Writes the import header for a transpiled component module:
/// <list type="bullet">
///   <item>A fixed set of runtime symbols from <c>@razorshave/runtime</c>
///         (the VDOM helpers, base classes, EventArgs types, built-in
///         components the runtime ships with).</item>
///   <item>One per-file import line for every user-authored component the
///         class references (via <c>OpenComponent&lt;T&gt;</c>), pointing at
///         the sibling <c>&lt;Name&gt;.js</c> file the transpiler will emit.</item>
/// </list>
/// </summary>
/// <remarks>
/// Over-importing from the runtime list is a non-issue — esbuild tree-shakes
/// anything the module doesn't reach. User-component imports are specific
/// though: missing one leaves a <c>ReferenceError: X is not defined</c> at
/// runtime the moment render() hits the <c>h(X, …)</c> call.
/// </remarks>
internal static class HeaderEmitter
{
    private const string RuntimeImports =
        "h, markup, Component, LayoutComponent, " +
        "EventArgs, MouseEventArgs, KeyboardEventArgs, ChangeEventArgs, FocusEventArgs, " +
        "PageTitle, NavLink, Router";

    /// <summary>
    /// Components supplied by <c>@razorshave/runtime</c> — never import these
    /// from a sibling <c>./Name.js</c> file, they're already in the runtime import.
    /// </summary>
    private static readonly HashSet<string> RuntimeComponentNames = new(StringComparer.Ordinal)
    {
        "PageTitle", "NavLink", "Router",
    };

    /// <summary>
    /// Blazor-server-only or Blazor-internal components that have no Razorshave
    /// counterpart. Silently skipped — they only show up inside App.razor /
    /// Routes.razor which the build pipeline already special-cases.
    /// </summary>
    private static readonly HashSet<string> BlazorInternalComponentNames = new(StringComparer.Ordinal)
    {
        "RouteView", "FocusOnNavigate", "HeadOutlet", "ImportMap",
    };

    public static void Emit(StringBuilder sb, ClassDeclarationSyntax component)
    {
        sb.Append("import { ").Append(RuntimeImports).Append(" } from '@razorshave/runtime';\n");

        foreach (var name in CollectUserComponentReferences(component))
        {
            sb.Append("import { ").Append(name).Append(" } from './").Append(name).Append(".js';\n");
        }

        sb.Append('\n');
    }

    /// <summary>
    /// Walks <c>BuildRenderTree</c> looking for <c>__builder.OpenComponent&lt;T&gt;</c>
    /// calls and returns the unique, sorted simple names of the T's that
    /// aren't provided by the runtime or are Blazor-internal.
    /// </summary>
    private static SortedSet<string> CollectUserComponentReferences(ClassDeclarationSyntax component)
    {
        var refs = new SortedSet<string>(StringComparer.Ordinal);
        var ownName = component.Identifier.Text;

        foreach (var inv in component.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is not MemberAccessExpressionSyntax mae) continue;
            if (mae.Name is not GenericNameSyntax gen) continue;
            if (gen.Identifier.Text != "OpenComponent") continue;
            if (gen.TypeArgumentList.Arguments.Count == 0) continue;

            var typeName = NameConventions.StripQualifiers(gen.TypeArgumentList.Arguments[0].ToString());
            if (typeName == ownName) continue;
            if (RuntimeComponentNames.Contains(typeName)) continue;
            if (BlazorInternalComponentNames.Contains(typeName)) continue;

            refs.Add(typeName);
        }
        return refs;
    }
}
