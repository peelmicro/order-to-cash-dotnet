'use client';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useRef } from 'react';
import type { OrderDetail, OrderStreamUpdate, ProjectionPending, TimelineStreamEntry } from '@/lib/api-types';
import { apiRequest } from '@/lib/api-client';
import { applyOrderUpdate, applyTimelineAppended, isTerminalStatus, pendingRetryDelayMs, type OrderDetailResult } from '@/lib/order-detail';

export function orderDetailKey(orderId: string): readonly unknown[] {
  return ['order-detail', orderId];
}

/**
 * While a document is live, the stream is the primary updater — but a frame
 * lost outright (no resend, no `resumed: false`) would otherwise leave a stale
 * status on screen forever with the connection badge saying "Live". So the
 * detail is re-read on a slow backstop until the order is terminal, plus ONE
 * further read after a terminal transition this page watched happen, so a
 * sibling fact of the same burst still lands (#7 D2/D7, ported).
 */
export const STALE_STATUS_BACKSTOP_MS = 5_000;

async function fetchOrderDetail(orderId: string): Promise<OrderDetailResult> {
  const { status, data, headers } = await apiRequest<OrderDetail | ProjectionPending>(`/api/orders/${encodeURIComponent(orderId)}`);
  if (status === 202) {
    const pending = data as ProjectionPending;
    return { kind: 'pending', pending, retryAfterMs: pendingRetryDelayMs(headers.get('retry-after'), pending) };
  }
  return { kind: 'ready', detail: data as OrderDetail };
}

/**
 * `GET /api/orders/{id}` (R54/R55). A `202` is a success with a waiting state,
 * never an error or a 404: the query keeps re-asking at the Gateway's own
 * suggested interval until the projection appears.
 */
export function useOrderDetail(orderId: string, backstopMs: number = STALE_STATUS_BACKSTOP_MS) {
  // `refetchInterval` below may be evaluated any number of times per update, so
  // it only ever makes IDEMPOTENT notes: "this page saw the order live", and
  // "the terminal status was first seen after N completed reads". The one
  // catch-up read is owed exactly while no read has completed since then.
  const completedReads = useRef(0);
  const observedNonTerminal = useRef(false);
  const terminalSeenAfterReads = useRef<number | undefined>(undefined);

  useEffect(() => {
    completedReads.current = 0;
    observedNonTerminal.current = false;
    terminalSeenAfterReads.current = undefined;
  }, [orderId]);

  return useQuery({
    queryKey: orderDetailKey(orderId),
    queryFn: async () => {
      const result = await fetchOrderDetail(orderId);
      completedReads.current += 1;
      return result;
    },
    refetchInterval: (query) => {
      const data = query.state.data;
      if (!data) return false;
      if (data.kind === 'pending') return data.retryAfterMs;
      if (!isTerminalStatus(data.detail.status)) {
        observedNonTerminal.current = true;
        return backstopMs;
      }
      if (!observedNonTerminal.current) return false;
      terminalSeenAfterReads.current ??= completedReads.current;
      return completedReads.current === terminalSeenAfterReads.current ? backstopMs : false;
    },
    refetchIntervalInBackground: false,
  });
}

/** Cache patchers the live stream drives. */
export function useOrderDetailPatchers(orderId: string) {
  const queryClient = useQueryClient();
  const onOrderUpdated = useCallback(
    (update: OrderStreamUpdate) => queryClient.setQueryData<OrderDetailResult>(orderDetailKey(orderId), (current) => applyOrderUpdate(current, update)),
    [queryClient, orderId],
  );
  const onTimelineAppended = useCallback(
    (entry: TimelineStreamEntry) => queryClient.setQueryData<OrderDetailResult>(orderDetailKey(orderId), (current) => applyTimelineAppended(current, entry)),
    [queryClient, orderId],
  );
  const onResync = useCallback(() => {
    void queryClient.invalidateQueries({ queryKey: orderDetailKey(orderId) });
  }, [queryClient, orderId]);
  return { onOrderUpdated, onTimelineAppended, onResync };
}
