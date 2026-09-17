// @vitest-environment node
//
// Session expiry, browser half: any query or mutation that fails with 401
// sends the browser to /login (a full navigation, so the server layout
// re-runs) — except on /login itself, where it would loop. Run under node with
// a stand-in `window`, because jsdom's `location.assign` cannot be observed.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { makeQueryClient } from '@/app/providers';
import { ApiError } from '@/lib/problem';

let assign: ReturnType<typeof vi.fn>;
function at(pathname: string) {
  assign = vi.fn();
  vi.stubGlobal('window', { location: { pathname, assign } });
}

const failure = (status: number) => new ApiError(status, { title: 'refused', detail: `status ${status}`, status });

async function failQuery(status: number) {
  const client = makeQueryClient();
  await client.fetchQuery({ queryKey: ['probe', status], queryFn: () => Promise.reject(failure(status)) }).catch(() => undefined);
}

async function failMutation(status: number) {
  const client = makeQueryClient();
  await client
    .getMutationCache()
    .build(client, { mutationFn: () => Promise.reject(failure(status)) })
    .execute(undefined)
    .catch(() => undefined);
}

beforeEach(() => at('/orders'));
afterEach(() => vi.unstubAllGlobals());

describe('makeQueryClient — a 401 signs the browser out', () => {
  it('a QUERY failing with 401 sends the browser to /login', async () => {
    await failQuery(401);
    expect(assign.mock.calls, 'a failed query with 401 must navigate to /login').toEqual([['/login']]);
  });

  it('a MUTATION failing with 401 sends the browser to /login', async () => {
    await failMutation(401);
    expect(assign.mock.calls, 'a failed mutation with 401 must navigate to /login').toEqual([['/login']]);
  });

  it.each([
    ['query', 403, failQuery],
    ['mutation', 403, failMutation],
  ] as const)('a %s failing with %i does NOT navigate', async (_kind, status, fail) => {
    await fail(status);
    expect(assign.mock.calls, `a ${status} must leave the user where they are`).toEqual([]);
  });

  it.each([
    ['query', failQuery],
    ['mutation', failMutation],
  ] as const)('a %s failing with 401 while already ON /login does NOT navigate (no loop)', async (_kind, fail) => {
    at('/login');
    await fail(401);
    expect(assign.mock.calls, 'on /login a 401 is the sign-in form\'s own error, not a redirect').toEqual([]);
  });
});
