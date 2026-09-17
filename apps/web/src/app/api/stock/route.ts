import type { NextRequest, NextResponse } from 'next/server';
import { proxyToGateway } from '@/server/gateway';

export const dynamic = 'force-dynamic';

/** `GET /api/stock` → Gateway `GET /stock` — a live read of Fulfillment's write model. */
export function GET(request: NextRequest): Promise<NextResponse> {
  return proxyToGateway(request, '/stock');
}
