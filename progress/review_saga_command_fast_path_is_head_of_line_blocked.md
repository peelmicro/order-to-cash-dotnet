# Review — `saga_command_fast_path_is_head_of_line_blocked` (backlog id 80, phase 14, `sdd: false`)

## Verdict: **APPROVED**

Reviewer: one session, 2026-09-12, ≈18:50 → ≈19:25 CEST. **Seven mutation probes** run by me (three families: deletion, payload corruption, identifier substitution), three enumerations re-run independently, two ledger citations re-read out of #7's checkout, one confirming `Orders.UnitTests` run (**465/465**, 10 s). **No full-suite re-run** — the leader supplied the reading (1907 total, 1 failed, 1906 passed across 18 projects) and the claim under test here is not about the full suite. What I ran instead is listed below, probe by probe.

**Transition I would make (I did not write the file):** `feature_list.json` id 80 → **`done`**, and a `progress/history.md` entry carrying the effort record in §7. Two numbered backlog entries are recommended in §6 (A8, A9); I have not written them.

---

## 1. Acceptance bullets → evidence

`sdd: false`: the specification of record is id 80's six acceptance bullets. No `R<n>` is claimed by this feature and no `specs/shared/test-matrix.md` row is owed — a content grep of the matrix for `SagaCommandDispatchWorker`/`fast path`/`dispatch worker` returns nothing, and the `SO<n>` ids this work touches are `design.md`-level claims, not EARS requirements. `specs/shared/test-matrix.md` **is** modified in the tree, with mtime `12:48:59`, i.e. five hours before this feature's dispatch anchor (`init_id80_start.log`, `17:50:22`) — it is id 72's, not this feature's.

| # | Bullet | Verified by me | Result |
|---|---|---|---|
| 1 | Enumerate every `ChannelSagaCommandSignal.Reader` consumer and every other sequential drain of an in-process dispatch queue, by content, path-excluded | I re-ran all three of the record's commands verbatim (§2) | **Met.** My output reproduces the record's table hit-for-hit. Two cosmetic slips, A1/A2 below — no hit is unclassified in a way that changes the conclusion |
| 2 | Reproduced FIRST on pre-fix code; sweeper disabled so it cannot mask | Probe **P1**: reverted `ExecuteAsync` to the single sequential loop, `--no-incremental`, ran the named test | **Met.** RED with the record's verbatim message, order id differing only as expected. The sweeper cannot mask here structurally: the test builds a `ServiceCollection` containing only the signal, the fake dispatcher, options and the worker — no sweeper type is registered at all |
| 3 | Concurrency model stated with its cost; invariants that rely on per-order dispatch ORDER named and shown unchanged | Read all five enumerated points against the source; hunted for a sixth (§3) | **Met**, and the conclusion holds. The enumeration is *incomplete as a document* — two further same-order interleaves are now genuinely raced and are not named (A3). I checked both: the spec prescribes the outcome and existing tests cover both orderings |
| 4 | Ported-idiom ledger row, in `impl_<feature>.md` (correct home for `sdd: false`) | Re-read `event-bus.js:196`, `package.json` version, `saga-dispatch.handlers.ts:22-23` in #7's checkout myself | **Met.** Both halves hold. `mergeMap` with no concurrency argument at the cited line; `"version": "11.0.3"` exact; the handler body is as quoted. Guard named and armed |
| 5 | Armed: restoring the single sequential drain fails the reproducing test, naming order B's command and the bound | Probe **P1** (mine, independent of the implementer's) | **Met.** `Failed: 2, Passed: 2` — the message names `StockReserve` and `00:00:02` |
| 6 | `design.md` §5.5 corrected in place | `git diff -- specs/order_saga_orchestrator/design.md` | **Met.** Points 3 and 4 rewritten, a "Correction (backlog id 80)" paragraph added after "the rejected alternative". `specs/shared/` untouched; `init.sh`'s own parity check reports the shared spec byte-identical to #7 |

## 2. The enumeration, re-run by me — complete output, classified

