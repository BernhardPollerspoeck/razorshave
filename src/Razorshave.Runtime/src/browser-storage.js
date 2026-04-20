// Synchronous wrappers around the browser's persistence primitives —
// localStorage, sessionStorage, document.cookie. Razorshave stays explicitly
// synchronous (unlike Blazored.LocalStorage) because the JS APIs are sync and
// we'd rather not fabricate a Task surface. Values are JSON-serialised so
// components can round-trip objects without stringifying at every call site.
//
// Each class accepts an optional backend in the constructor so tests can
// inject a stub and avoid sharing jsdom's global state between cases.

class WebStorageBase {
  constructor(backend) {
    this._backend = backend ?? null;
  }

  get(key) {
    if (!this._backend) return null;
    const raw = this._backend.getItem(key);
    if (raw === null) return null;
    try {
      return JSON.parse(raw);
    } catch {
      // Values written by code outside Razorshave won't be JSON — hand them
      // back as raw strings so we stay compatible with legacy data.
      return raw;
    }
  }

  set(key, value) {
    this._backend?.setItem(key, JSON.stringify(value));
  }

  remove(key) {
    this._backend?.removeItem(key);
  }

  has(key) {
    return !!this._backend && this._backend.getItem(key) !== null;
  }

  clear() {
    this._backend?.clear();
  }

  keys() {
    if (!this._backend) return [];
    const out = [];
    for (let i = 0; i < this._backend.length; i++) {
      out.push(this._backend.key(i));
    }
    return out;
  }

  get count() {
    return this._backend?.length ?? 0;
  }
}

export class LocalStorage extends WebStorageBase {
  constructor(backend = typeof localStorage !== 'undefined' ? localStorage : null) {
    super(backend);
  }
}

export class SessionStorage extends WebStorageBase {
  constructor(backend = typeof sessionStorage !== 'undefined' ? sessionStorage : null) {
    super(backend);
  }
}

// Cookies are a single delimited string on document.cookie — this class hides
// the parsing/escaping dance and exposes a Map-like surface. `remove` is a
// set with max-age=0, matching the canonical "expire the cookie" idiom.
//
// The parsed view is cached against the raw `document.cookie` string so
// repeated `get`/`has`/`getAll` calls in a render loop don't re-parse the
// same string each time. Invalidation is automatic: if `document.cookie`
// changed since the cache was written, the next read re-parses.
export class CookieStore {
  constructor(doc = typeof document !== 'undefined' ? document : null) {
    this._doc = doc;
    this._cachedRaw = null;
    this._cachedParsed = null;
  }

  get(name) {
    const all = this._parse();
    return name in all ? all[name] : null;
  }

  has(name) {
    return name in this._parse();
  }

  getAll() {
    return this._parse();
  }

  // opts: { path, maxAge, expires, secure, sameSite } — path defaults to '/'
  // so a cookie set on one page is visible across the SPA (otherwise the
  // browser scopes it to the current URL path, which almost never matches
  // the user's intent in a routed app).
  set(name, value, opts = {}) {
    if (!this._doc) return;
    const parts = [`${encodeURIComponent(name)}=${encodeURIComponent(String(value))}`];
    parts.push(`path=${opts.path ?? '/'}`);
    if (opts.maxAge !== undefined) parts.push(`max-age=${opts.maxAge}`);
    if (opts.expires) {
      const d = opts.expires instanceof Date ? opts.expires : new Date(opts.expires);
      parts.push(`expires=${d.toUTCString()}`);
    }
    if (opts.domain) parts.push(`domain=${opts.domain}`);
    if (opts.sameSite) parts.push(`samesite=${opts.sameSite}`);
    if (opts.secure) parts.push('secure');
    this._doc.cookie = parts.join('; ');
  }

  remove(name, opts = {}) {
    this.set(name, '', { ...opts, maxAge: 0 });
  }

  _parse() {
    const raw = this._doc?.cookie ?? '';
    if (this._cachedRaw === raw && this._cachedParsed !== null) {
      return this._cachedParsed;
    }
    const out = {};
    if (!raw) {
      this._cachedRaw = raw;
      this._cachedParsed = out;
      return out;
    }
    for (const pair of raw.split(';')) {
      const trimmed = pair.trim();
      if (!trimmed) continue;
      const eq = trimmed.indexOf('=');
      const k = eq === -1 ? trimmed : trimmed.slice(0, eq);
      const v = eq === -1 ? '' : trimmed.slice(eq + 1);
      try {
        out[decodeURIComponent(k)] = decodeURIComponent(v);
      } catch {
        // A cookie set outside Razorshave may contain bytes that aren't valid
        // percent-encoding — keep the raw value rather than dropping it.
        out[k] = v;
      }
    }
    this._cachedRaw = raw;
    this._cachedParsed = out;
    return out;
  }
}
