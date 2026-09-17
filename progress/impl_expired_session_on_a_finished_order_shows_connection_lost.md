# Backlog id 98 — an expired session on a finished order showed "Connection lost"

**Scope:** `apps/web/**` and this record. `feature_list.json` is untouched, as briefed. No `.cs`, `.csproj`, `specs/shared/` or script change. **Wall-clock:** from about 00:55 to about 01:30 on 2026-09-17 (after id 29's round 3). The reading was already in context from id 29.

## 1. The defect, confirmed in source

- **The stream gives up.** When the stream is refused, the browser closes it (`readyState` CLOSED). `lib/order-stream-client.ts:105-111` turns that into `gave-up`.
- **The page offers a Retry that cannot work.** `features/orders/order-detail-view.tsx` shows **Connection lost** with **Retry connection**, and that button only reopened the stream (`hooks/use-order-stream.ts:78`).
- **Nothing else asks the server.** A **terminal** order is never re-read (`hooks/use-order-detail.ts:59-70`). So no query failed with 401, `redirectOnSignedOut` (`app/providers.tsx`) never ran, and Retry reopened a stream the session could not open, indefinitely.
- **Why the stream cannot tell.** `EventSource` exposes neither status nor body, so it cannot distinguish a refusal from a dropped network.

## 2. The fix, and why this one

**When the stream gives up, ask the order again through the query client. Retry does the same before it reopens, and reopens only if that read succeeds.**

- **`order-detail-view.tsx`:**
  - an effect on `stream.status === 'gave-up'` calls the detail query's `refetch()`;
  - `retryConnection` awaits `refetch()` and calls `stream.retry()` only when the result `isSuccess`.
- **Why the order read.** It is the one request on this page that goes through the same session and the same Gateway as the stream, and it can see the answer:
  - an expired session → the route answers 401 (`proxyToGateway` → `notSignedIn()`) → `ApiError(401)` → the QueryCache's `redirectOnSignedOut` → `/login`. The server also clears the cookie;
  - a Gateway that cannot be reached, or an order that is gone → the error is shown in its own words by the page's existing `order-detail-error` (`Could not load this order: …`, a reviewed label);
  - a genuine network drop → the read succeeds, and the page keeps the honest **Connection lost** with a Retry that re-checks and then reopens.
- **Alternatives rejected:**
  - *Probe the stream route itself with `apiRequest`:* a successful answer is a never-ending SSE body, which `apiRequest` would wait on forever.
  - *Poll a session endpoint:* it adds a request that is not about this page, and it would not see a Gateway that is down.
  - *Retry-only re-check:* it leaves a terminal order on "Connection lost" until the user clicks, and bullet 1 wants the page to *end at* `/login`.
- **A test seam, `app/providers.tsx`.**
  - `redirectOnSignedOut` now navigates through `navigateSignedOut`, which defaults to `window.location.assign`. `setSignedOutNavigation(fn)` replaces it in a DOM test, because jsdom's `location.assign` cannot be observed. There is no behaviour change.
  - The old `eslint-disable` for `no-location-assign-relative-destination` became unused (the rule only fires on a literal argument), so it is now a plain comment giving the same reason.

**What the user sees now:**
- **Terminal order, expired session:** the stream gives up, the order read returns 401, and the browser goes to `/login`. While the page is still on screen it shows `Could not load this order: You are not signed in, or your session has expired. Sign in again.` and **no Retry**.
- **Genuine drop:** "Connection lost" + Retry, as before.
- **Session expired between the drop and the click:** Retry → 401 → `/login`, and no reopen.
- **Non-terminal order:** as before (its backstop re-reads were already seeing 401s), plus the same re-check on give-up.

## 3. Ported-idiom ledger

| Property | #7 relied on | #8 now | Guard |
|---|---|---|---|
| An expired session on a page whose stream gives up ends at sign-in | **Nothing: #7 has the same gap.** `apps/web/app/pages/orders/[id].vue:94-97` `retryConnection()` calls `connect(…)` directly, with no check. #7's session check runs only on route navigation (`apps/web/app/middleware/auth.global.ts:10-21`, `defineNuxtRouteMiddleware`) | **A strengthening.** Give-up and Retry both re-read the order through the query client, so a 401 reaches `redirectOnSignedOut` | `order-detail-view.test.tsx` › *an expired session while the stream is down (backlog id 98)*, three cases (§4). Worth inheriting for #9 |

**How the #7 half was checked.** A `grep -rnE "gave-up|401|/login|retry"` over `order-to-cash-nestjs/apps/web/app` (tests excluded) found:
- the stream client's `gave-up` (`app/lib/order-stream-client.ts:120`);
- the page's badge and Retry (`[id].vue:90,94-97,172`);
- the layout's logout navigation (`app/layouts/default.vue:11`);
- the navigation middleware (`auth.global.ts:11,20`).

No stream-failure path in #7 consults the session.

## 4. Tests

**Modified: `src/features/orders/order-detail-view.test.tsx`.**
- The existing case *when the client gives up … offers a retry that opens a fresh stream* now uses `waitFor` for the second stream, because Retry re-checks first. It asserts nothing less than before.
- A new describe, *OrderDetailView — an expired session while the stream is down (backlog id 98)*, renders with the app's **real** `makeQueryClient()` and records navigations through `setSignedOutNavigation`. The 401 body is the app's **real** `notSignedIn()` response. The refusal is held **by read count, not by timing**, and the stream's refusal is `FakeEventSource.fail(true)` (closed):
  1. ***a TERMINAL order whose stream is refused ends at /login — never at "Connection lost" with a Retry that loops.*** Read 1 is a `completed` order; every later read is 401. After the stream is refused, the test asserts:
     - navigations are `['/login']`;
     - exactly 2 reads;
     - the error shows the not-signed-in words;
     - no `stream-retry`;
     - still one `EventSource`.
  2. ***Retry re-checks before reopening.*** Reads 1–2 succeed, which is a genuine drop: the page shows "Connection lost" and there is no navigation. Read 3, at Retry, is 401: navigations are `['/login']`, and still one `EventSource`.
  3. ***a genuine drop on a terminal order keeps "Connection lost" with a Retry that reopens, and signs nobody out.*** Every read succeeds. The test asserts 2 reads after the give-up, a second `EventSource` after Retry, 3 reads, and no navigation.
- **Unchanged and green:** the existing reconnect guarantees:
  - one stream per page;
  - the stream closed on leave;
  - `resumed: false` re-fetch;
  - per-type de-duplication (`order-detail-view.test.tsx`, `order-stream-client.test.ts`, including the real-socket `Last-Event-ID` resume);
  - the factory-identity guard (`use-order-stream.test.tsx`);
  - the non-terminal backstop cases (D2/D7).
- **The behavioural sweep** (`error-text-sweep.test.tsx`, 56 cases, including the both-ways label check) is green. The only text this fix can show is the existing `Could not load this order:` label plus the problem's own words.

## 5. Arming

**Procedure.** The driver is `scratchpad/fr2/arm.py`. Each arm takes a `copy2` backup, applies an exact-anchor edit, runs the named test file, restores with `copyfile` + `utime`, and asserts `filecmp.cmp(shallow=False)`.

**Freshness.** The only edit after the arms was the comment-only lint fix in `providers.tsx`. X4, U2b, U3 and U4, which mutate that file, were re-run after it with the same results. After the runs, `grep` shows `providers.tsx:21 navigateSignedOut('/login');` and `:28 mutationCache: new MutationCache({ onError: redirectOnSignedOut }),` intact.

| # | Mutation | Result | Verbatim failure |
|---|---|---|---|
| X1 | **The fix removed**: no re-check on give-up, and Retry reopens directly | 3 failed / 24 | `AssertionError: a TERMINAL (completed) order with an expired session must send the user to /login when its stream is refused — no redirect happened: expected [] to deeply equal [ '/login' ]`. The other two cases fail on the re-read count, re-run after the messages were added: `AssertionError: when the stream gives up, the TERMINAL order must be asked again once (the re-check that can see a 401): expected 1 to be 2` (twice) |
| X2 | Only the give-up re-check removed | 3 failed / 24 | the same first failure (`… must send the user to /login when its stream is refused — no redirect happened: expected [] to deeply equal [ '/login' ]`) |
| X3 | Only Retry's re-check removed (Retry reopens directly) | 2 failed / 24 | `AssertionError: a Retry refused for an expired session on a TERMINAL order must send the user to /login: expected [] to deeply equal [ '/login' ]`; and `AssertionError: Retry must re-check the order before reopening the stream: expected 2 to be 3` (both re-run after the messages were added) |
| X4 | `providers.tsx` calls `browserNavigate` directly, bypassing the seam (the tests must observe the real redirect path) | 2 failed / 24 | `… must send the user to /login when its stream is refused — no redirect happened: expected [] to deeply equal [ '/login' ]` and `a Retry refused … must send the user to /login: expected [] to deeply equal [ '/login' ]` |
| U1–U4, U2b | id 29 FR3.7's session-expiry arms, re-run because `providers.tsx` changed | all still fail: U1 1/9, U2 2/9, U2b 4/9, U3 1/9, U4 2/9 | as recorded in `impl_web_app.md` FR3.7 |

**Defeat list, briefly:**
- attack 1 is X1, X2 and X3;
- attack 3 (substitution) is X4, where the right behaviour goes through the wrong path;
- attack 7 (absence) is covered by the "no Retry offered" and "still one EventSource" assertions;
- the text-shadowing attacks (4–6) and build output (10) do not apply, because these tests execute code;
- attack 8 does not apply: the population is three explicit cases, each asserting observed calls;
- attack 11 is covered by the behavioural sweep's unchanged coverage of this page.

## 6. Counts (read off runs this session)

- **`pnpm exec vitest run`:** **20 files, 267 passed**, which is 264 + 3 new cases.
- **`pnpm run lint`:** exit 0. It first failed on the now-unused `eslint-disable` directive, fixed as above.
- **`pnpm run typecheck`:** exit 0.
- **`QUALITY_ONLY=web ./quality.sh`:** **exit 0**. Lint coverage reports `all 99 source files (90 under src/)`. Vitest **20 files / 267 passed**; coverage statements 96.63% (834/863), lines 98.51% (732/743); production build OK; integration **7/7**.
- **No `.cs` or `.csproj` file** is newer than 2026-09-16 22:06 (`find … -newermt` printed nothing).

## 7. Files touched

- **Product:** `apps/web/src/features/orders/order-detail-view.tsx`, `apps/web/src/app/providers.tsx`.
- **Test:** `apps/web/src/features/orders/order-detail-view.test.tsx`.
- **Record:** this file.

## 8. Not done / notes

- **`feature_list.json` is untouched**, as briefed. The leader owns id 98's transition.
- **A non-401 refusal of the stream alone**, where the order read still succeeds (for example, a stream-only upstream fault), still shows "Connection lost" + Retry. Each Retry is one explicit user action, and each re-checks first. EventSource cannot say more, and bullet 2's *"names the actual state"* holds, because the order is readable and the connection is in fact lost.
