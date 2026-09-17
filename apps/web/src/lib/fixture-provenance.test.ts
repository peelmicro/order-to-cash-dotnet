// @vitest-environment node
//
// Id 29 bullet 5's "proven against a REAL Gateway response" half: every test
// that serves a captured fixture for a literally named route serves it on the
// route it was captured from. (Kept from fix round 1; the error-text claim
// itself is now proven behaviourally by src/app/error-text-sweep.test.tsx.)
import { describe, expect, it } from 'vitest';
import { fixtureUses, sameRoute } from '@/test/fixture-uses';
import { gatewayFixture, type GatewayFixtureName } from '@/test/gateway-fixtures';

/** A fixture served for a route it was NOT captured from, with the reason that is harmless. */
const MISMATCHED_FIXTURE_USES: Record<string, string> = {
  'app/api/route-handlers.test.ts | GET /stock | orders-without-token-401':
    "the Gateway's token check answers before routing, so its 401 is the same for every protected route; the case asserts byte-for-byte RELAY and the cleared session cookie, not words about /stock",
};

describe('captured Gateway fixtures are served on the route they were captured from', () => {
  it('every captured fixture a test serves for a literally named route was captured from THAT route (or is a listed, reasoned exception)', () => {
    const uses = fixtureUses();
    expect(uses.length, 'the fixture-use scanner found almost nothing — it is not reading the tests').toBeGreaterThanOrEqual(17);
    const mismatched = uses.filter((use) => !sameRoute(gatewayFixture(use.fixture as GatewayFixtureName).capturedFrom, use.route)).map((use) => use.key);
    expect(mismatched.sort(), 'fixture(s) served for a route they were NOT captured from (file | route | fixture)').toEqual(Object.keys(MISMATCHED_FIXTURE_USES).sort());
  });
});
