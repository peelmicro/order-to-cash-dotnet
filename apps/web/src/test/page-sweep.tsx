/// <reference types="vite/client" />
import { useQueryClient, type QueryClient } from '@tanstack/react-query';
import { act, cleanup, render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { randomUUID } from 'node:crypto';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { Component, Suspense, type ComponentType, type ReactNode } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { vi } from 'vitest';
import { Providers } from '@/app/providers';
import { DEFAULT_PENDING_RETRY_MS } from '@/lib/order-detail';
import { setApiFetch } from '@/lib/api-client';
import { ApiError } from '@/lib/problem';
import { FakeEventSource } from '@/test/fake-event-source';
import { gatewayFixture } from '@/test/gateway-fixtures';
import { operationFor } from '@/test/openapi-statuses';
import { json } from '@/test/render';

/**
 * The harness behind src/app/error-text-sweep.test.tsx (id 29 bullet 5, fix
 * round 2): render every route file the App Router would render, observe
 * every request it makes, fail each one in turn with a problem document whose
 * text is a unique sentinel, and read what the USER sees — the rendered text.
 * Nothing here knows how a page shows an error; it only knows what a page is.
 */

// ── The route-file population: the FILESYSTEM, twice ─────────────────────────

/** Every App Router file convention that can render UI (route handlers are not UI). */
export const UI_CONVENTIONS = ['page', 'layout', 'template', 'error', 'global-error', 'not-found', 'global-not-found', 'forbidden', 'unauthorized', 'loading', 'default'] as const;
const CONVENTION_FILE = new RegExp(`^(${UI_CONVENTIONS.join('|')})\\.(tsx|ts|jsx|js|mdx)$`);

export const WEB_ROOT = path.resolve(import.meta.dirname, '..', '..');
export const APP_DIR = path.join(WEB_ROOT, 'src', 'app');

const GLOB = import.meta.glob('/src/app/**/{page,layout,template,error,global-error,not-found,global-not-found,forbidden,unauthorized,loading,default}.{tsx,ts,jsx,js,mdx}');

export interface RouteFile {
  /** Relative to src/app, e.g. `(app)/stock/page.tsx`. */
  rel: string;
  kind: string;
  /** Directory segments, e.g. `['(app)', 'stock']`. */
  dir: string[];
  /** The route as a reader names it: route groups dropped, dynamic segments kept (`/orders/[id]`). */
  name: string;
  clientComponent: boolean;
  load: () => Promise<{ default: ComponentType<Record<string, unknown>> & ((props: Record<string, unknown>) => unknown) }>;
}

function routeName(dir: string[]): string {
  return `/${dir.filter((segment) => !/^\(.*\)$/.test(segment) && !segment.startsWith('@')).join('/')}`;
}

export const ROUTE_FILES: RouteFile[] = Object.entries(GLOB)
  .map(([key, load]) => {
    const rel = key.replace(/^\/src\/app\//, '');
    const parts = rel.split('/');
    const file = parts.pop()!;
    const source = readFileSync(path.join(APP_DIR, rel), 'utf8');
    return { rel, kind: file.replace(/\.[a-z]+$/, ''), dir: parts, name: routeName(parts), clientComponent: /^\s*(['"])use client\1/.test(source), load: load as RouteFile['load'] };
  })
  .sort((a, b) => a.rel.localeCompare(b.rel));

/** The same population read straight off the disk, to check the glob against. */
export function routeFilesOnDisk(): string[] {
  const out: string[] = [];
  const walk = (dir: string) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (CONVENTION_FILE.test(entry.name)) out.push(path.relative(APP_DIR, full).split(path.sep).join('/'));
    }
  };
  walk(APP_DIR);
  return out.sort();
}

// ── Sentinels ───────────────────────────────────────────────────────────────

/** Random per run, so no string in the app — a fallback, a label — can ever equal a sentinel. */
export const NONCE = randomUUID().replace(/-/g, '').slice(0, 12);
export const issuedSentinels: string[] = [];
let sentinelCount = 0;

export type Variant = 'detail' | 'title';

export function sentinel(variant: Variant): string {
  sentinelCount += 1;
  const value = `sweep-${NONCE}-${sentinelCount}-${variant}`;
  issuedSentinels.push(value);
  return value;
}

/**
 * A problem document shaped like a REAL captured Gateway one (same keys), with
 * the sentinel as its `detail` (variant `detail`) or, with `detail` ABSENT, as
 * its `title` (variant `title`), at `status`. The `detail` variant's title is a
 * decoy: a page that shows the title while a detail exists does not show the sentinel.
 */
export function problemBody(variant: Variant, text: string, status: number): { status: number; body: Record<string, unknown> } {
  const real = JSON.parse(gatewayFixture('stock-list-upstream-unavailable-503').body) as Record<string, unknown>;
  if (variant === 'detail') return { status, body: { ...real, status, title: `decoy-title-${NONCE}`, detail: text } };
  const { detail: _dropped, ...withoutDetail } = real;
  return { status, body: { ...withoutDetail, status, title: text } };
}

function problemResponse(variant: Variant, text: string, status: number): Response {
  const { body } = problemBody(variant, text, status);
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } });
}

