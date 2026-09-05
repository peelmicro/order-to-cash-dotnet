# Review — `fulfillment_stock` (feature 17, phase 9) — pass 1

**Verdict: REJECTED.** One blocking defect (**D1**), five advisories. Everything else in this feature is strong, and the report says so below in as much detail as the defect: the ported-idiom ledger did real work on its first outing, four of my own independent mutation probes were killed by named tests, the two gate rulings are implemented as ruled, and the live-stack claims are true against the running databases — I verified them by query, not by reading the report.

The defect is one omission, of exactly the class this repository keeps paying for and of exactly the class #7 was rejected for on **this same feature**: **the `stock.rejected.v1` branch's only observable output is never asserted by any test above the pure domain, and I proved it by mutation — the whole suite stays green when the rejected path's persistence is deleted.**

---

## 1. What I ran, and what I did not

Per `CLAUDE.md`'s reviewer rule (*probe the claims, do not re-run the world*), I did **not** re-run `./quality.sh` in full. What I ran independently:

| Run | Result |
|---|---|
| `dotnet test tests/Fulfillment.UnitTests` | **79/79 green** |
| `dotnet test tests/Fulfillment.IntegrationTests` (full, real MsSql/NATS/Kafka containers) | **48/48 green, 2 m 44 s** |
| `dotnet test tests/Orders.UnitTests` | **254/254 green** |
| `dotnet test tests/Architecture.Tests` | **16/16 green** — C3 verified by running the NetArchTest suite, not by eye |
| `./init.sh` | **exit 0** — 46 features, tripwire `no feature lost, no done reverted` |
| Six independent mutation probes (below) | 5 killed, **1 survived — D1** |
| Six read-only query batches against the **live** `otc_orders` / `otc_fulfillment` databases | every `FS17` claim in the impl report confirmed |
| `diff -rq specs/shared` against the #7 checkout | only `test-matrix.md` differs; its `git diff` is column 5 + the §1 counts, nothing else |
| `grep -rniE "IF NOT EXISTS|MERGE " src/Fulfillment` | **no match** — ledger L5's absence confirmed, as `tasks.md` D3 instructed the review to check |

All probe files were restored from backup copies taken first (`/tmp/.../scratchpad/backups/`), never with `git checkout --`, `cmp`-verified identical, force-rebuilt (`dotnet build --no-incremental`), and the confirming green runs above were made **after** the restores. `git status` shows no reviewer residue.

---

## 2. The blocking defect

### D1 — the `stock.rejected.v1` branch is unguarded above the domain, and its deletion survives the entire suite

**Where:** `tests/Fulfillment.IntegrationTests/StockReserveTests.cs:75-102` (`RejectedPath_ZeroRowsCreated_AndOneStockRejectedV1NamingRequestedAndAvailable`) and `tests/Fulfillment.IntegrationTests/StockReserveRaceTests.cs:15-53` (`FS6_TwoConcurrentReservesForTheLastUnits…`). Production code under test: `src/Fulfillment/Application/StockReservationService.cs:60`.

**What is missing.** No test anywhere in the repository ever observes a `stock.rejected.v1` row in `otc_fulfillment.outbox`. Grep confirms it: `stock.reserved.v1` is asserted in the outbox at `StockReserveTests.cs:63` and `FulfillmentOutboxRelayTests.cs:67`, `stock.released.v1` at `StockReleaseIdempotencyTests.cs:42` — `stock.rejected.v1` appears in the integration suite only inside two **method names**. Both named tests assert the reply payload and the unchanged counters, and stop there.

**The probe that makes it blocking.** I made the rejected path skip its persistence entirely — the one line that writes its only artefact:

```csharp
// src/Fulfillment/Application/StockReservationService.cs, in ReserveAsync
if (outcome.Kind == ReserveOutcomeKind.Reserved)
{
    await repository.SaveChangesAsync(ct).ConfigureAwait(false);
}
```

Result: **`Fulfillment.UnitTests` 79/79 green and `Fulfillment.IntegrationTests` 48/48 green.** A regression that permanently stops every `stock.rejected.v1` from being written passes the whole suite in silence. (Restored, force-rebuilt, re-run green: 79 + 48.)

**Why it matters, concretely.** On the rejected path nothing else is written: no reservation rows, no counter change. The outbox row **is** the transaction. `SagaCommandDispatcher` treats a delivered `outcome: rejected` reply as success (`SO6`) and marks the `saga_commands` row `sent`, so with the fact missing the order sits in `placed` forever with a `sent` command and nothing to consume — silent, unattended, and invisible to every guard now in the repository. This is `saga.md` §4.1's *intentional* race path: it is not an edge case, it is one of the two outcomes the feature exists to produce.

**Why it is a false tick rather than a scope question.** The approved `tasks.md` ordered the missing assertion twice, and both boxes are ticked `[x]`:

- `tasks.md` G3: *"the rejected path (`R33` integration shape — zero rows, **one `stock.rejected.v1`** naming requested and available)"* — the test asserts one **shortage in the reply**, not one fact row.
- `tasks.md` G5: *"asserting **only on the replies, the final counters and the outbox contents**"* — `design.md` §14 spells the same requirement out further: *"the outbox holding exactly one `stock.reserved.v1` and one `stock.rejected.v1`"*. The race test asserts replies and counters only.

Neither omission is disclosed in `progress/impl_fulfillment_stock.md`'s three argued deviations. The unit level does guard the *emission* (my probe deleting `carrier.RecordOrderFact(rejectedFact)` killed four unit tests, below); what survives is the deletion of everything that makes that emission reach the wire.

**This is #7's D1 wearing a different coat.** #7 was rejected here for *a branch that was implemented but untested, where a regression survived the entire suite*. #8 armed #7's exact mutation at **both** levels — genuinely, and I re-verified it — and then left the sibling branch of the same handler with no observation at the level where it can fail.

---

## 3. My own mutation probes

| # | Mutation | Expected guard | Result |
|---|---|---|---|
| P1 | `StockLockOrder.Fix`: `.OrderBy(code => code.ToUpperInvariant(), Ordinal)` → `.OrderBy(code => code, Ordinal)` (ledger **L3**) | `FS19_OrdersDistinctProductCodesByInvariantUppercaseOrdinal_…` | **KILLED** — 1 failed / 1 passed |
| P2 | `OrderStockReservation`: `Dictionary<string, long>` → `Dictionary<string, int>` for the repeated-line sum (ledger **L4**) | `FS20_RefusesAReserveWhoseSummedLineUnitsWouldOverflowTheUnitCounter_AndChangesNothing` | **KILLED** |
| P3 | Delete `carrier.RecordOrderFact(reservedFact)` | `R32_CreatesOneReservationPerLineIncreasesReservedUnitsAndEmitsExactlyOneStockReservedV1` | **KILLED** |
| P4 | Delete `carrier.RecordOrderFact(fact)` in `Release` | `R34_ReleasesTheReservationsDecreasesReservedUnitsAndEmitsExactlyOneStockReleasedV1` | **KILLED** |
| P5 | Delete `carrier.RecordOrderFact(rejectedFact)` | `R33_…`, `FS8_…`, `AThreeItemOrderWhoseThirdLineIsShort_…` | **KILLED** — 4 failed |
| P6 | Skip `SaveChangesAsync` on the rejected branch (D1) | *nothing* | **SURVIVED** — 79 + 48 green |

P1–P5 are the reason this review is one defect long rather than several: the pure-domain guards in this feature are real, and they are load-bearing at unit granularity, not just in aggregate.

---

## 4. The ported-idiom ledger — claim by claim

`design.md` §15, twelve rows. I checked the **claims**, not their existence, and probed the four the spec itself calls out as otherwise-silent losses.

| # | Property | Verified how | Standing |
|---|---|---|---|
| **L1** | A blocking, current read under RCSI | Read the SQL: `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` on the per-product stock read and `WITH (UPDLOCK, HOLDLOCK)` on the reservations read (`EfCoreStockItemRepository.cs:51,68`). The guard is **deterministic, not probabilistic**: `FS19_TheIdempotencyReadBlocksOnAConcurrentUncommittedReservationInsert_…` polls `sys.dm_exec_requests` for `blocking_session_id` and **asserts the interleaving was observed** before it asserts the outcome (`StockItemRepositoryTests.cs:139-163`). The *stock-row* hint has a second, independent guard the ledger does not claim: `FS18`'s held-lock test can only pass if the responder's read genuinely blocks | **Genuinely guarded** |
| **L2** | Deterministic global lock order | One `FromSqlInterpolated` per product in `StockLockOrder.Fix` order (`EfCoreStockItemRepository.cs:46-59`), never one multi-row statement. G6's arm stayed **green**, recorded honestly per `tasks.md`'s own instruction | **Guarded by construction**, not by observation — correctly disclosed |
| **L3** | Application sort order agrees with DB row identity | P1 killed. Dedup by `OrdinalIgnoreCase` guarded separately (`FS19_DeduplicatesCaseInsensitively`). The accent residual really is closed at the edge: `StockRequestValidator.ValidateAsciiAlphabet` (`^[\x20-\x7E]+$`) applies to **every** party and product code, with `ValidateReserve_RejectsANonAsciiProductCode` | **Genuinely guarded** — including the residual the ledger only mentioned in prose |
| **L4** | Counters that cannot wrap | P2 killed; B6's arm re-verified by reading `StockItem.Replenish`'s `quantity.Value > int.MaxValue - Units`; availability really is decided by subtraction (`AvailableUnits`), and `Reconstitute` refuses a reservation set that does not fit an `int` | **Genuinely guarded** (one narrowing remains — advisory A5) |
| **L5** | No upsert rendered | `grep -rniE "IF NOT EXISTS|MERGE "` over `src/Fulfillment` → no match. Every write is an `UPDATE` by primary key or an `INSERT` of a row created in this transaction, all under locks taken in the same transaction | **Confirmed** |
| **L6** | Concurrent handling | `StockRpcResponder.cs:80-101`: semaphore acquired **before** the scope, tracked task, `StopAsync` drains. `FS18` integration is deterministic (asserts the first request is *still* outstanding when the second is answered, then that it completes after the lock is released) and the unit half proves a distinct scope per request | **Genuinely guarded** |
| **L7** | A retryable failure never gets a terminal code | `StockErrorMapper` produces `CONFLICT` from no input; `FS21` reads the nine-code terminal set **out of `NatsSagaCommandsAdapter.cs` as text** and asserts non-membership — verified the regex extracts exactly the nine quoted literals of `IsTerminalRpcErrorCode` and nothing from the `_ => false` comment | **Genuinely guarded, and the strongest row in the ledger** |
| **L8** | Publication order = `seq` | Per-row awaited `INSERT`, copied with its reasoning (`EfCoreStockItemRepository.cs:205-214`), byte-comparable to `EfCoreOrderRepository.InsertOutboxRowAsync`. Arm reproduces ~5–6 runs in 10, disclosed | **Guarded, probabilistically armed** — disclosure accepted |
| **L9** | Bare-JSON wire | `StockWireTests.cs:34-37` asserts the reply object carries no `response` / `isDisposed` / `id`, one `InlineData` per subject | **Guarded** |
| **L10** | No uncallable consumer copy | No `IdempotentConsumer.cs` and no `BackgroundService` mentioning `IConsumer<` under `src/Fulfillment`; `IdempotentConsumerParityTests` green inside the 254 | **Confirmed, gate row 1 honoured** |
| **L11** | Declarative validation replaced by hand-rolled | One test per rule, including the ASCII rule L3 depends on | **Guarded** |
| **L12** | Transaction without a `tx` parameter | `ForcedRollback_LeavesNeitherTheStockOrReservationChangesNorTheOutboxRows` asserts zero stock change, zero reservations **and zero outbox rows** after a throw inside `ExecuteAsync` | **Guarded** |

