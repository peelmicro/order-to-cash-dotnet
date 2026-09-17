import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { Product, ReplenishStockRequest, StockItem, StockPage } from '@/lib/api-types';
import { fixtureDetail, fixtureResponse } from '@/test/gateway-fixtures';
import { deferred, json, renderWithQuery, routeApi } from '@/test/render';
import { StockView } from './stock-view';

function item(overrides: Partial<StockItem> = {}): StockItem {
  return { companyCode: 'IBERFOODS', productCode: 'PRD-0001', productName: 'Ration Pack Bundle', units: 40, reservedUnits: 10, availableUnits: 30, lowStockThreshold: 20, ...overrides };
}

/**
 * `description` always differs from `name` here — never left equal or
 * absent — so a bug substituting the sibling field (CLAUDE.md defeat-list row
 * 3) shows the WRONG text rather than coincidentally the right one.
 */
function product(overrides: Partial<Product> = {}): Product {
  return { code: 'PRD-0001', name: 'Ration Pack Bundle (catalog)', description: 'not the display name — a decoy sibling field', price: 1999, currency: 'EUR', enabled: true, ...overrides };
}

const page = (items: StockItem[], total = items.length): StockPage => ({ items, page: { page: 1, pageSize: 20, total } });

/** No products catalog is needed by most of these tests, since `item()` already carries its own `productName`. */
const NO_CATALOG = { 'GET /api/catalog/products': () => json({ items: [] }) };

describe('StockView — the stock table', () => {
  it('renders on-hand, reserved and available (units − reserved, F1) per line, flagging a low line', async () => {
    routeApi({ 'GET /api/stock': () => json(page([item(), item({ productCode: 'PRD-0002', productName: undefined, units: 25, reservedUnits: 10, availableUnits: 15 })])), ...NO_CATALOG });
    renderWithQuery(<StockView />);
    const [first, second] = await screen.findAllByTestId('stock-row');
    expect(within(first!).getByTestId('stock-units')).toHaveTextContent('40');
    expect(within(first!).getByTestId('stock-reserved')).toHaveTextContent('10');
    expect(within(first!).getByTestId('stock-available')).toHaveTextContent('30');
    expect(within(first!).getByText('Ration Pack Bundle')).toBeInTheDocument();
    expect(within(second!).getByTestId('stock-available').firstElementChild).toHaveAttribute('data-variant', 'destructive');
    expect(within(first!).getByTestId('stock-available').firstElementChild).toHaveAttribute('data-variant', 'secondary');
    expect(within(second!).getAllByText(/PRD-0002/).length).toBeGreaterThan(0);
  });

  it('shows the loading state while the request is UNRESOLVED, then the empty state', async () => {
    const response = deferred();
    routeApi({ 'GET /api/stock': () => response.promise, ...NO_CATALOG });
    renderWithQuery(<StockView />);
    expect(await screen.findByTestId('stock-loading')).toHaveTextContent('Loading stock…');
    expect(screen.queryByTestId('stock-empty')).not.toBeInTheDocument();
    expect(screen.queryByTestId('stock-error')).not.toBeInTheDocument();
    response.release(json(page([])));
    expect(await screen.findByTestId('stock-empty')).toHaveTextContent('No stock lines match these filters.');
    expect(screen.queryByTestId('stock-loading')).not.toBeInTheDocument();
  });

  it('Fulfillment being down shows the Gateway\'s own 503 words (real captured body), not an empty table', async () => {
    routeApi({ 'GET /api/stock': () => fixtureResponse('stock-list-upstream-unavailable-503'), ...NO_CATALOG });
    renderWithQuery(<StockView />);
    const error = await screen.findByTestId('stock-error');
    expect(error.textContent).toBe(`Could not load stock: ${fixtureDetail('stock-list-upstream-unavailable-503')}`);
    expect(error.textContent).toContain('fulfillment.stock.list');
    expect(screen.queryByTestId('stock-empty')).not.toBeInTheDocument();
    expect(screen.queryByTestId('stock-loading')).not.toBeInTheDocument();
  });

  it('the low-stock toggle and the text filters reach the query', async () => {
    const calls = routeApi({ 'GET /api/stock': () => json(page([item()])), ...NO_CATALOG });
    renderWithQuery(<StockView />);
    await screen.findAllByTestId('stock-row');
    expect(calls[0]?.query.get('belowThreshold')).toBeNull();
    await userEvent.click(screen.getByTestId('below-threshold-toggle'));
    await waitFor(() => expect(calls.at(-1)?.query.get('belowThreshold')).toBe('true'));
    expect(screen.getByTestId('below-threshold-toggle')).toHaveAttribute('aria-pressed', 'true');
    await userEvent.type(screen.getByLabelText('Company'), 'IBERFOODS');
    await userEvent.type(screen.getByLabelText('Product'), 'PRD-0001');
    await waitFor(() => expect([calls.at(-1)?.query.get('companyCode'), calls.at(-1)?.query.get('productCode')]).toEqual(['IBERFOODS', 'PRD-0001']));
  });

  it('exposes real column headers', async () => {
    routeApi({ 'GET /api/stock': () => json(page([item()])), ...NO_CATALOG });
    renderWithQuery(<StockView />);
    await screen.findAllByTestId('stock-row');
    const headers = screen.getAllByRole('columnheader');
    expect(headers.map((h) => h.textContent)).toEqual(['Company', 'Product', 'On hand', 'Reserved', 'Available', 'Threshold', 'Actions']);
    headers.forEach((header) => expect(header).toHaveAttribute('scope', 'col'));
  });
});

