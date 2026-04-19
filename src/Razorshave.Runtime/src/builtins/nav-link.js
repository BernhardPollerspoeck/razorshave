import { Component } from '../component.js';
import { h } from '../h.js';
import { navigationManager } from '../navigation-manager.js';

// <NavLink Href="/counter" class="nav-link">Counter</NavLink> in Razor maps to
// this component. Renders as an anchor that traps left-clicks and routes via
// the NavigationManager instead of letting the browser do a full page load.
// Adds the `active` class when the current path matches — Blazor defaults to
// prefix matching (so /counter/5 still highlights the /counter link), we do
// the same.
export class NavLink extends Component {
  render() {
    const href = this.props.Href ?? this.props.href ?? '#';
    const userClass = this.props.class ?? this.props.Class ?? '';
    const childContent = this.props.ChildContent?.() ?? [];

    const path = navigationManager.pathname;
    const active = isActiveFor(path, href);
    const className = active
      ? (userClass ? `${userClass} active` : 'active')
      : userClass;

    return h('a', {
      href,
      class: className || null,
      onclick: (e) => {
        // Only hijack plain left-clicks; let modifier-clicks (ctrl/cmd/new tab)
        // fall through to the browser.
        if (e.defaultPrevented) return;
        if (e.button !== 0) return;
        if (e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
        e.preventDefault();
        navigationManager.navigateTo(href);
      },
    }, ...childContent);
  }
}

function isActiveFor(currentPath, href) {
  if (!href || href === '#') return false;
  if (currentPath === href) return true;
  // Root link matches only exactly — otherwise '/' would be always active.
  if (href === '/') return false;
  return currentPath.startsWith(href + '/');
}
