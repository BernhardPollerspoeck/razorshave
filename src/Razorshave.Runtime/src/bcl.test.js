import { describe, it, expect } from 'vitest';
import { _isNullOrWhiteSpace, _isNullOrEmpty } from './bcl.js';

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
