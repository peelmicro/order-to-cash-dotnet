import path from 'node:path';
import { defineConfig } from 'vitest/config';

// Tests that need the PRODUCTION build (`next build` output) served by a real
// `next start` — the framework behaviour a unit test cannot see: whether a
// route handler's stream is buffered or compressed on the way out, and whether
// a client disconnect really aborts the upstream request. Run by ./quality.sh
// after `pnpm build`.
export default defineConfig({
  resolve: { alias: { '@': path.resolve(import.meta.dirname, 'src') } },
  test: {
    environment: 'node',
    include: ['tests-integration/**/*.test.ts'],
    testTimeout: 60_000,
    hookTimeout: 90_000,
    fileParallelism: false,
  },
});
