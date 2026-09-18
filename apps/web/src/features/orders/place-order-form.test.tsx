import { QueryClientProvider } from '@tanstack/react-query';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { NextRequest } from 'next/server';
import { hydrateRoot } from 'react-dom/client';
import { renderToString } from 'react-dom/server';
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import { POST as placeOrderRoute } from '@/app/api/orders/route';
import type { PlaceOrderRequest } from '@/lib/api-types';
import { setApiFetch } from '@/lib/api-client';
import { FakeGateway, sendRaw } from '@/test/fake-gateway';
import { fixtureDetail, fixtureResponse, gatewayFixture } from '@/test/gateway-fixtures';
import { deferred, json, renderWithQuery, routeApi, testQueryClient, type ApiCall, type RouteHandler } from '@/test/render';
import { PlaceOrderForm } from './place-order-form';

// Sealing a session cookie needs Node's own Uint8Array realm, which jsdom
// replaces; the session itself is proven in src/app/api/route-handlers.test.ts
// (node environment). Here only the route → Gateway → relay → page chain is under test.
vi.mock('@/server/session', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/server/session')>()),
  readSession: async () => ({ accessToken: 'test-token', expiresAt: Date.now() + 60_000, username: 'operator', roles: [] }),
}));

const retailers = [
  { code: 'AldiEs', name: 'Aldi España', country: 'ES', gln: '5400000000058', currency: 'EUR', enabled: true },
  { code: 'AldiGb', name: 'Aldi UK', country: 'GB', gln: '5400000000072', currency: 'GBP', enabled: true },
  { code: 'AeonJp', name: 'Aeon Japan with a deliberately very long trading name', country: 'JP', gln: '5400000000999', currency: 'JPY', enabled: true },
];
const companies = [{ code: 'IBERFOODS', name: 'Iberian Foods Distribution SA', country: 'ES', gln: '5400000000218', currency: 'EUR', enabled: true }];
const products = [
  { code: 'PRD-0001', name: 'Ration Pack Bundle', price: 24999, currency: 'EUR', enabled: true },
  { code: 'PRD-0002', name: 'Pasta 500g Case (24u)', price: 1850, currency: 'EUR', enabled: true },
];

const accepted = { orderId: '4b8a1f7e-0c1d-4a2b-9e3f-5d6c7b8a9f01', orderReference: 'ORD-000077', status: 'placed', currency: 'EUR', totalAmount: 1999, orderDate: '2026-09-16T10:00:00.000Z' };

function catalogRoutes(extra: Record<string, RouteHandler> = {}): ApiCall[] {
  return routeApi({
    'GET /api/catalog/retailers': () => json({ items: retailers }),
    'GET /api/catalog/companies': () => json({ items: companies }),
    'GET /api/catalog/products': () => json({ items: products }),
    ...extra,
  });
}

async function chooseHeader(retailer = 'AldiEs'): Promise<void> {
  await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Retailer' }), retailer);
  await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Company' }), 'IBERFOODS');
  await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Product' }), 'PRD-0001');
}

const submit = () => userEvent.click(screen.getByRole('button', { name: 'Place order' }));
const posted = (calls: ApiCall[]) => calls.filter((c) => c.method === 'POST').map((c) => c.body as PlaceOrderRequest);

