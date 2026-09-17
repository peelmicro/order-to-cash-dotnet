import type { NextRequest, NextResponse } from 'next/server';
import { proxyToGateway } from '@/server/gateway';

export const dynamic = 'force-dynamic';

/**
 * `GET /api/orders/{id}` → Gateway `GET /orders/{id}` (R54/R55). The Gateway's
 * `202` + `Retry-After` for a not-yet-projected order is relayed with that
 * status and header intact, so the page can show its waiting state and retry
 * on the Gateway's schedule rather than treating it as success or failure.
 */
export async function GET(request: NextRequest, context: { params: Promise<{ id: string }> }): Promise<NextResponse> {
  const { id } = await context.params;
  return proxyToGateway(request, `/orders/${encodeURIComponent(id)}`, { forwardQuery: false });
}