```
find . \( -name '*.cs' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "\.Reader\b"
./src/Gateway/Application/Stream/StreamHub.cs:91            per-connection Channel<StreamFrame>, one reader per HTTP request — not this channel
./src/Orders/.../ChannelSagaCommandSignal.cs:38             the property definition
./src/Orders/.../SagaCommandDispatchWorker.cs:50            THE consumer — the subject of this feature
```
```
find ./src \( -name '*.cs' \) ... | xargs -0 grep -n "ReadAllAsync\|await foreach"        → 7 hits
  6 × NATS subscription loops (StockRpcResponder:118, BillingRpcResponder:124, OrdersCreateResponder:68/76/84, NatsStreamSignalSubscriber:72) — live transport subscriptions, no application-owned buffer
  1 × SagaCommandDispatchWorker.cs:50 — the subject
```
```
find ./src \( -name '*.cs' \) ... | xargs -0 grep -n "Channel\.Create\|Channel<"          → 3 hits
  StreamHub.cs:39   field type declaration (ConcurrentDictionary<Guid, Channel<StreamFrame>>) — not a construction
  StreamHub.cs:82   construction, per-connection
  ChannelSagaCommandSignal.cs:31 construction, the subject
```

**Both of the record's negative claims hold**: exactly two `Channel<T>` constructions exist in `src/`, and `SagaCommandDispatchWorker.cs:50` is the only consumer of this channel. `AddHostedService<SagaCommandDispatchWorker>` appears **once** repository-wide, so the fan-out is `DegreeOfParallelism` loops inside one hosted service, not N hosted services.

## 3. Claim 1 — "no invariant depends on per-order dispatch ORDER", tested for completeness

I re-derived each of the five enumerated points against source, and then hunted for a sixth along the axes the brief named.

| Point | My check | Verdict |
|---|---|---|
| SO11 atomic claim | `EfCoreSagaCommandStore.TryClaimAsync:78-107` — one conditional `ExecuteUpdateAsync` keyed on `(order_id, command)` with the status/lease predicate inline; `affected == 0` returns `null` silently | Holds. Already contended pre-fix (sweeper vs. fast path) |
| SA-4 arbitration is Fulfillment's lock | `saga.md` §4.3 read from the shared spec: *"Fulfillment **shall** decide `stock.release` and `despatch.create` for one order under one lock"* | Holds. Orders never arbitrated |
| `credit.release` cannot exist before `stock.released.v1` | `CancelOrderCommandHandler.BeginStockReleaseCompensationAsync` enqueues `stock.release` **only**; `credit.release` appears solely as `SagaStepTable`'s `stock.released.v1` → `CreditApproved`/`Confirmed` Advance `CommandAfter`, i.e. after a real Kafka round trip | Holds. Ordering is enforced by the state machine, not by drain order |
| `(order_id, command)` unique index | Enqueue's duplicate-key path returns `AlreadyEnqueued`; a second in-flight dispatch of the same pair is impossible | Holds |
| Dispatcher never touches the aggregate | `SagaCommandDispatcher` reads the row's already-serialised payload JSON and issues; the only aggregate write on this path is `SagaFirstParkDeadLetterHandler`, which is itself gated by `TryClaimDeadLetterAsync`'s conditional UPDATE | Holds |

**Sixth candidates, checked rather than assumed:**

- **Sweeper racing the fast path for the same row.** Unchanged in kind. `ClaimDueAsync` uses `WITH (UPDLOCK, READPAST, ROWLOCK)` inside its own transaction and only takes `pending` rows past `PendingGraceMs`; `TryClaimAsync` stamps a lease. The race was already live pre-fix; eight loops raise its frequency, not its arbitration.
- **Lease expiry under a slow dispatch.** `LeaseMs` 60 000 against the documented 16 500 ms worst case — 3.6× headroom, unchanged by parallelism (the loops do not contend for CPU in any blocking way; every wait is an `await`).
- **Retry/backoff interleaving.** `TaskDelaySagaRetryDelay` is stateless; the backoff is computed from `claimed.Attempts` and the policy, both per-call locals. No shared state.
- **DI scope per dispatch — eight concurrent scopes.** `ISagaCommandStore`, `ISagaCommandDispatcher`, `ISagaCommands`, `ISagaFirstParkDeadLetterHandler` and `OrdersDbContext` are all **scoped**, and the worker creates one scope per item, so each concurrent dispatch owns its own `DbContext`. The two singletons on the path (`INatsConnection`, `ISagaRetryDelay`) are thread-safe by contract; `NatsSagaCommandsAdapter`'s own header records that it builds a **fresh `NatsHeaders` per request** precisely because that type is not thread-safe. Nothing in the dispatch path assumes single-threaded entry.
- **Two further same-order interleaves this change converts from near-deterministic to genuinely raced** — see **A3**. Both are prescribed by `saga.md` §4.3 and already covered by tests in both orderings.

