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

// Sort key that mirrors Blazor's route-priority rules: more specific
// patterns win over more general ones regardless of source order, so a
// user who registers routes in any order gets deterministic matching.
//
// Rule order (ascending priority):
//   1. Literal segments beat parameters (`/users/new` > `/users/{id}`)
//   2. Parameters without constraints ties (both are one "slot")
//   3. Longer patterns beat shorter ones at a tie
// Returns a numeric key where HIGHER = more specific.
function routeSpecificity(pattern) {
  const segs = pattern.split('/').filter(Boolean);
  let score = segs.length * 10;
  for (const seg of segs) {
    if (!seg.startsWith('{')) score += 1; // literal > parameter
  }
  return score;
}

// Renders whichever component currently matches `navigationManager.pathname`.
// Routes are pre-sorted by specificity on mount so a more-specific pattern
// (`/users/new`) wins over a less-specific one (`/users/{id}`) regardless
// of the order the user declared them in. Without this the match silently
// depended on array order — classic silent-fail shape.
//
// Props: { routes: [{ pattern, component }...], notFound?: Component }
export class Router extends Component {
  onInit() {
    const routes = this.props?.routes ?? [];
    this._sortedRoutes = [...routes].sort(
      (a, b) => routeSpecificity(b.pattern) - routeSpecificity(a.pattern));
  }

  onPropsChanged() {
    // If the caller swapped the routes array, re-sort.
    this.onInit();
  }

  render() {
    const routes = this._sortedRoutes ?? [];
    const layout = this.props?.defaultLayout;
    const path = navigationManager.pathname;

    for (const route of routes) {
      const params = matchRoute(route.pattern, path);
      if (params !== null) {
        return wrapInLayout(h(route.component, params), layout);
      }
    }

    const NotFound = this.props?.notFound ?? DefaultNotFound;
    return wrapInLayout(h(NotFound, { path }), layout);
  }
}

// Default fallback for routes that don't match and don't have a user-
// supplied notFound component configured. Kept minimal and exported so
// apps can import it, wrap it, or just rely on the fallback.
//
// User-supplied `notFound` receives `{ path }` as props — same shape here
// so swapping between default and custom is a one-line change.
export class DefaultNotFound extends Component {
  render() {
    return h('div', { class: 'rs-not-found', role: 'alert' },
      h('h2', null, 'Page not found'),
      h('p', null, 'No route matched ',
        h('code', null, this.props?.path ?? '')
      )
    );
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
