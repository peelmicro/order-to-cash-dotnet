# Review — `fulfillment_despatch` (feature 18, phase 9) — round 1

**Verdict: REJECTED.** One blocking defect (a named guard that does not guard the property it is named for), one required report change (a claim of absence carried in prose), four advisories. The code is behaviourally correct everywhere I probed it, the lock protocol is reused rather than re-derived, and both mutation families fire on the fact-emitting branch — the rejection is about a guard, not about behaviour.

**Backlog id 49 is a separate judgement and it holds.** `OrderStockReservation.Release` now takes the `newId` delegate, its one caller supplies it, and the named test genuinely fails when the delegate is bypassed — I armed it myself. **Id 49 is set `done`; id 18 goes back to `in_progress`.** The required change for id 18 does not touch `OrderStockReservation.cs` or its test, so the two do not interfere.

`sdd: false` — the specification of record is `feature_list.json` id 18's two acceptance bullets, read alongside `specs/shared/requirements.md` R36, `domain-model.md` §4.3 (F6/F7/F8) and `asyncapi.yaml`'s `despatchCreate` / `OrderDespatchedPayload`. Both acceptance bullets are met and are guarded over real containers.

---

## 1. What I ran, and what I did not

Per `CLAUDE.md`'s *probe the claims, do not re-run the world*:

| Ran | Result |
|---|---|
| `dotnet test tests/Fulfillment.UnitTests` (baseline) | 103 / 103 |
| `dotnet test tests/Fulfillment.IntegrationTests --filter DespatchCreateTests` (baseline, real MS-SQL + NATS + Kafka) | 5 / 5, 18 s |
| Six independent mutation probes (below), each restored and force-rebuilt | see §3 |
| `dotnet build OrderToCash.sln --no-incremental` after the last restore | Build succeeded, 0 warnings, 0 errors |
| `dotnet test tests/Fulfillment.UnitTests` (confirming) | 103 / 103 |
| `dotnet test tests/Architecture.Tests` (confirming — C3, run not eyeballed) | 16 / 16 |
| `dotnet test tests/Fulfillment.IntegrationTests` **whole suite** (confirming) | 53 / 53, 2 m 58 s |
| `./init.sh` before and after my `feature_list.json` edit | exit 0, coherent, 1 `in_progress` |
| `diff -rq ../order-to-cash-nestjs/specs/shared specs/shared` | one differing file: `test-matrix.md` (C7 box 1) |
| Five enumerating greps (§4) | outputs recorded below |

**Not re-run:** the other nine test projects and `./quality.sh` end to end. The implementer's run is on record at exit 0 across twelve projects with `dotnet format --verify-no-changes` clean; I verified 103 + 16 + 53 of those directly plus a clean full-solution `--no-incremental` build, and every probe restored to green. Nothing this feature touches lives outside `src/Fulfillment` and `tests/Fulfillment.*` (git status, §4 E1), so the untouched projects are untouched by construction rather than by assumption.

---

## 2. The blocking defect

### D1 — `OrderDespatchTests.Create_TheAdvicesIdAndTheFactsEventId_AreBothMintedByTheNewIdDelegate` guards neither of the two things its name asserts

`tests/Fulfillment.UnitTests/OrderDespatchTests.cs:99-115`. The test's own doc comment reads *"The fact's `EventId` and the advice's own id ARE the delegate's returned values, never independently minted"*. Its assertions are `Assert.NotEqual(default, advice.Id)` and `Assert.NotEqual(advice.Id, fact.EventId)` — both of which are satisfied by **any** two distinct GUIDs, however they were obtained. The `Queue` of two known ids is constructed and never compared against anything.

Two probes, each a one-token change in `src/Fulfillment/Domain/OrderDespatch.cs`:

| Probe | Mutation | Result |
|---|---|---|
| **P2** | `OrderDespatch.cs:93` — the fact's `eventId` argument `newId()` → `UniqueId.New()` | **103 / 103 PASSED** |
| **P3** | `OrderDespatch.cs:84` — the advice's `id` argument `newId()` → `UniqueId.New()` | **103 / 103 PASSED** |

Both halves of the claim survive their own deletion on a fully green suite.

**Why this matters enough to reject.** This is the *same* property as backlog id 49, in the feature that closes backlog id 49, in code written after that lesson was paid for. `design.md` §3.3's promise — *no ids beyond those `newId` supplies* — now holds in three places and is **asserted** in two: `Release` (armed, fires) and `DespatchAdvice.Create`'s use of its `eventId` parameter (`DespatchAdviceTests.cs:34`, fires). The one place with no assertion is the one layer that decides *which* ids are handed down, and it is the layer that regressed last time.

It is also a textbook instance of the failure `CLAUDE.md` names twice: the arming table (§4, row M1) cites this test as killed, and it is — but by **deleting the emission**, which kills it for an unrelated reason. *"A tick is not evidence the assertion exists"*, and neither is a kill by the wrong mutation family. The property was never armed.

**Required change:** make the assertions match the name — dequeue two known ids and assert `advice.Id` and `fact.EventId` **equal** them, in order, exactly as `OrderStockReservationTests.Release_TheFactsEventId_IsTheOneTheNewIdDelegateReturned` already does. Then arm it: run P2 and P3 above and record both verbatim failures. Roughly six lines of test.

### D2 — claims of absence carried in prose, with no enumerating command and no output

`CLAUDE.md`'s rule, adopted eleven minutes before this feature started: *a claim of absence is reportable only as (a) the exact command that enumerates the candidate set, (b) its complete output, (c) one classification line per hit.* This is the first report written under it, and it carries at least four bare-prose absences:

| Report | Claim | Evidence given |
|---|---|---|
| §58 | *"**No third pattern was introduced.** … `Consume`'s facts (via `DespatchAdvice`) never call `UniqueId.New()` directly anywhere."* | none |
| §69 | *"no `MERGE`, no `IF NOT EXISTS … INSERT` anywhere in this file"* | none |
| §32 | *"Checked column-by-column against the shape needed before writing any code; confirmed sufficient."* | none |
| §48, §139 | *"No `src/Orders/` file was edited"*, *"No `.env.example` change, no new `PackageReference`, no `src/Contracts` change"* — *"confirmed by `git status --porcelain`"* | command named, **output not given** |

