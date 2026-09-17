# Review — backlog id 98 `expired_session_on_a_finished_order_shows_connection_lost`

**Verdict: APPROVED.** There are no blocking defects and two advisories. `sdd: false`, so the contract is the five acceptance bullets in `feature_list.json`. The record is `progress/impl_expired_session_on_a_finished_order_shows_connection_lost.md`.

**Scope of verification.** I did not re-run the full .NET suite. No `.cs` file changed, so that claim was not under test. I ran:
- five arms of my own (§3);
- a targeted green run over the five stream and session test files;
- `QUALITY_ONLY=web ./quality.sh` once, with nothing else running (`ps` showed no vitest, dotnet or quality.sh process).

## 1. Acceptance bullets → tests

| Bullet | Verified by | Status |
|---|---|---|
| 1. Expired session on a TERMINAL order ends at `/login` via `redirectOnSignedOut` | `order-detail-view.test.tsx` › *a TERMINAL order whose stream is refused ends at /login …* (l.349). It uses the real `makeQueryClient()` and the real `notSignedIn()` body, and asserts navigations `['/login']`, 2 reads, and the error text. Killed by X1 (§3) | met |
| 2. Retry never loops on a refusal, and the page names the actual state | The same case asserts no `stream-retry` and one `EventSource`. *Retry re-checks before reopening* (l.373): after a 401 there is no reopen. *a genuine drop …* (l.395): a successful re-check reopens. Killed by X3, M-A and M-B | met |
| 3. Non-terminal path unchanged; reconnect guarantees pass | Green run: `order-detail-view.test.tsx` (D2/D7 backstop, resumed:false re-fetch, de-dup, one stream, close on leave), `order-stream-client.test.ts` (real-socket `Last-Event-ID` resume), `use-order-stream.test.tsx` | met |
| 4. Refusal held deterministically; arm names the terminal order and the missing redirect | The refusal is `FakeEventSource.fail(true)` plus a read-count switch, with no timers. The X1 message reads *"a TERMINAL (completed) order with an expired session must send the user to /login when its stream is refused — no redirect happened"* | met |
| 5. No generic text | The only new visible text goes through the existing `order-detail-error` label, registered at `app/error-text-sweep.test.tsx:193` (`'Could not load this order:'`). The sweep is inside the 267/267 | met |

## 2. Probes requested by the coordinator

1. **X1 re-armed, and X3.** Both reproduce the recorded failures verbatim (§3).
2. **The seam in `providers.tsx`.**
   - The production default is unchanged: `providers.tsx:11` is `const browserNavigate: Navigate = (url) => window.location.assign(url);`, and `navigateSignedOut` starts as `browserNavigate` (l.12).
   - `setSignedOutNavigation(undefined)` restores the default (l.16).
   - `grep -rn setSignedOutNavigation` over `apps/web/src` has 3 hits. The declaration is at `providers.tsx:15`. The other two, the import at l.8 and the calls at l.343/345, are both in `order-detail-view.test.tsx`. **No production caller exists.**
   - Probe M-C made the default a no-op, and `providers.test.ts` failed 2/6 (§3), so the default path is still guarded directly.
   - `providers.test.ts` and `gateway-relay.test.ts` pass (they are inside the 49/49 below).
3. **No loop.**
   - The re-check is an effect keyed on `[streamStatus, refetch]`, so it fires once per transition into `gave-up`. `refetch` is TanStack's bound observer method and stays stable across renders.
   - Retry reopens only on `isSuccess`.
   - Probe M-B made `refetch` a fresh closure on each render, which is what a loop would need. The loop showed up at once: case 2 recorded **471** navigations, and case 1 counted 3 reads where 2 were expected. **Both cases killed it**, so the no-loop property is guarded by an executing test, not just asserted.
   - After a 401, `detail.isError` replaces the `ReadyOrder` subtree, so no Retry button exists to click.
4. **Label sweep.** Green inside 267/267.
5. **The disclosed residual** (a stream refused for a non-401 reason while the order read succeeds). **I judge this an accurate state, not a defect.** `EventSource` exposes no status. The order really is readable and the live connection really is lost, so "Connection lost" is true. Each Retry is one user action: one re-read, then one reopen, and that reopen has the client's own bounded reconnect budget, which ends in at most one more give-up re-check. Nothing repeats without a click. No backlog entry is owed: the root cause is the browser API, not `specs/shared/`.
6. **Ledger row.** I checked it against #7's checkout (`order-to-cash-nestjs/apps/web/app`), and both citations are accurate:
   - `pages/orders/[id].vue:94-97` is `retryConnection()`, which calls `connect(...)` directly with no session check.
   - `middleware/auth.global.ts:10-21` is `defineNuxtRouteMiddleware`, checking `/api/auth/session` on navigation only.
   - I also checked the one other place #7 could have handled a 401, `plugins/vue-query.ts`. Its `QueryClient` has no `QueryCache`/`onError` at all, and a `grep -rn "statusCode\|onError\|QueryCache\|navigateTo"` over non-test sources finds only navigation sites (`index.vue:2`, `login.vue:36`, `layouts/default.vue:11`, `auth.global.ts:20`) plus comments in `[id].vue:28` and `lib/problem.ts`.
   - So the row's claim holds: #7 has the same gap, and #8 is now stronger. The named guard executes the code the row is about (X1/X2/X4 kill it).
