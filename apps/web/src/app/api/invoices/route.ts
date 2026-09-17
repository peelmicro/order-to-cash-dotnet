import type { NextRequest, NextResponse } from 'next/server';
import { proxyToGateway } from '@/server/gateway';

export const dynamic = 'force-dynamic';

/** `GET /api/invoices` → Gateway `GET /invoices`. */
export function GET(request: NextRequest): Promise<NextResponse> {
  return proxyToGateway(request, '/invoices');
}
