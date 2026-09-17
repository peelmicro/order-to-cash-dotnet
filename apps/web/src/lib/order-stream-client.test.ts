// @vitest-environment node
//
// OrderStreamClient against a REAL EventSource (the standards-following
// `eventsource` package — the same reconnect-with-Last-Event-ID behaviour a
// browser has) talking real HTTP to a real local server that writes real SSE
// bytes in the frame format openapi.yaml documents. Nothing about
// de-duplication or reconnection is mocked.
import { createServer, type IncomingMessage, type RequestListener, type Server, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';
import { EventSource } from 'eventsource';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { OrderStreamUpdate, TimelineStreamEntry } from './api-types';
import { OrderStreamClient, type EventSourceLike, type OrderStreamCallbacks, type StreamConnectionStatus } from './order-stream-client';

const realEventSource = (url: string): EventSourceLike => new EventSource(url) as unknown as EventSourceLike;

function frame(id: string | null, event: string, data: unknown): string {
  return `${id !== null ? `id: ${id}\n` : ''}event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
}

function openStream(res: ServerResponse): void {
  res.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache', Connection: 'keep-alive' });
}

const update = (eventId: string, status: OrderStreamUpdate['status']): OrderStreamUpdate => ({ eventId, orderId: 'ord-1', status, occurredAt: '2026-09-16T10:00:00.000Z' });
const entry = (eventId: string, eventType: string): TimelineStreamEntry => ({ eventId, orderId: 'ord-1', eventType, occurredAt: '2026-09-16T10:00:00.000Z', summary: eventType });

interface Recorded {
  updates: OrderStreamUpdate[];
  entries: TimelineStreamEntry[];
  resyncs: number;
  statuses: StreamConnectionStatus[];
  callbacks: OrderStreamCallbacks;
}

function recorder(): Recorded {
  const recorded: Recorded = {
    updates: [],
    entries: [],
    resyncs: 0,
    statuses: [],
    callbacks: {
      onOrderUpdated: (u) => recorded.updates.push(u),
      onTimelineAppended: (e) => recorded.entries.push(e),
      onResync: () => {
        recorded.resyncs += 1;
      },
      onStatusChange: (s) => recorded.statuses.push(s),
    },
  };
  return recorded;
}

describe('OrderStreamClient — real EventSource, real HTTP, real SSE bytes', () => {
  let server: Server | undefined;
  let client: OrderStreamClient | undefined;

  afterEach(async () => {
    client?.disconnect();
    client = undefined;
    if (server) {
      server.closeAllConnections();
      await new Promise<void>((resolve) => server!.close(() => resolve()));
      server = undefined;
    }
  });

  async function listen(handler: RequestListener): Promise<string> {
    server = createServer(handler);
    await new Promise<void>((resolve) => server!.listen(0, '127.0.0.1', resolve));
    return `http://127.0.0.1:${(server.address() as AddressInfo).port}/stream`;
  }

  /** Waits long enough for a wrongly duplicated or wrongly dropped frame to have arrived. */
  const settle = () => new Promise((resolve) => setTimeout(resolve, 150));

  it('R51 — a redelivered order.updated (same type, same eventId) is applied exactly once', async () => {
    const url = await listen((_req, res) => {
      openStream(res);
      res.write(frame(null, 'stream.ready', { cursor: 'c0', resumed: true }));
      res.write(frame('c1', 'order.updated', update('evt-dup', 'stock_reserved')));
      res.write(frame('c1', 'order.updated', update('evt-dup', 'stock_reserved')));
      res.write(frame('c2', 'timeline.appended', entry('evt-dup-t', 'x')));
      res.write(frame('c2', 'timeline.appended', entry('evt-dup-t', 'x')));
    });
    const r = recorder();
    client = new OrderStreamClient(url, r.callbacks, realEventSource);
    client.connect();

    await vi.waitFor(() => expect(r.entries.length).toBeGreaterThan(0), { timeout: 3000 });
    await settle();
    expect(r.updates.map((u) => u.eventId)).toEqual(['evt-dup']);
    expect(r.entries.map((e) => e.eventId)).toEqual(['evt-dup-t']);
  });

  it.each([
    ['order.updated first', ['order.updated', 'timeline.appended']],
    ['timeline.appended first', ['timeline.appended', 'order.updated']],
  ])('R51 — the two frames of ONE fact share an eventId and BOTH are applied (%s)', async (_label, order) => {
    const shared = 'evt-shared-1';
    const url = await listen((_req, res) => {
      openStream(res);
      res.write(frame(null, 'stream.ready', { cursor: 'c0', resumed: true }));
      order.forEach((type, index) => {
        res.write(frame(`c${index + 1}`, type!, type === 'order.updated' ? update(shared, 'confirmed') : entry(shared, 'order.confirmed.v1')));
      });
    });
    const r = recorder();
    client = new OrderStreamClient(url, r.callbacks, realEventSource);
    client.connect();

    await vi.waitFor(() => expect(r.updates.length + r.entries.length).toBeGreaterThanOrEqual(1), { timeout: 3000 });
    await settle();
    expect({ updates: r.updates.map((u) => u.eventId), entries: r.entries.map((e) => e.eventId) }).toEqual({ updates: [shared], entries: [shared] });
  });

  it('seeded snapshot ids suppress a replayed frame of EITHER type, yet a new fact still gets both frames', async () => {
    const url = await listen((_req, res) => {
      openStream(res);
      res.write(frame(null, 'stream.ready', { cursor: 'c0', resumed: true }));
      res.write(frame('c1', 'timeline.appended', entry('evt-old', 'old')));
      res.write(frame('c2', 'order.updated', update('evt-old', 'placed')));
      res.write(frame('c3', 'order.updated', update('evt-new', 'confirmed')));
      res.write(frame('c4', 'timeline.appended', entry('evt-new', 'new')));
    });
    const r = recorder();
    client = new OrderStreamClient(url, r.callbacks, realEventSource);
    client.seedSeenEventIds(['evt-old']);
    client.connect();

    await vi.waitFor(() => expect(r.entries.length).toBeGreaterThan(0), { timeout: 3000 });
    await settle();
    expect({ updates: r.updates.map((u) => u.eventId), entries: r.entries.map((e) => e.eventId) }, 'frames applied after seeding evt-old').toEqual({ updates: ['evt-new'], entries: ['evt-new'] });
  });

  it('stream.ready resumed:false asks the page to re-fetch; resumed:true does not', async () => {
    const url = await listen((req, res) => {
      openStream(res);
      res.write(frame(null, 'stream.ready', { cursor: 'c0', resumed: req.url?.includes('resumed') ?? false }));
      res.write(frame(null, 'ping', { at: '2026-09-16T10:00:00.000Z' }));
    });
    const first = recorder();
    client = new OrderStreamClient(url, first.callbacks, realEventSource);
    client.connect();
    await vi.waitFor(() => expect(first.resyncs).toBe(1), { timeout: 3000 });
    client.disconnect();

    const second = recorder();
    client = new OrderStreamClient(`${url}?resumed`, second.callbacks, realEventSource);
    client.connect();
    await vi.waitFor(() => expect(second.statuses).toContain('connected'), { timeout: 3000 });
    await settle();
    expect(second.resyncs).toBe(0);
  });

  it('id 30 — a forced mid-stream disconnect is resumed with Last-Event-ID: the missed frame arrives once, nothing is lost or duplicated', async () => {
    const seen: { lastEventId: string | undefined }[] = [];
    const url = await listen((req: IncomingMessage, res) => {
      const lastEventId = req.headers['last-event-id'] as string | undefined;
      seen.push({ lastEventId });
      openStream(res);
      // A short reconnection delay for this local server only; the Gateway sends no `retry:`.
      res.write('retry: 20\n\n');
      if (seen.length === 1) {
        res.write(frame(null, 'stream.ready', { cursor: 'c0', resumed: false }));
        res.write(frame('cursor-1', 'order.updated', update('evt-1', 'stock_reserved')));
        res.write(frame('cursor-1t', 'timeline.appended', entry('evt-1', 'stock.reserved.v1')));
        // `ping` has no id (openapi.yaml): the resume point must stay the last CONTENT frame.
        res.write(frame(null, 'ping', { at: '2026-09-16T10:00:01.000Z' }));
        setTimeout(() => res.destroy(), 50);
        return;
      }
      const resumed = lastEventId === 'cursor-1t';
      res.write(frame(null, 'stream.ready', { cursor: lastEventId ?? 'c0', resumed }));
      // The replay buffer re-sends from the cursor: evt-1's timeline frame is
      // at-least-once redelivered alongside the frames missed while away.
      res.write(frame('cursor-1t', 'timeline.appended', entry('evt-1', 'stock.reserved.v1')));
      res.write(frame('cursor-2', 'order.updated', update('evt-2', 'credit_approved')));
      res.write(frame('cursor-2t', 'timeline.appended', entry('evt-2', 'credit.approved.v1')));
    });
    const r = recorder();
    client = new OrderStreamClient(url, r.callbacks, realEventSource);
    client.connect();

    await vi.waitFor(() => expect(r.entries.map((e) => e.eventId)).toEqual(['evt-1', 'evt-2']), { timeout: 5000 });
    await settle();
    expect(seen.map((s) => s.lastEventId)).toEqual([undefined, 'cursor-1t']);
    expect(r.updates.map((u) => u.eventId)).toEqual(['evt-1', 'evt-2']);
    expect(r.entries.map((e) => e.eventId)).toEqual(['evt-1', 'evt-2']);
    expect(r.resyncs).toBe(1);
    expect(r.statuses).toContain('reconnecting');
    expect(r.statuses.at(-1)).toBe('connected');
  });

  it('gives up visibly after repeated failures, and stops reconnecting', async () => {
    let connections = 0;
    const url = await listen((_req, res) => {
      connections += 1;
      openStream(res);
      res.write('retry: 10\n\n');
      setTimeout(() => res.destroy(), 5);
    });
    const r = recorder();
    client = new OrderStreamClient(url, r.callbacks, realEventSource, 3);
    client.connect();

    await vi.waitFor(() => expect(r.statuses).toContain('gave-up'), { timeout: 5000 });
    const atGiveUp = connections;
    await new Promise((resolve) => setTimeout(resolve, 200));
    expect(connections).toBe(atGiveUp);
    expect(r.statuses.filter((s) => s === 'reconnecting')).toHaveLength(2);
  });

  it('a non-200 answer (e.g. a 401 problem) ends the stream as gave-up rather than retrying forever', async () => {
    const url = await listen((_req, res) => {
      res.writeHead(401, { 'Content-Type': 'application/problem+json' });
      res.end('{"title":"Not signed in","code":"UNAUTHENTICATED"}');
    });
    const r = recorder();
    client = new OrderStreamClient(url, r.callbacks, realEventSource);
    client.connect();
    await vi.waitFor(() => expect(r.statuses.at(-1)).toBe('gave-up'), { timeout: 3000 });
  });

  it('ignores malformed data without breaking the stream', async () => {
    const url = await listen((_req, res) => {
      openStream(res);
      res.write('event: order.updated\ndata: {not json\n\n');
      res.write(frame(null, 'stream.ready', { cursor: 'c0', resumed: true }));
      res.write(frame('c1', 'order.updated', update('evt-ok', 'confirmed')));
    });
    const r = recorder();
    client = new OrderStreamClient(url, r.callbacks, realEventSource);
    client.connect();
    await vi.waitFor(() => expect(r.updates.map((u) => u.eventId)).toEqual(['evt-ok']), { timeout: 3000 });
  });
});