7. **`QUALITY_ONLY=web ./quality.sh`:** **exit 0**.
   - Lint coverage: `all 99 source files (90 under src/)`.
   - Vitest: **20 files, 267 passed**.
   - Coverage: statements 96.63% (834/863), lines 98.51% (732/743).
   - Production-build integration: **7/7**.

## 3. Arming (reviewer's own)

**Procedure.** For each arm: `cp -p` backup to the scratchpad, an anchored `python3` replace, one named test file, restore with `cp`, then `touch` and `cmp`, all under Vitest. Vitest transforms from source, so there is no stale-binary hazard. Every restore printed `RESTORED`, and `providers.tsx:11` was re-read after its restore.

| # | Mutation | Test file | Result | Verbatim failure |
|---|---|---|---|---|
| X1 | Give-up effect body removed; Retry calls `retry()` directly | `order-detail-view.test.tsx` | 3 failed / 24 | `AssertionError: a TERMINAL (completed) order with an expired session must send the user to /login when its stream is refused — no redirect happened: expected [] to deeply equal [ '/login' ]`; and twice `AssertionError: when the stream gives up, the TERMINAL order must be asked again once (the re-check that can see a 401): expected 1 to be 2` |
| X3 | Only Retry's re-check removed | same | 2 failed / 24 | `AssertionError: a Retry refused for an expired session on a TERMINAL order must send the user to /login: expected [] to deeply equal [ '/login' ]`; `AssertionError: Retry must re-check the order before reopening the stream: expected 2 to be 3` |
| M-A | Retry re-checks but ignores the result (`void checked; retry();`) | same | 1 failed / 24 | `AssertionError: Retry must not reopen a stream the session can no longer open: expected [ FakeEventSource{ …(4) }, …(1) ] to have a length of 1 but got 2` |
| M-B | `refetch` made unstable per render (loop probe) | same | 2 failed / 24 | `AssertionError: the order is asked again once, when the stream gives up: expected 3 to be 2`; `AssertionError: a Retry refused for an expired session on a TERMINAL order must send the user to /login: expected [ Array(471) ] to deeply equal [ '/login' ]` |
| M-C | Default `browserNavigate` made a no-op (seam default path) | `providers.test.ts` | 2 failed / 6 | `AssertionError: a failed query with 401 must navigate to /login: expected [] to deeply equal [ [ '/login' ] ]`; `AssertionError: a failed mutation with 401 must navigate to /login: …` |

**Mutation families.**
- Deletion: X1, X3.
- Corruption of a decision the test controls: M-A, where the re-check result is ignored.
- Substitution of the path: M-C, plus the implementer's X4.
- Absence: covered by the "no Retry" and "one EventSource" assertions, which M-A kills.
- Text-shadowing attacks (4–6) and build-output attacks (10) do not apply, because these are executing tests.

**Green after restore:** `providers.test.ts`, `gateway-relay.test.ts`, `order-detail-view.test.tsx`, `order-stream-client.test.ts` and `use-order-stream.test.tsx` gave **5 files, 49 passed**. The 267/267 above came after that.

## 4. CHECKPOINTS (applicable boxes for a web-only, `sdd: false` fix)

- [x] Every acceptance bullet maps to a named, executing test (§1).
- [x] Tests are real and armed: 5 reviewer arms, all killed, with messages that name the claim.
- [x] Both mutation families were probed: deletion, and corruption/ignored-result.
- [x] Ported-idiom ledger present in the record (§3 there), with both halves checked against #7's source.
- [x] No Jest; Vitest only.
- [x] Scope: only `apps/web/src/features/orders/order-detail-view.tsx`, `apps/web/src/app/providers.tsx` and `order-detail-view.test.tsx`. No `.cs`, `specs/shared/` or script change.
- [x] Quality gate: web exit 0.
- [n/a] Domain purity, money, EF/Kafka/NATS classification: no backend change.
- [n/a] The `specs/shared/` routing rule: the residual's root cause is the browser API, not the shared spec.

## 5. Advisories (non-blocking)

- **A1: the record's wall-clock is impossible.** `impl_…md` line 3 says *"from about 00:55 to about 01:30 on 2026-09-17"*. This review ran `date` at **00:49:34** that same night, and the record file was last saved at **00:43:24**. The filesystem tells a different story:
  - `review_web_app.md` was last saved at 00:32:35;
  - the arm driver `scratchpad/fr2/arm.py` at 00:36:52;
  - `order-detail-view.test.tsx` at 00:37:39;
  - the record at 00:43:24.

  So the implementer pass ran about 00:33 → 00:43. The effort record in `history.md` uses the filesystem figures. The record's line should be corrected, and whoever owns it should edit it; I did not.
- **A2: the review round 4 end time in id 29's effort table.** `history.md` gives *"≈00:12 → 00:40"* for id 29's review round 4, but `review_web_app.md` was last written at 00:32:35. This is informational only; I did not change that entry.
