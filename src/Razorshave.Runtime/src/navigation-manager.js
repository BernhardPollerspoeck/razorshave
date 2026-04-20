// Singleton wrapper around the browser's history/location API.
//
// All navigation — `navigateTo(path)` (programmatic pushState) AND browser
// back/forward (popstate) — flows through `_emit`, so subscribers get ONE
// callback shape for BOTH sources. The window's popstate listener lives
// here (not in mount.js) so application-level concerns like guards, scroll
// restoration, or navigation middleware can be added in a single place
// without scattering window-event wiring.
//
// Guarded against SSR / non-browser environments with `typeof window` checks.
// The check happens at EVERY access rather than at module-load — Vitest and
// other test runners inject jsdom AFTER module initialisation, and a
// module-load snapshot would otherwise stay `false` for the whole run and
// silently drop every navigation event.

function hasWindow() {
  return typeof window !== 'undefined';
}

class NavigationManager {
  constructor() {
    this._listeners = new Set();
    this._popHandler = null;
    this._attach();
  }

  // Register our single popstate handler with the browser so back/forward
  // fires `_emit` exactly like `navigateTo`. Separate method (not inline
  // in the constructor) so tests can re-initialise after mocking `window`.
  _attach() {
    if (!hasWindow() || this._popHandler) return;
    this._popHandler = () => this._emit(window.location.pathname);
    window.addEventListener('popstate', this._popHandler);
  }

  get uri() {
    return hasWindow() ? window.location.href : '';
  }

  get pathname() {
    return hasWindow() ? window.location.pathname : '/';
  }

  navigateTo(path) {
    if (!hasWindow()) return;
    window.history.pushState(null, '', path);
    this._emit(path);
  }

  // Listener set is snapshotted before dispatch in `_emit` — same semantics
  // as `Store.onChange`: subscribing during dispatch does NOT see the current
  // event, only subsequent ones; unsubscribing during dispatch is safe.
  onLocationChanged(handler) {
    // Lazy attach covers the case where the module loaded before jsdom
    // was installed — tests can now navigate reliably.
    this._attach();
    this._listeners.add(handler);
    return () => this._listeners.delete(handler);
  }

  _emit(path) {
    // Snapshot before iteration — a listener that unsubscribes itself (or
    // subscribes another) during dispatch would otherwise corrupt the live
    // Set. Matches the defensive pattern in Store._emitChange.
    for (const listener of Array.from(this._listeners)) {
      listener(path);
    }
  }
}

export const navigationManager = new NavigationManager();
