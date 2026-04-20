import { describe, it, expect, vi } from 'vitest';
import { Store } from './store.js';

describe('Store', () => {
  it('set/get/has/delete round-trips a value and tracks count', () => {
    const s = new Store();
    expect(s.count).toBe(0);

    s.set('a', { id: 1 });
    expect(s.has('a')).toBe(true);
    expect(s.get('a')).toEqual({ id: 1 });
    expect(s.count).toBe(1);

    expect(s.delete('a')).toBe(true);
    expect(s.has('a')).toBe(false);
    expect(s.count).toBe(0);
  });

  it('delete on missing key is a no-op and returns false', () => {
    const s = new Store();
    const listener = vi.fn();
    s.onChange(listener);

    expect(s.delete('ghost')).toBe(false);
    expect(listener).not.toHaveBeenCalled();
  });

  it('getAll returns values in insertion order', () => {
    const s = new Store();
    s.set('a', 1); s.set('b', 2); s.set('c', 3);
    expect(s.getAll()).toEqual([1, 2, 3]);
  });

  it('where filters by predicate', () => {
    const s = new Store();
    s.set('a', { age: 20 });
    s.set('b', { age: 30 });
    s.set('c', { age: 40 });
    expect(s.where(x => x.age >= 30)).toHaveLength(2);
  });

  it('onChange fires on every mutation and the returned fn unsubscribes', () => {
    const s = new Store();
    const listener = vi.fn();
    const off = s.onChange(listener);

    s.set('a', 1);
    s.set('a', 2);
    s.delete('a');
    expect(listener).toHaveBeenCalledTimes(3);

    off();
    s.set('b', 1);
    expect(listener).toHaveBeenCalledTimes(3);
  });

  it('clear on an empty store does not notify; clear on non-empty does', () => {
    const s = new Store();
    const listener = vi.fn();
    s.onChange(listener);

    s.clear();
    expect(listener).not.toHaveBeenCalled();

    s.set('a', 1);
    listener.mockClear();
    s.clear();
    expect(s.count).toBe(0);
    expect(listener).toHaveBeenCalledOnce();
  });

  it('batch collapses multiple mutations into a single onChange', () => {
    const s = new Store();
    const listener = vi.fn();
    s.onChange(listener);

    s.batch(() => {
      s.set('a', 1);
      s.set('b', 2);
      s.delete('a');
    });
    expect(listener).toHaveBeenCalledOnce();
  });

  it('a batch that mutates nothing does not fire onChange', () => {
    const s = new Store();
    const listener = vi.fn();
    s.onChange(listener);

    s.batch(() => {});
    expect(listener).not.toHaveBeenCalled();
  });

  it('a batch that throws mid-way does not emit a change for the partial state', () => {
    // Regression for the error-path gap: partial mutation + change-notify
    // would surface half-applied state to the UI. On throw we drop the
    // dirty flag and rethrow; no listeners fire.
    const s = new Store();
    const listener = vi.fn();
    s.onChange(listener);

    expect(() => s.batch(() => {
      s.set('a', 1);
      throw new Error('boom');
    })).toThrow('boom');

    expect(listener).not.toHaveBeenCalled();
    // State IS partially mutated (rollback is caller's responsibility),
    // but no notification fires for the incoherent frame.
    expect(s.get('a')).toBe(1);
  });

  it('nested batch only fires once after the outer exits', () => {
    const s = new Store();
    const listener = vi.fn();
    s.onChange(listener);

    s.batch(() => {
      s.set('a', 1);
      s.batch(() => { s.set('b', 2); });
      expect(listener).not.toHaveBeenCalled();
    });
    expect(listener).toHaveBeenCalledOnce();
  });

  it('a listener that unsubscribes itself during dispatch does not throw', () => {
    const s = new Store();
    const offs = [];
    const a = vi.fn(() => offs[0]());
    const b = vi.fn();
    offs.push(s.onChange(a));
    s.onChange(b);

    s.set('x', 1);
    expect(a).toHaveBeenCalledOnce();
    expect(b).toHaveBeenCalledOnce();
  });
});
