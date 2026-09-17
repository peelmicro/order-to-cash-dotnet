// Backlog id 99 — the web app names its stack, from ONE definition, in three
// places: the header of every signed-in page, the sign-in page, and the
// browser-tab title.
//
// Units: the "every signed-in page" claim is about PAGES, so every page file on
// disk is rendered in its real layout chain (src/test/page-sweep.tsx) and the
// label is looked for where the claim puts it. The public set is a literal and
// everything else is derived by subtraction, so a page that stops being wrapped
// by the signed-in layout FAILS here rather than leaving the population.
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it, vi } from 'vitest';
import { metadata } from '@/app/layout';
import { STACK_LABEL } from '@/lib/stack-label';
import { ROUTE_FILES, runRoute, WEB_ROOT, type ActionScript } from '@/test/page-sweep';

// The (app) layout reads the session on the server; a signed-in session is given, as in error-text-sweep.test.tsx.
vi.mock('next/headers', () => ({
  cookies: async () => ({ get: () => undefined, getAll: () => [], has: () => false }),
  headers: async () => new Headers(),
}));
vi.mock('@/server/session', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/server/session')>()),
  readSession: async () => ({ accessToken: 'test-token', expiresAt: Date.now() + 60_000, username: 'operator', displayName: 'Operator', roles: [] }),
}));

/** Pinned here, not imported: substituting the definition (say, with #7's label) must fail too. */
const EXPECTED_LABEL = '#8 · .NET / Next.js';

/** Where each page must show the label. Pages NOT listed are signed-in pages and must show it in the header. */
const PUBLIC_PAGES: Record<string, 'redirect' | 'login'> = {
  '/': 'redirect',
  '/login': 'login',
};

const TIMEOUT = 60_000;
const pages = ROUTE_FILES.filter((file) => file.kind === 'page');

/** Where the label was found, captured while the page is still mounted. */
function captureScript(route: string, into: string[]): ActionScript {
  return {
    route,
    name: 'capture stack label',
    run: async () => {
      for (const element of document.querySelectorAll('[data-testid="stack-label"]')) {
        into.push(`${element.closest('header') ? 'header' : element.closest('main') ? 'main' : 'elsewhere'}: ${element.textContent}`);
      }
    },
  };
}

describe('the stack label (id 99)', () => {
  it('is the reviewed text', () => {
    expect(STACK_LABEL, 'src/lib/stack-label.ts: the label is not the reviewed one').toBe(EXPECTED_LABEL);
  });

  it('names the stack in the browser-tab title (src/app/layout.tsx metadata.title)', () => {
    const title = typeof metadata.title === 'string' ? metadata.title : JSON.stringify(metadata.title);
    expect(title, 'browser-tab title: src/app/layout.tsx metadata.title does not contain the stack label').toContain(EXPECTED_LABEL);
  });

  it('the public page set names only pages that exist', () => {
    const names = pages.map((file) => file.name);
    expect(Object.keys(PUBLIC_PAGES).filter((name) => !names.includes(name)), 'PUBLIC_PAGES entries with no page file').toEqual([]);
  });

  describe.each(pages.map((file) => [file.name, file.rel, file] as const))('%s (%s)', (name, rel, file) => {
    const where = PUBLIC_PAGES[name] ?? 'signed-in header';
    it(
      `shows the label where it belongs: ${where}`,
      async () => {
        const found: string[] = [];
        const run = await runRoute(file, { action: captureScript(name, found) });
        expect(run.renderError, `${rel} crashed while rendering`).toBeUndefined();
        if (where === 'redirect') {
          expect(run.redirect, `${rel} is listed as a redirect-only page but renders`).toBeDefined();
          return;
        }
        expect(run.redirect, `${rel} redirected to ${run.redirect} instead of rendering`).toBeUndefined();
        const place = where === 'login' ? 'main' : 'header';
        expect(found, `${where === 'login' ? 'login page' : 'signed-in header'} of ${name} (${rel}): the stack label is missing`).toContain(`${place}: ${EXPECTED_LABEL}`);
      },
      TIMEOUT,
    );
  });

  it('has one definition: no other source file writes the label text', () => {
    // Population: every non-test source file under src/, read off the disk (excluded by PATH, not by content).
    const src = path.join(WEB_ROOT, 'src');
    const files: string[] = [];
    const walk = (dir: string) => {
      for (const entry of readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        const rel = path.relative(src, full).split(path.sep).join('/');
        if (entry.isDirectory()) {
          if (rel !== 'test') walk(full);
        } else if (/\.(tsx?|jsx?|mjs|css|json)$/.test(entry.name) && !/\.test\.[tj]sx?$/.test(entry.name)) files.push(rel);
      }
    };
    walk(src);
    expect(files, 'the source population is empty — the walk is broken').toContain('lib/stack-label.ts');
    // Any spelling of the pair, not only the exact label, so a drifted copy is caught too.
    const pattern = /\.NET\s*\/\s*Next|#8\s*·/gi;
    const hits = files.flatMap((rel) => (readFileSync(path.join(src, rel), 'utf8').match(pattern) ?? []).map((match) => `${rel}: ${match}`));
    expect(hits, 'the stack label is written in more than one place (or not in src/lib/stack-label.ts)').toEqual(['lib/stack-label.ts: #8 ·', 'lib/stack-label.ts: .NET / Next']);
  });
});
