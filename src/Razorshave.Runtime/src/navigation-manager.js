// Singleton wrapper around the browser's history/location API.
//
// `navigateTo(path)` pushes a history entry and notifies subscribers — the
// built-in Router listens here so it can re-render on programmatic
// navigation. The browser's native popstate event covers back/forward; the
// Router listens there separately.
//
// Guarded against SSR / non-browser environments with `typeof window` checks
// so importing this module in Node (e.g., Vitest config files) doesn't blow up.

const hasWindow = typeof window !== 'undefined';

class NavigationManager {
  constructor() {
    this._listeners = new Set();
  }

  get uri() {
    return hasWindow ? window.location.href : '';
  }

  get pathname() {
    return hasWindow ? window.location.pathname : '/';
  }

  navigateTo(path) {
    if (!hasWindow) return;
    window.history.pushState(null, '', path);
    this._emit(path);
  }

  // Listener set is snapshotted before dispatch in `_emit` — same semantics
  // as `Store.onChange`: subscribing during dispatch does NOT see the current
  // event, only subsequent ones; unsubscribing during dispatch is safe.
  onLocationChanged(handler) {
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
