import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { OrderDetail, ProjectionPending } from '@/lib/api-types';
import { FakeEventSource, fakeEventSourceFactory } from '@/test/fake-event-source';
import { fixtureDetail, fixtureResponse } from '@/test/gateway-fixtures';
import { deferred, json, renderWithQuery, routeApi, type ApiCall } from '@/test/render';
import { makeQueryClient, setSignedOutNavigation } from '@/app/providers';
import { notSignedIn } from '@/server/gateway';
import { OrderDetailView } from './order-detail-view';

const ORDER_ID = '9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f';
const DETAIL_PATH = `GET /api/orders/${ORDER_ID}`;

function detail(overrides: Partial<OrderDetail> = {}): OrderDetail {
  return {
    orderId: ORDER_ID,
    orderReference: 'ORD-000042',
    orderDate: '2026-09-16T10:00:00.000Z',
    retailer: { code: 'AldiEs', gln: '5400000000058', name: 'Aldi España' },
    company: { code: 'IBERFOODS', gln: '5400000000218' },
    status: 'placed',
    currency: 'EUR',
    totals: { initialAmount: 24999, initialDiscount: 0, totalAmount: 24999 },
    items: [{ productCode: 'PRD-0001', name: 'Ration Pack Bundle', quantity: 1, unitPrice: 24999, lineDiscount: 0 }],
    references: {},
    events: [{ eventId: 'e0', eventType: 'order.placed.v1', occurredAt: '2026-09-16T10:00:00.000Z', summary: 'Order placed' }],
    headerComplete: true,
    updatedAt: '2026-09-16T10:00:00.000Z',
    ...overrides,
  };
}

const pending: ProjectionPending = { orderId: ORDER_ID, status: 'projection_pending', message: 'The order was accepted and is not projected yet. Subscribe to /orders/stream or retry.', retryAfterMs: 2000 };

function renderDetail(backstopMs?: number) {
  return renderWithQuery(<OrderDetailView orderId={ORDER_ID} streamFactory={fakeEventSourceFactory} backstopMs={backstopMs} />);
}

const detailCalls = (calls: ApiCall[]) => calls.filter((c) => c.path === `/api/orders/${ORDER_ID}`).length;
const entries = () => within(screen.getByTestId('order-timeline')).getAllByTestId('timeline-entry');
const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

beforeEach(() => {
  FakeEventSource.reset();
});

describe('OrderDetailView — R55 honest waiting state', () => {
  it('a 202 renders the NAMED waiting state (not an error, not a 404, not a bare spinner), retries on the Gateway\'s schedule, then shows the order', async () => {
    let calls = 0;
    const api = routeApi({
      [DETAIL_PATH]: () => {
        calls += 1;
        return calls === 1 ? json({ ...pending, retryAfterMs: 5000 }, 202, { 'Retry-After': '0' }) : json(detail());
      },
    });
    renderDetail();

    const waiting = await screen.findByTestId('order-detail-pending');
    expect(waiting).toHaveTextContent('Waiting for this order to appear');
    expect(waiting).toHaveTextContent('The order was accepted and is not projected yet.');
    // Retry-After: 0 wins over the body's 5000 ms, floored at 250 ms.
    expect(screen.getByTestId('order-detail-pending-schedule')).toHaveTextContent('Checking again every 0.3 s, as the Gateway asked');
    expect(screen.getByRole('button', { name: 'Check now' })).toBeEnabled();
    expect(screen.queryByTestId('order-detail-error')).not.toBeInTheDocument();
    expect(screen.queryByTestId('order-detail-loading')).not.toBeInTheDocument();
    // Nothing to patch yet, so no stream is opened while the answer is 202.
    expect(FakeEventSource.instances).toHaveLength(0);

    expect(await screen.findByTestId('order-detail-reference', {}, { timeout: 3000 })).toHaveTextContent('ORD-000042');
    expect(detailCalls(api)).toBe(2);
    expect(screen.queryByTestId('order-detail-pending')).not.toBeInTheDocument();
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
  });

  it('waits the Gateway\'s interval before asking again — no tight loop', async () => {
    const api = routeApi({ [DETAIL_PATH]: () => json({ ...pending, retryAfterMs: 400 }, 202) });
    renderDetail();
    await screen.findByTestId('order-detail-pending');
    await sleep(200);
    expect(detailCalls(api), 'GET /api/orders/{id} calls within 200 ms of a 202 that asked for 400 ms').toBe(1);
    await waitFor(() => expect(detailCalls(api)).toBe(2), { timeout: 2000 });
  });

  it('"Check now" asks again immediately', async () => {
    const api = routeApi({ [DETAIL_PATH]: () => json(pending, 202, { 'Retry-After': '30' }) });
    renderDetail();
    await screen.findByTestId('order-detail-pending');
    await userEvent.click(screen.getByRole('button', { name: 'Check now' }));
    await waitFor(() => expect(detailCalls(api)).toBe(2));
  });

  it('shows the loading state while the first request is UNRESOLVED', async () => {
    const response = deferred();
    routeApi({ [DETAIL_PATH]: () => response.promise });
    renderDetail();
    expect(await screen.findByTestId('order-detail-loading')).toHaveTextContent('Loading order…');
    expect(screen.queryByTestId('order-detail-pending')).not.toBeInTheDocument();
    response.release(json(detail()));
    expect(await screen.findByTestId('order-detail-reference')).toBeInTheDocument();
    expect(screen.queryByTestId('order-detail-loading')).not.toBeInTheDocument();
  });

  it.each(['order-unknown-404', 'order-malformed-id-400'] as const)('a failed load shows the Gateway\'s own words (real %s)', async (fixture) => {
    routeApi({ [DETAIL_PATH]: () => fixtureResponse(fixture) });
    renderDetail();
    const error = await screen.findByTestId('order-detail-error');
    expect(error.textContent).toBe(`Could not load this order: ${fixtureDetail(fixture)}`);
    expect(screen.queryByTestId('order-detail-pending')).not.toBeInTheDocument();
    expect(screen.queryByTestId('order-detail-loading')).not.toBeInTheDocument();
    expect(FakeEventSource.instances).toHaveLength(0);
  });
});

