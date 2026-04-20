import { describe, it, expect, beforeEach, vi } from 'vitest';
import { Container, container as defaultContainer } from './container.js';
import { Store } from './store.js';
import { LocalStorage, SessionStorage, CookieStore } from './browser-storage.js';

describe('Container', () => {
  let c;
  beforeEach(() => { c = new Container(); });

  it('throws when a service is not registered', () => {
    expect(() => c.resolve('missing')).toThrow(/not registered/);
  });

  it('resolves a registered instance directly', () => {
    const inst = { name: 'weatherApi' };
    c.register('IWeatherApi', inst);
    expect(c.resolve('IWeatherApi')).toBe(inst);
  });

  it('calls a factory exactly once and caches the result (singleton)', () => {
    const factory = vi.fn(() => ({ id: Math.random() }));
    c.register('IUserApi', factory);

    const a = c.resolve('IUserApi');
    const b = c.resolve('IUserApi');

    expect(factory).toHaveBeenCalledOnce();
    expect(a).toBe(b);
  });

  it('passes itself into the factory so services can resolve siblings', () => {
    c.register('HttpClient', { fetch: () => {} });
    c.register('IUserApi', (container) => ({ http: container.resolve('HttpClient') }));

    const api = c.resolve('IUserApi');
    expect(api.http).toBe(c.resolve('HttpClient'));
  });

  it('has() reports presence for both factories and pre-built instances', () => {
    c.register('A', () => ({}));
    c.register('B', { built: true });
    expect(c.has('A')).toBe(true);
    expect(c.has('B')).toBe(true);
    expect(c.has('missing')).toBe(false);
  });

  it('clear() empties all registrations', () => {
    c.register('X', () => ({}));
    c.resolve('X');
    c.clear();
    expect(c.has('X')).toBe(false);
    expect(() => c.resolve('X')).toThrow();
  });
});

describe('default container singleton', () => {
  // Exported `container` is the instance production code uses — tests share
  // the module singleton, so each test resets it explicitly.
  beforeEach(() => defaultContainer.clear());

  it('is a Container instance', () => {
    expect(defaultContainer).toBeInstanceOf(Container);
  });

  it('registrations survive across multiple resolves', () => {
    defaultContainer.register('X', () => 42);
    expect(defaultContainer.resolve('X')).toBe(42);
    expect(defaultContainer.resolve('X')).toBe(42);
  });
});

describe('auto-factory', () => {
  let c;
  beforeEach(() => { c = new Container(); });

  it('is consulted when no explicit registration exists', () => {
    c.registerAutoFactory(
      (key) => key.startsWith('Foo.'),
      (key) => ({ mintedFor: key })
    );
    const a = c.resolve('Foo.Bar');
    expect(a.mintedFor).toBe('Foo.Bar');
  });

  it('caches the minted instance as a singleton', () => {
    c.registerAutoFactory(() => true, () => ({}));
    expect(c.resolve('anything')).toBe(c.resolve('anything'));
  });

  it('explicit registrations take precedence over auto-factories', () => {
    const explicit = { explicit: true };
    c.register('key', explicit);
    c.registerAutoFactory(() => true, () => ({ auto: true }));
    expect(c.resolve('key')).toBe(explicit);
  });

  it('has() reports true when an auto-factory would match', () => {
    c.registerAutoFactory((k) => k === 'magic', () => ({}));
    expect(c.has('magic')).toBe(true);
    expect(c.has('other')).toBe(false);
  });
});

describe('default container IStore<T> auto-registration', () => {
  beforeEach(() => defaultContainer.clear());

  it('resolves IStore<User> to a shared Store instance', () => {
    const a = defaultContainer.resolve('IStore<User>');
    expect(a).toBeInstanceOf(Store);
    expect(defaultContainer.resolve('IStore<User>')).toBe(a);
  });

  it('different IStore<T> keys resolve to distinct stores', () => {
    const users = defaultContainer.resolve('IStore<User>');
    const orders = defaultContainer.resolve('IStore<Order>');
    expect(users).not.toBe(orders);
    users.set('k', 1);
    expect(orders.has('k')).toBe(false);
  });

  it('clear() removes auto-minted singletons but keeps the matcher active', () => {
    const before = defaultContainer.resolve('IStore<User>');
    defaultContainer.clear();
    const after = defaultContainer.resolve('IStore<User>');
    expect(after).toBeInstanceOf(Store);
    expect(after).not.toBe(before);
  });

  it('non-matching keys still throw', () => {
    expect(() => defaultContainer.resolve('SomeRandomService')).toThrow(/not registered/);
  });
});

describe('default container browser-storage bindings', () => {
  beforeEach(() => defaultContainer.clear());

  it('resolves ILocalStorage to a shared LocalStorage', () => {
    const a = defaultContainer.resolve('ILocalStorage');
    expect(a).toBeInstanceOf(LocalStorage);
    expect(defaultContainer.resolve('ILocalStorage')).toBe(a);
  });

  it('ILocalStorage and LocalStorage keys resolve to the same instance', () => {
    expect(defaultContainer.resolve('ILocalStorage')).toBe(defaultContainer.resolve('LocalStorage'));
  });

  it('ISessionStorage resolves to a SessionStorage singleton', () => {
    expect(defaultContainer.resolve('ISessionStorage')).toBeInstanceOf(SessionStorage);
    expect(defaultContainer.resolve('ISessionStorage')).toBe(defaultContainer.resolve('SessionStorage'));
  });

  it('ICookieStore resolves to a CookieStore singleton', () => {
    expect(defaultContainer.resolve('ICookieStore')).toBeInstanceOf(CookieStore);
    expect(defaultContainer.resolve('ICookieStore')).toBe(defaultContainer.resolve('CookieStore'));
  });
});
