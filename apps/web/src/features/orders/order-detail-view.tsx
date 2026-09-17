'use client';

import Link from 'next/link';
import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { DateTime } from '@/components/date-time';
import { ErrorMessage } from '@/components/error-message';
import { OrderStatusBadge } from '@/components/status-badge';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Separator } from '@/components/ui/separator';
import { useOrderDetail, useOrderDetailPatchers } from '@/hooks/use-order-detail';
import { useOrderStream } from '@/hooks/use-order-stream';
import type { OrderDetail } from '@/lib/api-types';
import { formatMinorUnits } from '@/lib/money';
import { causingEntry } from '@/lib/order-detail';
import type { EventSourceFactory, StreamConnectionStatus } from '@/lib/order-stream-client';

const CONNECTION_LABEL: Record<StreamConnectionStatus, string> = {
  connecting: 'Connecting…',
  connected: 'Live',
  reconnecting: 'Reconnecting…',
  'gave-up': 'Connection lost',
};

/**
 * One order: header, references, and the live saga timeline (R50/R55). The
 * stream starts only once a projected document exists; before that the page
 * shows the named "waiting for projection" state and keeps re-asking.
 * `streamFactory` and `backstopMs` are test seams; a page never passes them.
 */
export function OrderDetailView({ orderId, streamFactory, backstopMs }: { orderId: string; streamFactory?: EventSourceFactory; backstopMs?: number }) {
  const detail = useOrderDetail(orderId, backstopMs);
  const patchers = useOrderDetailPatchers(orderId);
  const ready = detail.data?.kind === 'ready' ? detail.data.detail : undefined;
  const snapshotEvents = ready?.events;
  const seedEventIds = useCallback(() => (snapshotEvents ?? []).map((event) => event.eventId), [snapshotEvents]);

  const stream = useOrderStream({ orderId, enabled: ready !== undefined, seedEventIds, handlers: patchers, factory: streamFactory });

  // An EventSource cannot say WHY it gave up: a dropped network and a refused
  // (signed-out) stream look the same. So when it gives up, ask the order again
  // through the query client, which can: a 401 there reaches the app's
  // sign-out redirect (app/providers.tsx), and any other failure is shown in
  // its own words. Without this, a TERMINAL order — which is never re-read —
  // stayed on "Connection lost" with an expired session (backlog id 98).
  const { refetch } = detail;
  const streamStatus = stream.status;
  useEffect(() => {
    if (streamStatus === 'gave-up') void refetch();
  }, [streamStatus, refetch]);

  // Retry re-checks the same way before reopening, so it never reopens a stream the session can no longer open.
  const { retry } = stream;
  const retryConnection = useCallback(async () => {
    const checked = await refetch();
    if (checked.isSuccess) retry();
  }, [refetch, retry]);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-semibold">Order detail</h1>
        <Link href="/orders" className="text-sm text-muted-foreground hover:underline">
          Back to orders
        </Link>
      </div>

      {detail.isError ? (
        <ErrorMessage error={detail.error} prefix="Could not load this order" fallback="the request failed" testId="order-detail-error" />
      ) : detail.isPending ? (
        <p className="text-sm text-muted-foreground" data-testid="order-detail-loading">
          Loading order…
        </p>
      ) : detail.data.kind === 'pending' ? (
        <WaitingForProjection message={detail.data.pending.message} retryAfterMs={detail.data.retryAfterMs} lastCheckedAt={detail.dataUpdatedAt} checking={detail.isFetching} onCheckNow={() => void detail.refetch()} />
      ) : (
        <ReadyOrder detail={detail.data.detail} connection={stream.status} onRetryConnection={() => void retryConnection()} />
      )}
    </div>
  );
}

/** R55's honest answer: the order was accepted and is not projected yet. Named, explained, retrying on the Gateway's schedule — not a spinner, not an error, not a 404. */
function WaitingForProjection({ message, retryAfterMs, lastCheckedAt, checking, onCheckNow }: { message?: string; retryAfterMs: number; lastCheckedAt: number; checking: boolean; onCheckNow: () => void }) {
  return (
    <div className="flex flex-col gap-2 rounded-md border border-dashed p-6 text-sm" data-testid="order-detail-pending" aria-live="polite">
      <p className="font-medium">Waiting for this order to appear</p>
      <p className="text-muted-foreground">{message ?? 'The order was accepted and is not projected yet.'}</p>
      <p className="text-muted-foreground" data-testid="order-detail-pending-schedule">
        Checking again every {Math.round(retryAfterMs / 100) / 10} s, as the Gateway asked · last checked <DateTime value={new Date(lastCheckedAt).toISOString()} />
        {checking ? ' · checking…' : ''}
      </p>
      <div>
        <Button type="button" variant="outline" size="sm" onClick={onCheckNow}>
          Check now
        </Button>
      </div>
    </div>
  );
}

