import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { NextRequest } from 'next/server';
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import { POST as paymentsRoute } from '@/app/api/invoices/[id]/payments/route';
import type { Credit, CreditPage, Invoice, InvoicePage, RegisterPaymentRequest, RegisterPaymentResponse } from '@/lib/api-types';
import { setApiFetch } from '@/lib/api-client';
import { FakeGateway, sendJson, sendRaw } from '@/test/fake-gateway';
import { fixtureDetail, fixtureResponse, gatewayFixture } from '@/test/gateway-fixtures';
import { deferred, json, renderWithQuery, routeApi, type ApiCall, type RouteHandler } from '@/test/render';
import { BillingView, suggestPaymentReference } from './billing-view';

// See place-order-form.test.tsx: jsdom cannot seal a session; the session is proven under node.
vi.mock('@/server/session', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/server/session')>()),
  readSession: async () => ({ accessToken: 'test-token', expiresAt: Date.now() + 60_000, username: 'operator', roles: [] }),
}));

function invoice(overrides: Partial<Invoice> = {}): Invoice {
  return {
    invoiceId: 'inv-1',
    invoiceReference: 'INV-000027',
    invoiceDate: '2026-09-16T10:00:00.000Z',
    orderReference: 'ORD-000042',
    retailerCode: 'AldiEs',
    companyCode: 'IBERFOODS',
    currency: 'EUR',
    amount: 24999,
    discount: 0,
    totalAmount: 24999,
    status: 'issued',
    ...overrides,
  };
}

function credit(overrides: Partial<Credit> = {}): Credit {
  return { creditCode: 'CR-000001', retailerCode: 'AldiEs', companyCode: 'IBERFOODS', currency: 'EUR', creditLimit: 500000, activeHolds: 24999, openExposure: 0, availableCredit: 475001, ...overrides };
}

const invoicePage = (items: Invoice[]): InvoicePage => ({ items, page: { page: 1, pageSize: 20, total: items.length } });
const creditPage = (items: Credit[], total = items.length, page = 1): CreditPage => ({ items, page: { page, pageSize: 20, total } });
const retailersResponse = () => json({ items: [{ code: 'AldiGb', name: 'Aldi UK', country: 'GB', gln: '5400000000072', currency: 'GBP', enabled: true }] });

function billingRoutes(routes: Partial<Record<string, RouteHandler>> = {}): ApiCall[] {
  return routeApi({
    'GET /api/catalog/retailers': retailersResponse,
    'GET /api/invoices': () => json(invoicePage([invoice()])),
    'GET /api/credits': () => json(creditPage([credit()])),
    'GET /api/orders': () => json({ items: [{ orderId: 'order-uuid-42' }], page: { page: 1, pageSize: 1, total: 1 } }),
    ...(routes as Record<string, RouteHandler>),
  });
}

const paymentPosts = (calls: ApiCall[]) => calls.filter((c) => c.method === 'POST').map((c) => c.body as RegisterPaymentRequest);
const reply = (outcome: 'accepted' | 'duplicate', reference = 'PAY-X'): RegisterPaymentResponse => ({ outcome, paymentReference: reference, invoiceReference: 'INV-000027', orderReference: 'ORD-000042', invoiceStatus: 'paid', paidAt: '2026-09-16T10:05:00.000Z' });

