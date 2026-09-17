// The SSE proxy, proven against the PRODUCTION build served by a real
// `next start` — the framework behaviour no unit test can see:
//
//   * the response (status and headers) arrives BEFORE the Gateway's first
//     frame — Next writes a route handler's headers with its first body chunk,
//     so without the proxy's SSE-comment preamble the browser would see
//     nothing at all until the first frame;
//   * frames reach the browser as they are written, not batched — tested in
//     LOCKSTEP (the Gateway writes the next frame only after the client has
//     received the previous one, so any buffering layer stalls the test
//     instead of passing it), with the `Accept-Encoding` a browser sends;
//   * `Last-Event-ID` crosses the real server;
//   * a client that goes away makes the proxy close its Gateway connection;
//   * a real EventSource resumes through the proxy after the Gateway drops it.
//
// Needs `pnpm build` first (./quality.sh runs it). The Gateway is a local HTTP
// server; nothing else is required.
import { spawn, type ChildProcess } from 'node:child_process';
import { existsSync } from 'node:fs';
import { createServer, request as httpRequest, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';
import path from 'node:path';
import { createBrotliDecompress, createGunzip, createInflate, constants as zlib } from 'node:zlib';
import { EventSource } from 'eventsource';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';

const appRoot = path.resolve(import.meta.dirname, '..');
const TOKEN = 'integration-token';

interface Upstream {
  req: IncomingMessage;
  res: ServerResponse;
  closed: boolean;
}

const upstreams: Upstream[] = [];
let onStream: (upstream: Upstream) => void = () => undefined;
let gateway: Server;
let web: ChildProcess;
let webOutput = '';
let webUrl = '';
let cookie = '';

function withoutSessionPassword(env: NodeJS.ProcessEnv): NodeJS.ProcessEnv {
  const copy = { ...env };
  delete copy.WEB_SESSION_PASSWORD;
  return copy;
}

async function freePort(): Promise<number> {
  const probe = createServer();
  await new Promise<void>((resolve) => probe.listen(0, '127.0.0.1', resolve));
  const { port } = probe.address() as AddressInfo;
  await new Promise<void>((resolve) => probe.close(() => resolve()));
  return port;
}

async function waitFor<T>(what: string, probe: () => T | undefined | false, timeoutMs = 5000): Promise<T> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const value = probe();
    if (value) return value;
    if (Date.now() > deadline) throw new Error(`timed out after ${timeoutMs} ms waiting for ${what}`);
    await new Promise((resolve) => setTimeout(resolve, 20));
  }
}

