import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import type { AddressInfo } from 'node:net';

export interface RecordedRequest {
  method: string;
  url: string;
  headers: IncomingMessage['headers'];
  body: string;
}

export type FakeHandler = (request: RecordedRequest, response: ServerResponse) => void | Promise<void>;

/**
 * A real HTTP server standing in for the Gateway, so route handlers are tested
 * over a real socket: what is asserted is what actually went over the wire.
 */
export class FakeGateway {
  readonly requests: RecordedRequest[] = [];
  private readonly routes = new Map<string, FakeHandler>();
  private server: Server | undefined;
  baseUrl = '';

  on(method: string, pathname: string, handler: FakeHandler): this {
    this.routes.set(`${method} ${pathname}`, handler);
    return this;
  }

  async start(): Promise<string> {
    this.server = createServer((req, res) => {
      const chunks: Buffer[] = [];
      req.on('data', (chunk: Buffer) => chunks.push(chunk));
      req.on('end', () => {
        const recorded: RecordedRequest = { method: req.method ?? 'GET', url: req.url ?? '/', headers: req.headers, body: Buffer.concat(chunks).toString('utf8') };
        this.requests.push(recorded);
        const pathname = new URL(recorded.url, 'http://x').pathname;
        const handler = this.routes.get(`${recorded.method} ${pathname}`);
        if (!handler) {
          res.writeHead(599, { 'Content-Type': 'text/plain' });
          res.end(`fake gateway: no route for ${recorded.method} ${pathname}`);
          return;
        }
        void handler(recorded, res);
      });
    });
    await new Promise<void>((resolve) => this.server!.listen(0, '127.0.0.1', resolve));
    this.baseUrl = `http://127.0.0.1:${(this.server.address() as AddressInfo).port}`;
    return this.baseUrl;
  }

  async stop(): Promise<void> {
    if (!this.server) return;
    this.server.closeAllConnections();
    await new Promise<void>((resolve) => this.server!.close(() => resolve()));
    this.server = undefined;
  }
}

export function sendJson(res: ServerResponse, status: number, body: unknown, headers: Record<string, string> = {}): void {
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', ...headers });
  res.end(JSON.stringify(body));
}

export function sendRaw(res: ServerResponse, status: number, body: string, headers: Record<string, string>): void {
  res.writeHead(status, headers);
  res.end(body);
}
