// .NET Base Class Library bridges.
//
// Every helper here corresponds to a specific C# BCL member that the
// transpiler cannot express with a 1:1 JS primitive. Keeping them in one
// place gives the transpiler a stable target (emit `_isNullOrWhiteSpace(x)`
// rather than inline IIFEs) and gives future additions (`_format`, LINQ
// fallbacks, DateTime arithmetic) a clear home.
//
// The underscore-prefix naming matches the convention we already use for
// transpiler-internal runtime calls (e.g. `_bound` on Component); user code
// never calls these directly.
//
// Every function takes its argument exactly once, so emitting
// `_isNullOrWhiteSpace(GetUser().Name)` runs `GetUser()` once — unlike the
// inline `x == null || x.trim() === ""` rewrite which emitted `x` twice.

// `string.IsNullOrWhiteSpace(x)` — true when x is null, undefined, or
// contains only whitespace. Mirrors .NET's definition exactly: null/empty
// match plus any string that trim()s to the empty string.
export function _isNullOrWhiteSpace(value) {
  return value == null || (typeof value === 'string' && value.trim() === '');
}

// `string.IsNullOrEmpty(x)` — true when x is null, undefined, or the empty
// string. Does NOT coerce non-string inputs to strings; a `0` or `false`
// argument returns false, matching .NET's `string`-typed signature.
export function _isNullOrEmpty(value) {
  return value == null || value === '';
}

// `Guid.NewGuid()` — `crypto.randomUUID()` is the right answer, but it throws
// in non-secure contexts (http://, file://, some srcdoc iframes). Blazor apps
// are commonly bootstrapped from `dotnet run` on http://localhost, so the
// secure-context guarantee isn't available across our whole usage surface.
// Prefer the real thing, fall back to a Math.random-based UUIDv4 and warn
// once so the degraded entropy isn't silent. The fallback is NOT cryptographic
// and must not be used for security-sensitive identifiers — but for UI keys
// and client-generated record IDs it matches the C# Guid contract (unique
// enough, valid v4 shape).
let _warnedGuidFallback = false;
export function _newGuid() {
  const c = typeof crypto !== 'undefined' ? crypto : null;
  if (c && typeof c.randomUUID === 'function') {
    try { return c.randomUUID(); } catch { /* secure-context failure — fall through */ }
  }
  if (!_warnedGuidFallback) {
    _warnedGuidFallback = true;
    // eslint-disable-next-line no-console
    console.warn(
      '[razorshave] crypto.randomUUID() unavailable (non-secure context?). '
      + 'Falling back to Math.random-based UUIDv4 — valid shape, weak entropy. '
      + 'Do not use for security-sensitive identifiers.'
    );
  }
  // RFC 4122 v4 shape, Math.random digits. Good enough for UI keys, not for
  // anything that must be unguessable.
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (ch) => {
    const r = Math.random() * 16 | 0;
    const v = ch === 'x' ? r : (r & 0x3 | 0x8);
    return v.toString(16);
  });
}

// `List<T>.Remove(item)` — .NET signature returns bool (true if removed).
// Mirrors by indexOf + splice. Both arguments evaluate exactly once at the
// call site (the transpiler routes through here specifically to avoid the
// double-evaluation the naive inline rewrite would cause).
export function _listRemove(arr, item) {
  if (!arr) return false;
  const i = arr.indexOf(item);
  if (i < 0) return false;
  arr.splice(i, 1);
  return true;
}
