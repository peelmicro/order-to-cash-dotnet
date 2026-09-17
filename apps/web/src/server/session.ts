import 'server-only';
import { sealData, unsealData } from 'iron-session';
import type { NextResponse } from 'next/server';
import type { SessionInfo } from '@/lib/api-types';
import { cookieSecure, sessionPassword } from '@/server/config';

/**
 * The session this server holds ON BEHALF OF the browser. `accessToken` is the
 * real Gateway-issued JWT; it exists only inside the sealed (encrypted and
 * authenticated) value of an `httpOnly` cookie, so no client-side script can
 * read it and the raw cookie never carries it in the clear. Nothing this app
 * returns to the browser in a response BODY ever includes it — see
 * `toSessionInfo`, the only shape the browser is given.
 */
export interface OtcSession {
  accessToken: string;
  /** Epoch milliseconds after which the token is treated as expired without asking the Gateway. */
  expiresAt: number;
  username: string;
  displayName?: string;
  roles: string[];
}

export const SESSION_COOKIE = 'otc_session';
const MAX_AGE_SECONDS = 60 * 60 * 12;

/** Anything that can read a cookie by name — a `NextRequest`, or `{ cookies: await cookies() }` in a server component. */
export interface CookieSource {
  cookies: { get(name: string): { value: string } | undefined };
}

export async function readSession(request: CookieSource, now: number = Date.now()): Promise<OtcSession | undefined> {
  const sealed = request.cookies.get(SESSION_COOKIE)?.value;
  if (!sealed) return undefined;
  try {
    const session = await unsealData<Partial<OtcSession>>(sealed, { password: sessionPassword(), ttl: MAX_AGE_SECONDS });
    if (!session.accessToken || typeof session.expiresAt !== 'number' || session.expiresAt <= now || !session.username) {
      return undefined;
    }
    return { accessToken: session.accessToken, expiresAt: session.expiresAt, username: session.username, displayName: session.displayName, roles: session.roles ?? [] };
  } catch {
    return undefined;
  }
}

export async function writeSession(response: NextResponse, session: OtcSession): Promise<void> {
  const sealed = await sealData(session, { password: sessionPassword(), ttl: MAX_AGE_SECONDS });
  response.cookies.set(SESSION_COOKIE, sealed, {
    httpOnly: true,
    secure: cookieSecure(),
    sameSite: 'lax',
    path: '/',
    maxAge: MAX_AGE_SECONDS,
  });
}

export function clearSession(response: NextResponse): void {
  response.cookies.set(SESSION_COOKIE, '', { httpOnly: true, secure: cookieSecure(), sameSite: 'lax', path: '/', maxAge: 0 });
}

/** The ONLY session shape any response body carries — identity, never the token. */
export function toSessionInfo(session: OtcSession | undefined): SessionInfo {
  if (!session) return { authenticated: false };
  return { authenticated: true, username: session.username, displayName: session.displayName, roles: session.roles };
}
