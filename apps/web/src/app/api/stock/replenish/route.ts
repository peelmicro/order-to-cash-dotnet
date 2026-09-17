import type { NextRequest, NextResponse } from 'next/server';
import { proxyToGateway } from '@/server/gateway';

export const dynamic = 'force-dynamic';

/** `POST /api/stock/replenish` → Gateway `POST /stock/replenish`. `units` is a delta; not idempotent by design. */
export function POST(request: NextRequest): Promise<NextResponse> {
  return proxyToGateway(request, '/stock/replenish', { method: 'POST' });
}
