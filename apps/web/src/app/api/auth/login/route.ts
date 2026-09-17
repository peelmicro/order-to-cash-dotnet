import { NextResponse, type NextRequest } from 'next/server';
import type { CurrentUser, LoginRequest, LoginResponse, Problem } from '@/lib/api-types';
import { callGateway, gatewayUnreachable, localProblem, relay } from '@/server/gateway';
import { toSessionInfo, writeSession, type OtcSession } from '@/server/session';

export const dynamic = 'force-dynamic';

/**
 * `POST /api/auth/login` — exchanges operator credentials for a Gateway JWT and
 * seals it into this server's httpOnly session cookie. The response BODY is
 * `SessionInfo` (identity only); the token never appears in it.
 *
 * Accepts JSON (the hydrated login form) AND a plain HTML form post
 * (`application/x-www-form-urlencoded`), so a submit that happens before the
 * page has hydrated still logs in — with the password in a POST body, never in
 * a URL — instead of the control having to be disabled until hydration (#7's
 * stuck-disabled login button, review_web_app.md Pass 2a / Pass 4). A form post
 * is answered with a 303 redirect rather than JSON.
 */
export async function POST(request: NextRequest): Promise<NextResponse> {
  const isForm = (request.headers.get('content-type') ?? '').includes('application/x-www-form-urlencoded');
  let credentials: LoginRequest;
  try {
    if (isForm) {
      const form = new URLSearchParams(await request.text());
      credentials = { username: form.get('username') ?? '', password: form.get('password') ?? '' };
    } else {
      const parsed = (await request.json()) as Partial<LoginRequest>;
      credentials = { username: String(parsed.username ?? ''), password: String(parsed.password ?? '') };
    }
  } catch {
    return localProblem(400, 'VALIDATION_FAILED', 'The request was malformed', 'The login request body could not be read.');
  }

  let issuedResponse: Response;
  try {
    issuedResponse = await callGateway('/auth/login', { method: 'POST', body: JSON.stringify(credentials) });
  } catch (error) {
    return isForm ? redirectToLogin(request, gatewayUnreachableDetail(error)) : gatewayUnreachable(error);
  }

  if (!issuedResponse.ok) {
    return isForm ? redirectToLogin(request, await problemText(issuedResponse)) : relay(issuedResponse);
  }

  const issued = (await issuedResponse.json()) as LoginResponse;
  let meResponse: Response;
  try {
    meResponse = await callGateway('/auth/me', { token: issued.accessToken });
  } catch (error) {
    return isForm ? redirectToLogin(request, gatewayUnreachableDetail(error)) : gatewayUnreachable(error);
  }
  if (!meResponse.ok) {
    return isForm ? redirectToLogin(request, await problemText(meResponse)) : relay(meResponse);
  }
  const me = (await meResponse.json()) as CurrentUser;

  const session: OtcSession = {
    accessToken: issued.accessToken,
    expiresAt: Date.now() + issued.expiresIn * 1000,
    username: me.username,
    displayName: me.displayName,
    roles: me.roles,
  };
  const response = isForm ? NextResponse.redirect(new URL('/orders', request.url), 303) : NextResponse.json(toSessionInfo(session));
  await writeSession(response, session);
  return response;
}

/** A refused upstream call's own words for the no-JS form path: the problem's `detail`, else its `title`; the status only when the body carries neither. */
async function problemText(response: Response): Promise<string> {
  const problem = (await response.json().catch(() => undefined)) as Partial<Problem> | undefined;
  return problem?.detail?.trim() || problem?.title?.trim() || `Sign-in failed (HTTP ${response.status}).`;
}

function gatewayUnreachableDetail(error: unknown): string {
  return `The Gateway could not be reached (${error instanceof Error ? error.message : String(error)}).`;
}

function redirectToLogin(request: NextRequest, detail: string): NextResponse {
  const url = new URL('/login', request.url);
  url.searchParams.set('error', detail);
  return NextResponse.redirect(url, 303);
}
