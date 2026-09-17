// @vitest-environment node
//
// The BFF route handlers, called directly, against a REAL HTTP server standing
// in for the Gateway. What is asserted is what crossed the socket in each
// direction. Error bodies are the ones captured from the running #8 Gateway
// (src/test/fixtures/gateway/), so "relayed verbatim" is proven on real bytes.
import { NextRequest } from 'next/server';
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { FakeGateway, sendJson, sendRaw } from '@/test/fake-gateway';
import { gatewayFixture, type GatewayFixtureName } from '@/test/gateway-fixtures';
import { sealedSessionCookie, signedInRequest, TEST_TOKEN } from '@/test/session-cookie';
import { POST as login } from './auth/login/route';
import { POST as logout } from './auth/logout/route';
import { GET as session } from './auth/session/route';
import { GET as catalog } from './catalog/[kind]/route';
import { GET as credits } from './credits/route';
import { POST as registerPayment } from './invoices/[id]/payments/route';
import { GET as invoices } from './invoices/route';
import { GET as orderById } from './orders/[id]/route';
import { GET as listOrders, POST as placeOrder } from './orders/route';
import { GET as stock } from './stock/route';
import { POST as replenish } from './stock/replenish/route';

const APP = 'http://web.test';
const gateway = new FakeGateway();
const JWT_SHAPE = /eyJ[A-Za-z0-9_-]{8,}/;

function params<T>(value: T): { params: Promise<T> } {
  return { params: Promise.resolve(value) };
}

function relayFixture(name: GatewayFixtureName) {
  return (_req: unknown, res: Parameters<typeof sendRaw>[0]) => {
    const fixture = gatewayFixture(name);
    sendRaw(res, fixture.status, fixture.body, fixture.headers);
  };
}

beforeAll(async () => {
  process.env.GATEWAY_BASE_URL = await gateway.start();
  process.env.WEB_SESSION_PASSWORD = 'test-session-password-at-least-32-characters-long';
});

afterAll(async () => {
  await gateway.stop();
});

beforeEach(() => {
  gateway.requests.length = 0;
});

afterEach(() => {
  delete process.env.WEB_COOKIE_SECURE;
});

function stubLogin(): void {
  gateway.on('POST', '/auth/login', (_req, res) => sendJson(res, 200, { accessToken: TEST_TOKEN, tokenType: 'Bearer', expiresIn: 3600 }));
  gateway.on('GET', '/auth/me', (_req, res) => sendJson(res, 200, { username: 'operator', displayName: 'Demo Operator', roles: ['operator'] }));
}

