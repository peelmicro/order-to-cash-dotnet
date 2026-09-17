import type { NextRequest, NextResponse } from 'next/server';
import { proxyToGateway } from '@/server/gateway';

export const dynamic = 'force-dynamic';

/** `GET /api/orders` → Gateway `GET /orders` (read model, R54). Query string forwarded as-is. */
export function GET(request: NextRequest): Promise<NextResponse> {
  return proxyToGateway(request, '/orders');
}

/** `POST /api/orders` → Gateway `POST /orders`. `201` means accepted, not completed; `Idempotency-Key` is forwarded. */
export function POST(request: NextRequest): Promise<NextResponse> {
  return proxyToGateway(request, '/orders', { method: 'POST', forwardRequestHeaders: ['idempotency-key'] });
}
