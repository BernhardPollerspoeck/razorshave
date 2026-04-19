import { Component } from './component.js';
import { h } from './h.js';
import { navigationManager } from './navigation-manager.js';

// Matches a route pattern like `/users/{id}` against a concrete path and
// returns the extracted parameters, or null when the pattern doesn't apply.
//
// Scope for M0 per the bootstrap: simple paths, `{name}` parameters, no type
// constraints (`{id:int}`), no catch-all (`{*rest}`). Those land in the v0.2
// router expansion — see RAZORSHAVE-RUNTIME-API.md for the full Blazor-parity
// surface.
export function matchRoute(pattern, path) {
  const patternSegs = pattern.split('/').filter(Boolean);
  const pathSegs = path.split('/').filter(Boolean);
  if (patternSegs.length !== pathSegs.length) return null;

  const params = {};
  for (let i = 0; i < patternSegs.length; i++) {
    const p = patternSegs[i];
    if (p.startsWith('{') && p.endsWith('}')) {
      params[p.slice(1, -1)] = decodeURIComponent(pathSegs[i]);
    } else if (p !== pathSegs[i]) {
      return null;
    }
  }
  return params;
}

// Renders whichever component currently matches `navigationManager.pathname`.
// The Router itself has no subscription logic — mount() listens to
// navigation events at the root and re-renders the whole tree, so every
// child Router re-evaluates its match on the new path. That keeps the
// naive-diff runtime simple; a real reconciler in v0.2 can subscribe more
// granularly.
//
// Props: { routes: [{ pattern, component }...], notFound?: Component }
export class Router extends Component {
  render() {
    const routes = this.props?.routes ?? [];
    const layout = this.props?.defaultLayout;
    const path = navigationManager.pathname;

    for (const route of routes) {
      const params = matchRoute(route.pattern, path);
      if (params !== null) {
        return wrapInLayout(h(route.component, params), layout);
      }
    }

    const NotFound = this.props?.notFound;
    if (NotFound) {
      return wrapInLayout(h(NotFound, { path }), layout);
    }
    return h('p', {}, 'Route not found: ' + path);
  }
}

// Wraps a matched route's vnode in the layout component when one is
// configured, matching Blazor's <RouteView DefaultLayout="..."> shape.
// The layout receives the route vnode as its `body` prop — LayoutComponent's
// `body` getter (or the transpiler's inherited-member hack) picks it up from
// there.
function wrapInLayout(vnode, layout) {
  return layout ? h(layout, { body: vnode }) : vnode;
}
