// @vitest-environment node
//
// Session expiry, server half: when the Gateway answers 401 (the token expired
// or was revoked), relay() forwards that answer AND expires this app's session
// cookie, so the browser is signed out here too. Any other failure keeps it.
import { describe, expect, it } from 'vitest';
import { relay } from '@/server/gateway';
import { gatewayFixture } from '@/test/gateway-fixtures';

const upstream = (status: number) => {
  const fixture = gatewayFixture('orders-without-token-401');
  return new Response(JSON.stringify({ ...JSON.parse(fixture.body), status }), { status, headers: { 'Content-Type': 'application/problem+json' } });
};

describe('relay() and the session cookie', () => {
  it('an upstream 401 is relayed with a Set-Cookie that EXPIRES the otc_session cookie', async () => {
    const response = await relay(upstream(401));
    expect(response.status).toBe(401);
    const setCookie = response.headers.get('set-cookie') ?? '<no Set-Cookie header: the session was kept after a 401>';
    expect(setCookie, 'the 401 must clear the session cookie').toMatch(/^otc_session=;/);
    expect(setCookie, 'the cleared cookie must expire now').toMatch(/;\s*Max-Age=0(;|$)/i);
    expect(setCookie, 'the cleared cookie must stay HttpOnly on the same path').toMatch(/;\s*Path=\/(;|$).*HttpOnly/i);
  });

  it.each([403, 500])('an upstream %i is relayed and the session cookie is NOT touched', async (status) => {
    const response = await relay(upstream(status));
    expect(response.status).toBe(status);
    expect(response.headers.get('set-cookie'), `a ${status} must not sign the user out`).toBeNull();
  });
});