/** Routes of THIS app with no Gateway operation behind them, and the status a failure of each is drawn as. */
export const WEB_ONLY_STATUSES: Record<string, { statuses: number[]; reason: string }> = {
  'POST /api/auth/logout': { statuses: [500], reason: 'the logout route makes no Gateway call (app/api/auth/logout/route.ts); the only way it fails is an unhandled throw, which Next answers with 500' },
};

/**
 * The statuses a failing request is drawn as: every 4xx/5xx `openapi.yaml`
 * declares for the Gateway operation behind it (the browser path minus `/api`).
 * 401 is left out except on `/login`: everywhere else the app's answer to a
 * 401 is to leave for the sign-in page (app/providers.tsx), not to render text.
 */
export function failureStatuses(key: string, route: string): number[] {
  const [method, target] = key.split(' ') as [string, string];
  const apiPath = target.split('?')[0]!;
  const webOnly = WEB_ONLY_STATUSES[`${method} ${apiPath}`];
  let declared: number[];
  if (webOnly) declared = webOnly.statuses;
  else {
    const operation = operationFor(method, apiPath.replace(/^\/api/, ''));
    if (!operation) throw new Error(`sweep: ${key} matches no operation in openapi.yaml and is not in WEB_ONLY_STATUSES`);
    declared = operation.statuses;
  }
  const failing = declared.filter((status) => status >= 400 && (status !== 401 || route === '/login'));
  if (failing.length === 0) throw new Error(`sweep: ${key} declares no failure status the sweep can serve on ${route} (declared: ${declared.join(', ')})`);
  return failing;
}

/**
 * How long a baseline keeps watching after it has settled, for requests that
 * start late. The longest NON-poll timer the app sets is the 202 retry's
 * default, `DEFAULT_PENDING_RETRY_MS` (lib/order-detail.ts); the query
 * client's own retry delay is 1 s. Polls (`refetchInterval: 5_000`) only
 * repeat keys already observed, so they do not bound this.
 */
export const LATE_WATCH_MS = DEFAULT_PENDING_RETRY_MS + 500;

// ── Realistic success bodies (shapes as in the component tests) ─────────────

export const ORDER_ID = '9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f';
/** A value for every dynamic segment a route may have. A segment with no value fails the sweep by name. */
export const PARAM_VALUES: Record<string, string> = { id: ORDER_ID };