Every one of them is **true** — I ran the enumerations myself and they are in §4 below. That is not the point: the rule exists because three prose sweeps in three features were reported clear and disproved within minutes, and the artefact is what makes a miss visible as an unclassified line instead of invisible as a sentence. The enumerating command is one `grep` and it is the same artefact whether the answer is "clear" or "three hits".

**Required change:** replace those four claims with their commands and complete outputs. No code change.

---

## 3. Mutation probes — both families, run independently of the implementer's

Backups taken to the scratchpad (outside the repository), restores verified with `cmp` against the backup, `touch` on every restored file, `dotnet build --no-incremental` before the confirming green runs. **The three pre-mutation hashes I took independently match the implementer's recorded hashes byte for byte** — `DespatchAdvice.cs` `0b118525…`, `DespatchCreationService.cs` `e6724480…`, `OrderStockReservation.cs` `9516a4e9…` — which is independent confirmation that its own restores landed.

| # | Family | Mutation | Named test | Result |
|---|---|---|---|---|
| **P1** | deletion (id 49) | `OrderStockReservation.cs` — `EventId: newId()` → `EventId: UniqueId.New()` | `OrderStockReservationTests.Release_TheFactsEventId_IsTheOneTheNewIdDelegateReturned` | **FAILED** — `Assert.Equal() Failure: Values differ` at `OrderStockReservationTests.cs:53` (1 failed / 102 passed) |
| **P2** | deletion (identity) | `OrderDespatch.cs:93` — fact `eventId` no longer from the delegate | *none* | **103 / 103 passed — D1** |
| **P3** | deletion (identity) | `OrderDespatch.cs:84` — advice `id` no longer from the delegate | *none* | **103 / 103 passed — D1** |
| **P4** | suppression | `DespatchCreationService.cs:70` — the in-flight-race branch replies `created: true` | `DespatchCreationServiceTests.F8_InFlightRace_AConcurrentCommitter…` | **FAILED** — `Assert.False() Failure` (1 failed / 102 passed) |
| **P5** | **payload corruption, on the wire** | `StockFactPayloadMapper.cs` — `despatched.RetailerCode` → `"WRONG-RETAILER"` (row present, one field wrong) | `DespatchCreateTests.HappyPath_…AndEmitsExactlyOneOrderDespatchedV1CarryingTheDespatchedFields` | **FAILED** — `Assert.Equal() Failure: Strings differ` (1 failed / 4 passed, real MS-SQL + NATS + Kafka) |
| **P6** | **emission deletion, at the persistence boundary** | `EfCoreDespatchRepository.SaveAsync` — the outbox drain loop neutered, despatch rows still written | same test | **FAILED** — `Assert.Single() Failure: The collection was empty` (1 failed / 4 passed) |

