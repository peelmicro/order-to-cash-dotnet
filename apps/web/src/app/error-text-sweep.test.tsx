// Id 29 bullet 5 — "every error shown to a user is THAT error's own text" —
// tested as BEHAVIOUR, page by page (fix round 2).
//
// Fix round 1 guarded this claim by recognising the SYNTAX of an error display,
// and review round 2 beat it twice with ordinary code (an early `return`, a
// `switch`, an `||` fallback, an `error.tsx` file; a proof case satisfied by
// incidental lines). CLAUDE.md's defeat list row 11: when a syntax guard keeps
// losing to new forms of a behavioural claim, test the behaviour instead.
//
// So this file knows nothing about how a page renders an error:
//   1. the route files come from the FILESYSTEM (src/test/page-sweep.tsx);
//   2. each page is rendered in its real layout chain and every request it
//      makes is OBSERVED at the api-client seam;
//   3. each observed request is failed in turn with a problem document whose
//      `detail` is a unique sentinel — then again with `detail` ABSENT and the
//      sentinel as `title` — and the rendered text must contain it;
//   4. requests that follow a user action are reached by the scripts in
//      ACTIONS, and every mutating `apiRequest` the source contains must be
//      made by one of them;
//   5. anything whose failure is legitimately not shown is listed, with a
//      reason, and the list is checked for staleness in both directions.
import { screen } from '@testing-library/react';
import { NextRequest } from 'next/server';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { createElement } from 'react';
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import { POST as loginRoute } from '@/app/api/auth/login/route';
import { POST as logoutRoute } from '@/app/api/auth/logout/route';
import RootLayout from '@/app/layout';
import { Providers } from '@/app/providers';
import { FakeGateway, sendJson } from '@/test/fake-gateway';
import { declaredOperations } from '@/test/openapi-statuses';
import { addedTexts, ApiError, failureStatuses, LATE_WATCH_MS, NONCE, ROUTE_FILES, issuedSentinels, newTexts, paramsOf, problemBody, routeFilesOnDisk, runRoute, sentinel, WEB_ONLY_STATUSES, WEB_ROOT, type ActionScript, type RouteFile, type Run, type User, type Variant } from '@/test/page-sweep';
import { API_CLIENT, browserSourceFiles, mutatingCalls, patternMatches, requestPrimitives, requestUnits, SRC } from '@/test/request-sites';
import { routerMock } from '@/test/setup';

// The (app) layout is a server component that reads the session cookie. jsdom
// cannot unseal one (see place-order-form.test.tsx); the session itself is
// proven in src/app/api/route-handlers.test.ts. Here a signed-in session is given.
vi.mock('next/headers', () => ({
  cookies: async () => ({ get: () => undefined, getAll: () => [], has: () => false }),
  headers: async () => new Headers(),
}));
vi.mock('@/server/session', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/server/session')>()),
  readSession: async () => ({ accessToken: 'test-token', expiresAt: Date.now() + 60_000, username: 'operator', displayName: 'Operator', roles: [] }),
  // The native sign-in's SUCCESS path seals a cookie, which jsdom cannot do; sealing is proven in route-handlers.test.ts.
  writeSession: async (response: import('next/server').NextResponse) => {
    response.cookies.set('otc_session', 'sealed-elsewhere', { httpOnly: true, path: '/' });
  },
}));

const SWEEP_TIMEOUT = 180_000;

// ── Literal, reviewed lists ─────────────────────────────────────────────────

/** Page-like conventions the sweep renders; `layout` is rendered as part of every page's chain; `error`/`global-error` are rendered with a failure. */
const PAGE_LIKE = new Set(['page', 'not-found', 'global-not-found', 'forbidden', 'unauthorized', 'loading', 'default']);
const BOUNDARIES = new Set(['error', 'global-error']);

/** A route file the harness cannot render, with the reason. The test fails for any file neither swept nor listed here. */
const NOT_RENDERABLE: Record<string, string> = {};

/**
 * The requests each route makes on load, as last reviewed. This is NOT where
 * the sweep's population comes from — that is observed — it is the check that
 * observation still sees what it saw (a harness that stops too early, or a
 * seam that stops recording, would otherwise shrink the population silently).
 */
const EXPECTED_LOAD: Record<string, string[]> = {
  '/': [],
  '/login': [],
  '/orders': ['GET /api/catalog/retailers', 'GET /api/orders?page=1&pageSize=20'],
  '/orders/place': ['GET /api/catalog/companies', 'GET /api/catalog/products', 'GET /api/catalog/retailers'],
  '/orders/[id]': ['GET /api/orders/9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f'],
  '/stock': ['GET /api/catalog/products', 'GET /api/stock?page=1&pageSize=20'],
  '/billing': ['GET /api/catalog/retailers', 'GET /api/credits?page=1&pageSize=20', 'GET /api/invoices?page=1&pageSize=20'],
};