describe('login — the JWT never reaches the browser', () => {
  it('a JSON login answers identity only; the token is sealed into an httpOnly cookie and appears in no body', async () => {
    stubLogin();
    const response = await login(new NextRequest(`${APP}/api/auth/login`, { method: 'POST', body: JSON.stringify({ username: 'operator', password: 'pw' }), headers: { 'content-type': 'application/json' } }));

    expect(response.status).toBe(200);
    const text = await response.text();
    expect(JSON.parse(text)).toEqual({ authenticated: true, username: 'operator', displayName: 'Demo Operator', roles: ['operator'] });
    expect(text).not.toContain(TEST_TOKEN);
    expect(text).not.toMatch(JWT_SHAPE);

    const setCookie = response.headers.get('set-cookie') ?? '';
    expect(setCookie).toMatch(/^otc_session=[^;]+;/);
    expect(setCookie).toMatch(/HttpOnly/i);
    expect(setCookie).toMatch(/SameSite=lax/i);
    expect(setCookie).toMatch(/Secure/i);
    expect(setCookie).not.toContain(TEST_TOKEN);
    expect(setCookie).not.toMatch(JWT_SHAPE);

    expect(gateway.requests.map((r) => `${r.method} ${r.url}`)).toEqual(['POST /auth/login', 'GET /auth/me']);
    expect(JSON.parse(gateway.requests[0]!.body)).toEqual({ username: 'operator', password: 'pw' });
    expect(gateway.requests[1]!.headers.authorization).toBe(`Bearer ${TEST_TOKEN}`);
  });

  it('a rejected login relays the Gateway\'s own problem document byte for byte, and sets no session', async () => {
    gateway.on('POST', '/auth/login', relayFixture('login-bad-credentials-401'));
    const response = await login(new NextRequest(`${APP}/api/auth/login`, { method: 'POST', body: JSON.stringify({ username: 'operator', password: 'wrong' }), headers: { 'content-type': 'application/json' } }));

    expect(response.status).toBe(401);
    expect(response.headers.get('content-type')).toBe('application/problem+json');
    expect(await response.text()).toBe(gatewayFixture('login-bad-credentials-401').body);
    expect(response.headers.get('set-cookie') ?? '').not.toMatch(/otc_session=[^;]+[^;]/);
  });

  it('a plain HTML form post (before hydration) signs in with a 303 to /orders — the password stays in the POST body', async () => {
    stubLogin();
    const response = await login(new NextRequest(`${APP}/api/auth/login`, { method: 'POST', body: 'username=operator&password=p%26w', headers: { 'content-type': 'application/x-www-form-urlencoded' } }));
    expect(response.status).toBe(303);
    expect(response.headers.get('location')).toBe(`${APP}/orders`);
    expect(response.headers.get('set-cookie')).toMatch(/^otc_session=[^;]+;.*HttpOnly/i);
    expect(JSON.parse(gateway.requests[0]!.body)).toEqual({ username: 'operator', password: 'p&w' });
  });

  it('a rejected form post returns to /login carrying the Gateway\'s own detail', async () => {
    gateway.on('POST', '/auth/login', relayFixture('login-bad-credentials-401'));
    const response = await login(new NextRequest(`${APP}/api/auth/login`, { method: 'POST', body: 'username=operator&password=nope', headers: { 'content-type': 'application/x-www-form-urlencoded' } }));
    expect(response.status).toBe(303);
    const location = new URL(response.headers.get('location') ?? '');
    expect(location.pathname).toBe('/login');
    expect(location.searchParams.get('error')).toBe('username or password is incorrect');
  });

  it('WEB_COOKIE_SECURE=false drops only the Secure flag', async () => {
    stubLogin();
    process.env.WEB_COOKIE_SECURE = 'false';
    const response = await login(new NextRequest(`${APP}/api/auth/login`, { method: 'POST', body: JSON.stringify({ username: 'operator', password: 'pw' }), headers: { 'content-type': 'application/json' } }));
    const setCookie = response.headers.get('set-cookie') ?? '';
    expect(setCookie).toMatch(/HttpOnly/i);
    expect(setCookie).not.toMatch(/Secure/i);
  });

  it('an unreadable body is a 400 problem, and the Gateway is never called', async () => {
    const response = await login(new NextRequest(`${APP}/api/auth/login`, { method: 'POST', body: '{not json', headers: { 'content-type': 'application/json' } }));
    expect(response.status).toBe(400);
    expect(await response.json()).toMatchObject({ code: 'VALIDATION_FAILED' });
    expect(gateway.requests).toHaveLength(0);
  });

  it('a Gateway that cannot be reached is a 502 problem naming it — for JSON and for a form post', async () => {
    const saved = process.env.GATEWAY_BASE_URL;
    process.env.GATEWAY_BASE_URL = 'http://127.0.0.1:1';
    try {
      const json = await login(new NextRequest(`${APP}/api/auth/login`, { method: 'POST', body: '{"username":"a","password":"b"}', headers: { 'content-type': 'application/json' } }));
      expect(json.status).toBe(502);
      const problem = (await json.json()) as { code: string; detail: string };
      expect(problem.code).toBe('GATEWAY_UNREACHABLE');
      expect(problem.detail).toContain('http://127.0.0.1:1');
      const form = await login(new NextRequest(`${APP}/api/auth/login`, { method: 'POST', body: 'username=a&password=b', headers: { 'content-type': 'application/x-www-form-urlencoded' } }));
      expect(new URL(form.headers.get('location') ?? '').searchParams.get('error')).toMatch(/could not be reached/);
    } finally {
      process.env.GATEWAY_BASE_URL = saved;
    }
  });
});

