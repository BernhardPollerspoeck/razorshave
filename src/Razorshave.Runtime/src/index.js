// @razorshave/runtime — public entry point.
// All types the transpiled Razorshave apps reference at runtime live here.

export { h, markup } from './h.js';
export { Component, LayoutComponent } from './component.js';
export { mount } from './mount.js';
export {
  EventArgs,
  MouseEventArgs,
  KeyboardEventArgs,
  ChangeEventArgs,
  FocusEventArgs,
} from './events.js';
export { PageTitle } from './builtins/page-title.js';
export { NavLink } from './builtins/nav-link.js';
export { Router, matchRoute, DefaultNotFound } from './router.js';
export { navigationManager } from './navigation-manager.js';
export { Container, container } from './container.js';
export { Store } from './store.js';
export { LocalStorage, SessionStorage, CookieStore } from './browser-storage.js';
export { ApiClient, ApiException } from './api-client.js';
export { reportRuntimeError, setErrorHandler, pushErrorHandler } from './errors.js';

// Transpiler-internal helpers from bcl.js are re-exported by name rather
// than via `export *` so every addition goes through an explicit review —
// a new helper that shouldn't be part of the public API gets noticed at
// this line instead of silently joining the export surface.
export { _isNullOrWhiteSpace, _isNullOrEmpty, _newGuid, _listRemove } from './bcl.js';

export const VERSION = '0.0.1';
