'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import type { OrderStreamUpdate, TimelineStreamEntry } from '@/lib/api-types';
import { OrderStreamClient, type EventSourceFactory, type EventSourceLike, type StreamConnectionStatus } from '@/lib/order-stream-client';

export interface OrderStreamHandlers {
  onOrderUpdated: (update: OrderStreamUpdate) => void;
  onTimelineAppended: (entry: TimelineStreamEntry) => void;
  onResync: () => void;
}

/** The browser's own `EventSource`, pointed at THIS origin's proxy — never at the Gateway. */
export const browserEventSourceFactory: EventSourceFactory = (url) => new EventSource(url) as unknown as EventSourceLike;

export interface UseOrderStreamOptions {
  orderId: string;
  /** Start only once the page has a projected document to patch (never while the answer is still 202). */
  enabled: boolean;
  /** `eventId`s already rendered from the snapshot the stream starts from. */
  seedEventIds: () => readonly string[];
  handlers: OrderStreamHandlers;
  factory?: EventSourceFactory;
}

/**
 * React wrapper around `OrderStreamClient`: opens one stream per mounted order
 * page, exposes its connection status, closes it on unmount (which is what
 * makes the proxy abort its upstream Gateway request), and offers a manual
 * `retry` once the client has given up.
 */
export function useOrderStream({ orderId, enabled, seedEventIds, handlers, factory }: UseOrderStreamOptions) {
  const [status, setStatus] = useState<StreamConnectionStatus>('connecting');
  const clientRef = useRef<OrderStreamClient | null>(null);
  const handlersRef = useRef(handlers);
  const seedRef = useRef(seedEventIds);
  const factoryRef = useRef(factory);

  useEffect(() => {
    handlersRef.current = handlers;
    seedRef.current = seedEventIds;
    factoryRef.current = factory;
  });

  const open = useCallback(() => {
    clientRef.current?.disconnect();
    const client = new OrderStreamClient(
      `/api/orders/stream?orderId=${encodeURIComponent(orderId)}`,
      {
        onOrderUpdated: (update) => handlersRef.current.onOrderUpdated(update),
        onTimelineAppended: (entry) => handlersRef.current.onTimelineAppended(entry),
        onResync: () => handlersRef.current.onResync(),
        onStatusChange: setStatus,
      },
      (url) => (factoryRef.current ?? browserEventSourceFactory)(url),
    );
    client.seedSeenEventIds(seedRef.current());
    client.connect();
    clientRef.current = client;
    // The stream depends on the ORDER only. The factory is read through a ref:
    // in the production build the minifier inlined the default factory into
    // the parameter list (`factory: i = e => new EventSource(e)`), creating a
    // new function on every render — as a dependency here it reopened the
    // stream on every render, hundreds of connections a second. Found in a
    // real browser against `next start`; guarded by use-order-stream.test.tsx
    // › *a factory that is a NEW function on every render still opens ONE stream*.
  }, [orderId]);

  useEffect(() => {
    if (!enabled) return undefined;
    open();
    return () => {
      clientRef.current?.disconnect();
      clientRef.current = null;
    };
  }, [enabled, open]);

  return { status, retry: open };
}