describe('StockView — replenish is a delta', () => {
  it('sends the typed amount as units to ADD (never the resulting level), and says so on the form', async () => {
    const calls = routeApi({
      'GET /api/stock': () => json(page([item()])),
      'POST /api/stock/replenish': () => json({ items: [item({ units: 150, availableUnits: 140 })] }),
      ...NO_CATALOG,
    });
    renderWithQuery(<StockView />);
    await userEvent.click(await screen.findByTestId('replenish-button'));
    expect(screen.getByTestId('replenish-form')).toHaveTextContent('This adds to on-hand stock — a delta, not a target level.');
    await userEvent.type(screen.getByTestId('replenish-units-input'), '110');
    await userEvent.click(screen.getByTestId('submit-replenish-button'));

    expect(await screen.findByTestId('replenish-outcome')).toHaveTextContent('Added 110 units to PRD-0001 — on hand is now 150.');
    const posts = calls.filter((c) => c.method === 'POST').map((c) => c.body as ReplenishStockRequest);
    expect(posts).toEqual([{ companyCode: 'IBERFOODS', lines: [{ productCode: 'PRD-0001', units: 110 }] }]);
  });

  it('an answered replenish that omits this line says so — never an ellipsis that reads as still working', async () => {
    routeApi({ 'GET /api/stock': () => json(page([item()])), 'POST /api/stock/replenish': () => json({ items: [item({ productCode: 'PRD-0009' })] }), ...NO_CATALOG });
    renderWithQuery(<StockView />);
    await userEvent.click(await screen.findByTestId('replenish-button'));
    await userEvent.type(screen.getByTestId('replenish-units-input'), '5');
    await userEvent.click(screen.getByTestId('submit-replenish-button'));
    const outcome = await screen.findByTestId('replenish-outcome');
    expect(outcome.textContent, 'the answered replenish must not read as unfinished').toBe('Added 5 units to PRD-0001 — the answer did not include this line’s new on-hand level.');
  });

  it('refuses a non-whole or non-positive amount in place, sending nothing', async () => {
    const calls = routeApi({ 'GET /api/stock': () => json(page([item()])), 'POST /api/stock/replenish': () => json({ items: [] }), ...NO_CATALOG });
    renderWithQuery(<StockView />);
    await userEvent.click(await screen.findByTestId('replenish-button'));
    for (const bad of ['0', '1.5', '-3', 'ten']) {
      await userEvent.clear(screen.getByTestId('replenish-units-input'));
      await userEvent.type(screen.getByTestId('replenish-units-input'), bad);
      await userEvent.click(screen.getByTestId('submit-replenish-button'));
      expect(screen.getByText('Enter a whole number of units to add (at least 1).')).toBeInTheDocument();
    }
    expect(calls.filter((c) => c.method === 'POST')).toHaveLength(0);
  });

  it('shows "Adding…" while the replenish is UNRESOLVED', async () => {
    const response = deferred();
    routeApi({ 'GET /api/stock': () => json(page([item()])), 'POST /api/stock/replenish': () => response.promise, ...NO_CATALOG });
    renderWithQuery(<StockView />);
    await userEvent.click(await screen.findByTestId('replenish-button'));
    await userEvent.type(screen.getByTestId('replenish-units-input'), '5');
    await userEvent.click(screen.getByTestId('submit-replenish-button'));
    expect(await screen.findByRole('button', { name: 'Adding…' })).toBeDisabled();
    response.release(json({ items: [item({ units: 45 })] }));
    expect(await screen.findByTestId('replenish-outcome')).toHaveTextContent('on hand is now 45');
  });

  it('a rejected replenish shows the Gateway\'s own words (real 404), and the error does not follow the form to another line', async () => {
    routeApi({
      'GET /api/stock': () => json(page([item(), item({ productCode: 'PRD-0002' })])),
      'POST /api/stock/replenish': () => fixtureResponse('replenish-unknown-product-404'),
      ...NO_CATALOG,
    });
    renderWithQuery(<StockView />);
    const buttons = await screen.findAllByTestId('replenish-button');
    await userEvent.click(buttons[0]!);
    await userEvent.type(screen.getByTestId('replenish-units-input'), '10');
    await userEvent.click(screen.getByTestId('submit-replenish-button'));
    expect((await screen.findByTestId('replenish-error')).textContent).toBe(fixtureDetail('replenish-unknown-product-404'));

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await userEvent.click((await screen.findAllByTestId('replenish-button'))[1]!);
    expect(screen.getByTestId('replenish-form')).toBeInTheDocument();
    expect(screen.queryByTestId('replenish-error')).not.toBeInTheDocument();
  });
});

