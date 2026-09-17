import { readFileSync } from 'node:fs';
import path from 'node:path';

/**
 * Responses captured from a RUNNING #8 Gateway by
 * scripts/capture-gateway-responses.mjs — real status, real content type, real
 * body bytes. Tests that assert error text use these instead of hand-written
 * problem documents, so they cannot encode a shape the Gateway does not send.
 */
export type GatewayFixtureName =
  | 'login-bad-credentials-401'
  | 'order-malformed-id-400'
  | 'order-unknown-404'
  | 'orders-without-token-401'
  | 'payment-amount-mismatch-422'
  | 'place-order-no-lines-400'
  | 'place-order-stock-unavailable-409'
  | 'replenish-unknown-product-404'
  | 'stock-list-upstream-unavailable-503'
  | 'invoices-list-upstream-unavailable-503'
  | 'credits-list-upstream-unavailable-503'
  | 'place-order-upstream-unavailable-503'
  | 'orders-list-bad-page-400'
  | 'catalog-retailers-upstream-unavailable-503'
  | 'catalog-companies-upstream-unavailable-503'
  | 'catalog-products-upstream-unavailable-503';

export interface GatewayFixture {
  capturedFrom: string;
  capturedAt: string;
  status: number;
  headers: Record<string, string>;
  /** The raw body text exactly as the Gateway sent it. */
  body: string;
}

const dir = path.join(import.meta.dirname, 'fixtures', 'gateway');

export function gatewayFixture(name: GatewayFixtureName): GatewayFixture {
  return JSON.parse(readFileSync(path.join(dir, `${name}.json`), 'utf8')) as GatewayFixture;
}

/** The fixture's own `detail` — what a page must show for it. */
export function fixtureDetail(name: GatewayFixtureName): string {
  const detail = (JSON.parse(gatewayFixture(name).body) as { detail?: string }).detail;
  if (!detail) throw new Error(`fixture ${name} has no detail`);
  return detail;
}

/** A fetch Response carrying the captured bytes, as this app's route handler relays them. */
export function fixtureResponse(name: GatewayFixtureName): Response {
  const fixture = gatewayFixture(name);
  return new Response(fixture.body, { status: fixture.status, headers: fixture.headers });
}