describe('BillingView — invoices and credit limits', () => {
  it('renders invoice and credit amounts as decimals from minor units, never the raw integer', async () => {
    billingRoutes({ 'GET /api/invoices': () => json(invoicePage([invoice(), invoice({ invoiceId: 'inv-jp', invoiceReference: 'INV-000028', currency: 'JPY', totalAmount: 1999, status: 'paid' })])) });
    renderWithQuery(<BillingView />);
    const [eur, jpy] = await screen.findAllByTestId('invoice-row');
    expect(within(eur!).getByTestId('invoice-total')).toHaveTextContent('€249.99');
    expect(within(eur!).getByTestId('invoice-total')).not.toHaveTextContent('24999');
    expect(within(jpy!).getByTestId('invoice-total')).toHaveTextContent('JP¥1,999');
    expect(within(eur!).getByText('ORD-000042')).toBeInTheDocument();
    expect(within(jpy!).queryByTestId('register-payment-button')).not.toBeInTheDocument();
    const row = await screen.findByTestId('credit-row');
    expect(within(row).getByTestId('credit-held')).toHaveTextContent('€249.99');
    expect(within(row).getByTestId('credit-available')).toHaveTextContent('€4,750.01');
    expect(within(row).getByText('€5,000.00')).toBeInTheDocument();
  });

  it('shows each table\'s loading state while its request is UNRESOLVED, then its empty state', async () => {
    const invoices = deferred();
    const credits = deferred();
    billingRoutes({ 'GET /api/invoices': () => invoices.promise, 'GET /api/credits': () => credits.promise });
    renderWithQuery(<BillingView />);
    expect(await screen.findByTestId('invoices-loading')).toBeInTheDocument();
    expect(screen.getByTestId('credits-loading')).toBeInTheDocument();
    expect(screen.queryByTestId('invoices-empty')).not.toBeInTheDocument();
    invoices.release(json(invoicePage([])));
    expect(await screen.findByTestId('invoices-empty')).toHaveTextContent('No invoices match these filters.');
    expect(screen.getByTestId('credits-loading')).toBeInTheDocument();
    credits.release(json(creditPage([])));
    expect(await screen.findByTestId('credits-empty')).toHaveTextContent('No credit lines match this filter.');
  });

  it('Billing being down shows each list\'s own real 503 words, independently', async () => {
    billingRoutes({ 'GET /api/invoices': () => fixtureResponse('invoices-list-upstream-unavailable-503') });
    renderWithQuery(<BillingView />);
    expect((await screen.findByTestId('invoices-error')).textContent).toBe(`Could not load invoices: ${fixtureDetail('invoices-list-upstream-unavailable-503')}`);
    expect(await screen.findByTestId('credit-row')).toBeInTheDocument();
    expect(screen.queryByTestId('invoices-empty')).not.toBeInTheDocument();
  });

  it('a failed credit list does not take the invoice list with it', async () => {
    billingRoutes({ 'GET /api/credits': () => fixtureResponse('credits-list-upstream-unavailable-503') });
    renderWithQuery(<BillingView />);
    expect((await screen.findByTestId('credits-error')).textContent).toBe(`Could not load credit limits: ${fixtureDetail('credits-list-upstream-unavailable-503')}`);
    expect(await screen.findByTestId('invoice-row')).toBeInTheDocument();
    expect(screen.queryByTestId('credits-pager')).not.toBeInTheDocument();
  });

  it('credit limits page server-side and filter by retailer; invoices filter by status and retailer', async () => {
    const all = Array.from({ length: 25 }, (_, i) => credit({ creditCode: `CR-${String(i + 1).padStart(6, '0')}` }));
    const calls = billingRoutes({
      'GET /api/credits': (call) => {
        const pageNumber = Number(call.query.get('page') ?? '1');
        return json(creditPage(all.slice((pageNumber - 1) * 20, pageNumber * 20), all.length, pageNumber));
      },
    });
    renderWithQuery(<BillingView />);
    expect(await screen.findAllByTestId('credit-row')).toHaveLength(20);
    expect(screen.getByTestId('credits-pager')).toHaveTextContent('Page 1 of 2 · 25 credit lines');
    await userEvent.click(screen.getByTestId('credits-pager-next'));
    await waitFor(() => expect(screen.getAllByTestId('credit-row')).toHaveLength(5));

    const [creditRetailer, invoiceRetailer] = screen.getAllByLabelText('Retailer');
    await waitFor(() => expect(within(creditRetailer!).getAllByRole('option')).toHaveLength(2));
    await userEvent.selectOptions(creditRetailer!, 'AldiGb');
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/credits').at(-1)?.query.get('retailerCode')).toBe('AldiGb'));
    expect(calls.filter((c) => c.path === '/api/credits').at(-1)?.query.get('page')).toBe('1');

    await userEvent.selectOptions(screen.getByLabelText('Status'), 'paid');
    await userEvent.selectOptions(invoiceRetailer!, 'AldiGb');
    await waitFor(() => {
      const last = calls.filter((c) => c.path === '/api/invoices').at(-1);
      expect([last?.query.get('status'), last?.query.get('retailerCode')]).toEqual(['paid', 'AldiGb']);
    });
  });

  it('exposes real column headers on both tables', async () => {
    billingRoutes();
    renderWithQuery(<BillingView />);
    await screen.findByTestId('credit-row');
    await screen.findByTestId('invoice-row');
    const headers = screen.getAllByRole('columnheader');
    expect(headers.map((h) => h.textContent)).toEqual(['Retailer', 'Company', 'Limit', 'Held', 'Open exposure', 'Available', 'Invoice', 'Order', 'Retailer', 'Company', 'Issued', 'Total', 'Status', 'Actions']);
    headers.forEach((header) => expect(header).toHaveAttribute('scope', 'col'));
  });
});