describe('OrderDetailView — the document and its live timeline', () => {
  it('renders header, total, items, references and the timeline from the GET', async () => {
    routeApi({ [DETAIL_PATH]: () => json(detail({ status: 'invoiced', references: { despatchReference: 'DES-000009', invoiceReference: 'INV-000007' } })) });
    renderDetail();
    expect(await screen.findByTestId('order-detail-reference')).toHaveTextContent('ORD-000042');
    expect(screen.getByTestId('order-detail-status')).toHaveTextContent('invoiced');
    expect(screen.getByTestId('order-detail-total')).toHaveTextContent('€249.99');
    expect(screen.getByTestId('order-detail-items')).toHaveTextContent('1 × Ration Pack Bundle at €249.99');
    expect(screen.getByText('DES-000009')).toBeInTheDocument();
    expect(screen.getByText('INV-000007')).toBeInTheDocument();
    expect(entries().map((e) => e.getAttribute('data-event-id'))).toEqual(['e0']);
    expect(screen.getByText('Aldi España · IBERFOODS')).toBeInTheDocument();
  });

  it('opens ONE stream for this order, through this app\'s own proxy, and reports its connection state', async () => {
    routeApi({ [DETAIL_PATH]: () => json(detail()) });
    renderDetail();
    await screen.findByTestId('order-detail-reference');
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    expect(FakeEventSource.latest().url).toBe(`/api/orders/stream?orderId=${ORDER_ID}`);
    expect(screen.getByTestId('stream-status')).toHaveTextContent('Connecting…');
    FakeEventSource.latest().emit('stream.ready', { cursor: 'c0', resumed: true });
    expect(screen.getByTestId('stream-status')).toHaveTextContent('Live');
    FakeEventSource.latest().fail();
    expect(screen.getByTestId('stream-status')).toHaveTextContent('Reconnecting…');
  });

  it('R51 — a live timeline.appended is rendered once; its redelivery adds nothing', async () => {
    routeApi({ [DETAIL_PATH]: () => json(detail()) });
    renderDetail();
    await screen.findByTestId('order-detail-reference');
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    const source = FakeEventSource.latest();
    source.emit('stream.ready', { cursor: 'c0', resumed: true });
    const frame = { eventId: 'e1', orderId: ORDER_ID, eventType: 'stock.reserved.v1', occurredAt: '2026-09-16T10:00:01.000Z', summary: 'Stock reserved' };
    source.emit('timeline.appended', frame);
    source.emit('timeline.appended', frame);
    expect(entries().map((e) => e.getAttribute('data-event-id'))).toEqual(['e0', 'e1']);
    expect(screen.getByText('Stock reserved')).toBeInTheDocument();
  });

  it.each([
    ['order.updated first', ['order.updated', 'timeline.appended']],
    ['timeline.appended first', ['timeline.appended', 'order.updated']],
  ])('R51 — the two frames of one fact share an eventId and BOTH land: status AND timeline (%s)', async (_label, order) => {
    routeApi({ [DETAIL_PATH]: () => json(detail()) });
    renderDetail();
    await screen.findByTestId('order-detail-reference');
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    const source = FakeEventSource.latest();
    source.emit('stream.ready', { cursor: 'c0', resumed: true });
    for (const type of order) {
      source.emit(
        type!,
        type === 'order.updated'
          ? { eventId: 'e-shared', orderId: ORDER_ID, status: 'stock_reserved', occurredAt: '2026-09-16T10:00:01.000Z' }
          : { eventId: 'e-shared', orderId: ORDER_ID, eventType: 'stock.reserved.v1', occurredAt: '2026-09-16T10:00:01.000Z', summary: 'Stock reserved' },
      );
    }
    expect({ status: screen.getByTestId('order-detail-status').textContent, timeline: entries().map((e) => e.getAttribute('data-event-id')) }).toEqual({ status: 'stock_reserved', timeline: ['e0', 'e-shared'] });
  });

  it('frames replayed for a fact already in the snapshot change nothing', async () => {
    routeApi({ [DETAIL_PATH]: () => json(detail({ status: 'stock_reserved' })) });
    renderDetail();
    await screen.findByTestId('order-detail-reference');
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    const source = FakeEventSource.latest();
    source.emit('order.updated', { eventId: 'e0', orderId: ORDER_ID, status: 'placed', occurredAt: '2026-09-16T10:00:00.000Z' });
    expect(screen.getByTestId('order-detail-status')).toHaveTextContent('stock_reserved');
  });

  it('a live order.updated moves the status badge and fills in references and cancellation reason', async () => {
    routeApi({ [DETAIL_PATH]: () => json(detail()) });
    renderDetail();
    await screen.findByTestId('order-detail-reference');
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    FakeEventSource.latest().emit('order.updated', { eventId: 'e9', orderId: ORDER_ID, status: 'cancelled', cancellationReason: 'credit_rejected', references: { despatchReference: 'DES-000001' }, occurredAt: '2026-09-16T10:00:09.000Z' });
    expect(screen.getByTestId('order-detail-status')).toHaveTextContent('cancelled');
    expect(screen.getByText('credit_rejected')).toBeInTheDocument();
    expect(screen.getByText('DES-000001')).toBeInTheDocument();
  });

  it('stream.ready resumed:false re-fetches the order instead of keeping possibly stale data', async () => {
    let calls = 0;
    routeApi({
      [DETAIL_PATH]: () => {
        calls += 1;
        return json(detail(calls === 1 ? {} : { status: 'confirmed' }));
      },
    });
    renderDetail();
    await screen.findByTestId('order-detail-reference');
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    expect(calls).toBe(1);
    FakeEventSource.latest().emit('stream.ready', { cursor: 'unknown', resumed: false });
    await waitFor(() => expect(screen.getByTestId('order-detail-status')).toHaveTextContent('confirmed'));
    expect(calls).toBe(2);
    // The stream is not reopened by a re-fetch.
    expect(FakeEventSource.instances).toHaveLength(1);
  });

  it('when the client gives up, the page says so and offers a retry that opens a fresh stream', async () => {
    routeApi({ [DETAIL_PATH]: () => json(detail()) });
    renderDetail();
    await screen.findByTestId('order-detail-reference');
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    FakeEventSource.latest().fail(true);
    expect(screen.getByTestId('stream-status')).toHaveTextContent('Connection lost');
    expect(FakeEventSource.instances[0]!.closed).toBe(true);
    await userEvent.click(screen.getByTestId('stream-retry'));
    // Retry re-checks the order first (backlog id 98), then reopens.
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(2));
    expect(FakeEventSource.latest().closed).toBe(false);
    expect(screen.getByTestId('stream-status')).toHaveTextContent('Connecting…');
  });

  it('leaving the page closes the stream (which is what lets the proxy release its Gateway connection)', async () => {
    routeApi({ [DETAIL_PATH]: () => json(detail()) });
    const view = renderDetail();
    await screen.findByTestId('order-detail-reference');
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    view.unmount();
    expect(FakeEventSource.latest().closed).toBe(true);
  });
});

