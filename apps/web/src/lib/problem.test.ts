// @vitest-environment node
import { describe, expect, it } from 'vitest';
import { ApiError, describeError, readProblem, stockShortages } from './problem';

describe('problem documents — the error the user sees is THAT error\'s own text', () => {
  it('describeError returns the problem\'s own detail, not the fallback', () => {
    const error = new ApiError(409, { title: 'Insufficient stock', detail: 'Insufficient stock for 1 line(s) at acceptance.', code: 'STOCK_UNAVAILABLE', status: 409, type: 'about:blank' });
    expect(describeError(error, 'Placing the order failed.')).toBe('Insufficient stock for 1 line(s) at acceptance.');
  });

  it('an absent detail falls back to the title — the optional element simply missing (defeat-list attack 7)', () => {
    expect(describeError(new ApiError(503, { title: 'The owning context is unreachable', code: 'UPSTREAM_UNAVAILABLE' }), 'fallback')).toBe('The owning context is unreachable');
    expect(describeError(new ApiError(503, { title: 'Title only', detail: '   ', code: 'X' }), 'fallback')).toBe('Title only');
  });

  it('uses the fallback only when the server said nothing usable, or the error is not an API error', () => {
    expect(describeError(new ApiError(502, undefined), 'fallback')).toBe('fallback');
    expect(describeError(new ApiError(500, { code: 'X' }), 'fallback')).toBe('fallback');
    expect(describeError(new TypeError('Failed to fetch'), 'fallback')).toBe('fallback');
    expect(describeError(undefined, 'fallback')).toBe('fallback');
  });

  it('readProblem parses the response body itself — the problem is the body, not a field of an envelope', async () => {
    const body = { type: 'about:blank', title: 'Bad', status: 400, detail: 'id "x" is not a valid UUID', code: 'VALIDATION_FAILED' };
    expect(await readProblem(new Response(JSON.stringify(body), { status: 400 }))).toEqual(body);
  });

  it('readProblem returns undefined for a body that is not a problem document', async () => {
    expect(await readProblem(new Response('<html>502 Bad Gateway</html>', { status: 502 }))).toBeUndefined();
    expect(await readProblem(new Response('', { status: 500 }))).toBeUndefined();
    expect(await readProblem(new Response('[1,2]', { status: 500 }))).toBeUndefined();
    expect(await readProblem(new Response('{"unrelated":true}', { status: 500 }))).toBeUndefined();
    expect(await readProblem(new Response('null', { status: 500 }))).toBeUndefined();
  });

  it('ApiError carries the status and the problem, and its message is the problem\'s own text', () => {
    const error = new ApiError(422, { title: 'Mismatch', detail: 'Payment amount 100 does not match invoice total 24999', code: 'PAYMENT_MISMATCH' });
    expect(error.status).toBe(422);
    expect(error.problem?.code).toBe('PAYMENT_MISMATCH');
    expect(error.message).toBe('Payment amount 100 does not match invoice total 24999');
    expect(new ApiError(504, undefined).message).toBe('Request failed with status 504');
  });

  it('stockShortages reads a 409 STOCK_UNAVAILABLE problem\'s shortages, and nothing else', () => {
    const shortages = [{ productCode: 'PRD-0001', requested: 50, available: 12 }];
    expect(stockShortages(new ApiError(409, { title: 't', code: 'STOCK_UNAVAILABLE', shortages } as never))).toEqual(shortages);
    expect(stockShortages(new ApiError(409, { title: 't', code: 'STOCK_UNAVAILABLE', shortages: [] } as never))).toBeUndefined();
    expect(stockShortages(new ApiError(409, { title: 't', code: 'STOCK_UNAVAILABLE' }))).toBeUndefined();
    expect(stockShortages(new Error('x'))).toBeUndefined();
  });
});