/** Every request primitive browser code may use OUTSIDE the api-client, with how many uses and why the sweep still covers it. */
const PRIMITIVE_EXCEPTIONS: Record<string, { count: number; reason: string }> = {
  'hooks/use-order-stream.ts | EventSource': {
    count: 1,
    reason: 'the live timeline stream. An EventSource exposes no response body, so there is no problem text to show; its failure is the connection badge and the manual retry, proven in order-detail-view.test.tsx and use-order-stream.test.tsx. Listed in STREAMS below.',
  },
  'features/auth/login-form.tsx | form action': { count: 1, reason: 'the no-JavaScript sign-in form post; swept by NATIVE_POSTS below' },
  'features/shell/logout-button.tsx | form action': { count: 1, reason: 'the no-JavaScript sign-out form post; covered by NATIVE_POSTS below' },
};

/** EventSources a route may open — an EventSource exposes no body, so its failure cannot carry problem text. */
const STREAMS: Record<string, string> = {
  '/orders/[id] | /api/orders/stream': 'connection status + retry, proven in order-detail-view.test.tsx (gave-up → "Connection lost" + Retry connection)',
};

/**
 * One scripted user action per mutation the app can make. Every request an
 * action causes is swept exactly like a load request.
 */
const ACTIONS: ActionScript[] = [
  {
    route: '/login',
    name: 'sign-in',
    run: async (user: User) => {
      await user.type(screen.getByLabelText('Password'), 'secret');
      await user.click(screen.getByRole('button', { name: 'Sign in' }));
    },
  },
  {
    route: '/orders/place',
    name: 'place-order',
    run: async (user: User) => {
      await user.selectOptions(await screen.findByRole('combobox', { name: 'Retailer' }), 'AldiEs');
      await user.selectOptions(await screen.findByRole('combobox', { name: 'Company' }), 'IBERFOODS');
      await user.selectOptions(await screen.findByRole('combobox', { name: 'Product' }), 'PRD-0001');
      await user.click(screen.getByRole('button', { name: 'Place order' }));
    },
  },
  {
    route: '/stock',
    name: 'replenish',
    run: async (user: User) => {
      await user.click(await screen.findByTestId('replenish-button'));
      await user.type(screen.getByTestId('replenish-units-input'), '5');
      await user.click(screen.getByTestId('submit-replenish-button'));
    },
  },
  {
    route: '/billing',
    name: 'register-payment',
    run: async (user: User) => {
      await user.click(await screen.findByTestId('register-payment-button'));
      await user.click(screen.getByTestId('submit-payment-button'));
    },
  },
  {
    route: '/orders',
    name: 'sign-out',
    run: async (user: User) => {
      await user.click(screen.getByRole('button', { name: 'Log out' }));
    },
  },
];

/** The requests each action makes, as last reviewed. Repeats of the route's LOAD requests (a poll) may also appear. */
const EXPECTED_ACTION: Record<string, string[]> = {
  '/login | sign-in': ['POST /api/auth/login'],
  '/orders/place | place-order': ['POST /api/orders'],
  '/stock | replenish': ['GET /api/stock?page=1&pageSize=20', 'POST /api/stock/replenish'],
  '/billing | register-payment': ['GET /api/credits?page=1&pageSize=20', 'GET /api/invoices?page=1&pageSize=20', 'GET /api/orders?orderReference=ORD-000042&page=1&pageSize=1', 'POST /api/invoices/inv-1/payments'],
  '/orders | sign-out': ['POST /api/auth/logout'],
};

/**
 * `route | load-or-action | request` whose failure is legitimately NOT shown,
 * with the reason and a check that the reason is true. If one of these ever
 * starts showing its sentinel, the entry is stale and the sweep says so.
 */
const leavesForLogin = () => expect(routerMock.replace, 'signing out must leave for /login').toHaveBeenCalledWith('/login');
const NOT_SHOWN: Record<string, { reason: string; verify: () => void | Promise<void> }> = {
  '/orders | sign-out | POST /api/auth/logout': {
    reason: 'signing out proceeds whatever the answer — the cache is cleared and the user is sent to /login (logout-button.tsx); the route makes no upstream call that could fail (app/api/auth/logout/route.ts)',
    verify: leavesForLogin,
  },
  '/orders | sign-out | GET /api/catalog/retailers': {
    reason: "the list's refetch after sign-out cleared the cache (logout-button.tsx `queryClient.clear()`), while the user is being sent to /login — there is no page left to show it on",
    verify: leavesForLogin,
  },
  '/orders | sign-out | GET /api/orders?page=1&pageSize=20': {
    reason: "the same refetch after sign-out, for the orders list",
    verify: leavesForLogin,
  },
};

