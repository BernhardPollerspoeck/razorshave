// Singleton-only DI container matching the Razorshave design doc: every
// registration resolves to the same instance for the process, factories run
// once and their result is cached.
//
// A single module-level `container` is exported alongside the class so the
// production path stays boilerplate-free — register services at app start,
// forget about wiring afterwards. Tests import the class directly or reset
// the singleton via `container.clear()` between cases to avoid cross-test
// leakage.

export class Container {
  constructor() {
    this._factories = new Map();
    this._singletons = new Map();
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

  resolve(key) {
    if (this._singletons.has(key)) {
      return this._singletons.get(key);
    }
    const factory = this._factories.get(key);
    if (!factory) {
      throw new Error(`Razorshave: service '${key}' is not registered.`);
    }
    const instance = factory(this);
    this._singletons.set(key, instance);
    return instance;
  }

  has(key) {
    return this._factories.has(key) || this._singletons.has(key);
  }

  // Reset state — exposed primarily for test isolation.
  clear() {
    this._factories.clear();
    this._singletons.clear();
  }
}

export const container = new Container();