describe('OrderDetailView — the backstop re-read (#7 D2/D7)', () => {
  it('D2 — a fact whose frames are lost entirely still reaches the page, by re-reading while the order is live', async () => {
    let calls = 0;
    routeApi({
      [DETAIL_PATH]: () => {
        calls += 1;
        return json(detail({ status: calls === 1 ? 'paid' : 'completed' }));
      },
    });
    renderDetail(150);
    await screen.findByTestId('order-detail-reference');
    expect(screen.getByTestId('order-detail-status')).toHaveTextContent('paid');
    await waitFor(() => expect(screen.getByTestId('order-detail-status')).toHaveTextContent('completed'), { timeout: 2000 });
  });

  it('D2 — an order that is terminal on first read is never re-read', async () => {
    let calls = 0;
    routeApi({
      [DETAIL_PATH]: () => {
        calls += 1;
        return json(detail({ status: 'completed' }));
      },
    });
    renderDetail(100);
    await screen.findByTestId('order-detail-reference');
    await sleep(450);
    expect(calls, 'reads of an order that was terminal on the first read').toBe(1);
  });

  it('D7 — after a terminal transition this page watched, ONE more read lands the lost sibling entry, then reading stops', async () => {
    let calls = 0;
    routeApi({
      [DETAIL_PATH]: () => {
        calls += 1;
        return calls === 1
          ? json(detail({ status: 'paid' }))
          : json(detail({ status: 'completed', events: [...detail().events, { eventId: 'e9', eventType: 'credit.released.v1', occurredAt: '2026-09-16T10:00:09.000Z', summary: 'Credit released' }] }));
      },
    });
    renderDetail(300);
    await screen.findByTestId('order-detail-reference');
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    FakeEventSource.latest().emit('order.updated', { eventId: 'e10', orderId: ORDER_ID, status: 'completed', occurredAt: '2026-09-16T10:00:09.000Z' });
    expect(screen.getByTestId('order-detail-status')).toHaveTextContent('completed');
    expect(entries()).toHaveLength(1);

    await waitFor(() => expect(screen.getByText('Credit released')).toBeInTheDocument(), { timeout: 2000 });
    const afterCatchUp = calls;
    await sleep(1000);
    expect(calls, 'reads after the one catch-up read').toBe(afterCatchUp);
  });
});

