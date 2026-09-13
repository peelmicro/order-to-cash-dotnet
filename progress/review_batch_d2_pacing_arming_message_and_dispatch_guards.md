# review — batch D2: backlog ids 69, 82 and 89

**Verdict: APPROVED — id 69 APPROVED, id 82 APPROVED (with one bullet handed to the coordinator, below), id 89 APPROVED.** First round, no rejections.

Reviewer: one session, 2026-09-13, ≈15:35 → ≈16:35 CEST. **Scoped review, as briefed.** I did **not** re-run `./quality.sh` — the claim under test is not the full suite, and I verified the implementer's run from its own log instead (see §4). What I ran myself: four armings from scratch (three of id 82's nineteen, plus id 89's), six independent enumerations re-derived, and the count reconciliation the record left open.

---

## CHECKPOINTS walked

- [x] **C1** — harness intact; `./init.sh` exit 0 reported by the implementer at both ends of its session and unchanged since.
- [x] **C2** — at most one `in_progress` before my write (id 69); all statuses valid; `current.md` describes this session. After my write, **zero** `in_progress`.
- [x] **C3** — not touched by this batch. `src/` carries only batch D1's nine `src/Gateway/**` files (`git status --porcelain src/`, re-run by me after all four of my own armings were restored). Every `src/` edit in this batch and in this review was an arm, restored from a `cp` backup and `cmp`-verified.
- [x] **C4** — `./quality.sh` clean: I re-summed the implementer's own log (`scratchpad/quality.log`, 2026-09-13 15:24) with an independent parser: **18 projects, 2010 total, 0 `Failed!` lines**. New integration guards hit real containers (real NATS for id 69's Gateway tests, real MS-SQL + Kafka + NATS for id 89's); no Jest; xUnit throughout. Coverage gate line unlocated by me, unchanged from prior reviews.
- [x] **C5** — history entries with effort records appended by me; `feature_list.json` updated by me and by nobody else this session; no commit, no push.
- [n/a] **C6** — all three entries are `sdd: false`.
- [x] **C7** — `specs/shared/` untouched by this batch.

---

## Acceptance → evidence

### Id 69 — `gateway_readiness_pacing_is_unguarded`

| bullet | verdict | evidence I checked myself |
|---|---|---|
| 1 — deleting each `Task.Delay` fails a named test | **met** | arms 69-A/69-B in the record; the two named tests exist and read the production constants, not transcriptions |
| 2 — a DETERMINISTIC test, change of KIND | **met, and the determinism is in the TEST** | This was the brief's sharpest question and the answer is clean. `GatewayReadinessPacingRaceTests.cs:38` holds `_subscriberDelay = 300 ms` **in the test class**, and `:192-220` holds `UnpacedReplicaReachableAsync`, an in-test replica of the loop with the `Task.Delay` removed. Control and subject therefore race the **same** delayed subscriber in the same fixture, over `Runs = 3`. No delay lives in any mutation. This is not the phase-14 shape where a delay was inserted into the mutation while the two tests never overlapped. |
| 3 — the failure names pacing, not an incidental timeout | **met** | `:84-94` asserts **both** that the unpaced replica lost and that it lost in **less than** the 300 ms delay, so a loss for another reason fails the test rather than passing it. `:155-164` names site, run, observed wall-clock, pacing interval and paced budget, in both directions. |
| 4 — armed, verbatim | **met** | record §1.3 |
| 5 — the widened committed-offset half | **met** | I re-ran the prescribed sweep myself, path-excluded at `find`. **5 hits: 4 call sites + 1 comment** (`NotificationDeadLetterTests.cs:100`), exactly as classified — plus, now, the new helper's own doc-comment lines. All four call sites go through `KafkaCommittedOffsetRetry.Read` (verified by grep at `SagaIntegrationTestSupport.cs:431`, `NotificationDeadLetterTests.cs:425`, `OffsetContractTests.cs:60`, `ProjectorDeadLetterTests.cs:339`). **Three per-project copies each carry their own 4-test proof** (`grep -c '[Fact]'` = 4, 4, 4), so no copy is guarded only by a sibling's test — the class-not-instance rule honoured. The substituted broker is disclosed in the test class's own doc comment, which bullet 5 explicitly permits. |

