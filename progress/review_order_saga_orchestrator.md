# Review — `order_saga_orchestrator` (feature 16, phase 8)

**Verdict: REJECTED.** One blocking defect (**D1**), one required bookkeeping fix (**D2**), four advisories. `feature_list.json` set back to `in_progress`; no entry appended to `progress/history.md` (an effort record is written on approval only).

**The rejection is narrow and the work is not.** Every claim this review was asked to probe held up under independent mutation except one, and that one is a single missing assertion in a single integration test. The two gate-approved architecture rules fire, the sweeper fix is guarded by the test the report names, the fact-emission arming rows I re-ran reproduce verbatim, the live-stack walkthrough is true against the running stack, and every cited test name exists. Estimated cost of the fix: one test method extended, one arming row added, two report paragraphs. This is a re-review of one file, not a re-do.

---

## 1. What I ran, and what I took on trust

Per `CLAUDE.md`'s reviewer guidance I did not re-run the full suite: `./quality.sh` exit 0, `./init.sh` exit 0 and **492 passed / 0 failed** were independently confirmed by the leader before this review, as were the `specs/shared/test-matrix.md` column-5 confinement and the deliberately-ungated coverage (`quality.sh` lines 4-8 and 80-83 name feature 34, `sonarqube_quality_gates`, phase 21, as the owner of the threshold — a low per-assembly number is not a defect today).

Run by me, independently:

| Probe | Result |
|---|---|
| `FactPublisherConfinementTests` + `FactConsumerConfinementTests` against **my own** violations (a real `ProducerBuilder<string, byte[]>` under `Application/Sagas/`, a real `ConsumerBuilder<string, byte[]>` under `Presentation/`) | **Both FAIL**, naming my probe types — the corrected rules genuinely fire |
| The same two violations against the **un-suffixed** type names design.md §10 specified | **Producer rule stays GREEN** — §4's finding is true, and the correction was necessary, not cosmetic |
| Sweeper reverted to the claim-then-issue path (`DispatchAsync(row.OrderId, row.Command)`) | `SagaCommandRetryTests.SO3_APendingRowCommittedWithNoInProcessSignal_...` **FAILS** (`Assert.True() Failure / Expected: True / Actual: False`, line 136) — deviation 3's fix is guarded |
| Group K spot-checks K2 (delete `Complete`), K4 (`CompensationStepsFrom` ⇒ `[]`), K8 (`credit.rejected.v1` given a status-changing `Apply`), armed together | **5 cases FAIL**, messages identical to the report's rows (`Expected: Completed / Actual: Paid`; `Assert.Single() Failure: The collection was empty`; `Expected: StockReserved / Actual: CreditApproved`; and R27's non-null `Apply` assertion) |
| **`EnableAutoCommit = true` ⇒ `false`** in `KafkaFactStreamSubscriber` | **`SagaConsumptionTests` stays GREEN, 2/2** — see D1 |
| `AllocateNextAsync_ConcurrentFirstEverAllocations_...` × 4 idle, × 2 under 16-core saturation | **6/6 green**, 3-4 s idle, 15-22 s loaded |
| Live compose stack queried directly (`otcnet-mssql`, `otc_orders`) | Exactly three `parked` `stock.reserve` rows for `ORD-000007/8/9`, `attempts = 3`, transport-failure `last_error`, all three orders still `placed`, `saga_ignored_facts` empty — §8 of the report is accurate |
| All sixteen test case names cited in `test-matrix.md` §3 and `requirements.md` §3 | All present, character-exact |
| `NetArchTest` suite (`Architecture.Tests`) | **16/16 green** after every restore |
| `specs/shared/` vs the #7 checkout, file by file (`cmp`) | Six of seven files **byte-identical**; only `test-matrix.md` differs |
| Confirming green after my three source mutations were restored | `SagaStepTableTests` 100/100; `Architecture.Tests` 16/16; `SagaConsumptionTests` + SO3 crash-window 3/3 |

Every mutation was backed up by copy, restored from the backup, `touch`ed to force the rebuild, re-read on disk, and re-run green. `git status` is byte-for-byte what it was when this review started, plus the one `feature_list.json` status flip this review owns.

---

## 2. CHECKPOINTS walk

### C1 — The harness is complete

- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (plus `suite_runner`).
- [x] Every agent definition declares its model.
- [x] `./init.sh` exits 0 (confirmed by the leader; unchanged by this review other than the status flip, which `init.sh` accepts as `1 feature in_progress`).

### C2 — State is coherent

- [x] At most one feature `in_progress` — exactly one after this review's flip (`order_saga_orchestrator`).
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it.
- [x] `progress/current.md` describes the active session (feature 16), not leftovers. **It will need its Status line moved off "in_review" when the fix pass starts.**
- [x] Every `blocked` feature records why — there are none.

### C3 — Architecture is respected

- [x] No `Microsoft.EntityFrameworkCore` / `Confluent.Kafka` / `NATS.*` / `MongoDB.*` / `Microsoft.AspNetCore.*` inside any `Domain/` — verified by **running** `Architecture.Tests` (16/16), not by eye. Nothing under `src/Orders/Domain/` was touched by this feature.
- [x] No cross-service DB access; no FK crosses a service boundary. The saga carries `OrderReference`/`CompanyCode`/`RetailerCode`/`ProductCode` in payloads, never identifiers into another schema.
- [x] No shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs` — `ls src/` unchanged; the RPC payload records correctly live in `src/Orders`, not in `Contracts` (design.md §6.1, feature 15's precedent).
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` — `CqrsDomainPurityTests` green.
- [x] `src/SharedKernel` still has zero `PackageReference` — `SharedKernelHasNoPackagesTests` green.
- [x] No `decimal` in domain arithmetic — `DomainDecimalTests` green; `SagaMoney(long Amount, string Currency)` and every money field in `SagaCommandPayloads.cs` is `long`; the only `double` is a backoff-millisecond computation in `EfCoreSagaCommandStore.ParkAsync`.
- [x] Every interaction classifiable as Kafka-fact or NATS-RPC — facts consumed from the three `*.facts.v1` topics, the five commands issued over NATS request-reply on the `asyncapi.yaml` subjects. No Kafka-as-request-bus, no RPC-for-facts.
- [x] No stray debug logging, no context-free TODOs — grep across every new file returns nothing; the two `TODO`s in `quality.sh` name feature 34.

### C4 — Verification is real

- [x] `./quality.sh` passes (leader-confirmed, exit 0).
- [x] Domain-pure tests are pure — `SagaStepTableTests` builds real `Order` instances with no store, no framework, no broker; `Application/Sagas/` carries no `Microsoft.*`/`Confluent.*`/`NATS.*`/EF Core reference.
- [x] Integration tests use Testcontainers against real MsSql / Kafka / NATS — verified by running them; no mocked brokers anywhere in this feature.
- [x] Coverage thresholds — **not gated by design**, `quality.sh` naming feature 34 (phase 21) as the owner and explicitly refusing to "fake a gate that does not gate". Applicable-but-deferred, not empty.
- [x] No Jest anywhere.

### C5 — The session closed cleanly

- [x] No suspicious untracked files — `git status` is exactly the feature's own new sources and tests.
- [ ] `progress/history.md` has an entry for the feature **including its effort record** — **not applicable on a rejection**, and deliberately not written. It is owed at approval.
- [x] `feature_list.json` reflects the true state — set to `in_progress` by this review.
- [x] The human has been told what was done and how to test it manually — `progress/impl_order_saga_orchestrator.md` §8 is the manual recipe, and I confirmed its observation against the live stack.
- [x] Claude did not commit.

### C6 — Spec-Driven Development

- [x] `specs/order_saga_orchestrator/` has all three of `requirements.md`, `design.md`, `tasks.md`.
- [x] `requirements.md` uses strict EARS with `R<n>`/`SO<n>` ids on every requirement.
- [ ] **Every task ticked** — `tasks.md` **M5 is `[ ]`** (see D2). Also, F5 is ticked `[x]` while half of its stated content is absent (D1).
- [ ] **Every `R<n>` covered by a concrete named test** — true for R19-R29 and for SO1-SO8, SO10, SO11. **SO9 is only half covered** (D1): the clause "advance the committed offset ... only **after** the handler ... has returned successfully" is proven on its "not before" side and unproven on its "does advance" side.
- [x] The spec commit precedes the implementation commit — spec and implementation are both uncommitted; the human commits, and the spec pass is a separate document (`progress/spec_order_saga_orchestrator.md`) predating the code.

### C7 — Spec-reuse fidelity and benchmark honesty

- [x] **`specs/shared/` byte-identical to #7's except `test-matrix.md`'s Status column** — verified with `cmp` against `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs/specs/shared/`: `asyncapi.yaml`, `domain-model.md`, `n8n-workflows.md`, `openapi.yaml`, `requirements.md`, `saga.md` all identical; `test-matrix.md` differs in the Status column and the derived tallies only, and this feature's own `git diff` touches nothing else. The tallies are arithmetically right (16→25 done, 1→3 partial, 46→35 todo).
- [x] Every deviation is a recorded amendment — **no amendment to `specs/shared/` was made or needed**; the `R29` split was inherited from #7's own gate, not re-forked.
- [x] The `R<n>` ids are #7's, and the .NET realisations genuinely satisfy them — walked row by row in §3 below.
- [ ] `n8n/workflows/*.json` fire green against the .NET Gateway — **not applicable**: no Gateway exists yet.
- [ ] The black-box API script proves the same saga steps as #7's — **not applicable yet**; this is exactly the `R24` API half and the `R28` e2e half both left `TODO` by the ratified scope.
- [ ] `progress/history.md` effort records complete — owed at approval.
- [x] The README's benchmark section — unchanged by this feature; the benchmark note for it is in §6 below, ready for the history entry.

---

## 3. Traceability walked

Shared rows, all cited names confirmed present and character-exact, and the tests read for substance (not just existence):

| Req | Test | Verified |
|---|---|---|
| R19-R24 | `SagaHappyPathTests.R19_R24_HappyPath_AdvancesTheOrderThroughEveryStatusIssuingEachOwedCommandAndEmittingExactlyOneOrderConfirmedAndOneOrderCompleted` | Present; asserts per step the status, the request the stand-in observed (line count, money value, order reference), `exactly one` `order.confirmed.v1` and `order.completed.v1` in the outbox, and the R23 absence as a `SagaCommands` count of exactly 4 |
| R21, R23 | `SagaStepTableTests.R21_...`, `.R23_...` | Present; R21 asserts the intermediate `credit_approved` edge as well as the single event (armed by me — it fails when `Confirm` alone is kept) |
| R25 | `SagaPreconditionTests.R25_EachOfTheTenConsumedFacts_RedeliveredAfterCompletion_IsIgnoredAndRecordedWithObservedAndExpectedStatus` | Present; sweeps all ten facts, asserts both `ObservedStatus` and `ExpectedStatus` per row and that the status never moves |
| R26 | `SagaCompensationStockRejectedTests.R26_CancelsWithReasonStockRejectedAndIssuesNoStockReleaseCommand` | Present; armed by me indirectly via B4 row 1 / K6 |
| R27, R28 | `SagaCompensationCreditRejectedTests.R27_R28_SO6_SO7_...` | Present |
| R29 (retry clause) | `SagaCommandRetryTests.R29_SO4_SO5_WithNoResponder_ParksAfterExhaustedAttemptsLeavingTheOrderStatusUnchanged` | Present; asserts `parked`, `attempts = 3`, non-null error and next-attempt, status still `placed` |
| R24 (API half), R29 (dead-letter row) | `TODO` | Correctly left `TODO`; both deferrals are the two the gate designed as partial, each citing its inherited ruling |