/** Native form posts a route may show, and how each one's failure is covered. */
const NATIVE_POSTS: Record<string, string> = {
  'POST /api/auth/login':
    'swept below: the form post is made once against an all-success Gateway, every upstream call the route makes is observed, and each is then refused in turn (every declared status, both variants); the page the 303 names must show that refusal\'s own words',
  'POST /api/auth/logout': 'the route never answers with a problem: it only clears the cookie and answers 200 (checked below)',
};

/**
 * The ONLY text a failure may add besides the problem's own words (id 29
 * bullet 5's other half: never a generic message). Each is a label that frames
 * the problem text, or help that stays true whatever the problem says. Checked
 * both ways: a label no failing run produces is stale.
 */
const LABELS: Record<string, string> = {
  'Could not load credit limits:': 'prefix of the credits list error',
  'Could not load invoices:': 'prefix of the invoices list error',
  'Could not load orders:': 'prefix of the orders list error',
  'Could not load stock:': 'prefix of the stock list error',
  'Product names unavailable:': "prefix of the stock page's catalog (product-name) error — the stock table itself is never hidden by this failure (id 101)",
  'Could not load this order:': 'prefix of the order detail error',
  'Retailer filter unavailable:': 'prefix of the /orders retailer-filter error',
  'Retailer filters unavailable:': 'prefix of the /billing retailer-filter error',
  'the order link could not be resolved:': 'prefix of the payment form order-link error',
  // The catalogue notice's manual-entry help (`catalog-manual-entry`), split into text nodes by its <code> elements;
  // the codes IBERFOODS and PRD-0001 are already on screen as options, so only these fragments are new.
  'Enter codes by hand meanwhile (e.g. retailer': 'manual-entry help, fragment 1',
  CarrefourEs: 'manual-entry help, fragment 2 (the example retailer code)',
  ', company': 'manual-entry help, fragment 3',
  ', product': 'manual-entry help, fragment 4',
  ').': 'manual-entry help, fragment 5',
};

/**
 * Every hook export (and every component that mutates or gates a read itself)
 * with the files that use it and — for mutations and gated reads — the files
 * that render those. Derived by content (src/test/request-sites.ts). A second
 * way to reach a mutation or an interaction-gated read changes a line here,
 * and must then be scripted in ACTIONS before this list is updated.
 */
const REQUEST_UNITS: string[] = [
  'features/auth/login-form.tsx#* mutation | app/login/page.tsx',
  'features/shell/logout-button.tsx#* mutation | app/(app)/layout.tsx',
  'hooks/use-billing.ts#useCredits query | features/billing/billing-view.tsx',
  'hooks/use-billing.ts#useInvoices query | features/billing/billing-view.tsx',
  'hooks/use-billing.ts#useRegisterPayment mutation | features/billing/billing-view.tsx | via app/(app)/billing/page.tsx',
  'hooks/use-catalog.ts#useCompanies query | features/orders/place-order-form.tsx',
  'hooks/use-catalog.ts#useProducts query | features/orders/place-order-form.tsx, features/stock/stock-view.tsx',
  'hooks/use-catalog.ts#useRetailers query | features/billing/billing-view.tsx, features/orders/orders-list.tsx, features/orders/place-order-form.tsx',
  'hooks/use-order-detail.ts#orderDetailKey value | ',
  'hooks/use-order-detail.ts#STALE_STATUS_BACKSTOP_MS value | ',
  'hooks/use-order-detail.ts#useOrderDetail query | features/orders/order-detail-view.tsx',
  'hooks/use-order-detail.ts#useOrderDetailPatchers value | features/orders/order-detail-view.tsx',
  'hooks/use-order-stream.ts#browserEventSourceFactory value | ',
  'hooks/use-order-stream.ts#useOrderStream value | features/orders/order-detail-view.tsx',
  'hooks/use-orders.ts#useOrderIdByReference gated-query | features/billing/billing-view.tsx | via app/(app)/billing/page.tsx',
  'hooks/use-orders.ts#useOrders query | features/orders/orders-list.tsx',
  'hooks/use-orders.ts#usePlaceOrder mutation | features/orders/place-order-form.tsx | via app/(app)/orders/place/page.tsx',
  'hooks/use-stock.ts#useReplenishStock mutation | features/stock/stock-view.tsx | via app/(app)/stock/page.tsx',
  'hooks/use-stock.ts#useStock query | features/stock/stock-view.tsx',
];

// ── Helpers ─────────────────────────────────────────────────────────────────

