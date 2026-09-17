import { ApiError, readProblem } from '@/lib/problem';

/**
 * The browser's only HTTP client. It talks to this app's own origin
 * (`/api/*` route handlers) and never to the Gateway: the bearer token lives
 * server-side, so there is nothing here that could attach one.
 */
export interface ApiResponse<T> {
  status: number;
  data: T;
  headers: Headers;
}

export type FetchLike = (input: string, init?: RequestInit) => Promise<Response>;

let fetchImpl: FetchLike = (input, init) => fetch(input, init);

/** Test seam: route the client through something other than the global `fetch` (e.g. straight into a route handler). */
export function setApiFetch(next: FetchLike | undefined): void {
  fetchImpl = next ?? ((input, init) => fetch(input, init));
}

export type QueryValue = string | number | boolean | undefined | null | readonly (string | number)[];

export function withQuery(path: string, query?: Record<string, QueryValue>): string {
  if (!query) return path;
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '' || value === false) continue;
    if (Array.isArray(value)) {
      for (const item of value) params.append(key, String(item));
    } else {
      params.append(key, String(value));
    }
  }
  const qs = params.toString();
  return qs ? `${path}?${qs}` : path;
}

export async function apiRequest<T>(path: string, init: RequestInit & { query?: Record<string, QueryValue>; json?: unknown } = {}): Promise<ApiResponse<T>> {
  const { query, json, headers, ...rest } = init;
  const requestHeaders = new Headers(headers);
  requestHeaders.set('Accept', 'application/json');
  let body = rest.body;
  if (json !== undefined) {
    requestHeaders.set('Content-Type', 'application/json');
    body = JSON.stringify(json);
  }
  const response = await fetchImpl(withQuery(path, query), { ...rest, body, headers: requestHeaders, credentials: 'same-origin' });
  if (!response.ok) {
    throw new ApiError(response.status, await readProblem(response));
  }
  const text = await response.text();
  const data = (text ? JSON.parse(text) : undefined) as T;
  return { status: response.status, data, headers: response.headers };
}

export async function apiGet<T>(path: string, query?: Record<string, QueryValue>): Promise<T> {
  return (await apiRequest<T>(path, { method: 'GET', query })).data;
}