**I agree with the claim.** No invariant in this saga depends on per-order dispatch order.

## 4. Claim 2 — the disclosed behaviour change, judged against the spec

**The new behaviour is closer to `specs/shared/saga.md` §4.3, not further.** The spec says the cancellation releases the stock **first** and *"lets Fulfillment decide"*, with both outcomes ("the release wins" / "the despatch wins") legal. Pre-fix, Orders' single FIFO worker silently decided that race in `despatch.create`'s favour almost every time — an implementation detail arrogating an arbitration the spec assigns to Fulfillment's lock. Post-fix, both commands leave Orders concurrently and Fulfillment's lock decides, which is what the spec describes. The *enqueue* ordering the spec actually mandates ("released first") is untouched: `CancelOrderCommandHandler` still enqueues `stock.release` and nothing else.

**`Confirmed_ReleaseWins` was corrected honestly, corroborated independently of the implementer's word.** Id 62's own review record quotes that test's assertions at the time it was approved — `:117 Assert.Equal(["stock.release"], fulfillment.CommandsProcessed)`, plus the F3 `DeadLetteredAt` / `"rejected"` pair. All three survive verbatim in the current file (now `:135`, `:162`, `:163`), and the wait helper is still `WaitForSagaCommandCountAsync(..., "stock.release", "sent", _wait)`, which never asserted *which* mechanism delivered the row. Only the two comment blocks changed. No assertion was weakened, deleted or retimed.

## 5. Defeat list (`CLAUDE.md`, "Run the defeat list against your own guard BEFORE submitting it") — every row, run by me

Backups taken with `cp`; each mutation built `--no-incremental`; restore by `cp` + `touch`, verified with `cmp` (byte-identical on all three files after every probe) **and** `git diff --stat` (these files are tracked, so the diff is meaningful — it shows the intended fix, 73 insertions / 7 deletions across the three). Confirming run after a forced rebuild: **465/465**.

| # | Attack | Ran? | Result |
|---|---|---|---|
| 1 | Delete the behaviour | **P1** | **RED.** Single sequential loop restored → `Failed: 2` — `HeadOfLineBlocking…` with the verbatim id-80 message, **and** `DegreeOfParallelism_Bounds…` with *"observed only 1 concurrent dispatch(es)"*. Two independent guards catch the regression |
| 2 | Corrupt a payload field | **P4a, P4b** | **RED both.** Forcing the dispatched command to `CreditHold` → `Failed: 2` (`SO10…`, `OneFailingItem…`). Replacing the order id with `Guid.NewGuid()` → `Failed: 4`, the whole class. The record marked this row N/A ("no field to corrupt"); it was wrong to, and the news is good — the payload *is* guarded, by the two pre-existing tests |
| 3 | Substitute a valid sibling identifier | **P2, P3** | **RED both.** `Dispatch.DegreeOfParallelism` → `Command.MaxAttempts` (a real sibling in the same options object, value 3) → *"observed 3 concurrent dispatches with DegreeOfParallelism=2 — the worker exceeded its own configured bound"*. And the production default `8` → `1` → the head-of-line test fails. **That second probe is the provenance answer**: the reproducing test passes no degree at all, so it reads the real production default, and the default itself is guarded rather than supplied by the test |
| 4 | Shadow the pattern from a comment / string literal | N/A | The guards execute code against a live worker; no static text scanning anywhere |
| 5 | Hide the real thing in a dead region | N/A | Same — and the arming build is the same compiler the tests run against |
| 6 | Hide it in a raw/verbatim string | N/A | Same |
| 7 | Drop an optional element entirely | **Ran, as P6** | **GREEN — the one probe that survived.** Deleting the `Math.Max(1, …)` clamp leaves all 4 tests passing. See **A4**: not exploitable today, but the guard does not exist |
| 8 | Compare a literal to a literal | N/A by inspection | `MaxObservedConcurrency` is an `Interlocked` observation of real concurrent entries and `Completed(orderB)` is a real completion; both sides are observations, not constants |
| 9 | Satisfy the closer half, leave the premise half stale | Checked | The premise is asserted, not assumed: `Started(orderA)` is awaited and `Assert.False(dispatcher.Completed(orderA).IsCompleted, …)` fires first. P4b confirms it empirically — corrupting the order id fails the *premise* wait, not the closing bound |
| 10 | Let a build-output copy join the population | N/A | Not a population-enumeration guard. My own enumerations in §2 exclude `bin/`/`obj/` **by path**, at `find`, not by post-filtering output |
| — | **New: the pairing the fix depends on** (`SingleReader`) | **P5** | **GREEN.** Setting `SingleReader = true` while eight loops read is undefined behaviour under `System.Threading.Channels`' contract, and nothing fails. See **A6** — this one is arguably untestable, and I do not ask for a guard |