const pages = ROUTE_FILES.filter((file) => PAGE_LIKE.has(file.kind));
const boundaries = ROUTE_FILES.filter((file) => BOUNDARIES.has(file.kind));
const byName = (name: string): RouteFile => {
  const found = pages.find((file) => file.kind === 'page' && file.name === name);
  if (!found) throw new Error(`sweep: no page file renders ${name}`);
  return found;
};

const baselines = new Map<string, Promise<Run>>();
function baseline(file: RouteFile, action?: ActionScript): Promise<Run> {
  const key = `${file.rel} | ${action?.name ?? 'load'}`;
  if (!baselines.has(key)) baselines.set(key, runRoute(file, { action, watchLate: true }));
  return baselines.get(key)!;
}

/** Labels some failing run produced, and the sweeps that ran — for the both-ways label check. */
const producedLabels = new Set<string>();
const completedSweeps = new Set<string>();

/** Text a failure added that is neither the problem's own words nor a reviewed label. */
function unlabelled(texts: string[], text: string): string[] {
  const out: string[] = [];
  for (const added of texts) {
    if (added.includes(text)) continue;
    if (added in LABELS) producedLabels.add(added);
    else out.push(added);
  }
  return out;
}

const streamKey = (route: string, url: string) => `${route} | ${new URL(url, 'http://web.test').pathname}`;

/**
 * Fails each of `requests` in turn and returns one line per request whose
 * sentinel the user did not see: `route | phase | request | variant | expected sentinel | what was shown instead`.
 */
async function sweep(file: RouteFile, variant: Variant, phase: 'load' | 'action', requests: string[], base: Run, action?: ActionScript): Promise<string[]> {
  const failures: string[] = [];
  for (const key of requests) {
    for (const status of failureStatuses(key, file.name)) {
      const text = sentinel(variant);
      routerMock.replace.mockClear();
      const run = await runRoute(file, { action, fail: { phase, key, variant, text, status } });
      const where = `${file.name} | ${action ? action.name : 'load'} | ${key}`;
      const shown = run.text.includes(text);
      const listed = NOT_SHOWN[where];
      if (run.renderError) failures.push(`${where} | ${status} ${variant} | the page crashed instead: ${run.renderError}`);
      else if (listed) {
        if (shown) failures.push(`${where} | ${status} ${variant} | listed in NOT_SHOWN but the sentinel IS shown now — remove the entry`);
        else await listed.verify();
      } else if (!shown) {
        failures.push(`${where} | ${status} ${variant} | expected the problem ${variant} "${text}" on screen | shown instead: ${newTexts(run, base)}`);
      } else {
        const generic = unlabelled(addedTexts(run, base), text);
        if (generic.length) failures.push(`${where} | ${status} ${variant} | the problem's words are shown, but so is text that is not a reviewed label: ${generic.map((g) => `"${g}"`).join(', ')}`);
      }
      if (run.bypasses.length) failures.push(`${where} | requests bypassed the api-client seam: ${run.bypasses.join(', ')}`);
    }
  }
  completedSweeps.add(`${file.name} | ${action ? action.name : 'load'} | ${variant}`);
  return failures;
}

// ── The population of route files ──────────────────────────────────────────

