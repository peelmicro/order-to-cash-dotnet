// @vitest-environment node
import { describe, expect, it } from 'vitest';
import type { OrderDetail, TimelineEntry } from './api-types';
import { applyOrderUpdate, applyTimelineAppended, causingEntry, isTerminalStatus, orderTimeline, pendingRetryDelayMs, type OrderDetailResult } from './order-detail';

function ready(overrides: Partial<OrderDetail> = {}): OrderDetailResult {
  return {
    kind: 'ready',
    detail: {
      orderId: 'ord-1',
      orderReference: 'ORD-000001',
      status: 'placed',
      currency: 'EUR',
      totals: { initialAmount: 1000, initialDiscount: 0, totalAmount: 1000 },
      events: [{ eventId: 'e0', eventType: 'order.placed.v1', occurredAt: '2026-08-27T10:00:00.000Z', summary: 'Order placed' }],
      references: {},
      updatedAt: '2026-08-27T10:00:00.000Z',
      ...overrides,
    },
  };
}

const pending: OrderDetailResult = { kind: 'pending', pending: { orderId: 'ord-1', status: 'projection_pending' }, retryAfterMs: 2000 };

describe('R55 — the retry schedule of a 202 comes from the Gateway', () => {
  it('Retry-After (seconds) wins, then the body\'s retryAfterMs, then the default; never below 250 ms', () => {
    expect(pendingRetryDelayMs('3', { retryAfterMs: 500 })).toBe(3000);
    expect(pendingRetryDelayMs(null, { retryAfterMs: 1500 })).toBe(1500);
    expect(pendingRetryDelayMs(null, undefined)).toBe(2000);
    expect(pendingRetryDelayMs('not-a-number', { retryAfterMs: 700 })).toBe(700);
    expect(pendingRetryDelayMs('0', undefined)).toBe(250);
    expect(pendingRetryDelayMs(null, { retryAfterMs: 0 })).toBe(250);
  });

  it('only completed and cancelled are terminal', () => {
    expect(['placed', 'stock_reserved', 'credit_approved', 'confirmed', 'despatched', 'invoiced', 'paid'].map((s) => isTerminalStatus(s as never))).toEqual([false, false, false, false, false, false, false]);
    expect(isTerminalStatus('completed')).toBe(true);
    expect(isTerminalStatus('cancelled')).toBe(true);
  });
});

describe('R51 — applyTimelineAppended appends once, in timeline order', () => {
  it('appends a new entry, carrying its causationId', () => {
    const next = applyTimelineAppended(ready(), { eventId: 'e1', causationId: 'e0', orderId: 'ord-1', eventType: 'stock.reserved.v1', occurredAt: '2026-08-27T10:05:00.000Z', summary: 'Stock reserved' });
    expect(next?.kind === 'ready' && next.detail.events.map((e) => [e.eventId, e.causationId ?? null])).toEqual([
      ['e0', null],
      ['e1', 'e0'],
    ]);
  });

  it('a redelivered entry (eventId already present) returns the SAME document, unchanged', () => {
    const current = ready();
    const next = applyTimelineAppended(current, { eventId: 'e0', orderId: 'ord-1', eventType: 'order.placed.v1', occurredAt: '2026-08-27T10:00:00.000Z', summary: 'Order placed' });
    expect(next?.kind === 'ready' && next.detail.events.map((e) => e.eventId), 'a redelivered entry must not be appended again').toEqual(['e0']);
    expect(next, 'a redelivery returns the very same document object').toBe(current);
  });

  it('an entry that arrives out of order is placed by occurredAt, not by arrival (R50)', () => {
    const current = ready({ events: [{ eventId: 'e2', eventType: 'credit.approved.v1', occurredAt: '2026-08-27T10:10:00.000Z', summary: 'Credit approved' }] });
    const next = applyTimelineAppended(current, { eventId: 'e1', orderId: 'ord-1', eventType: 'stock.reserved.v1', occurredAt: '2026-08-27T10:05:00.000Z', summary: 'Stock reserved' });
    expect(next?.kind === 'ready' && next.detail.events.map((e) => e.eventId)).toEqual(['e1', 'e2']);
  });

  it('is a no-op while the answer is still projection-pending, and for an empty cache', () => {
    const entry = { eventId: 'e1', orderId: 'ord-1', eventType: 'x', occurredAt: '2026-08-27T10:05:00.000Z', summary: 's' };
    expect(applyTimelineAppended(pending, entry)).toBe(pending);
    expect(applyTimelineAppended(undefined, entry)).toBeUndefined();
  });
});