**Two probes defeated nothing but are worth recording as shapes tried:** P5 and P6. Everything else killed. Reporting all seven here rather than one per round, per the phase-14 rule.

## 6. Defects and advisories

**Blocking defects: none.**

- **A1 — a count word, in the enumeration cell (`impl_….md:40`).** The cell says *"five `await foreach`"* and then lists **six** line references (`118`, `124`, `68/76/84`, `72`). My re-run returns six. No hit is unclassified; only the word is wrong. `CLAUDE.md`: a claim of completeness is a count, and a count is a reading.
- **A2 — an unclassified hit (`impl_….md:41`).** `StreamHub.cs:39` (`ConcurrentDictionary<Guid, Channel<StreamFrame>>`) is returned by the record's own third command and appears nowhere in its table. The claim it supports ("exactly two constructions") is true — that hit is a field-type declaration — but the rule wants one classification line per hit, so a future reader re-running the command sees three hits against a two-row table.
- **A3 — the invariant enumeration is complete in its conclusion and incomplete in its inventory (`impl_….md:65-73`).** Two further same-order interleaves become genuinely raced by this change and are not named:
  1. **An operator cancel at `stock_reserved` vs. a still-unsent `credit.hold`.** Pre-fix, `credit.hold` (signalled earlier) always went first; now they race, so Billing can receive a hold for an order whose stock is already released. **Safe and already guarded** — `saga.md` §4.3's *"A credit approval that arrives after the cancellation"* prescribes exactly the outcome, `SagaFactHandler.cs:95-125` implements it, and `StockReserved_LateApproval_AfterStockReleased`, `…BeforeStockReleased` and `…WithTheSweeperDisabled_TheFastPathAloneDeliversCreditRelease` cover both orderings plus the sweeper-free path.
  2. **`credit.release` vs. a still-pending `despatch.create` on the release-wins path.** Also prescribed: *"A `despatch.create` that reaches Fulfillment after the release is refused (`PRECONDITION_FAILED`) and emits no fact… no retry, no dead-letter"* — which is precisely what `Confirmed_ReleaseWins` asserts (`Assert.Null(despatchRow.DeadLetteredAt)`, `Assert.Equal("rejected", …)`).
  Record-only correction; the conclusion stands unchanged.