describe('the route-file population is the filesystem', () => {
  it('import.meta.glob sees exactly the App Router UI files on disk', () => {
    const onDisk = routeFilesOnDisk();
    expect(onDisk.length, 'the disk walk found no page at all — it is not reading src/app').toBeGreaterThanOrEqual(7);
    const globbed = ROUTE_FILES.map((file) => file.rel);
    expect({ onDiskNotGlobbed: onDisk.filter((rel) => !globbed.includes(rel)), globbedNotOnDisk: globbed.filter((rel) => !onDisk.includes(rel)) }, 'route files the glob and the disk disagree on').toEqual({ onDiskNotGlobbed: [], globbedNotOnDisk: [] });
  });

  it('there is no second router the sweep would not see (pages/, or an app/ beside src/)', () => {
    const exists = (rel: string) => {
      try {
        readFileSync(path.join(WEB_ROOT, rel));
        return true;
      } catch (error) {
        return (error as NodeJS.ErrnoException).code === 'EISDIR';
      }
    };
    expect(['pages', 'src/pages', 'app'].filter(exists), 'a router directory the sweep does not walk').toEqual([]);
  });

  it('every route file is swept, rendered as part of a page, or listed as not renderable', () => {
    const rootLayout = ROUTE_FILES.find((file) => file.kind === 'layout' && file.dir.length === 0);
    const unhandled = ROUTE_FILES.filter((file) => !PAGE_LIKE.has(file.kind) && !BOUNDARIES.has(file.kind) && file.kind !== 'layout' && !(file.rel in NOT_RENDERABLE)).map((file) => `${file.rel} (${file.kind})`);
    expect(unhandled, 'route files the sweep neither renders nor lists').toEqual([]);
    expect(rootLayout?.rel, 'the root layout (src/app/layout.tsx) is not in the swept population').toBe('layout.tsx');
    expect(Object.keys(NOT_RENDERABLE).filter((rel) => !ROUTE_FILES.some((file) => file.rel === rel)), 'NOT_RENDERABLE entries with no file').toEqual([]);
  });

  it('the root layout is exactly <html><body><Providers>{children}, so the harness (which renders inside the real Providers) sees what a browser sees', () => {
    const marker = createElement('span', { id: 'marker' });
    const html = RootLayout({ children: marker }) as { type: unknown; props: { children: { type: unknown; props: { children: { type: unknown; props: { children: unknown } } } } } };
    const body = html.props.children;
    const providers = body.props.children;
    expect({ html: html.type, body: body.type, providers: providers.type === Providers, children: providers.props.children === marker }, 'the root layout wraps pages in something the harness does not render').toEqual({ html: 'html', body: 'body', providers: true, children: true });
  });

  it('every page has a reviewed load list, and no reviewed list names a page that no longer exists', () => {
    const names = pages.filter((file) => file.kind === 'page').map((file) => file.name);
    expect(names.filter((name) => !(name in EXPECTED_LOAD)), 'pages with no EXPECTED_LOAD entry').toEqual([]);
    expect(Object.keys(EXPECTED_LOAD).filter((name) => !names.includes(name)), 'EXPECTED_LOAD entries with no page').toEqual([]);
    for (const file of ROUTE_FILES) expect(() => paramsOf(file.dir)).not.toThrow();
  });
});

// ── Pages: load ─────────────────────────────────────────────────────────────

describe.each(pages.map((file) => [file.name, file.rel, file] as const))('%s (%s)', (name, _rel, file) => {
  it(
    'renders; its observed requests are the reviewed ones, all through the seam, all answered',
    async () => {
      if (file.rel in NOT_RENDERABLE) return;
      const run = await baseline(file);
      expect(run.renderError, `${name} crashed while rendering`).toBeUndefined();
      expect(run.unrouted, `${name}: requests with no success body in page-sweep.tsx`).toEqual([]);
      expect(run.bypasses, `${name}: requests that bypassed the api-client seam`).toEqual([]);
      expect(run.late, `${name}: requests that first started only after load had settled (watched for ${LATE_WATCH_MS} ms) — review them before adding them to EXPECTED_LOAD`).toEqual([]);
      expect(run.load, `${name}: the requests observed on load differ from EXPECTED_LOAD`).toEqual(EXPECTED_LOAD[name] ?? ['(no EXPECTED_LOAD entry)']);
      expect(run.text.includes(NONCE), `${name}: the sentinel nonce is on screen before any failure`).toBe(false);
      expect(
        run.streams.filter((url) => !(streamKey(name, url) in STREAMS)),
        `${name}: EventSources not listed in STREAMS`,
      ).toEqual([]);
      expect(
        run.forms.filter((form) => !(form in NATIVE_POSTS)),
        `${name}: native form posts not listed in NATIVE_POSTS`,
      ).toEqual([]);
      if (run.redirect === undefined) expect(run.text.trim().length, `${name} rendered nothing`).toBeGreaterThan(0);
    },
    SWEEP_TIMEOUT,
  );

  it.each(['detail', 'title'] as const)(
    "shows each load request's failure as that problem's own %s",
    async (variant) => {
      if (file.rel in NOT_RENDERABLE) return;
      const base = await baseline(file);
      const failures = await sweep(file, variant, 'load', base.load, base);
      expect(failures, `${name}: load failures the user does not see in the problem's own words (route | phase | request | variant | expected | shown instead)`).toEqual([]);
    },
    SWEEP_TIMEOUT,
  );
});

// ── Error boundaries (error.tsx / global-error.tsx) ─────────────────────────