Local rows: SO1, SO2, SO3 (both halves), SO4, SO5, SO6, SO7, SO8, SO10, SO11 — all cited, all present, all substantive. **SO9 — present but incomplete, see D1.**

---

## 4. Defects

### D1 — BLOCKING. `SO9`'s committed-offset clause is unguarded: `EnableAutoCommit` can be reverted and the whole suite stays green

**Files:** `src/Orders/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs:115`; `tests/Orders.IntegrationTests/SagaConsumptionTests.cs:106-169` (specifically the assertions at lines 157, 161-162); `specs/order_saga_orchestrator/tasks.md` F5 (line 65), ticked `[x]`.

**What I did.** I changed line 115 from `EnableAutoCommit = true` to `EnableAutoCommit = false` — the setting `design.md` §3.3 lists as **step 4 of the five-step SO9 construction** — rebuilt, and ran `SagaConsumptionTests`. Result: `Passed! - Failed: 0, Passed: 2` — both `SO1_FirstBoot_...` and `SO9_AHandlerThatThrows_LeavesTheCommittedOffsetUnchangedAndTheFactIsRedelivered` stayed green while the consumer group's committed offset never advanced at all. `grep -rn EnableAutoCommit` across `src/` and `tests/` returns exactly one hit — that line. Nothing else in the repository observes it.

**Why it matters, concretely.** SO9 is a two-sided requirement: the offset advances **only after** a successful handler, which asserts both that it does not advance early **and** that it does advance. The landed test proves the first half by observing a redelivery. It cannot prove the second, because with `EnableAutoCommit = false` the redelivery still happens — the re-subscribe finds no committed offset and `AutoOffsetReset.Earliest` replays from the beginning of the topic, which looks identical to the test. `design.md` §3.3 calls this "the one behaviour in the feature whose failure mode is silent data loss, so it is proven twice". It is proven once and a half. The consequence of the regression is not corrupted data — the `processed_events` ledger absorbs the replay — but every restart re-reads every fact on three topics forever, silently, which is precisely the "slow, correct, silent" failure shape the row-8 gate ruling says this repository keeps paying for.

**This was foreseen and the instruction was not followed.** `tasks.md` F5 reads: *"assert the second delivery happened **and** that the committed offset only advanced after success (**read the group's committed offset from the broker; do not infer it from the redelivery alone**)."* The landed test infers it from the redelivery alone (`gate.Attempts >= 2`). The task is ticked `[x]`, and `progress/impl_order_saga_orchestrator.md` §3 closes with *"No other deviation"*, so the shortfall is undisclosed. That combination — an explicitly-specified assertion, skipped, ticked, and not recorded — is the same defect class that cost feature 15 three rounds, on this feature's own most dangerous line.

**Note on F6.** F6's two arming rows are genuine and I do not dispute them: flipping `EnableAutoOffsetStore` and moving `StoreOffset` above the `await` both fail the test, as recorded. F6 passing does **not** discharge F5 — those two mutations break the "not before" half, which is the half the test covers.

**To clear D1:** extend `SO9_AHandlerThatThrows_...` to read the `orders.saga` group's committed offset from the broker (`IConsumer.Committed(...)` on a throwaway consumer, or `AdminClient.ListConsumerGroupOffsetsAsync`) and assert it is unchanged across the failed delivery and advanced past that offset after the successful one; then arm `EnableAutoCommit = false` (and, while there, deleting `consumer.Close()` from the `finally`) and record the verbatim failure in `progress/impl_order_saga_orchestrator.md` §5. If either mutation leaves the test green, the assertion is still not reaching the property.

### D2 — REQUIRED. `tasks.md` M5 is unticked

**File:** `specs/order_saga_orchestrator/tasks.md:136`.

M5 ("set `in_review` and stop") was performed — the feature was in `in_review` when this review began — but the box is `[ ]`. `CHECKPOINTS.md` C6 requires every task of a `done` sdd feature to be ticked, so the box cannot be closed honestly while it is empty. Tick it in the fix pass, together with whatever F5's repair adds.

---

## 5. Advisories (non-blocking, none needs a further review round on its own)

**A1 — the `EfCoreOrderNumberAllocator` self-seed race is a real defect in closed feature 15 and needs a home outside a test comment.** I read the SQL: `AllocateNextAsync` runs `IF NOT EXISTS (SELECT 1 ...) INSERT ...` with no lock hint and no duplicate-key handling, so two genuinely concurrent **first-ever** callers can both evaluate "not exists" and the loser dies on the `PK_order_number_sequences` violation. The finding is correctly attributed (feature 15, not this feature), correctly left unfixed per `tasks.md` I6, and recorded in the test's own XML doc and in `impl_...md` §3.5. It is **not** in `feature_list.json`, and a finding that lives only in a passing test's comment is one refactor away from evaporating. Recommendation for the leader: add it to the backlog as a named, `sdd: false` item (the fix is one statement — `WITH (UPDLOCK, HOLDLOCK)` on the existence probe, or catching 2601/2627 and re-reading, the shape `ProcessedEventLedger` and `EfCoreSagaCommandStore` already use), and carry it into the history entry when this feature closes. Practical exposure is narrow — `order_number_sequences` ships empty and the seed writes no row, so only concurrent placements against a never-allocated deployment can hit it — but "narrow" is not "recorded".

**A2 — the landed race test is reliable enough to keep, and its polarity should be understood.** `AllocateNextAsync_ConcurrentFirstEverAllocations_CanRaceTheSelfSeedInsertAndFail` asserts that a race **reproduces**, so it goes red both when the allocator is fixed and, in principle, when the machine serialises all sixteen callers. I ran it 4× idle and 2× with all 16 cores saturated: **6/6 green**, 3-4 s idle, 15-22 s loaded. The two-phase design (open every connection under a 30 s `CancellationTokenSource`, then one ungated `Task.WhenAll`) does remove the failure mode that made the first shape flaky, and the `Barrier` deadlock was correctly diagnosed and correctly abandoned. The test's own comment already tells a future reader not to trust a green run as proof the race is gone. Keep it — but when A1 is fixed, this test must be **inverted**, not deleted, or the fix lands unguarded.

**A3 — a well-formed envelope with an unparseable payload retries forever.** `SagaFactsConsumer.HandleMessageAsync:123` throws on a null/undeserialisable payload; the exception escapes to `ExecuteAsync`, which logs and re-enters after 2 s, and the offset is never stored — so a single poisoned payload blocks that partition indefinitely at one log line every two seconds. This is inside feature 27's deferred `R16` scope (`requirements.md` §1.2) and the three routing outcomes design.md §3.5 enumerates do not cover it. No action now; worth naming in feature 27's spec so it is not rediscovered.

**A4 — two small design-vs-code divergences, both benign and both disclosed in code but not in §3.** (i) `design.md` §6.3's claim SQL is `WHERE id = @id`; `TryClaimAsync` claims by `(order_id, command)` instead, which is the only identity a channel signal carries — stated in `ISagaCommandDispatcher`'s own doc-comment and a direct consequence of the deviation-3 split, so it is covered in substance. (ii) `ISagaCommands` and `SagaCommandRequestFactory` sit in `Application/` and reference `Infrastructure.Messaging.Rpc` types; `design.md` §6.1 chose that explicitly ("referenced directly here rather than duplicated behind a second, port-local DTO shape") and it passed the gate, so it is not a deviation — but it is the **first** inward-pointing-arrow exception in this repository and no architecture rule watches it. If a later feature wants the layering enforced, the rule belongs in `Architecture.Tests`, not in review prose.

---

## 6. What was right, in detail — so the fix pass is scoped to D1 and D2 only

- **The two gate-approved architecture rules are real guards.** I introduced genuine violations of both and both failed, naming my types. I also reproduced §4's finding independently: with `"Confluent.Kafka.ProducerBuilder"` (no arity suffix) and a live `ProducerBuilder<string, byte[]>` under `Application/`, the rule stays **green**. `NetArchTest` 1.3.2 matches the CLR metadata name exactly, the report's diagnosis is correct, the repair changes only how the four approved names are spelled, and the repository ends this feature with one more armed rule than it started with. Handling this as a repair-and-record rather than a silent edit is the right call and it is documented in both files' `<remarks>`.
- **Deviation 1 (singleton `IFactStreamSubscriber`) is sound on the evidence, not just the prose.** `KafkaFactStreamSubscriber`'s only constructor dependency is `IOptions<OrdersSagaOptions>`, itself registered singleton; the `IConsumer` is built inside `ConsumeAsync` under a `using` and dies with the call. Nothing scoped is captured directly or transitively. This is not the defect class that cost the dispatcher feature a round.
- **Deviation 3 is a genuine bug found live, and the fix is guarded.** I reverted the sweeper to the single claim-then-issue method and the SO3 crash-window integration test went red with the exact assertion and message the report records. The reasoning is right too: `ClaimDueAsync` stamps a 60 s lease, so a second `TryClaimAsync` on the same row necessarily finds it held and no-ops — a silent no-op on the only durability guarantee the feature has.
- **Group K holds.** Three of the eight rows re-armed independently, all three fired, all three messages match the report verbatim.
- **`SO3_CommitBeforeIssue_WhenEnqueueFailsInsideTheTransactionTheAggregateChangeRollsBackToo` is exactly what L7 row 2 asked for** — the implementer found that no existing test distinguished in-transaction from post-commit enqueue, and added a permanent regression test rather than a throwaway probe. That is the right response to an arming row that finds nothing.
- **M3 is true.** I queried the running `otcnet-mssql` directly: three `parked` `stock.reserve` rows for `ORD-000007/8/9`, `attempts = 3`, the transport-failure error, all three orders still `placed`, zero ignored-fact rows. Exactly design.md §8.2's predicted steady state.
- **`specs/shared/` was not forked.** Six of seven files are byte-identical to #7's; the seventh differs in the Status column and the two derived tallies, whose arithmetic I checked.

---

## 7. Benchmark note, for the history entry when this closes

#7 closed this feature approved on the first pass: 1 spec session, ~1 h 45 min implementation, ~35 min review. #8 ran ~2.5 h of implementation plus a resumed pass, and is now taking a second review round. **The difference is not the saga.** The step table, the fourteen rows, the compensation mapping and the redelivery argument were transcribed from #7 and cost #8 almost nothing — #8's spec pass closed **20 of 22** open points from #7's own gate record rather than re-deriving them, and that saving is banked in the spec phase, not here.

The extra implementation time went into the four places .NET genuinely differs, and each produced work #7 never had to do: the offset contract had to be **constructed** rather than verified (`Confluent.Kafka`'s defaults are at-most-once, where kafkajs's are not); the `@Saga` hop had to be **built** (channel + `BackgroundService`) where #7 got it free from an RxJS subscription; `SKIP LOCKED` does not exist in MS-SQL, so SO11's lease is a written requirement with three arming rows where #7 had one MySQL clause; and #8's own feature-14 confinement rule collided with the first fact-stream consumer in the repository, which is a #8 artefact with no #7 counterpart. Two of those four turned up defects that #7 could not have had — the vacuous confinement rule and the sweeper's double-claim no-op — and both were found by **arming**, not by review.