function ReadyOrder({ detail, connection, onRetryConnection }: { detail: OrderDetail; connection: StreamConnectionStatus; onRetryConnection: () => void }) {
  const [highlighted, setHighlighted] = useState<string | null>(null);
  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <CardTitle data-testid="order-detail-reference">{detail.orderReference ?? detail.orderId}</CardTitle>
            <p className="truncate text-sm text-muted-foreground">
              {detail.retailer?.name ?? detail.retailer?.code ?? '—'} · {detail.company?.name ?? detail.company?.code ?? '—'}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <OrderStatusBadge status={detail.status} testId="order-detail-status" />
            <Badge variant={connection === 'connected' ? 'secondary' : connection === 'gave-up' ? 'destructive' : 'outline'} data-testid="stream-status" data-status={connection}>
              {CONNECTION_LABEL[connection]}
            </Badge>
            {connection === 'gave-up' ? (
              <Button type="button" size="sm" variant="outline" onClick={onRetryConnection} data-testid="stream-retry">
                Retry connection
              </Button>
            ) : null}
          </div>
        </CardHeader>
        <CardContent className="flex flex-col gap-3">
          {detail.headerComplete === false ? (
            <p className="text-xs text-muted-foreground" data-testid="order-detail-header-incomplete">
              Header still filling in — the timeline is real, some fields are not projected yet.
            </p>
          ) : null}
          <dl className="flex flex-wrap gap-x-8 gap-y-3 text-sm">
            <Field label="Total" testId="order-detail-total">
              {detail.totals && detail.currency ? formatMinorUnits(detail.totals.totalAmount, detail.currency) : '—'}
            </Field>
            {detail.cancellationReason ? <Field label="Cancellation reason">{detail.cancellationReason}</Field> : null}
            {detail.references?.despatchReference ? <Field label="Despatch">{detail.references.despatchReference}</Field> : null}
            {detail.references?.invoiceReference ? <Field label="Invoice">{detail.references.invoiceReference}</Field> : null}
            {detail.references?.paymentReference ? <Field label="Payment">{detail.references.paymentReference}</Field> : null}
          </dl>
          {detail.items && detail.items.length > 0 && detail.currency ? (
            <ul className="text-sm text-muted-foreground" data-testid="order-detail-items">
              {detail.items.map((item) => (
                <li key={item.productCode}>
                  {item.quantity} × {item.name ?? item.productCode} at {formatMinorUnits(item.unitPrice, detail.currency ?? 'EUR')}
                  {item.lineDiscount > 0 ? ` (− ${formatMinorUnits(item.lineDiscount, detail.currency ?? 'EUR')})` : ''}
                </li>
              ))}
            </ul>
          ) : null}
        </CardContent>
      </Card>

      <Separator />

      <section className="flex flex-col gap-3" aria-labelledby="timeline-heading">
        <h2 id="timeline-heading" className="text-sm font-semibold text-muted-foreground">
          Timeline
        </h2>
        {detail.events.length === 0 ? (
          <p className="text-sm text-muted-foreground">No facts recorded yet.</p>
        ) : (
          <ol className="flex flex-col gap-2" data-testid="order-timeline">
            {detail.events.map((event) => {
              const cause = causingEntry(event, detail.events);
              return (
                <li
                  key={event.eventId}
                  id={`timeline-entry-${event.eventId}`}
                  data-testid="timeline-entry"
                  data-event-id={event.eventId}
                  className={`flex items-start justify-between gap-4 rounded-md border p-3 text-sm transition-colors ${highlighted === event.eventId ? 'bg-muted' : ''}`}
                >
                  <div className="min-w-0">
                    <p className="font-medium">{event.summary}</p>
                    <p className="text-xs text-muted-foreground">{event.eventType}</p>
                    {cause ? (
                      <p className="text-xs text-muted-foreground" data-testid="timeline-causation">
                        caused by{' '}
                        <a href={`#timeline-entry-${cause.eventId}`} onClick={() => setHighlighted(cause.eventId)} className="underline decoration-dotted underline-offset-2 hover:text-foreground" data-testid="timeline-causation-link">
                          {cause.eventType}
                        </a>
                      </p>
                    ) : null}
                  </div>
                  <span className="text-xs whitespace-nowrap text-muted-foreground">
                    <DateTime value={event.occurredAt} />
                  </span>
                </li>
              );
            })}
          </ol>
        )}
      </section>
    </div>
  );
}

function Field({ label, children, testId }: { label: string; children: ReactNode; testId?: string }) {
  return (
    <div>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="font-medium" data-testid={testId}>
        {children}
      </dd>
    </div>
  );
}