describe('error boundary files show the failure that reached them in its own words', () => {
  it('the boundary population is every error / global-error file on disk (none today)', () => {
    expect(boundaries.map((file) => file.rel)).toEqual(routeFilesOnDisk().filter((rel) => /(^|\/)(error|global-error)\.[a-z]+$/.test(rel)));
  });

  it.each(boundaries.flatMap((file) => (['detail', 'title'] as const).map((variant) => [file.rel, variant, file] as const)))(
    '%s: %s',
    async (rel, variant, file) => {
      const statuses = [...new Set(declaredOperations().flatMap((op) => op.statuses))].filter((status) => status >= 400 && status !== 401).sort();
      for (const status of statuses) {
        const text = sentinel(variant);
        const { body } = problemBody(variant, text, status);
        const run = await runRoute(file, { boundaryError: new ApiError(status, body) });
        expect(run.text.includes(text), `${rel} | a failed request (${status}) reaching this boundary | expected the problem ${variant} "${text}" | shown instead: ${run.text.trim() || '(nothing)'}`).toBe(true);
        expect(unlabelled(run.texts, text), `${rel} | ${status} ${variant} | text beside the problem's words that is not a reviewed label`).toEqual([]);
      }
    },
    SWEEP_TIMEOUT,
  );
});

// ── Actions ─────────────────────────────────────────────────────────────────

describe.each(ACTIONS.map((action) => [`${action.route} | ${action.name}`, action] as const))('%s', (label, action) => {
  it(
    'runs; the requests it makes are the reviewed ones, all through the seam, all answered',
    async () => {
      const file = byName(action.route);
      const run = await baseline(file, action);
      const load = new Set((await baseline(file)).load);
      expect(run.renderError, `${label} crashed`).toBeUndefined();
      expect(run.unrouted, `${label}: requests with no success body`).toEqual([]);
      expect(run.bypasses, `${label}: requests that bypassed the api-client seam`).toEqual([]);
      const expected = EXPECTED_ACTION[label] ?? ['(no EXPECTED_ACTION entry)'];
      expect(expected.filter((key) => !run.action.includes(key)), `${label}: reviewed requests the action no longer makes`).toEqual([]);
      expect(run.late, `${label}: requests that first started only after the action had settled (watched for ${LATE_WATCH_MS} ms)`).toEqual([]);
      expect(run.action.filter((key) => !expected.includes(key) && !load.has(key)), `${label}: requests the action makes that EXPECTED_ACTION does not list`).toEqual([]);
    },
    SWEEP_TIMEOUT,
  );

  it.each(['detail', 'title'] as const)(
    "shows each of its requests' failure as that problem's own %s",
    async (variant) => {
      const file = byName(action.route);
      const run = await baseline(file, action);
      const failures = await sweep(file, variant, 'action', run.action, run, action);
      expect(failures, `${label}: action failures the user does not see in the problem's own words (route | action | request | variant | expected | shown instead)`).toEqual([]);
    },
    SWEEP_TIMEOUT,
  );
});

// ── Native form posts ───────────────────────────────────────────────────────