const party = (code: string, name: string, currency = 'EUR') => ({ code, name, country: 'ES', gln: '5400000000058', currency, enabled: true });
const orderSummary = { orderId: ORDER_ID, orderReference: 'ORD-000042', orderDate: '2026-09-16T10:00:00.000Z', retailer: { code: 'AldiEs', gln: '5400000000058', name: 'Aldi España' }, company: { code: 'IBERFOODS', gln: '5400000000218' }, status: 'confirmed', currency: 'EUR', totals: { initialAmount: 24999, initialDiscount: 0, totalAmount: 24999 }, updatedAt: '2026-09-16T10:00:00.000Z' };
const stockItem = { companyCode: 'IBERFOODS', productCode: 'PRD-0001', productName: 'Ration Pack Bundle', units: 40, reservedUnits: 10, availableUnits: 30, lowStockThreshold: 20 };
const invoice = { invoiceId: 'inv-1', invoiceReference: 'INV-000027', invoiceDate: '2026-09-16T10:00:00.000Z', orderReference: 'ORD-000042', retailerCode: 'AldiEs', companyCode: 'IBERFOODS', currency: 'EUR', amount: 24999, discount: 0, totalAmount: 24999, status: 'issued' };
const credit = { creditCode: 'CR-000001', retailerCode: 'AldiEs', companyCode: 'IBERFOODS', currency: 'EUR', creditLimit: 500000, activeHolds: 24999, openExposure: 0, availableCredit: 475001 };
const page1 = (items: unknown[]) => ({ items, page: { page: 1, pageSize: 20, total: items.length } });

type Success = [method: string, path: RegExp, answer: (url: URL) => Response];
const SUCCESS: Success[] = [
  ['GET', /^\/api\/catalog\/retailers$/, () => json({ items: [party('AldiEs', 'Aldi España')] })],
  ['GET', /^\/api\/catalog\/companies$/, () => json({ items: [party('IBERFOODS', 'Iberian Foods Distribution SA')] })],
  ['GET', /^\/api\/catalog\/products$/, () => json({ items: [{ code: 'PRD-0001', name: 'Ration Pack Bundle', price: 24999, currency: 'EUR', enabled: true }] })],
  ['GET', /^\/api\/orders$/, () => json(page1([orderSummary]))],
  [
    'GET',
    /^\/api\/orders\/[^/]+$/,
    () =>
      json({
        ...orderSummary,
        status: 'placed',
        items: [{ productCode: 'PRD-0001', name: 'Ration Pack Bundle', quantity: 1, unitPrice: 24999, lineDiscount: 0 }],
        references: {},
        events: [{ eventId: 'e0', eventType: 'order.placed.v1', occurredAt: '2026-09-16T10:00:00.000Z', summary: 'Order placed' }],
        headerComplete: true,
      }),
  ],
  ['GET', /^\/api\/stock$/, () => json(page1([stockItem]))],
  ['GET', /^\/api\/invoices$/, () => json(page1([invoice]))],
  ['GET', /^\/api\/credits$/, () => json(page1([credit]))],
  ['POST', /^\/api\/orders$/, () => json({ orderId: ORDER_ID, orderReference: 'ORD-000077', status: 'placed', currency: 'EUR', totalAmount: 24999, orderDate: '2026-09-16T10:00:00.000Z' }, 201)],
  ['POST', /^\/api\/stock\/replenish$/, () => json({ items: [{ ...stockItem, units: 45, availableUnits: 35 }] })],
  ['POST', /^\/api\/invoices\/[^/]+\/payments$/, () => json({ outcome: 'accepted', paymentReference: 'PAY-X', invoiceReference: 'INV-000027', orderReference: 'ORD-000042', invoiceStatus: 'paid', paidAt: '2026-09-16T10:05:00.000Z' }, 201)],
  ['POST', /^\/api\/auth\/login$/, () => json({ authenticated: true, username: 'operator', roles: ['operator'] })],
  ['POST', /^\/api\/auth\/logout$/, () => json({ authenticated: false })],
];

// ── One run ─────────────────────────────────────────────────────────────────