beforeAll(async () => {
  if (!existsSync(path.join(appRoot, '.next', 'BUILD_ID'))) {
    throw new Error('No production build — run `pnpm build` before `pnpm test:integration`.');
  }

  gateway = createServer((req, res) => {
    if (req.url === '/auth/login') {
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ accessToken: TOKEN, tokenType: 'Bearer', expiresIn: 3600 }));
      return;
    }
    if (req.url === '/auth/me') {
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ username: 'operator', roles: ['operator'] }));
      return;
    }
    if (req.url?.startsWith('/orders/stream')) {
      const upstream: Upstream = { req, res, closed: false };
      upstreams.push(upstream);
      res.on('close', () => {
        upstream.closed = true;
      });
      res.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache', Connection: 'keep-alive' });
      res.flushHeaders();
      onStream(upstream);
      return;
    }
    res.writeHead(404);
    res.end();
  });
  await new Promise<void>((resolve) => gateway.listen(0, '127.0.0.1', resolve));
  const gatewayUrl = `http://127.0.0.1:${(gateway.address() as AddressInfo).port}`;

  const port = await freePort();
  webUrl = `http://127.0.0.1:${port}`;
  web = spawn(process.execPath, [path.join(appRoot, 'node_modules', 'next', 'dist', 'bin', 'next'), 'start', '--port', String(port), '--hostname', '127.0.0.1'], {
    cwd: appRoot,
    // No WEB_SESSION_PASSWORD on purpose: the server runs on its generated
    // fallback key, which must be ONE key across every bundle of the process.
    env: { ...withoutSessionPassword(process.env), GATEWAY_BASE_URL: gatewayUrl, NODE_ENV: 'production', NEXT_TELEMETRY_DISABLED: '1' },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  web.stdout?.on('data', (chunk: Buffer) => (webOutput += chunk.toString()));
  web.stderr?.on('data', (chunk: Buffer) => (webOutput += chunk.toString()));

  const deadline = Date.now() + 60_000;
  for (;;) {
    try {
      const response = await fetch(`${webUrl}/login`);
      if (response.ok) break;
    } catch {
      // not listening yet
    }
    if (Date.now() > deadline || web.exitCode !== null) throw new Error(`next start did not come up:\n${webOutput}`);
    await new Promise((resolve) => setTimeout(resolve, 250));
  }

  const login = await fetch(`${webUrl}/api/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username: 'operator', password: 'pw' }) });
  expect(login.status).toBe(200);
  cookie = (login.headers.get('set-cookie') ?? '').split(';')[0] ?? '';
  expect(cookie).toMatch(/^otc_session=.+/);
});

afterAll(async () => {
  web?.kill('SIGTERM');
  gateway?.closeAllConnections();
  await new Promise<void>((resolve) => (gateway ? gateway.close(() => resolve()) : resolve()));
});

function frame(id: string, event: string, data: unknown): string {
  return `id: ${id}\nevent: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
}

/** Opens the proxied stream the way a browser does (compression offered) and decodes whatever encoding comes back, incrementally. */
function openBrowserLikeStream(extraHeaders: Record<string, string> = {}): Promise<{ response: IncomingMessage; received: () => string; destroy: () => void }> {
  return new Promise((resolve, reject) => {
    const noHeaders = setTimeout(() => {
      req.destroy();
      reject(new Error('no response status/headers within 3000 ms while the Gateway had not yet written a frame — the proxy is holding the response until the first upstream chunk'));
    }, 3000);
    const req = httpRequest(`${webUrl}/api/orders/stream?orderId=9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f`, {
      headers: { Cookie: cookie, Accept: 'text/event-stream', 'Accept-Encoding': 'gzip, deflate, br', ...extraHeaders },
    });
    req.on('response', (response) => {
      clearTimeout(noHeaders);
      let text = '';
      const encoding = response.headers['content-encoding'];
      const decoder =
        encoding === 'gzip'
          ? createGunzip({ flush: zlib.Z_SYNC_FLUSH, finishFlush: zlib.Z_SYNC_FLUSH })
          : encoding === 'deflate'
            ? createInflate({ flush: zlib.Z_SYNC_FLUSH, finishFlush: zlib.Z_SYNC_FLUSH })
            : encoding === 'br'
              ? createBrotliDecompress({ flush: zlib.BROTLI_OPERATION_FLUSH, finishFlush: zlib.BROTLI_OPERATION_FLUSH })
              : undefined;
      if (decoder) {
        response.pipe(decoder);
        decoder.on('data', (chunk: Buffer) => (text += chunk.toString()));
        decoder.on('error', () => undefined);
      } else {
        response.on('data', (chunk: Buffer) => (text += chunk.toString()));
      }
      response.on('error', () => undefined);
      resolve({ response, received: () => text, destroy: () => req.destroy() });
    });
    req.on('error', (error) => {
      clearTimeout(noHeaders);
      reject(error);
    });
    req.end();
  });
}

describe('the session on the production server', () => {
  it('a session sealed by the login route is accepted by the pages (one key across route handlers and server components)', async () => {
    for (const path of ['/orders', '/orders/place', '/stock', '/billing', '/orders/9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f']) {
      const response = await fetch(`${webUrl}${path}`, { headers: { Cookie: cookie }, redirect: 'manual' });
      expect(`${path} → ${response.status} ${response.headers.get('location') ?? ''}`.trim()).toBe(`${path} → 200`);
    }
    expect(webOutput.match(/WEB_SESSION_PASSWORD is not set/g) ?? [], 'the fallback key is generated once per process').toHaveLength(1);
  });

  it('a page without a session redirects to /login', async () => {
    const response = await fetch(`${webUrl}/orders`, { redirect: 'manual' });
    expect([response.status, new URL(response.headers.get('location') ?? '', webUrl).pathname]).toEqual([307, '/login']);
  });
});

describe('GET /api/orders/stream on the production server', () => {
  it('delivers each frame as it is written — in lockstep, with compression offered — and never lets the proxy hold one back', async () => {
    upstreams.length = 0;
    onStream = () => undefined;
    const client = await openBrowserLikeStream();
    try {
      expect(client.response.statusCode).toBe(200);
      expect(client.response.headers['content-type']).toBe('text/event-stream; charset=utf-8');
      expect(client.response.headers['cache-control']).toBe('no-cache, no-transform');
      const upstream = await waitFor('the upstream request', () => upstreams[0]);
      expect(upstream.req.headers.authorization).toBe(`Bearer ${TOKEN}`);

      for (let i = 1; i <= 5; i += 1) {
        const text = frame(`c${i}`, 'order.updated', { eventId: `e${i}`, orderId: '9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f', status: 'confirmed', occurredAt: '2026-09-16T10:00:00.000Z' });
        upstream.res.write(text);
        // The next frame is not written until this one has reached the client.
        await waitFor(`frame ${i} to reach the client (received so far: ${JSON.stringify(client.received())}; content-encoding: ${String(client.response.headers['content-encoding'])})`, () => client.received().includes(`id: c${i}\n`), 3000);
      }
      expect(client.received()).not.toContain('id: c6');
    } finally {
      client.destroy();
    }
  });

  it('forwards Last-Event-ID through the real server', async () => {
    upstreams.length = 0;
    const client = await openBrowserLikeStream({ 'Last-Event-ID': '1755511234901-18' });
    try {
      const upstream = await waitFor('the upstream request', () => upstreams[0]);
      expect(upstream.req.headers['last-event-id']).toBe('1755511234901-18');
      expect(upstream.req.url).toBe('/orders/stream?orderId=9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f');
    } finally {
      client.destroy();
    }
  });

  it('closes its Gateway connection when the browser goes away', async () => {
    upstreams.length = 0;
    onStream = (upstream) => upstream.res.write(frame('c1', 'order.updated', { eventId: 'e1' }));
    const client = await openBrowserLikeStream();
    const upstream = await waitFor('the upstream request', () => upstreams[0]);
    await waitFor('the first frame', () => client.received().includes('id: c1'));
    expect(upstream.closed).toBe(false);

    client.destroy();

    await waitFor('the proxy to close its Gateway connection after the client left', () => upstream.closed, 5000);
  });

  it('a real EventSource, dropped by the Gateway, reconnects THROUGH the proxy and resumes with Last-Event-ID', async () => {
    upstreams.length = 0;
    onStream = (upstream) => {
      upstream.res.write('retry: 50\n\n');
      if (upstreams.length === 1) {
        upstream.res.write(frame('cursor-1', 'order.updated', { eventId: 'e1' }));
        setTimeout(() => upstream.res.destroy(), 100);
      } else {
        upstream.res.write(frame('cursor-2', 'order.updated', { eventId: 'e2' }));
      }
    };
    const received: string[] = [];
    const source = new EventSource(`${webUrl}/api/orders/stream?orderId=9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f`, {
      fetch: (input, init) => fetch(input, { ...init, headers: { ...init.headers, Cookie: cookie } }),
    });
    source.addEventListener('order.updated', (event) => received.push(JSON.parse(event.data).eventId));
    try {
      await waitFor('both frames through the proxy', () => received.length >= 2, 10_000);
      expect(received).toEqual(['e1', 'e2']);
      expect(upstreams.map((u) => u.req.headers['last-event-id'])).toEqual([undefined, 'cursor-1']);
    } finally {
      source.close();
    }
  });

  it('refuses the stream without a session, and never opens an upstream connection', async () => {
    upstreams.length = 0;
    const response = await fetch(`${webUrl}/api/orders/stream`);
    expect(response.status).toBe(401);
    expect(await response.json()).toMatchObject({ code: 'UNAUTHENTICATED' });
    expect(upstreams).toHaveLength(0);
  });
});