describe('PlaceOrderForm', () => {
  it('Place order is operable on the first render — never disabled waiting for hydration or for the catalogue', () => {
    catalogRoutes();
    renderWithQuery(<PlaceOrderForm />);
    const button = screen.getByRole('button', { name: 'Place order' });
    expect(button).toBeEnabled();
    expect(button).toHaveAttribute('type', 'submit');
  });

  it('offers retailer, company and product from the catalogue, and every control is reachable by its label', async () => {
    catalogRoutes();
    renderWithQuery(<PlaceOrderForm />);
    const retailer = await screen.findByRole('combobox', { name: 'Retailer' });
    expect(within(retailer).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select a retailer', 'Aldi España (AldiEs)', 'Aldi UK (AldiGb)', 'Aeon Japan with a deliberately very long trading name (AeonJp)']);
    expect(await screen.findByRole('combobox', { name: 'Company' })).toBeInTheDocument();
    const product = await screen.findByRole('combobox', { name: 'Product' });
    expect(within(product).getByRole('option', { name: 'Ration Pack Bundle (PRD-0001) — €249.99' })).toBeInTheDocument();
    for (const label of ['Currency', 'Quantity', 'Unit price override', 'Line discount', 'Notes']) {
      expect(screen.getByLabelText(label)).toBeInTheDocument();
    }
  });

  it('a long option cannot widen its select past its grid cell (the overlap #7 found in a real browser)', async () => {
    catalogRoutes();
    renderWithQuery(<PlaceOrderForm />);
    for (const name of ['Retailer', 'Company', 'Product', 'Currency']) {
      const select = await screen.findByRole('combobox', { name });
      expect(select.parentElement).toHaveClass('w-full', 'min-w-0');
      expect(select).toHaveClass('w-full', 'min-w-0', 'truncate');
    }
  });

  it('the currency follows the selected retailer (a GBP retailer makes the order GBP)', async () => {
    catalogRoutes();
    renderWithQuery(<PlaceOrderForm />);
    const currency = await screen.findByRole('combobox', { name: 'Currency' });
    expect(currency).toHaveValue('EUR');
    await userEvent.selectOptions(await screen.findByRole('combobox', { name: 'Retailer' }), 'AldiGb');
    expect(currency).toHaveValue('GBP');
  });

  it('"19.99" typed as a unit-price override reaches the wire as exactly 1999, "5.50" discount as 550, with an Idempotency-Key', async () => {
    const calls = catalogRoutes({ 'POST /api/orders': () => json(accepted, 201) });
    renderWithQuery(<PlaceOrderForm />);
    await chooseHeader();
    await userEvent.clear(screen.getByLabelText('Quantity'));
    await userEvent.type(screen.getByLabelText('Quantity'), '3');
    await userEvent.type(screen.getByLabelText('Unit price override'), '19.99');
    await userEvent.type(screen.getByLabelText('Line discount'), '5.50');
    await userEvent.type(screen.getByLabelText('Notes'), '  urgent  ');
    await submit();

    await waitFor(() => expect(posted(calls)).toHaveLength(1));
    expect(posted(calls)[0]).toEqual({ retailerCode: 'AldiEs', companyCode: 'IBERFOODS', currency: 'EUR', notes: 'urgent', lines: [{ productCode: 'PRD-0001', quantity: 3, unitPrice: 1999, lineDiscount: 550 }] });
    const key = calls.find((c) => c.method === 'POST')?.headers.get('idempotency-key');
    expect(key).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/);

    const success = await screen.findByTestId('place-order-success');
    expect(success).toHaveTextContent('Order ORD-000077 accepted.');
    expect(within(success).getByRole('link')).toHaveAttribute('href', `/orders/${accepted.orderId}`);
  });

  it('the running total is exact integer arithmetic from the typed decimals', async () => {
    catalogRoutes();
    renderWithQuery(<PlaceOrderForm />);
    await chooseHeader();
    expect(screen.getByTestId('running-total')).toHaveTextContent('€249.99');
    await userEvent.clear(screen.getByLabelText('Quantity'));
    await userEvent.type(screen.getByLabelText('Quantity'), '3');
    await userEvent.type(screen.getByLabelText('Unit price override'), '0.29');
    await userEvent.type(screen.getByLabelText('Line discount'), '0.01');
    expect(screen.getByTestId('running-total')).toHaveTextContent('€0.86');
  });

  it('with no override, the line omits unitPrice so the catalogue price is snapshotted server-side', async () => {
    const calls = catalogRoutes({ 'POST /api/orders': () => json(accepted, 201) });
    renderWithQuery(<PlaceOrderForm />);
    await chooseHeader();
    await submit();
    await waitFor(() => expect(posted(calls)).toHaveLength(1));
    expect(posted(calls)[0]?.lines).toEqual([{ productCode: 'PRD-0001', quantity: 1 }]);
    expect(posted(calls)[0]?.notes).toBeUndefined();
  });

  it('a JPY order takes whole yen: "1999" is 1999, and "19.99" is refused in place and nothing is sent', async () => {
    const calls = catalogRoutes({ 'POST /api/orders': () => json(accepted, 201) });
    renderWithQuery(<PlaceOrderForm />);
    await chooseHeader('AeonJp');
    expect(screen.getByRole('combobox', { name: 'Currency' })).toHaveValue('JPY');
    await userEvent.type(screen.getByLabelText('Unit price override'), '19.99');
    await submit();
    expect(await screen.findByText('Enter a whole amount (JPY).')).toBeInTheDocument();
    expect(posted(calls)).toHaveLength(0);

    await userEvent.clear(screen.getByLabelText('Unit price override'));
    await userEvent.type(screen.getByLabelText('Unit price override'), '1999');
    await submit();
    await waitFor(() => expect(posted(calls)).toHaveLength(1));
    expect(posted(calls)[0]).toMatchObject({ currency: 'JPY', lines: [{ productCode: 'PRD-0001', quantity: 1, unitPrice: 1999 }] });
  });

  it('"1.005" in a 2-decimal currency is refused, never rounded', async () => {
    const calls = catalogRoutes({ 'POST /api/orders': () => json(accepted, 201) });
    renderWithQuery(<PlaceOrderForm />);
    await chooseHeader();
    await userEvent.type(screen.getByLabelText('Unit price override'), '1.005');
    await submit();
    expect(await screen.findByText('Enter an amount with at most 2 decimals (EUR).')).toBeInTheDocument();
    expect(screen.getByLabelText('Unit price override')).toHaveAttribute('aria-invalid', 'true');
    expect(posted(calls)).toHaveLength(0);
  });

  it('an incomplete order says what is missing and sends nothing', async () => {
    const calls = catalogRoutes({ 'POST /api/orders': () => json(accepted, 201) });
    renderWithQuery(<PlaceOrderForm />);
    await screen.findByRole('combobox', { name: 'Retailer' });
    await submit();
    expect(await screen.findByTestId('place-order-incomplete')).toHaveTextContent('Choose a retailer, a company and at least one product.');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Retailer' }), 'AldiEs');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Company' }), 'IBERFOODS');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Product' }), 'PRD-0001');
    await userEvent.clear(screen.getByLabelText('Quantity'));
    await userEvent.type(screen.getByLabelText('Quantity'), '0');
    await submit();
    expect(await screen.findByText('Enter a whole number of at least 1.')).toBeInTheDocument();
    expect(posted(calls)).toHaveLength(0);
  });

  it('"Fill demo order" pre-fills 249.99 as a decimal (not 24999) for the .99 compensation demo', async () => {
    const calls = catalogRoutes({ 'POST /api/orders': () => json(accepted, 201) });
    renderWithQuery(<PlaceOrderForm />);
    await screen.findByRole('combobox', { name: 'Retailer' });
    await userEvent.click(screen.getByRole('button', { name: /Fill demo order/ }));
    expect(screen.getByLabelText('Unit price override')).toHaveValue('249.99');
    await submit();
    await waitFor(() => expect(posted(calls)).toHaveLength(1));
    expect(posted(calls)[0]).toMatchObject({ retailerCode: 'AldiEs', companyCode: 'IBERFOODS', currency: 'EUR', lines: [{ productCode: 'PRD-0001', quantity: 1, unitPrice: 24999 }] });
  });

  it('shows "Placing…" while the request is UNRESOLVED, then re-enables', async () => {
    const response = deferred();
    catalogRoutes({ 'POST /api/orders': () => response.promise });
    renderWithQuery(<PlaceOrderForm />);
    await chooseHeader();
    await submit();
    expect(await screen.findByRole('button', { name: 'Placing…' })).toBeDisabled();
    response.release(json(accepted, 201));
    expect(await screen.findByRole('button', { name: 'Place order' })).toBeEnabled();
  });

  it('lines can be added and removed; the last line cannot be removed', async () => {
    catalogRoutes();
    renderWithQuery(<PlaceOrderForm />);
    await screen.findByRole('combobox', { name: 'Retailer' });
    expect(screen.getByRole('button', { name: 'Remove line 1' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Add line' }));
    expect(screen.getAllByTestId('order-line')).toHaveLength(2);
    await userEvent.click(screen.getByRole('button', { name: 'Remove line 2' }));
    expect(screen.getAllByTestId('order-line')).toHaveLength(1);
  });

  it('the product select id still agrees with its own label htmlFor after HYDRATING a server render, even after several PRIOR renders already ran in the SAME module instance (id 106)', async () => {
    catalogRoutes();
    const queryClient = testQueryClient();
    const ui = () => (
      <QueryClientProvider client={queryClient}>
        <PlaceOrderForm />
      </QueryClientProvider>
    );

    // id 106's regression: a long-lived `next start` process serves many
    // requests without restarting, and the OLD id-generation counter was
    // module-scope, free-running across every one of them. Simulate five
    // PRIOR requests' worth of server rendering in this SAME module instance
    // BEFORE the render under test — the old counter would already be well
    // past where a freshly-loaded browser bundle's own copy starts.
    for (let i = 0; i < 5; i++) renderToString(ui());

    // The render under test: one more server pass, as if for THIS request...
    const serverHtml = renderToString(ui());
    const container = document.createElement('div');
    container.innerHTML = serverHtml;
    document.body.appendChild(container);

    // ...hydrated by what stands in for the browser's own module instance
    // (a fresh element tree reconciled against the server markup above).
    let root: ReturnType<typeof hydrateRoot>;
    try {
      await act(async () => {
        root = hydrateRoot(container, ui());
      });

      // getByRole with `name` resolves the accessible name through the real
      // for/id relationship (dom-accessibility-api) — it does not read
      // line.key back to itself, so a genuinely desynced pair fails this,
      // rather than trivially agreeing by construction.
      const productSelect = await within(container).findByRole('combobox', { name: 'Product' });
      expect(productSelect).toBeInTheDocument();

      const label = within(container).getByText('Product');
      expect(label.tagName).toBe('LABEL');
      expect(label).toHaveAttribute('for', productSelect.id);
    } finally {
      root!.unmount();
      container.remove();
    }
  });

  it('a Gateway 503 is shown in its own words (real captured body)', async () => {
    catalogRoutes({ 'POST /api/orders': () => fixtureResponse('place-order-upstream-unavailable-503') });
    renderWithQuery(<PlaceOrderForm />);
    await chooseHeader();
    await submit();
    expect((await screen.findByTestId('place-order-error')).textContent).toBe(fixtureDetail('place-order-upstream-unavailable-503'));
    expect(screen.queryByTestId('place-order-shortages')).not.toBeInTheDocument();
  });

  it('when the catalogue cannot be loaded, the page shows the Gateway\'s own words (real captured /catalog/* 503s) and the codes can still be typed by hand', async () => {
    const calls = routeApi({
      'GET /api/catalog/retailers': () => fixtureResponse('catalog-retailers-upstream-unavailable-503'),
      'GET /api/catalog/companies': () => fixtureResponse('catalog-companies-upstream-unavailable-503'),
      'GET /api/catalog/products': () => fixtureResponse('catalog-products-upstream-unavailable-503'),
      'POST /api/orders': () => json(accepted, 201),
    });
    renderWithQuery(<PlaceOrderForm />);
    const notice = await screen.findByTestId('catalog-unavailable');
    // The three captured answers carry one and the same detail, so it is shown once.
    await waitFor(() => expect(within(notice).getAllByTestId('catalog-error').map((e) => e.textContent), 'the catalogue notice must show the Gateway\'s own detail').toEqual([fixtureDetail('catalog-products-upstream-unavailable-503')]));
    expect(fixtureDetail('catalog-retailers-upstream-unavailable-503')).toBe(fixtureDetail('catalog-products-upstream-unavailable-503'));
    expect(fixtureDetail('catalog-companies-upstream-unavailable-503')).toBe(fixtureDetail('catalog-products-upstream-unavailable-503'));
    expect(within(notice).getByTestId('catalog-manual-entry')).toHaveTextContent('Enter codes by hand');
    for (const label of ['Currency', 'Quantity', 'Unit price override', 'Line discount', 'Notes']) {
      expect(screen.getByLabelText(label)).toBeInTheDocument();
    }
    await userEvent.type(screen.getByRole('textbox', { name: 'Retailer' }), 'CarrefourEs');
    await userEvent.type(screen.getByRole('textbox', { name: 'Company' }), 'IBERFOODS');
    await userEvent.type(screen.getByRole('textbox', { name: 'Product' }), 'PRD-0001');
    fireEvent.submit(screen.getByTestId('place-order-form'));
    await waitFor(() => expect(posted(calls)).toHaveLength(1));
    expect(posted(calls)[0]).toMatchObject({ retailerCode: 'CarrefourEs', companyCode: 'IBERFOODS', lines: [{ productCode: 'PRD-0001', quantity: 1 }] });
  });

  it('two DIFFERENT catalogue failures are both shown: the real 503\'s own words, and the fallback only for a body that carries no problem', async () => {
    routeApi({
      'GET /api/catalog/retailers': () => fixtureResponse('catalog-retailers-upstream-unavailable-503'),
      'GET /api/catalog/companies': () => json({ items: companies }),
      // A proxy's HTML error page: no problem document, so the fallback is the only honest text.
      'GET /api/catalog/products': () => new Response('<html><body>502 Bad Gateway</body></html>', { status: 502, headers: { 'Content-Type': 'text/html' } }),
    });
    renderWithQuery(<PlaceOrderForm />);
    const notice = await screen.findByTestId('catalog-unavailable');
    await waitFor(() => expect(within(notice).getAllByTestId('catalog-error').map((e) => e.textContent)).toEqual([fixtureDetail('catalog-retailers-upstream-unavailable-503'), 'The catalogue could not be loaded.']));
    expect(screen.getByRole('combobox', { name: 'Company' })).toBeInTheDocument();
  });

  describe('a 409 STOCK_UNAVAILABLE through the real route handler, from a Gateway answering with its REAL captured body', () => {
    const gateway = new FakeGateway();

    beforeAll(async () => {
      gateway.on('POST', '/orders', (_req, res) => {
        const fixture = gatewayFixture('place-order-stock-unavailable-409');
        sendRaw(res, fixture.status, fixture.body, fixture.headers);
      });
      process.env.GATEWAY_BASE_URL = await gateway.start();
      process.env.WEB_SESSION_PASSWORD = 'test-session-password-at-least-32-characters-long';
    });

    afterAll(async () => {
      await gateway.stop();
    });

    it('renders that problem\'s own detail and its per-product shortages', async () => {
      catalogRoutes();
      setApiFetch(async (input, init) => {
        const url = new URL(input, 'http://web.test');
        if (url.pathname === '/api/orders') {
          return placeOrderRoute(new NextRequest(url, { method: 'POST', body: init?.body as string, headers: new Headers(init?.headers) }));
        }
        return json({ items: url.pathname.endsWith('retailers') ? retailers : url.pathname.endsWith('companies') ? companies : products });
      });
      renderWithQuery(<PlaceOrderForm />);
      await chooseHeader();
      await submit();

      const error = await screen.findByTestId('place-order-error');
      expect(error.textContent).toBe(fixtureDetail('place-order-stock-unavailable-409'));
      expect(error.textContent).toBe('Stock check reports 1 short line(s): PRD-0001 (requested 999999, available 500)');
      expect(screen.getByTestId('place-order-shortages')).toHaveTextContent('PRD-0001: requested 999999, only 500 available');
      expect(gateway.requests).toHaveLength(1);
    });
  });
});
