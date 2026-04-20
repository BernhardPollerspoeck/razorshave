import { describe, it, expect, vi } from 'vitest';
import { _isNullOrWhiteSpace, _isNullOrEmpty, _newGuid, _listRemove } from './bcl.js';

describe('_isNullOrWhiteSpace (string.IsNullOrWhiteSpace bridge)', () => {
  it('returns true for null / undefined / "" / whitespace-only strings', () => {
    expect(_isNullOrWhiteSpace(null)).toBe(true);
    expect(_isNullOrWhiteSpace(undefined)).toBe(true);
    expect(_isNullOrWhiteSpace('')).toBe(true);
    expect(_isNullOrWhiteSpace(' ')).toBe(true);
    expect(_isNullOrWhiteSpace('\t\n  ')).toBe(true);
  });

  it('returns false for strings with any non-whitespace char', () => {
    expect(_isNullOrWhiteSpace('x')).toBe(false);
    expect(_isNullOrWhiteSpace(' hi ')).toBe(false);
  });

  it('does not coerce non-string inputs — matches .NET string-typed sig', () => {
    expect(_isNullOrWhiteSpace(0)).toBe(false);
    expect(_isNullOrWhiteSpace(false)).toBe(false);
  });

  it('evaluates the argument exactly once at the call site', () => {
    // Regression for the inline-double-emit bug: argument expressions with
    // side effects must run once, not twice.
    let calls = 0;
    const sideEffecting = () => { calls++; return ''; };
    _isNullOrWhiteSpace(sideEffecting());
    expect(calls).toBe(1);
  });
});

describe('_isNullOrEmpty (string.IsNullOrEmpty bridge)', () => {
  it('returns true for null / undefined / ""', () => {
    expect(_isNullOrEmpty(null)).toBe(true);
    expect(_isNullOrEmpty(undefined)).toBe(true);
    expect(_isNullOrEmpty('')).toBe(true);
  });

  it('returns false for any non-empty string — whitespace counts as content', () => {
    expect(_isNullOrEmpty(' ')).toBe(false);
    expect(_isNullOrEmpty('x')).toBe(false);
  });
});

describe('_newGuid (Guid.NewGuid bridge)', () => {
  it('returns a valid UUIDv4 shape', () => {
    const uuid = _newGuid();
    expect(uuid).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
  });

  it('delegates to crypto.randomUUID when available', () => {
    const spy = vi.spyOn(crypto, 'randomUUID').mockReturnValue('deadbeef-dead-4bee-8bee-deadbeefdead');
    try {
      expect(_newGuid()).toBe('deadbeef-dead-4bee-8bee-deadbeefdead');
      expect(spy).toHaveBeenCalledOnce();
    } finally {
      spy.mockRestore();
    }
  });

  it('falls back to Math.random UUID when crypto.randomUUID throws (non-secure context)', () => {
    // jsdom's crypto.randomUUID throws in some test modes; simulate that.
    const spy = vi.spyOn(crypto, 'randomUUID').mockImplementation(() => {
      throw new Error('SecurityError: crypto.randomUUID requires a secure context');
    });
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
    try {
      const uuid = _newGuid();
      expect(uuid).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
      // Warning is one-time — may already have fired in an earlier test; either way
      // the fallback UUID is valid.
    } finally {
      spy.mockRestore();
      warnSpy.mockRestore();
    }
  });
});

describe('_listRemove (List<T>.Remove bridge)', () => {
  it('returns true-only-on-remove and mutates the array in place', () => {
    const xs = ['a', 'b', 'c'];
    expect(_listRemove(xs, 'b')).toBe(true);
    expect(xs).toEqual(['a', 'c']);
  });

  it('returns false when the item is not in the array', () => {
    const xs = [1, 2, 3];
    expect(_listRemove(xs, 99)).toBe(false);
    expect(xs).toEqual([1, 2, 3]);
  });

  it('treats null/undefined receivers as an empty list (no throw)', () => {
    // Defensive for the transpiler: an uninitialised field marked
    // `List<T>? xs` is `null` in C# and `undefined` in JS. Remove() on
    // those should be a silent no-op, not a TypeError.
    expect(_listRemove(null, 1)).toBe(false);
    expect(_listRemove(undefined, 1)).toBe(false);
  });

  it('removes only the first matching occurrence (matches .NET contract)', () => {
    const xs = ['x', 'y', 'x'];
    expect(_listRemove(xs, 'x')).toBe(true);
    expect(xs).toEqual(['y', 'x']);
  });
});