P5 and P6 are deliberately at **different injection sites** from the implementer's M1/M2, which both attacked `DespatchAdvice.Create` in the domain. P6 proves the outbox drain is guarded end to end (a despatch that persists without its fact fails); P5 proves the guard **opens the payload** rather than counting the row — the exact defect that survived a whole review pass in feature 17. Both families hold on this feature's one fact-emitting branch, and the suppression branch (P4, plus the implementer's M3 on the F8 fast path) holds too.

**Restore evidence:** `cmp` reports IDENTICAL for all six mutated files against their backups; `dotnet build OrderToCash.sln --no-incremental` succeeded with 0 warnings; then 103/103, 16/16 and **53/53** (the whole Fulfillment integration suite, not the filtered class). Nothing is left armed. `git status --porcelain` returns the same 37 entries it did at the start of the review.

---

## 4. Enumerations (the absences D2 asks the report to carry)

**E1 — every file this feature touches.** `git status --porcelain`, complete output: 21 modified + 16 untracked = 37 lines. Classification: **0** under `src/Orders/`, **0** under `src/Contracts/`, **0** under `src/SharedKernel/`, **0** under `src/Cqrs/`, **0** `.csproj`, **0** `.env.example`, **0** `infra/`, **0** migration files. Under `specs/`: exactly one, `specs/shared/test-matrix.md`. Under `feature_list.json`: one line before this review, two after.

**E2 — every direct id mint in the service.** `grep -rn "UniqueId\.New" src/Fulfillment/ --include=*.cs` — 4 hits:

- `Application/DespatchCreationService.cs:94` — passes `UniqueId.New` **as the delegate**. Correct.
- `Application/StockReservationService.cs:53` — same, for `Reserve`. Correct.
- `Application/StockReservationService.cs:109` — same, for `Release`. **The id-49 fix.**
- `Domain/OrderStockReservation.cs:208` — inside a doc comment. Not code.

Zero calls to `UniqueId.New()` inside any `Domain/` method body. **The report's §58 claim is true, and this is what makes it checkable.** `grep -rn "Guid\.NewGuid" src/Fulfillment/` — 3 hits: `EfCoreDespatchRepository.cs:77` (a `despatch_items` surrogate row key, the precedent `OutboxWriter.cs:52` already sets), `OutboxWriter.cs:52`, and one doc comment. No fourth event-id pattern exists.

**E3 — the L5 idiom.** `grep -rniE "IF NOT EXISTS|MERGE " src/Fulfillment/ --include=*.cs` — 1 hit, `EfCoreDespatchNumberAllocator.cs:13`, inside the doc comment that explains why the **fixed** idiom is used instead. `grep -rn "WHERE NOT EXISTS" src/Fulfillment/` — 2 hits, `EfCoreDespatchNumberAllocator.cs:15` (doc) and `:38` (the single atomic `INSERT … SELECT … WHERE NOT EXISTS (… WITH (UPDLOCK, HOLDLOCK) …)`). **No check-then-act rendering exists in this service.**

**E4 — the banned `CONFLICT` code.** `grep -rn '"CONFLICT"' src/ --include=*.cs` — 2 hits, both in `src/Orders` and both *classification sets* (`OrdersCreateErrorMapper.cs:31`, `NatsSagaCommandsAdapter.cs:152`), neither a construction. **Nothing in Fulfillment produces `CONFLICT`** — the report's §39 claim, now enumerated.

**E5 — spec-reuse fidelity.** `diff -rq ../order-to-cash-nestjs/specs/shared specs/shared` — 1 line of output, `test-matrix.md` differs. C7 box 1 verified against the #7 checkout, not from memory.

---

## 5. The ported-idiom ledger, claim by claim

This is the first feature to **extend** ledgered code rather than author it, so I checked `specs/fulfillment_stock/design.md` §15's twelve properties against what the new code actually does, rather than checking that a ledger section exists.

| Property | New code's exposure | Claim | Verdict |
|---|---|---|---|
| **L1** blocking current read | `DespatchCreationService` takes `LockForOrderAsync` before every decision | "not newly supplied — calls feature 17's method verbatim" | **Holds.** The method is unmodified (`git status`: `IStockItemRepository.cs` and `EfCoreStockItemRepository.cs` are not in the change set) and `LockForOrderAsync` is the first statement inside the transaction, before any branch. |
| **L1**, second face | The **un-hinted** `FindByOrderReferenceAsync` at `DespatchCreationService.cs:67`, inside the transaction, decides `created: false` vs `ConcurrentDespatchChangeError` | the ledger table does not cover it; the code comment says "guaranteed current, since we hold the same stock-row lock it held" | **Correct, but by a two-step argument the ledger omits — see A2.** Verified: `EfCoreUnitOfWork.cs:30` opens `IsolationLevel.ReadCommitted` explicitly, and RCSI is ON for every database, so the read is a **statement-scoped** snapshot taken *after* the lock was granted, hence after the racing writer committed. Under a transaction-scoped snapshot it would be stale. |
| **L2** deterministic lock order | none introduced | "locks the SAME rows through the SAME method, so no new multi-row statement exists" | **Holds.** No new locking SQL anywhere in the change set; the only new hints are the allocator's. |
| **L3** collation vs sort order | `locked.ExistingReservationsOfOrder … Distinct(StringComparer.OrdinalIgnoreCase) … ItemsByProductCode.TryGetValue` | not claimed | **Holds, and the dangerous half is right.** `StockLockResult.ItemsByProductCode` is documented and constructed with `StringComparer.OrdinalIgnoreCase` keys, so a reservation spelling a code in different case still resolves to the row MS-SQL's CI collation resolved it to. Had it been an ordinal dictionary, the mismatch would surface as `ConcurrentDespatchChangeError` → `UNAVAILABLE` → a saga retry loop. |
| **L4** counters cannot wrap | no new arithmetic — `StockItem.Consume` (feature 17) does the subtraction, `Quantity` carries units | not claimed | **Holds.** No new counter arithmetic exists in the diff. |
| **L5** insert-or-leave-alone | `EfCoreDespatchNumberAllocator` | "copied verbatim from the **fixed** `EfCoreOrderNumberAllocator`, table/column names substituted only" | **Holds — read line by line.** Same derived-table `MAX(CAST(SUBSTRING(...) AS int))+1` seed, same single-statement `INSERT … SELECT … WHERE NOT EXISTS (… WITH (UPDLOCK, HOLDLOCK) …)`, same `WITH (UPDLOCK, ROWLOCK)` claiming read. Not check-then-act (E3). |
| **L5**, despatch rows | `EfCoreDespatchRepository.SaveAsync` | "not needed, plain `Add`, backstopped by `uq_despatches_order_reference`" | **Holds.** Plain `Add`; the unique index predates this feature and its test is unchanged and green. |
| **L7** `CONFLICT` is banned | two new error mappings | `PRECONDITION_FAILED` and `UNAVAILABLE` | **Holds** (E4), and both are in the mapper's existing exhaustive-classification test. |
| **L8** publication order | `EfCoreDespatchRepository.InsertOutboxRowAsync` | "one awaited `ExecuteSqlInterpolatedAsync` per row, never `AddRange`" | **Holds** — read the method; and P6 shows the drain is load-bearing. |
| L6, L9, L10, L11, L12 | inherited unchanged | — | L11 extended correctly (`ValidateDespatchCreate` + 2 unit cases); the rest untouched. |

**The ledger's claims are accurate.** The one gap is what it does not say (A2), not what it says wrongly — which, on a first outing against *extended* rather than authored code, is a better result than the class's 0-for-3 history would predict.

---

## 6. Acceptance bullets and `R36` → test mapping

| Claim | Test | Verified how |
|---|---|---|
| **Bullet 1** — reservations move to `consumed` | `DespatchCreateTests.HappyPath_…` `Assert.Equal("consumed", Assert.Single(reservations).Status)` + on-hand `Units` 10→6 and `ReservedUnits`→0 | ran it (real MS-SQL) |
| **Bullet 2** — `OrderDespatched` emitted via outbox | same test, `OutboxRowsForAsync(correlationId, "order.despatched.v1")` → `Assert.Single`, then the payload's six fields | ran it; **P6** proves absence fails it; **P5** proves a wrong field fails it |
| **R36** consume + exactly one despatch + exactly one fact | `OrderDespatchTests` (5), `DespatchAdviceTests` (2), `DespatchCreationServiceTests` (5), `DespatchCreateTests` (5) | ran all 17; probes P2–P6 |
| **F6** ≥ 1 line | `DespatchAdviceTests.Create_F6_RefusesAnEmptyLineListAndCreatesNoAggregate` — asserts the stable code `EMPTY_DESPATCH_LINES` | read + ran |
| **F7** 1:1 line↔consumed reservation, units equal | `OrderDespatchTests.Create_ConsumesEveryReserved…` (`Assert.Collection` over two items) and the integration payload assertion | read + ran |
| **F8** at most one per `orderReference` | fast path (`DespatchCreationServiceTests.F8_FastPath_…`, armed by the implementer's M3), in-flight race (armed by **P4**), integration repeat (`F8_AReissued…` asserts `created: false`, same reference, **no second fact**), DB backstop (`UniqueConstraintTests.Despatches_Rejects_A_Duplicate_OrderReference`, pre-existing, green) | read + ran |
| **FS3** header discipline | `StockResponderHeaderTests` (+2 `[InlineData]` rows) and `DespatchCreateTests.FS3_…` | read + ran |
| Wire shape | `StockRpcPayloadTests` (+2, keys and null-omission), `StockSubjectsTests` (+1, reads `asyncapi.yaml` as text) | read + ran; independently diffed both payload records against `asyncapi.yaml` §`DespatchCreateRequestPayload` / `DespatchCreateReplyPayload` / `OrderDespatchedPayload` — field names, order, optionality and `required` lists all agree |
| **Backlog 49** | `OrderStockReservationTests.Release_TheFactsEventId_IsTheOneTheNewIdDelegateReturned` | **armed by me (P1), fires** |

Kafka/NATS classification is correct: `fulfillment.despatch.create` is a saga **command** on the `rpcTransport` server in `asyncapi.yaml` and is served over NATS request/reply; `order.despatched.v1` is a **fact** and leaves only through the outbox to Kafka. No Kafka-as-request-bus, no RPC-for-facts.

Adding the sixth subject to `StockRpcResponder` rather than creating a second responder class is **correct** and I would have rejected the opposite. "One `BackgroundService` per transport" forbids multiplexing *different* transports through one class; this is the same NATS transport, in the same process, with the same per-request scope and error mapping. The class doc comment now says so.

---

## 7. Advisories (not blocking)

**A1 — `Reserve`'s two facts have the same unguarded property D1 names, and it is pre-existing.** While arming P1 my `sed` matched all three `EventId: newId(),` sites in `OrderStockReservation.cs` (161 = `StockRejected`, 185 = `StockReserved`, 234 = `StockReleased`). Only **one** test failed. So the delegate-sourced-id property is asserted for `Release` and for nothing else in that file — `StockReserved` and `StockRejected` can both be minted directly on a green suite. Not this feature's defect and not in its scope; **backlog wording**: *"`OrderStockReservationTests` pins the `newId` delegate for the released fact only; the reserved and rejected facts' `EventId`s survive being minted with `UniqueId.New()` on a green suite. Same class as id 49, same one-line-per-test fix."* Fixing D1 in the same pass would make it natural to close this too.

**A2 — the ledger should name the un-hinted in-transaction re-read.** `DespatchCreationService.cs:67` is a decision-bearing read that takes **no lock hint**, and it is correct only because (i) it is issued after `LockForOrderAsync` was granted and (ii) `EfCoreUnitOfWork` opens `IsolationLevel.ReadCommitted` under RCSI, which makes the snapshot **statement**-scoped. That is exactly the shape §15's L1 row exists to interrogate, and the ledger table does not have a row for it — the reasoning lives only in a code comment. This matters for #9: PostgreSQL `READ COMMITTED` behaves the same way, but a `REPEATABLE READ` transaction would return a stale snapshot and turn a correct idempotent repeat into a permanent `UNAVAILABLE`. One ledger line, in the report or in `specs/fulfillment_stock/design.md` §15, costs nothing now and is expensive to rediscover.

**A3 — `test-matrix.md`'s `R36` row is marked `DONE` while one of the test cases it names is unrealised, and the deviation is missing from the report's own deviations section.** The row prescribes `fulfillment/integration/despatch-create.spec` › *concurrency against a simultaneous `stock.release`*; the Status cell says it was "not separately probed" and argues the shared lock protocol plus FS6/FS7 cover the mechanism. **The argument is sound** — the path re-decides under the same lock and I traced it — but two things are off. First, the immediately adjacent `R61` row's precedent for exactly this situation is the label `DOMAIN UNIT HALF DONE`, not `DONE`; a reused `R<n>` is a claim about behaviour (C7), so a partial realisation should be labelled partial. Second, `progress/impl_fulfillment_despatch.md` §7 "Deviations / open points" lists three deviations and not this one — the disclosure exists only inside the spec file it modifies. **Either add the case or relabel the row and list the deviation in the report.**

**A4 — the fact's `despatchDate` is asserted nowhere on the wire.** `DespatchCreateTests.HappyPath_…` opens the payload and checks `orderReference`, `despatchReference`, `companyCode`, `retailerCode` and `lines[].{productCode,units}` — five of six required fields. `despatchDate` is `required` in `asyncapi.yaml` and is the one field sourced from the clock rather than from the request or the reservations. A wrong `despatchDate` on the wire would survive. Cheap to add while D1 is being fixed.

**A5 — `progress/current.md` says `**Status:** in_progress` while `feature_list.json` said `in_review`.** `init.sh` reports lockstep because it does not compare the status word. Cosmetic, the leader's file, noted only because this repository's recurring finding is checks whose invariants are satisfied by an incorrect state.

---

## 8. `CHECKPOINTS.md` walk

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all present — `init.sh` §2.
- [x] `progress/current.md` and `progress/history.md` present.
- [x] `.claude/agents/` holds 6 definitions, every one declaring or documenting its model — `init.sh` §2.
- [x] `./init.sh` exits 0, before and after my edit.

### C2 — state is coherent
- [x] Exactly one feature `in_progress` (`fulfillment_despatch`, set back by this review).
- [x] Every status in `rules.valid_status` — `init.sh`.
- [x] Every `done` feature has passing tests — id 49's is armed and fires (P1).
- [x] `progress/current.md` describes the active session (see A5 for the stale status word).
- [x] No `blocked` feature.

### C3 — architecture is respected
- [x] No banned framework reference in any `Domain/` folder — `Architecture.Tests` **16/16 run**, not eyeballed.
- [x] No cross-service DB access — the change set touches one service's schema; `despatch.create` reaches Orders' data never, only its own reservations.
- [x] No shared runtime code beyond `SharedKernel` / `Contracts` / `Cqrs` — no `.csproj` added or edited (E1).
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` — architecture suite; the new `Domain/` files reference only `SharedKernel` and `Contracts.Facts` (the `DespatchLine` payload record, the precedent `StockReserved` sets).
- [x] `src/SharedKernel` still has zero `PackageReference`.
- [x] No `decimal` in domain arithmetic — none in the diff; this service handles no money.
- [x] Every interaction classifiable — NATS RPC for `despatch.create`, Kafka fact for `order.despatched.v1`. §6.
- [x] No stray debug logging, no context-free TODOs — read every new file.

### C4 — verification is real
- [x] Domain tests are pure — `DespatchAdviceTests` and `OrderDespatchTests` use xUnit and domain types only; no DB, no broker, no infrastructure mock.
- [x] Integration tests use Testcontainers against real MS-SQL + NATS + Kafka — `DespatchCreateTests` drives the real `StockRpcResponder` over a real NATS connection and reads real rows; no mocked broker.
- [x] No Jest.
- [ ] `./quality.sh` passes — **not re-run by this review, deliberately.** The implementer's run is on record at exit 0 across twelve projects; I verified 103 + 16 + 53 directly and a clean `--no-incremental` solution build.
- [ ] Coverage thresholds — standing gap, not this feature's: the coverlet gate is feature 34 and has not landed.

### C5 — the session closed cleanly
- [x] No suspicious untracked files — 16 untracked, all intended source/test/report files (E1). My probe backups are in the scratchpad, outside the repository.
- [ ] `progress/history.md` entry for id 18 with effort record — **correctly absent**: the feature is not closing. To be written by the review that approves it. Id 49's entry **is** written by this review.
- [x] `feature_list.json` reflects the true state — id 18 → `in_progress`, id 49 → `done`; two single-line edits, `git diff` read and confirmed to contain nothing else.
- [ ] The human has been told what was done and how to test it manually — the leader's step, after re-review.
- [x] Claude did not commit.

### C6 — SDD
Not applicable to id 18 (`sdd: false`) or id 49 (`sdd: false`). No `specs/<name>/` is required for either; the specification of record is the acceptance list, and `specs/shared/` is where R36 lives. The one `specs/` file modified is `test-matrix.md`'s Status column plus its derived count rows — established practice, and see A3 for the labelling point.

### C7 — spec-reuse fidelity
- [x] `specs/shared/` byte-identical to #7 except `test-matrix.md` — **real `diff -rq` against the #7 checkout** (E5), one differing file.
- [x] No silent fork — no amendment made or needed by this feature.
- [x] The `R<n>` ids are #7's — `R36` reused, and R36's EARS text is genuinely satisfied (§6). See **A3** for the row's `DONE` label versus one unrealised named case.
- [ ] n8n workflows fire green — not this feature's surface; standing.
- [ ] Black-box API script parity — standing; no Gateway surface for `despatch.create` (the saga calls it).
- [ ] `progress/history.md` effort records complete — id 49's written below; id 18's owed at approval.
- [ ] README benchmark section — standing, phase-close work.

---

## 9. What must change before re-review

1. **D1** — make `OrderDespatchTests.Create_TheAdvicesIdAndTheFactsEventId_AreBothMintedByTheNewIdDelegate` assert what its name says: both ids **equal** the delegate's returned values, in order. Arm it with both mutations (`OrderDespatch.cs:84` and `:93` → `UniqueId.New()`) and record the two verbatim failures in `progress/impl_fulfillment_despatch.md`.
2. **D2** — replace the four prose absences (§58, §69, §32, §48/§139) with their enumerating commands and complete outputs.
3. **A3** — either add the `despatch.create` vs `stock.release` concurrency integration case, or relabel `test-matrix.md`'s `R36` Status to a partial in `R61`'s style **and** list the deviation in the report's §7.
4. Optional but cheap while the file is open: **A4** (assert `despatchDate` on the wire) and **A1** (extend the id guard to `Reserve`'s two facts, closing an advisory instead of filing it).

Nothing here touches the lock protocol, the outbox path, the responder or the allocator. On re-review I will re-arm D1's two mutations and re-run `Fulfillment.UnitTests`; the integration suite need not be re-run unless §1's files change.

---

## 10. Phase-9 closing assessment — **not written**

The brief asks for one on approval. Feature 18 is not approved, so the phase is not closed and the assessment would be premature. It is owed by the review that approves this feature, alongside id 18's effort record.

---

# Review — `fulfillment_despatch` (feature 18, phase 9) — **round 2**

> Round 1 above is unchanged and not reopened. This section records only what round 2 ran, found and decided.

**Verdict: APPROVED.** D1 is fixed and I killed it myself with three mutations, two of them the ones round 1 named and one round 1 did not think of. D2's four absence claims are now command + complete output + classification, and the one I re-ran reproduces exactly. A1 was closed rather than filed and both new guards fire. A3's concurrency case is a **real** guard, not a probabilistic one — I armed it by stripping the lock hints and it failed on the first iteration of three consecutive runs. A4's `despatchDate` assertion fires when the field is corrupted on the wire. Two advisories carried forward, one of them a backlog entry the leader still owes.

---

## R2.1 — What I ran, and what I did not

| Ran | Result |
|---|---|
| `dotnet test tests/Fulfillment.UnitTests` (baseline, before any probe) | **105 / 105** |
| Eight independent mutation probes (R2.2–R2.5), each restored, `touch`ed and force-rebuilt | see below |
| `dotnet test tests/Fulfillment.IntegrationTests --filter …Concurrency…` — baseline, then **3 armed runs**, real MS-SQL + NATS + Kafka | 1/1 green, then **3 × FAILED** |
| `dotnet test tests/Fulfillment.IntegrationTests --filter …HappyPath` armed on `despatchDate` | **FAILED** |
| `dotnet build OrderToCash.sln --no-incremental` after each restore batch | Build succeeded, **0 warnings, 0 errors** (twice) |
| `dotnet test tests/Fulfillment.IntegrationTests` **whole suite** (confirming) | **54 / 54**, 3 m 18 s |
| `dotnet test tests/Fulfillment.UnitTests` (confirming) | **105 / 105** |
| `dotnet test tests/Architecture.Tests` (confirming — C3, run not eyeballed) | **16 / 16** |
| `dotnet format OrderToCash.sln --verify-no-changes` | **exit 0** |
| `./init.sh` | exit 0, coherent, 52 features, **0 `in_progress`**, tripwire clean |
| `diff -rq ../order-to-cash-nestjs/specs/shared specs/shared` | one differing file: `test-matrix.md` (C7 box 1) |
| Re-ran three of D2's four enumerating commands | outputs reproduce exactly (R2.5) |
| `git diff feature_list.json` | exactly two changed lines (id 18 status, id 49 `done`) — nothing else |

**Not re-run:** `./quality.sh` end to end and the eight test projects outside Fulfillment. The claim under test in this round is about `tests/Fulfillment.*`, `src/Fulfillment/` and four report claims; nothing in the fix round touches anything else (`git status --porcelain`: 0 lines under `src/Orders/`, `src/Contracts/`, `src/SharedKernel/`, `src/Cqrs/`, 0 `.csproj`). I did run the two parts of `quality.sh` a fix round can plausibly break on its own — the solution-wide `dotnet format --verify-no-changes` and a clean `--no-incremental` build — plus 105 + 54 + 16 directly.

**Every file I mutated is restored and hash-verified** against a scratchpad backup taken before the first probe, and the three source hashes match the values both the implementer and round 1 recorded: `OrderDespatch.cs` `5374af01…`, `OrderStockReservation.cs` `9516a4e9…`, `EfCoreStockItemRepository.cs` `bc1d9c59…`, `StockFactPayloadMapper.cs` restored `cmp`-IDENTICAL. `grep -c UPDLOCK` on the stock repository returns **3**, its original count.

---

## R2.2 — D1's fix, probed with three mutations

`tests/Fulfillment.UnitTests/OrderDespatchTests.cs:115-116` now asserts `Assert.Equal(expectedAdviceId, advice.Id)` and `Assert.Equal(expectedFactEventId, ((OrderDespatched)fact).EventId)` against two ids the test itself queued. Round 1's rejection was that a guard had been killed by the wrong mutation family, so accepting the implementer's recorded kill would have repeated the error one level up. I ran them.

| Probe | Mutation (`src/Fulfillment/Domain/OrderDespatch.cs`) | Result | Message |
|---|---|---|---|
| **R2-P2** | line 93 — the fact's `eventId` argument `newId()` → `UniqueId.New()` | **FAILED**, 104/105 | `Assert.Equal() Failure: Values differ` at `OrderDespatchTests.cs:line 116` |
| **R2-P3** | line 84 — the advice's `id` argument `newId()` → `UniqueId.New()` | **FAILED**, 104/105 | `Assert.Equal() Failure: Values differ` at `OrderDespatchTests.cs:line 115` |
| **R2-P7** | **new family — provenance swap.** Hoisted both mints (`var idA = newId(); var idB = newId();`) and passed them **crossed**: `idB` as the advice id, `idA` as the fact's `eventId`. Nothing is deleted, nothing is externally minted, both ids still come from the delegate, and both are still distinct | **FAILED**, 104/105 | `Assert.Equal() Failure: Values differ` |

R2-P2 and R2-P3 fail at **different line numbers**, which is the evidence that the two halves are pinned separately rather than one assertion carrying both. R2-P7 is the probe round 1 did not specify and it is the one that decides whether the fix is real: a test that merely dequeued two known ids into a `HashSet` and checked membership would survive it. This one does not. **D1 is closed.**

---

## R2.3 — A1's extension: verified, and it did not weaken the original

The implementer closed A1 instead of filing it, adding two tests to `OrderStockReservationTests.cs`. `OrderStockReservation.cs` has **four** delegate call sites; I probed all four, one at a time, restoring between each.

| Probe | Mutation | Named test killed | Result |
|---|---|---|---|
| R2-P8 | line 161 — `StockRejected.EventId` → `UniqueId.New()` | `Reserve_TheRejectedFactsEventId_IsTheOneTheNewIdDelegateReturned` | **FAILED**, 104/105 |
| R2-P9 | line 185 — `StockReserved.EventId` → `UniqueId.New()` | `Reserve_TheReservedFactsEventId_IsTheOneTheNewIdDelegateReturnedLast` | **FAILED**, 104/105 |
| R2-P10 | line 234 — `StockReleased.EventId` → `UniqueId.New()` | `Release_TheFactsEventId_IsTheOneTheNewIdDelegateReturned` | **FAILED**, 104/105 |
| R2-P11 | line 181 — the **reservation line's own** id, `item.Reserve(newId(), …)` → `UniqueId.New()` | `Reserve_TheReservedFactsEventId_IsTheOneTheNewIdDelegateReturnedLast` | **FAILED**, 104/105 |

Three points worth recording. **First, each mutation kills exactly one test** — the guards are per-fact, not one blanket assertion that happens to cover three sites. **Second, R2-P10 proves the extension did not weaken the original**: the id-49 guard round 1 armed still fires, unchanged, and `git diff` on the test file shows **79 insertions / 3 deletions**, the three deletions being the `Release(...)` call-site updates the signature change forced — no assertion was removed or relaxed. **Third, R2-P11 was not asked for by anyone**: the two-value queue in the reserved-fact test pins the *ordering* of the delegate's returns, so bypassing the delegate for the reservation line displaces the fact's id too. All four mint sites in that file are now held; round 1's A1 said one of three was, so the count went from 1/4 to 4/4.

---

## R2.4 — A3's concurrency probe: judged by arming it, not by counting green runs

The brief asked whether the new race test's signal is a change in **kind** or merely in **likelihood**, and whether five green runs are evidence. **Five green runs are evidence of nothing** — green is the expected outcome both when the guard is live and when the two requests never actually overlap, which is precisely the mutation-vehicle-that-went-quiet shape this build has been bitten by twice. Neither the implementer nor round 1 armed this test, so I did.

**Mutation:** stripped all three lock hints from `src/Fulfillment/Infrastructure/Persistence/EfCoreStockItemRepository.cs` — `dbo.stock WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` at lines 51 and 98 and `dbo.reservations WITH (UPDLOCK, HOLDLOCK)` at line 68 — leaving every other line, including the transaction and the isolation level, untouched. Under RCSI this turns the decision read into a non-blocking snapshot, which is exactly the defect the hints exist to prevent.

**Result: FAILED on all three consecutive runs, and on iteration 0 of 10 every time.** Verbatim, from run 1:

```
exactly one of despatch.create / stock.release must win the race for ORD-900000
(despatch reply: {"orderReference":"ORD-900000","despatchReference":"DES-000001","despatchDate":"…","created":true,"lines":[{"productCode":"RACE-DESP-0","units":4}]},
 release reply:  {"outcome":"released","orderReference":"ORD-900000","released":[{"reservationId":"…","productCode":"RACE-DESP-0","units":4}]})
```

Both sides reported success on the same reservation — the double-spend the lock protocol exists to make impossible — and the XOR assertion caught it. **So the signal is a change in kind, not in likelihood:** the failing observation is "two successful replies for one reservation", a state the correct code cannot produce at all, rather than "a rare interleaving happened to be sampled". The empirical rate matters too and it is not marginal: 3 of 3 runs failed at the **first** iteration, so the overlap is reliable rather than lucky, and the ten iterations are belt-and-braces on top of a signal that arrives immediately. Restored, `cmp`-IDENTICAL, `--no-incremental` rebuild, whole suite **54/54**.

**On the implementer's report of the defect it caught in its own first draft:** I accept it, and it is the right kind of evidence — the draft assumed the losing `stock.release` would answer F5's `already_released` no-op, and the real system answered `PRECONDITION_FAILED` because `StockItem.Release` refuses a *consumed* reservation as a terminal state (F4/FS10). I traced that path in the source and it is what the code does. That is a test that was corrected by the system rather than the system being bent to the test, which is the direction that counts.

One residual note, non-blocking and not filed: the winning side's "exactly one fact" is read through `WaitForAsync(rows.Count > 0)` and then `Assert.Single`, so in principle the snapshot is taken at the first row rather than after things settle. It is not a real gap here — the despatch row and its outbox row commit in one transaction, so a duplicate could not appear later — but the pattern is worth not copying into Billing, where two facts can legitimately be written by two different commits.

---

## R2.5 — D2's redone absence claims: one spot-check became three

The rule is that the command must be re-runnable and the output checkable, so I re-ran them rather than reading them.

**§58** — `grep -rn "UniqueId\.New(" src/Fulfillment/Domain/ --include=*.cs` → **1 line**, `OrderStockReservation.cs:208`, inside a `///` doc comment. Byte-identical to the output in the report. Classification correct.

**§69** — `grep -rniE "MERGE |IF NOT EXISTS" src/Fulfillment/Infrastructure/Persistence/EfCoreDespatchRepository.cs` → no output, **exit 1**. Reproduces exactly.

**§48/§139** — `git status --porcelain | grep -cE "src/Orders/|src/Contracts/|\.csproj|\.env\.example"` → **0**, against 39 total lines. Reproduces (the report's own snapshot has since grown by the two files the fix round added to the tree, which the report states).

I did not re-run §32's migration enumeration; it is the one claim of the four whose subject is a *process* ("checked column-by-column before writing code") rather than a repository fact, and the report says so itself and substitutes an enumerable proxy. That is the correct handling of an unenumerable claim and it is the first time in this build one has been labelled rather than asserted. **D2 is closed.**

---

## R2.6 — A4, verified by corrupting the field

`DespatchCreateTests.HappyPath_…` now brackets the RPC with `before`/`after` and asserts `Assert.InRange(factPayload.DespatchDate, before.AddSeconds(-1), after.AddSeconds(1))`. Probed: `StockFactPayloadMapper.cs:38`, `despatched.DespatchDate` → `despatched.DespatchDate.AddMinutes(3)` — the row still present, every other field correct, one clock-sourced field wrong on the wire.

**FAILED** — `Assert.InRange() Failure: Value not in range`. Restored, `cmp`-IDENTICAL, rebuilt. All six required fields of `OrderDespatchedPayload` are now asserted on the wire, and five of the six have been shown to fail when corrupted (round 1's P5 for `retailerCode`, the implementer's M2 for `lines[].units`, this round for `despatchDate`).

---

## R2.7 — Advisories carried out of round 2

**R2-A1 (was round 1's A2, still open) — the leader owes one backlog entry.** The implementer judged A2 a correct finding and declined to act, on the ground that `specs/fulfillment_stock/design.md` is outside a test-only fix round's scope. **I agree with the judgement and it is not a reason to hold the feature** — but the entry is not filed, and an advisory that lives only in two report files is how this class disappears. Wording, ready to paste, and it should be phase 10 (Billing copies this service's transaction shape):

> *`specs/fulfillment_stock/design.md` §15's ported-idiom ledger has no row for the un-hinted in-transaction re-read at `src/Fulfillment/Application/DespatchCreationService.cs:67` (the F8 in-flight-race decision). It is correct only because it is issued after `LockForOrderAsync` is granted **and** because `EfCoreUnitOfWork.cs:30` opens `IsolationLevel.ReadCommitted` under RCSI, making the snapshot statement-scoped; under a transaction-scoped snapshot (#9 on PostgreSQL `REPEATABLE READ`) the same code returns a stale read and turns a correct idempotent repeat into a permanent `UNAVAILABLE`. Add the row. Text is in `progress/review_fulfillment_despatch.md` §5 row "L1, second face".*

**R2-A2 — `progress/current.md` still says `**Status:** in_progress`** (round 1's A5, correctly declined as the leader's file). Cosmetic, and `init.sh` still reports lockstep because it does not compare the status word. Named again only because "a check whose invariants are satisfied by an incorrect state" is this repository's most-repeated finding.

---

## R2.8 — `CHECKPOINTS.md` walk (round 2)

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` present — `init.sh` §2.
- [x] `progress/current.md` and `progress/history.md` present.
- [x] `.claude/agents/` holds 6 definitions, each declaring its model — `init.sh` §2.
- [x] `./init.sh` exits 0, before and after my `feature_list.json` edit.

### C2 — state is coherent
- [x] At most one feature `in_progress` — **zero**, id 18 goes straight `in_review` → `done` by this review.
- [x] Every status in `rules.valid_status` — `init.sh`.
- [x] Every `done` feature has passing tests — id 18's 22 new cases run green and eight of them have been seen to fail; id 49's guard re-armed this round (R2-P10).
- [x] `progress/current.md` describes the active session — see R2-A2 for the stale status word.
- [x] No `blocked` feature.

### C3 — architecture is respected
- [x] No banned framework reference in any `Domain/` folder — `Architecture.Tests` **16/16 run**, not eyeballed.
- [x] No cross-service DB access — the change set touches one service's schema only; `despatch.create` reads Fulfillment's own reservations and never Orders' data.
- [x] No shared runtime code beyond `SharedKernel` / `Contracts` / `Cqrs` — 0 `.csproj` in the change set.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` — architecture suite.
- [x] `src/SharedKernel` still has zero `PackageReference`.
- [x] No `decimal` in domain arithmetic — none in the diff; this service handles no money.
- [x] Every interaction classifiable — `fulfillment.despatch.create` is a NATS RPC saga command, `order.despatched.v1` is a Kafka fact leaving only through the outbox. No Kafka-as-request-bus, no RPC-for-facts.
- [x] No stray debug logging, no context-free TODOs.

### C4 — verification is real
- [x] Domain tests are pure — `OrderDespatchTests`, `DespatchAdviceTests`, `OrderStockReservationTests` use xUnit and domain types only.
- [x] Integration tests hit real containers — `DespatchCreateTests` drives the real responder over real NATS against real MS-SQL and reads real rows; the new race case runs two real concurrent RPCs.
- [x] No Jest.
- [x] `./quality.sh` passes — **the implementer's run is the record** (exit 0, twelve projects). Round 2 verified its two fix-round-breakable parts directly: `dotnet format --verify-no-changes` **exit 0** at solution level and a clean `--no-incremental` solution build, plus 105 + 54 + 16 run here. Marked `[x]` on that basis, with the split stated.
- [ ] Coverage thresholds — standing gap, not this feature's: the coverlet gate is feature 34 and has not landed. Coverage is reported, not enforced.

### C5 — the session closed cleanly
- [x] No suspicious untracked files — every untracked path is an intended source/test/report file; my probe backups are in the scratchpad, outside the repository.
- [x] `progress/history.md` has an entry for id 18 **with its effort record** — written by this review, below.
- [x] `feature_list.json` reflects the true state — id 18 → `done`; `git diff` read, exactly the lines intended.
- [ ] The human has been told what was done and how to test it manually — the leader's step, next.
- [x] Claude did not commit.

### C6 — SDD
Not applicable: id 18 is `sdd: false`. The specification of record is its two acceptance bullets plus `specs/shared/` R36 / F6 / F7 / F8, and both bullets are met and guarded over real containers (round 1 §6, re-confirmed here). The only `specs/` file modified is `test-matrix.md`'s Status column — and round 1's A3 is now closed correctly: the `R36` row's named concurrency case exists as a real test rather than an argument, so the `DONE` label is earned rather than relabelled.

### C7 — spec-reuse fidelity
- [x] `specs/shared/` byte-identical to #7 except `test-matrix.md` — real `diff -rq` against the #7 checkout, one differing file.
- [x] No silent fork — no amendment made or needed.
- [x] The `R<n>` ids are #7's — `R36` reused and genuinely satisfied, now including the concurrency case #7's own matrix row names.
- [ ] n8n workflows fire green — standing, not this feature's surface.
- [ ] Black-box API script parity — standing; `despatch.create` has no Gateway surface, the saga calls it.
- [x] `progress/history.md` effort records complete and honest — id 18's written below, with its measurement caveats stated rather than smoothed.
- [ ] README benchmark section — standing, phase-close work for the leader.

---

## R2.9 — Verdict

**APPROVED.** `feature_list.json` id 18 → `done`. Phase 9 is complete; its closing assessment and id 18's effort record are appended to `progress/history.md`.

The leader owes one backlog entry (**R2-A1**) and one cosmetic status-word fix in `progress/current.md` (**R2-A2**). Neither blocks the close.

---

### R2.10 — Postscript: one action is owed before the session advances

`./init.sh` ran **exit 0** before the approval and **FAILS** after it, with exactly one new failure and it is the expected consequence of closing the last feature of the phase:

```
── 4. Session file in lockstep
[FAIL]  progress/current.md claims a feature while none is active:
        "**Feature:** `fulfillment_despatch` (id 18, phase 9) — the last of the phase"
```

`progress/current.md` is the leader's file and its own template says to reset it on session close; this review deliberately did not touch it. **Reset it to the template (or point it at Phase 10) and `init.sh` returns to exit 0.** Nothing else in the check is red: 52 features, 0 `in_progress`, 24 `done`, backlog tripwire clean, no superseded rule text.

Note that this is R2-A2 upgraded: round 1 recorded the stale status *word* as cosmetic because `init.sh` did not compare it. The *feature* line is compared, so the same file has now produced both a check that could not see a wrong state and a check that could — which is a small, live illustration of this repository's most-repeated finding, in the file that records the finding.
