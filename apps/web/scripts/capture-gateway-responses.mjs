// Captures REAL responses from a running #8 Gateway into
// src/test/fixtures/gateway/ — the bytes the web app's error-rendering tests
// feed through its route handlers.
//
// Why: #7's pages read a field that was always undefined, and every mock in
// its suite encoded the same wrong shape, so four review passes saw styled,
// wrong error text (review_web_app.md Pass 7; history.md "Notes for #8 and
// #9"). A fixture captured from the real Gateway cannot encode a shape the
// Gateway does not produce.
//
//   node --env-file-if-exists=../../.env.example --env-file-if-exists=../../.env scripts/capture-gateway-responses.mjs
//
// Needs the stack running (scripts/dev-stack.sh start); `--upstream-down`
// captures the 503 set instead, with Fulfillment and Billing stopped, and
// `--orders-down` the catalogue's 503 set, with Orders stopped (Orders is the
// `catalog.reference.list` responder: src/Orders/Presentation/OrdersCreateResponder.cs).
// `--only=a,b` writes just the named fixtures (the requests are still made in
// order, so the login-throttle ordering below is unchanged). Re-run it when the
// Gateway's problem documents change; the files are committed.
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';

const appRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const outDir = path.join(appRoot, 'src', 'test', 'fixtures', 'gateway');
const base = process.env.GATEWAY_BASE_URL ?? `http://localhost:${process.env.GATEWAY_PORT ?? '3001'}`;
const username = process.env.GATEWAY_OPERATOR_USERNAME ?? 'operator';
const password = process.env.GATEWAY_OPERATOR_PASSWORD;
if (!password) throw new Error('GATEWAY_OPERATOR_PASSWORD is not set');

async function call(method, pathname, { token, body, headers } = {}) {
  const response = await fetch(`${base}${pathname}`, {
    method,
    headers: { Accept: 'application/json', ...(body ? { 'Content-Type': 'application/json' } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}), ...headers },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  const kept = {};
  for (const name of ['content-type', 'retry-after', 'idempotent-replay']) {
    const value = response.headers.get(name);
    if (value !== null) kept[name] = value;
  }
  return { status: response.status, headers: kept, body: text };
}

const onlyArg = process.argv.find((arg) => arg.startsWith('--only='));
const only = onlyArg ? new Set(onlyArg.slice('--only='.length).split(',').filter(Boolean)) : undefined;

async function save(name, request, captured, expectStatus) {
  if (only && !only.has(name)) return;
  if (captured.status !== expectStatus) {
    throw new Error(`${name}: expected HTTP ${expectStatus}, the Gateway answered ${captured.status}: ${captured.body}`);
  }
  const record = { capturedFrom: `${request.method} ${request.path}`, capturedAt: new Date().toISOString(), status: captured.status, headers: captured.headers, body: captured.body };
  await writeFile(path.join(outDir, `${name}.json`), `${JSON.stringify(record, null, 2)}\n`, 'utf8');
  console.log(`captured ${name}: HTTP ${captured.status} ${captured.body.slice(0, 120)}`);
}

await mkdir(outDir, { recursive: true });
const upstreamDown = process.argv.includes('--upstream-down');
const ordersDown = process.argv.includes('--orders-down');

const login = await call('POST', '/auth/login', { body: { username, password } });
if (login.status !== 200) throw new Error(`login failed: ${login.status} ${login.body}`);
const token = JSON.parse(login.body).accessToken;

if (ordersDown) {
  // Run with Orders stopped (scripts/dev-stack.sh stop-service Orders): the
  // Gateway's own 503 for each catalogue collection the place-order page loads.
  for (const kind of ['retailers', 'companies', 'products']) {
    await save(`catalog-${kind}-upstream-unavailable-503`, { method: 'GET', path: `/catalog/${kind}` }, await call('GET', `/catalog/${kind}`, { token }), 503);
  }
  process.exit(0);
}

if (upstreamDown) {
  // Run with Fulfillment AND Billing stopped (scripts/dev-stack.sh stop-service …):
  // the Gateway's own 503 answers for a responder that is not there.
  await save('stock-list-upstream-unavailable-503', { method: 'GET', path: '/stock' }, await call('GET', '/stock', { token }), 503);
  await save('invoices-list-upstream-unavailable-503', { method: 'GET', path: '/invoices' }, await call('GET', '/invoices', { token }), 503);
  await save('credits-list-upstream-unavailable-503', { method: 'GET', path: '/credits' }, await call('GET', '/credits', { token }), 503);
  await save('place-order-upstream-unavailable-503', { method: 'POST', path: '/orders' }, await call('POST', '/orders', { token, body: { retailerCode: 'AldiEs', companyCode: 'IBERFOODS', currency: 'EUR', lines: [{ productCode: 'PRD-0001', quantity: 1 }] } }), 503);
  process.exit(0);
}

await save('orders-without-token-401', { method: 'GET', path: '/orders' }, await call('GET', '/orders'), 401);
// The orders LIST route's own failure: the read model is served by the Gateway
// itself (no responder to stop), so its reachable refusal is a bad page number.
await save('orders-list-bad-page-400', { method: 'GET', path: '/orders?page=0' }, await call('GET', '/orders?page=0', { token }), 400);
await save('order-malformed-id-400', { method: 'GET', path: '/orders/not-a-uuid' }, await call('GET', '/orders/not-a-uuid', { token }), 400);
const unknownId = randomUUID();
await save('order-unknown-404', { method: 'GET', path: '/orders/{random uuid}' }, await call('GET', `/orders/${unknownId}`, { token }), 404);
await save('place-order-no-lines-400', { method: 'POST', path: '/orders' }, await call('POST', '/orders', { token, body: { retailerCode: 'AldiEs', companyCode: 'IBERFOODS', currency: 'EUR', lines: [] } }), 400);
await save(
  'place-order-stock-unavailable-409',
  { method: 'POST', path: '/orders' },
  await call('POST', '/orders', { token, body: { retailerCode: 'AldiEs', companyCode: 'IBERFOODS', currency: 'EUR', lines: [{ productCode: 'PRD-0001', quantity: 999999 }] } }),
  409,
);
await save('replenish-unknown-product-404', { method: 'POST', path: '/stock/replenish' }, await call('POST', '/stock/replenish', { token, body: { companyCode: 'IBERFOODS', lines: [{ productCode: 'PRD-DOES-NOT-EXIST', units: 5 }] } }), 404);

const invoices = JSON.parse((await call('GET', '/invoices?status=issued&pageSize=1', { token })).body);
const invoice = invoices.items[0];
if (!invoice) throw new Error('no issued invoice to capture a payment mismatch against');
await save(
  'payment-amount-mismatch-422',
  { method: 'POST', path: '/invoices/{issued invoice}/payments' },
  await call('POST', `/invoices/${invoice.invoiceId}/payments`, { token, body: { paymentReference: `PAY-CAPTURE-${Date.now()}`, amount: { amount: invoice.totalAmount + 1, currency: invoice.currency }, valueDate: new Date().toISOString(), source: 'test' } }),
  422,
);

// Last: a failed login may count against the login throttle.
await save('login-bad-credentials-401', { method: 'POST', path: '/auth/login' }, await call('POST', '/auth/login', { body: { username, password: `${password}-wrong` } }), 401);