**No ledger row is merely asserted.** That is a real result, and it is the first thing I looked for.

---

## 5. The two gate rulings, judged on implementation

**Row 1 (no idempotent-consumer copy).** Implemented as ruled. `IdempotentConsumerParityTests` still passes with Fulfillment contributing to neither set, because it has no Kafka consumer `BackgroundService` — I confirmed the absence directly rather than trusting the report.

**Row 2 (concurrent handling, `SemaphoreSlim(32)`, one scope each).** Implemented as ruled, and read correctly by the implementer. G11's arm reverts to `await HandleAsync(...)` inline; `FS18` fails (`NatsNoReplyException : No reply received`) and `FS6` **stays green** — recorded as the finding, not as a broken guard, which is exactly what the gate said the arming was for. The other guard for concurrent handling (`FS18` integration) does fail under the reversion, so the claim is not resting on a pass/fail ratio.

But note where this leaves `FS6`: its extra assertions are `reserved_units == 5` and `reserved_units <= units`, both of which a serialised responder also satisfies, and it makes **no outbox assertion at all** (the second half of D1). `FS6`'s independent value therefore rests entirely on `FS18` being present and correct. That is acceptable as designed — but it is one more reason the missing outbox assertion is worth a round.

---

## 6. Traceability walked

Shared rows this feature flips (`specs/shared/test-matrix.md` §4, column 5 only — `git diff` confirms no other column and no requirement text changed):

| `R<n>` | Cited test | Exercises the requirement? |
|---|---|---|
| `R30` | `StockItemTests.R30_RejectsInFullAnyOperationThatWouldPushReservedUnitsAboveUnitsAndChangesNoStockItem` | Yes — asserts the throw **and** that counters and reservations are unchanged |
| `R31` | `StockCheckTests.R31_AnswersPerLineWithoutMutatingAStockItemAndWithoutEmittingAFact` | Yes — row equality before/after **and** `OutboxMessages.CountAsync() == 0` |
| `R32` | `ReservationTests.R32_CreatesOneReservationPerLine…` | Yes — P3 killed it |
| `R33` | `ReservationTests.R33_CreatesNoReservationAtAll…` | Yes at domain level — P5 killed it. **Its integration half is D1** |
| `R34` | `OrderStockReservationTests.R34_…` + `StockReleaseIdempotencyTests.R34_AnswersSuccessAndEmitsNoSecondFact_…` | Yes — P4 killed the domain half; the integration half asserts the outbox |
| `R35` | `ReservationTests.R35_RefusesEveryTransitionOutOfReleasedAndOutOfConsumed…` | Yes |
| `R61` | `StockItemTests.R61_…` (domain half); API half correctly left `TODO` with the deferral argued in-cell | Yes — B10's arm (append an event) is a real fact-**suppression** guard |
| `R36` | Correctly left `TODO` — only `Consume()` lands here, unit-tested and uncalled | n/a |