describe('native (no-JavaScript) form posts', () => {
  const gateway = new FakeGateway();
  let refuse: { key: string; variant: Variant; text: string; status: number } | undefined;
  const upstream = (key: string, ok: (res: import('node:http').ServerResponse) => void) => (request: { method: string; url: string }, res: import('node:http').ServerResponse) => {
    if (refuse && refuse.key === `${request.method} ${new URL(request.url, 'http://x').pathname}`) {
      const { body } = problemBody(refuse.variant, refuse.text, refuse.status);
      sendJson(res, refuse.status, body);
    } else ok(res);
    void key;
  };
  beforeAll(async () => {
    gateway.on('POST', '/auth/login', upstream('POST /auth/login', (res) => sendJson(res, 200, { accessToken: 'sweep-token', tokenType: 'Bearer', expiresIn: 3600 })));
    gateway.on('GET', '/auth/me', upstream('GET /auth/me', (res) => sendJson(res, 200, { username: 'operator', displayName: 'Operator', roles: ['operator'] })));
    process.env.GATEWAY_BASE_URL = await gateway.start();
  });
  afterAll(async () => {
    delete process.env.GATEWAY_BASE_URL;
    await gateway.stop();
  });

  const formPost = () => loginRoute(new NextRequest('http://web.test/api/auth/login', { method: 'POST', body: 'username=operator&password=wrong', headers: { 'content-type': 'application/x-www-form-urlencoded' } }));

  it.each(['detail', 'title'] as const)(
    "POST /api/auth/login: every upstream call the route makes, refused in turn, comes back through ?error= as the problem's own %s",
    async (variant) => {
      refuse = undefined;
      const before = gateway.requests.length;
      const ok = await formPost();
      expect(ok.status, 'the all-success form post must sign in').toBe(303);
      expect(new URL(ok.headers.get('location')!).pathname).toBe('/orders');
      const observed = [...new Set(gateway.requests.slice(before).map((r) => `${r.method} ${new URL(r.url, 'http://x').pathname}`))];
      expect(observed, 'the upstream calls the form post makes').toEqual(['POST /auth/login', 'GET /auth/me']);
      const failures: string[] = [];
      for (const key of observed) {
        const [method, gatewayPath] = key.split(' ') as [string, string];
        const operation = declaredOperations().find((op) => op.method === method && op.template === gatewayPath);
        const statuses = (operation?.statuses ?? []).filter((status) => status >= 400);
        expect(statuses.length, `${key}: no failure status declared in openapi.yaml`).toBeGreaterThan(0);
        for (const status of statuses) {
          refuse = { key, variant, text: sentinel(variant), status };
          const response = await formPost();
          const location = new URL(response.headers.get('location') ?? 'http://web.test/(no-location)');
          const target = pages.find((file) => file.kind === 'page' && file.name === location.pathname);
          if (response.status !== 303 || !target) {
            failures.push(`/login | native POST /api/auth/login | upstream ${key} ${status} ${variant} | expected a 303 to a page, got ${response.status} → ${location.pathname}`);
            continue;
          }
          const base = await baseline(target);
          const run = await runRoute(target, { searchParams: Object.fromEntries(location.searchParams) });
          if (!run.text.includes(refuse.text)) failures.push(`${location.pathname} | native POST /api/auth/login | upstream ${key} ${status} ${variant} | expected the problem ${variant} "${refuse.text}" on screen | shown instead: ${newTexts(run, base)}`);
          else {
            const generic = unlabelled(addedTexts(run, base), refuse.text);
            if (generic.length) failures.push(`${location.pathname} | native POST /api/auth/login | upstream ${key} ${status} ${variant} | text beside the problem's words that is not a reviewed label: ${generic.join(', ')}`);
          }
        }
      }
      refuse = undefined;
      completedSweeps.add(`/login | native | ${variant}`);
      expect(failures, 'native sign-in refusals the user does not see in the problem\'s own words').toEqual([]);
    },
    SWEEP_TIMEOUT,
  );

  it('POST /api/auth/logout: the route answers 200 and makes no upstream call, so there is no problem to show', async () => {
    const before = gateway.requests.length;
    const response = await logoutRoute();
    expect(response.status).toBe(200);
    expect(gateway.requests.length).toBe(before);
  });

  it('every native post a page shows is listed, and every listed one is shown somewhere', async () => {
    const seen = new Set<string>();
    for (const file of pages) for (const form of (await baseline(file)).forms) seen.add(form);
    expect([...seen].sort()).toEqual(Object.keys(NATIVE_POSTS).sort());
  }, SWEEP_TIMEOUT);
});

// ── The sweep's own premises ────────────────────────────────────────────────

