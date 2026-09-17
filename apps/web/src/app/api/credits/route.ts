import type { NextRequest, NextResponse } from 'next/server';
import { proxyToGateway } from '@/server/gateway';

export const dynamic = 'force-dynamic';

/** `GET /api/credits` → Gateway `GET /credits`. */
export function GET(request: NextRequest): Promise<NextResponse> {
  return proxyToGateway(request, '/credits');
}
