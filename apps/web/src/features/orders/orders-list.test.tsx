import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { OrderSummary, OrderSummaryPage } from '@/lib/api-types';
import { fixtureDetail, fixtureResponse } from '@/test/gateway-fixtures';
import { deferred, json, renderWithQuery, routeApi } from '@/test/render';
import { OrdersList } from './orders-list';

function order(overrides: Partial<OrderSummary> = {}): OrderSummary {
  return {
    orderId: '9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f',
    orderReference: 'ORD-000042',
    orderDate: '2026-09-16T10:00:00.000Z',
    retailer: { code: 'AldiEs', gln: '5400000000058', name: 'Aldi España' },
    company: { code: 'IBERFOODS', gln: '5400000000218' },
    status: 'confirmed',
    currency: 'EUR',
    totals: { initialAmount: 124250, initialDiscount: 0, totalAmount: 124250 },
    updatedAt: '2026-09-16T10:00:00.000Z',
    ...overrides,
  };
}

const page = (items: OrderSummary[], total = items.length): OrderSummaryPage => ({ items, page: { page: 1, pageSize: 20, total } });
const retailers = () => json({ items: [{ code: 'AldiGb', name: 'Aldi UK', country: 'GB', gln: '5400000000072', currency: 'GBP', enabled: true }] });

describe('OrdersList', () => {
  it('renders order rows with a link to each order and its total formatted from minor units', async () => {
    routeApi({ 'GET /api/orders': () => json(page([order(), order({ orderId: 'b', orderReference: 'ORD-000043', currency: 'JPY', totals: { initialAmount: 1999, initialDiscount: 0, totalAmount: 1999 } })])), 'GET /api/catalog/retailers': retailers });
    renderWithQuery(<OrdersList />);

    const rows = await screen.findAllByTestId('order-row');
    expect(rows).toHaveLength(2);
    expect(within(rows[0]!).getByRole('link', { name: 'ORD-000042' })).toHaveAttribute('href', '/orders/9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f');
    expect(within(rows[0]!).getByTestId('order-total')).toHaveTextContent('€1,242.50');
    expect(within(rows[1]!).getByTestId('order-total')).toHaveTextContent('JP¥1,999');
    expect(within(rows[0]!).getByText('Aldi España')).toBeInTheDocument();
    expect(within(rows[0]!).getByText('IBERFOODS')).toBeInTheDocument();
    expect(screen.getByTestId('orders-pager')).toHaveTextContent('Page 1 of 1 · 2 orders');
  });

  it('shows the loading state while the request is UNRESOLVED, then the empty state — three distinct renderings', async () => {
    const response = deferred();
    routeApi({ 'GET /api/orders': () => response.promise, 'GET /api/catalog/retailers': retailers });
    renderWithQuery(<OrdersList />);

    expect(await screen.findByTestId('orders-loading')).toHaveTextContent('Loading orders…');
    expect(screen.getByTestId('orders-pager').textContent, 'while loading, the pager must not claim a count').toMatch(/^Page 1PreviousNext$/);
    expect(screen.queryByTestId('orders-empty')).not.toBeInTheDocument();
    expect(screen.queryByTestId('orders-error')).not.toBeInTheDocument();

    response.release(json(page([])));
    expect(await screen.findByTestId('orders-empty')).toHaveTextContent('No orders match these filters.');
    expect(screen.getByTestId('orders-pager')).toHaveTextContent('Page 1 of 1 · 0 orders');
    expect(screen.queryByTestId('orders-loading')).not.toBeInTheDocument();
    expect(screen.queryByTestId('orders-error')).not.toBeInTheDocument();
  });

  it('a failed list shows the Gateway\'s own detail (real captured GET /orders 400), never an empty table or a spinner', async () => {
    routeApi({ 'GET /api/orders': () => fixtureResponse('orders-list-bad-page-400'), 'GET /api/catalog/retailers': retailers });
    renderWithQuery(<OrdersList />);
    const error = await screen.findByTestId('orders-error');
    expect(error.textContent).toBe(`Could not load orders: ${fixtureDetail('orders-list-bad-page-400')}`);
    expect(screen.queryByTestId('orders-empty')).not.toBeInTheDocument();
    expect(screen.queryByTestId('orders-loading')).not.toBeInTheDocument();
    expect(screen.queryByTestId('orders-pager')).not.toBeInTheDocument();
  });

  it('the status and retailer filters reach the query, and paging asks for the next page', async () => {
    const calls = routeApi({ 'GET /api/orders': () => json(page([order()], 45)), 'GET /api/catalog/retailers': retailers });
    renderWithQuery(<OrdersList />);
    await screen.findAllByTestId('order-row');

    await userEvent.selectOptions(screen.getByLabelText('Status'), 'cancelled');
    await waitFor(() => expect(calls.at(-1)?.query.getAll('status')).toEqual(['cancelled']));
    await userEvent.selectOptions(await screen.findByLabelText('Retailer'), 'AldiGb');
    await waitFor(() => expect(calls.at(-1)?.query.get('retailerCode')).toBe('AldiGb'));

    await userEvent.click(screen.getByTestId('orders-pager-next'));
    await waitFor(() => expect(calls.at(-1)?.query.get('page')).toBe('2'));
    expect(calls.at(-1)?.query.get('pageSize')).toBe('20');
    expect(calls.at(-1)?.query.getAll('status')).toEqual(['cancelled']);
  });

  it('exposes labelled filters and real column headers', async () => {
    routeApi({ 'GET /api/orders': () => json(page([order()])), 'GET /api/catalog/retailers': retailers });
    renderWithQuery(<OrdersList />);
    await screen.findAllByTestId('order-row');
    const headers = screen.getAllByRole('columnheader');
    expect(headers.map((h) => h.textContent)).toEqual(['Reference', 'Date', 'Retailer', 'Company', 'Status', 'Total']);
    headers.forEach((header) => expect(header).toHaveAttribute('scope', 'col'));
    expect(screen.getByRole('link', { name: 'Place order' })).toHaveAttribute('href', '/orders/place');
  });
});