export type User = ReturnType<typeof userEvent.setup>;

export interface ActionScript {
  /** Route name, e.g. `/stock`. */
  route: string;
  name: string;
  run: (user: User) => Promise<void>;
}

export interface FailOne {
  phase: 'load' | 'action';
  key: string;
  variant: Variant;
  text: string;
  status: number;
}

export interface Run {
  redirect?: string;
  renderError?: string;
  /** Requests through the api-client seam, as `METHOD /path?sorted-query`. */
  load: string[];
  action: string[];
  /** Keys first requested AFTER a phase settled (during the late watch), that the phase had not already requested. */
  late: string[];
  /** Requests that did NOT go through the seam (global fetch, XHR, sendBeacon, WebSocket). */
  bypasses: string[];
  /** EventSource URLs opened. */
  streams: string[];
  /** Native form posts on screen after load, as `METHOD action`. */
  forms: string[];
  /** Requests with no success body in {@link SUCCESS}. */
  unrouted: string[];
  text: string;
  /** Every non-empty text node, trimmed — for naming what a page showed INSTEAD. */
  texts: string[];
  /** Every text node that was on screen at ANY moment of the run (a form before submit, a pending label, the outcome). */
  everShown: string[];
}

export function requestKey(method: string, url: URL): string {
  const query = new URLSearchParams([...url.searchParams].sort(([a], [b]) => a.localeCompare(b))).toString();
  return `${method.toUpperCase()} ${url.pathname}${query ? `?${query}` : ''}`;
}

class Boundary extends Component<{ fallback: (error: unknown, reset: () => void) => ReactNode; children: ReactNode }, { error: unknown; failed: boolean }> {
  override state = { error: undefined as unknown, failed: false };
  static getDerivedStateFromError(error: unknown) {
    return { error, failed: true };
  }
  override render() {
    return this.state.failed ? this.props.fallback(this.state.error, () => this.setState({ error: undefined, failed: false })) : this.props.children;
  }
}

function ClientProbe({ onClient }: { onClient: (client: QueryClient) => void }) {
  onClient(useQueryClient());
  return null;
}

const redirectTarget = (error: unknown): string | undefined => {
  const digest = (error as { digest?: unknown } | null)?.digest;
  return typeof digest === 'string' && digest.startsWith('NEXT_REDIRECT;') ? digest.split(';')[2] : undefined;
};

export function paramsOf(dir: string[]): Record<string, string> {
  const params: Record<string, string> = {};
  for (const segment of dir) {
    const match = /^\[+(?:\.\.\.)?([^\]]+)\]+$/.exec(segment);
    if (!match) continue;
    const value = PARAM_VALUES[match[1]!];
    if (value === undefined) throw new Error(`sweep: no PARAM_VALUES entry for the dynamic segment "${segment}" — add one`);
    params[match[1]!] = value;
  }
  return params;
}

async function asElement(file: RouteFile, props: Record<string, unknown>): Promise<ReactNode> {
  const Rendered = (await file.load()).default;
  return file.clientComponent ? <Rendered {...props} /> : ((await Rendered(props)) as ReactNode);
}

/**
 * What Next.js renders for a page-like file: the file itself, wrapped from the
 * inside out by each enclosing segment's `error` boundary and then its
 * `layout` (an `error` file does not wrap the layout of its own segment). The
 * root layout is not rendered here — it is `<html><body><Providers>`, which
 * the test checks, and the harness supplies that same `Providers`.
 */
