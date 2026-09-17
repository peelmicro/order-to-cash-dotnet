// @vitest-environment node
import { afterEach, describe, expect, it } from 'vitest';
import { apiGet, apiRequest, setApiFetch, withQuery } from './api-client';
import { ApiError } from './problem';

afterEach(() => setApiFetch(undefined));

describe('api-client — the browser talks only to this origin', () => {
  it('withQuery drops empty values, repeats arrays, and leaves a bare path alone', () => {
    expect(withQuery('/api/orders')).toBe('/api/orders');
    expect(withQuery('/api/orders', { status: ['placed', 'paid'], retailerCode: undefined, page: 2, belowThreshold: false, empty: '', nothing: null })).toBe('/api/orders?status=placed&status=paid&page=2');
    expect(withQuery('/api/stock', { belowThreshold: true })).toBe('/api/stock?belowThreshold=true');
    expect(withQuery('/api/x', {})).toBe('/api/x');
  });

  it('sends JSON with the right headers and returns status, data and headers', async () => {
    const seen: { url: string; init?: RequestInit }[] = [];
    setApiFetch(async (url, init) => {
      seen.push({ url, init });
      return new Response(JSON.stringify({ outcome: 'duplicate' }), { status: 200, headers: { 'Idempotent-Replay': 'true' } });
    });
    const result = await apiRequest<{ outcome: string }>('/api/invoices/i/payments', { method: 'POST', json: { paymentReference: 'PAY-1' } });
    expect(result).toMatchObject({ status: 200, data: { outcome: 'duplicate' } });
    expect(result.headers.get('idempotent-replay')).toBe('true');
    expect(seen[0]?.url).toBe('/api/invoices/i/payments');
    expect(seen[0]?.init?.body).toBe('{"paymentReference":"PAY-1"}');
    expect(new Headers(seen[0]?.init?.headers).get('content-type')).toBe('application/json');
    expect(seen[0]?.init?.credentials).toBe('same-origin');
  });

  it('a non-2xx answer throws ApiError carrying the parsed problem document', async () => {
    setApiFetch(async () => new Response(JSON.stringify({ title: 'Not found', detail: 'no order for id "x"', code: 'NOT_FOUND', status: 404 }), { status: 404, headers: { 'Content-Type': 'application/problem+json' } }));
    const failure = await apiGet('/api/orders/x').catch((error: unknown) => error);
    expect(failure).toBeInstanceOf(ApiError);
    expect((failure as ApiError).status).toBe(404);
    expect((failure as ApiError).problem?.detail).toBe('no order for id "x"');
  });

  it('an empty 2xx body yields undefined data rather than a parse error', async () => {
    setApiFetch(async () => new Response(null, { status: 204 }));
    expect((await apiRequest('/api/x')).data).toBeUndefined();
  });
});