describe('BillingView — Register payment (R47/R48)', () => {
  it('suggests a reference shaped like the spec example and pre-fills the invoice total as a decimal', async () => {
    expect(suggestPaymentReference({ invoiceReference: 'INV-000027' }, new Date('2026-08-18T11:00:00Z'))).toBe('PAY-2026-08-18-000027');
    billingRoutes();
    renderWithQuery(<BillingView />);
    await userEvent.click(await screen.findByTestId('register-payment-button'));
    expect(screen.getByTestId('payment-reference-input')).toHaveValue(suggestPaymentReference({ invoiceReference: 'INV-000027' }));
    expect(screen.getByTestId('payment-amount-input')).toHaveValue('249.99');
    expect(screen.getByLabelText('Amount (EUR)')).toBeInTheDocument();
    expect(screen.getByTestId('submit-payment-button')).toBeEnabled();
  });

  it('"19.99" typed by the user reaches the wire as exactly 1999 minor units, in the invoice currency, from the operator', async () => {
    const calls = billingRoutes({ 'POST /api/invoices/inv-1/payments': () => json(reply('accepted'), 201) });
    renderWithQuery(<BillingView />);
    await userEvent.click(await screen.findByTestId('register-payment-button'));
    await userEvent.clear(screen.getByTestId('payment-reference-input'));
    await userEvent.type(screen.getByTestId('payment-reference-input'), 'PAY-OPERATOR-1');
    await userEvent.clear(screen.getByTestId('payment-amount-input'));
    await userEvent.type(screen.getByTestId('payment-amount-input'), '19.99');
    await userEvent.click(screen.getByTestId('submit-payment-button'));

    await waitFor(() => expect(paymentPosts(calls)).toHaveLength(1));
    const body = paymentPosts(calls)[0]!;
    expect(body).toMatchObject({ paymentReference: 'PAY-OPERATOR-1', amount: { amount: 1999, currency: 'EUR' }, source: 'operator' });
    expect(Number.isInteger(body.amount.amount)).toBe(true);
    expect(new Date(body.valueDate).toISOString()).toBe(body.valueDate);
    expect(calls.find((c) => c.method === 'POST')?.path).toBe('/api/invoices/inv-1/payments');
  });

  it('an accepted payment and a duplicate replay are rendered DIFFERENTLY, each saying what happened', async () => {
    let n = 0;
    billingRoutes({
      'POST /api/invoices/inv-1/payments': (call) => {
        n += 1;
        const reference = (call.body as RegisterPaymentRequest).paymentReference;
        return n === 1 ? json(reply('accepted', reference), 201) : json(reply('duplicate', reference), 200, { 'Idempotent-Replay': 'true' });
      },
    });
    renderWithQuery(<BillingView />);
    await userEvent.click(await screen.findByTestId('register-payment-button'));
    await userEvent.click(screen.getByTestId('submit-payment-button'));

    const accepted = await screen.findByTestId('payment-outcome-accepted');
    expect(accepted).toHaveTextContent(/recorded \(HTTP 201\) — invoice INV-000027 is now paid/);
    expect(screen.queryByTestId('payment-outcome-duplicate')).not.toBeInTheDocument();
    expect(await screen.findByTestId('view-order-link')).toHaveAttribute('href', '/orders/order-uuid-42');

    await userEvent.click(screen.getByTestId('submit-payment-button'));
    const duplicate = await screen.findByTestId('payment-outcome-duplicate');
    expect(duplicate).toHaveTextContent(/was already recorded \(HTTP 200\) — nothing new was created/);
    expect(screen.queryByTestId('payment-outcome-accepted')).not.toBeInTheDocument();
  });

  it('while the order-link lookup is UNRESOLVED the form says it is resolving the link, and nothing else', async () => {
    const lookup = deferred();
    billingRoutes({ 'POST /api/invoices/inv-1/payments': () => json(reply('accepted'), 201), 'GET /api/orders': () => lookup.promise });
    renderWithQuery(<BillingView />);
    await userEvent.click(await screen.findByTestId('register-payment-button'));
    await userEvent.click(screen.getByTestId('submit-payment-button'));
    await screen.findByTestId('payment-outcome-accepted');
    expect(await screen.findByTestId('view-order-link-resolving')).toHaveTextContent('resolving the order link…');
    expect(screen.queryByTestId('view-order-link-not-found')).not.toBeInTheDocument();
    expect(screen.queryByTestId('view-order-link')).not.toBeInTheDocument();
    lookup.release(json({ items: [{ orderId: 'order-uuid-42' }], page: { page: 1, pageSize: 1, total: 1 } }));
    expect(await screen.findByTestId('view-order-link')).toHaveAttribute('href', '/orders/order-uuid-42');
    expect(screen.queryByText(/resolving the order link/)).not.toBeInTheDocument();
  });

  it('an order-link lookup that ANSWERS with no order says no order was found — it never stays on "resolving" (id 29 bullet 4)', async () => {
    const calls = billingRoutes({ 'POST /api/invoices/inv-1/payments': () => json(reply('accepted'), 201), 'GET /api/orders': () => json({ items: [], page: { page: 1, pageSize: 1, total: 0 } }) });
    renderWithQuery(<BillingView />);
    await userEvent.click(await screen.findByTestId('register-payment-button'));
    await userEvent.click(screen.getByTestId('submit-payment-button'));
    await screen.findByTestId('payment-outcome-accepted');
    await waitFor(() => expect(calls.filter((c) => c.path === '/api/orders' && c.query.get('orderReference') === 'ORD-000042')).toHaveLength(1));
    await waitFor(() => expect(screen.getByTestId('payment-form').textContent, 'the answered lookup must not still read as working').not.toMatch(/resolving/), { timeout: 2_000 });
    expect(screen.getByTestId('view-order-link-not-found')).toHaveTextContent('no order was found for ORD-000042, so there is no timeline to link to.');
    expect(screen.queryByTestId('view-order-link')).not.toBeInTheDocument();
  });

  it('the outcome stays on screen when the paid invoice drops out of an "issued" list on the next read', async () => {
    let paid = false;
    billingRoutes({
      'GET /api/invoices': () => json(invoicePage(paid ? [] : [invoice()])),
      'POST /api/invoices/inv-1/payments': () => {
        paid = true;
        return json(reply('accepted'), 201);
      },
    });
    renderWithQuery(<BillingView />);
    await userEvent.selectOptions(screen.getByLabelText('Status'), 'issued');
    await userEvent.click(await screen.findByTestId('register-payment-button'));
    expect(screen.getByTestId('payment-form')).toHaveTextContent('Register payment for INV-000027 (ORD-000042) — €249.99');
    await userEvent.click(screen.getByTestId('submit-payment-button'));
    expect(await screen.findByTestId('invoices-empty')).toBeInTheDocument();
    expect(screen.getByTestId('payment-outcome-accepted'), 'the accepted outcome after the invoice left the list').toBeInTheDocument();
  });

  it('shows "Registering…" while the payment is UNRESOLVED', async () => {
    const response = deferred();
    billingRoutes({ 'POST /api/invoices/inv-1/payments': () => response.promise });
    renderWithQuery(<BillingView />);
    await userEvent.click(await screen.findByTestId('register-payment-button'));
    await userEvent.click(screen.getByTestId('submit-payment-button'));
    expect(await screen.findByRole('button', { name: 'Registering…' })).toBeDisabled();
    response.release(json(reply('accepted'), 201));
    expect(await screen.findByTestId('payment-outcome-accepted')).toBeInTheDocument();
  });

  it('an amount with too many decimals, or an empty reference, is refused in place and nothing is sent', async () => {
    const calls = billingRoutes({ 'POST /api/invoices/inv-1/payments': () => json(reply('accepted'), 201) });
    renderWithQuery(<BillingView />);
    await userEvent.click(await screen.findByTestId('register-payment-button'));
    await userEvent.clear(screen.getByTestId('payment-amount-input'));
    await userEvent.type(screen.getByTestId('payment-amount-input'), '249.999');
    await userEvent.click(screen.getByTestId('submit-payment-button'));
    expect(screen.getByText('Enter an amount in EUR (for example 249.99).')).toBeInTheDocument();
    await userEvent.clear(screen.getByTestId('payment-reference-input'));
    await userEvent.click(screen.getByTestId('submit-payment-button'));
    expect(screen.getByText('Enter the remittance reference.')).toBeInTheDocument();
    expect(paymentPosts(calls)).toHaveLength(0);
  });

  it('a JPY invoice takes whole yen', async () => {
    const calls = billingRoutes({
      'GET /api/invoices': () => json(invoicePage([invoice({ invoiceId: 'inv-jp', currency: 'JPY', totalAmount: 1999 })])),
      'POST /api/invoices/inv-jp/payments': () => json(reply('accepted'), 201),
    });
    renderWithQuery(<BillingView />);
    await userEvent.click(await screen.findByTestId('register-payment-button'));
    expect(screen.getByTestId('payment-amount-input')).toHaveValue('1999');
    await userEvent.click(screen.getByTestId('submit-payment-button'));
    await waitFor(() => expect(paymentPosts(calls)).toHaveLength(1));
    expect(paymentPosts(calls)[0]!.amount).toEqual({ amount: 1999, currency: 'JPY' });
  });

  it('a rejected remittance shows the Gateway\'s own words (real 422), and not on another invoice\'s form', async () => {
    billingRoutes({
      'GET /api/invoices': () => json(invoicePage([invoice(), invoice({ invoiceId: 'inv-2', invoiceReference: 'INV-000028' })])),
      'POST /api/invoices/inv-1/payments': () => fixtureResponse('payment-amount-mismatch-422'),
    });
    renderWithQuery(<BillingView />);
    await userEvent.click((await screen.findAllByTestId('register-payment-button'))[0]!);
    await userEvent.click(screen.getByTestId('submit-payment-button'));
    expect((await screen.findByTestId('payment-error')).textContent).toBe(fixtureDetail('payment-amount-mismatch-422'));
    expect(screen.queryByTestId('payment-outcome-accepted')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await userEvent.click((await screen.findAllByTestId('register-payment-button'))[1]!);
    expect(screen.getByTestId('payment-form')).toBeInTheDocument();
    expect(screen.queryByTestId('payment-error')).not.toBeInTheDocument();
  });

  describe('through the real payments route handler, against a Gateway that answers 201 then 200 and then its REAL 422', () => {
    const gateway = new FakeGateway();
    let n = 0;

    beforeAll(async () => {
      gateway.on('POST', '/invoices/inv-1/payments', (req, res) => {
        n += 1;
        if (n === 3) {
          const fixture = gatewayFixture('payment-amount-mismatch-422');
          sendRaw(res, fixture.status, fixture.body, fixture.headers);
          return;
        }
        const reference = (JSON.parse(req.body) as RegisterPaymentRequest).paymentReference;
        sendJson(res, n === 1 ? 201 : 200, reply(n === 1 ? 'accepted' : 'duplicate', reference), n === 1 ? {} : { 'Idempotent-Replay': 'true' });
      });
      process.env.GATEWAY_BASE_URL = await gateway.start();
    });

    afterAll(async () => {
      await gateway.stop();
    });

    it('accepted, then duplicate, then the real mismatch detail — each rendered as the server said', async () => {
      const viaRoute = async (input: string, init?: RequestInit) => {
        const url = new URL(input, 'http://web.test');
        if (url.pathname === '/api/invoices/inv-1/payments') {
          return paymentsRoute(new NextRequest(url, { method: 'POST', body: init?.body as string, headers: new Headers(init?.headers) }), { params: Promise.resolve({ id: 'inv-1' }) });
        }
        return url.pathname.startsWith('/api/invoices')
          ? json(invoicePage([invoice()]))
          : url.pathname.startsWith('/api/credits')
            ? json(creditPage([credit()]))
            : url.pathname.startsWith('/api/orders')
              ? json({ items: [], page: { page: 1, pageSize: 1, total: 0 } })
              : retailersResponse();
      };
      setApiFetch(viaRoute);
      renderWithQuery(<BillingView />);
      await userEvent.click(await screen.findByTestId('register-payment-button'));

      await userEvent.click(screen.getByTestId('submit-payment-button'));
      expect(await screen.findByTestId('payment-outcome-accepted')).toHaveTextContent('HTTP 201');
      await userEvent.click(screen.getByTestId('submit-payment-button'));
      expect(await screen.findByTestId('payment-outcome-duplicate')).toHaveTextContent('HTTP 200');
      await userEvent.click(screen.getByTestId('submit-payment-button'));
      expect((await screen.findByTestId('payment-error')).textContent).toBe(fixtureDetail('payment-amount-mismatch-422'));
      expect(screen.queryByTestId('payment-outcome-duplicate')).not.toBeInTheDocument();
      expect(gateway.requests.map((r) => r.headers.authorization)).toEqual(['Bearer test-token', 'Bearer test-token', 'Bearer test-token']);
    });
  });
});