async function pageTree(file: RouteFile, searchParams: Record<string, string>): Promise<ReactNode> {
  const params = paramsOf(file.dir);
  const props = { params: Promise.resolve(params), searchParams: Promise.resolve(searchParams) };
  let tree = await asElement(file, props);
  for (let depth = file.dir.length; depth >= 0; depth -= 1) {
    const dir = file.dir.slice(0, depth).join('/');
    const at = (kind: string) => ROUTE_FILES.find((candidate) => candidate.kind === kind && candidate.dir.join('/') === dir);
    const errorFile = at('error');
    if (errorFile) {
      const ErrorView = (await errorFile.load()).default;
      const inner = tree;
      tree = <Boundary fallback={(error, reset) => <ErrorView error={error} retry={reset} reset={reset} />}>{inner}</Boundary>;
    }
    const layout = at('layout');
    if (layout && depth > 0) tree = await asElement(layout, { children: tree, params: Promise.resolve(params) });
  }
  return tree;
}

function textNodes(): string[] {
  const out: string[] = [];
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    const text = node.textContent?.trim();
    if (text) out.push(text);
  }
  return out;
}

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Renders `file` — a page-like file, or an `error`/`global-error` boundary
 * given `boundaryError` — observes every request, optionally fails ONE of them,
 * optionally runs a user action, and waits until nothing is in flight.
 */