describe('OrderDetailView — causal links', () => {
  it('an entry whose causationId names an entry here links to it; one naming nothing here, or no cause, renders no link', async () => {
    routeApi({
      [DETAIL_PATH]: () =>
        json(
          detail({
            events: [
              { eventId: 'e0', eventType: 'order.placed.v1', occurredAt: '2026-09-16T10:00:00.000Z', summary: 'Order placed' },
              { eventId: 'e1', causationId: 'e0', eventType: 'stock.reserved.v1', occurredAt: '2026-09-16T10:00:01.000Z', summary: 'Stock reserved' },
              { eventId: 'e2', causationId: 'req-a-command-id', eventType: 'credit.released.v1', occurredAt: '2026-09-16T10:00:02.000Z', summary: 'Credit released' },
            ],
          }),
        ),
    });
    renderDetail();
    await screen.findByTestId('order-detail-reference');
    const [origin, caused, unresolved] = entries();
    expect(within(origin!).queryByTestId('timeline-causation')).not.toBeInTheDocument();
    expect(within(origin!).getByText('Order placed')).toBeInTheDocument();
    const link = within(caused!).getByTestId('timeline-causation-link');
    expect(link).toHaveTextContent('order.placed.v1');
    expect(link).toHaveAttribute('href', '#timeline-entry-e0');
    expect(within(caused!).getByTestId('timeline-causation')).toHaveTextContent('caused by order.placed.v1');
    expect(within(unresolved!).queryByTestId('timeline-causation')).not.toBeInTheDocument();
    expect(within(unresolved!).getByText('Credit released')).toBeInTheDocument();
    expect(screen.queryByTestId('order-detail-error')).not.toBeInTheDocument();
    expect(origin).toHaveAttribute('id', 'timeline-entry-e0');
    await userEvent.click(link);
    expect(origin).toHaveClass('bg-muted');
  });

  it('a live frame carrying a causationId renders the same link as a loaded entry', async () => {
    routeApi({ [DETAIL_PATH]: () => json(detail()) });
    renderDetail();
    await screen.findByTestId('order-detail-reference');
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    FakeEventSource.latest().emit('timeline.appended', { eventId: 'e1', causationId: 'e0', orderId: ORDER_ID, eventType: 'stock.reserved.v1', occurredAt: '2026-09-16T10:00:01.000Z', summary: 'Stock reserved' });
    expect(within(entries()[1]!).getByTestId('timeline-causation-link')).toHaveTextContent('order.placed.v1');
  });
});