### Id 82 — `arming_failure_messages_that_name_nothing`

**Population re-derived independently.** I re-ran both of the record's commands. Command A now returns **577** quoted failure messages across 20 assertion families (record: 545); Command B returns **20** `Assert.False()` and **16** `Assert.True()` (record: 17 and 15). The deltas are this batch's **own** record additions — `impl_batch_d2_….md` alone contributes 4, and the 17 annotated records contribute the rest — so the two runs are consistent, not contradictory.

**The derivation is not filtered by the property under test**, which was the brief's question, and it survives the test: membership is decided by *"this string appears as a quoted failure in a `progress/` record"*, and every one of the 20 families is then given a classification line. A violation — a nameless assertion used as an arm's failing assertion — lands **inside** the candidate set and must be classified out explicitly; it cannot remove itself. That is the literal-set-plus-subtraction shape `CLAUDE.md` asks for. Deriving from the 562 raw code hits instead would have required selecting the ones that "look nameless", which is the failure the rule names.

**Three of the nineteen arms re-run by me from scratch**, each: `cp` backup → mutate → `dotnet build --no-incremental` → run the ONE named test → restore → `cmp` → `touch` → forced rebuild → confirming green. Nothing was built concurrently (`pgrep -a -x dotnet | grep -v nodemode` empty before each).

| arm | my mutation | result | message names the claim? |
|---|---|---|---|
| **82-10** | `src/Orders/Infrastructure/Outbox/KafkaFactPublisher.cs:41` `EnableIdempotence = true → false` | `KafkaFactPublisherConfigTests.OI7_…` **FAILED** | yes — *"the fact producer is built with EnableIdempotence = False. Without it an internal librdkafka retry can reorder or duplicate a partition's records (OI7)."* **Verbatim identical to the record.** Restore `cmp` OK, 1/1 green. |
| **82-5** | `src/Projector/Domain/Summaries.cs` — `detail["note"]` written unconditionally | `SummariesTests.PR16_OrderCancelled` **FAILED** | yes — *"Summaries.OrderCancelled wrote a 'note' detail key (value: &lt;null&gt;) for a fact that carries no note. SA-2 requires the key to be absent, not present-and-null."* Restore `cmp` OK, `SummariesTests` 18/18 green. |
| **82-8** | `src/Fulfillment/Application/DespatchCreationService.cs:70` `BuildReply(raced, created: false → true)` | `DespatchCreationServiceTests.F8_InFlightRace_…` **FAILED** | yes — *"the in-flight race branch replied created=true for despatch DES-000002, which a concurrent committer had ALREADY created. Only the winner may report created=true."* **Verbatim identical to the record.** Restore `cmp` OK, `Fulfillment.UnitTests` **146/146** green — matching the record's own table. |

**Bullet 2's closure re-derived.** The content-based sweep `find ./tests … | xargs -0 grep -n 'string\.IsNullOrEmpty'` returns **15** lines: **13 code sites and 2 comments** describing the deletions, exactly the reconciliation the record states. I read all 13 and **every one carries a claim-naming message** — including the six re-formatted across three lines (`Fulfillment`/`Billing` `LogCorrelationTests:71`, `SagaCommandDeadLetterTests:93`, `TimelineProjectionTests:40`, `AuthAndRateLimitHttpTests:30`, `ProblemJsonCorrelationTests:63`), which is why the single-line sweep returns 9 and not 13. The count reconciles.

**Bullet 5 is NOT applied, correctly, and is handed to the coordinator — see "The one open item" below.**

### Id 89 — `saga_dispatch_concurrency_is_exercised_everywhere_and_asserted_nowhere`

