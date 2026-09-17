import { NextRequest, NextResponse } from 'next/server';
import { SESSION_COOKIE, writeSession, type OtcSession } from '@/server/session';

export const TEST_TOKEN = 'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJvcGVyYXRvciJ9.c2lnbmF0dXJlLW9mLXRoZS10ZXN0LXRva2Vu';

/** A real sealed session cookie value, produced by the same code the login route uses. */
export async function sealedSessionCookie(overrides: Partial<OtcSession> = {}): Promise<string> {
  const response = NextResponse.json({});
  await writeSession(response, { accessToken: TEST_TOKEN, expiresAt: Date.now() + 3_600_000, username: 'operator', displayName: 'Operator', roles: ['operator'], ...overrides });
  const value = response.cookies.get(SESSION_COOKIE)?.value;
  if (!value) throw new Error('no session cookie was written');
  return value;
}

export async function signedInRequest(url: string, init: { method?: string; body?: string; headers?: Record<string, string>; signal?: AbortSignal } = {}): Promise<NextRequest> {
  const cookie = await sealedSessionCookie();
  return new NextRequest(url, { method: init.method ?? 'GET', body: init.body, signal: init.signal, headers: { cookie: `${SESSION_COOKIE}=${cookie}`, ...init.headers } });
}