describe("the sweep's premises", () => {
  it('no browser code makes a request outside the api-client seam, except the listed ones (by content, file and line)', () => {
    const hits = requestPrimitives();
    const counts = new Map<string, string[]>();
    for (const hit of hits) counts.set(hit.key, [...(counts.get(hit.key) ?? []), hit.site]);
    const unlisted = hits.filter((hit) => !(hit.key in PRIMITIVE_EXCEPTIONS)).map((hit) => hit.site);
    expect(unlisted, `request primitives outside ${API_CLIENT} — route the request through apiRequest, or list it with a reason`).toEqual([]);
    const wrongCount = Object.entries(PRIMITIVE_EXCEPTIONS)
      .filter(([key, { count }]) => (counts.get(key)?.length ?? 0) !== count)
      .map(([key, { count }]) => `${key}: listed ${count}, found ${counts.get(key)?.length ?? 0} (${(counts.get(key) ?? []).join(', ')})`);
    expect(wrongCount, 'PRIMITIVE_EXCEPTIONS counts that no longer match the source').toEqual([]);
    expect(browserSourceFiles().length, 'the scan read almost nothing').toBeGreaterThanOrEqual(40);
  });

  it('the api-client is the seam: its only request primitive is the swappable fetch', () => {
    const client = readFileSync(path.join(SRC, API_CLIENT), 'utf8');
    expect(client.match(/\bfetch\(/g)?.length, 'fetch( calls in api-client.ts (the default and the reset)').toBe(2);
    expect(client).toMatch(/const response = await fetchImpl\(/);
  });

  it('every mutating apiRequest in the source is made by a scripted action (the action population)', async () => {
    const calls = mutatingCalls();
    expect(calls.filter((call) => call.unresolved).map((call) => `${call.site}: ${call.unresolved}`), 'apiRequest calls this scan cannot classify').toEqual([]);
    const observed: string[] = [];
    for (const action of ACTIONS) observed.push(...(await baseline(byName(action.route), action)).action.filter((key) => !key.startsWith('GET ')));
    const unscripted = calls
      .filter((call) => !observed.some((key) => key.startsWith(`${call.method} `) && patternMatches(call.pattern!, key.slice(call.method.length + 1).split('?')[0]!)))
      .map((call) => `${call.site} ${call.method} ${call.pattern}`);
    expect(unscripted, 'mutating requests no scripted action makes — add an ACTIONS entry that makes each one').toEqual([]);
    expect(calls.length, 'the scan found almost no mutations — it is not reading the hooks').toBeGreaterThanOrEqual(5);
  }, SWEEP_TIMEOUT);

  it('every reviewed label is produced by some failing run, and every sweep ran (run the whole file)', () => {
    const expectedSweeps = [
      ...pages.flatMap((file) => (['detail', 'title'] as const).map((variant) => `${file.name} | load | ${variant}`)),
      ...ACTIONS.flatMap((action) => (['detail', 'title'] as const).map((variant) => `${action.route} | ${action.name} | ${variant}`)),
      '/login | native | detail',
      '/login | native | title',
    ].sort();
    expect([...completedSweeps].sort(), 'the label check needs every sweep in this file to have run first').toEqual(expectedSweeps);
    expect(Object.keys(LABELS).filter((label) => !producedLabels.has(label)), 'LABELS no failing run produced — stale').toEqual([]);
  });

  it('every way to reach a mutation or an interaction-gated read is the reviewed one (by content)', () => {
    const derived = requestUnits().map((unit) => `${unit.key} ${unit.kind} | ${unit.consumers.join(', ')}${unit.via.length ? ` | via ${unit.via.join(', ')}` : ''}`);
    expect(derived, 'hook exports / mutating components and who uses them — a new consumer of a mutation or gated read must be scripted in ACTIONS first').toEqual(REQUEST_UNITS);
  });

  it('every failure status the sweep serves comes from openapi.yaml (or a listed web-only route)', () => {
    expect(failureStatuses('GET /api/orders/x', '/orders/[id]')).toEqual([404]);
    expect(failureStatuses('POST /api/auth/login', '/login')).toEqual([400, 401, 429]);
    expect(failureStatuses('POST /api/invoices/inv-1/payments', '/billing')).toEqual([400, 404, 409, 422, 503]);
    expect(Object.keys(WEB_ONLY_STATUSES).filter((key) => declaredOperations().some((op) => `${op.method} /api${op.template}` === key)), 'WEB_ONLY_STATUSES entries that DO have an operation').toEqual([]);
  });

  it('the late watch outlasts every non-poll timer the app sets', () => {
    expect(LATE_WATCH_MS).toBeGreaterThan(2_000);
    expect(LATE_WATCH_MS).toBeGreaterThan(1_000);
  });

  it('NOT_SHOWN and STREAMS name only things the sweep actually observed', async () => {
    const observed = new Set<string>();
    for (const file of pages) {
      const run = await baseline(file);
      for (const key of run.load) observed.add(`${file.name} | load | ${key}`);
      for (const url of run.streams) observed.add(streamKey(file.name, url));
    }
    for (const action of ACTIONS) for (const key of (await baseline(byName(action.route), action)).action) observed.add(`${action.route} | ${action.name} | ${key}`);
    expect([...Object.keys(NOT_SHOWN), ...Object.keys(STREAMS)].filter((key) => !observed.has(key)), 'stale exception entries').toEqual([]);
  }, SWEEP_TIMEOUT);

  it('sentinels are unique, random, and cannot equal any text in the app', () => {
    const probe = [sentinel('detail'), sentinel('title')];
    const all = [...issuedSentinels];
    expect(all.filter((value) => !/^sweep-[0-9a-f]{12}-\d+-(detail|title)$/.test(value) || !value.includes(NONCE)), 'malformed sentinels').toEqual([]);
    expect(new Set(all).size, 'duplicate sentinels').toBe(all.length);
    expect(probe[0]).not.toBe(probe[1]);
    const source = browserSourceFiles()
      .map((rel) => readFileSync(path.join(SRC, rel), 'utf8'))
      .join('\n');
    expect(source.includes(NONCE), 'the nonce occurs in the app source').toBe(false);
  });

  it('settling waits for a retried 5xx: a failure the app retries is still asserted after the retry, not during it', async () => {
    // The detail variant answers 503, which the app's own QueryClient retries once
    // after a delay; a harness that stopped waiting at the first failure would
    // read the loading state. A page must therefore show the sentinel here.
    const file = byName('/stock');
    const text = sentinel('detail');
    const started = Date.now();
    const run = await runRoute(file, { fail: { phase: 'load', key: 'GET /api/stock?page=1&pageSize=20', variant: 'detail', text, status: 503 } });
    expect({ shown: run.text.includes(text), waitedForRetry: Date.now() - started >= 900 }).toEqual({ shown: true, waitedForRetry: true });
  }, SWEEP_TIMEOUT);
});
