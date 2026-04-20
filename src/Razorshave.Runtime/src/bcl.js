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