And the second review round is the same story once more. #7's review of this feature was approved first time with its live-system probing as its strength; #8's is being rejected for a missing broker-side assertion that #7's stack made unnecessary, because kafkajs commits on the framework's terms and `Confluent.Kafka` commits on yours. **The honest line for the history entry is: the reuse made the hard part cheap and the stack-specific part is where every hour and every defect went.** That is the measurement, and it is more interesting than a green tick.

---

## 8. What must change before re-review

1. **D1** — extend `SagaConsumptionTests.SO9_AHandlerThatThrows_...` to read the `orders.saga` group's committed offset **from the broker**, asserting it is unchanged across the failed delivery and advanced past that offset after the successful one. Then arm `EnableAutoCommit = false` (and, in the same pass, deleting `consumer.Close()` from the `finally`), confirm the test FAILS on each, restore with a forced rebuild, and record both rows verbatim in `progress/impl_order_saga_orchestrator.md` §5.
2. **D2** — tick `tasks.md` M5.
3. **Correct the report** — `impl_order_saga_orchestrator.md` §3's "No other deviation" is not accurate while F5's stated content is unmet; add the F5 shortfall (now repaired) as a recorded deviation, or state that it was repaired rather than deviated from.
4. **Nothing else.** Do not re-touch any other file, any other test, or any other arming row. Re-review will re-run the two arming rows above and the confinement suite, and will not re-litigate anything in §6.

Advisories A1-A4 need no code change in this feature. A1 is owed a backlog entry by the leader before this feature's history entry is written.

---

# Review — round 2 (second pass)

> **This section is additive. Nothing above it is amended, withdrawn or reopened — round 1's findings stand as written, including D1's phrasing, which is corrected factually in §2.4 below rather than edited in place.**

**Verdict: REJECTED (second pass).** **D1 is genuinely fixed** and **D2 is done**; both were re-armed and reproduced independently. The rejection is for a **different, newly-established defect (D3)**: the `R25_...` failure that round 1's fix report attributed to memory pressure is **a real race in the test**, not an environmental artefact, and I reproduced it **deterministically**. `feature_list.json` id 16 set back to `in_progress`; no `progress/history.md` entry written (effort records are appended on approval only).

**Scope of the fix required: one wait, in one test method.** `SagaHappyPathTests` line 79 already does the correct thing; `SagaPreconditionTests.DriveToCompletedAsync` does not. Nothing else in the feature is in question.

---

## 2.1 What I ran this round, and what I took on trust

Taken on trust (leader-confirmed before this pass, not re-run by me): `./quality.sh` exit 0, `./init.sh` exit 0, the solution-wide test counts in §12, and `feature_list.json` id 16 = `in_review`.

Run by me, independently, each with a copy backup, a forced rebuild after restore, a `cmp` against the backup and a confirming green:

| # | Probe | Result |
|---|---|---|
| P1 | `EnableAutoCommit = true` ⇒ `false` (`KafkaFactStreamSubscriber.cs:115`), run `SagaConsumptionTests` | **FAILS at `SagaConsumptionTests.cs:219`** — `the 'orders.saga' group's committed offset on 'otc.orders.facts.v1' never advanced past 0 after the successful redelivery (last observed 0) — SO9's 'only after success' half is unproven.` Verbatim match to §12 row 1. **D1 is closed.** |
| P2 | `consumer.StoreOffset(consumeResult)` moved **before** `await handler(...)` (§12 row 3's mutation), run `SagaConsumptionTests` | **FAILS at `SagaConsumptionTests.cs:190`** — `the redelivery never reached the decorated store a second time within the wait budget.` Verbatim match. Note **which line fires** — see §2.4. |
| P3 | `SagaIntegrationTestSupport.StartHostAsync`'s `Relay.PollIntervalMs = 200` ⇒ `6_000`, run `SagaPreconditionTests` | **`R25_...` FAILS at `SagaPreconditionTests.cs:45` with `Assert.Equal() Failure: Values differ / Expected: 0 / Actual: 1`, test duration 10 s** — assertion, line, values and duration all identical to the "flaky" run recorded in §12. **This is D3.** |
| P4 | Confirming green after all three restores | `SagaConsumptionTests` + `SagaPreconditionTests` **4/4, exit 0** |
| P5 | `specs/order_saga_orchestrator/tasks.md` — count of unticked boxes | **zero**; 73 ticked, M5 among them. **D2 is closed.** |
| P6 | `git diff --stat src/ tests/` at end of pass vs. start | unchanged; both files I mutated are byte-identical to their backups |

---

## 2.2 D1 — fixed, and the fix is load-bearing

The rewritten `SO9_AHandlerThatThrows_...` reads the `orders.saga` group's committed offset from the broker at three points, via `IConsumer.Committed` on a throwaway consumer (`SagaIntegrationTestSupport.ReadCommittedOffsetsAsync`, `SagaIntegrationTestSupport.cs:204`). P1 shows the "does advance" assertion at line 219 is real: the exact mutation that round 1 flipped and found the old test green now turns it red, with a message that names the requirement it protects. The helper is proven live in both directions — it reports a real advance on the green path and reports "0, never advanced" on the armed path — so it is not a constant.

**The deterministic gate genuinely removes the wall-clock dependency it was built to remove.** `ThrowOnceGate.BeforeEnqueueAsync` (`SagaConsumptionTests.cs:262`) increments the attempt counter and then blocks on a `TaskCompletionSource` **before** delegating to the inner store, so at the moment the "not before" read happens (line 204) the redelivered fact provably has not reached the store, cannot have returned from the handler, and therefore cannot have had its offset stored. That is an ordering fact, not a duration guess. The remaining `Task.Delay(100)` polls at lines 173-188 are bounded waits for an event to occur, which is a different thing from assuming an event has *not yet* occurred — the failure mode that killed the 7-second sleep. The self-catch disclosed in §12 was real, was found the right way, and the replacement is the right replacement.

**Correction of round 1's D1 phrasing, for the record.** `EnableAutoCommit = true` is the **correct** value at `KafkaFactStreamSubscriber.cs:115`, and `design.md` §3.3 specifies `true`: the class disables `EnableAutoOffsetStore` and stores offsets by hand after the handler returns, so the background committer commits only what the class chose to store, and nothing calls `Commit()`. Round 1 flipped it to `false` **as a mutation**, and its write-up can be read as though `false` were the specified value. It is not. The reasoning and the defect were right; the phrasing was not, and the fix round correctly left the line alone.

## 2.3 D2 — fixed

`tasks.md:136` M5 is `[x]`; zero unticked boxes remain in the file. C6's "every task ticked" is satisfiable now.

## 2.4 An honest limit on what the SO9 rewrite proves (no action required)

P2 answers the question the brief asked — can *both* asserted halves fail, or only the one the mutation happens to hit? The answer: **the "not before" half, as read from the broker at line 205, cannot be made to fail by any mutation of the subscriber, and this is a property of the design rather than a defect.** Every early-commit fault suppresses the redelivery entirely (the offset is stored, `consumer.Close()` in the `finally` commits it, the re-subscribed consumer resumes past the fact), so `gate.Attempts` never reaches 2 and the run dies at line 190 first — which is exactly what P2 observed, at line 190, not line 205.

So SO9 ends up guarded on both halves, but by **two different assertions**: "does not advance before success" by the redelivery observation at line 190 (armed, fires), and "does advance after success" by the broker read at line 219 (armed, fires). Line 205's broker read is a defensive cross-check that no available mutation can trip. That is worth stating plainly rather than leaving a future reader to infer that all three reads are load-bearing — but it is not a defect, it needs no change, and it does not weaken the traceability claim for SO9.

---

## 2.5 D3 — BLOCKING (new). `SagaPreconditionTests.R25_...` races the outbox relay and can fail on correct code

**Files:** `tests/Orders.IntegrationTests/SagaPreconditionTests.cs:45` (the assertion), `:89-92` (`DriveToCompletedAsync`, the missing synchronisation); contrast `tests/Orders.IntegrationTests/SagaHappyPathTests.cs:79`, which has it.

**The mechanism.** `order.placed.v1` reaches Kafka only via the **outbox relay**, on its own poll interval. `DriveToCompletedAsync` publishes `stock.reserved.v1` **directly to Kafka** the moment `PlaceOrderAsync` returns, without waiting for `order.placed.v1` to be consumed. The two facts are on different topics, so nothing orders them. `SagaStepTable` gives **both** the precondition `OrderStatus.Placed` (`SagaStepTable.cs:66` and `:71`), and `stock.reserved.v1` advances the order to `stock_reserved`. If the directly-published fact wins, the order legitimately moves on, and the late `order.placed.v1` is then — correctly, per R25 — recorded `precondition_unmet`. `SagaPreconditionTests.cs:45` asserts `Assert.Equal(0, ...SagaIgnoredFacts.CountAsync(f => f.CorrelationId == orderId))` and sees **1**.

**The demonstration (P3).** Widening the relay's poll interval from 200 ms to 6 s in `SagaIntegrationTestSupport.StartHostAsync` — a change to *test timing only*, no production code touched — reproduces the failure **on the first run**: same test, same line 45, same `Expected: 0 / Actual: 1`, same 10 s duration as the run recorded in §12. Restored and green immediately afterwards (P4).

**Why the "memory pressure" attribution has to be withdrawn.** Load does not create this failure; it only widens the window that the missing wait leaves open. The count matches exactly (one unsynchronised fact, one ignored row), the assertion is the first one after the drive, and the identical failure is producible on an idle machine by delaying the relay. §12 records the failure honestly and then declines to investigate it under round 1's instruction 4 ("nothing else") — that instruction was about not re-touching *other* files, and a whole-suite red in this feature's own named requirement test is inside the feature's scope, not outside it. Round 1's §1 explicitly took "492 passed / 0 failed" on trust; a run of this feature's suite that is not green cannot be closed by attribution.

**Why it matters beyond a retry.** `R25_...` is the single named test carrying `R25` in `specs/shared/test-matrix.md` and `SO8`'s sibling. A test that can go red on correct code teaches the next person to re-run rather than to read — and this repository's own standard, stated in §12 two paragraphs above the attribution, is that "a test that fails on correct code is exactly the kind of thing this feature's own standing rule exists to catch **before it reaches a reviewer**". The same standard applies to the test that was left in.

**To clear D3:** in `DriveToCompletedAsync`, wait for `order.placed.v1` to have been consumed before publishing `stock.reserved.v1` — the established pattern is `await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, id, "stock.reserve", "sent", _wait);`, exactly as `SagaHappyPathTests.cs:79` does it. Then **arm it**: re-run `SagaPreconditionTests` with `Relay.PollIntervalMs` widened to 6 s (restore afterwards) and record in `progress/impl_order_saga_orchestrator.md` that `R25_...` now passes under a lag that previously reproduced the failure, together with the confirming green at the normal 200 ms. Correct §12's attribution paragraph in the same pass — do not delete it; supersede it, so the record shows what was believed and what was established.

---

## 2.6 Advisories added this round (no code change demanded)

**A5 — SO9's baseline read is still exposed to the shared consumer group, by analysis rather than by demonstration.** The `orders.saga` group and the fact topics are shared, sequentially, by every test in `SagaCollection`, and a test whose host stops while facts are still unconsumed leaves an uncommitted backlog for the next test's consumer. `SagaConsumptionTests.cs:163` reads the baseline shortly after `host.StartAsync()`; if a backlog is drained just after that read, `auto.commit.interval.ms` (5 s, library default, not overridden) can commit those offsets between the baseline read and the "not before" read at line 204, advancing the total and failing line 205 **on correct code**. I did not reproduce this — it has not fired in two full-suite runs plus my four — so it is recorded as reasoning, not as a finding. Cheap mitigation if it ever fires: require the committed total to be stable across two reads more than one auto-commit interval apart before accepting it as the baseline. Not required for approval.

**A6 — the same unsynchronised shape as D3 exists in `SagaCompensationCreditRejectedTests.cs:50-54` and its stock-rejected sibling**, which publish `stock.reserved.v1` immediately after `PlaceOrderAsync`. Neither asserts an ignored-fact count, so neither is currently exposed; both would become exposed the moment such an assertion is added. Worth fixing with D3 if it costs nothing, not worth a round on its own.

**A2-A4 — judgements in §12 are adequate.** A2 (invert, do not delete, when the allocator is fixed) is now durably captured in the acceptance criteria of `feature_list.json` id 45, so it no longer lives only in a test comment. A3's deferral is correct and correctly addressed: `feature_list.json` id 27 `observability_reliability` (phase 14) explicitly owns "retry + DLQ", the poisoned-payload path is a dead-letter concern, and half-implementing a dead-letter route inside a saga feature would be worse than deferring it — the seam is named (`SagaFactsConsumer.cs:123`) so feature 27 does not have to rediscover it. A4's two divergences are now disclosed in writing with their arguments; recording them in §12 rather than retro-editing §3 is the right call.

**Note on scope (the brief's probe 4).** The implementer set `feature_list.json` to `in_review` because **`tasks.md` M5 instructs it to** — M5 is part of the gate-approved spec and reads "Set `order_saga_orchestrator` to `in_review` in `feature_list.json` and **stop**". The brief forbidding backlog edits and the approved spec requiring one are in conflict; the implementer followed the spec, the resulting state is correct, and no write race occurred. This is a harness inconsistency to resolve (either M5 or the standing brief should change), **not** a finding against the implementer, and it warrants nothing beyond this note.

---

## 2.7 CHECKPOINTS — boxes re-walked this round

Only boxes whose status could have changed are re-walked; the rest stand as marked in §2 above.

- [x] **C2** — at most one feature `in_progress` (set by this pass); every status valid; id 45 added by the leader is well-formed.
- [x] **C3** — unchanged by the fix round: no production source was modified (`git diff --stat src/` identical before and after my probes), so the architecture walk of §2 stands. `Architecture.Tests` not re-run — no claim of mine this round depends on it.
- [ ] **C4 — verification is real.** **Fails on D3**: this feature's own integration suite contains a test that can go red on correct code, demonstrated. `./quality.sh` exit 0 is taken on trust and is not in dispute; the defect is that a green run and a red run of the same code are both reachable.
- [ ] **C5** — `progress/history.md` entry with effort record: still owed, still correctly unwritten on a rejection.
- [x] **C6 — every task ticked**: now true (P5). **C6 — every `R<n>` covered by a concrete named test**: SO9's shortfall is closed (P1); `R25`'s named test exists and is substantive, but is not reliable, which is D3.
- [x] **C7** — `specs/shared/` untouched by the fix round; the round-1 `cmp` result stands. Effort records still owed.

---

## 2.8 Benchmark, second-round addendum (carry into `progress/history.md` at approval)

Round 1's §7 stands unamended; this is what the extra rounds cost and bought.

**What round 2 cost:** four container-backed runs and one analysis pass — under fifteen minutes of machine time, plus the fix round's own implementation session. **What it bought:** two things #7 never had to buy. First, a genuinely armed offset contract: `Confluent.Kafka` hands you the commit decision, so #8 had to *construct* SO9's guarantee and then prove the proof — and the proof needed a second attempt, because the first rewrite failed on correct code. Second — and this is the one worth recording — **the round-1 rejection is what surfaced D3.** A first-pass approval would have shipped the R25 race with "passed on the retry" as its epitaph, and the next session to see it red would have re-run rather than read.

That is the shape of the reuse story at this feature. The saga itself — fourteen rows, the compensation mapping, the redelivery table — was transcribed and cost almost nothing. Every hour and every one of the four defects found across both rounds (the vacuous confinement rule, the sweeper's double-claim no-op, SO9's unguarded half, and now R25's unsynchronised drive) came from the four places .NET, `Confluent.Kafka`, MS-SQL and #8's own confinement rules differ from #7's stack. Three of the four are **test-infrastructure** defects, not domain defects — which is itself the measurement: reuse moves the cost from *deciding what to build* to *proving the stack does what the other stack did for free*.

---

## 2.9 What must change before re-review

1. **D3** — add the missing wait to `SagaPreconditionTests.DriveToCompletedAsync` (`SagaHappyPathTests.cs:79`'s pattern), arm it by re-running `SagaPreconditionTests` with `Relay.PollIntervalMs` widened to 6 s, record the before/after verbatim in `progress/impl_order_saga_orchestrator.md`, and restore the interval.
2. **Supersede §12's attribution paragraph** — state that the `R25` failure was established as a test-side race, not memory pressure. Do not delete the original text.
3. **Optional, same pass, zero-risk:** A6's two compensation tests, for the same reason.
4. **Nothing else.** Do not touch `KafkaFactStreamSubscriber.cs`, `SagaConsumptionTests.cs`, `SagaIntegrationTestSupport.cs`, `tasks.md` or `feature_list.json`. Re-review will re-run P1, P3 and the confirming green, and will not re-litigate anything above.

---

# Review — round 3 (third pass)

> **This section is additive. Nothing above it is amended, withdrawn or reopened — rounds 1 and 2 stand exactly as written, including D3's diagnosis, which round 3 confirms rather than revises.**

**Verdict: REJECTED (third pass).** **D3's fix is correct**, and I armed it myself this round with the discriminator the fix round did not produce — that work is recorded below so round 4 does not repeat it. **A6 is genuinely discharged** and **A5 is correctly left as analysis**. The rejection is for **D4**: a *second*, still-present race in the very same test, which I reproduced verbatim on the D3-fixed code at the normal 200 ms relay interval. It is the failure the fix round's own arming table row 1 attributed to D3 — and it is not D3.

**Scope of the fix required: one wait helper call, in one loop, in one test method.** No production source is in question, and nothing in rounds 1 or 2 is reopened.

---

## 3.1 What I ran this round, and what I took on trust

Taken on trust (leader-confirmed before this pass, not re-run by me): `./init.sh` exit 0, `./quality.sh` exit 0, `Orders.IntegrationTests` 63/63 twice, and `feature_list.json` id 16 = `in_review`. Per `CLAUDE.md`'s reviewer guidance I re-ran no full suite; every run below is a targeted probe of a claim.

Run by me, independently. Both mutated files were backed up by copy first, restored from that backup, `md5sum`-verified against it, `touch`ed, rebuilt with `dotnet build --no-incremental`, and confirmed green.

| # | Probe | Runs | Result |
|---|---|---|---|
| Q1 | **D3's fix reverted** (the `WaitForSagaCommandCountAsync` at `SagaPreconditionTests.cs:99` commented out) + `Relay.PollIntervalMs = 6_000` — round 2's own P3 vehicle | 3 | **1 FAIL / 2 PASS.** The failure is round 2's exact signature: `Assert.Equal() Failure: Values differ` at `SagaPreconditionTests.cs:line 45`, 27 s |
| Q2 | **D3's fix in place** + the same `Relay.PollIntervalMs = 6_000` | 6 | **6/6 PASS**, 15–16 s each |
| Q3 | **D3's fix reverted** + `Relay.PollIntervalMs = 12_000` | 3 | **3/3 PASS**, 15 s each — the reproduction window has an *upper* edge, see §3.3 |
| Q4 | **D3's fix in place, `Relay.PollIntervalMs = 200` (shipped values)**, with only the incidental `db.Orders.SingleAsync` + status assert at lines 70–71 removed from between the loop's wait and its `FirstAsync` | 4 | **2 FAIL / 2 PASS.** `System.InvalidOperationException : Sequence contains no elements.` at `SagaPreconditionTests.cs:line 73` — **byte-identical to the fix round's run-11 failure. This is D4.** |
| Q5 | `SagaCompensationStockRejectedTests.cs` read for the unsynchronised shape (A6), not inferred from mtime | — | **Genuinely correct**: line 41 is `WaitForSagaCommandCountAsync(..., "stock.reserve", "sent", _wait)`, before the first fact publish at line 44 |
| Q6 | Restore both files, forced rebuild, confirming green | 1 | `md5sum` identical to backup (`7c71c079…`, `0e4c472f…`); `SagaPreconditionTests` **2/2, exit 0** |

`git status` for `src/` and `tests/` is exactly what it was when this round began; no production file was touched at any point.

---

## 3.2 D3 — the fix is correct, and this is the arming it deserved

**The fix itself is not probabilistic, and that is the answer to the standards question.** `DriveToCompletedAsync`'s added `await WaitForSagaCommandCountAsync(..., "stock.reserve", "sent", _wait)` is a **state gate, not a duration**: a `saga_commands` row for `stock.reserve` in status `sent` exists *only because* the saga consumed `order.placed.v1`. When that call returns non-zero, `order.placed.v1` has provably been consumed while the order was still `placed`, so it can never later contend for the `Placed` precondition against a fact that has already moved the order past it. The stray `precondition_unmet` row D3 names is therefore excluded **by construction**, not by winning a race. That is exactly the technique D1's repair used for SO9 — an observable fact rather than a wall-clock guess — and the leader's question 3 resolves in the fix's favour: the deterministic gate *was* available here, and it *is* what was used. What was probabilistic was only the demonstration that the unfixed shape is broken.

**My discriminator, replacing the fix round's weak one.** Q1/Q2 hold every variable fixed except the presence of the wait, at round 2's own 6 s interval: **unfixed 1 failure in 3, fixed 0 failures in 6**, with the failure being D3's own line-45 assertion rather than a different one. Taken with round 2's P3 (unfixed at 6 s, failed first try), the unfixed shape has now failed **2 of 4** reviewer-run attempts at 6 s while the fixed shape has failed **0 of 6** at the same value. That is a materially stronger separation than 11/12 against 15/15 at a tuned interval, and it is on the symptom D3 actually names.

**I therefore consider D3 closed.** Round 4 should not re-arm it; the evidence is in Q1/Q2 above.

---

## 3.3 Why 6 s reproduced for me and not for the implementer — and why no single knob can arm this deterministically

Q3 is the piece that was missing from both previous accounts, and it dissolves the disagreement rather than attributing it to machine speed.

The stray row only fails `SagaPreconditionTests.cs:45` inside a **two-sided window**: the relayed `order.placed.v1` must be consumed **after** the directly-published `stock.reserved.v1` has moved the order past `Placed` (otherwise it is consumed legitimately and no row is written at all) **and before** line 45 executes (otherwise the row lands harmlessly during the redelivery loop). So the failure probability is **not monotonic in the relay interval** — it rises, peaks near the time line 45 is reached, and falls back to zero beyond it. Q3 proves the upper edge directly: at 12 s the unfixed shape passes **3/3**, because `order.placed.v1` has not even been produced yet when line 45 runs. The fix round's own 1.5 s result sits on the *lower* edge, where `order.placed.v1` usually still arrives while the order is legitimately `placed`.

Two consequences worth recording, because both are transferable:

- **The implementer's "6 s did not reproduce on this machine" and my "6 s reproduced first try" are both true and are the same phenomenon** — 6 s sits near the peak here and the peak is a few seconds wide, so a machine whose drive reaches line 45 a little sooner or later moves off it. Neither report was wrong; the *explanation* offered ("this machine is faster") was directionally right but stopped one step short of the mechanism, which is why it produced a mis-tuned arming rather than a discriminating one.
- **Answering the leader's question 1 directly: a 1-in-12 reproduction is not, by itself, an armed guard by this repository's standard** — the arming protocol asks for a mutation that makes a named test fail, and "sometimes" is not "fails". But the honest conclusion here is not that the implementer was lazy: **for a bounded-window three-way race, no value of one knob makes the unfixed shape fail deterministically**, as Q1 and Q3 together show. The right standard in that case is the one D1's repair already demonstrated and the one the fix satisfies anyway — *make the fix a state gate and argue its guarantee structurally, then use the probabilistic run as confirmation only*. The report has the gate; what it lacks is the argument, and it substituted a tuned flake for it. That substitution is the reportable failing, not the fix.

---

## 3.4 The leader's question 2 — the two failures are **not** the same race, and this is D4

Round 2's failure was `Expected: 0, Actual: 1` at line 45. The fix round's run-11 failure was `System.InvalidOperationException : Sequence contains no elements.` at line 73. §13 asserts these are "a second, distinct manifestation of the same D3 root cause". **They are not, and the assertion is refutable by reading the helper it depends on.**

`SagaIntegrationTestSupport.WaitForSagaIgnoredFactCountAsync` (`SagaIntegrationTestSupport.cs:157-174`) returns **as soon as `count > 0`** for `(correlationId, marker)`. It takes no expected count and no event type. The R25 loop calls it at line 67 with the marker `precondition_unmet`, then at line 73 requires a row **for this iteration's own `eventType`**.

- **Iteration 1** is genuinely gated: no `precondition_unmet` row exists yet, so the wait blocks until one appears. A stray `order.placed.v1` row from D3 could satisfy it early — but iteration 1's event type *is* `order.placed.v1`, so line 73's filtered `FirstAsync` would find that very row and return it. Iteration 1 therefore **cannot** throw `Sequence contains no elements`, with or without D3.
- **Iterations 2–10** are not gated at all: iteration 1's own row already makes `count > 0`, so line 67 returns immediately, every time, on every run. The only thing standing between the publish at line 66 and the filtered `FirstAsync` at line 73 is the **incidental latency of an unrelated query** — the `CreateDbContext` + `Orders.SingleAsync` at lines 69–70. D3's stray row is irrelevant to this: the gate is already satisfied without it.

So run 11's failure was iteration ≥ 2, and D3's stray row is not what caused it.

**Q4 demonstrates this rather than arguing it.** On the **D3-fixed** test, at the **shipped** `Relay.PollIntervalMs = 200`, I removed only lines 70–71 — an assertion about the order's status, wholly unrelated to the ignored-fact row — and the test failed **2 of 4 runs** with the fix round's run-11 message, verbatim:

```
  Failed OrderToCash.Orders.IntegrationTests.SagaPreconditionTests.R25_EachOfTheTenConsumedFacts_RedeliveredAfterCompletion_IsIgnoredAndRecordedWithObservedAndExpectedStatus [22 s]
  Error Message:
   System.InvalidOperationException : Sequence contains no elements.
  Stack Trace:
     at Microsoft.EntityFrameworkCore.Query.ShapedQueryCompilingExpressionVisitor.SingleAsync[TSource](IAsyncEnumerable`1 asyncEnumerable, CancellationToken cancellationToken)
   at OrderToCash.Orders.IntegrationTests.SagaPreconditionTests.R25_EachOfTheTenConsumedFacts_RedeliveredAfterCompletion_IsIgnoredAndRecordedWithObservedAndExpectedStatus() in .../SagaPreconditionTests.cs:line 73
```

A test whose passing depends on an unrelated `SELECT` being slower than a Kafka round trip is unsynchronised, full stop. Removing that `SELECT` does not *create* the race; it removes the accident that was hiding it.

---

## 3.5 D4 — BLOCKING (new). `R25_...` still goes red on correct code, for a second and independent reason

**Files:** `tests/Orders.IntegrationTests/SagaPreconditionTests.cs:67` (the vacuous wait), `:73` (the assertion it fails to gate); `tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs:157-174` (`WaitForSagaIgnoredFactCountAsync`, which returns on `count > 0`).

**What is wrong.** From iteration 2 of the ten-fact sweep onwards, the loop's only synchronisation point is satisfied by rows written by *earlier* iterations, so it returns without waiting for anything. The assertion that follows needs a row this iteration's publish has not yet produced. Nine of the ten iterations are unguarded, on every run.

**Why it matters, and why it is the same defect class the feature was rejected for in round 2.** `R25_...` is the single named test carrying `R25` in `specs/shared/test-matrix.md` and `SO8`'s sibling. Round 2 rejected D3 on the stated ground that *"a test that can go red on correct code teaches the next person to re-run rather than to read"*. That ground applies here unchanged, and this time the red run is reachable **with the shipped relay interval and the shipped fix in place**. It has already fired once in the implementer's own record (run 11) and twice in mine.

**Why it must not be closed by attribution again.** §13 closes run 11 by assigning it to D3 and citing 15/15 as confirmation. Fifteen clean runs is exactly what a ~1-in-40 race looks like; it is the same reasoning shape as §12's "memory pressure", one level down, and §13's own "what I would do differently" paragraph describes precisely the move that would have caught it — isolate and loop *before* writing the attribution.

**To clear D4:** make the loop's wait prove what line 73 reads. Either add an expected-count parameter (`WaitForSagaIgnoredFactCountAsync(..., marker, _wait, atLeast: expectedIgnoredCount)`, which the loop already computes) or — better, because it is exact rather than ordinal — add an event-type-filtered overload and wait for a row matching `(correlationId, eventType, marker)`. Then **arm it**: with the repair in place, re-run Q4's probe (comment out lines 70–71, shipped 200 ms interval) and confirm the test now passes where it failed 2 of 4 — the incidental query must stop being load-bearing. Record the before/after verbatim. Check the same helper's other call sites while there: `SagaPreconditionTests.cs:145` (SO8) and `SagaCompensationStockRejectedTests.cs`'s redelivery probe each expect exactly one row, so `count > 0` is adequate at both and neither needs changing — say so explicitly rather than changing them.

---

## 3.6 D5 — REQUIRED. The fix round's arming table row 1 misattributes a D4 failure to D3

**File:** `progress/impl_order_saga_orchestrator.md` §13, the arming table and the paragraph beginning "Row 1's failure is a **second, distinct manifestation of the same D3 root cause**".

The consequence is not cosmetic: with row 1 reassigned to D4, **§13 contains no reproduction of D3's own symptom at all**. The fix round never observed `Expected: 0, Actual: 1` at line 45; it observed a different failure, attributed it to D3, and reported the pair as arming. D3's fix is nonetheless correct — §3.2 establishes that on my evidence — but the record must not claim an arming it does not have.

Correct §13 in the same pass as D4, and **supersede rather than delete**, exactly as §13 itself correctly did for §12: state that row 1's `Sequence contains no elements` failure was established in review round 3 as D4, an independent unsynchronised gate in the same loop; that D3's fix is a state gate whose guarantee is structural; and cite round 3's Q1/Q2 as D3's arming rather than re-running it.

---

## 3.7 A6 — verified by reading, not by mtime

`SagaCompensationCreditRejectedTests.cs:53-58` now carries the wait, with a comment naming the review finding. Good.

`SagaCompensationStockRejectedTests.cs` I read rather than accepting the mtime argument, and the fix round's claim is **true**: line 41 is `await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, orderId, "stock.reserve", "sent", _wait);` followed by `Assert.NotNull(observedReserve)`, and the first fact publish (`stock.rejected.v1`) does not begin until line 44. `order.placed.v1` is therefore provably consumed before that test publishes anything, and the later redelivery of `stock.rejected.v1` against the `cancelled` order is a deliberate, already-synchronised R25 probe. **A6 is discharged, and round 1's advisory text was over-broad in naming this file as a sibling with the same problem.** The implementer was right to push back on it and right to say so in writing rather than change the file.

## 3.8 A5 — content to leave it as analysis

Yes. The judgement offered ("no code change; if SO9 is ever seen to fail on correct code, this is the first place to look, and the analysis is the map") is the correct disposition for a failure mode neither of us has reproduced, and the reasoning for declining the pre-emptive double-read — added runtime defending an unobserved path — is sound. Recorded as analysis, unchanged, not blocking. Note the one asymmetry, for the record rather than for action: A5 and D4 are the same *class* of concern, and D4 is blocking only because it was reproduced. That is the right line to draw between them.

## 3.9 The superseded misattribution — is the record honest and complete?

**Honest, yes; complete, no — and D5 is why.** §13's supersession does the two hard things properly: it leaves §12's wrong text standing and marks it wrong in place, so a future reader sees what was believed as well as what was established; and its "what I would do differently" names the actual reasoning error rather than the symptom — that *"a single re-run 'in isolation' that happened to pass is not evidence of 'environmental', it is exactly what a race looks like on a lucky draw"*, and that "nothing else" in a fix-round brief bounds which *other* files get touched and was never licence to leave this feature's own named requirement test red. That paragraph is the most transferable sentence in the whole feature's record and it deserves to survive into `docs/PROCESS.md`.

What stops it being complete is that the same section then repeats the pattern it has just diagnosed — run 11 closed by attribution, 15/15 offered as confirmation, no isolation loop against the alternative hypothesis. The lesson is written correctly and applied incompletely. Fixing D5 is what makes the record match its own conclusion, and that is worth more here than the code change D4 asks for.

---

## 3.10 CHECKPOINTS — boxes re-walked this round

Only boxes whose status could have changed since §2.7 are re-walked; the rest stand as marked in §2 and §2.7.

- [x] **C2** — `feature_list.json` id 16 set back to `in_progress` by this pass; exactly one `in_progress`; every status valid.
- [x] **C3** — no production source was modified by the fix round or by my probes (`git status` for `src/` identical before and after); the round-1 architecture walk stands. `Architecture.Tests` not re-run — no claim of mine this round depends on it.
- [ ] **C4 — verification is real.** **Fails on D4**: this feature's own integration suite still contains a test that can go red on correct code, reproduced twice at the shipped configuration. D3's half of round 2's C4 objection is cleared; the box is not.
- [ ] **C5** — `progress/history.md` entry with effort record: still owed, still correctly unwritten on a rejection.
- [x] **C6 — every task ticked**: unchanged since P5, still true. **Every `R<n>` covered by a concrete named test**: SO9 closed in round 2; `R25`'s named test exists, is substantive, and is now *correct* — but not yet *reliable*, which is D4.
- [x] **C7** — `specs/shared/` untouched by this fix round; round 1's `cmp` result stands. Effort records still owed.

---

## 3.11 Benchmark, third-round addendum (for `progress/history.md` at approval — not written on a rejection)

Round 1 §7 and round 2 §2.8 stand unamended. Round 3 adds one number and one correction to the story they tell.

**The arc is now 3 implementation passes and 3 review rounds, against #7's 1 implementation session and 1 review pass approved first time.** The temptation is to read that as #8 being four times the work. It is not what happened, and the history entry should say so precisely: **the orchestrator itself — the fourteen-row step table, the compensation mapping, the redelivery semantics, the precondition matrix — was transcribed from #7 and was right on the first pass. It has not been touched by a single defect in three rounds.** What consumed three rounds was *proving the .NET stack does what #7's stack did for free*, and every one of those hours landed in test infrastructure.

**The sharpest version of the measurement, and the one to carry:** across all three review rounds, **every defect a reviewer found was in test code — D1's missing broker read, D3's unsynchronised drive, D4's vacuous gate — and none was in the shipped saga.** The only production defect in the entire feature (the sweeper's double-claim no-op) was found by the *implementer's own arming* in round 1, before review. That is not an accident of this feature; it is what reuse does to the shape of the work. When the design decisions arrive pre-made from #7, the residual risk stops being "did we model the domain right" and becomes "does our proof of the domain actually prove it" — and a proof is much easier to get subtly wrong than a transcription, because a wrong proof is green.

**The cost of the extra rounds, honestly stated.** Round 2 cost about fifteen minutes of machine time and bought D3. Round 3 cost roughly forty minutes of container-backed probing (sixteen targeted runs, no full suite) and bought three things: D3's fix confirmed with a real discriminator rather than a tuned flake; the non-monotonic reproduction window that explains why reviewer and implementer disagreed about 6 s, which retires that disagreement permanently rather than filing it under "different machines"; and D4, which was hiding *behind* D3 and would have shipped as an occasional red with "passed on the retry" as its epitaph. Set against #7's single approved pass, the extra rounds are not waste and they are not a language penalty — they are the price of the one thing #7 never had to buy, which is a commit contract and a fact-ordering contract that the framework does not supply. **#7's kafkajs and `@nestjs/cqrs` made those guarantees ambient; `Confluent.Kafka` and a hand-rolled dispatcher make them yours, and yours means you must prove them.** That sentence is the benchmark finding for feature 16.

One process note belongs in the entry too, because it is the second time it has cost a round: **twice now this feature has closed a red integration run by attribution rather than by isolation** — §12's "memory pressure" and §13's run-11-is-D3. Both attributions were plausible, both were written by someone who had correctly identified the *previous* defect, and both were wrong. The cheap rule that would have caught each: *a red run in this feature's own suite is isolated and looped before any sentence explaining it is written.*

---

## 3.12 What must change before re-review

1. **D4** — make `SagaPreconditionTests`'s redelivery loop wait for the row it then asserts on, not for any row with the marker. Prefer an event-type-filtered overload of `WaitForSagaIgnoredFactCountAsync`; an `atLeast: expectedIgnoredCount` parameter is acceptable. Then arm it with §3.4's probe (comment out lines 70–71 at the shipped 200 ms interval, run four times) and record the before/after verbatim. State explicitly that SO8's call site and `SagaCompensationStockRejectedTests`'s were checked and need no change.
2. **D5** — supersede §13's arming-table row 1 and the "second, distinct manifestation" paragraph: reassign that failure to D4, state that §13 therefore contains no reproduction of D3's own symptom, and cite round 3 §3.2's Q1/Q2 as D3's arming. Do not delete the original text.
3. **Do not re-arm D3.** §3.2 discharges it. Re-running Q1/Q2 costs ten container-backed runs and would establish nothing new.
4. **Nothing else.** Do not touch `KafkaFactStreamSubscriber.cs`, `SagaConsumptionTests.cs`, `SagaCompensationStockRejectedTests.cs`, `tasks.md`, any file under `src/`, or `specs/`. Re-review will re-run §3.4's probe and a confirming green, and will not re-litigate anything in rounds 1, 2 or §3.2–3.9.

A5 remains analysis; A1–A4 and A6 need no further action in this feature.

---

## 3.13 Incident — this review destroyed the leader's uncommitted `feature_list.json` id 45, and it must be re-added

**Owner: the leader. This is not a defect against the implementer and nothing above depends on it.**

Setting id 16 back to `in_progress`, I first wrote `feature_list.json` with a JSON round-trip that re-escaped every non-ASCII em dash in the file. To undo that reformatting I ran `git checkout -- feature_list.json` — and the file was **uncommitted**, so the checkout did not undo my reformatting, it discarded **every** uncommitted change to the file, including the backlog entry **id 45** that the leader added between rounds 1 and 2 for advisory A1 (`EfCoreOrderNumberAllocator`'s self-seed race) and which round 2 §2.6 and §2.7 both cite as present and well-formed. The file now holds 43 features with a maximum id of 44. The entry is not recoverable: it was never staged, so no blob exists, and no copy survives in `progress/`, the scratchpad or a stash.

I have **not** reconstructed it. Re-adding a backlog entry I did not author, from a recommendation rather than from the leader's actual text, would put invented provenance into the one file this repository uses as its state of record, and the reviewer's mandate over `feature_list.json` is a status transition, not authorship. The material to restore it from is intact in two places: `progress/impl_order_saga_orchestrator.md` §12's A1 bullet carries the recommended title and `"sdd": false` verbatim, and round 2 §2.6 records that A2's *"invert, don't delete"* polarity note was captured in the entry's acceptance criteria. **The leader should re-add id 45 before this feature's next gate.**

Two things worth carrying beyond the fix, because this is the second time this exact mechanism has cost this repository something. First, it is precisely the failure `CLAUDE.md`'s arming protocol already names — *"restore from a backup copy you took, never with `git checkout --` — most files are untracked while a feature is in flight"* — and I applied that rule correctly to the two test files I mutated (backed up by copy, restored by copy, `md5sum`-verified) and then failed to apply it to the one file I touched *outside* the arming loop. The rule is written as an arming-protocol clause, so it did not fire in my head for a bookkeeping edit; it is not an arming rule, it is a **working-tree rule**, and it belongs stated that way. Second, `./init.sh` exits **0** on the damaged file, exactly as it does on a reverted status: 43 features with contiguous-enough ids and one `in_progress` is a coherent backlog, and nothing in the harness knows an entry ever existed. That is the guard-that-does-not-guard shape this repository keeps re-finding, on the same single-writer file the leader already wrote a rule about — and the cheap defence is the same one the rule already implies: **`feature_list.json` should never be edited in a way that needs an undo; take the copy first.**

---

# Review — round 4 (fourth pass)

> **This section is additive. Nothing above it is amended, withdrawn or reopened — rounds 1, 2 and 3 stand exactly as written, including §3.2's closure of D3 and §3.13's incident record.**

**Verdict: APPROVED.** D4 is closed, and closed **structurally**: the loop's wait predicate is now literally the assertion's predicate, so the failure mode is removed by construction and not by winning a race. D5's supersession is in place, not a deletion. The attribution note is the real thing — a rule with a trigger and an action, not a confession. Nothing the four fix rounds touched shows collateral damage.

One finding of my own runs a thread through this whole section and I put it up front rather than bury it: **my round-3 reproduction vehicle did not bite today.** The control leg — D4 *unfixed*, exactly the configuration that failed 2 of 4 for me in round 3 and 3 of 4 for the implementer — passed **4 of 4**. So the implementer's pass/fail ratios and my own are both worthless as evidence *by themselves*, and the only thing that closes D4 is the structural argument plus a deterministic probe. I built one. Details in §4.2 and §4.3.

---

## 4.1 What I ran this round, and what I took on trust

Taken on trust (leader-confirmed, and not re-run by me — per `CLAUDE.md`'s reviewer guidance I re-ran no full suite): `./quality.sh` exit 0, `Orders.IntegrationTests` 63/63 twice, `./init.sh` exit 0, and the backlog state (44 features, id 45 present and well-formed, id 16 `in_review`). I confirmed the backlog myself by reading it — 44 features, `in_review` = [16], `in_progress` = [] — but did not touch it until the flip in §4.11.

Every run below is a targeted probe of a claim. `SagaPreconditionTests.cs` was backed up by copy before the first mutation (`md5 676ecc6916d4f8ca9d929bf460ac8fea` — the same value the implementer's §14 records for the shipped file, which is itself a check that the file on disk is the one their arming table restored), restored from that copy, `md5sum`-verified against it, `touch`ed and rebuilt with `dotnet build --no-incremental` before every subsequent run. `SagaIntegrationTestSupport.cs` was backed up and `cmp`-verified untouched at the end — I never mutated it.

| # | Probe | Runs | Result |
|---|---|---|---|
| P1 | **Read-level equivalence**: the loop's wait predicate vs the assertion's predicate, both read in full | — | **Identical triple.** Wait: `Count(f => f.CorrelationId == correlationId && f.Marker == marker && (eventType == null \|\| f.EventType == eventType)) > 0`. Assertion: `Where(f => f.CorrelationId == orderId && f.EventType == eventType && f.Marker == "precondition_unmet").FirstAsync()`. If the wait returns non-zero, the row the assertion reads **exists**. See §4.2 |
| P2 | **The leader's cleaner claim**: D4 fixed, and the incidental `db.Orders.SingleAsync` + status assert (lines 76–77) removed — nothing should depend on that `SELECT` any more | 4× `R25_...` alone, shipped `Relay.PollIntervalMs = 200` | **4/4 PASS**, 15–17 s each |
| P3 | **Control leg** (mine, not asked for): D4 **unfixed** — the loop reverted to the marker-only overload — with the same `SELECT` removed. This is round 3's Q4 vehicle verbatim | 4× `R25_...` alone | **4/4 PASS.** The vehicle did not bite. P2 therefore proves nothing on its own, and I decline to count it |
| P4 | **Deterministic discriminator** (replacing P2/P3): D4 fixed, wait called with a **bogus** `eventType` (`"no.such.fact.v1"`) that no row can ever match. If the new overload's filter is honoured, all ten iterations must block for their **full** 20 s budget; if the argument were inert, the run would stay at ~16 s | 1× `R25_...` | **16 s → 3 m 37 s** (217 s ≈ 16 s + 10 × 20 s), test still green. The filter is load-bearing and every iteration now waits on **its own** predicate |
| P5 | Restore from backup, `touch`, `dotnet build --no-incremental`, confirming green | 1× `SagaPreconditionTests` (both tests) | `md5sum` identical to backup; **2/2 PASS, 23 s** — and the 23 s is itself the proof the restore reached the binary, since the armed one ran 217 s |
| P6 | `Architecture.Tests` — **run**, not eyeballed (C3) | 1 | **16/16 PASS**, 3 s |
| P7 | Production source unchanged since round 3 — `cmp` of my own round-2/3 pre-mutation backups against the files on disk | — | `KafkaFactStreamSubscriber.cs`, `SagaCommandSweeper.cs`, `SagaStepTable.cs`, `FactPublisherConfinementTests.cs` all **byte-identical**. No fix round touched production code after round 1 |
| P8 | `specs/shared/` fidelity, re-derived | — | `diff -rq` against the #7 checkout: **only `test-matrix.md` differs**. Its `git diff` is 13 changed rows, and a column-by-column comparison shows **every one differs in the Status column alone**, plus the two derived tally rows (feature-3 row `0/0/11` → `9/2/0`, total `16/1/46` → `25/3/35`, both arithmetically correct against 63) |
| P9 | `tasks.md` completeness | — | **72 ticked, 0 unticked** |
| P10 | `R19`–`R29` named tests exist, character-exact, programmatically | — | **13 citations, 13 found.** No invented names |

`git status` for `src/` is exactly what it was when this round began; no production file was touched at any point by me.

---

## 4.2 D4 — closed, and the closure is structural

**The argument that decides it is a read, not a run.** Before the fix, the loop's only synchronisation was `(correlationId, marker)` and the assertion read `(correlationId, eventType, marker)` — a strictly narrower predicate, so the wait could be satisfied by a row the assertion would not accept, which is exactly what happened from iteration 2 onward. After the fix, the wait's predicate and the assertion's predicate are the **same triple** (P1). A non-zero return therefore *entails* the existence of the row `FirstAsync` then reads: there is no interleaving in which the wait succeeds and the read finds nothing, and nothing between them can invalidate it (rows are only inserted, never deleted, and no other writer touches this correlation).

The only failure mode left is the wait **timing out** — returning 0 after 20 s because the row genuinely never arrived. That is a real failure of the requirement, not a flake, and it is exactly what a test is for. `R25_...` can no longer go red on correct code by this mechanism.

**And the fix is armed, deterministically.** P4 is the probe I built after P3 showed the timing vehicle had gone quiet: give the wait an `eventType` no row can match and the run stretches from 16 s to **3 m 37 s** — ten iterations each burning the full 20 s budget. That is only possible if the filter is applied on every iteration, which is precisely the property D4 asked for: **each iteration now waits for its own row and not for an earlier one's.** It is a red/green pair with no race in it: the "red" is a 13× duration change, reproducible on one run, on any machine, at any load.

**Other call sites of the retained marker-only overload, checked by reading rather than accepted:**

- `SagaPreconditionTests.cs:151` (SO8) — one fact, one fresh `correlationId` matching no order, `unknown_order` marker, and the assertion is a `SingleAsync` on that correlation. No earlier row can exist. `count > 0` is exact.
- `SagaCompensationStockRejectedTests.cs:88` — one redelivered `stock.rejected.v1` against an already-`cancelled` order, and `order.placed.v1` is *provably* consumed legitimately before it (line 41's `stock.reserve`/`sent` state gate), so no other row can carry `precondition_unmet` for that correlation. The assertions after the wait (lines 91–95) read counts, not a specific row. `count > 0` is exact.

Both were left unchanged, and the implementer says so explicitly, as round 3 §3.12 instruction 1 required.

**One thing beyond what was asked, and it is the right kind of extra.** The retained overload now carries an XML doc that names the trap in the imperative — *"Do NOT use this inside a loop that publishes several facts for the SAME correlation and then asserts on the row for ONE of them"*. D4 was a helper whose contract was fine for its original caller and silently wrong for a new one; a warning at the definition is the only place that catches the *next* caller, and it costs nothing. That is a durable defence rather than a point fix.

---

## 4.3 My own control leg — this feature's lesson, turned on the reviewer

P2 alone reads as a clean confirmation: the leader asked for exactly this claim ("removing the intervening `SELECT` should leave the test green"), and it is green, 4 for 4. I nearly stopped there.

P3 is why I did not. Running the **unfixed** code through the same vehicle — the configuration that failed **2 of 4** for me in round 3 and **3 of 4** for the implementer in their §14, at the same shipped 200 ms interval, on this same machine — produced **0 failures in 4**. The vehicle has gone quiet in the hours since. Whatever the cause (the box is materially less loaded than it was this afternoon: swap was exhausted during round 3's probing and 36 GB was available during mine), the consequence is exact and unpleasant: **a pass/fail ratio on this race is not stable across a few hours on one machine, so no ratio — 1-in-12, 3-of-4, or 15/15 — was ever going to arm it.**

Three things follow, and all three belong in the record:

- It **retroactively vindicates round 3 §3.3** rather than contradicting it. That section argued the reproduction window is two-sided and non-monotonic, so no single knob makes the unfixed shape fail deterministically. P3 is that argument arriving as an observation: the window moved without anyone touching a knob.
- It means **P2 is not evidence and I do not present it as such.** Reporting "4/4 green with the fix" without a control would have been the same reasoning shape this feature has been rejected over twice — a green run offered as confirmation of a hypothesis that was never loaded. The difference between §12's "memory pressure", §13's "run 11 is D3" and this paragraph is one probe.
- It is why **P4 exists**. When the probabilistic vehicle dies, the answer is not a larger sample; it is a different question. P4 asks "is the filter applied?" instead of "does the race fire?", and that question has a deterministic answer.

The implementer wrote the attribution rule this round. The reviewer needed it in the same round. That is worth more to the trilogy than either of the defects.

---

## 4.4 D5 — supersession verified in place, not a deletion

`progress/impl_order_saga_orchestrator.md` §14 opens with *"§13, superseded (not deleted) — D5"* and does the three things round 3 §3.12 instruction 2 named: it reassigns row 1's `Sequence contains no elements` failure to D4, it states outright that **§13 therefore contains no reproduction of D3's own symptom**, and it directs the reader to round 3 §3.2's Q1/Q2 as D3's arming — *"Round 4 should treat D3 as closed on the review's Q1/Q2, not on this report's superseded §13."*

I verified the original text is still standing and unedited: §13's arming table (both rows) and the paragraph beginning *"Row 1's failure is a **second, distinct manifestation of the same D3 root cause**"* are intact at their original position. Nothing was rewritten to look as if it had been right all along. This is the same discipline §13 correctly applied to §12, applied one level further, and it is now the established convention of this document.

**One residual, non-blocking (A8 below):** the supersession is discoverable only by reading forward. A reader who jumps to §13 for D3's arming — which is exactly what a future session looking for "how was this armed" will do — is not told at the point of the claim that it has been withdrawn. A single bracketed pointer at §13's table (and at §12's closing sentence) would close that, and the same applies to §12. Advisory, not a defect: the instruction was to supersede in the established way, and it was followed.

---

## 4.5 The attribution note — would it stop the next person?

**Yes, and it is the most transferable paragraph this feature produced.** I judged it against one test: does it give a *trigger* and an *action*, or does it give remorse?

It gives both. The trigger is mechanical and checkable by someone else — *"before writing a sentence that explains a red run in this feature's own suite"*. The action is specific and is the non-obvious half: isolate and loop **the mutation the new explanation implies**, *"not the mutation that was already believed to be the cause, but a probe of the specific new failure's own shape"*. And it names the epistemics that make the whole class invisible: *"A green re-run, however many times repeated, answers 'does this still happen sometimes' and never answers 'is this the same cause as last time'"*.

It also does the thing a merely-recording note does not: it explains **why it felt safe both times** — that the attribution was written by someone who had just correctly diagnosed the *previous* defect, so a second red run in the same test reads as confirmation rather than as an unloaded second hypothesis. That is the part that generalises past this feature, because it is a property of the situation, not of this implementer. And it does not overclaim: it states plainly that the rule was applied *"on this round only after the reviewer named the pattern rather than on my own initiative"*.

The one thing it does not say, which §4.3 supplies from the other side of the table: **the rule binds the reviewer too**, and the failing green control is the cheapest way to catch it. I recommend the leader lift both paragraphs — §14's and §4.3 — into `docs/PROCESS.md` as one entry.

---

## 4.6 Collateral from four passes over the same files — what I looked for and what I found

Four fix rounds over two test files is enough churn to warrant looking for damage rather than assuming none. What I checked:

- **A wait weakened elsewhere.** Read every `Wait*` call site in the saga suites. All are **state gates** (`stock.reserve`/`sent`, an order status, an outbox count, a broker offset), none is a sleep, and the only `Task.Delay` in an assertion path is `SagaCompensationStockRejectedTests.cs:90`'s explicit 1 s *settle window* before asserting that **nothing** changed — which is the one case where a delay is the correct instrument, since it is proving a negative.
- **An assertion loosened to make a run go green.** `SagaConsumptionTests` SO9 still asserts **both** halves from the broker (`baseline == afterFailedDelivery`, then `afterSuccess > baseline`) with the deterministic `ThrowOnceGate` release between them — D1's repair is intact and no weaker than when I passed it in round 2. `SagaHappyPathTests` still asserts exact counts (`SagaCommands == 4` for R23's absence, exactly one `order.confirmed.v1` and one `order.completed.v1`), `SagaCompensationCreditRejectedTests` still asserts the compensation array length, the step name, the causation id and the `eventId` linkage. Nothing has been relaxed to an inequality or a `NotNull`.
- **A helper whose new overload changed behaviour for an existing caller.** It cannot: the marker-only signature is preserved and forwards with `eventType: null`, and the new predicate collapses to the original when `eventType` is null. Both surviving callers were re-read (§4.2) and are single-candidate cases where `count > 0` is exact.
- **Production drift.** P7: four files I hold pre-mutation backups of are byte-identical. Rounds 2, 3 and 4 of the fix work touched **test code only** — which is the same sentence as the benchmark finding in §3.11, now true for the whole feature rather than for three rounds of it.
- **Spec drift.** P8: `specs/shared/` differs from #7 in `test-matrix.md` alone, and that file's diff is Status cells plus two derived tallies. No amendment was made, and none was needed.

Nothing found.

---

## 4.7 Advisories at approval (7 open, none blocking)

- **A1 — `EfCoreOrderNumberAllocator` self-seed race.** Now **backlog id 45** (`order_number_allocator_seed_race`, `pending`, phase 8), restored by the leader after this review destroyed it in round 3. Verified present and well-formed: four acceptance criteria, including *"is INVERTED, not deleted"* (A2) and *"the fix is armed"*. **This is a real defect in already-closed, already-committed feature 15, found by feature 16's own work** — see the history entry.
- **A2 — race-test polarity.** Carried into id 45's acceptance criteria. No further action here.
- **A3 — a payload that throws during deserialization retries forever.** Feature 27's (DLQ) scope, seam identified at `SagaFactsConsumer.cs:123`. Unchanged.
- **A4 — two design-vs-code divergences**, both argued in code comments and now disclosed in §12. If the inward-pointing-arrow exception (`ISagaCommands`/`SagaCommandRequestFactory` in `Application/` referencing `Infrastructure.Messaging.Rpc` types) is ever to be *bounded*, the rule belongs in `Architecture.Tests`; today nothing watches it.
- **A5 — SO9's baseline read** could in principle be inflated by another test's uncommitted backlog draining between the two reads. Correctly left as analysis by both sides; still unreproduced. If SO9 ever fails on correct code, §13's A5 paragraph is the map.
- **A7 — NEW. A timeout in the saga waits still surfaces as an opaque exception.** `WaitForSagaIgnoredFactCountAsync` and `WaitForSagaCommandCountAsync` (`SagaIntegrationTestSupport.cs:180-200`, `:110-130`) **return 0** on timeout rather than throwing, so the *next* genuine failure at `R25_...`'s loop will still read `System.InvalidOperationException : Sequence contains no elements` at the `FirstAsync` — the exact message that took three rounds to attribute correctly — rather than "no `precondition_unmet` row for `stock.rejected.v1` within 00:00:20". `WaitForOrderStatusAsync` in the same file already does the better thing and throws a `TimeoutException` naming the last observed value. One `Assert.True(count > 0, …)` after the loop's wait, or a throwing wait, converts the next failure into a self-explaining one. Not blocking — the guard is correct and the property is proven; this is about what the *next* person reads at 2 a.m.
- **A8 — NEW. The superseded passages carry no in-place pointer.** §12's closing attribution and §13's arming table are both correctly left standing and correctly corrected later, but only a linear reader finds the correction. A bracketed *"[superseded — see §14]"* at each claim would make the record safe to read by jump. Documentation only.

---

## 4.8 CHECKPOINTS — full and final walk

Every applicable box, re-walked at approval rather than carried forward.

### C1 — The harness is complete

- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (plus `suite_runner`).
- [x] Every agent definition declares its model.
- [x] `./init.sh` exits 0 in a coherent state — leader-confirmed before this pass. Re-run by me **after** the status flip it exits **1**, on the lockstep check alone, because `progress/current.md` still names feature 16 as active while the backlog now has none `in_progress`. That is the guard working, not failing: see §4.11.

### C2 — State is coherent

- [x] At most one feature `in_progress` — **zero** after this approval; id 16 is `done`.
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it — including id 16, whose 63 `Orders.IntegrationTests` and 233 `Orders.UnitTests` are green.
- [x] `progress/current.md` describes the active session, not leftovers. **Owed to the leader at close:** its Status line still reads `in_review` and must move to reflect the approval before the next feature opens.
- [x] Every `blocked` feature records why — there are none.
- [x] **The backlog is intact**: 44 features, id 45 present and well-formed after round 3's incident, and this round's only write was the single `"status"` value on line 261 (§4.11).

### C3 — Architecture is respected

- [x] No `Microsoft.EntityFrameworkCore` / `Confluent.Kafka` / `NATS.*` / `MongoDB.*` / `Microsoft.AspNetCore.*` inside any `Domain/` — verified by **running** `Architecture.Tests` this round (P6, 16/16), not by eye.
- [x] No cross-service DB access; no FK crosses a service boundary.
- [x] No shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs`.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` — `CqrsDomainPurityTests` green in P6.
- [x] `src/SharedKernel` still has zero `PackageReference` — `SharedKernelHasNoPackagesTests` green in P6.
- [x] No `decimal` in domain arithmetic — `DomainDecimalTests` green in P6.
- [x] Every interaction classifiable as Kafka-fact or NATS-RPC — walked in round 1 §2, unchanged since (P7).
- [x] No stray debug logging, no context-free TODOs.

### C4 — Verification is real

- [x] `./quality.sh` passes (exit 0, leader-confirmed; I re-ran `Architecture.Tests` and `SagaPreconditionTests` rather than the world, per `CLAUDE.md`).
- [x] Domain-pure tests are pure — `SagaStepTableTests` builds real `Order` instances with no store, no framework, no broker.
- [x] Integration tests use Testcontainers against real MsSql / Kafka / NATS — verified by running them this round.
- [x] Coverage thresholds — **not gated by design**; `quality.sh` names feature 34 (phase 21) as the owner and refuses to fake a gate. Applicable-but-deferred, not empty. Coverage is *reported*, not gated, and this record says so rather than implying otherwise.
- [x] No Jest anywhere.
- [x] **The box round 3 failed on is now clear**: `R25_...` can no longer go red on correct code — §4.2, structural, with a deterministic arming in P4.

### C5 — The session closed cleanly

- [x] No suspicious untracked files — `git status` is exactly the feature's own new sources, tests and reports.
- [x] `progress/history.md` has an entry for this feature **including its effort record** — written at this approval (§4.10).
- [x] `feature_list.json` reflects the true state — id 16 `done` (§4.11); id 45 untouched by me.
- [x] The human has been told what was done and how to test it manually — `progress/impl_order_saga_orchestrator.md` §8 is the manual recipe, and round 1 confirmed its observation against the live stack.
- [x] Claude did not commit.

### C6 — Spec-Driven Development

- [x] `specs/order_saga_orchestrator/` has all three of `requirements.md`, `design.md`, `tasks.md`.
- [x] `requirements.md` uses strict EARS with `R<n>`/`SO<n>` ids on every requirement.
- [x] **Every task ticked** — P9: 72 ticked, 0 unticked, M5 included (D2, round 2).
- [x] **Every `R<n>` covered by a concrete named test** — P10: all 13 citations for R19–R29 exist and are character-exact; SO1–SO11 walked in rounds 1–3; SO9 closed in round 2; R25's test is now correct *and* reliable, which is what D4 was.
- [x] The spec commit precedes the implementation commit — spec and code are both uncommitted and the human commits in that order; `progress/spec_order_saga_orchestrator.md` predates the code.

### C7 — Spec-reuse fidelity and benchmark honesty

- [x] **`specs/shared/` byte-identical to #7's except `test-matrix.md`'s Status column** — re-derived this round (P8), not carried: `diff -rq` shows only `test-matrix.md` differing, and its 13 changed rows differ in the Status column alone, plus two derived tallies that recompute correctly (25 + 3 + 35 = 63).
- [x] Every deviation is a recorded amendment — **none was made and none was needed**; `R24`'s and `R29`'s splits are #7's own, inherited rather than re-forked.
- [x] The `R<n>` ids are #7's, and the .NET realisations genuinely satisfy them — walked row by row in round 1 §3 and re-confirmed by P10.
- [ ] `n8n/workflows/*.json` fire green against the .NET Gateway — **not applicable**: no Gateway exists yet (feature 17+).
- [ ] The black-box API script proves the same saga steps as #7's — **not applicable yet**; this is exactly `R24`'s API half and `R28`'s e2e half, both left `TODO` under the ratified scope.
- [x] `progress/history.md` effort records complete and honest, **including the features that were not faster** — this feature is the clearest "not faster" in the build and is recorded as one, with the reason attached rather than explained away (§4.10).
- [x] The README's benchmark section — unchanged by this feature; the material for its next revision is §3.11 and §4.10.

---

## 4.9 `R<n>` → test mapping, as verified this round

Programmatic (P10), against `specs/shared/test-matrix.md` as it now stands — every cited name confirmed to exist, character-exact, in the test sources:

| Req | Named test | Status |
|---|---|---|
| R19, R20, R21, R22, R23, R24 | `SagaHappyPathTests.R19_R24_HappyPath_AdvancesTheOrderThroughEveryStatusIssuingEachOwedCommandAndEmittingExactlyOneOrderConfirmedAndOneOrderCompleted` | Found; R24's API half correctly `TODO` (Gateway) |
| R21 | `SagaStepTableTests.R21_CreditApprovedV1_PerformsBothEdgesInOneLoadSaveAndRaisesExactlyOneOrderConfirmed` | Found; armed in round 1 |
| R23 | `SagaStepTableTests.R23_InvoiceIssuedV1_AdvancesToInvoicedAndOwesNothing` | Found; armed in round 1 (K7) |
| R25 | `SagaPreconditionTests.R25_EachOfTheTenConsumedFacts_RedeliveredAfterCompletion_IsIgnoredAndRecordedWithObservedAndExpectedStatus` | Found; **D3 and D4 both closed**; armed structurally (P1) and deterministically (P4) |
| R26 | `SagaCompensationStockRejectedTests.R26_CancelsWithReasonStockRejectedAndIssuesNoStockReleaseCommand` | Found; armed in round 1 (B4 row 1 / K6) |
| R27, R28 | `SagaCompensationCreditRejectedTests.R27_R28_SO6_SO7_ReleasesThenCancelsInCausalOrderWithOneCompensationStepAndNeverRetriesTheRejectedHold` | Found |
| R29 | `SagaCommandRetryTests.R29_SO4_SO5_WithNoResponder_ParksAfterExhaustedAttemptsLeavingTheOrderStatusUnchanged` | Found; dead-letter row correctly `TODO` (feature 27) |
| SO1–SO11 | walked in rounds 1–3 | SO9 closed round 2 (D1); SO3's crash-window half proven by `SagaCommandRetryTests.SO3_...IsStillIssuedBySweeperCycle...` with its negative companion |

---

## 4.10 Effort record and benchmark — written into `progress/history.md` at this approval

Round 1 §7, round 2 §2.8 and round 3 §3.11 stand unamended. The history entry carries their conclusion forward with the final arc — **4 implementation passes and 4 review rounds, against #7's 1 implementation session and 1 review pass approved first time** — and states plainly that this is **the most expensive feature in the build**, recorded as a loss with the reason attached.

The three sentences that survive from the whole record:

1. **Every defect any reviewer found across four rounds was in test code.** D1's missing broker read, D3's unsynchronised drive, D4's vacuous gate. The transcribed orchestrator — the fourteen-row step table, the compensation mapping, the precondition matrix, the redelivery semantics — **was never touched by a single reviewer defect**, and the only production defect in the feature (the sweeper's double-claim no-op) was found by the *implementer's own arming*, before any review.
2. **Reuse moves the residual risk from "did we model it right" to "does our proof actually prove it" — and a wrong proof is green.** That is why four rounds bought real things rather than churn, and it is the sharpest single finding of the reuse run so far.
3. **The same review found a real defect in already-closed, already-committed feature 15** — the allocator's unlocked self-seed, now backlog id 45 — which the round count alone hides. A process that runs four rounds and also reaches backwards into closed work is not simply an expensive process.

---

## 4.11 Bookkeeping performed by this review

- `feature_list.json` — **id 16 → `done`**, applied as a single-value edit to line 261 (`"status": "in_review"` → `"status": "done"`, the file's only `in_review`). **No JSON round-trip, no reformatting, and no `git checkout`** — per `CLAUDE.md`'s new working-tree rule, added after round 3's incident and read on disk before this edit. Nothing else in the file was touched; id 45 is exactly as the leader restored it.
- `progress/history.md` — entry for `order_saga_orchestrator` appended, **with the effort record**.
- `./init.sh` re-run after both writes: **exit 1**, and correctly so. Every backlog check is green — `no feature in_progress`, `SDD coherence: 3 sdd feature(s) past pending have their triple-doc`, `progress: 18/44 features done` — and the single `FAIL` is the session-file lockstep check: *"progress/current.md claims a feature while none is active"*. That is a **true** statement about the working tree the moment a feature closes, and `progress/current.md` is the leader's file, not this review's: the reviewer's bookkeeping mandate is the `feature_list.json` status and the `progress/history.md` entry. It clears when the leader resets the session file at close, and the C2 box above records it as owed. I am recording the exit code as it actually was rather than the one I expected — this feature has cost four rounds over exactly that distinction.
- `progress/current.md` is the leader's to reset; its Status line still reads `in_review` and is the one piece of bookkeeping this review does not own.