**Enumeration re-derived by me, and it is sound.** `AddHostedService<SagaCommandDispatchWorker>()` at `OrdersSagaServiceCollectionExtensions.cs:98` is the **only** registration; `AddOrdersSaga` has exactly one caller, `OrdersHost.cs:93` (all other hits are doc/prose comments). The host-boot sweep returns 13 `CreateBuilder` lines: **5 `OrdersHost.CreateBuilder` worker-bearing sites** (`SagaIntegrationTestSupport.cs:41`, `SagaConsumptionTests.cs:86` and `:122`, `HealthProbesTests.cs:135`, `SagaCommandRetryTests.cs:225`), **5 bare `Host.CreateApplicationBuilder` non-worker sites** (exactly the five named), and 3 comment lines. Identical to the record.

**The `Dispatch` column is genuinely derived by subtraction.** I re-ran the whole-project `Dispatch` sweep: **28 hits** (the record's 20 plus this batch's own new file), and after removing `AddDispatcher`, doc comments and prose only four remain — three of them the new test class's own name/method/message and one `GetRequiredService<IDispatcher>()`. **There is no `Dispatch.DegreeOfParallelism` assignment anywhere under `tests/`**, including in the new test, which overrides only `Command.TimeoutMs` and `Sweeper.Enabled`. Id 80's mitigant is verified live, not inherited.

**Bullet 5's exit was not taken and that was the right call** — the guard exists, at 10 s and zero new containers.

**Bullet 4's cost stated and confirmed by me**: my own confirming run of the restored worker took **10 s** for the one test, and it joins `SagaCollection`, starting no container of its own.

**Bullet 3 armed by me from scratch.** `src/Orders/Infrastructure/Saga/SagaCommandDispatchWorker.cs:40` reverted to a single sequential loop (`_ = options.Value.Dispatch.DegreeOfParallelism; var degreeOfParallelism = 1;`), `dotnet build --no-incremental`, one named test:

> **FAILED (30 s)** — *"order ORD-000002's stock.reserve never reached 'sent' within 20s while order ORD-000001's stock.reserve RPC was held open by the gated responder (its own per-attempt budget is 60s, and the sweeper backstop is disabled for this test). SagaCommandDispatchWorker is therefore dispatching the fast path SEQUENTIALLY: one blocked responder is stalling every other order's saga command behind it — backlog id 80's defect. Order ORD-000001's own row is 'pending'; requests observed by the responder: [ORD-000001]."*

**Verbatim identical to the record.** It names the blocked order, the stalled order and the bound, as bullet 3 requires. Restored from backup, `cmp` OK, line re-read, forced `--no-incremental` rebuild, confirming run **1/1 green in 10 s**.

The test's two premises are asserted rather than assumed — `:81` that the gated responder **provably received** order A's request, and `:107` that A's row is still not `sent` at the moment B's reaches it — so defeat-list attack #9 is closed on both halves. Disabling the sweeper and raising `Command.TimeoutMs` to 60 s are the two things that make the guard *able to fail at all*, and both are stated in the test's own doc comment.

---

## §4 — The count reconciliation: the record left it open, and I closed it

The record discloses honestly that its **+160** gap to the last figure it could find (1833) is unreconciled. It is now reconciled, and nothing is wrong.

The record took its baseline from `progress/current.md` (the SA-3 verification run, 2026-09-11). **A later full 18-project run existed in the same scratchpad the whole time**: `quality_feature83.log`, 2026-09-12 19:43, which my parser sums to **1908, 0 failed**. Against the implementer's own **2010**:

- Whole-solution delta **1908 → 2010 = +102**, per-project, summed by me.
- **This batch's +17 lands exactly where it claims**: `Gateway.IT` 61→65 (+4), `Projector.IT` 59→63 (+4), `Notifications.IT` 22→26 (+4), `Orders.IT` 146→152 (+6, of which D2's 4 + 1 = 5).
- The remaining **+85** is entirely in projects D2 added nothing to: `Gateway.UnitTests` +26, `Orders.UnitTests` +26, `Billing.UnitTests` +20, `Fulfillment.UnitTests` +12 (= the +78 batch D1 records, plus the id-62/SA-4 rework), and `Orders.IT` +1.
- Every other project is byte-for-byte unchanged across both runs.

So the attribution *"those +160 are other features'"* is **true**, and now measured rather than asserted. Advisory A1 below is about the method, not the claim.

---

## Defects and advisories

