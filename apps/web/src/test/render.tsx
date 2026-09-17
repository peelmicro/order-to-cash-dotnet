import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, type RenderResult } from '@testing-library/react';
import type { ReactElement } from 'react';
import { setApiFetch } from '@/lib/api-client';

export function testQueryClient(): QueryClient {
  return new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: Infinity }, mutations: { retry: false } } });
}

export function renderWithQuery(ui: ReactElement, queryClient: QueryClient = testQueryClient()): RenderResult & { queryClient: QueryClient } {
  return { ...render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>), queryClient };
}

export interface ApiCall {
  method: string;
  path: string;
  query: URLSearchParams;
  body: unknown;
  headers: Headers;
}

export type RouteHandler = (call: ApiCall) => Response | Promise<Response>;

/**
 * Routes the app's browser client (`apiRequest`) to per-path handlers, and
 * records every call. An unrouted call fails the test loudly.
 */
export function routeApi(routes: Record<string, RouteHandler>): ApiCall[] {
  const calls: ApiCall[] = [];
  setApiFetch(async (input, init) => {
    const url = new URL(input, 'http://web.test');
    const method = init?.method ?? 'GET';
    const text = typeof init?.body === 'string' ? init.body : undefined;
    const call: ApiCall = { method, path: url.pathname, query: url.searchParams, body: text ? JSON.parse(text) : undefined, headers: new Headers(init?.headers) };
    calls.push(call);
    const handler = routes[`${method} ${url.pathname}`];
    if (!handler) throw new Error(`test: no route for ${method} ${url.pathname}`);
    return handler(call);
  });
  return calls;
}

export function json(body: unknown, status = 200, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } });
}

/** A response that does not arrive until `release` is called — for asserting loading states. */
export function deferred(): { promise: Promise<Response>; release: (response: Response) => void } {
  let release!: (response: Response) => void;
  const promise = new Promise<Response>((resolve) => {
    release = resolve;
  });
  return { promise, release };
}