- **A4 — the `Math.Max(1, …)` clamp is unguarded (P6 green).** With `DegreeOfParallelism <= 0`, `Enumerable.Range(0, 0)` makes `Task.WhenAll` complete **immediately**: the hosted service reports healthy while the fast path consumes nothing at all, silently demoting every saga command to the 30 s sweeper. One `[Theory]` case at `degreeOfParallelism: 0` asserting a dispatch still happens would close it. Not reachable today only because of A5.
- **A5 — `DegreeOfParallelism` is not bound from the environment.** `OrdersProgramConfiguration.ConfigureSaga` reads `KAFKA_BOOTSTRAP_SERVERS`, `FACT_RETRY_*` and the DLQ broker, and never touches `Dispatch`. Production therefore runs at a hard-coded 8 with no operator control under load. Defensible (the number is documented and bounded) but undisclosed. If it is ever bound, id 56/67's env-read guard convention applies to it, including the sibling-substitution guard.
- **A6 — the `SingleReader = false` pairing is unguarded, and I do not ask for a guard (P5 green).** Restoring `true` under eight readers is undefined behaviour, not defined-and-wrong, so no test can reliably observe it. The doc comment at `ChannelSagaCommandSignal.cs:15-26` explains the coupling, which is the right instrument. Recorded so the next reader does not mistake the green for safety.
- **A7 — #7's *tests* were not enumerated (`CLAUDE.md`, "when you port a mechanism, port its guards").** The record enumerates #7's **source** (correctly, with verified citations) but never asks *what did #7 check about this, and does an equivalent exist here?* — the omission that cost phase 13 two rejections. **I ran it, and it is clean:** `apps/orders/src/application/commands/saga-dispatch.handlers.spec.ts` holds 8 cases, all of the form *"handler dispatches (orderId, command)"* plus a no-op case (#8's equivalents: `OrderSagasTests`, `SagaCommandDispatchWorkerTests.SO10…`/`OneFailingItem…`); `application/sagas/order.sagas.spec.ts` holds 8 mapping cases plus a no-termination guard; and a **content** grep for `head-of-line|blocks|blocked|in parallel|concurrently` across every `apps/orders/**/*.spec.ts` returns no assertion about concurrent or non-blocking dispatch. **No #7 guard was dropped** — consistent with the ledger's own claim that the property came free from `mergeMap` and #7 never guarded it. Approval does not depend on this being added to the record, because the search result is here.
- **A8 — residual, and it should be routed rather than narrated. Recommend a numbered backlog entry.** The fix **bounds** the defect class, it does not eliminate it: with one unresponsive responder and enough traffic, eight concurrently-stuck dispatches restore the original stall for the ninth order, throughput degrading to ~8 commands per 16.5 s until the 30 s sweeper. The record states the bound and why bounded beat unbounded, but never states this residual. **No `SA-n` is owed**: I checked `specs/shared/` and it prescribes nothing about dispatch concurrency — the degree of parallelism is implementation-owned, exactly as `saga_commands` itself is, so an amendment would be the expensive routing on an unchecked premise `CLAUDE.md` warns about.
- **A9 — no integration-level assertion of the new concurrency, though it is now exercised everywhere.** All three guards are unit-level against a fake `ISagaCommandDispatcher`. The mitigant is real and I verified it: `SagaIntegrationTestSupport.StartHostAsync` overrides `Kafka`, `Command`, `Sweeper` and `DeadLetter` and **does not override `Dispatch`**, so all 146 `Orders.IntegrationTests` boot the real host at DoP 8 over real MS-SQL/NATS/Kafka and pass. The concurrency is exercised; it is simply never asserted, and no test drives N concurrent real dispatches through EF Core. Worth a backlog line given that a SQL Server deadlock is live in a sibling concurrency test (id 87).

**Routing statement, per `CLAUDE.md`.** No finding here has its root cause in `specs/shared/`, and nothing in this review is discharged by "the next feature that touches X". A8 and A9 are offered as numbered backlog entries for the leader to file; I have not written `feature_list.json`.

## 7. The red suite — I agree with the id 87 attribution, checked rather than accepted

`OutboxRelayConcurrencyTests` is `[Collection(MsSqlCollection.Name)]` over `MsSqlContainerFixture` and constructs `OutboxRelay` objects directly through a private `BuildRelay(...)` factory at `:44`, `:45`, `:102`, `:160`, `:182` — **no host is booted**, so `SagaCommandDispatchWorker`, `ChannelSagaCommandSignal` and `OrdersSagaOptions` are never constructed and id 80's parallelism cannot reach that path. `OutboxRelay.cs` carries mtime `2026-09-10 21:48` and an **empty** `git diff`. The attribution to id 87 stands; I did not re-diagnose it.

## 8. `CHECKPOINTS.md` — every applicable box walked

