# @razorshave/runtime

JavaScript runtime shipped (embedded) with the Razorshave CLI and bundled into every transpiled SPA.

## Layout

- `src/` — runtime source, pure ESM
- `src/**/*.test.js` — Vitest tests alongside the files they cover
- `vitest.config.js` — `node` default environment; tests that need DOM APIs opt in per file via `// @vitest-environment jsdom`
- `eslint.config.js` — flat config extending `@eslint/js` recommended

## Scripts

```
npm test             # vitest run
npm run test:watch   # vitest watch
npm run test:coverage
npm run lint
```

Runtime source is pure JavaScript — no TypeScript, no build step.
