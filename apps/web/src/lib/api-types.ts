/**
 * The single type surface for every wire shape this app handles — pages,
 * hooks and route handlers all import from here, and this file is the ONLY
 * importer of the generated OpenAPI types (eslint `no-restricted-imports`
 * enforces it). Nothing here is hand-written except `SessionInfo`, which is
 * this app's own BFF shape and never crosses the Gateway wire.
 */
import type { components, operations } from '@/generated/openapi';

type Schemas = components['schemas'];

export type Problem = Schemas['Problem'];
export type ValidationProblem = Schemas['ValidationProblem'];
export type StockUnavailableProblem = Schemas['StockUnavailableProblem'];

export type LoginRequest = Schemas['LoginRequest'];
export type LoginResponse = Schemas['LoginResponse'];
export type CurrentUser = Schemas['CurrentUser'];

export type Party = Schemas['Party'];
export type Product = Schemas['Product'];
export type PageInfo = Schemas['PageInfo'];

export type PlaceOrderRequest = Schemas['PlaceOrderRequest'];
export type PlaceOrderLine = Schemas['PlaceOrderLine'];
export type PlaceOrderResponse = Schemas['PlaceOrderResponse'];
export type OrderStatus = Schemas['OrderStatus'];
export type OrderSummary = Schemas['OrderSummary'];
export type OrderSummaryPage = Schemas['OrderSummaryPage'];
export type OrderDetail = Schemas['OrderDetail'];
export type TimelineEntry = Schemas['TimelineEntry'];
export type ProjectionPending = Schemas['ProjectionPending'];

export type OrderStreamUpdate = Schemas['OrderStreamUpdate'];
export type TimelineStreamEntry = Schemas['TimelineStreamEntry'];
export type StreamReady = Schemas['StreamReady'];

export type StockItem = Schemas['StockItem'];
export type StockPage = Schemas['StockPage'];
export type ReplenishStockRequest = Schemas['ReplenishStockRequest'];
export type ReplenishStockResponse = Schemas['ReplenishStockResponse'];

export type Invoice = Schemas['Invoice'];
export type InvoicePage = Schemas['InvoicePage'];
export type InvoiceStatus = Schemas['InvoiceStatus'];
export type RegisterPaymentRequest = Schemas['RegisterPaymentRequest'];
export type RegisterPaymentResponse = Schemas['RegisterPaymentResponse'];
export type Credit = Schemas['Credit'];
export type CreditPage = Schemas['CreditPage'];

/** `GET /catalog/{products,retailers,companies}` — the spec declares the envelope inline, so it is derived from the operation, not re-typed. */
export type CatalogProductsResponse = operations['listProducts']['responses'][200]['content']['application/json'];
export type CatalogPartiesResponse = operations['listRetailers']['responses'][200]['content']['application/json'];

/** What `GET /api/auth/session` and `POST /api/auth/login` return to the browser — the operator's identity, never the token. */
export interface SessionInfo {
  authenticated: boolean;
  username?: string;
  displayName?: string;
  roles?: string[];
}

export const ORDER_STATUSES: readonly OrderStatus[] = ['placed', 'stock_reserved', 'credit_approved', 'confirmed', 'despatched', 'invoiced', 'paid', 'completed', 'cancelled'];
export const INVOICE_STATUSES: readonly InvoiceStatus[] = ['issued', 'paid'];
