// Singleton-only DI container matching the Razorshave design doc: every
// registration resolves to the same instance for the process, factories run
// once and their result is cached.
//
// A single module-level `container` is exported alongside the class so the
// production path stays boilerplate-free — register services at app start,
// forget about wiring afterwards. Tests import the class directly or reset
// the singleton via `container.clear()` between cases to avoid cross-test
// leakage.
//
// Auto-factories: a small extension point that lets the container mint a
// service on demand when nothing matches a key directly. The default export
// ships with an IStore<T> matcher so `@inject IStore<User> Users` just works
// without any user-side registration — every unique IStore<T> key yields its
// own shared Store singleton.

import { Store } from './store.js';
import { LocalStorage, SessionStorage, CookieStore } from './browser-storage.js';

export class Container {
  constructor() {
    this._factories = new Map();
    this._singletons = new Map();
    // Array<{ test: (key) => boolean, factory: (key, container) => instance }>
    // Consulted only when a key has no explicit factory/instance registered.
    // Matches are cached via the singleton map, so each key resolves once.
    this._autoFactories = [];
  }

  // factoryOrInstance can be either a `(c) => instance` factory (lazy, called
  // once on first resolve) or a ready-made instance (cached immediately).
  register(key, factoryOrInstance) {
    if (typeof factoryOrInstance === 'function') {
      this._factories.set(key, factoryOrInstance);
    } else {
      this._singletons.set(key, factoryOrInstance);
    }
  }

  // Registers a fallback factory consulted when no explicit registration
  // matches. Used for open-generic services like IStore<T> where the number
  // of concrete keys is unbounded and the factory is the same shape.
  registerAutoFactory(test, factory) {
    this._autoFactories.push({ test, factory });
  }

  resolve(key) {
    if (this._singletons.has(key)) {
      return this._singletons.get(key);
    }
    const factory = this._factories.get(key);
    if (factory) {
      const instance = factory(this);
      this._singletons.set(key, instance);
      return instance;
    }
    for (const auto of this._autoFactories) {
      if (auto.test(key)) {
        const instance = auto.factory(key, this);
        this._singletons.set(key, instance);
        return instance;
      }
    }
    throw new Error(`Razorshave: service '${key}' is not registered.`);
  }

  // Returns the resolved service, or `undefined` if nothing matches — no
  // throw. Use when a service is optional (feature flags, debug sinks,
  // telemetry); avoids the two-call `has(k) ? resolve(k) : null` TOCTOU.
  tryResolve(key) {
    return this.has(key) ? this.resolve(key) : undefined;
  }

  has(key) {
    if (this._factories.has(key) || this._singletons.has(key)) return true;
    return this._autoFactories.some(a => a.test(key));
  }

  // Reset state — exposed primarily for test isolation. Auto-factories are
  // re-seeded with the default IStore matcher so the container behaves like
  // a fresh export.
  //
  // Caveat: code that already resolved a service holds a live reference
  // that WON'T pick up the new registration — clear() only affects the
  // container's own cache, not downstream captures. Safe for test-between
  // resets (nothing alive across `beforeEach` boundaries), dangerous to
  // call mid-application (existing components continue to see the old
  // instance while new resolves see the new one).
  clear() {
    this._factories.clear();
    this._singletons.clear();
    this._autoFactories = [];
    seedDefaults(this);
  }
}

function seedDefaults(c) {
  // Recognises both the .NET-style key (`IStore<User>`) emitted by the C#
  // transpiler and a bare `IStore` for JS-side registrations.
  c.registerAutoFactory(
    (key) => typeof key === 'string' && /^IStore(<.+>)?$/.test(key),
    () => new Store()
  );

  // Browser-persistence wrappers. Lazy factories so the singletons don't
  // construct (and don't touch document/localStorage) until something asks.
  // Accept both the `I`-prefixed interface name and the bare class name so
  // the C# `[Inject] ILocalStorage` and a JS-side `container.resolve('LocalStorage')`
  // both hit the same instance.
  const browserBindings = [
    [['ILocalStorage', 'LocalStorage'], () => new LocalStorage()],
    [['ISessionStorage', 'SessionStorage'], () => new SessionStorage()],
    [['ICookieStore', 'CookieStore'], () => new CookieStore()],
  ];
  for (const [keys, factory] of browserBindings) {
    let cached = null;
    const shared = () => (cached ??= factory());
    for (const key of keys) c.register(key, shared);
  }
}

export const container = new Container();
seedDefaults(container);
