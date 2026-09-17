import type { NextRequest, NextResponse } from 'next/server';
import { proxyToGateway } from '@/server/gateway';

export const dynamic = 'force-dynamic';

/**
 * `POST /api/invoices/{id}/payments` → Gateway `POST /invoices/{id}/payments`
 * (R47/R48). `201`/`accepted` and `200`/`duplicate` are both relayed with their
 * own status, so the page can tell a new payment from an idempotent replay.
 */
export async function POST(request: NextRequest, context: { params: Promise<{ id: string }> }): Promise<NextResponse> {
  const { id } = await context.params;
  return proxyToGateway(request, `/invoices/${encodeURIComponent(id)}/payments`, { method: 'POST' });
}
