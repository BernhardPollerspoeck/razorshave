import { Component } from '../component.js';
import { h } from '../h.js';
import { navigationManager } from '../navigation-manager.js';
import { reportRuntimeError } from '../errors.js';

// <NavLink Href="/counter" class="nav-link">Counter</NavLink> in Razor maps to
// this component. Renders as an anchor that traps left-clicks and routes via
// the NavigationManager instead of letting the browser do a full page load.
// Adds the `active` class when the current path matches — Blazor defaults to
// prefix matching (so /counter/5 still highlights the /counter link), we do
// the same.
export class NavLink extends Component {
  render() {
    const rawHref = this.props.Href ?? this.props.href ?? '';
    const userClass = this.props.class ?? this.props.Class ?? '';
    // Guard the RenderFragment invocation: a throwing ChildContent would
    // otherwise crash the entire render pipeline. Log + fall back to empty
    // children so the link still renders (clickable, unstyled).
    let childContent;
    try {
      childContent = this.props.ChildContent?.() ?? [];
    } catch (err) {
      reportRuntimeError(err, { phase: 'RenderFragment', component: 'NavLink' });
      childContent = [];
    }

    const target = normalizePath(rawHref);
    const path = navigationManager.pathname;
    const active = isActiveFor(path, target);
    const className = active
      ? (userClass ? `${userClass} active` : 'active')
      : userClass;

    return h('a', {
      // Rendering the normalised target (always slash-prefixed) keeps the
      // browser's own link semantics clean — an empty href="" would otherwise
      // tickle jsdom/some-browsers into treating the link as a reload.
      href: target,
      class: className || null,
      onclick: (e) => {
        // Only hijack plain left-clicks; let modifier-clicks (ctrl/cmd/new tab)
        // fall through to the browser.
        if (e.defaultPrevented) return;
        if (e.button !== 0) return;
        if (e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
        e.preventDefault();
        navigationManager.navigateTo(target);
      },
    }, ...childContent);
  }
}

// Blazor's NavLink accepts relative hrefs (`href="counter"`) whose meaning
// depends on the containing page's base URL. Razorshave's router always
// compares against absolute pathnames (`/counter`), so we normalise here:
// empty → root, missing leading slash → prepend it. Comparison and
// navigation then stay consistent.
function normalizePath(href) {
  if (!href) return '/';
  return href.startsWith('/') ? href : '/' + href;
}

function isActiveFor(currentPath, href) {
  if (!href || href === '#') return false;
  const target = normalizePath(href);
  if (currentPath === target) return true;
  // Root link matches only exactly — otherwise '/' would be always active.
  if (target === '/') return false;
  return currentPath.startsWith(target + '/');
}
