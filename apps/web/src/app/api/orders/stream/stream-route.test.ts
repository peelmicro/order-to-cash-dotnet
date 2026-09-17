// @vitest-environment node
//
// The SSE proxy route handler, called directly, against a real HTTP server
// playing the Gateway's `GET /orders/stream`. The production-build behaviour
// (buffering, compression, a real client disconnect through Next's server) is
// proven separately in tests-integration/stream-proxy.test.ts.
import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';
import { NextRequest } from 'next/server';
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { gatewayFixture } from '@/test/gateway-fixtures';
import { signedInRequest, TEST_TOKEN } from '@/test/session-cookie';
import { GET } from './route';

interface Upstream {
  req: IncomingMessage;
  res: ServerResponse;
  closed: boolean;
}

const upstreams: Upstream[] = [];
let mode: 'stream' | 'unauthorized' = 'stream';
let server: Server;
const APP = 'http://web.test';

beforeAll(async () => {
  server = createServer((req, res) => {
    const entry: Upstream = { req, res, closed: false };
    upstreams.push(entry);
    res.on('close', () => {
      entry.closed = true;
    });
    if (mode === 'unauthorized') {
      const fixture = gatewayFixture('orders-without-token-401');
      res.writeHead(fixture.status, fixture.headers);
      res.end(fixture.body);
      return;
    }
    res.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache', Connection: 'keep-alive' });
    res.write('event: stream.ready\ndata: {"cursor":"c0","resumed":true}\n\n');
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  process.env.GATEWAY_BASE_URL = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  process.env.WEB_SESSION_PASSWORD = 'test-session-password-at-least-32-characters-long';
});

afterAll(async () => {
  server.closeAllConnections();
  await new Promise<void>((resolve) => server.close(() => resolve()));
});

afterEach(() => {
  mode = 'stream';
  for (const upstream of upstreams) upstream.res.destroy();
  upstreams.length = 0;
});

const decoder = new TextDecoder();

describe('GET /api/orders/stream — the SSE proxy', () => {
  it('without a session: a 401 problem, and no upstream connection is opened', async () => {
    const response = await GET(new NextRequest(`${APP}/api/orders/stream`));
    expect(response.status).toBe(401);
    expect(await response.json()).toMatchObject({ code: 'UNAUTHENTICATED' });
    expect(upstreams).toHaveLength(0);
  });

  it('attaches the bearer token and forwards orderId and the browser\'s Last-Event-ID verbatim', async () => {
    const response = await GET(await signedInRequest(`${APP}/api/orders/stream?orderId=9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f`, { headers: { 'last-event-id': '1755511234901-18' } }));
    expect(response.status).toBe(200);
    const upstream = upstreams[0]!;
    expect(upstream.req.url).toBe('/orders/stream?orderId=9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f');
    expect(upstream.req.headers.authorization).toBe(`Bearer ${TEST_TOKEN}`);
    expect(upstream.req.headers['last-event-id']).toBe('1755511234901-18');
    expect(upstream.req.headers.accept).toBe('text/event-stream');
    expect(upstream.req.headers.cookie).toBeUndefined();
    await response.body?.cancel();
  });

  it('sends no Last-Event-ID and no orderId when the browser sent none', async () => {
    const response = await GET(await signedInRequest(`${APP}/api/orders/stream`));
    expect(upstreams[0]!.req.url).toBe('/orders/stream');
    expect(upstreams[0]!.req.headers['last-event-id']).toBeUndefined();
    await response.body?.cancel();
  });

  it('answers as an event stream that no intermediary may transform or buffer', async () => {
    const response = await GET(await signedInRequest(`${APP}/api/orders/stream`));
    expect(response.headers.get('content-type')).toBe('text/event-stream; charset=utf-8');
    expect(response.headers.get('cache-control')).toBe('no-cache, no-transform');
    expect(response.headers.get('x-accel-buffering')).toBe('no');
    await response.body?.cancel();
  });

  it('hands each upstream frame on as soon as it is written, before the next one exists', async () => {
    const response = await GET(await signedInRequest(`${APP}/api/orders/stream`));
    const reader = response.body!.getReader();
    expect(decoder.decode((await reader.read()).value)).toBe(': connected\n\n');
    const first = decoder.decode((await reader.read()).value);
    expect(first).toBe('event: stream.ready\ndata: {"cursor":"c0","resumed":true}\n\n');

    upstreams[0]!.res.write('id: c1\nevent: order.updated\ndata: {"eventId":"e1"}\n\n');
    const second = decoder.decode((await reader.read()).value);
    expect(second).toBe('id: c1\nevent: order.updated\ndata: {"eventId":"e1"}\n\n');
    await reader.cancel();
  });

  it('closes the upstream Gateway connection when the browser stops reading (response body cancelled)', async () => {
    const response = await GET(await signedInRequest(`${APP}/api/orders/stream`));
    const reader = response.body!.getReader();
    await reader.read();
    await reader.read();
    expect(upstreams[0]!.closed, 'the upstream Gateway connection is open while the browser reads').toBe(false);
    await reader.cancel();
    await vi.waitFor(() => expect(upstreams[0]!.closed, 'the upstream Gateway connection must close once the browser stops reading').toBe(true), { timeout: 3000 });
  });

  it('closes the upstream Gateway connection when the incoming request is aborted', async () => {
    const controller = new AbortController();
    const response = await GET(await signedInRequest(`${APP}/api/orders/stream`, { signal: controller.signal }));
    expect(response.status).toBe(200);
    expect(upstreams[0]!.closed, 'the upstream Gateway connection is open before the abort').toBe(false);
    controller.abort();
    await vi.waitFor(() => expect(upstreams[0]!.closed, 'the upstream Gateway connection must close once the incoming request aborts').toBe(true), { timeout: 3000 });
  });

  it('when the upstream stream ends, the proxied stream ends too', async () => {
    const response = await GET(await signedInRequest(`${APP}/api/orders/stream`));
    const reader = response.body!.getReader();
    await reader.read();
    await reader.read();
    upstreams[0]!.res.end();
    expect((await reader.read()).done).toBe(true);
  });

  it('a refusing Gateway (401) is relayed as its own problem document, not as a stream', async () => {
    mode = 'unauthorized';
    const response = await GET(await signedInRequest(`${APP}/api/orders/stream`));
    expect(response.status).toBe(401);
    expect(response.headers.get('content-type')).toBe('application/problem+json');
    expect(await response.text()).toBe(gatewayFixture('orders-without-token-401').body);
  });

  it('an unreachable Gateway is a 502 problem', async () => {
    const saved = process.env.GATEWAY_BASE_URL;
    process.env.GATEWAY_BASE_URL = 'http://127.0.0.1:1';
    try {
      const response = await GET(await signedInRequest(`${APP}/api/orders/stream`));
      expect(response.status).toBe(502);
      expect(await response.json()).toMatchObject({ code: 'GATEWAY_UNREACHABLE' });
    } finally {
      process.env.GATEWAY_BASE_URL = saved;
    }
  });
});
