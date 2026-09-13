# `saga_command_fast_path_is_head_of_line_blocked` (backlog id 80, phase 14, `sdd: false`)

## Status

Implemented, reproduced first (bullet 2), fixed, armed (bullet 5), design.md corrected in place (bullet 6). `Orders.UnitTests` 465/465, `Architecture.Tests` 35/35, `Orders.IntegrationTests` 146/146 (real MS-SQL/Kafka/NATS Testcontainers, including `Confirmed_ReleaseWins` and `Confirmed_DespatchWins` both green) — see "Full-suite confirmation" for the reconciliation. `dotnet build OrderToCash.sln --no-incremental` clean (all 18 test projects + 8 service projects). `dotnet format --verify-no-changes` clean on every touched file.

## The defect, restated as found

`SagaCommandDispatchWorker.ExecuteAsync` was a single `await foreach` over `ChannelSagaCommandSignal.Reader.ReadAllAsync(...)`, awaiting `SagaCommandDispatcher.DispatchAsync(...)` inline for every item in turn. `ChannelSagaCommandSignal`'s channel was created with `SingleReader = true`. `OrdersSagaServiceCollectionExtensions.AddOrdersSaga` registered exactly one `AddHostedService<SagaCommandDispatchWorker>()`. One slow-or-absent responder therefore stalled the fast-path dispatch of **every other order's** saga commands for up to `OrdersSagaCommandOptions`'s own documented worst case — `MaxAttempts × TimeoutMs + Σ(BackoffMs × 2^n)` = 3 × 5 000 + 500 + 1 000 = **16 500 ms** per stuck command — with no other order's command able to dispatch through the fast path in the meantime. The sweeper (`IntervalMs` 30 000, `PendingGraceMs` 10 000) was the only other route, exactly as `Confirmed_ReleaseWins`'s own test comment recorded (see "Consequence recorded", below).

## Files touched

- `src/Orders/Infrastructure/OrdersSagaOptions.cs` — new `OrdersSagaDispatchOptions` (`DegreeOfParallelism`, default 8), added as `OrdersSagaOptions.Dispatch`; doc comment count corrected from "five" to "six" nested groups.
- `src/Orders/Infrastructure/Saga/ChannelSagaCommandSignal.cs` — `SingleReader` changed `true` → `false`; doc comment explains why.
- `src/Orders/Infrastructure/Saga/SagaCommandDispatchWorker.cs` — `ExecuteAsync` now fans out to `DegreeOfParallelism` parallel `ConsumeLoopAsync` calls via `Task.WhenAll`; each loop is otherwise byte-identical to the pre-fix body (one DI scope per item, same try/catch, same log line).
- `tests/Orders.UnitTests/SagaCommandDispatchWorkerTests.cs` — two new tests (`HeadOfLineBlocking_OrderBsCommandIsNotBlockedByOrderAsUnresponsiveDispatch`, `DegreeOfParallelism_BoundsConcurrentDispatchesAcrossOrders`), two new fakes (`PerOrderGatedFakeDispatcher`, `ConcurrencyTrackingFakeDispatcher`), `BuildProvider` extended to accept an optional `degreeOfParallelism`. One existing test changed: `OneFailingItem_DoesNotStopTheWorker` pinned to `degreeOfParallelism: 1` — its own claim ("one failing item does not stop the worker") never depended on cross-item order, only on both items eventually being processed; the exact-sequence assertion (`[first, second]`) was incidental to the pre-fix single-worker implementation and would become non-deterministic under real parallelism. Pinned to 1, with a comment explaining why, rather than weakened into a membership check — the new parallelism itself is proven separately, at the production default, by `HeadOfLineBlocking_OrderBsCommandIsNotBlockedByOrderAsUnresponsiveDispatch`.
- `tests/Orders.IntegrationTests/OperatorCancelRacesSagaForwardProgressTests.cs` — two comment blocks in `Confirmed_ReleaseWins` corrected in place; no assertion changed (see "Consequence recorded", below).
- `specs/order_saga_orchestrator/design.md` §5.5 — corrected in place (bullet 6, below). Nothing under `specs/shared/` touched.
- This file.

No change to `feature_list.json`, to any file under `specs/shared/`, or to any service other than Orders.

## Bullet 1 — enumeration, by content, before any fix

Command (run before any production change):