Local `FS2` – `FS22`: every row in `specs/fulfillment_stock/requirements.md` §2 names a test that exists and that exercises the distinguishing branch. Two rows I checked especially hard because a name is cheap: `FS5` (the `[Theory]` seeds **ample** availability, so a status-filtering handler would happily reserve — the case genuinely discriminates) and `FS21` (reads the terminal set from Orders' source rather than a retyped list). `FS17`'s "live verification" row is true — see §7.

---

## 7. The live-stack walkthrough, verified by query rather than by report

Every `FS17` claim in `progress/impl_fulfillment_stock.md` § Live boot is true against the running stack:

- `otc_orders`: `ORD-000007` – `ORD-000010` all `stock_reserved`, their `stock.reserve` rows `sent` with `attempts = 9`, and a `credit.hold` row parked on each (now `attempts = 6`, higher than the report's 3 — the sweeper has kept running since, which is itself the unattended behaviour the feature claims).
- `otc_fulfillment.reservations`: one `reserved` row per order matching each parked payload; `stock`: `IBERFOODS/PRD-0001` 5 reserved (4 + `ORD-000011`'s 1), `IBERFOODS/PRD-0002` 6.
- **`F2` holds across the whole live database**, not just the touched rows — I ran the correlated query for every `stock` row whose `reserved_units` differs from the sum of its `reserved` reservations and it returned **zero rows**.
- **The cross-service chain is exact.** For all five orders (`ORD-000007` – `ORD-000011`) the outbox row's `correlation_id` equals `otc_orders.orders.id` and its `causation_id` equals the `saga_commands` row id, digit for digit. Published timestamps `05:34:19.814`–`05:35:27.222`, matching the report.
- `ORD-000011` exists with `stock.reserve` `sent`, `attempts = 0` — the first genuinely end-to-end `orders.create` acceptance in this repository, as claimed.
- No reservation rows exist for `ORD-999999` — the H5 negative check left no side effect.

This closes the claim Phase 8's design made and could not demonstrate. It is the best-evidenced part of the feature, and it is worth saying that it was evidenced *before* I asked.

---

## 8. Advisories (non-blocking, but fix A1 while you are in the file)

**A1 — three test names claim assertions the tests do not make.** `StockReserveTests.cs:15` (`…AndEmitsExactlyOneStockReservedV1` — asserts no outbox row; the emission is covered by the separate `FS3` test), `StockReserveTests.cs:75` (`…AndOneStockRejectedV1NamingRequestedAndAvailable` — D1), `StockReserveRaceTests.cs:15` (`…YieldExactlyOneStockReservedAndOneStockRejected` — asserts outcomes, not facts). A name that overstates is the cheapest way for a later reader to believe a branch is covered when it is not; #7's own review recorded the same class as nit `N1`.

**A2 — "a fresh `NatsHeaders` per call" is satisfied by construction but guarded by nothing.** `NatsSagaCommandsAdapter.SendAsync` builds the instance as a local (`src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs:90-94`), which is correct and is the only path all five methods take. But `FS2_SendsCorrelationAndRequestIdHeaders_OnEverySagaCommandRequest` records and asserts the header **values**; hoisting the instance to a field and mutating it per call would keep every test green while reintroducing precisely the thread-safety hazard `design.md` §11 cites the NATS XML doc about. Either add an assertion that two calls receive different instances, or record in the design that the property is structural.

**A3 — `OrderStockReservation.Release` calls `UniqueId.New()` internally** (`src/Fulfillment/Domain/OrderStockReservation.cs:230`) while `Reserve` takes a `Func<UniqueId> newId`. `design.md` §3.3 says of both: *"no ids beyond those `newId` supplies"*. Harmless today, asymmetric, and an undisclosed deviation from the design's own sentence.

**A4 — `ClampToInt`** (`OrderStockReservation.cs:246`) silently clamps a summed `requested` above `int.MaxValue` to `int.MaxValue` in the `stock.rejected.v1` shortage payload. Defensible — the AsyncAPI field is an integer and the order is rejected anyway — but it is a silent narrowing in the one feature whose ledger row L4 is *about* silent narrowing, and `FS20`'s test asserts only `Available`. Worth one sentence in the design or one assertion on `Requested`.

**A5 — `StockRpcResponder.StopAsync` awaits `Task.WhenAll(pending)` over faulted tasks.** If a request's `ReplyAsync` throws during shutdown, `StopAsync` rethrows out of host shutdown. Cosmetic today; a `ContinueWith`-style swallow or `WhenAll` on wrapped tasks would be tidier.

---

## 9. `CHECKPOINTS.md` walked

**C1 — harness complete**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` exist
- [x] `progress/current.md`, `progress/history.md` exist
- [x] `.claude/agents/` holds all five roles
- [x] every agent definition declares its model
- [x] `./init.sh` exits 0 (run by me)

**C2 — state coherent**
- [x] at most one feature `in_progress` (17 returns to `in_progress` with this verdict; nothing else is)
- [x] every status in `rules.valid_status`
- [x] every `done` feature has passing tests
- [x] `progress/current.md` describes this session
- [x] no `blocked` feature

**C3 — architecture**
- [x] domain purity — `Architecture.Tests` 16/16 **run**, covering `DomainPurityTests`, `CqrsDomainPurityTests`, `DomainDecimalTests` over `DomainAssemblies.All` (which already includes Fulfillment)
- [x] no cross-service DB access — Fulfillment reads only `otc_fulfillment`; the correlation carrier is a header, not an FK
- [x] no shared runtime code beyond `SharedKernel`, `Contracts`, `Cqrs` — the relay family is **copied** with `// COPY OF —` banners, as `design.md` §8.3 and #7's own gate ruled
- [x] no `Domain/` namespace references `OrderToCash.Cqrs`
- [x] `SharedKernel` still has zero `PackageReference`
- [x] no `decimal` in domain arithmetic — this service handles no money at all
- [x] every interaction classifiable: five `fulfillment.stock.*` subjects are NATS-RPC, three `stock.*` facts are Kafka via the outbox relay; no Kafka-as-request-bus, no RPC-for-facts
- [x] no stray debug logging, no context-free TODOs

**C4 — verification real**
- [x] `./quality.sh` passes — the implementer's run (exit 0, after fixing an `IDE1006` violation it disclosed); **I did not re-run it in full**, and instead re-ran 397 tests across four projects independently (§1)
- [x] domain tests pure — `Fulfillment.UnitTests` domain files reference no framework, no DB, no broker
- [x] integration tests use real Testcontainers MsSql / NATS / Kafka — confirmed by watching the containers start during my own 48-test run
- [x] coverage thresholds — domain 84.8% / 80.5% as measured by the implementer from the emitted cobertura files; `quality.sh` does not yet *gate* them (feature 34), which the implementer stated rather than implied
- [x] no Jest anywhere

**C5 — session close**
- [ ] `progress/history.md` entry with effort record — **deliberately not written**: the feature is rejected and is not closeable. The measured timings are recorded in §10 so the eventual entry is a transcription, not a re-derivation
- [x] no suspicious untracked files (my probe backups live outside the repo)
- [x] `feature_list.json` reflects true state (set back to `in_progress` by this verdict)
- [x] the human will be told what was done and how to test it
- [x] Claude did not commit

**C6 — SDD**
- [x] `specs/fulfillment_stock/` has all three documents
- [x] `requirements.md` is EARS with `R<n>`/`FS<n>` ids
- [ ] **every task genuinely ticked** — G3 and G5 are ticked `[x]` while the outbox assertions they name are absent (**D1**)
- [x] every `R<n>` covered by a named test recorded in `specs/shared/test-matrix.md`
- [x] the spec commit will precede the implementation commit (both are uncommitted; the human commits after testing)

**C7 — spec-reuse fidelity**
- [x] `specs/shared/` byte-identical to #7's apart from `test-matrix.md`'s Status column — verified by real `diff -rq` against the #7 checkout, and by `git diff` showing only column 5 plus the §1 counts. The two prose paragraphs that differ pre-date this feature and are the ones #7's own text invites #8 to delete
- [x] no silent fork; no amendment proposed or needed by this feature
- [x] the `R<n>` ids are #7's and the .NET realisation satisfies the same requirements (§6), with `FS1` **deliberately not reused** and argued
- [ ] n8n workflows — not touched by this feature (feature 25/31 own the Gateway surface)
- [ ] black-box API script — not this feature's surface
- [x] effort records complete and honest — §10, and #7's own numbers quoted beside them
- [x] README benchmark section — unchanged by this feature; the phase-9 row is owed at phase close

---

## 10. Effort, measured (for the history entry this verdict withholds)

Timings are from the session transcript's subagent dispatch timestamps (UTC), which is stronger evidence than file mtimes here — a `dotnet format` pass rewrote most of this feature's files at the end and destroyed the mtime record.

| Phase | #8 | #7's baseline |
|---|---|---|
| Spec + human gate | **≈27 min** (spec_author dispatched 03:36:11Z, implementer dispatched 04:03:42Z, gate ruled in between) | ≈8 min |
| Implementation | **≈1 h 50 min** (04:03:42Z → 05:54:06Z), including the eleven armings, the live walkthrough and a failed-then-fixed `quality.sh` | ≈1 h 35 min |
| Review pass 1 | **≈50 min** (05:54:06Z → this verdict), of which ≈6 min Testcontainers (two full 48-test integration runs at 2 m 41 s and 2 m 44 s) and six live-database query batches | ≈75 min |
| Rounds | **1 review pass so far — REJECTED, 1 blocking defect** | 2 review passes — REJECTED then approved, 1 blocking defect |

So on the shape of the outcome the two runs have converged exactly: **both #7 and #8 were rejected on this feature, once, on a single defect, and in both cases the defect was a branch that worked and was not observed.** #8's implementation ran ≈1.16× #7's clock; its spec pass ran ≈3× #7's eight minutes, which is the ledger's cost and is discussed below.

---

## 11. Did the ported-idiom ledger earn its cost? — yes, and the evidence is specific

**It cost roughly 15–20 minutes of the spec session** (twelve rows, four of them requiring a real answer, plus the `tasks.md` guard columns they force). #7's spec pass on this feature was ≈8 minutes; #8's was ≈27. Most of that delta is the ledger.

**It bought four properties that would otherwise have been silent losses, and I can name what each one would have cost:**

1. **L7 — `CONFLICT` banned from this service's mapper.** This is the row that justifies the convention on its own. #7 mapped its concurrency error to `CONFLICT` and its orchestrator retried every code, so the mapping was safe *there*. In #8, feature 42 made `CONFLICT` terminal: a deadlock victim answered `CONFLICT` would mark the `saga_commands` row `rejected`, which `ClaimDueAsync` structurally never re-claims — the order's saga would end permanently, **while satisfying `R32`'s requirement text exactly**. Traceability could not see it (the requirement is met) and arming could not see it (the behaviour is correct on the tested path). It was found by asking what supplied the property, and it is now guarded by a test that reads the terminal set out of the Orders adapter's own source.
2. **L1 — explicit lock hints.** Without them, under `READ_COMMITTED_SNAPSHOT ON`, the idempotency read takes **no lock at all** and returns a pre-insert row version, so two `stock.reserve` for the same order can both proceed. #7 got the property free from `FOR UPDATE` under `REPEATABLE READ`. The guard built for it is the best test in the feature: it asserts the interleaving was *observed* via `sys.dm_exec_requests` before it asserts anything else.
3. **L2/L3 — the lock order.** #7 got a total order free from InnoDB's index scan; MS-SQL guarantees nothing about a multi-row seek's lock-acquisition order, so #8 had to build it per-row and in an application-fixed order — and then noticed, only because the row forced the question, that a *plain ordinal* sort would let two callers spelling a code with different case derive different orders for the same row. My probe P1 killed that mutation; without the ledger row there would have been no test to kill it with.
4. **L4 — `int` counters.** C# arithmetic is unchecked by default here; my probe P2 shows the `long` sum is genuinely load-bearing.

**And the negative result matters too.** L5 says *there is no upsert here to mis-render* — that row exists purely because feature 45's defect was a check-then-act rendering of `INSERT … ON DUPLICATE KEY UPDATE`, and the next reader will look for it. Writing "not applicable, and here is why, and grep to confirm" is worth more than silence, and it took one line.

**The honest limits.** The ledger did **not** catch D1 — and could not have, because D1 is not a ported property at all: it is an ordinary missing assertion in a branch both assessments implement the same way. The convention is aimed at a specific blind spot and it hit it; it is not a general-purpose review. Two rows are also weaker than they read: **L2**'s arm stayed green (guarded by construction, honestly disclosed) and **L8**'s reproduces on a majority but not all runs (honestly disclosed). Both disclosures are the right behaviour — an arming table that claimed kills it did not get would be worse than either.

**Verdict on the convention: keep it, unchanged, for phases 10–13.** Its 0-for-3 predecessor record is now 4-for-4 on its first outing, at a cost of about a fifth of a spec session.

---

## 12. What must change before re-review

1. **Assert the rejected fact where it can fail.** In `tests/Fulfillment.IntegrationTests/StockReserveTests.cs`'s `RejectedPath_…`, after the reply assertions, read the outbox for the request's `correlationId` and assert **exactly one** row of `event_type = 'stock.rejected.v1'` whose payload names the requested and available units. This is what `tasks.md` G3 already asks for.
2. **Give the race test its outbox assertion.** In `StockReserveRaceTests.FS6_…`, assert the outbox holds exactly one `stock.reserved.v1` and exactly one `stock.rejected.v1` per iteration, as `tasks.md` G5 and `design.md` §14 both specify.
3. **Arm it.** Apply the D1 probe — skip `SaveChangesAsync` on the rejected branch, or delete the drain for that path — confirm the two tests above **FAIL**, record the messages verbatim in `progress/impl_fulfillment_stock.md`'s arming table, restore from a backup copy, force the rebuild, re-run green. Nothing else in the arming table needs redoing.
4. **Fix the three overstated test names** (A1) so a later reader cannot mistake a name for an assertion.
5. **Re-tick G3 and G5 honestly**, and add a line to the impl report's deviations section saying what was missing and is now covered.

Advisories A2–A5 are not owed for re-review; A2 is worth a decision one way or the other before feature 18 copies the responder shape.

Nothing else is asked. `src/Fulfillment` production code needs **no change**: the behaviour is correct, it is the observation that is missing — which is, precisely, the finding.

---

*Reviewed by the `reviewer` agent, pass 1. Feature 17 set back to `in_progress` in `feature_list.json` (single-line edit). No commit made.*

---

# Review — `fulfillment_stock` (feature 17, phase 9) — **pass 2 (round 2)**

> **Round 1 above is closed and is not amended by this section.** Nothing in it is reopened, withdrawn or re-scored. Where round 2 corrects a round-1 measurement or records something round 1 missed, it says so here and leaves round 1's text as written.

**Verdict: REJECTED.** One blocking defect (**D2**), narrowly bounded, fixable in about three lines plus one arming cycle. **D1 is genuinely closed** — I re-armed it myself rather than accepting the recorded table, and the fix survives the specific attack round 1's brief was worried about (one assertion wearing two names): the two new assertions fail on *different* mutations at *different* lines. The fix also turned out to be worth more than it claimed, closing a hole nothing else in 127 tests covers.

**D2 is the same shape as D1, one file over, and the round that existed to close that shape did not look for it.** `tasks.md` **G7** — unflagged, ticked `[x]` — says the release happy path asserts *"exactly one `stock.released.v1` **carrying the request's `reason`**"*. The row count is asserted; the `reason` is never read. I corrupted the wire payload and the whole suite stayed green.

---

## 1. What I ran, and what I did not

Per `CLAUDE.md`'s reviewer rule I did **not** re-run `./quality.sh`, and I did not re-run `Orders.UnitTests` or the live-stack walkthrough — round 1 verified those and the fix round touched neither Orders nor `src/`. What I ran independently in this round:

| Run | Result |
|---|---|
| Four fresh mutation probes with forced `--no-incremental` rebuilds (§2, §3) | 3 killed, **1 survived — D2** |
| `dotnet test tests/Fulfillment.IntegrationTests` (full, real containers) — twice under mutation, twice confirming | armed runs as tabled; **48/48 green** after every restore |
| `dotnet test tests/Fulfillment.IntegrationTests --filter` (the two D1 tests, then FS6 alone) | as tabled in §2 |
| `dotnet test tests/Fulfillment.UnitTests` | **79/79 green** (also run under two mutations, see §3) |
| `dotnet test tests/Architecture.Tests` | **16/16 green** — re-run because it is cheap, not because anything architectural changed |
| Dangling-citation sweep: every `` `Name_With_Underscores` `` cited in `specs/fulfillment_stock/{requirements,design,tasks}.md` and `specs/shared/test-matrix.md` resolved against every `public Task/void` method name under `tests/` | **zero dangling test citations** — the two renames left no stale reference |
| `git diff -- specs/shared/` | still column 5 of `test-matrix.md` only (9 insertions / 9 deletions); mtime `05:48Z`, i.e. **untouched by the fix round** |
| `git status --short` on tracked files | identical to round 1's list — no file added, removed or reverted by the fix round |

All four probe files were backed up first (`…/scratchpad/backups/`), restored by `cp` from those backups (**never** `git checkout --`), verified byte-identical with `diff`, re-read at the changed line, and force-rebuilt before every confirming run. **The stale-binary trap fired on me once and I caught it**: `dotnet build projA projB` in one invocation is rejected by MSBuild (`For switch syntax, type "MSBuild -help"`), the build silently does not happen, and `--no-build` then runs the *previous* probe's binary. My first attempt at probe C reported four failures that belonged to probe B. Rebuilt each project separately, checked the elapsed-time line, and re-ran. The four-failure result is discarded; only the corrected run is reported below. (This is the exact trap `progress/history.md`'s feature-42 note warns about, now observed a second time, in the same direction.)

---

## 2. D1's fix — verified independently, and the independence question answered

**Probe R2-A — the reviewer's own D1 mutation, re-armed.** `src/Fulfillment/Application/StockReservationService.cs:60`, `await repository.SaveChangesAsync(ct)` wrapped in `if (outcome.Kind == ReserveOutcomeKind.Reserved) { … }`:

```
StockReserveTests.RejectedPath_ZeroRowsCreated_AndOneStockRejectedV1RowInTheOutboxNamingRequestedAndAvailable [FAIL]
  Assert.Single() Failure: The collection was empty
  at …/StockReserveTests.cs:line 126
StockReserveRaceTests.FS6_TwoConcurrentReservesForTheLastUnits_Yield… [FAIL]
  Assert.Single() Failure: The collection was empty
  at …/StockReserveRaceTests.cs:line 76
Failed! - Failed: 2, Passed: 0
```

**Probe R2-B — the independence check the brief asked for.** `src/Fulfillment/Domain/OrderStockReservation.cs:195`, `carrier.RecordOrderFact(reservedFact);` deleted — the *reserved* fact suppressed, the rejected one untouched:

```
StockReserveRaceTests.FS6_TwoConcurrentReservesForTheLastUnits_Yield… [FAIL]
  Assert.Single() Failure: The collection was empty
  at …/StockReserveRaceTests.cs:line 70
Failed! - Failed: 1, Passed: 0
```

**This is the answer to "one assertion wearing two names", and it is a clean no.** Under probe A, `FS6` fails at **line 76** — the rejected assertion — which means **line 70's reserved assertion executed and passed**. Under probe B it fails at **line 70** — the reserved assertion — with the rejected side untouched. Different mutations, different lines, neither failure implies the other. `FS6`'s counter assertion (`reserved_units == 5`, line 58) also passed under probe B, so the reserved-fact assertion is not a restatement of the counter either: it observes something the counter cannot see.

**Probe R2-C — and the fix is worth more than it claimed.** `src/Fulfillment/Infrastructure/Outbox/StockFactPayloadMapper.cs:26`, `rejected.Shortages` → `[]` — a payload-mapping regression that empties the shortages on the wire while the row, the envelope, the reply and the counters all stay correct:

```
Fulfillment.UnitTests: 79/79 PASSED
Fulfillment.IntegrationTests: Failed: 1, Passed: 47
  StockReserveTests.RejectedPath_…NamingRequestedAndAvailable [FAIL]
```

**One test out of 127 catches it, and it is one of the two assertions added in this fix round.** The mapper had no test of its own (`grep -rl StockFactPayloadMapper tests/` → nothing), so before the fix a mapping regression on `stock.rejected.v1` reached Kafka unobserved. That is a genuine second closure, not claimed in the impl report.

**Everything else in the fix round checks out.** The renames are consistent with every citation (§1's sweep). `FS6`'s per-order correlation ids are strictly stronger than the shared-header shape they replace, and they are what makes the winner/loser attribution assertable at all. The A4 assertion at `tests/Fulfillment.UnitTests/StockItemTests.cs:151` is load-bearing: `(int.MaxValue - 1) + 3` clamps to `int.MaxValue`, and a wraparound would land on `-2147483647` and fail it.

---

## 3. D2 — the blocking defect: `tasks.md` G7's payload claim is ticked and absent

**Where:** `tests/Fulfillment.IntegrationTests/StockReleaseIdempotencyTests.cs:15-46` (`ReleaseHappyPath_ReleasedReply_RowsReleased_CounterDown_AndExactlyOneStockReleasedV1CarryingTheReason`). Production code under test: `src/Fulfillment/Infrastructure/Outbox/StockFactPayloadMapper.cs:29-34`.

**The claim.** `specs/fulfillment_stock/tasks.md` **G7**, ticked `[x]`: *"…and the release happy path (`released` reply, rows `released`, counter down, **exactly one `stock.released.v1` carrying the request's `reason`**)."* The test's own name repeats it: `…_AndExactlyOneStockReleasedV1CarryingTheReason`.

**What the test does.** It sends `reason = "order_cancelled"` (line 28), asserts the reply outcome, the released list, the counter, the row status, and then `Assert.Single(factRows)` on the outbox filtered by correlation id and `event_type` (line 45). **It never deserialises the payload.** The reply carries no `reason` field either (`StockReleaseReplyPayload` has none), so nothing in this test observes the request's reason reaching anything.

**Probe R2-D, which survived.** Two field-level corruptions in the mapper, one on each of the other two fact types:

```csharp
// src/Fulfillment/Infrastructure/Outbox/StockFactPayloadMapper.cs
StockReserved reserved => new StockReservedPayload(…, "PROBE-D-WRONG-RETAILER"),
StockReleased released => new StockReleasedPayload(…, "probe_d_wrong_reason", released.RetailerCode),
```

Result: **`Fulfillment.UnitTests` 79/79 green and `Fulfillment.IntegrationTests` 48/48 green.** Every `stock.reserved.v1` on the wire carries a fabricated `retailerCode` and every `stock.released.v1` a fabricated `reason`, and the entire feature's suite passes. (Restored from backup, `diff`-verified, force-rebuilt, confirming run **48/48 green**.)

**Why it matters, concretely.** `retailerCode` and `reason` are not decoration. `CLAUDE.md`'s database-per-service rule makes `CompanyCode`/`RetailerCode`/`ProductCode`/`OrderReference` the *only* things that cross a service boundary — "business identifiers carried in messages, never FKs". Phases 10–13 build Billing, Projector and Notifications on these exact fields; `stock.released.v1`'s `reason` is the one field that distinguishes a cancellation from a compensation, and `saga.md`'s compensation path turns on it. A corrupted value here is not a crash, it is wrong data arriving in four downstream services with every test green — and `StockFactPayloadMapper` is a **new file this feature introduces**, which the relay-family copy protocol (`design.md` §8.3, feature 19) will replicate into each of them. An unguarded mapper is the template for five unguarded mappers.

**Why it is a false tick and not a scope question.** G7's sentence names the assertion literally, the box is `[x]`, and the assertion is absent. That is precisely the standard `CLAUDE.md` gained this morning — I read it on disk, at the amendment the leader landed at `08:40` local: *"A task that makes a countable claim must be armed, whether or not it carries the arming flag… A tick is not evidence the assertion exists."* G7 is unflagged, and its prose makes an identity claim. It is the third instance of the pattern in this feature's task list and the first one to be found **after** the diagnosis was written.

**Two things I will not pretend about.** First, **round 1 missed this**, and it is squarely the same family as round 1's own advisory A1 (an overstated test name) and D1 (an unobserved wire artefact) — my six round-1 probes attacked *emission deletion* and never *payload corruption*, so the mapper was never in front of a mutation. Second, the fix round's report is right that the diagnosis it wrote is procedural and general ("arm every task whose sentence contains a countable claim, not only the flagged ones"); **it just was not applied to the rest of the task list it had already opened.** Applying it to G7 would have taken one grep.

---

## 4. The three declined advisories, judged

| | Declination sound? | Deserves a backlog entry? |
|---|---|---|
| **A2** — Orders-side fresh `NatsHeaders` per call, structural but unguarded | **Yes, with a correction to its stated reason** | **Yes** |
| **A3** — `OrderStockReservation.Release` uses `UniqueId.New()` instead of the passed `newId` | **Yes** | **Yes, low priority** |
| **A5** — `StockRpcResponder.StopAsync` rethrows a faulted drain | **Yes** | **Yes** |

**A2.** The *decision* to decline is right — it is a one-assertion test in another service's suite and nothing in this feature can regress it. The *reason given* is not: the report calls `src/Orders` out of this fix round's scope, but `NatsSagaCommandsAdapter.cs` **is one of the three Orders files this very feature changed** (group A), so "we are not scoped to touch it" is weaker than it reads. I checked the hazard's real size before recommending anything: the adapter is registered `AddScoped` (`OrdersSagaServiceCollectionExtensions.cs:42`), and the sweeper dispatches its claimed batch in a plain `foreach` (`SagaCommandSweeper.cs:42`), so today the instance is neither shared across scopes nor driven concurrently within one. The hazard is genuinely latent — which is why it is not blocking, and also why the class remark asserting a thread-safety property that nothing enforces should not be left standing while five more services copy this adapter shape.

**A3.** Sound. It is a production signature change with no observable consequence, and the fix round was right not to make it under a test-only mandate. It is now disclosed in two places, which is what the repository asks of a deviation from an approved `design.md` sentence. The cheap moment to close it is feature 18, which opens `OrderStockReservation.cs` anyway for `Consume`.

**A5.** Sound. `StopAsync` awaits `Task.WhenAll(pending)` (`StockRpcResponder.cs:60-71`), so a faulted `ReplyAsync` during drain rethrows out of host shutdown — noisy, after the drain has completed, with no data at risk. Declining a production change with no test to demonstrate it is the right call. It still belongs in the backlog for the same reason as A2: this responder is the template for Billing's and Despatch's.

**Backlog wording, if the leader wants it — three entries, phrased for `feature_list.json`:**

> **`saga_command_headers_thread_safety_guard`** — *NatsSagaCommandsAdapter: the "a fresh NatsHeaders per call" property is structural and guarded by nothing.* Acceptance: a named test in `tests/Orders.UnitTests/NatsSagaCommandsAdapterTests.cs` captures the `NatsHeaders` instance passed to `RawRequester` on two consecutive `SendAsync` calls and asserts they are **reference-distinct**; armed — hoisting the instance to a field and clearing/re-adding per call makes that test fail while every existing header-value assertion stays green. Notes: found by feature 17's review (advisory A2). `NatsHeaders` is documented not thread-safe; the adapter is `AddScoped` and the sweeper dispatches sequentially, so the hazard is latent today, but the class remark asserts a property no test holds and five more services will copy this shape. Suggested phase 10.

> **`stock_release_deterministic_event_id`** — *OrderStockReservation.Release allocates its fact's EventId with UniqueId.New() while Reserve takes a `newId` delegate.* Acceptance: `Release` takes the same `Func<UniqueId> newId` seam as `Reserve`, its one caller in `StockReservationService.ReleaseAsync` supplies it, and a named unit test asserts the released fact's `EventId` is the one the delegate returned. Notes: found by feature 17's review (advisory A3); an undisclosed-then-disclosed deviation from `specs/fulfillment_stock/design.md` §3.3's *"no ids beyond those `newId` supplies"*. Harmless today, asymmetric, and cheapest to close in feature 18, which opens the same file for `Consume`.

> **`rpc_responder_shutdown_fault_isolation`** — *StockRpcResponder.StopAsync rethrows out of host shutdown when a drained request's ReplyAsync faults.* Acceptance: a faulted in-flight task no longer propagates out of `StopAsync`; the drain still waits for every in-flight request; a named test proves shutdown completes with one faulted and one healthy in-flight task. Notes: found by feature 17's review (advisory A5), cosmetic today. Worth doing **before** Billing and Despatch copy the responder, not after — the fix belongs wherever the responder family is consolidated.

---

## 5. A1's third name — what it is, and whether leaving it is defensible

The third is `FS6_TwoConcurrentReservesForTheLastUnits_YieldExactlyOneStockReservedAndOneStockRejected_AndReservedUnitsNeverExceedsUnits`, deliberately left unrenamed.

**Defensible, and for a better reason than the one given.** The report justifies it by citation stability (`requirements.md` §2 and `tasks.md` G5 both quote the string). That is true — and the sharper justification is that **the name is now true**, which probes A and B prove independently: the body really does assert exactly one `stock.reserved.v1` and exactly one `stock.rejected.v1`, and each half fails on its own mutation. A1 asked for names not to overstate; this one no longer does. Note also that the one-underscore repair went in the **right direction** — the `.cs` method was renamed to match `requirements.md`'s existing citation, not the spec bent to the code.

**One residual, non-blocking.** "Exactly one" is asserted *per correlation id*: one reserved row for the winner, one rejected row for the loser. Nothing asserts the winner emitted no rejected row or the loser no reserved row. The harmful direction is covered indirectly — a second reservation would push `reserved_units` past 5 and fail line 58 — so this is a completeness note, not a hole. If it is ever tightened, the cheap form is one query per correlation id with **no** `event_type` filter and `Assert.Single` on each.

---

## 6. Did the fix round weaken anything? — no, and here is what I checked

Two rounds over the same files warranted the look. `tests/Fulfillment.IntegrationTests/StockReserve*.cs` are untracked, so there is no `git` baseline and no pre-fix backup survives; I judged from full reads of both files plus the following specific checks, and I say so rather than implying a diff I could not run.

- **Iteration counts intact** — `FS6` and `FS19` both still loop `for (var i = 0; i < 10; i++)` on fresh items, as G5 requires.
- **No wait shortened, no timeout loosened** — every new wait is `WaitForAsync(…, rows => rows.Count > 0, TimeSpan.FromSeconds(10))`, the same budget and the same monotonic predicate the fixture's own comment mandates; `RequestBareAsync`'s default 10 s reply timeout is untouched and `FS19` still passes its explicit 15 s.
- **Correlation ids: strengthened, not reused** — `FS6` now allocates a distinct id per order and keys each outbox read by it; the shared-id case that genuinely needs sameness (`FS5`'s sweeper re-issue, `StockReserveTests.cs:147-148`) still uses one correlation id with two request ids, which is the point of that test.
- **No assertion dropped in the renamed accepted-path test** — the reply, counter and reservation-row assertions are all still there. The emission claim it gave up is covered by `FS3` (killed by probe B) and by `FS5`'s `Assert.Single` on the reserved rows, so G3's "exactly one `stock.reserved.v1`" survives the rename.
- **No test disabled** — `grep "Skip *=" tests/Fulfillment.*` returns nothing; counts unchanged at 79 and 48; the A4 change is an added assertion, not a relaxed one.
- **No spec bent to fit the code** — `specs/shared/test-matrix.md` untouched by the fix round (mtime `05:48Z`, before it started), `requirements.md` and `tasks.md` likewise (`05:49Z`). The impl report lists `tasks.md` under "files touched", which is a slip of bookkeeping rather than of fact: it was not modified, and G3/G5 needed no re-ticking because their text was always right.

---

## 7. `CHECKPOINTS.md` walked — round 2

**C1 — harness complete**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` exist
- [x] `progress/current.md`, `progress/history.md` exist
- [x] `.claude/agents/` holds all five roles
- [x] every agent definition declares its model
- [x] `./init.sh` exits 0 — verified by the leader this session and re-run by me after the status edit below

**C2 — state coherent**
- [x] at most one feature `in_progress` (17 returns to `in_progress` with this verdict; nothing else is)
- [x] every status in `rules.valid_status`
- [x] every `done` feature has passing tests
- [x] `progress/current.md` describes this session
- [x] no `blocked` feature

**C3 — architecture**
- [x] domain purity — `Architecture.Tests` **16/16 run**, not eyeballed
- [x] no cross-service DB access
- [x] no shared runtime code beyond `SharedKernel`, `Contracts`, `Cqrs`
- [x] no `Domain/` namespace references `OrderToCash.Cqrs`
- [x] `SharedKernel` still has zero `PackageReference`
- [x] no `decimal` in domain arithmetic
- [x] every interaction classifiable as Kafka-fact or NATS-RPC
- [x] no stray debug logging, no context-free TODOs — and no probe residue: `grep -rn "PROBE" src/` is empty

**C4 — verification real**
- [x] `./quality.sh` passes — the implementer's post-fix run, exit 0; **I did not re-run it**, and instead ran four probes and 143 tests across three projects (§1)
- [x] domain tests pure
- [x] integration tests use real Testcontainers MsSql / NATS / Kafka
- [ ] **coverage thresholds** — measured at 84.8% / 80.5% domain by the implementer; `quality.sh` still does not *gate* them (feature 34). Unchanged from round 1 and not this feature's debt, recorded as open rather than ticked
- [x] no Jest anywhere

**C5 — session close**
- [ ] `progress/history.md` entry with effort record — **deliberately not written**: rejected features are not closeable. §9 below carries the measured numbers and the three findings the leader asked to be preserved, so the eventual entry is a transcription
- [x] no suspicious untracked files (probe backups live outside the repo)
- [x] `feature_list.json` reflects true state (set back to `in_progress` by this verdict, single-line edit)
- [x] the human will be told what was done and how to test it
- [x] Claude did not commit

**C6 — SDD**
- [x] `specs/fulfillment_stock/` has all three documents
- [x] `requirements.md` is EARS with `R<n>`/`FS<n>` ids
- [ ] **every task genuinely ticked** — **G7 is ticked `[x]` while the `reason` assertion its own sentence names is absent (D2)**. G3 and G5, round 1's instances, are now genuinely satisfied
- [x] every `R<n>` covered by a named test recorded in `specs/shared/test-matrix.md` — re-walked below
- [x] the spec commit will precede the implementation commit

**C7 — spec-reuse fidelity**
- [x] `specs/shared/` byte-identical to #7's apart from `test-matrix.md`'s Status column — round 1 verified by real `diff` against the #7 checkout; the fix round did not touch the file (mtime + `git diff` both confirm)
- [x] no silent fork; no amendment proposed or needed
- [x] the `R<n>` ids are #7's and the .NET realisation satisfies the same requirements
- [ ] n8n workflows — not this feature's surface
- [ ] black-box API script — not this feature's surface
- [ ] **effort records complete and honest** — cannot be ticked while the feature is open; §9 holds the numbers and one **correction to round 1's own estimate**
- [x] README benchmark section — owed at phase close, unchanged by this feature

**Traceability, re-walked for the rows this round touches.** `R30`–`R35` and `R61`'s domain half are unchanged since round 1 and still map to the named tests in `test-matrix.md` column 5; probes A, B and C re-killed the `R32`/`R33` integration halves rather than re-reading them. `R34`'s integration half (`R34_AnswersSuccessAndEmitsNoSecondFact_…`) is **not** the test carrying D2 — D2 sits in the *happy path* test in the same file, which no `R<n>` row cites, only `tasks.md` G7. So D2 is invisible to traceability by construction, which is the third time in this build that sentence has had to be written.

---

## 8. What must change before re-review

Bounded deliberately. **Do not re-touch anything else in this feature**, and do not redo any arming already recorded.

1. **Assert the released fact's `reason` where it can fail.** In `tests/Fulfillment.IntegrationTests/StockReleaseIdempotencyTests.cs`'s `ReleaseHappyPath_…`, after `Assert.Single(factRows)`, deserialise the row's `Payload` with `JsonSerializer.Deserialize<StockReleasedPayload>(row.Payload, JsonWire.Options)` — the same two lines the rejected-path fix already uses — and assert the payload's `Reason` equals the request's `"order_cancelled"`. This is what G7 already asks for.
2. **Arm it with probe D's mutation.** Replace `released.Reason` with a literal in `StockFactPayloadMapper.cs`, confirm the test above **FAILS**, record the message verbatim in the impl report's arming table, restore from a backup copy, force the rebuild, re-run green.
3. **Sweep the remaining task list for the same shape, once, and say what you found.** Every `tasks.md` sentence containing *"exactly one"*, *"no second"*, *"zero rows"*, *"only on"* or *"carrying …"* — flagged or not — and for each, state whether an assertion exists. G3, G5 and G7 were three of them; the sweep is one grep and it is the only way to know there is no fourth. Record the result in the impl report even if it is "no further instances".

**Recommended, not required** (say yes or no in the report, either is fine): one assertion that a business identifier survives the mapper on the *accepted* path too — e.g. `retailerCode` in `FS3`'s existing outbox read. Probe D shows `stock.reserved.v1`'s payload fields are currently unobserved end to end, and this is the last cheap moment before feature 19 copies the mapper.

Advisories A2, A3 and A5 remain **not owed** for re-review; §4 recommends them as backlog entries for the leader to add.

---

## 9. Carried forward for the history entry this verdict again withholds

**Effort, measured** (mtimes are CEST, converted to UTC; estimates, recorded as such):

| Phase | #8 | #7's baseline |
|---|---|---|
| Spec + human gate | ≈27 min (03:36Z → 04:03Z) | ≈8 min |
| Implementation | ≈1 h 50 min (04:03Z → 05:54Z), 11 armings + the live walkthrough | ≈1 h 35 min |
| Review pass 1 | **≤19 min** (05:54Z → the verdict file's own last write at **06:12:48Z**) | ≈75 min for 2 passes |
| Fix round 1 | ≈25 min (06:13Z → 06:38Z), test files at 06:15/06:17/06:24Z, report at 06:38Z | — |
| Review pass 2 | ≈35 min, four probes, five container runs (~14 min of Testcontainers alone) | — |
| Rounds | **2 review passes, 2 rejections, 2 blocking defects — both "the branch works, nothing observes it"** | 2 review passes — rejected then approved, 1 blocking defect |

**A correction to round 1's own §10, made here and not by amending it:** §10 recorded review pass 1 as "≈50 min (05:54:06Z → this verdict)". The verdict file's mtime is `06:12:48Z`, which bounds that pass at **≤18.7 min** from the implementer's final save. The file record is the stronger evidence and the ≈50 min figure should not be carried into `history.md`. Round 1's text stands as written; this is the number to use.

**#7's baseline for this feature: 1 spec session, 1 implementation session ≈1 h 35 min, 2 review passes — rejected then approved.** #8 has now reached the same round count with a second rejection outstanding, so the *shape* has diverged: #7 needed one fix round on this feature and #8 needs two, both on the same defect class.

**The ported-idiom ledger earned its cost — round 1's finding, carried forward in full and unchanged.** Twelve rows, none merely asserted, four independently probed and killed; it cost ≈15–20 min of a spec session that ran ≈27 min against #7's ≈8, and it bought four properties that would otherwise have been silent losses: **L7** (`CONFLICT` banned from this service's mapper — the row that alone justifies the convention, since feature 42 made `CONFLICT` terminal and a deadlock victim answered `CONFLICT` would end the order's saga permanently *while satisfying `R32`'s text exactly*), **L1** (explicit `UPDLOCK, HOLDLOCK` under RCSI, guarded by a test that asserts the interleaving was *observed* via `sys.dm_exec_requests`), **L2/L3** (the application-fixed per-row lock order, and the invariant-uppercase ordinal sort the row's own question exposed), and **L4** (`long` counters, probed and killed). Its honest limits stand too: **L2**'s arm stayed green and is guarded by construction, **L8**'s reproduces on a majority of runs and not all, both disclosed rather than dressed up; and the ledger did **not** catch D1 — nor D2 — because neither is a ported property. **Verdict on the convention: keep it, unchanged, for phases 10–13. Its 0-for-3 predecessor record is 4-for-4 on its first outing.** Round 2 adds nothing against it and nothing for it: it aimed at a specific blind spot and hit it.

**The ticked-but-absent pattern — now at three instances, and the flag diagnosis holds.** The saga orchestrator's committed-offset task, this feature's G3/G5, and now this feature's G7. In all three the feature armed **every flagged task perfectly** (11 of 11, then 12 of 12) and the defect sat in an **unflagged** task whose prose nonetheless made a countable or identity claim. The mechanism is not carelessness: ticking records *"a test covers this scenario"*, and the tasks all asked for something narrower — *does a test fail if I delete the specific write this sentence is about?* The remedy landed this session in `CLAUDE.md`'s testing conventions and in `.claude/agents/spec_author.md`, placing responsibility for flagging on the spec author rather than on the implementer's judgement, which is the right home for it. **What D2 adds is the limit of that remedy: it cannot retro-flag a task list already written.** G7 was authored before the amendment existed, so the amendment could not have caught it — only a sweep of the existing prose could, which is why §8.3 asks for exactly that sweep before this feature closes. For phases 10–13 the amendment should hold; for the three task lists already approved, someone has to grep.

**And a note on what found each of them:** D1 by a probe that deleted a persistence call; D2 by a probe that corrupted a payload field. Round 1 ran six probes and none of them was the second kind. *Deleting* a write and *corrupting* a value are different attacks and they find different defects — an emission guard says the fact exists, and says nothing about whether it is right.

**For the phase-9 picture, unchanged from round 1 and worth repeating at close:** this feature made a latent Orders-side defect reachable for the first time — feature 46 `orders_stock_check_rpc_error_discriminator`, where an `RpcError` reply to `stock.check` deserialises to `Lines = null` and throws a bare `NullReferenceException` out of order acceptance. `FS22` keeps that path off the ordinary route today (an unknown product answers `available: 0, sufficient: false`), so the seam is reachable but unexercised. Phase 9 should close it deliberately rather than let an integration run discover it.

---

*Reviewed by the `reviewer` agent, pass 2. Round 1 left intact above. Feature 17 set back to `in_progress` in `feature_list.json` (single-line edit; no `git checkout --`, no rewrite). No `progress/history.md` entry — the feature is not closeable. No commit made.*

---

# Review — `fulfillment_stock` (feature 17, phase 9) — **pass 3 (round 3)**

> **Rounds 1 and 2 above are closed and are not amended by this section.** Nothing in either is reopened, withdrawn or re-scored. Where round 3 corrects a measurement or records something an earlier round missed, it says so here and leaves the earlier text as written.

**Verdict: APPROVED.** No blocking defect. **D2 is genuinely closed, and closed against both mutation families** — I armed the corruption and the absence separately and each one killed the same test at a different line, so there is no gap between "the row is there" and "the row is right". The sweep spot-checked out on nine of ten claims I chose independently; the tenth is a real but non-blocking finding I could size by probe rather than by argument (**A6**, below), and it is not the ticked-but-absent shape the sweep was asked to hunt. The live-stack claim is confirmed from the two databases, digit for digit, and it is Phase 8's undemonstrated claim finally closing.

---

## 1. What I ran, and what I did not

Per `CLAUDE.md`'s reviewer rule I did **not** re-run `./quality.sh`, `Orders.UnitTests` or `Orders.IntegrationTests` — the fix round touched two integration test files and nothing else, and no claim under test this round is about the full solution. What I ran independently:

| Run | Result |
|---|---|
| Five fresh mutation probes, **three corruption, two deletion/rename**, each with a forced `--no-incremental` rebuild (§2, §4) | **5 killed, 0 survived** |
| `dotnet test tests/Fulfillment.IntegrationTests` (full, real MsSql/NATS/Kafka containers), post-restore, post-forced-rebuild | **48/48 green, 2 m 37 s** |
| `dotnet test tests/Fulfillment.UnitTests` (twice: after the domain restore, and after the payload-record restore) | **79/79 green** both times |
| `dotnet test tests/Architecture.Tests` | **16/16 green** — C3 walked by running NetArchTest, not by eye |
| Six read-only query batches against the **live** `otc_orders` / `otc_fulfillment` databases (§5) | every claim confirmed, including the cross-service chain on five orders |
| Independent extraction of all **sixteen** `fulfillment.stock.*` schemas from `specs/shared/asyncapi.yaml` (parsed, not grepped) and comparison against the ten payload records' asserted key sets | **no drift** — every key set matches |
| Dangling-citation sweep re-run over `specs/fulfillment_stock/{requirements,design,tasks}.md` + `specs/shared/test-matrix.md` against all **431** test method names under `tests/` | **zero dangling test citations** (the 37 remaining hits are `SCREAMING_SNAKE` error codes and env vars, not method names) |
| `git diff -- specs/shared/` | still 9 insertions / 9 deletions, column 5 of `test-matrix.md` plus the §1 counts — **nothing else** |
| Test-file hygiene sweep (§4) | no `Skip =`, both 10× loops intact, every wait still 10 s / 15 s / 20 s as before |

All five probe files were backed up first (`…/scratchpad/r3backups/`, `md5sum` recorded), restored by `cp` from those backups (**never** `git checkout --`), verified byte-identical with `diff`, re-read at the changed line, `touch`ed, and force-rebuilt before every confirming run. `grep -rn "PROBE3\|Satisfied" src/ tests/` is empty and `git status --short` is byte-for-byte the list rounds 1 and 2 recorded. No reviewer residue.

---

## 2. D2's fix, attacked from both directions — the question the brief asked, answered

The brief asked whether the new payload assertion also needs to survive the **row being absent**, or whether that is covered elsewhere. **One test answers both, and the two answers fail at different lines** — which is the same independence check round 2 applied to D1's fix, and it comes out the same way.

**Probe R3-A — corruption.** `src/Fulfillment/Infrastructure/Outbox/StockFactPayloadMapper.cs:33`, `released.Reason` → `"PROBE3-WRONG-REASON"`:

```
StockReleaseIdempotencyTests.ReleaseHappyPath_…AndExactlyOneStockReleasedV1CarryingTheReason [FAIL]
  Assert.Equal() Failure: Strings differ
  Expected: "order_cancelled"
  Actual:   "PROBE3-WRONG-REASON"
  at …/StockReleaseIdempotencyTests.cs:line 53
```

**Probe R3-B — absence.** `src/Fulfillment/Domain/OrderStockReservation.cs:241`, `carrier.RecordOrderFact(fact);` deleted from `Release` — the fact suppressed while the reply, the counter and the row statuses all stay correct:

```
StockReleaseIdempotencyTests.ReleaseHappyPath_…AndExactlyOneStockReleasedV1CarryingTheReason [FAIL]
  Assert.Single() Failure: The collection was empty
  at …/StockReleaseIdempotencyTests.cs:line 48
```

**Line 48 versus line 53. There is no gap between them.** The absence family is caught by `Assert.Single(factRows)` after a 10 s monotonic `WaitForAsync`, the corruption family by the `Reason` equality two lines later, and neither failure implies the other: under R3-A line 48 executed and passed, under R3-B line 53 was never reached. The three other tests in the class stayed green under R3-B, which is correct — `R34_AnswersSuccessAndEmitsNoSecondFact`, `FS9` and `FS10` all assert the *suppression* direction (`Assert.Empty(factRows)`), so a deleted emission cannot make them red. That asymmetry is the right shape, not a hole: the suppression tests guard against a fact appearing, the happy path guards against one vanishing, and both are present.

**A single assertion answering both questions is what the brief said was fine, and this is slightly better than that** — two assertions, one per family, in one test, on one artefact.

---

## 3. The sweep — spot-checked independently, and one thing it did not cover

The sweep is the load-bearing claim of this round and it is a **negative** claim, so I did not accept it. I picked my own candidates from `tasks.md` rather than re-walking the implementer's list, read each cited test **body** in full, and where the claim was about a value on the wire I attacked it with a probe instead of reading.

**Verified genuinely asserted (ten claims, chosen by me, seven of them not on the sweep's own list):**

| `tasks.md` | The claim | What the test actually does |
|---|---|---|
| **G2** | `R31`: "assert row equality before/after and an empty outbox" | `StockCheckTests.cs:47-55` — re-reads `units`/`reserved_units` after the reply **and** `Assert.Equal(0, await readDb.OutboxMessages.CountAsync())`. A direct count, not a proxy |
| **G2** | `FS22`: unknown product "never with an `RpcError`" | Deserialises into `StockCheckReplyPayload` and asserts `available: 0`, `sufficient: false`; an `RpcError` body would leave `Lines` null and throw at `Assert.Single`. Guarded, if indirectly — and the indirection is itself feature 46's bug shape, which is worth knowing |
| **G4** | "exactly one reservation row still in status `released`, and **zero** outbox rows" | `StockReserveTests.cs:206-213` — `Assert.Single(reservations)` + `Assert.Equal("released", …)` + `Assert.Empty(factRows)` on an **unfiltered** correlation-id read |
| **G7** | `R34` "no second fact", `FS9` "emits nothing", `FS10` "emits nothing" | All three read the outbox by correlation id with **no `event_type` filter** and assert `Assert.Empty`. `FS10` also re-reads the reservation and asserts it is still `consumed` |
| **G8** | happy path "units up, `reserved_units` and reservations untouched, **outbox empty**" | `StockReplenishTests.cs:60-62` — a direct `CountAsync() == 0`, plus the reservation's status re-read |
| **G8** | `FS14` "replenishes **no** line when any line is unknown" | Re-reads `units` and asserts `10` — the all-or-nothing claim, not just the error code |
| **E7** | "asserting the dispatcher was **never** called" | `StockResponderHeaderTests.cs:28` — `Assert.False(dispatcher.WasCalled, …)`, a recorded fake, not an inference from the reply |
| **C6** | "no save happened"; "`ExecuteAsync` is never called"; "the reply is returned only after `ExecuteAsync` resolves" | Three separate counters asserted at `0`, `0` and `1`/`1`. Counts, not shapes |
| **B5** | "`RecordOrderFact` refusing a foreign `AggregateId`" | `StockItemTests.cs:171-175` — asserts the throw, **both** ids on the error, **and** `Assert.Empty(item.DomainEvents)` |
| **G5** | `FS6` "exactly one reserved and one rejected", 10× | Re-read in full (`StockReserveRaceTests.cs:23-79`): distinct correlation id per order, winner/loser attributed from the replies, `Assert.Single` on each side, inside the loop. Round 2's assessment holds on a third reading |

**The one thing the sweep did not cover is not a countable claim, and I record it as an advisory rather than a defect — see A6.** `tasks.md` C3 makes a *provenance* claim ("read `asyncapi.yaml` as text and compare"), not a count, an identity or a field value, so it falls outside the grep round 2's §8.3 specified and outside the shape the sweep was asked to hunt. The implementer's sweep did what it was told to do, and its "no fourth instance" conclusion is **correct as scoped**. I am recording the scope explicitly here so that nobody later cites the sweep as blanket clearance: it cleared countable claims, not every sentence in the document.

---

## 4. My own mutation probes

| # | Family | Mutation | Expected guard | Result |
|---|---|---|---|---|
| **R3-A** | corruption | `StockFactPayloadMapper`: `released.Reason` → literal | `ReleaseHappyPath_…CarryingTheReason` | **KILLED** at line 53 |
| **R3-B** | deletion | `OrderStockReservation.Release`: `carrier.RecordOrderFact(fact)` deleted | same test, different line | **KILLED** at line 48 |
| **R3-C** | corruption | `OutboxWriter`: `CausationId = stockEvent.CorrelationId.Value` — the causation id silently stamped with the correlation id, a plausible copy-paste regression that leaves both columns non-null and well-formed | `FS3_StampsCorrelationIdFromTheHeaderAndCausationIdFromTheRequestId_…` | **KILLED** — `Assert.Equal() Failure: Values differ` at `StockReserveTests.cs:76` |
| **R3-D** | corruption | `KafkaFactPublisher`: `Key = fact.Key` → literal — the partitioning key wrong on the wire while the row, the payload and `published_at` all stay correct | `FS16_PublishesTheFactsOfAReserveTransaction…KeyedByCorrelationId_…` | **KILLED** — `Expected: "d1af7c0c-…" / Actual: "PROBE3-WRONG-KEY"` at `FulfillmentOutboxRelayTests.cs:80` |
| **R3-E** | rename | `StockRpcPayloads.cs:23`: `bool Sufficient` → `bool Satisfied` — a unilateral drift of a reply record away from the AsyncAPI schema | `StockRpcPayloadTests.StockCheckReplyPayload_SerialisesWithTheDeclaredCamelCaseKeys` | **KILLED** — `Assert.Equal() Failure: HashSets differ`, `["…","sufficient"]` vs `["…","satisfied"]` |

**All five killed.** R3-C and R3-D matter beyond this round: they are the two remaining places where a value crosses a service boundary in this feature — the causation chain the saga's forensics depend on, and the Kafka key that decides partition ordering for every downstream consumer phases 10–13 will build — and both are now demonstrated load-bearing rather than assumed. Round 1 attacked deletion six times and never these; round 2 found one of this family; round 3 finds the family closed.

**Round 3's third pass over the same test files found nothing weakened.** `grep -rn "Skip *=" tests/Fulfillment.*` is empty; both race loops are still `for (var i = 0; i < 10; i++)`; every `WaitForAsync` budget is still 10 s and `FulfillmentOutboxRelayTests`' consumer still 20 s; `StockItemTests.cs:151`'s A4 assertion (`Assert.Equal(int.MaxValue, shortage.Requested)`) is intact; `specs/fulfillment_stock/{requirements,tasks}.md` (mtime `07:49` local) and `specs/shared/test-matrix.md` (`07:48`) both **pre-date** fix round 2's test edits (`09:12`), so no spec was bent to fit code in either fix round. Test counts unchanged at 79 and 48.

---

## 5. The live-stack evidence — confirmed from the databases, not from the report

Queried directly against the running compose stack (both application hosts stopped; the infra containers up 4 h). **This is Phase 8's undemonstrated claim closing, and it belongs in the history entry.**

- **All four previously-parked orders are through.** `ORD-000007` – `ORD-000010`: order `stock_reserved`, `stock.reserve` `sent` with `attempts = 9`, one `credit.hold` row each, `parked` at `attempts = 6` — Billing does not exist yet, which is the designed steady state, and the count has not moved since round 1, consistent with the hosts being down.
- **`ORD-000011` is the first end-to-end acceptance in this repository**: `stock_reserved`, `stock.reserve` `sent` at `attempts = 0` — answered on the first try by a live responder, never parked.
- **Reservations match the parked payloads exactly**: `ORD-000007`→`PRD-0001` ×2, `ORD-000008`→`PRD-0002` ×3, `ORD-000009`→`PRD-0002` ×3, `ORD-000010`→`PRD-0001` ×2, `ORD-000011`→`PRD-0001` ×1, all `reserved`. `IBERFOODS/PRD-0001 reserved_units = 5`, `IBERFOODS/PRD-0002 reserved_units = 6` — the exact sums.
- **The cross-service chain is exact on all five orders.** For each, the `otc_fulfillment.outbox` row's `correlation_id` equals `otc_orders.orders.id` and its `causation_id` equals that order's `stock.reserve` `saga_commands.id`, digit for digit — e.g. `ORD-000007`: `8B0670D1-082D-4462-91B1-0495C488D3E2` / `D6441D2F-6C9A-45AA-BDD2-AD2D709F669C` on both sides of the boundary. Published `05:34:19.814` – `05:35:27.222`. **Two databases, no shared FK, joined only by identifiers carried in messages — which is the architecture rule being observed rather than asserted.**
- **`F2` holds across the entire live database.** The correlated query for any `stock` row whose `reserved_units` differs from the sum of its `reserved` reservations returns **0 rows**, over all 100+ seeded rows, not just the touched ones.
- No reservation exists for `ORD-999999` — H5's negative check left no side effect.

---

## 6. Traceability, walked a third time

| `R<n>` | Cited test (`test-matrix.md` §4 column 5) | Verified this round |
|---|---|---|
| `R30` | `StockItemTests.R30_RejectsInFullAnyOperation…` | Name resolves; body asserts throw + unchanged counters + unchanged reservations |
| `R31` | `StockCheckTests.R31_AnswersPerLineWithoutMutating…` | Re-read in full (§3) — row equality **and** an outbox count of 0 |
| `R32` | `ReservationTests.R32_CreatesOneReservationPerLine…` | Killed by round 1's P3; unchanged since |
| `R33` | `ReservationTests.R33_CreatesNoReservationAtAll…` (domain) + `StockReserveTests.RejectedPath_…RowInTheOutbox…` (integration) | Both halves present; the integration half is D1's fix, re-armed in round 2 |
| `R34` | `OrderStockReservationTests.R34_…` (domain) + `StockReleaseIdempotencyTests.R34_AnswersSuccessAndEmitsNoSecondFact_…` (integration) | Domain half killed by round 1's P4; integration half re-read (§3) and confirmed to assert `Assert.Empty` on an unfiltered read |
| `R35` | `ReservationTests.R35_RefusesEveryTransition…` | Name resolves; body re-checks status and `From` after each of four illegal calls |
| `R61` | `StockItemTests.R61_…` (domain half) | Present; B10's arm is a genuine fact-**suppression** guard. API half correctly `TODO`, deferral argued in-cell and gate-ratified |
| `R36` | correctly `TODO` — only `Consume()` lands here, unit-tested and uncalled | n/a, feature 18 |

**Every `R<n>` this feature flips maps to a named test that exists and exercises the requirement's distinguishing branch.** The 431-method dangling-citation sweep found no citation anywhere in this feature's four documents that does not resolve to a real method — so round 2's two renames remain consistent after round 3's reading, and nothing in the fix rounds orphaned a reference.

**D2 sat outside this table by construction** — the release happy path is cited by `tasks.md` G7 and by no `R<n>` row. That remains true, and it is the third time in this build the sentence has had to be written; it is the reason the ledger and the sweep exist alongside traceability rather than inside it.

---

## 7. Advisories

**A6 (new, non-blocking) — `StockRpcPayloadTests` does not read `asyncapi.yaml`, while three documents say it does.** `tests/Fulfillment.UnitTests/StockRpcPayloadTests.cs:9-15` claims in its own XML doc *"with exactly the keys `specs/shared/asyncapi.yaml` declares"*, `tasks.md` **C3** says *"read `asyncapi.yaml` as text and compare each schema's declared property names against the record's `JsonWire`-serialised property names"*, and `design.md` §6.3 says the instrument *"`tests/Orders.UnitTests/SagaCommandPayloadTests.cs` already established: it reads `asyncapi.yaml` as text"*. **It does not.** Both instruments compare against key lists retyped by hand (`AssertKeys(json, "companyCode", "lines")`), and `SagaCommandPayloadTests` — the reference the design names — has never read the file either. So `design.md` §6.3 misdescribes #8's own existing code, `tasks.md` C3 inherited the misdescription, and the implementer copied the instrument that actually exists while copying the doc sentence that describes the one that does not.

**Why it is an advisory and not D3, sized by probe rather than argued:**

1. **There is no drift.** I parsed all sixteen `fulfillment.stock.*` schemas out of `specs/shared/asyncapi.yaml` and compared them to the ten records' asserted key sets myself — including the two the retyped lists could most easily have got wrong, `StockListRequestPayload` (an `allOf` over `PageRequest`, so its properties are not literally in the schema node) and `PageInfo`. Every key set matches. The transcription is correct.
2. **The retyped list is still load-bearing.** Probe R3-E renamed one reply property and the test died immediately. So the guard does catch unilateral drift of the code away from the spec, which is the direction that can actually happen while `specs/shared/` is read-only and human-gated.
3. **What is genuinely lost is only the authoring-time correlated error** — the same wrong name typed into both the record and the test — and I have just excluded that possibility for this feature by direct comparison.

**It still matters, for one reason: feature 19 copies this instrument, and phases 10–13 copy the false sentence with it.** Round 1 praised `FS21` precisely for reading the terminal set *out of Orders' source as text* rather than retyping the nine codes; this is the same distinction, decided the other way, in a test whose doc comment claims the stronger form. That is the A1 class — a name or comment asserting an assertion that is not made — and A1 was already a finding on this feature.

**Backlog wording, if the leader wants it (one entry; I am adding none myself):**

> **`rpc_payload_schema_parity_from_asyncapi`** — *`StockRpcPayloadTests` and `SagaCommandPayloadTests` compare wire records against hand-retyped key lists, while `design.md` §6.3, `tasks.md` C3 and the test's own XML doc all claim they read `specs/shared/asyncapi.yaml` as text.* Acceptance: both instruments derive their expected property names by parsing `specs/shared/asyncapi.yaml` (the `RpcSubjectsTests` / `OrdersFactTopicTests` pattern, which already reads that file), covering every `fulfillment.stock.*` and saga-command request/reply schema; armed by editing one schema's property name in a scratch copy and confirming the named test fails; and the three documents' sentences are made true or corrected. Notes: found by feature 17's review round 3 (advisory A6). **No drift exists today** — the review parsed all sixteen schemas and confirmed every key set matches — and the retyped lists do catch unilateral drift of the code (probe R3-E killed a renamed reply property). What is missing is only the correlated authoring error, and the reason to close it is that feature 19 copies this instrument into Billing, Projector and Notifications and copies the overstated doc comment with it. Cheapest at the relay-family consolidation. Suggested phase 10.

**A2, A3, A5 are now backlog ids 48, 49, 50** and are not owed here. **A1 and A4** were closed in fix round 1. **No advisory from any round is left unaccounted for.**

---

## 8. The ported-idiom ledger — did it earn its cost? Yes, unchanged from rounds 1 and 2, and this round adds one line to the account

Round 1 checked all twelve rows' **claims** and probed four; round 2 added nothing for or against; round 3 confirms the verdict stands and records one thing neither earlier round said.

**The ledger's four real purchases, unchanged:** **L7** (`CONFLICT` banned from this service's error mapper — the row that alone justifies the convention, because feature 42 made `CONFLICT` terminal and a deadlock victim answered `CONFLICT` would end the order's saga permanently *while satisfying `R32`'s requirement text exactly*), **L1** (explicit `UPDLOCK, HOLDLOCK` under RCSI, guarded by a test that asserts the interleaving was *observed* via `sys.dm_exec_requests` before it asserts anything else), **L2/L3** (the application-fixed per-row lock order, and the invariant-uppercase ordinal sort that only the row's own question exposed), **L4** (`long` counters, probed and killed). Its honest limits stand too: **L2**'s arm stayed green and is guarded by construction, **L8**'s reproduces on a majority of runs and not all, both disclosed rather than dressed up.

**What round 3 adds: the ledger's blind spot is now measured, not just asserted.** The ledger did not catch D1, did not catch D2, and did not catch A6 — and A6 is the interesting one, because it is *nearly* a ledger question and is not one. #7's equivalent instrument had the same shape, so nothing was lost in translation; what went wrong is that `design.md` described the instrument it wished existed. **The ledger asks "what supplied this property over there, and does that thing exist here?" — it does not ask "is the sentence I just wrote about my own repository true?"** Three of this feature's four findings across three rounds were of the second kind. That is not an argument against the ledger; it is a measurement of what still needs a different instrument, and in this feature that instrument was the sweep plus five probes.

**Verdict on the convention, as the human gate asked when adopting it: keep it, unchanged, for phases 10–13.** Its 0-for-3 predecessor record is 4-for-4 on its first outing, at a cost of roughly a fifth of a spec session — 12 rows, ≈15–20 min, in a spec pass that ran ≈27 min against #7's ≈8. **It bought properties that traceability and arming are both structurally blind to, and it did so on the first feature it was applied to.** The question left open at adoption is answered.

---

## 9. `CHECKPOINTS.md` walked — round 3

**C1 — harness complete**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` exist
- [x] `progress/current.md`, `progress/history.md` exist
- [x] `.claude/agents/` holds all five roles
- [x] every agent definition declares its model
- [x] `./init.sh` exits 0 — re-run by me after setting id 17 `done`

**C2 — state coherent**
- [x] at most one feature `in_progress` — with this approval **zero** are; 17 becomes `done`
- [x] every status in `rules.valid_status`
- [x] every `done` feature has passing tests — 17's are 79 + 48 + 16, re-run this round
- [x] `progress/current.md` describes this session
- [x] no `blocked` feature

**C3 — architecture**
- [x] domain purity — `Architecture.Tests` **16/16 run**, not eyeballed, covering `DomainPurityTests`, `CqrsDomainPurityTests`, `DomainDecimalTests` over `DomainAssemblies.All`
- [x] no cross-service DB access — confirmed again by query in §5: the two databases are joined only by identifiers carried in messages
- [x] no shared runtime code beyond `SharedKernel`, `Contracts`, `Cqrs` — the relay family is **copied** with `// COPY OF —` banners
- [x] no `Domain/` namespace references `OrderToCash.Cqrs`
- [x] `SharedKernel` still has zero `PackageReference`
- [x] no `decimal` in domain arithmetic — this service handles no money
- [x] every interaction classifiable — five `fulfillment.stock.*` subjects NATS-RPC, three `stock.*` facts Kafka via the outbox relay; probe R3-D proves the Kafka key is asserted, not assumed
- [x] no stray debug logging, no context-free TODOs, **no probe residue** (`grep -rn "PROBE3\|Satisfied" src/ tests/` empty)

**C4 — verification real**
- [x] `./quality.sh` passes — the implementer's post-fix run, exit 0, twelve projects green; **I did not re-run it**, and instead ran five probes and 143 tests across three projects (§1)
- [x] domain tests pure — no framework, DB or broker reference in the domain unit files
- [x] integration tests use real Testcontainers MsSql / NATS / Kafka — watched them start during my own 48-test run
- [x] **coverage thresholds** — 84.8% / 80.5% domain as measured from the emitted cobertura files, above the ≥80% gate. `quality.sh` does not yet *gate* them; that is **feature 34's** debt, stated plainly by the implementer rather than implied, and it is not this feature's to pay. Ticked as measured-and-above; the gating gap is recorded here and in feature 34
- [x] no Jest anywhere

**C5 — session close**
- [x] `progress/history.md` entry with effort record — written with this approval (§10)
- [x] no suspicious untracked files — probe backups live outside the repository
- [x] `feature_list.json` reflects true state — id 17 → `done` by single-line edit; `git diff` read and confirmed to show **only** that line
- [x] the human will be told what was done and how to test it — the impl report's manual verification script stands
- [x] Claude did not commit

**C6 — SDD**
- [x] `specs/fulfillment_stock/` has all three documents
- [x] `requirements.md` is EARS with `R<n>`/`FS<n>` ids
- [x] **every task genuinely done, not just ticked** — G3 and G5 closed in fix round 1, G7 in fix round 2 and re-armed by me from both directions. C3's provenance sentence is over-claimed rather than absent (**A6**), the property it names holds, and I verified it by parsing the schema file myself
- [x] every `R<n>` covered by a named test recorded in `specs/shared/test-matrix.md` (§6)
- [x] the spec commit will precede the implementation commit — both uncommitted; the human commits after testing

**C7 — spec-reuse fidelity**
- [x] `specs/shared/` byte-identical to #7's apart from `test-matrix.md`'s Status column — round 1 verified by real `diff -rq` against the #7 checkout; `git diff` still shows 9/9 lines, column 5 plus the §1 counts, after both fix rounds
- [x] no silent fork; no amendment proposed or needed by this feature
- [x] the `R<n>` ids are #7's and the .NET realisation satisfies the same requirements, with `FS1` deliberately not reused and argued
- [ ] n8n workflows — not this feature's surface (features 25/31)
- [ ] black-box API script — not this feature's surface
- [x] effort records complete and honest — §10, with round 1's own figure corrected
- [x] README benchmark section — the phase-9 row is owed at phase close, not at this feature's close

---

## 10. Effort, measured — the numbers for the history entry

Timings are file mtimes and subagent dispatch timestamps converted to UTC. Estimates, recorded as such.

| Phase | #8 | #7's baseline |
|---|---|---|
| Spec + human gate | ≈27 min (03:36Z → 04:03Z) | ≈8 min, 1 spec session |
| Implementation | ≈1 h 51 min (04:03Z → 05:54Z), 11 armings + the live walkthrough | ≈1 h 35 min |
| Review pass 1 | **≤19 min** (05:54Z → the verdict file's own write at 06:12:48Z) | — |
| Fix round 1 (D1) | ≈25 min (06:13Z → 06:38Z) | — |
| Review pass 2 | ≈30 min (06:38Z → 07:08Z), four probes, five container runs | — |
| Fix round 2 (D2 + the sweep) | ≈21 min (07:08Z → 07:29Z) | — |
| Review pass 3 | ≈20 min (07:31Z → 07:51Z), five probes, three container runs, six live query batches | — |
| Rounds | **3 review passes, 2 rejections, 2 blocking defects, APPROVED on the third** | **2 review passes, 1 rejection, 1 blocking defect** |
| **Total** | **≈4 h 13 min** | **≈2 h 40 min** (est.) |

**Round 1's own §10 recorded review pass 1 as "≈50 min". That figure is wrong and must not reach `history.md`.** The verdict file's mtime bounds that pass at **≤18.7 min** from the implementer's final save. Round 2 made this correction; round 3 repeats it because it is the number the history entry uses, and because a review that over-reports its own duration by 2.6× corrupts the one measurement this repository exists to produce. Round 1's text stands as written above; this is the number.

**The shape of the two runs has now diverged, and the divergence is the finding.** #7: one rejection, one defect, two passes. #8: two rejections, two defects, three passes — **and both of #8's defects were the same shape as #7's one** (a branch that works and nothing observes), found one round apart because round 1 attacked only half the mutation space. **#8's implementation ran ≈1.17× #7's clock; its review-and-fix cycle ran ≈1 h 55 min against an implementation of ≈1 h 51 min** — review at parity with implementation, exactly what Phase 8's closing assessment told #9 to budget for, now reproduced in Phase 9's first feature.

---

*Reviewed by the `reviewer` agent, pass 3. Rounds 1 and 2 left intact above. Feature 17 set to `done` in `feature_list.json` (single-line edit; no rewrite, no `git checkout --`). Effort record appended to `progress/history.md`. No commit made.*
