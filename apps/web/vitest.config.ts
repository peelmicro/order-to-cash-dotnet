import path from 'node:path';
import { defineConfig } from 'vitest/config';

// Unit + component tests. Vitest is the web runner (CLAUDE.md: no Jest anywhere).
// Files that exercise real sockets opt into the node environment per file.
export default defineConfig({
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, 'src'),
      // `server-only` throws when imported outside a React Server environment;
      // route handlers are tested directly under Node, so the marker is a no-op here.
      'server-only': path.resolve(import.meta.dirname, 'src/test/server-only-stub.ts'),
    },
  },
  oxc: {
    jsx: { runtime: 'automatic' },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}', 'scripts/**/*.test.ts'],
    restoreMocks: true,
    // Component tests drive real user-event keystrokes; the default 5 s is too tight on a loaded machine.
    testTimeout: 20_000,
    coverage: {
      provider: 'v8',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/generated/**', 'src/components/ui/**', 'src/test/**', 'src/**/*.test.{ts,tsx}'],
      reporter: ['text-summary', 'json-summary', 'cobertura'],
    },
  },
});
