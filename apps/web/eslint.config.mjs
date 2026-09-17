// apps/web's ESLint flat config. It lives HERE, beside the files it lints, and
// every `files` glob is relative to this directory — the defect #7's review
// found (review_web_app.md Pass 2a, Finding 1) was a config at the repository
// root whose `apps/web/**` globs matched nothing when ESLint ran from apps/web,
// so `lint` exited 0 while linting zero component files. `scripts/lint-coverage.mjs`
// (chained into `pnpm lint`) is the guard that this config actually reaches
// every source file.
import { defineConfig, globalIgnores } from 'eslint/config';
import nextVitals from 'eslint-config-next/core-web-vitals';
import nextTs from 'eslint-config-next/typescript';

export default defineConfig([
  ...nextVitals,
  ...nextTs,
  globalIgnores(['.next/**', 'node_modules/**', 'coverage/**', 'next-env.d.ts', 'src/generated/**']),
  {
    files: ['**/*.{ts,tsx,mts,mjs}'],
    rules: {
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
      'no-console': ['error', { allow: ['warn', 'error'] }],
    },
  },
  {
    // CLI scripts report on stdout by design.
    files: ['scripts/**/*.mjs'],
    rules: { 'no-console': 'off' },
  },
  {
    // Money never goes through floating point (CLAUDE.md "Money is long minor
    // units"; #7 review_web_app.md Pass 5 — the unit-price float bug).
    // Decimal strings are parsed digit-wise in src/lib/money.ts instead.
    files: ['src/**/*.{ts,tsx}'],
    ignores: ['src/**/*.test.{ts,tsx}', 'src/test/**'],
    rules: {
      'no-restricted-globals': ['error', { name: 'parseFloat', message: 'Money is integer minor units — parse decimal strings with parseDecimalToMinorUnits (src/lib/money.ts).' }],
      'no-restricted-properties': [
        'error',
        { object: 'Number', property: 'parseFloat', message: 'Money is integer minor units — parse decimal strings with parseDecimalToMinorUnits (src/lib/money.ts).' },
        { property: 'toFixed', message: 'toFixed formats through a float — format minor units with formatMinorUnits (src/lib/money.ts).' },
      ],
    },
  },
  {
    // The generated OpenAPI file is imported by exactly one module; everything
    // else takes its wire shapes from src/lib/api-types.ts.
    files: ['src/**/*.{ts,tsx}'],
    ignores: ['src/lib/api-types.ts'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            { group: ['@/generated/*', '**/generated/*'], message: 'Import wire shapes from @/lib/api-types — it is the only importer of the generated OpenAPI types.' },
          ],
        },
      ],
    },
  },
]);
