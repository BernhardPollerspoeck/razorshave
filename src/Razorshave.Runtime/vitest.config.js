import { defineConfig } from 'vitest/config';
import { fileURLToPath, URL } from 'node:url';

export default defineConfig({
  resolve: {
    alias: {
      // The transpiler emits `from '@razorshave/runtime'` in every verified.js
      // snapshot. Mapping the bare specifier to our runtime's ESM entry lets
      // the integration tests import those snapshots verbatim.
      '@razorshave/runtime': fileURLToPath(new URL('./src/index.js', import.meta.url)),
    },
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.js'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html', 'lcov'],
      include: ['src/**/*.js'],
      exclude: ['src/**/*.test.js'],
      thresholds: {
        lines: 90,
        functions: 90,
        branches: 85,
        statements: 90,
      },
    },
  },
});