**No blocking defects.** Four advisories, none of which changes a verdict:

- **A1 — a stale baseline was chosen when a fresher one was one `ls` away.** `progress/impl_batch_d2_….md:459`. `CLAUDE.md`'s rule is *"a number that does not reconcile is a finding, not a footnote"*; the record did treat it as a finding and said so plainly, which is why this is advisory rather than blocking. But the resolving artefact (`quality_feature83.log`, +102 reconciling exactly) was in the same directory as the log it was quoting. **When a figure will not reconcile, look for a nearer baseline before declaring the gap unreconciled.** Reconciliation now recorded in §4 above.
- **A2 — id 89's harness call-site count is an overcount.** The record says *"36 call sites in 13 files"*; my per-file count over the same sweep gives **32 in 12**. `OrdersCancelResponderReadinessRaceTests.cs` contributes **zero** call sites — all five of its `StartHostAsync` hits are doc/prose comments (`:18, :53, :73, :94, :115`). The direction is over-, not under-counting, so no member of the population escaped classification, and the conclusion the number supports (every such site runs the real parallel worker; none overrides `Dispatch`) is independently verified above.
- **A3 — one residual hole in id 82's population, worth naming for #9.** Deriving from *quoted* failure messages cannot see an arm that was **recorded without a verbatim message**. Bullet 1's class is *"the failing assertion of a recorded arm"*, and an arm recorded as prose only is in the class and outside the sweep. Given that `CLAUDE.md` has required verbatim messages throughout, the residue is likely empty — but it is unmeasured, and the honest statement is that the derivation covers *recorded-and-quoted* arms.
- **A4 — a quoted message differs from what the obvious mutation produces.** Record row 82-5 quotes `(value: )`; the straightforward unconditional write yields `(value: <null>)`, which is what my own re-run produced. Consistent with the implementer having mutated to an empty string rather than a null. Harmless; the arm is real either way, and I confirm the assertion kills.

**The finding D2 raised (`SagaConsumptionTests.SO9`'s F6 arm no longer killing) is out of scope here and already filed by the leader as id 94, phase 15.** I did not re-file it and did not touch `feature_list.json` beyond the three status lines. Its substance stands on the record's own measurement, and the annotation in `impl_order_saga_orchestrator.md` means no reader inherits the superseded claim.

---

## The one open item — id 82 bullet 5, routed and not narrated

Id 82's fifth acceptance bullet asks for **one sentence in `CLAUDE.md`**. `CLAUDE.md` is the coordinator's file and the implementer correctly did not edit it: `grep` for *"failure message must name the claim"* returns **nothing** on disk, and the sentence sits **drafted, verbatim, in `progress/impl_batch_d2_pacing_arming_message_and_dispatch_guards.md` §2.6**.

I am closing id 82 because the work it owned is done and the remaining action belongs to a different writer — **not** because "someone later will apply it". So, explicitly, with a named artefact and a named owner rather than a sentence about the future:

> **OWED BY THE COORDINATOR, before phase 14 is committed: apply the sentence at `progress/impl_batch_d2_pacing_arming_message_and_dispatch_guards.md` §2.6 to `CLAUDE.md`'s arming-protocol bullet under *Testing conventions*.** It is a copy-paste of one paragraph. If it is not applied in this phase, it is not discharged by this review and must be filed as its own numbered entry — *"the next feature that touches `CLAUDE.md`"* is the exact formulation this repository has watched fail twice.

The reason this is worth the paragraph rather than a footnote: the rule is the only part of id 82 that changes what future work does. The nineteen re-armed messages are the evidence; the sentence is the thing that stops the class recurring.

---

## One housekeeping consequence of this close, for the coordinator

`./init.sh` now reports exactly **one** `[FAIL]`, and it is a direct and expected consequence of closing the last `in_progress` feature: *"progress/current.md claims a feature while none is active"* — `current.md` still names id 69 as the active feature. `progress/current.md` is the coordinator's session file and I did not edit it. Everything else in `init.sh` is `[OK]`, including SDD coherence, the commit-msg hook and shared-spec parity with #7 (byte-identical across 6 files).
