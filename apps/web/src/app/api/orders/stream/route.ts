import { type NextRequest } from 'next/server';
import { gatewayUnreachable, notSignedIn, relay } from '@/server/gateway';
import { readSession } from '@/server/session';
import { openUpstreamOrderStream, relayStream, STREAM_RESPONSE_HEADERS } from '@/server/stream-proxy';

// A long-lived, per-request response: never prerendered, never cached.
export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

/**
 * `GET /api/orders/stream` — the browser's `EventSource` connects HERE, never to
 * the Gateway: the bearer token is attached server-side. The browser's
 * `Last-Event-ID` (sent automatically on reconnect) is forwarded verbatim, the
 * upstream bytes are handed on as they arrive (tests-integration/ proves this
 * against the production build, compressed or not), and a client disconnect
 * aborts the upstream request.
 */
export async function GET(request: NextRequest): Promise<Response> {
  const session = await readSession(request);
  if (!session) return notSignedIn();

  const upstreamAbort = new AbortController();
  const onClientGone = () => upstreamAbort.abort();
  if (request.signal.aborted) onClientGone();
  request.signal.addEventListener('abort', onClientGone, { once: true });

  let upstream: Response;
  try {
    upstream = await openUpstreamOrderStream({
      token: session.accessToken,
      orderId: request.nextUrl.searchParams.get('orderId') ?? undefined,
      lastEventId: request.headers.get('last-event-id') ?? undefined,
      signal: upstreamAbort.signal,
    });
  } catch (error) {
    return gatewayUnreachable(error);
  }

  if (!upstream.ok || !upstream.body) {
    return relay(upstream);
  }

  return new Response(relayStream(upstream.body, upstreamAbort), { status: 200, headers: STREAM_RESPONSE_HEADERS });
}
