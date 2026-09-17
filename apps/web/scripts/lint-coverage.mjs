// Guard for the defect #7's web lint shipped with (review_web_app.md Pass 2a,
// Finding 1): `eslint` exited 0 while linting ZERO component files, because
// its config globs never matched the files from the directory it ran in.
//
// The expected population is enumerated from the FILESYSTEM, not from ESLint's
// own view of the project — a file ESLint silently skips must show up here as
// a failure, never simply be absent from the list (CLAUDE.md: "a sweep must not
// filter by the property it is testing"). For every source file it asserts:
//   1. ESLint does not ignore it, and
//   2. the resolved config for it carries this app's own rules — so a file
//      that matches only a bare default block (no Next.js / React rules, no
//      money rule) fails too.
import { readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { ESLint } from 'eslint';

const appRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

// Directories never linted, by PATH (never by content).
const EXCLUDED_DIRS = new Set(['node_modules', '.next', 'coverage', 'generated']);
const EXTENSIONS = new Set(['.ts', '.tsx', '.mts', '.mjs']);

async function enumerate(dir) {
  const out = [];
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (EXCLUDED_DIRS.has(entry.name) || entry.name.startsWith('.')) continue;
      out.push(...(await enumerate(path.join(dir, entry.name))));
    } else if (EXTENSIONS.has(path.extname(entry.name)) && entry.name !== 'next-env.d.ts') {
      out.push(path.join(dir, entry.name));
    }
  }
  return out;
}

const eslint = new ESLint({ cwd: appRoot });
const files = await enumerate(appRoot);
const failures = [];

for (const file of files) {
  const rel = path.relative(appRoot, file);
  if (await eslint.isPathIgnored(file)) {
    failures.push(`${rel}: ignored by ESLint`);
    continue;
  }
  const config = await eslint.calculateConfigForFile(file);
  const rules = config?.rules ?? {};
  const required = ['@typescript-eslint/no-unused-vars'];
  if (rel.startsWith('src/')) {
    required.push('no-console', '@next/next/no-html-link-for-pages');
    // Literal exemptions, stated here rather than inferred from the config:
    // the one module allowed to import the generated types, and test code,
    // which may build float fixtures to demonstrate the float failures.
    if (rel !== 'src/lib/api-types.ts') required.push('no-restricted-imports');
    if (!rel.startsWith('src/test/') && !/\.test\.tsx?$/.test(rel)) required.push('no-restricted-globals', 'no-restricted-properties');
  }
  if (rel.endsWith('.tsx')) required.push('react-hooks/rules-of-hooks');
  const missing = required.filter((rule) => rules[rule] === undefined || rules[rule] === 'off' || (Array.isArray(rules[rule]) && (rules[rule][0] === 0 || rules[rule][0] === 'off')));
  if (missing.length > 0) failures.push(`${rel}: resolved config lacks ${missing.join(', ')}`);
}

const srcCount = files.filter((f) => path.relative(appRoot, f).startsWith('src/')).length;
if (srcCount === 0) failures.push('no files found under src/ — the enumeration itself is broken');

if (failures.length > 0) {
  console.error(`lint-coverage FAILED — ${failures.length} of ${files.length} source files are not really linted:`);
  for (const failure of failures) console.error(`  ${failure}`);
  process.exitCode = 1;
} else {
  console.log(`lint-coverage OK — all ${files.length} source files (${srcCount} under src/) are linted with this app's rules.`);
}