describe('session and logout', () => {
  it('GET /api/auth/session reports identity only, and nothing without a valid session', async () => {
    const signedIn = await session(await signedInRequest(`${APP}/api/auth/session`));
    const text = await signedIn.text();
    expect(JSON.parse(text)).toEqual({ authenticated: true, username: 'operator', displayName: 'Operator', roles: ['operator'] });
    expect(text).not.toMatch(JWT_SHAPE);

    expect(await (await session(new NextRequest(`${APP}/api/auth/session`))).json()).toEqual({ authenticated: false });
    const expired = await sealedSessionCookie({ expiresAt: Date.now() - 1 });
    expect(await (await session(new NextRequest(`${APP}/api/auth/session`, { headers: { cookie: `otc_session=${expired}` } }))).json()).toEqual({ authenticated: false });
    const tampered = `${(await sealedSessionCookie()).slice(0, -4)}AAAA`;
    expect(await (await session(new NextRequest(`${APP}/api/auth/session`, { headers: { cookie: `otc_session=${tampered}` } }))).json()).toEqual({ authenticated: false });
  });

  it('logout expires the session cookie', async () => {
    const response = await logout();
    expect(response.headers.get('set-cookie') ?? '<no Set-Cookie header>').toMatch(/^otc_session=;.*Max-Age=0/i);
  });
});