**C1 — harness complete:** `[x]` — `init.sh` run at the dispatch anchor (`17:50:22`) closes with *"environment and state are coherent"*; all five agent definitions and the six harness files present.
**C2 — state coherent:** `[x]` — exactly **one** `in_progress` (id 80), 59 `done`, 26 `pending`, every status valid; `progress/current.md` describes this session, not a leftover.
**C3 — architecture:** `[x]` — the six files this feature wrote contain no `Domain/` file; all production changes are in `Orders/Infrastructure/`; no new shared runtime code, no cross-service DB access, no new inter-service interaction to classify (the change is intra-process). `Architecture.Tests` **35/35** is the leader's full-suite reading, **not re-run by me**, and the justification for not re-running is that no namespace, reference or project relationship changed — the diff adds a class to an existing Infrastructure file and rewrites one method body.
**C4 — verification real:** `[ ]` — **honestly open, and not because of this feature.** `./quality.sh` was not run by me (instructed) and the suite carries one red, `OutboxRelayConcurrencyTests.OI4_…`, attributed to id 87 and corroborated in §7. Sub-boxes: domain tests pure `[x]`; Testcontainers against real brokers `[x]`; **No Jest** `[x]`; coverage not re-measured `[ ]` (unaffected — two tests added, no production code removed).
**C5 — session closed cleanly:** `[ ]` — **one box outstanding and it is the closing condition**: `progress/history.md` has **no entry for id 80 yet**, so the effort record does not exist. Numbers in §9 for the leader to use. No suspicious untracked files; no commit was made by me; `feature_list.json` not written by me.
**C6 — SDD:** **N/A** — `sdd: false`, no `specs/<feature>/` is owed. (The `design.md` this feature corrected belongs to `order_saga_orchestrator`, a different, already-closed feature.)
**C7 — reuse fidelity:** `[x]` — `specs/shared/` untouched by this feature; `init.sh`'s own parity check reports the shared spec **byte-identical to #7 across 6 files**, `test-matrix.md` exempt. The only `specs/` change this feature made is `order_saga_orchestrator/design.md`, which is #8's own stack-specific document. No `R<n>` id is reused or claimed here.

## 9. Effort record — the numbers, for the leader to append to `progress/history.md`

The feature is not closeable without this entry; I cannot write it under my bounds, so the measurements are here.

- **1 implementation session, ≈34 min** — bounded below by the dispatch anchor `init_id80_start.log` at **17:50:22** and above by the record's last save at **18:24:21**. File mtimes inside it: `OrdersSagaOptions.cs` 17:55:26, `ChannelSagaCommandSignal.cs` 17:55:39, `SagaCommandDispatchWorker.cs` 17:57:54, `OperatorCancelRacesSagaForwardProgressTests.cs` 18:01:01, `design.md` 18:01:45, `SagaCommandDispatchWorkerTests.cs` 18:08:23. **Six files, one service.** Includes the ≈9-minute self-inflicted hang the record discloses (a teardown awaiting `StopAsync(CancellationToken.None)` behind an unreleased gate) — worth carrying into the entry, because it is the cost of testing a blocking property with a blocking fake.
- **1 review session (this one), ≈35 min**, 7 mutation probes across three families, 3 enumerations re-run, 2 #7 citations re-read, 1 confirming 465-test run. **Approved at review round 1** — the second feature in phase 14 to need only one round.
- **Suite at close: 1907 total, 1906 passed, 1 failed** (the id 87 deadlock, unrelated, §7). Reconciles to the 1905 baseline + exactly this feature's 2 new tests, and I re-measured the Orders slice myself: `Orders.UnitTests` **465/465**.
- **Zero new NuGet packages.**
- **#7 counterpart: none, and that is the benchmark datum.** #7 never built this — `@nestjs/cqrs` 11.0.3's `registerSaga` subscribes every command through `mergeMap` with no concurrency argument, so #7 got bounded-free concurrency from its framework and wrote no code, no option and (per my enumeration in A7) no test for it. There is no ratio. The whole ≈34 min is **#8-only cost created by hand-rolling the dispatcher** — the same parity trade-off `CLAUDE.md` accepts for Notifications and Projector, here showing up as a defect that shipped and had to be found later. **For #9:** FastAPI has no `mergeMap` either, so this property must be built a third time, and it belongs in the plan at the moment the dispatch worker is written rather than as a phase-14 backlog entry.