export async function runRoute(file: RouteFile, options: { searchParams?: Record<string, string>; action?: ActionScript; fail?: FailOne; boundaryError?: unknown; watchLate?: boolean } = {}): Promise<Run> {
  const run: Run = { load: [], action: [], late: [], everShown: [], bypasses: [], streams: [], forms: [], unrouted: [], text: '', texts: [] };
  const state = { phase: 'load' as 'load' | 'action', watching: false, inflight: 0 };
  const seen = { load: new Set<string>(), action: new Set<string>() };
  const late = new Set<string>();

  const bypass = (what: string) => run.bypasses.push(what);
  vi.stubGlobal('fetch', (input: unknown) => {
    bypass(`fetch ${String(input instanceof Request ? input.url : input)}`);
    return Promise.reject(new TypeError('sweep: a request bypassed the api-client seam'));
  });
  vi.stubGlobal(
    'XMLHttpRequest',
    class {
      open(method: string, url: string) {
        bypass(`XMLHttpRequest ${method} ${url}`);
      }
      send() {}
      setRequestHeader() {}
      addEventListener() {}
    },
  );
  vi.stubGlobal(
    'WebSocket',
    class {
      constructor(url: string) {
        bypass(`WebSocket ${url}`);
      }
      addEventListener() {}
      close() {}
    },
  );
  Object.defineProperty(navigator, 'sendBeacon', { configurable: true, value: (url: string) => (bypass(`sendBeacon ${url}`), true) });
  FakeEventSource.reset();
  vi.stubGlobal('EventSource', FakeEventSource);

  setApiFetch(async (input, init) => {
    const url = new URL(input, 'http://web.test');
    const method = (init?.method ?? 'GET').toUpperCase();
    const key = requestKey(method, url);
    if (state.watching && !seen[state.phase].has(key) && !seen.load.has(key)) late.add(`${state.phase}: ${key}`);
    seen[state.phase].add(key);
    state.inflight += 1;
    try {
      await sleep(0);
      if (options.fail && options.fail.phase === state.phase && options.fail.key === key) return problemResponse(options.fail.variant, options.fail.text, options.fail.status);
      const success = SUCCESS.find(([m, pattern]) => m === method && pattern.test(url.pathname));
      if (!success) {
        run.unrouted.push(key);
        return new Response('', { status: 599 });
      }
      return success[2](url);
    } finally {
      state.inflight -= 1;
    }
  });

  let client: QueryClient | undefined;
  const user = userEvent.setup();
  window.history.replaceState(null, '', urlOf(file, options.searchParams));
  const everShown = new Set<string>();
  const sample = () => {
    for (const text of textNodes()) everShown.add(text);
  };
  const observer = new MutationObserver(sample);
  observer.observe(document.body, { childList: true, subtree: true, characterData: true });
  try {
    let tree: ReactNode;
    if (file.kind === 'global-error') {
      const View = (await file.load()).default;
      const html = renderToStaticMarkup(<View error={options.boundaryError} retry={() => {}} reset={() => {}} />);
      const doc = new DOMParser().parseFromString(html, 'text/html');
      run.text = doc.documentElement.textContent ?? '';
      run.texts = [run.text.trim()];
      return run;
    } else if (file.kind === 'error') {
      const View = (await file.load()).default;
      tree = <View error={options.boundaryError} retry={() => {}} reset={() => {}} />;
    } else {
      try {
        tree = await pageTree(file, options.searchParams ?? {});
      } catch (error) {
        const target = redirectTarget(error);
        if (target === undefined) throw error;
        run.redirect = target;
        return run;
      }
    }

    await act(async () => {
      render(
        <Providers>
          <ClientProbe onClient={(c) => (client = c)} />
          <Boundary fallback={(error) => ((run.renderError = error instanceof Error ? `${error.name}: ${error.message}` : String(error)), null)}>
            <Suspense fallback={null}>{tree}</Suspense>
          </Boundary>
        </Providers>,
      );
    });

    const settle = async (label: string) => {
      let quiet = 0;
      const deadline = Date.now() + 20_000;
      while (Date.now() < deadline) {
        await act(async () => {
          await sleep(20);
        });
        const busy = state.inflight > 0 || (client?.isFetching() ?? 0) > 0 || (client?.isMutating() ?? 0) > 0;
        quiet = busy ? 0 : quiet + 1;
        if (quiet >= 5) return;
      }
      throw new Error(`sweep: ${file.name} (${label}) never settled — requests still in flight after 20 s`);
    };

    const watch = async (label: string) => {
      if (!options.watchLate) return;
      state.watching = true;
      await act(async () => {
        await sleep(LATE_WATCH_MS);
      });
      await settle(`${label}, late watch`);
      state.watching = false;
    };

    await settle('load');
    await watch('load');
    run.forms = [...document.querySelectorAll('form[action]')].map((form) => `${(form.getAttribute('method') ?? 'get').toUpperCase()} ${form.getAttribute('action')}`);
    if (options.action) {
      state.phase = 'action';
      await options.action.run(user);
      await settle(`action ${options.action.name}`);
      await watch(`action ${options.action.name}`);
    }
    run.text = document.body.textContent ?? '';
    run.texts = textNodes();
    return run;
  } finally {
    run.load = [...seen.load].sort();
    run.action = [...seen.action].sort();
    run.late = [...late].sort();
    sample();
    observer.disconnect();
    run.everShown = [...everShown];
    run.streams = FakeEventSource.instances.map((source) => source.url);
    cleanup();
    client?.clear();
    setApiFetch(undefined);
    vi.unstubAllGlobals();
    delete (navigator as { sendBeacon?: unknown }).sendBeacon;
  }
}

/** Every text node a failing run ends with that its all-success baseline never showed at any moment. */
export function addedTexts(run: Run, baseline: Run): string[] {
  const before = new Set(baseline.everShown);
  return run.texts.filter((text) => !before.has(text));
}

/** The same, joined — what the page put on screen INSTEAD of the sentinel. */
export function newTexts(run: Run, baseline: Run): string {
  const added = addedTexts(run, baseline);
  return added.length ? added.join(' ⏎ ') : '(no new text at all)';
}

/** The URL a route file is rendered at (dynamic segments filled from PARAM_VALUES). */
export function urlOf(file: RouteFile, searchParams: Record<string, string> = {}): string {
  const params = paramsOf(file.dir);
  const pathname = `/${file.dir
    .filter((segment) => !/^\(.*\)$/.test(segment) && !segment.startsWith('@'))
    .map((segment) => {
      const match = /^\[+(?:\.\.\.)?([^\]]+)\]+$/.exec(segment);
      return match ? params[match[1]!]! : segment;
    })
    .join('/')}`;
  const query = new URLSearchParams(searchParams).toString();
  return query ? `${pathname}?${query}` : pathname;
}

export { ApiError };