```
find . \( -name '*.cs' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "\.Reader\b"
find ./src \( -name '*.cs' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "ReadAllAsync\|await foreach"
find ./src \( -name '*.cs' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "Channel\.Create\|Channel<"
```

Complete output, one classification line per hit:

| Hit | Classification |
|---|---|
| `ChannelSagaCommandSignal.cs:38` `public ChannelReader<SagaCommandRef> Reader => _channel.Reader;` | The property **definition**, not a consumer. |
| `SagaCommandDispatchWorker.cs:50` `await foreach (var commandRef in signal.Reader.ReadAllAsync(...))` | **THE** consumer of `ChannelSagaCommandSignal.Reader` — the leader's own pre-dispatch finding, confirmed. Fixed by this feature: now run by `DegreeOfParallelism` parallel `ConsumeLoopAsync` invocations rather than once. |
| `src/Gateway/Application/Stream/StreamHub.cs:91` `return (subscriptionId, channel.Reader);` | A **different** `Channel<T>` (`Channel<StreamFrame>`), not `ChannelSagaCommandSignal`. One channel **per live SSE connection** (`Subscribe()`, `:82`), fed by a non-blocking `TryWrite` fan-out (`Publish`, `:52`) and drained by exactly one reader — that connection's own request-handling loop (`StreamEndpoints.cs:77-116`, `hub.Subscribe()` → `reader.ReadAsync` in a `while(true)`/`Task.WhenAny` loop scoped to that one HTTP request). No shared worker drains more than one client's channel, so no cross-client (analogue of cross-order) head-of-line blocking is even structurally possible — a slow SSE client can only ever stall its own channel, and `StreamHub`'s own doc comment already cites `ChannelSagaCommandSignal` as its single-consumer precedent. **Out of scope, structurally immune to this class.** |
| `StockRpcResponder.cs:118`, `BillingRpcResponder.cs:124`, `OrdersCreateResponder.cs:68/76/84`, `NatsStreamSignalSubscriber.cs:72` — five `await foreach (var message in connection.SubscribeAsync<byte[]>(...))` | NATS RPC responder / signal-subscriber loops — a **live transport subscription** driven by the NATS client library's own async enumerable, not a local `Channel<T>`/in-process buffered dispatch **queue** the application itself owns. The acceptance bullet's own wording ("every OTHER sequential drain of an in-process dispatch queue") is about a queue this codebase buffers work into before its own worker drains it — these five have no such buffer; each message is delivered directly off the subscription. **Out of scope by the letter of the bullet.** (Whether any of these five has an *independent* head-of-line concern of its own — e.g. one slow `orders.cancel` request stalling the next `orders.create` on the same subscription — is a legitimate question but a DIFFERENT one, over a DIFFERENT mechanism, for a DIFFERENT backlog entry if it turns out to be real; not investigated further here, and not something this feature's bounds (`src/Orders/` only) permit fixing even if it were.) |
| `ChannelSagaCommandSignal.cs:31` `Channel.CreateBounded<SagaCommandRef>(...)`, `StreamHub.cs:82` `Channel.CreateBounded<StreamFrame>(...)` | The two (and only two) `Channel<T>` constructions in `src/` — confirms the table above is exhaustive for "in-process dispatch queue" in the `Channel<T>` sense. |

**Repository-wide `Channel<T>` count: exactly two, both enumerated above.** No other sequential drain of an in-process dispatch queue exists in `src/`.

## Bullet 2 — reproduced first, on today's (pre-fix) code

Test: `SagaCommandDispatchWorkerTests.HeadOfLineBlocking_OrderBsCommandIsNotBlockedByOrderAsUnresponsiveDispatch` (unit-level, against a fake `ISagaCommandDispatcher` — no database, no real NATS/Kafka, matching this class's own existing precedent). Order A's dispatcher is gated to block **forever** (never released until the test's own teardown, simulating "the responder never answers" — deliberately far past even the ~16.5 s worst case, so the test proves the property rather than merely outrunning a slow-but-bounded delay). Order B's command is signalled strictly after A's, once A's dispatch is confirmed genuinely still blocked. Order B must reach dispatched within a 2 s bound.

Arming mutation (to prove this against **today's** pre-fix code, before applying the fix's `OrdersSagaOptions`/`ChannelSagaCommandSignal` changes): `SagaCommandDispatchWorker.ExecuteAsync` temporarily reverted to call `ConsumeLoopAsync` directly (the single-sequential-loop shape), ignoring `DegreeOfParallelism` entirely.

Verbatim failure (copied from the run, not retyped):

```
order B's command StockReserve for order 9d18ecf9-1be3-4d7a-8c14-0aff1f393715 did not reach dispatched within the 00:00:02 bound — the fast path's dispatch worker is head-of-line blocked behind order fe13f51b-7d38-4d3b-81fa-1feaa3914935's unresponsive dispatch (backlog id 80).
```

This is the SAME arming evidence bullet 5 asks for (see below) — one mutation, one test, recorded once.

## Bullet 3 — the concurrency model, stated with its cost, and the invariant enumeration

**Model adopted: bounded parallelism across orders — `OrdersSagaDispatchOptions.DegreeOfParallelism` (default 8) parallel consumer loops over the same channel, with NO per-order affinity or serialisation.**

**Cost, stated:** up to `DegreeOfParallelism` concurrent NATS RPC calls in flight from the fast path at once (bounded, not unbounded — proven by `DegreeOfParallelism_BoundsConcurrentDispatchesAcrossOrders`, below), each still going through its own DI scope exactly as before. A channel-drop storm (many orders' commands signalled at once) can now open up to 8 concurrent calls rather than serialising them one at a time — the entire point of the fix, and the reason the number is *bounded* rather than "one loop per signalled item": an unbounded fan-out was rejected for the same reason `ChannelSagaCommandSignal`'s own channel is bounded at 1 024 rather than unbounded — a slow-responder storm should degrade to latency (the sweeper backstop), never to an unbounded number of concurrent outbound NATS calls.

**Invariant enumeration — does anything in this saga rely on per-order dispatch ORDER? Enumerated, not assumed:**

1. **SO11's claim (`ISagaCommandStore.TryClaimAsync`, `EfCoreSagaCommandStore.cs:78-107`, design.md §6.3).** A single atomic conditional `UPDATE ... WHERE id=@id AND status IN ('pending','parked') AND (next_attempt_at IS NULL OR next_attempt_at <= @now)`, keyed by the row's own primary key. Two concurrent claim attempts on the SAME row race at the database, not in application code, and exactly one wins regardless of which of Orders' own threads issued it — this was already true before this fix (the sweeper and the fast path could already race for the same row). Concurrent claims on DIFFERENT rows (the normal case under this fix) were never contended at all. **Unchanged.**
2. **The SA-4 contested-resource race** (a cancellation's `stock.release` racing the confirmation's own `despatch.create` for the same order, `saga.md` §4.3 "The despatch already requested", `specs/shared/saga.md:226-230`, read from the shared spec, not amended): *"Fulfillment **shall** decide `stock.release` and `despatch.create` for one order under one lock, so exactly one of them takes it — and the cancellation therefore releases the stock **first** and lets Fulfillment decide."* The arbitration is explicitly Fulfillment's own lock over the RPC arrival order at Fulfillment, never Orders' own dispatch sequencing. This fix changes WHICH mechanism happens to determine arrival order in the common case (before: the single worker's own FIFO drain, so `despatch.create` — enqueued earlier, at confirmation — almost always dispatched before `stock.release`; after: whichever of two parallel loops' NATS round-trip completes first, genuinely raced) — but never changes WHO arbitrates, because Orders never arbitrated in the first place. **Unchanged as an invariant; changed as an empirical frequency — see "Consequence recorded", below.**
3. **Compensation ordering — `stock.release` before `credit.release`** (`SagaCommandKind.cs:15-33`, "reverse-order-of-acquisition compensation"). `CancelOrderCommandHandler` enqueues ONLY `stock.release` directly (`CancelOrderCommandHandler.cs:215`, per that file's own comment); `credit.release` is **never enqueued** until the triggering fact (`stock.released.v1`) is itself processed by `SagaFactHandler`'s ordinary Advance path and returns it as `CommandAfter` (`SagaStepTable`'s `stock.released.v1` row, `credit_approved`/`confirmed` variants). So `credit.release`'s `saga_commands` row does not EXIST until after a real Kafka fact round-trip has completed — this ordering is enforced by the fact-driven state machine (a row cannot be claimed before it is enqueued), never by this worker's own drain order. **Unchanged.**
4. **The `(order_id, command)` unique index** (design.md §6.3) — a step can never owe the SAME command twice, so there is never a scenario where two dispatch attempts for the SAME (order, command) pair are in flight at once under this fix; the claim's `affected == 0` no-op path (already existed) covers the only case where that could otherwise matter.
5. **Aggregate state** — the dispatcher never reads or writes the `Order` aggregate. The command's payload is serialised to JSON at ENQUEUE time (inside the fact's own transaction, design.md §5.1) and stored in the row; dispatch only reads that already-durable JSON and issues the RPC. There is therefore no read-then-act race on the aggregate at dispatch time regardless of how many dispatches run concurrently, for the same order or different orders. **Unchanged.**

**Conclusion: no invariant in this saga depends on per-order dispatch ORDER.** No per-order affinity was added.

## Bullet 4 — ported-idiom ledger row, verified against #7's own checkout (not transcribed from the brief)

**#7 relied on** `@nestjs/cqrs` 11.0.3's `registerSaga` (`node_modules/.pnpm/@nestjs+cqrs@11.0.3.../node_modules/@nestjs/cqrs/dist/event-bus.js:196`, `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs`) — read directly, not assumed:

```javascript
196: }), (0, operators_1.mergeMap)((command) => (0, rxjs_1.defer)(() => this.commandBus.execute(command)).pipe((0, operators_1.catchError)((error) => {
```

Confirmed: `package.json` for the resolved `@nestjs/cqrs` package reports `"version": "11.0.3"` exactly. `mergeMap` here carries **no concurrency-limit argument**, which is RxJS's own "unlimited concurrent inner subscriptions" default — every command's `commandBus.execute(command)` is subscribed to (and therefore begins running) as soon as it arrives on the stream, never waiting for a prior one to complete. This is what let #7 get concurrent dispatch "for free" from its framework, with no code of its own expressing a degree of parallelism at all.

`apps/orders/src/application/commands/saga-dispatch.handlers.ts:22-23` (read directly):

```typescript
22:  async execute(command: IssueStockReserveCommand): Promise<void> {
23:    await this.dispatcher.dispatch(UniqueId.from(command.orderId), 'stock.reserve');
24:  }
```

Confirmed: one of the five `Issue…Command` handlers `mergeMap` subscribes to concurrently; `dispatcher.dispatch` is the NATS issue (the direct analogue of `SagaCommandDispatcher.DispatchAsync`) — a slow or absent responder here blocks only THIS command's own `execute` promise, never another order's, because `mergeMap` already runs every such promise concurrently rather than sequentially.

**Both citations verified exactly as filed — file, line and content match the checkout.** Nothing in the brief's ledger row needed correcting.

**In #8 that property is supplied by** this fix: `SagaCommandDispatchWorker`'s `DegreeOfParallelism` parallel `ConsumeLoopAsync` loops (`src/Orders/Infrastructure/Saga/SagaCommandDispatchWorker.cs`), each independently awaiting its own `SagaCommandDispatcher.DispatchAsync` call — the .NET analogue of `mergeMap`'s per-command concurrent subscription, bounded rather than unlimited (§3, above, states why bounded was chosen over #7's own unlimited shape — a deliberate, recorded deviation, not an oversight).

**Guard:** `SagaCommandDispatchWorkerTests.HeadOfLineBlocking_OrderBsCommandIsNotBlockedByOrderAsUnresponsiveDispatch` (bullet 2/5) proves the property directly (order B is never blocked behind order A); `DegreeOfParallelism_BoundsConcurrentDispatchesAcrossOrders` proves the bound is real (§3) rather than accidentally unbounded or accidentally still serial.

## Bullet 5 — armed: restoring the single sequential drain fails the reproducing test

Protocol: `cp` backup → mutate → `dotnet build --no-incremental` → run the ONE named test → verbatim failure (copied) → restore from the backup → `cmp` (byte-identical) **and** `git diff --stat` (shows the real, intended diff against HEAD, since this file is tracked) → forced rebuild (`dotnet build --no-incremental`) → confirmed green.

- **Backup:** `cp src/Orders/Infrastructure/Saga/SagaCommandDispatchWorker.cs /tmp/.../scratchpad/SagaCommandDispatchWorker.cs.fixed.bak` (taken AFTER the fix was written, i.e. this is a backup of the FIXED file, mutated FROM there back to the pre-fix shape — the same file, both directions).
- **Mutation:** `ExecuteAsync` body replaced with `_ = options; return ConsumeLoopAsync(stoppingToken);` — the single-sequential-loop shape, `DegreeOfParallelism` read but discarded (kept to satisfy CS9113 "parameter is unread" as an error under this repo's analyzer settings, not a functional change).
- **Build:** `dotnet build tests/Orders.UnitTests/Orders.UnitTests.csproj --no-incremental` — 0 errors.
- **Named test:** `HeadOfLineBlocking_OrderBsCommandIsNotBlockedByOrderAsUnresponsiveDispatch`.
- **Verbatim failure:**
  ```
  order B's command StockReserve for order 9d18ecf9-1be3-4d7a-8c14-0aff1f393715 did not reach dispatched within the 00:00:02 bound — the fast path's dispatch worker is head-of-line blocked behind order fe13f51b-7d38-4d3b-81fa-1feaa3914935's unresponsive dispatch (backlog id 80).
  ```
  Names order B's own command (`StockReserve`) and the bound it missed (`00:00:02`), per the acceptance criterion's own wording.
- **Restore:** `cp` the backup back over the file. `cmp` reported byte-identical (silent success — confirmed by exit code, not merely absence of output). `git diff --stat -- src/Orders/Infrastructure/Saga/SagaCommandDispatchWorker.cs` showed `38 +++--` (the real, intended fix diff against the last commit) — a non-empty, expected diff, which is what a tracked file's restore should show (this is the case the arming protocol's own warning distinguishes: `git diff` on a tracked file IS meaningful evidence here, unlike on an untracked file where it prints nothing).
- **Forced rebuild:** `dotnet build tests/Orders.UnitTests/Orders.UnitTests.csproj --no-incremental` — 0 errors.
- **Confirmed green:** `dotnet test tests/Orders.UnitTests/Orders.UnitTests.csproj --no-build --filter "FullyQualifiedName~SagaCommandDispatchWorkerTests"` — 4/4 passed (591 ms). Full `Orders.UnitTests` project: 465/465 (10 s).

**One live defect found and fixed during arming itself, recorded rather than smoothed over:** the reproduction test's first draft called `worker.StopAsync(CancellationToken.None)` in its teardown without ever releasing order A's blocked gate. `BackgroundService.StopAsync(CancellationToken.None)` cancels the internal stopping token but then `await`s the `ExecuteAsync` task with NO timeout (the `cancellationToken` parameter, here `None`, is what would bound that wait) — and the fake dispatcher's `await gate.Task` never observed the cancellation token at all, so that one loop (and therefore `Task.WhenAll`) never returned. The confirming green run hung for ~9 minutes at ~1% CPU before this was diagnosed and killed — a genuine hang, not a slow suite. Fixed by releasing order A's gate in a `finally` block before calling `StopAsync`, plus a defensive `WaitAsync(TimeSpan.FromSeconds(10))` around `StopAsync` itself so a similar bug in a FUTURE test cannot hang the whole run silently again.

## Bullet 6 — `specs/order_saga_orchestrator/design.md` §5.5 corrected in place

Point 3 (the channel) and point 4 (the worker) in "The composition adopted" updated to describe `SingleReader = false` and the parallel-loop shape, with a forward reference. A new paragraph, **"Correction (backlog id 80) — the single worker moved the same defect one stage later, and it stood uncorrected until this fix,"** inserted after "the rejected alternative" paragraph: names precisely what `:327`'s own argument actually proves (an argument against serial awaiting of `DispatchAsync` behind ANY single point of consumption, not specifically the Kafka consume loop), states that the ORIGINAL composition moved that single point of consumption to `SagaCommandDispatchWorker` and left it serial there, cites the `Confirmed_ReleaseWins` test comment as where this was found, states the fix, and cites the invariant enumeration (§3 above) for why no per-order affinity was added. `specs/shared/` untouched — this file is `order_saga_orchestrator`'s own stack-specific design doc, not shared.

## Consequence recorded, not silently changed

Per the entry's own notes: before this fix, `Confirmed_ReleaseWins` (`OperatorCancelRacesSagaForwardProgressTests.cs`) depended on `SagaCommandSweeper` — not the fast path — to deliver `stock.release` while `despatch.create` was held in flight by the test's own gate, because the single worker was blocked on the gated RPC. After this fix, the fast path itself delivers `stock.release` directly (a DIFFERENT parallel loop than the one blocked on `despatch.create`), independent of the sweeper's schedule.

**Checked whether this test's own assertions encoded the old (sweeper-dependent) behaviour as a claim:** no — `WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.release", "sent", _wait)` only waits for the row to reach `sent`, never asserting WHICH mechanism delivered it. The test's PASS/FAIL behaviour is therefore unchanged by this fix; only its two comment blocks (the `HoldDespatchCreateGate.Reset()` explanation and the `TimeoutMs = 10_000` margin comment) described the old mechanism as current fact and are corrected in place — see "Files touched", above — rather than weakened or deleted.

**The empirical frequency this fix changes, stated honestly:** before this fix, `despatch.create` (enqueued at confirmation, dispatched by the single worker in FIFO order) almost always reached Fulfillment BEFORE a same-order `stock.release` enqueued later by an operator cancellation, because the single worker processed items strictly in signal order — so "the despatch wins" (`Confirmed_DespatchWins`) was the practically common outcome and "the release wins" (`Confirmed_ReleaseWins`) required either the sweeper or a slow despatch. After this fix, both commands can be dispatched by two DIFFERENT parallel loops at nearly the same time, so which one reaches Fulfillment's lock first depends on real NATS round-trip timing — a genuine race, exactly as `saga.md` §4.3 always specified it should be, rather than one Orders' own single-worker implementation detail had been silently deciding in `despatch.create`'s favour. Both outcomes remain correct and both are still covered by an existing, unmodified test (`Confirmed_ReleaseWins`, `Confirmed_DespatchWins`) — this fix makes the RACE Fulfillment was always meant to arbitrate an actual race rather than a near-certainty, it does not introduce a new outcome.

## Defeat list run against `HeadOfLineBlocking_OrderBsCommandIsNotBlockedByOrderAsUnresponsiveDispatch`

| # | Attack | Ran? | Result / why N/A |
|---|---|---|---|
| 1 | Delete the behaviour | **Ran** | Bullet 5's arming — fails with the verbatim message above. |
| 2 | Corrupt a payload field the test supplied | N/A | This guard asserts TIMING (a bound), not a payload field's value — there is no field to corrupt. |
| 3 | Substitute a valid sibling identifier | N/A | No `MSSQL_DB_*`/subject/topic/env-var literal is involved in this unit test. |
| 4 | Shadow the pattern from a comment/string literal | N/A | This is a runtime behavioural test against a fake, not a static text/pattern scanner. |
| 5 | Hide in a dead region (`#if false`) | N/A | Same reason as #4 — no static scanning involved. |
| 6 | Hide in a raw/verbatim string | N/A | Same reason as #4. |
| 7 | Drop an optional element entirely | N/A | No optional element is compared by this guard. |
| 8 | Compare a literal to a literal | N/A | The assertion compares a REAL elapsed-time observation (`Completed(orderB)` actually completing or not within the bound) against a literal bound — not two literals against each other. |
| 9 | Satisfy the closer half of a two-part claim, leave the premise half stale | **Ran** | The test's own premise — "order A's dispatch is genuinely still blocked" — is asserted directly (`Assert.False(dispatcher.Completed(orderA).IsCompleted, ...)`) and `Started(orderA)` is awaited before signalling B, so a mutation that never actually invoked A's dispatch at all (leaving nothing to be blocked behind) would fail THIS assertion first, not silently pass the closing one. |
| 10 | Let a build-output copy join the population | N/A | Not an enumeration-over-a-population guard. |

Six of ten rows are N/A because this guard is a runtime concurrency/timing property, not a static-analysis or population-enumeration guard — the same reasoning applies to all six and is stated once rather than six times. Rows 1 and 9 are the two that could plausibly apply to a concurrency guard shaped like this one, and both were run.

## Full-suite confirmation

- `Orders.UnitTests`: **465/465** (463 baseline + `HeadOfLineBlocking_OrderBsCommandIsNotBlockedByOrderAsUnresponsiveDispatch` + `DegreeOfParallelism_BoundsConcurrentDispatchesAcrossOrders`), 10 s.
- `Architecture.Tests`: **35/35**, unchanged from baseline — no domain-purity or layering rule affected (all production changes are in `Infrastructure/`; `IOptions<OrdersSagaOptions>` was already an Infrastructure-layer dependency of this class before this fix).
- `Orders.IntegrationTests`: **146/146**, unchanged count from baseline, real MS-SQL/Kafka/NATS Testcontainers, 11 m 2 s. `OperatorCancelRacesSagaForwardProgressTests` (containing the two comment-only edits) is entirely within this run — `Confirmed_ReleaseWins`, `Confirmed_DespatchWins`, both `StockReserved_LateApproval_*` tests and `Confirmed_OperatorFirst_LateDespatchedV1IsIgnoredByPreconditionUnmet` all passed with zero assertion changes, confirming the "Consequence recorded" section's own claim that this test's PASS/FAIL behaviour was never coupled to which mechanism (fast path vs. sweeper) delivered `stock.release`.
- `dotnet build OrderToCash.sln --no-incremental`: clean, 0 warnings, 0 errors, all 26 projects (8 service + 18 test/architecture projects) — confirms no other service references `OrdersSagaOptions`/`OrdersSagaDispatchOptions`/`ChannelSagaCommandSignal`/`SagaCommandDispatchWorker` (independently confirmed by `grep -rln "OrdersSagaOptions\|OrdersSagaDispatchOptions" --include=*.cs . | grep -v '/bin/\|/obj/'` — every hit is under `src/Orders/` or `tests/Orders.*Tests/`).
- `dotnet format OrderToCash.sln --verify-no-changes --include <every touched .cs file>`: exit 0, no output.

**Reconciliation against the stated baseline (1 905 tests / 18 projects):** this feature added exactly 2 tests, both in `Orders.UnitTests` (463 → 465). No other project's test count changed — `Orders.IntegrationTests` (146) and `Architecture.Tests` (35) are unchanged from baseline, confirmed by direct runs above, not inferred. Expected new repository-wide total: 1 905 + 2 = **1 907**. Not independently re-run for every one of the other 15 projects (Notifications/Projector/Billing/Fulfillment/Gateway/Seed/Cqrs/Contracts/SharedKernel × Unit/Integration), because this feature's bounds (`src/Orders/`, its test projects, `specs/order_saga_orchestrator/design.md`, this record) make them structurally unreachable by this change, and the solution-wide build above already confirms nothing outside Orders references the touched types.

## What I could not do, and why

Nothing in the six acceptance bullets was left undone. Two things worth flagging as deliberately out of scope rather than overlooked:

1. **The five NATS RPC responder/subscriber `await foreach` loops** found by bullet 1's own enumeration (`StockRpcResponder`, `BillingRpcResponder`, `OrdersCreateResponder` ×3, `NatsStreamSignalSubscriber`) may or may not have an analogous head-of-line property of their own — a slow handler for one inbound request potentially delaying the next on the same subscription. This was NOT investigated beyond classifying them as out of scope for THIS entry (they are not `ChannelSagaCommandSignal` consumers and not `Channel<T>`-based in-process dispatch queues, which is what the acceptance bullet's wording covers), and this feature's bounds (`src/Orders/` only) would not have permitted fixing any of the four that live outside Orders even if it turned out to be real. If it is real, it is a new backlog entry, not a silent extension of this one.
2. **A quantitative measurement of the empirical race-frequency shift** described in "Consequence recorded" (how often `Confirmed_ReleaseWins` vs. `Confirmed_DespatchWins` now occurs under real network timing, versus the near-determinism the single worker used to produce) was not measured — both outcomes are already deterministically forced by each test's own gates (`RecordingFulfillmentStandIn.HoldDespatchCreateGate`, explicit wait-for-`sent` ordering), so neither test exercises the natural, un-gated race frequency at all, and this feature's bounds do not include building a new statistical test to characterise it. The correctness claim (both outcomes remain correct, both remain covered) does not depend on the frequency, so this was not pursued further.

## What surprised me

The arming pass for bullet 5 hung the confirming "green" run for about nine minutes before I diagnosed it (see "Bullet 5" above) — not because the production fix was wrong, but because my OWN reproduction test's teardown called `BackgroundService.StopAsync(CancellationToken.None)` without ever releasing the fake dispatcher's gate, and `CancellationToken.None` means that wait has no bound. It is the same class of lesson the arming protocol itself keeps surfacing (a check that cannot fail, here inverted into a check that cannot finish) — a fake built to prove a blocking property has to be just as careful about its OWN teardown as the production code it is testing, or the test harness inherits the very defect it is trying to demonstrate.
