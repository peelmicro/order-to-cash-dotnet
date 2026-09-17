import { NextResponse } from 'next/server';
import { clearSession } from '@/server/session';

export const dynamic = 'force-dynamic';

/** `POST /api/auth/logout` — forgets the sealed session. The Gateway JWT is stateless; dropping it is the logout. */
export async function POST(): Promise<NextResponse> {
  const response = NextResponse.json({ authenticated: false });
  clearSession(response);
  return response;
}
