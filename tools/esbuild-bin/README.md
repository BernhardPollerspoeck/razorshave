# Pinned esbuild binaries

These binaries are bundled into the `Razorshave.Cli` NuGet package so the
transpiler's final bundling step works out-of-the-box without requiring the
user to install Node.js / npm.

## Version

**esbuild 0.28.0** — matches the `^0.28.0` range in
`src/Razorshave.Runtime/package.json` (pinned via `package-lock.json`).

## Source

Fetched from the official npm registry:

| RID | npm package | in-archive path |
|-----|-------------|-----------------|
| `win-x64`   | `@esbuild/win32-x64`   | `package/esbuild.exe` |
| `linux-x64` | `@esbuild/linux-x64`   | `package/bin/esbuild` |
| `osx-x64`   | `@esbuild/darwin-x64`  | `package/bin/esbuild` |
| `osx-arm64` | `@esbuild/darwin-arm64`| `package/bin/esbuild` |

## Updating

When the JS runtime's esbuild version changes, refresh every binary here.
The Cli-pack target reads from this folder unconditionally — forgetting to
update a platform means that platform's consumers silently get the old esbuild.

```sh
# Quick-fetch script for three-of-four (linux-x64 can come from local
# node_modules/ after `npm install` in src/Razorshave.Runtime):
VERSION=0.28.0
for variant in win32-x64 darwin-x64 darwin-arm64; do
  curl -sL "https://registry.npmjs.org/@esbuild/$variant/-/$variant-$VERSION.tgz" \
    | tar -xzO "$(if [ $variant = win32-x64 ]; then echo package/esbuild.exe; else echo package/bin/esbuild; fi)" \
    > <target-rid>/esbuild<.exe if win>
done
```
