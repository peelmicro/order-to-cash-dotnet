// @vitest-environment node
//
// Trap 1, end to end in one process: the browser client (apiRequest) → this
// app's real route handler → a Gateway answering with a REAL captured problem
// document → back to `describeError`, which is what every error-rendering
// component shows. For every captured fixture, the text the user would see is
// that problem's own `detail`.
import { readdirSync } from 'node:fs';
import path from 'node:path';
import { NextRequest } from 'next/server';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { POST as login } from '@/app/api/auth/login/route';
import { POST as registerPayment } from '@/app/api/invoices/[id]/payments/route';
import { GET as orderById } from '@/app/api/orders/[id]/route';
import { GET as listOrders, POST as placeOrder } from '@/app/api/orders/route';
import { GET as catalog } from '@/app/api/catalog/[kind]/route';
import { GET as credits } from '@/app/api/credits/route';
import { GET as invoices } from '@/app/api/invoices/route';
import { POST as replenish } from '@/app/api/stock/replenish/route';
import { GET as stock } from '@/app/api/stock/route';
import { FakeGateway, sendRaw } from '@/test/fake-gateway';
import { fixtureDetail, gatewayFixture, type GatewayFixtureName } from '@/test/gateway-fixtures';
import { sealedSessionCookie } from '@/test/session-cookie';
import { apiRequest, setApiFetch } from './api-client';
import { describeError } from './problem';

type Route = (request: NextRequest) => Promise<Response>;

/** Which browser call reaches which route, for each captured Gateway answer. A literal table: a fixture file with no row fails the completeness test below. */
const CASES: Record<GatewayFixtureName, { method: 'GET' | 'POST'; browserPath: string; gatewayPath: string; route: Route; signedIn: boolean }> = {
  'login-bad-credentials-401': { method: 'POST', browserPath: '/api/auth/login', gatewayPath: '/auth/login', route: login, signedIn: false },
  'order-malformed-id-400': { method: 'GET', browserPath: '/api/orders/not-a-uuid', gatewayPath: '/orders/not-a-uuid', route: (r) => orderById(r, { params: Promise.resolve({ id: 'not-a-uuid' }) }), signedIn: true },
  'order-unknown-404': { method: 'GET', browserPath: '/api/orders/0d0e5d53-5c8e-4c49-9a55-1f7a9e2c1b10', gatewayPath: '/orders/0d0e5d53-5c8e-4c49-9a55-1f7a9e2c1b10', route: (r) => orderById(r, { params: Promise.resolve({ id: '0d0e5d53-5c8e-4c49-9a55-1f7a9e2c1b10' }) }), signedIn: true },
  'orders-without-token-401': { method: 'GET', browserPath: '/api/orders', gatewayPath: '/orders', route: listOrders, signedIn: true },
  'payment-amount-mismatch-422': { method: 'POST', browserPath: '/api/invoices/inv-1/payments', gatewayPath: '/invoices/inv-1/payments', route: (r) => registerPayment(r, { params: Promise.resolve({ id: 'inv-1' }) }), signedIn: true },
  'place-order-no-lines-400': { method: 'POST', browserPath: '/api/orders', gatewayPath: '/orders', route: placeOrder, signedIn: true },
  'place-order-stock-unavailable-409': { method: 'POST', browserPath: '/api/orders', gatewayPath: '/orders', route: placeOrder, signedIn: true },
  'place-order-upstream-unavailable-503': { method: 'POST', browserPath: '/api/orders', gatewayPath: '/orders', route: placeOrder, signedIn: true },
  'replenish-unknown-product-404': { method: 'POST', browserPath: '/api/stock/replenish', gatewayPath: '/stock/replenish', route: replenish, signedIn: true },
  'stock-list-upstream-unavailable-503': { method: 'GET', browserPath: '/api/stock', gatewayPath: '/stock', route: stock, signedIn: true },
  'invoices-list-upstream-unavailable-503': { method: 'GET', browserPath: '/api/invoices', gatewayPath: '/invoices', route: invoices, signedIn: true },
  'credits-list-upstream-unavailable-503': { method: 'GET', browserPath: '/api/credits', gatewayPath: '/credits', route: credits, signedIn: true },
  'orders-list-bad-page-400': { method: 'GET', browserPath: '/api/orders?page=0', gatewayPath: '/orders', route: listOrders, signedIn: true },
  'catalog-retailers-upstream-unavailable-503': { method: 'GET', browserPath: '/api/catalog/retailers', gatewayPath: '/catalog/retailers', route: (r) => catalog(r, { params: Promise.resolve({ kind: 'retailers' }) }), signedIn: true },
  'catalog-companies-upstream-unavailable-503': { method: 'GET', browserPath: '/api/catalog/companies', gatewayPath: '/catalog/companies', route: (r) => catalog(r, { params: Promise.resolve({ kind: 'companies' }) }), signedIn: true },
  'catalog-products-upstream-unavailable-503': { method: 'GET', browserPath: '/api/catalog/products', gatewayPath: '/catalog/products', route: (r) => catalog(r, { params: Promise.resolve({ kind: 'products' }) }), signedIn: true },
};

const gateway = new FakeGateway();
let current: GatewayFixtureName | undefined;

beforeAll(async () => {
  for (const { method, gatewayPath } of Object.values(CASES)) {
    gateway.on(method, gatewayPath, (_req, res) => {
      const fixture = gatewayFixture(current!);
      sendRaw(res, fixture.status, fixture.body, fixture.headers);
    });
  }
  process.env.GATEWAY_BASE_URL = await gateway.start();
  process.env.WEB_SESSION_PASSWORD = 'test-session-password-at-least-32-characters-long';
});

afterAll(async () => {
  setApiFetch(undefined);
  await gateway.stop();
});

describe('the error a user sees is the Gateway\'s own detail — every captured answer, through the real route handler', () => {
  it('every captured fixture file has a case (the population is the directory, not this table)', () => {
    const files = readdirSync(path.join(import.meta.dirname, '..', 'test', 'fixtures', 'gateway')).map((f) => f.replace(/\.json$/, '')).sort();
    expect(files).toEqual(Object.keys(CASES).sort());
    expect(files.length).toBeGreaterThanOrEqual(16);
  });

  it.each(Object.keys(CASES) as GatewayFixtureName[])('%s', async (name) => {
    current = name;
    const spec = CASES[name];
    const cookie = spec.signedIn ? `otc_session=${await sealedSessionCookie()}` : '';
    setApiFetch((input, init) => spec.route(new NextRequest(new URL(input, 'http://web.test'), { method: init?.method, body: init?.body as string | undefined, headers: { ...Object.fromEntries(new Headers(init?.headers)), cookie } })));

    const failure = await apiRequest(spec.browserPath, { method: spec.method, json: spec.method === 'POST' ? { any: 'body' } : undefined }).catch((error: unknown) => error);
    const shown = describeError(failure, 'GENERIC FALLBACK');

    expect(shown).toBe(fixtureDetail(name));
    expect(shown).not.toBe('GENERIC FALLBACK');
  });
});
