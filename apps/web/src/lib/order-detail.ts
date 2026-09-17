import type { OrderDetail, OrderStatus, OrderStreamUpdate, ProjectionPending, TimelineEntry, TimelineStreamEntry } from '@/lib/api-types';

/** What `GET /api/orders/{id}` answered: a projected document, or the R55 "projection pending" (202) answer with the Gateway's suggested retry delay. */
export type OrderDetailResult = { kind: 'ready'; detail: OrderDetail } | { kind: 'pending'; pending: ProjectionPending; retryAfterMs: number };

export const DEFAULT_PENDING_RETRY_MS = 2000;

/** The Gateway's retry hint for a 202: the `Retry-After` header (delta-seconds) first, then the body's `retryAfterMs`, then a default. Never zero, so a hint of `0` cannot become a tight loop. */
export function pendingRetryDelayMs(retryAfterHeader: string | null, pending: Partial<ProjectionPending> | undefined): number {
  if (retryAfterHeader !== null && /^\d+$/.test(retryAfterHeader.trim())) {
    return Math.max(250, Number(retryAfterHeader.trim()) * 1000);
  }
  if (typeof pending?.retryAfterMs === 'number' && Number.isSafeInteger(pending.retryAfterMs)) {
    return Math.max(250, pending.retryAfterMs);
  }
  return DEFAULT_PENDING_RETRY_MS;
}

const TERMINAL: ReadonlySet<OrderStatus> = new Set<OrderStatus>(['completed', 'cancelled']);

/** `completed` and `cancelled` are the only statuses an order never leaves (openapi.yaml `OrderStatus`). */
export function isTerminalStatus(status: OrderStatus): boolean {
  return TERMINAL.has(status);
}

/**
 * The timeline order openapi.yaml `TimelineEntry` prescribes: `occurredAt`
 * ascending (R50); within one identical `occurredAt`, a fact follows the fact
 * that caused it (`causationId` = the other's `eventId`); where no recorded
 * edge exists, `eventId` ascending (amendment A1). Applied to live-appended
 * entries so a page that received them over the stream shows the same order a
 * reload would.
 */
export function orderTimeline(entries: readonly TimelineEntry[]): TimelineEntry[] {
  const byInstant = new Map<string, TimelineEntry[]>();
  for (const entry of entries) {
    const group = byInstant.get(entry.occurredAt);
    if (group) group.push(entry);
    else byInstant.set(entry.occurredAt, [entry]);
  }
  const instants = [...byInstant.keys()].sort((a, b) => Date.parse(a) - Date.parse(b) || compareText(a, b));
  const ordered: TimelineEntry[] = [];
  for (const instant of instants) {
    ordered.push(...orderTieGroup(byInstant.get(instant) ?? []));
  }
  return ordered;
}

function orderTieGroup(group: TimelineEntry[]): TimelineEntry[] {
  if (group.length < 2) return [...group];
  const ids = new Set(group.map((entry) => entry.eventId));
  const remaining = [...group].sort((a, b) => compareText(a.eventId, b.eventId));
  const placed = new Set<string>();
  const out: TimelineEntry[] = [];
  while (remaining.length > 0) {
    // The lowest eventId whose in-group cause (if any) is already placed; a cycle falls back to the lowest eventId.
    let index = remaining.findIndex((entry) => !entry.causationId || !ids.has(entry.causationId) || placed.has(entry.causationId));
    if (index < 0) index = 0;
    const [next] = remaining.splice(index, 1);
    if (!next) break;
    out.push(next);
    placed.add(next.eventId);
  }
  return out;
}

function compareText(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}

/** Applies a live `order.updated` frame to a projected document. A pending answer has nothing to patch. */
export function applyOrderUpdate(current: OrderDetailResult | undefined, update: OrderStreamUpdate): OrderDetailResult | undefined {
  if (!current || current.kind !== 'ready') return current;
  return {
    kind: 'ready',
    detail: {
      ...current.detail,
      status: update.status,
      cancellationReason: update.cancellationReason !== undefined ? update.cancellationReason : current.detail.cancellationReason,
      references: update.references ? { ...current.detail.references, ...update.references } : current.detail.references,
      totals: update.totals ?? current.detail.totals,
      orderReference: update.orderReference ?? current.detail.orderReference,
      updatedAt: update.occurredAt,
    },
  };
}

/**
 * Appends a live `timeline.appended` frame. An entry whose `eventId` is already
 * in the timeline is a redelivery and changes nothing (R51) — the second
 * de-duplication layer, independent of the stream client's own.
 */
export function applyTimelineAppended(current: OrderDetailResult | undefined, entry: TimelineStreamEntry): OrderDetailResult | undefined {
  if (!current || current.kind !== 'ready') return current;
  if (current.detail.events.some((existing) => existing.eventId === entry.eventId)) return current;
  const appended: TimelineEntry = { eventId: entry.eventId, eventType: entry.eventType, occurredAt: entry.occurredAt, summary: entry.summary };
  if (entry.causationId) appended.causationId = entry.causationId;
  return { kind: 'ready', detail: { ...current.detail, events: orderTimeline([...current.detail.events, appended]) } };
}

/** The entry a timeline entry's `causationId` names, within THIS order's timeline — or nothing (absent id, or an id naming a command rather than a fact here). */
export function causingEntry(entry: TimelineEntry, events: readonly TimelineEntry[]): TimelineEntry | undefined {
  if (!entry.causationId) return undefined;
  return events.find((candidate) => candidate.eventId === entry.causationId);
}
