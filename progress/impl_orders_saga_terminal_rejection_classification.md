# `orders_saga_terminal_rejection_classification` — implementation record

Feature 42, phase 8, `sdd: false`. Specification of record: `feature_list.json` id 42's three acceptance bullets, plus the seam feature 16 deliberately left at `design.md` §6.1/§6.3/§12.

## What was built

`NatsSagaCommandsAdapter` used to collapse **every** `RpcError`-shaped reply into `SagaCommandTransportError` (always retryable). That made a terminal business rejection — e.g. `stock.release` against an already-`consumed` reservation, answered `PRECONDITION_FAILED` — retry forever at capped backoff instead of resolving, because the responder's "no" can never turn into a "yes" on a later attempt.

**1. New error type — `src/Orders/Application/Ports/ISagaCommands.cs`**
Added `SagaCommandBusinessRejectionError(subject, rpcErrorCode, reason)`, parallel in shape to `SagaCommandTimeoutError`/`SagaCommandTransportError`. `SagaCommandTransportError`'s doc comment narrowed to state it is now reserved for genuinely retryable failures.

**2. The classification, in the one place design.md §6.1 named — `src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs`**
Added `private static bool IsTerminalRpcErrorCode(string code)`, an exhaustive switch over the closed set below. `SendAsync`'s `RpcError`-body branch now dispatches on it: a terminal code throws `SagaCommandBusinessRejectionError`; everything else keeps throwing `SagaCommandTransportError` exactly as before. `NatsStockAvailabilityChecker` (the sibling feature 15 file the brief said not to disturb) was **not touched** — it has no `RpcError`-body branch at all, only the two existing exception types.

**3. Dispatcher short-circuit — `src/Orders/Infrastructure/Saga/SagaCommandDispatcher.cs`**
`DispatchClaimedAsync`'s retry loop now has a `catch (SagaCommandBusinessRejectionError ex)` clause **before** the existing `SagaCommandTimeoutError or SagaCommandTransportError` filter. On a match: no further in-line attempts, no backoff `delay()` call, calls the new `store.RejectAsync(claimed.Id, attempt, ex.Message, ct)`, logs a structured error line naming the RpcError code, and returns — never reaching `ParkAsync`'s retry-eligible path.

**4. Terminal end state — `rejected` — new store method**
- `ISagaCommandStore.RejectAsync(commandId, attemptsMade, lastError, ct)` added to the port (`src/Orders/Application/Ports/ISagaCommandStore.cs`).
- Implemented in `EfCoreSagaCommandStore.RejectAsync` — same shape as the existing `ParkAsync`: reads the row's current `Attempts` and accumulates (never overwrites), truncates `last_error` to the same 4000-char cap, but sets `next_attempt_at = NULL` instead of scheduling a retry, because there is none.
- **No migration.** `status` is `nvarchar(10)` with `HasMaxLength(10)` and no `CHECK` constraint (confirmed by reading `SagaCommandConfiguration.cs` and the `20260901100855_InitialCreate` migration directly) — `"rejected"` is 8 characters, fits with zero DDL change, exactly as design.md §6.3/§12 asserted. `ClaimDueAsync`'s own SQL predicate (`status = 'pending' ... OR status = 'parked' ...`) was **not modified** — a `rejected` row structurally matches neither branch, so it is already excluded with no extra guard, and this is proven directly (not assumed) by the new integration test below.

## The closed set adopted, and its justification from the shared contract

Per the brief's instruction to cross-check against `specs/shared/asyncapi.yaml` rather than transcribe #7's list on trust: `asyncapi.yaml`'s `RpcError.code` schema (line ~2837) is a closed twelve-value enum: `VALIDATION_FAILED, NOT_FOUND, CONFLICT, PRECONDITION_FAILED, ORDER_NOT_CANCELLABLE, STOCK_UNAVAILABLE, INVOICE_NOT_PAYABLE, PAYMENT_MISMATCH, DOMAIN_ERROR, INTERNAL_ERROR, UNAVAILABLE, TIMEOUT`. The schema's own description marks `TIMEOUT` as caller-produced (never by a responder), which is why it never actually reaches this classifier in the codebase (`NatsNoReplyException`/null-`Data` already throw `SagaCommandTimeoutError` first) — but it is listed on the transient side for defensive completeness in case a responder ever echoes it in a body of its own.

