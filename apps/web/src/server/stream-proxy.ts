import 'server-only';
import { gatewayBaseUrl } from '@/server/config';

/**
 * The upstream half of `GET /api/orders/stream`: opens the Gateway's
 * `GET /orders/stream` with the bearer token attached server-side and the
 * browser's `Last-Event-ID` forwarded verbatim (so the Gateway's bounded replay
 * buffer can resume it — openapi.yaml "Reconnection").
 */
export interface UpstreamStreamOptions {
  token: string;
  orderId?: string;
  lastEventId?: string;
  signal: AbortSignal;
  baseUrl?: string;
}

export function openUpstreamOrderStream(options: UpstreamStreamOptions): Promise<Response> {
  const url = new URL('/orders/stream', options.baseUrl ?? gatewayBaseUrl());
  if (options.orderId) url.searchParams.set('orderId', options.orderId);
  const headers: Record<string, string> = { Accept: 'text/event-stream', Authorization: `Bearer ${options.token}` };
  if (options.lastEventId) headers['Last-Event-ID'] = options.lastEventId;
  return fetch(url, { headers, signal: options.signal, cache: 'no-store' });
}

/**
 * Wraps the upstream body in a stream this route handler owns, so that when
 * the BROWSER goes away — Next cancels the response body, or the request's own
 * signal aborts — the upstream fetch is aborted too. Without this every closed
 * tab would leave a Gateway SSE connection (and a StreamHub subscription) open.
 * Pull-based: nothing is read from upstream faster than the client consumes it,
 * and each upstream chunk is handed on the moment it arrives — never collected.
 */
/**
 * An SSE comment line, which every EventSource ignores. Next writes a route
 * handler's status and headers together with the FIRST body chunk, so without
 * this the browser would not see the 200 (and fire `open`) until the Gateway's
 * first frame — found by tests-integration/stream-proxy.test.ts, where a
 * response with no body yet never produced headers at all.
 */
export const STREAM_PREAMBLE = ': connected\n\n';

export function relayStream(upstreamBody: ReadableStream<Uint8Array>, upstreamAbort: AbortController): ReadableStream<Uint8Array> {
  const reader = upstreamBody.getReader();
  return new ReadableStream<Uint8Array>({
    start(controller) {
      controller.enqueue(new TextEncoder().encode(STREAM_PREAMBLE));
    },
    async pull(controller) {
      try {
        const { done, value } = await reader.read();
        if (done) {
          controller.close();
        } else {
          controller.enqueue(value);
        }
      } catch (error) {
        controller.error(error);
      }
    },
    cancel(reason) {
      upstreamAbort.abort(reason);
      reader.cancel(reason).catch(() => undefined);
    },
  });
}

/**
 * Response headers of the proxied stream. `no-transform` and
 * `X-Accel-Buffering: no` are for intermediaries (compressing or buffering
 * reverse proxies). Next 16.3.5's own `next start` neither compressed nor
 * buffered this stream even without `no-transform` (progress/impl_web_app.md,
 * probes P1/P1b) — the behaviour that DOES hold frames back is covered by the
 * preamble above, and tests-integration/ proves delivery on the real server.
 */
export const STREAM_RESPONSE_HEADERS: Readonly<Record<string, string>> = {
  'Content-Type': 'text/event-stream; charset=utf-8',
  'Cache-Control': 'no-cache, no-transform',
  Connection: 'keep-alive',
  'X-Accel-Buffering': 'no',
};
