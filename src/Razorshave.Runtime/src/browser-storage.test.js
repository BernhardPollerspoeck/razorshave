// @vitest-environment jsdom
import { describe, it, expect, beforeEach } from 'vitest';
import { LocalStorage, SessionStorage, CookieStore } from './browser-storage.js';

function makeFakeBackend() {
  const data = new Map();
  return {
    getItem(k) { return data.has(k) ? data.get(k) : null; },
    setItem(k, v) { data.set(k, String(v)); },
    removeItem(k) { data.delete(k); },
    clear() { data.clear(); },
    key(i) { return Array.from(data.keys())[i] ?? null; },
    get length() { return data.size; },
  };
}

describe.each([
  ['LocalStorage', LocalStorage],
  ['SessionStorage', SessionStorage],
])('%s', (_name, Cls) => {
  let store;
  beforeEach(() => { store = new Cls(makeFakeBackend()); });

  it('round-trips objects through JSON', () => {
    store.set('user', { id: 1, name: 'Ada' });
    expect(store.get('user')).toEqual({ id: 1, name: 'Ada' });
  });

  it('returns null for missing keys', () => {
    expect(store.get('missing')).toBeNull();
  });

  it('has() reflects set/remove', () => {
    expect(store.has('k')).toBe(false);
    store.set('k', 1);
    expect(store.has('k')).toBe(true);
    store.remove('k');
    expect(store.has('k')).toBe(false);
  });

  it('keys() and count reflect contents', () => {
    store.set('a', 1);
    store.set('b', 2);
    expect(store.keys().sort()).toEqual(['a', 'b']);
    expect(store.count).toBe(2);
    store.clear();
    expect(store.count).toBe(0);
  });

  it('returns raw string when a value was not stored as JSON', () => {
    // Simulate legacy data written by non-Razorshave code.
    const backend = makeFakeBackend();
    backend.setItem('plain', 'hello');
    const s = new Cls(backend);
    expect(s.get('plain')).toBe('hello');
  });

  it('silently no-ops when there is no backend', () => {
    const s = new Cls(null);
    expect(() => s.set('k', 1)).not.toThrow();
    expect(s.get('k')).toBeNull();
    expect(s.has('k')).toBe(false);
    expect(s.keys()).toEqual([]);
    expect(s.count).toBe(0);
  });
});

describe('CookieStore', () => {
  let fakeDoc;
  let store;

  beforeEach(() => {
    fakeDoc = {
      _cookies: new Map(),
      get cookie() {
        return Array.from(fakeDoc._cookies.entries()).map(([k, v]) => `${k}=${v}`).join('; ');
      },
      set cookie(str) {
        const [nameValue, ...attrs] = str.split(';').map(s => s.trim());
        const eq = nameValue.indexOf('=');
        const name = nameValue.slice(0, eq);
        const value = nameValue.slice(eq + 1);
        const ageAttr = attrs.find(a => a.toLowerCase().startsWith('max-age='));
        if (ageAttr && ageAttr.split('=')[1] === '0') {
          fakeDoc._cookies.delete(name);
        } else {
          fakeDoc._cookies.set(name, value);
        }
      },
    };
    store = new CookieStore(fakeDoc);
  });

  it('set writes a URL-encoded cookie with path=/ by default', () => {
    store.set('user', 'Ada Lovelace');
    expect(fakeDoc._cookies.get('user')).toBe('Ada%20Lovelace');
  });

  it('get decodes percent-escaped values', () => {
    store.set('greeting', 'hallo welt');
    expect(store.get('greeting')).toBe('hallo welt');
  });

  it('has/remove works end-to-end', () => {
    store.set('token', 'abc');
    expect(store.has('token')).toBe(true);
    store.remove('token');
    expect(store.has('token')).toBe(false);
  });

  it('getAll returns a snapshot of every cookie as a plain object', () => {
    store.set('a', '1');
    store.set('b', '2');
    expect(store.getAll()).toEqual({ a: '1', b: '2' });
  });

  it('get returns null for missing cookies', () => {
    expect(store.get('ghost')).toBeNull();
  });

  it('set with options serialises max-age, expires, secure, samesite', () => {
    let lastWritten = '';
    const stubDoc = {
      get cookie() { return ''; },
      set cookie(s) { lastWritten = s; },
    };
    const s = new CookieStore(stubDoc);
    s.set('k', 'v', { maxAge: 3600, secure: true, sameSite: 'Strict', path: '/app' });
    expect(lastWritten).toContain('path=/app');
    expect(lastWritten).toContain('max-age=3600');
    expect(lastWritten).toContain('samesite=Strict');
    expect(lastWritten).toContain('secure');
  });

  it('silently no-ops when there is no document', () => {
    const s = new CookieStore(null);
    expect(() => s.set('k', 'v')).not.toThrow();
    expect(s.get('k')).toBeNull();
    expect(s.getAll()).toEqual({});
  });
});
