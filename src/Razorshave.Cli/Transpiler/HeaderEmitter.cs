using System.Text;

namespace Razorshave.Cli.Transpiler;

/// <summary>
/// Writes the import header that makes the transpiled module resolve its
/// runtime dependencies (<c>h</c>, <c>Component</c>, <c>MouseEventArgs</c>, etc.)
/// from the <c>@razorshave/runtime</c> package.
/// </summary>
/// <remarks>
/// <para>
/// For M0 every file gets the same fixed import list. Bundlers (esbuild) drop
/// unused imports via tree-shaking, so over-importing here is not a bundle-size
/// concern. Doing a precise per-file usage scan would double the walker work
/// for a feature we'll get for free from the bundler in 5.13.
/// </para>
/// <para>
/// The bare specifier <c>@razorshave/runtime</c> is resolved by the browser via
/// an importmap (see <c>demo/index.html</c>) or by the bundler in production.
/// </para>
/// </remarks>
internal static class HeaderEmitter
{
    private const string ImportList =
        "h, markup, Component, LayoutComponent, " +
        "EventArgs, MouseEventArgs, KeyboardEventArgs, ChangeEventArgs, FocusEventArgs, " +
        "PageTitle";

    public static void Emit(StringBuilder sb)
    {
        sb.Append("import { ").Append(ImportList).Append(" } from '@razorshave/runtime';\n\n");
    }
}
