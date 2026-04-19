import { describe, it, expect, beforeEach, vi } from 'vitest';
import { Container, container as defaultContainer } from './container.js';

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