describe('orderTimeline — occurredAt, then cause-before-effect inside a tie, then eventId (openapi.yaml TimelineEntry, amendment A1)', () => {
  const at = '2026-08-27T10:23:05.945Z';
  it('within one identical occurredAt, a fact follows the fact that caused it even when its eventId sorts first', () => {
    const entries: TimelineEntry[] = [
      { eventId: 'c-completed', causationId: 'b-released', eventType: 'order.completed.v1', occurredAt: at, summary: '3' },
      { eventId: 'a-payment', eventType: 'payment.received.v1', occurredAt: at, summary: '1' },
      { eventId: 'b-released', causationId: 'z-not-in-group', eventType: 'credit.released.v1', occurredAt: at, summary: '2' },
      { eventId: '0-earlier', eventType: 'invoice.issued.v1', occurredAt: '2026-08-27T10:20:00.000Z', summary: '0' },
    ];
    // b-released names a cause outside the group, so it is free; c-completed must wait for b-released.
    expect(orderTimeline(entries).map((e) => e.eventId)).toEqual(['0-earlier', 'a-payment', 'b-released', 'c-completed']);
  });

  it('an effect whose eventId sorts BEFORE its cause is still placed after it', () => {
    const entries: TimelineEntry[] = [
      { eventId: 'b', eventType: 'cause', occurredAt: at, summary: '' },
      { eventId: 'a', causationId: 'b', eventType: 'effect', occurredAt: at, summary: '' },
    ];
    expect(orderTimeline(entries).map((e) => e.eventId)).toEqual(['b', 'a']);
  });

  it('with no recorded edge, ties fall back to eventId ascending; a causal cycle does not hang', () => {
    expect(orderTimeline([{ eventId: 'y', eventType: '', occurredAt: at, summary: '' }, { eventId: 'x', eventType: '', occurredAt: at, summary: '' }]).map((e) => e.eventId)).toEqual(['x', 'y']);
    const cycle: TimelineEntry[] = [
      { eventId: 'p', causationId: 'q', eventType: '', occurredAt: at, summary: '' },
      { eventId: 'q', causationId: 'p', eventType: '', occurredAt: at, summary: '' },
    ];
    expect(orderTimeline(cycle).map((e) => e.eventId)).toEqual(['p', 'q']);
  });
});

describe('applyOrderUpdate — a live order.updated frame patches the header', () => {
  it('patches status, references, totals, cancellationReason and updatedAt', () => {
    const next = applyOrderUpdate(ready({ references: { invoiceReference: 'INV-000001' } }), {
      eventId: 'e2',
      orderId: 'ord-1',
      status: 'cancelled',
      cancellationReason: 'credit_rejected',
      references: { despatchReference: 'DES-000001' },
      totals: { initialAmount: 24999, initialDiscount: 0, totalAmount: 24999 },
      occurredAt: '2026-08-27T10:10:00.000Z',
    });
    expect(next?.kind === 'ready' && next.detail).toMatchObject({
      status: 'cancelled',
      cancellationReason: 'credit_rejected',
      references: { invoiceReference: 'INV-000001', despatchReference: 'DES-000001' },
      totals: { totalAmount: 24999 },
      updatedAt: '2026-08-27T10:10:00.000Z',
    });
  });

  it('keeps what the frame does not carry, and is a no-op while pending', () => {
    const next = applyOrderUpdate(ready({ cancellationReason: null }), { eventId: 'e2', orderId: 'ord-1', status: 'confirmed', occurredAt: '2026-08-27T10:10:00.000Z' });
    expect(next?.kind === 'ready' && [next.detail.status, next.detail.totals?.totalAmount, next.detail.orderReference]).toEqual(['confirmed', 1000, 'ORD-000001']);
    expect(applyOrderUpdate(pending, { eventId: 'e2', orderId: 'ord-1', status: 'confirmed', occurredAt: 'x' })).toBe(pending);
  });
});

describe('causingEntry — causal links resolve only within this order\'s timeline', () => {
  const events: TimelineEntry[] = [
    { eventId: 'e0', eventType: 'order.placed.v1', occurredAt: 't', summary: '' },
    { eventId: 'e1', causationId: 'e0', eventType: 'stock.reserved.v1', occurredAt: 't', summary: '' },
    { eventId: 'e2', causationId: 'req-command-id', eventType: 'credit.released.v1', occurredAt: 't', summary: '' },
  ];
  it('resolves a causationId naming an entry here, and nothing for an absent or unresolvable one', () => {
    expect(causingEntry(events[1]!, events)?.eventId).toBe('e0');
    expect(causingEntry(events[0]!, events)).toBeUndefined();
    expect(causingEntry(events[2]!, events)).toBeUndefined();
  });
});
