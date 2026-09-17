import 'server-only';
import { NextResponse, type NextRequest } from 'next/server';
import type { Problem } from '@/lib/api-types';
import { gatewayBaseUrl } from '@/server/config';
import { clearSession, readSession, type OtcSession } from '@/server/session';

/**
 * The ONLY module that calls the Gateway. Every `/api/*` route handler goes
 * through it; the browser never does. It attaches the bearer token from the
 * sealed session and forwards the Gateway's answer VERBATIM — status, body
 * bytes and the headers a client acts on (`Content-Type`, `Retry-After`,
 * `Idempotent-Replay`) — so an RFC 9457 problem document reaches the browser
 * exactly as the Gateway wrote it, with no envelope around it.
 */

/** Response headers of the Gateway that the browser needs, forwarded as-is. */
const FORWARDED_RESPONSE_HEADERS = ['content-type', 'retry-after', 'idempotent-replay', 'x-correlation-id'] as const;

export interface GatewayCall {
  method?: 'GET' | 'POST';
  /** Raw query string INCLUDING the leading `?`, or empty. */
  search?: string;
  /** Raw request body text, forwarded unchanged. */
  body?: string;
  token?: string;
  headers?: Record<string, string>;
  signal?: AbortSignal;
}

export async function callGateway(path: string, call: GatewayCall = {}): Promise<Response> {
  const headers: Record<string, string> = { Accept: 'application/json', ...call.headers };
  if (call.token) headers.Authorization = `Bearer ${call.token}`;
  if (call.body !== undefined) headers['Content-Type'] = 'application/json';
  return fetch(`${gatewayBaseUrl()}${path}${call.search ?? ''}`, {
    method: call.method ?? 'GET',
    headers,
    body: call.body,
    signal: call.signal,
    cache: 'no-store',
    redirect: 'manual',
  });
}

/** A problem document minted by THIS app (not the Gateway) — only for failures that never reached the Gateway. */
export function localProblem(status: number, code: string, title: string, detail: string): NextResponse {
  const body: Problem = { type: 'about:blank', title, status, detail, code };
  return NextResponse.json(body, { status, headers: { 'Content-Type': 'application/problem+json' } });
}

export function notSignedIn(): NextResponse {
  const response = localProblem(401, 'UNAUTHENTICATED', 'Not signed in', 'You are not signed in, or your session has expired. Sign in again.');
  clearSession(response);
  return response;
}

export function gatewayUnreachable(error: unknown): NextResponse {
  const reason = error instanceof Error ? error.message : String(error);
  return localProblem(502, 'GATEWAY_UNREACHABLE', 'The Gateway could not be reached', `The Gateway at ${gatewayBaseUrl()} could not be reached (${reason}).`);
}

/** Copies an upstream Gateway response into a route-handler response, byte for byte. */
export async function relay(upstream: Response): Promise<NextResponse> {
  const body = await upstream.arrayBuffer();
  const headers = new Headers();
  for (const name of FORWARDED_RESPONSE_HEADERS) {
    const value = upstream.headers.get(name);
    if (value !== null) headers.set(name, value);
  }
  const response = new NextResponse(upstream.status === 204 ? null : body, { status: upstream.status, headers });
  if (upstream.status === 401) clearSession(response);
  return response;
}

export interface ProxyOptions {
  method?: 'GET' | 'POST';
  /** Forward the incoming query string (default true for GET). */
  forwardQuery?: boolean;
  /** Incoming request headers to forward to the Gateway, by name. */
  forwardRequestHeaders?: readonly string[];
}

/** The whole of an authenticated proxied call: session → Gateway → verbatim relay. */
export async function proxyToGateway(request: NextRequest, gatewayPath: string, options: ProxyOptions = {}): Promise<NextResponse> {
  const session: OtcSession | undefined = await readSession(request);
  if (!session) return notSignedIn();

  const method = options.method ?? 'GET';
  const headers: Record<string, string> = {};
  for (const name of options.forwardRequestHeaders ?? []) {
    const value = request.headers.get(name);
    if (value) headers[name] = value;
  }
  const forwardQuery = options.forwardQuery ?? method === 'GET';
  const body = method === 'POST' ? await request.text() : undefined;

  let upstream: Response;
  try {
    upstream = await callGateway(gatewayPath, {
      method,
      search: forwardQuery ? request.nextUrl.search : '',
      body: body === '' ? undefined : body,
      token: session.accessToken,
      headers,
      signal: request.signal,
    });
  } catch (error) {
    return gatewayUnreachable(error);
  }
  return relay(upstream);
}