// ── id 101: the product name, from the catalog (GET /api/catalog/products) ──
describe('StockView — product name (id 101)', () => {
  it('the name comes from the catalog when the stock line itself carries none', async () => {
    routeApi({
      'GET /api/stock': () => json(page([item({ productName: undefined })])),
      'GET /api/catalog/products': () => json({ items: [product({ code: 'PRD-0001', name: 'Ration Pack Bundle (catalog)' })] }),
    });
    renderWithQuery(<StockView />);
    const row = await screen.findByTestId('stock-row');
    expect(within(row).getByTestId('stock-product')).toHaveTextContent('Ration Pack Bundle (catalog) (PRD-0001)');
  });

  it('the code appears only once, with no known name — a test that fails if the duplicate returns', async () => {
    routeApi({
      'GET /api/stock': () => json(page([item({ productName: undefined, productCode: 'PRD-0007' })])),
      ...NO_CATALOG,
    });
    renderWithQuery(<StockView />);
    const row = await screen.findByTestId('stock-row');
    const cell = within(row).getByTestId('stock-product');
    expect(cell).toHaveTextContent('PRD-0007');
    expect(cell.textContent?.match(/PRD-0007/g)?.length, `the product cell showed the code more than once: "${cell.textContent}"`).toBe(1);
  });

  it("productName (a backend fact) takes precedence over the catalog's own name", async () => {
    routeApi({
      'GET /api/stock': () => json(page([item({ productName: 'On the stock line' })])),
      'GET /api/catalog/products': () => json({ items: [product({ code: 'PRD-0001', name: 'From the catalog — should lose' })] }),
    });
    renderWithQuery(<StockView />);
    const row = await screen.findByTestId('stock-row');
    expect(within(row).getByTestId('stock-product')).toHaveTextContent('On the stock line (PRD-0001)');
    expect(within(row).queryByText(/From the catalog/)).not.toBeInTheDocument();
  });

  it('a catalog failure does not hide the stock table: rows still render, falling back to the code alone, and the catalog error is shown separately', async () => {
    routeApi({
      'GET /api/stock': () => json(page([item({ productName: undefined })])),
      'GET /api/catalog/products': () => fixtureResponse('catalog-products-upstream-unavailable-503'),
    });
    renderWithQuery(<StockView />);
    const row = await screen.findByTestId('stock-row');
    expect(within(row).getByTestId('stock-product')).toHaveTextContent('PRD-0001');
    expect(within(row).getByTestId('stock-units')).toHaveTextContent('40');
    const catalogError = await screen.findByTestId('stock-products-error');
    expect(catalogError.textContent).toBe(`Product names unavailable: ${fixtureDetail('catalog-products-upstream-unavailable-503')}`);
    expect(screen.queryByTestId('stock-error')).not.toBeInTheDocument();
    expect(screen.queryByTestId('stock-empty')).not.toBeInTheDocument();
  });
});
