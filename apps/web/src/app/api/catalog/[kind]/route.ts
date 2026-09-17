import type { NextRequest, NextResponse } from 'next/server';
import { localProblem, proxyToGateway } from '@/server/gateway';

export const dynamic = 'force-dynamic';

/** The three catalogue collections openapi.yaml defines — a literal set, so an unknown `kind` is a 404 here and never a Gateway path built from user input. */
const KINDS = new Set(['products', 'retailers', 'companies']);

/** `GET /api/catalog/{products|retailers|companies}` → Gateway `GET /catalog/{kind}`. */
export async function GET(request: NextRequest, context: { params: Promise<{ kind: string }> }): Promise<NextResponse> {
  const { kind } = await context.params;
  if (!KINDS.has(kind)) {
    return localProblem(404, 'NOT_FOUND', 'No such catalogue', `There is no catalogue named "${kind}".`);
  }
  return proxyToGateway(request, `/catalog/${kind}`);
}
