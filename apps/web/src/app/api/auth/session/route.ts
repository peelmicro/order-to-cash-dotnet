import { NextResponse, type NextRequest } from 'next/server';
import { readSession, toSessionInfo } from '@/server/session';

export const dynamic = 'force-dynamic';

/** `GET /api/auth/session` — what the browser may know about its own session: identity, never the token. */
export async function GET(request: NextRequest): Promise<NextResponse> {
  return NextResponse.json(toSessionInfo(await readSession(request)), { headers: { 'Cache-Control': 'no-store' } });
}