describe('authenticated proxying — every Gateway call is made here, with the token attached here', () => {
  it('without a session: 401 problem, and the Gateway is never called', async () => {
    const response = await listOrders(new NextRequest(`${APP}/api/orders`));
    expect(response.status).toBe(401);
    expect(await response.json()).toMatchObject({ code: 'UNAUTHENTICATED', title: 'Not signed in' });
    expect(gateway.requests).toHaveLength(0);
  });

  it('forwards the query string and the bearer token, and relays the body verbatim', async () => {
    const page = { items: [], page: { page: 2, pageSize: 20, total: 0 } };
    gateway.on('GET', '/orders', (_req, res) => sendJson(res, 200, page));
    const response = await listOrders(await signedInRequest(`${APP}/api/orders?status=placed&status=paid&page=2`));
    expect(response.status).toBe(200);
    expect(await response.json()).toEqual(page);
    expect(gateway.requests[0]!.url).toBe('/orders?status=placed&status=paid&page=2');
    expect(gateway.requests[0]!.headers.authorization).toBe(`Bearer ${TEST_TOKEN}`);
    expect(gateway.requests[0]!.headers.cookie).toBeUndefined();
  });

  it.each<[GatewayFixtureName, string, (url: string) => Promise<Response>]>([
    ['order-malformed-id-400', '/orders/not-a-uuid', async (url) => orderById(await signedInRequest(url), params({ id: 'not-a-uuid' }))],
    ['order-unknown-404', '/orders/0d0e5d53-5c8e-4c49-9a55-1f7a9e2c1b10', async (url) => orderById(await signedInRequest(url), params({ id: '0d0e5d53-5c8e-4c49-9a55-1f7a9e2c1b10' }))],
    ['place-order-no-lines-400', '/orders', async (url) => placeOrder(await signedInRequest(url, { method: 'POST', body: '{"lines":[]}' }))],
    ['place-order-stock-unavailable-409', '/orders', async (url) => placeOrder(await signedInRequest(url, { method: 'POST', body: '{}' }))],
    ['replenish-unknown-product-404', '/stock/replenish', async (url) => replenish(await signedInRequest(url, { method: 'POST', body: '{}' }))],
    ['payment-amount-mismatch-422', '/invoices/inv-1/payments', async (url) => registerPayment(await signedInRequest(url, { method: 'POST', body: '{}' }), params({ id: 'inv-1' }))],
  ])('relays the REAL Gateway problem %s unchanged — status, content type and body bytes', async (name, gatewayPath, invoke) => {
    const fixture = gatewayFixture(name);
    gateway.on(gatewayPath.startsWith('/orders/') ? 'GET' : 'POST', gatewayPath, relayFixture(name));
    const response = await invoke(`${APP}/api${gatewayPath}`);
    expect(response.status).toBe(fixture.status);
    expect(response.headers.get('content-type')).toBe(fixture.headers['content-type']);
    expect(await response.text()).toBe(fixture.body);
  });

  it('a Gateway 401 (token expired) is relayed and the session cookie is cleared', async () => {
    gateway.on('GET', '/stock', relayFixture('orders-without-token-401'));
    const response = await stock(await signedInRequest(`${APP}/api/stock`));
    expect(response.status).toBe(401);
    expect(await response.text()).toBe(gatewayFixture('orders-without-token-401').body);
    expect(response.headers.get('set-cookie') ?? '<no Set-Cookie header: the session was kept>').toMatch(/^otc_session=;.*Max-Age=0/i);
  });

  it('R55 — a 202 projection-pending answer keeps its status AND its Retry-After header', async () => {
    const pending = { orderId: '0d0e5d53-5c8e-4c49-9a55-1f7a9e2c1b10', status: 'projection_pending', message: 'not projected yet', retryAfterMs: 2000 };
    gateway.on('GET', '/orders/0d0e5d53-5c8e-4c49-9a55-1f7a9e2c1b10', (_req, res) => sendJson(res, 202, pending, { 'Retry-After': '2' }));
    const response = await orderById(await signedInRequest(`${APP}/api/orders/0d0e5d53-5c8e-4c49-9a55-1f7a9e2c1b10?ignored=1`), params({ id: '0d0e5d53-5c8e-4c49-9a55-1f7a9e2c1b10' }));
    expect(response.status).toBe(202);
    expect(response.headers.get('retry-after')).toBe('2');
    expect(await response.json()).toEqual(pending);
    expect(gateway.requests[0]!.url).toBe('/orders/0d0e5d53-5c8e-4c49-9a55-1f7a9e2c1b10');
  });

  it('an id is path-encoded, never able to reach another Gateway path', async () => {
    gateway.on('GET', '/orders/..%2Fstock', (_req, res) => sendJson(res, 404, { code: 'NOT_FOUND', title: 'x' }));
    await orderById(await signedInRequest(`${APP}/api/orders/x`), params({ id: '../stock' }));
    expect(gateway.requests[0]!.url).toBe('/orders/..%2Fstock');
  });

  it('POST /api/orders forwards the body bytes and the Idempotency-Key, and relays the 201', async () => {
    const accepted = { orderId: 'o-1', orderReference: 'ORD-000123', status: 'placed', currency: 'EUR', totalAmount: 1999, orderDate: '2026-09-16T10:00:00.000Z' };
    gateway.on('POST', '/orders', (_req, res) => sendJson(res, 201, accepted));
    const body = '{"retailerCode":"AldiEs","companyCode":"IBERFOODS","currency":"EUR","lines":[{"productCode":"PRD-0001","quantity":1,"unitPrice":1999}]}';
    const response = await placeOrder(await signedInRequest(`${APP}/api/orders?x=1`, { method: 'POST', body, headers: { 'idempotency-key': '6b3f1d0e-3a9f-4a53-9b61-0c7d9a1f2e10', 'x-not-forwarded': 'y' } }));
    expect(response.status).toBe(201);
    expect(await response.json()).toEqual(accepted);
    const sent = gateway.requests[0]!;
    expect(sent.url).toBe('/orders');
    expect(sent.body).toBe(body);
    expect(sent.headers['content-type']).toBe('application/json');
    expect(sent.headers['idempotency-key']).toBe('6b3f1d0e-3a9f-4a53-9b61-0c7d9a1f2e10');
    expect(sent.headers['x-not-forwarded']).toBeUndefined();
  });

  it('R47/R48 — a new payment (201) and a replay (200 + Idempotent-Replay) keep their own statuses', async () => {
    let calls = 0;
    gateway.on('POST', '/invoices/inv-7/payments', (_req, res) => {
      calls += 1;
      const outcome = calls === 1 ? 'accepted' : 'duplicate';
      sendJson(res, calls === 1 ? 201 : 200, { outcome, paymentReference: 'PAY-1', invoiceReference: 'INV-000007', invoiceStatus: 'paid' }, calls === 1 ? {} : { 'Idempotent-Replay': 'true' });
    });
    const first = await registerPayment(await signedInRequest(`${APP}/api/invoices/inv-7/payments`, { method: 'POST', body: '{"paymentReference":"PAY-1"}' }), params({ id: 'inv-7' }));
    const second = await registerPayment(await signedInRequest(`${APP}/api/invoices/inv-7/payments`, { method: 'POST', body: '{"paymentReference":"PAY-1"}' }), params({ id: 'inv-7' }));
    expect([first.status, (await first.json()).outcome, first.headers.get('idempotent-replay')]).toEqual([201, 'accepted', null]);
    expect([second.status, (await second.json()).outcome, second.headers.get('idempotent-replay')]).toEqual([200, 'duplicate', 'true']);
    expect(gateway.requests.every((r) => r.body === '{"paymentReference":"PAY-1"}')).toBe(true);
  });

  it('invoices, credits and stock map to their own Gateway paths', async () => {
    const page = { items: [], page: { page: 1, pageSize: 20, total: 0 } };
    gateway.on('GET', '/invoices', (_req, res) => sendJson(res, 200, page));
    gateway.on('GET', '/credits', (_req, res) => sendJson(res, 200, page));
    gateway.on('GET', '/stock', (_req, res) => sendJson(res, 200, page));
    await invoices(await signedInRequest(`${APP}/api/invoices?status=issued`));
    await credits(await signedInRequest(`${APP}/api/credits?retailerCode=AldiEs`));
    await stock(await signedInRequest(`${APP}/api/stock?belowThreshold=true`));
    expect(gateway.requests.map((r) => r.url)).toEqual(['/invoices?status=issued', '/credits?retailerCode=AldiEs', '/stock?belowThreshold=true']);
  });

  it('the catalogue proxies only its three literal collections', async () => {
    gateway.on('GET', '/catalog/products', (_req, res) => sendJson(res, 200, { items: [] }));
    const known = await catalog(await signedInRequest(`${APP}/api/catalog/products`), params({ kind: 'products' }));
    expect(known.status).toBe(200);
    const unknown = await catalog(await signedInRequest(`${APP}/api/catalog/orders`), params({ kind: 'orders' }));
    expect(unknown.status).toBe(404);
    expect(await unknown.json()).toMatchObject({ code: 'NOT_FOUND', detail: 'There is no catalogue named "orders".' });
    expect(gateway.requests.map((r) => r.url)).toEqual(['/catalog/products']);
  });

  it('a Gateway that cannot be reached is a 502 problem, not a thrown error', async () => {
    const saved = process.env.GATEWAY_BASE_URL;
    process.env.GATEWAY_BASE_URL = 'http://127.0.0.1:1/';
    try {
      const response = await listOrders(await signedInRequest(`${APP}/api/orders`));
      expect(response.status).toBe(502);
      expect(await response.json()).toMatchObject({ code: 'GATEWAY_UNREACHABLE', title: 'The Gateway could not be reached' });
    } finally {
      process.env.GATEWAY_BASE_URL = saved;
    }
  });
});