Adopted split — **terminal** (9 codes, a definitive "no" from the responder's own domain): `VALIDATION_FAILED, NOT_FOUND, CONFLICT, PRECONDITION_FAILED, ORDER_NOT_CANCELLABLE, STOCK_UNAVAILABLE, INVOICE_NOT_PAYABLE, PAYMENT_MISMATCH, DOMAIN_ERROR` — versus **transient/infra** (3 codes, a later attempt genuinely might resolve): `TIMEOUT, UNAVAILABLE, INTERNAL_ERROR`. This is exactly #7's own closed set (`nats-saga-commands.adapter.ts`'s `isTerminalRpcErrorCode`, confirmed by reading its commit `fd445bc` directly) and it falls out of the shared contract itself: the twelve codes minus the three that name a **transport/infrastructure** condition (a timeout, a service being down, an unexpected internal fault) rather than a **business** one leaves exactly the nine above. My reading did not differ from #7's, so there is nothing to flag to the gate beyond this citation.

One deliberate divergence in defensive posture, not in the adopted set: an RpcError code **outside** the closed set (which cannot happen against the current contract) falls to the **transient** side rather than throwing. #7's TypeScript uses an exhaustive `switch` with a `never`-typed default that throws — a compile-time device that is unreachable at runtime only because the union type is closed; at runtime, an actually-unrecognised string reaching that `switch` throws inside `call()`, and the **caller's own generic `catch (error)`** in the dispatcher (not a type-filtered catch) treats that thrown `Error` as retryable/transient regardless, so TS's *observed* runtime behaviour for an unrecognised code is already transient, not terminal. C#'s `catch (Exception ex) when (ex is SagaCommandTimeoutError or SagaCommandTransportError)` is a **filtered** catch, so an exception type outside that filter would propagate unhandled out of `DispatchClaimedAsync` instead — a different, worse failure mode than parking with visibility. I chose the default branch (`_ => false`) to match TS's actual behaviour rather than its type-level unreachable branch: declaring an unrecognised code **terminal** (a permanent dead end this dispatcher will never revisit) is the riskier of the two wrong answers, so an unexpected value keeps retrying — and eventually parks, visibly, for a human to look at — rather than being silently written off. Documented in the method's own doc comment rather than left implicit.

## Inherited dead-letter deferral (explicit, not silently decided)

Per the brief: the dead-letter/DLQ hook is **deliberately not wired** to the new `rejected` path here. `SagaCommandDispatcher`'s existing `ParkAsync` branch is the only one that can ever trigger a first-park dead-letter signal (feature 27 `observability_reliability` owns that mechanism and has not landed in #8 yet — no `HandlesFirstPark`/OR3 equivalent exists in this codebase to wire into in the first place). A terminal business rejection resolves via one structured `logger.LogError` line (naming the RpcError code, the command, the order id and the accumulated attempts) plus the durable `rejected` row — not the same dead-letter signal an exhausted transient retry raises, because "the responder legitimately, correctly said no" is a different signal from "the responder is broken," which is what a DLQ/dead-letter alert exists to raise. #7 recommended folding `rejected` into the phase-14-equivalent observability feature when it lands (their `progress/impl_orders_saga_terminal_rejection.md` item 5); the same recommendation applies here to #8's `observability_reliability` feature, not yet started. No test asserts the non-wiring, since there is nothing to arm-and-delete for an absence — this paragraph is the record instead.

## Arming table (verbatim, forced rebuild after every restore, restored from a backup copy — never `git checkout --`)

Backups taken to `/tmp/claude-1000/.../scratchpad/arming/{NatsSagaCommandsAdapter,SagaCommandDispatcher,EfCoreSagaCommandStore}.cs.bak` before any arming edit. Every restore was confirmed both by re-reading the changed line and by a final `diff` against the backup showing **zero** difference (recorded below), and every restore was followed by `dotnet build --no-incremental` before the confirming green run, per the protocol's own warning about a stale-but-correct binary vouching for still-armed source.

| # | Guard armed | Change | Named test(s) | Verbatim failure |
|---|---|---|---|---|
| 1 | `NatsSagaCommandsAdapter.IsTerminalRpcErrorCode` (the terminal/transient split itself) | Body replaced with `=> false;` (unconditional — the pre-42 "every RpcError is transport" behaviour) | `NatsSagaCommandsAdapterTests` — 10 of 23 tests in the file | `Assert.Throws() Failure: Exception type was not an exact match / Expected: ...SagaCommandBusinessRejectionError / Actual: ...SagaCommandTransportError` — e.g. for `NOT_FOUND`: `OrderToCash.Orders.Application.Ports.SagaCommandTransportError : fulfillment.stock.release: transport failure: NOT_FOUND: reservation already consumed` |
| 2 | `SagaCommandDispatcher`'s short-circuit `catch (SagaCommandBusinessRejectionError ex)` | Clause's own filter changed to a non-constant `false` (`when (ArmedGuardDisabled)`, a `static readonly bool` — a literal `false` filter is itself a compiler error, CS8359/CS0162, under this repo's `TreatWarningsAsErrors`) and the general retry catch's filter widened to also catch `SagaCommandBusinessRejectionError`, reproducing the pre-42 catch-all exactly | `SagaCommandDispatcherTests.R42_ATerminalBusinessRejectionCallsThePortExactlyOnceDelaysZeroTimesAndRejectsRatherThanParking` | `Assert.Equal() Failure: Values differ / Expected: 1 / Actual: 3` (the port was invoked the full retry budget instead of short-circuiting on the first attempt) |
| 3 | `EfCoreSagaCommandStore.RejectAsync`'s terminal-status emission | `.SetProperty(c => c.Status, "rejected")` changed to `.SetProperty(c => c.Status, "parked")` | `SagaCommandStoreTests.RejectAsync_MarksTheRowRejectedAccumulatesAttemptsClearsTheLease_AndClaimDueAsyncNeverReclaimsIt` (real MS-SQL) | `Assert.Equal() Failure: Strings differ / Expected: "rejected" / Actual: "parked"` |

Restore verification for all three: `diff <backup>.bak <source>` → **zero output** (byte-identical) after every restore, confirmed both immediately after `cp` and again as a final sanity pass before wrap-up. Each restore was followed by `dotnet build --no-incremental` (guards 1–2: `Orders.UnitTests`; guard 3: `Orders.IntegrationTests`) and then the confirming run, all green — recorded per-guard in the sections above.

## Tests added, mapped to the three acceptance bullets

- **Bullet 1** ("a `PRECONDITION_FAILED` ... RpcError reply ... is classified as terminal, not retried at capped backoff forever"):
  - `NatsSagaCommandsAdapterTests.R42_ATerminalRpcErrorCode_MapsToSagaCommandBusinessRejectionErrorNotTransportError` — `[Theory]` over all 9 terminal codes.
  - `NatsSagaCommandsAdapterTests.R42_ReleaseStockAgainstAnAlreadyConsumedReservation_PreconditionFailedIsTerminalNotTransport` — the exact reproduced bug (`stock.release`, `PRECONDITION_FAILED`).
  - `SagaCommandDispatcherTests.R42_ATerminalBusinessRejectionCallsThePortExactlyOnceDelaysZeroTimesAndRejectsRatherThanParking` — the dispatcher-side proof: port called exactly once, zero delays, `RejectAsync` called once with `attemptsMade == 1`, `ParkAsync`/`MarkSentAsync` never called.
- **Bullet 2** ("a genuinely retryable transport failure ... is unaffected — still retried exactly as today"):
  - `NatsSagaCommandsAdapterTests.AnRpcErrorReplyBody_WithATransientCode_MapsToSagaCommandTransportError` (renamed from the pre-42 test; same assertion, `UNAVAILABLE`).
  - `NatsSagaCommandsAdapterTests.R42_ATransientRpcErrorCode_StillMapsToSagaCommandTransportErrorUnchanged` — `[Theory]` over `TIMEOUT`, `UNAVAILABLE`, `INTERNAL_ERROR`.
  - `SagaCommandDispatcherTests.R42_ATransientRpcErrorIsUnaffectedByTheTerminalClassification_StillRetriedToExhaustionAndParked` — full `MaxAttempts` (3) calls, the `[500, 1000]` backoff schedule, one `ParkAsync` call, zero `RejectAsync` calls.
  - The pre-existing `SagaCommandDispatcherTests.SO4_RetriesATimedOutCommandUpToMaxAttemptsWithTheConfiguredBackoffSchedule` and `ExhaustionParksWithTheAccumulatedAttemptsAndTheLastError`, unmodified and still green — proving the new branch is additive.
- **Bullet 3** ("a `saga_commands` row that receives a terminal RpcError reaches a resolved end state ... rather than retrying indefinitely"):
  - `SagaCommandStoreTests.RejectAsync_MarksTheRowRejectedAccumulatesAttemptsClearsTheLease_AndClaimDueAsyncNeverReclaimsIt` (real MS-SQL, Testcontainers) — asserts `status == "rejected"`, `attempts` accumulated onto the pre-existing count, `next_attempt_at IS NULL`, **and** — the durable half of "never retried again" — that `ClaimDueAsync` never reclaims the row even once every other "due" criterion is forced true (old `created_at`, no lease), proving the exclusion directly against the same row rather than by reading the SQL predicate.

## Mechanical fixture updates (not new coverage, needed for the interface widening to compile)

`ISagaCommandStore` gained a required `RejectAsync` method, so every fake/decorator implementing it needed a stub added, each matching its file's existing convention:
- `tests/Orders.UnitTests/SagaCommandDispatcherTests.cs` — `FakeSagaCommandStore.RejectAsync` records calls (`RejectCalls`), used by the new tests above.
- `tests/Orders.UnitTests/SagaFactCommandHandlerTests.cs`, `tests/Orders.UnitTests/SagaFactHandlerTests.cs` — `FakeSagaCommandStore.RejectAsync` throws `NotSupportedException()`, matching the file's existing `ParkAsync`/`TryClaimAsync`/`ClaimDueAsync` stubs (none of these paths exercise the saga command dispatch layer).
- `tests/Orders.IntegrationTests/SagaCommandRetryTests.cs` (`ThrowingOnCreditHoldSagaCommandStore`), `tests/Orders.IntegrationTests/SagaConsumptionTests.cs` (`ThrowOnceSagaCommandStore`) — both decorators, `RejectAsync` delegates to `inner.RejectAsync(...)`.

`OrdersDispatcherRegistrationTests.cs` and `ISagaIgnoredFactRecorder.cs` reference `ISagaCommandStore` by name/type only (DI registration assertions and a doc-comment cross-reference) — neither implements the interface, so neither needed a change; confirmed by reading both before concluding this.

## Files touched

- `src/Orders/Application/Ports/ISagaCommands.cs` — new `SagaCommandBusinessRejectionError`; `SagaCommandTransportError` doc narrowed.
- `src/Orders/Application/Ports/ISagaCommandStore.cs` — new `RejectAsync` port method.
- `src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs` — `IsTerminalRpcErrorCode`, wired into `SendAsync`.
- `src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs` — `RejectAsync` implementation.
- `src/Orders/Infrastructure/Saga/SagaCommandDispatcher.cs` — short-circuit catch clause + doc comments.
- `tests/Orders.UnitTests/NatsSagaCommandsAdapterTests.cs`, `SagaCommandDispatcherTests.cs`, `SagaFactCommandHandlerTests.cs`, `SagaFactHandlerTests.cs`.
- `tests/Orders.IntegrationTests/SagaCommandStoreTests.cs`, `SagaCommandRetryTests.cs`, `SagaConsumptionTests.cs`.
- `feature_list.json` — id 42 `status: in_progress` → `in_review` (the only change to that file).

**`specs/shared/test-matrix.md`: not touched, deliberately.** Feature 42 has no `R<n>` requirement id of its own — `specs/shared/`'s R19–R29 (`order_saga_orchestrator`'s own section) cover the saga's retry/backoff/park clause (R29) but not this feature's terminal-vs-transient split, and no other `R<n>` names it either (confirmed by grepping the whole matrix for "42", "terminal_rejection" and "BusinessRejection" before concluding this — the only hits are R42/R43/R44's unrelated numeric coincidence, the credit-simulator rows). #7 recorded the identical finding for its own counterpart feature. There is no `TODO` row to flip to a test name.

## Test counts

- `Orders.UnitTests`: 233 → **248** passed (net +15: 13 in `NatsSagaCommandsAdapterTests`, 2 in `SagaCommandDispatcherTests`).
- `Orders.IntegrationTests`: 63 → **64** passed (net +1, `SagaCommandStoreTests`), full suite run against real MS-SQL/Kafka/NATS via Testcontainers — **64/64 passed in 5 m 01 s**.
- `Architecture.Tests`: **16/16** passed, unaffected (no `Domain/` file touched; `src/Cqrs` not referenced by this change).

## Quality gates run

- `dotnet build OrderToCash.sln --nologo` — **0 warnings, 0 errors**, whole solution (all 6 services + all test projects).
- `dotnet format OrderToCash.sln --verify-no-changes --include <every touched file>` — **clean**.
- `dotnet test tests/Orders.UnitTests` — **248/248 passed**.
- `dotnet test tests/Orders.IntegrationTests` (full suite, real Testcontainers MS-SQL/Kafka/NATS) — **64/64 passed**, 5 m 01 s.
- `dotnet test tests/Architecture.Tests` — **16/16 passed** (NetArchTest layering, unaffected).
- `dotnet test ... --collect:"XPlat Code Coverage"` run against `Orders.UnitTests` to confirm the collector still runs cleanly (coverlet's own **gate** is feature 34 `sonarqube_quality_gates`, not yet landed — `quality.sh`'s own header comment: "this script reports a number, it does not enforce one").
- `./init.sh` — **exits 0** ("environment and state are coherent"; the only warnings are the expected "uncommitted changes mid-session" and "run `./quality.sh` before closing", both informational).
- **Full `./quality.sh` was not run** (its step 3 is `dotnet test` over the **whole solution**, including Fulfillment/Billing/Notifications/Seed integration suites this feature does not touch — a 15–20+ minute Testcontainers run of code outside this feature's `src/Orders`-only diff). Ran the narrower, fully-equivalent-for-this-diff sequence above instead: solution build, format check, and every test project this change can possibly affect (`Orders.UnitTests`, `Orders.IntegrationTests` in full, `Architecture.Tests`), all green. #7's own record for this feature made the identical budget call for the analogous "full monorepo test:integration" step.

## What I could not do / deliberately left out

- Did not wire the dead-letter/DLQ mechanism to the new `rejected` terminal state — see the dedicated section above; this is the brief's own explicit instruction, not a scope judgement call.
- Did not add a Testcontainers end-to-end test with a **stand-in NATS responder** answering `stock.release` with a real `RpcError{code: PRECONDITION_FAILED}` body over the wire (as opposed to the direct `EfCoreSagaCommandStore.RejectAsync` integration test, and the unit-level adapter/dispatcher proofs). `StandInSagaResponders.cs`'s existing helpers (`StartStockReleaseAsync` etc.) only support returning a typed success reply, not an `RpcError` body — adding that is a small but real responder-side addition, and #7's own record for this exact feature made the same call for the same reason ("a slightly bigger change than 'add a test'"), judging the armed unit-level proofs (adapter classification + dispatcher short-circuit, both proven to fail on deletion) plus the direct store-level integration test as satisfying the acceptance bullets' literal wording. Flagging for the reviewer rather than doing unasked, matching #7's own disclosed judgement call here.
- Did not touch `NatsStockAvailabilityChecker.cs` — confirmed by reading it that it has no `RpcError`-body branch to touch at all (its taxonomy is only the two existing exception types), so the brief's "do not disturb it" held trivially, not by omission.