describe('OrderDetailView — an expired session while the stream is down (backlog id 98)', () => {
  // The app's REAL query client (its QueryCache is what turns a 401 into the
  // sign-out redirect), and the app's REAL signed-out answer: what
  // proxyToGateway returns when the session cookie no longer opens.
  let navigations: string[];
  beforeEach(() => {
    navigations = [];
    setSignedOutNavigation((url) => navigations.push(url));
  });
  afterEach(() => setSignedOutNavigation(undefined));

  const renderSignedIn = () => renderWithQuery(<OrderDetailView orderId={ORDER_ID} streamFactory={fakeEventSourceFactory} />, makeQueryClient());

  it('a TERMINAL order whose stream is refused ends at /login — never at "Connection lost" with a Retry that loops', async () => {
    let calls = 0;
    routeApi({
      [DETAIL_PATH]: () => {
        calls += 1;
        // Read 1: the order, already completed (so it is never re-read on a schedule). Every later read: the session is gone.
        return calls === 1 ? json(detail({ status: 'completed' })) : notSignedIn();
      },
    });
    renderSignedIn();
    await screen.findByTestId('order-detail-reference');
    expect(screen.getByTestId('order-detail-status')).toHaveTextContent('completed');
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));

    // The refused stream: the browser closes it (readyState CLOSED) — held here, not timed.
    FakeEventSource.latest().fail(true);

    await waitFor(() => expect(navigations, 'a TERMINAL (completed) order with an expired session must send the user to /login when its stream is refused — no redirect happened').toEqual(['/login']));
    expect(calls, 'the order is asked again once, when the stream gives up').toBe(2);
    expect(screen.getByTestId('order-detail-error')).toHaveTextContent('Could not load this order: You are not signed in, or your session has expired. Sign in again.');
    expect(screen.queryByTestId('stream-retry'), 'no Retry is offered for a refusal').not.toBeInTheDocument();
    expect(FakeEventSource.instances, 'nothing reopens a refused stream').toHaveLength(1);
  });

  it('Retry re-checks before reopening: a session that expired after the drop signs the user out instead of reopening', async () => {
    let calls = 0;
    routeApi({
      [DETAIL_PATH]: () => {
        calls += 1;
        // Reads 1-2 (load, and the re-check when the stream gives up) succeed: a genuine drop. Read 3 (at Retry): the session is gone.
        return calls <= 2 ? json(detail({ status: 'completed' })) : notSignedIn();
      },
    });
    renderSignedIn();
    await screen.findByTestId('order-detail-reference');
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    FakeEventSource.latest().fail(true);
    await waitFor(() => expect(calls, 'when the stream gives up, the TERMINAL order must be asked again once (the re-check that can see a 401)').toBe(2));
    expect(screen.getByTestId('stream-status')).toHaveTextContent('Connection lost');
    expect(navigations).toEqual([]);

    await userEvent.click(screen.getByTestId('stream-retry'));
    await waitFor(() => expect(navigations, 'a Retry refused for an expired session on a TERMINAL order must send the user to /login').toEqual(['/login']));
    expect(FakeEventSource.instances, 'Retry must not reopen a stream the session can no longer open').toHaveLength(1);
  });

  it('a genuine drop on a terminal order (the re-check succeeds) keeps "Connection lost" with a Retry that reopens, and signs nobody out', async () => {
    let calls = 0;
    routeApi({
      [DETAIL_PATH]: () => {
        calls += 1;
        return json(detail({ status: 'completed' }));
      },
    });
    renderSignedIn();
    await screen.findByTestId('order-detail-reference');
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));
    FakeEventSource.latest().fail(true);
    await waitFor(() => expect(calls, 'when the stream gives up, the TERMINAL order must be asked again once (the re-check that can see a 401)').toBe(2));
    expect(screen.getByTestId('stream-status')).toHaveTextContent('Connection lost');
    await userEvent.click(screen.getByTestId('stream-retry'));
    await waitFor(() => expect(FakeEventSource.instances).toHaveLength(2));
    expect(calls, 'Retry must re-check the order before reopening the stream').toBe(3);
    expect(navigations).toEqual([]);
  });
});
