# review_guard_hardening — ids 60, 61, 63, 64, 65

**Verdict: REJECTED.** One blocking defect, in **id 64**, and it is the loop's own subject reappearing inside the loop's own fix: the acceptance bullet that exists to make an uncovered record *visible* was discharged with a prose sentence, its completeness claim is false, and the record it swallowed is unguarded — demonstrated live, at 361/361 green.

Everything else in the loop is sound and independently reproved below. Because the five ride together, all five stay open: id 60 → `in_progress`, ids 61, 63, 64, 65 stay `pending`. The re-review list is short and is in §7.

**This does not close phase 13.** Id **66** (`operator_note_reaches_the_timeline`, the bullet `SA-2` made satisfiable) remains `pending` regardless of this verdict, and `SA-2` — applied byte-identically in both repositories — still owes a commit of its own.

---

## 1. The suite figure — resolved, off my own run

**No test disappeared. The true total is 1609, and the implementer's 1535 is a transcription error in one row of its own table.**

| | container-free | integration | total |
|---|---|---|---|
| Baseline before this loop (review of feature 56, my predecessor's own count) | 1226 | 349 | **1575** |
| **My run, after this loop, 2026-09-09** | **1251** | **358** | **1609** |
| Implementer's reported table | 1177 | 358 | 1535 |

**The whole 74-test gap is one cell.** The implementer's table records `Projector.UnitTests` as **31/31**; the project holds **105**. 31 is the count of the `ProjectorFactsConsumerTests` class alone — a filtered run recorded as a project total. My own run, twice, and again while that project's production code was mutated (`Failed: 15, Passed: 90, Total: 105`), puts it at 105. 1535 + 74 = 1609.

**And the arithmetic reconciles exactly against the baseline**, which is the check that proves nothing was lost rather than merely that two runs disagree:

- container-free 1226 → **1251**, exactly **+25**: Orders 351→361 (the 10-case `EachConsumedFact…` theory), Projector 91→105 (the 14-case `EachOfTheFourteenFacts…` theory), Gateway 204→205 (`ToOrderDetail_ReturnsTotals…`). No other project moved by a single test.
- integration 349 → **358**, exactly **+9**: 3 catalog-ordering tests, 3 `BillingResponderReadinessRaceTests`, 3 `FulfillmentResponderReadinessRaceTests`.
- **+34 added, 0 removed, 0 skipped.** `git status` shows no deleted test file.

My per-project run (`dotnet test <project> --no-build`, after `dotnet build OrderToCash.sln --no-incremental`):

```
Architecture.Tests 16 | Billing.UnitTests 232 | Contracts.UnitTests 21 | Cqrs.UnitTests 23
Fulfillment.UnitTests 124 | Gateway.UnitTests 205 | Notifications.UnitTests 70 | Orders.UnitTests 361
Projector.UnitTests 105 | Seed.UnitTests 44 | SharedKernel.UnitTests 50              => 1251
Seed.IT 6 | Notifications.IT 12 | Projector.IT 52 | Billing.IT 86 | Fulfillment.IT 59
Orders.IT 95 | Gateway.IT 48                                                          =>  358
```

All 18 projects: **1609 passed, 0 failed, 0 skipped.**

**This is itself a finding, non-blocking but not cosmetic.** The report saw the 40-test drop, wrote a paragraph about it, and closed with *"The count above is this session's own reading, not a reconciliation against that baseline."* A count regression noticed and explicitly declined is the guard-that-does-not-guard in the reporting layer: the number that would have exposed the bad cell was one subtraction away, in a loop whose entire subject is claims that cannot fail. `CLAUDE.md`'s *"a count is a reading"* applies to the row as much as to the total — 31 was read off a filtered run and recorded as something else.

---

## 2. The blocking defect

### D1 — id 64: the completeness enumeration is prose, its claim is false, and the omitted record is unguarded (BLOCKING)

**File:** `tests/Orders.UnitTests/SagaCommandPayloadTests.cs:176-190` (`RequestAndReplySchemas`), `progress/impl_guard_hardening.md:195-199`.

Id 64's third acceptance bullet is explicit about **form**: *"every payload record the theory is meant to cover is enumerated as a search result, with one classification line per hit, so a record with no case is visible as an unclassified line rather than absent."* What the report delivers is a sentence listing twelve names, ending *"every record declared in `src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs`, confirmed by reading that file in full; no record without a case."* No command, no output, no per-hit line — the exact prose-sweep form `CLAUDE.md` bans, and the exact form that has been reported clear and disproved three times in this build.

**The claim is false.** The enumerating command and its complete output:

```
$ grep -n "record " src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs
19:public sealed record StockReserveRequestLine(string ProductCode, int Units);
22:public sealed record StockReserveRequestPayload(
35:public sealed record StockReserveReplyPayload(
44:public sealed record StockReleaseRequestPayload(string OrderReference, string Reason);
47:public sealed record StockReleaseReplyPayload(
55:public sealed record DespatchCreateRequestPayload(string OrderReference);
58:public sealed record DespatchCreateReplyPayload(
77:public sealed record SagaMoney(long Amount, string Currency);
80:public sealed record CreditHoldRequestPayload(
92:public sealed record CreditHoldReplyPayload(
104:public sealed record InvoiceIssueRequestPayload(
113:public sealed record InvoiceIssueReplyPayload(
130:public sealed record CreditReleaseRequestPayload(string OrderReference, string RetailerCode, string CompanyCode);
137:public sealed record CreditReleaseReplyPayload(
```

**14 records declared, 12 with a case.** Two lines are unclassified, and they classify differently:

- `:19 StockReserveRequestLine` — **not applicable, and would have to be said**: `asyncapi.yaml:3283-3294` declares the `lines[]` item shape **inline**, with no named schema, so `AsyncApiSchema.PropertyNamesOf(...)` has nothing to look up. A legitimate "no case", invisible because nothing enumerated it.
- `:77 SagaMoney` — **a real gap**, and the one the bullet was written to catch. Its own doc comment names its schema: *"`asyncapi.yaml`'s `Money` schema (`{ amount, currency }`)"*. **Billing's `CreditRpcPayloadTests.cs:24` — the very form this entry ports, cited by the entry down to the line range — carries `{ "Money", typeof(CreditMoney) }`.** Orders' port dropped that row. Bullet 1 says port Billing's form; bullet 4 says *"if Billing's form does not transfer cleanly, say what differs"*. Neither happened; the row was simply not carried, and the prose sweep asserted it had been.

**Why it matters, proved rather than argued.** The identical mutation, both sides, `--no-incremental` rebuild each time:

| Mutation | Suite | Result |
|---|---|---|
| `SagaMoney(long Amount, string Currency, string? Note = null)` — a property `Money` does not declare, on the record with **no** row | `Orders.UnitTests` | **361 passed, 0 failed — GREEN** |
| `CreditMoney(long Amount, string Currency, string? Note = null)` — same mutation, on the record **with** the row | `Billing.UnitTests` | **FAILED** — `BC23_…(schemaName: "Money", payloadType: typeof(…CreditMoney))`, 231/232 |

Nulls are omitted on the wire, so no serialised-key assertion can see this; only the reflection-over-the-record row can, and Orders has not got one. The residual guard for `SagaMoney` is `SagaCommandPayloadTests.cs:99`'s `AssertKeys(json.RootElement.GetProperty("amount"), "amount", "currency")` — **a hand-typed key list**, which is the precise pattern id 64 exists to retire.

This is not a technicality about report formatting. The bullet demanded a form *because* prose hides exactly this, the report used prose, and prose hid exactly this.

---

## 3. Findings that do not block, but must be routed or recorded

### D2 — id 63: the 7th and 8th sites are paced and **unguarded**, proved (route to a numbered entry)

The implementer found two unpaced loops beyond id 63's six — `tests/Gateway.IntegrationTests/StandInResponder.cs:116` (whose own doc comment already *claimed* it was paced) and `tests/Gateway.IntegrationTests/FulfillmentStockEndToEndTests.cs:61` — paced both, and disclosed honestly that neither is armed.

**My ruling on the leader's three questions:**

1. **In scope — yes.** Id 63's own `notes` record that its enumeration *"named the sites known at filing time, not a searched set"*, and it was already widened once (four → six) for precisely this reason. Finding the 7th and 8th during the fix is the enumeration finally being done properly, and leaving them unpaced while pacing six would have been the worse outcome.
2. **Proved — no, and demonstrably not.** Enumeration of the guard population, path-excluded, content-based:

   ```
   $ find . -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
       | xargs -0 grep -ln "ADelayedSubscriberNoLongerLosesTheRace" | sort
   ./tests/Billing.IntegrationTests/BillingResponderReadinessRaceTests.cs
   ./tests/Fulfillment.IntegrationTests/FulfillmentResponderReadinessRaceTests.cs
   ./tests/Orders.IntegrationTests/OrdersCancelResponderReadinessRaceTests.cs
   ```

   `Gateway.IntegrationTests` has no race guard. I deleted **both** `Task.Delay` lines, rebuilt `--no-incremental`, and ran the whole assembly: **48 passed, 0 failed.** Restored (`cmp`-verified, rebuilt), re-ran: **48 passed.** So the two new pacings are correct code that nothing checks — id 63's own defect class, one file over.
3. **Does the population hold — yes, now.** My own enumeration, on the sentinel the loops retry rather than on a loop-header string: `grep -ln "NatsNoRespondersException"` over all `.cs` (path-excluded) returns 16 files; of these, three are production RPC clients (no retry loop), one is a unit test of exception mapping, three are the race-guard suites, and the remaining nine are the readiness loops — `SagaIntegrationTestSupport` (already paced and armed), `BillingHostFixture`, `FulfillmentHostFixture` (both paced + newly armed), the four Orders sites (now **delegating** to `SagaIntegrationTestSupport.WaitUntilReachableAsync`, verified by reading each call site), and the two Gateway sites (paced, unarmed). I also swept every loop header in `tests/` (`for (var attempt|for (var i|while (true)`): the only other unpaced-looking candidates are Kafka `Consume(TimeSpan)` loops, whose retried call genuinely costs wall-clock, and `GatewayTestHost.cs:64`, which is explicitly paced at `200ms × attempt`. **No unpaced NATS readiness loop remains.**

**Routing, per `CLAUDE.md`'s "never discharge a gap with *the next feature that touches X*":** this needs a numbered backlog entry of its own — *"the two `Gateway.IntegrationTests` readiness loops are paced by nothing that can fail"* — with acceptance requiring one shared paced helper in that assembly plus a `GatewayResponderReadinessRaceTests` of the same three-part shape, armed. It is **not** a reason to hold id 63, whose six named sites are all paced, and whose pacing I re-armed myself (§4). **I have not written it to `feature_list.json`** — the leader files it.

### A1 — id 61: the report chose *keep and guard* where the bullet said *delete*, and defended it

Bullet 4 reads: *"if any of the three carries no ordering requirement, the `.OrderBy` is DELETED rather than guarded."* The report answers that **none of the four** carries a written requirement, and keeps all three anyway — because `ListProductsAsync`'s `.OrderBy` was already kept and guarded by feature 40 on the same (absent) requirement, so deleting the other three would invent an asymmetry between four structurally identical collections that `openapi.yaml` and `asyncapi.yaml`'s `CatalogReferenceListReplyPayload` treat alike. That reasoning is sound, it is stated where the bullet asks for it, and the alternative — deleting products' too — would reopen an approved feature. **Accepted as a disclosed deviation, not a defect.**

### A2 — id 61: the three-row guards are falsifiable, but not *deterministically*

Arming all four deletions at once, my run produced **3 of 4 failures**: `ListRetailersAsync_…` passed, because with no `ORDER BY` the engine happened to return `A, B, C`. Seeding order is `C, A, B`, so it was not insertion-order luck — it was storage/plan-order luck, which three rows cannot exclude (one arrangement in six matches). Re-run in isolation, the same mutated build failed the retailers guard **3 times out of 3** (`["RETAILER-B","RETAILER-C","RETAILER-A"]`, `["RETAILER-C","RETAILER-A","RETAILER-B"]`, `["RETAILER-C","RETAILER-B","RETAILER-A"]`), so the guard does have teeth. Recorded because bullet 2 says *"cannot pass on insertion-order luck"* and the observed false-negative rate is not zero; the products guard approved in feature 40 has the identical property, so this is a class note rather than this loop's regression.

### A3 — the instrument the loop was asked for was not built

The brief asked for an instrument that interrogates **ledger rows** — *if this row's stated evidence were false, what would turn red, and has anyone seen it?* Nothing here does that. **No ledger row was owed by these five entries** (none ports a #7 mechanism; id 64 ports an intra-#8 form), so this is not a rule violation — but the ambition is unmet, five fixes landed as point fixes, and D1 is a reminder of why it was wanted: an enumeration that hides a missing row is precisely a claim whose falsity turns nothing red. If the leader still wants it, it needs its own entry; it will not arrive as a side effect of the next fix round.

---

## 4. What I verified myself — probes, all three families

Every mutation: `cp` backup → mutate → `dotnet build --no-incremental` → run → record → restore from backup → `cmp` → rebuild → confirm green. No `git checkout --` was used on anything. All six source files touched are tracked and `git status src/` is clean of them at close.

| # | Family | Mutation | Result |
|---|---|---|---|
| P1 | transposition (production) | `SagaFactsConsumer.cs:126-133` — swap `envelope.AggregateId` / `envelope.CorrelationId` into `SagaFact` | **10/10 theory cases FAIL**: `Assert.Equal() Failure: Values differ` — id 60's Orders site genuinely guards the hop, not just the fixture |
| P2 | substitution of a sibling field (production) | `ProjectorFactsConsumer.cs:110` — `envelope.CorrelationId` → `envelope.AggregateId` | **15 FAIL / 105**: the 14 new theory cases **and `PR37`**. PR37 is the point: before this loop its inline envelope gave both fields the same sentinel, so this mutation would have passed |
| P3 | transposition (mapper) | `OrderReadModelMapper.cs:84` — `InitialAmount`/`InitialDiscount` in `ToOrderDetail` | **exactly 1 FAIL / 205**: `ToOrderDetail_ReturnsTotals_WithAllThreeFieldsCarriedFromTheDocument`, `Expected: 124950 / Actual: 700` |
| P4 | deletion (the entry's own named mutation) | remove `long AvailableCreditAfter` from `CreditReleaseReplyPayload`, call sites adjusted | **exactly 1 FAIL / 361**: `BC23_…(schemaName: "CreditReleaseReplyPayload")`. The mutation that left 342/342 green now bites |
| P5 | **substitution** | swap two real schema-name literals in `RequestAndReplySchemas` (`StockReleaseRequestPayload` ↔ `DespatchCreateRequestPayload`) | **2 FAIL**, and the message names both the schema and the type — the false-negative caution is satisfied: the failure says what I broke |
| P6 | **substitution → the gap** | `SagaMoney` gains a property `Money` does not declare | **361/361 GREEN** — D1 |
| P7 | control for P6 | same mutation on Billing's `CreditMoney` (the ported form's own row) | **FAILS** at `schemaName: "Money"` — proves the dropped row, not the mutation, is what differs |
| P8 | deletion | delete **all four** `.OrderBy` in `EfCoreOrderReferenceCatalog` | **3 FAIL / 11** (products, companies, currencies) — retailers passed once by storage-order luck, then failed 3/3 in isolation (A2) |
| P9 | deletion | delete the `Task.Delay` from `FulfillmentHostFixture.WaitUntilReachableAsync` | **FAIL in 302 ms**: `System.TimeoutException : 'stock.check.readiness-race-repro.…' never became reachable` — a change of **kind** against a 300 ms-delayed subscriber, exactly as the entry demands |
| P10 | deletion | delete **both** `Task.Delay`s in `Gateway.IntegrationTests` | **48/48 GREEN** — D2 |

**Independent enumeration for id 60, at the granularity the claim is about.** The report's command was `grep -l "Envelope<"` — a *file* list for a claim about *fixtures*. Its table happens to classify every construction site, so nothing was hidden; I verified that mechanically rather than by re-reading. A parser over every `.cs` in `tests/` extracted **42** seven-argument `Envelope` construction sites (including the two target-typed `=> new(...)` builders a `new Envelope<` grep misses) and compared the *expressions* in the `eventId`/`aggregateId`/`correlationId`/`causationId` positions. Genuine same-variable collisions: **16**, all of them the `aggregateId == correlationId` pair — `StreamProjectorEndToEndTests.cs:133` (`orderId`), `StandInSagaResponders.cs:215` (`correlationId`), and the 14 `EnvelopeBuilders` (`correlation`). Every one is disclosed in the report with its field pair and the `saga.md:23` / `saga.md:346` reason (`correlationId = orderId`, and the order id *is* the aggregate id). **No undisclosed collision anywhere.** Textual repeats of `Guid.NewGuid()` are independent calls and are not collisions.

**Line-citation item:** done, and the residue swept — `grep -nE '\(`?:[0-9]{2,5}`?\)'` over all `.cs`, path-excluded, returns **no hits**.

**Not re-run by me, and why.** `./quality.sh` end to end. No claim in this loop is about coverage: no `src/` file's shipped behaviour changed (I re-verified — `git status src/` shows only feature 56's already-approved files), and 34 tests were added, so neither gate can move down. I ran its other steps instead: `dotnet build OrderToCash.sln --no-incremental` (0 warnings, 0 errors), `dotnet format OrderToCash.sln --verify-no-changes` (exit 0), `./init.sh` (exit 0, §5d shared-spec parity intact across 6 files), and all 18 test projects individually.

---

## 5. CHECKPOINTS.md

- **C1** — [x] harness complete; [x] `progress/` files present; [x] five agent definitions with models; [x] `./init.sh` exits 0.
- **C2** — [x] one feature `in_progress` after my transition (id 60); [x] all statuses valid; [x] every `done` feature has passing tests (1609/0/0); [ ] **`progress/current.md` is stale** — its body still describes the pre-loop dispatch of ids 40/41/25/26 while its `**Feature:**` line is current, which is the exact blind spot `init.sh` §4 announces about itself. Leader-owned file, not the implementer's; flagged, not counted against the loop; [x] no `blocked` feature.
- **C3** — [x] NetArchTest suite run, not eyeballed: `Architecture.Tests` **16/16**; [x] no cross-service DB access (this loop touched no persistence boundary); [x] shared runtime code still only `SharedKernel`/`Contracts`/`Cqrs`; [x] no `Domain/` reference to `OrderToCash.Cqrs`; [x] `SharedKernel` zero `PackageReference`; [x] no `decimal` in domain arithmetic — the only money code touched, `OrderTotalsView`, is `long` throughout; [x] Kafka-fact / NATS-RPC classification unchanged and correct — the readiness probes are RPC-shaped requests on RPC subjects; [x] no stray debug logging or context-free TODOs in the diff.
- **C4** — [~] `./quality.sh` **not re-run in full**, substituted as set out in §4; [x] domain tests pure; [x] integration tests hit real MsSql/Kafka/NATS/MongoDB containers — the two new race suites use a real `NatsConnection` against the container, not a mock; [~] coverage thresholds not measured this round, and cannot have fallen (no `src/` change, +34 tests); [x] no Jest.
- **C5** — [ ] `progress/history.md` has **no** entry for this loop — correct at a rejection, and the effort record it will need is preserved in §6; [x] `feature_list.json` reflects true state after my transition; [x] no suspicious untracked files (the untracked list is this loop's new test files, the other writer's feature-56 files, and the two progress reports); [x] **Claude did not commit** — I ran no `git commit`, no `git push`, and no `git checkout --` on any path.
- **C6** — not applicable. All five entries are `sdd: false`; no `specs/<name>/` is owed.
- **C7** — [x] `specs/shared/` byte-identical to #7 per `init.sh` §5d; the only diff in `asyncapi.yaml` is the pre-existing `SA-2` hunk, applied in both repositories, which this loop did not touch (verified before and after); [x] `R<n>` ids untouched; [ ] `SA-2` still owes its own commit and its README entry — **open, and named here so it is not lost**; n8n and API-script boxes are ids 31/33, out of scope.

---

## 6. Traceability, and the effort record held for the close

These entries carry acceptance bullets rather than `R<n>` ids. The mapping I verified, bullet → named test → my own probe:

| Entry | Named guard | My probe |
|---|---|---|
| 60 | `SagaFactsConsumerTests.EachConsumedFact_DispatchedFactCopiesEveryEnvelopeFieldFromTheSource` | P1 — 10/10 fail |
| 60 | `ProjectorFactsConsumerTests.EachOfTheFourteenFacts_DispatchedEnvelopeCopiesEveryFieldFromTheSource` + `PR37_…SentinelPerField` | P2 — 15 fail |
| 61 | `EfCoreOrderReferenceCatalogListTests.List{Products,Retailers,Companies,Currencies}Async_ThreeRowsSeededOutOfOrder_ReturnsThemOrderedByCode` | P8 — 3 fail + 3/3 in isolation |
| 63 | `Billing`/`FulfillmentResponderReadinessRaceTests.WithTheWait_ADelayedSubscriberNoLongerLosesTheRace`; four Orders sites via `OrdersCancelResponderReadinessRaceTests` | P9 — fails in 302 ms |
| 64 | `SagaCommandPayloadTests.BC23_EveryPayloadRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped` (+ `G5`) | P4 bites, **P6 does not** — D1 |
| 65 | `OrderReadModelMapperTests.ToOrderDetail_ReturnsTotals_WithAllThreeFieldsCarriedFromTheDocument` | P3 — exactly 1 fail |

**Effort so far, for the joint record when this closes** (read off file mtimes, not estimated):

- **Implementation round 1 ≈16:16 → 17:11, ≈55 min.** `SagaFactsConsumerTests.cs` 16:26:42, `ProjectorFactsConsumerTests.cs` 16:27:28, `EfCoreOrderReferenceCatalogListTests.cs` 16:30:22, the four Orders delegations 16:37:19–16:39:15, the Gateway 7th/8th sites 16:38:44 and 16:44:59, the two new race suites 16:42:37 and 16:45:29, `SagaCommandPayloadTests.cs` 16:50:15, the line-citation edit 16:53:47, report 17:10:24, `feature_list.json` 17:11:01.
- **Review round 1 ≈17:15 → ≈18:40, ≈1 h 25 min. REJECTED.** Ten mutation probes in three families, a full 18-project run (1609), an independent expression-level enumeration of all 42 envelope fixtures, and the pacing population re-derived on the retried exception rather than on a loop-header string.

**#7 filed none of these five entries**, so the whole cost is #8-only process. What it has bought so far, on the evidence rather than the report: four guards that provably could not fail now do (P1, P2, P3, P4), eight readiness loops paced where four were latent flakes, and — the honest other half — one guard that still cannot fail (D1), two paced loops nothing checks (D2), and a count regression the report declined to reconcile. **One loop for five entries was the right call**: the five share one cause, the fixes are independent, and running them together is what produced the transferable result — a *control* for D1 (P7) existed only because Billing's form was in the same session's context. It is also what made the miss possible: a single report covering five entries is where one prose paragraph slips past, and the re-review should treat each entry's enumeration bullet on its own.

---

## 7. What must change before re-review

1. **Id 64 — replace the prose with a search result and close the gap it hid.** Add `{ "Money", typeof(SagaMoney) }` to `RequestAndReplySchemas` (Billing's `CreditRpcPayloadTests.cs:24` is the form). Put the `grep -n "record " src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs` output in the report **verbatim**, with one classification line per hit — 14 lines — including `StockReserveRequestLine` as *not applicable, `asyncapi.yaml` declares its item shape inline with no named schema*. **Arm the new row**: `SagaMoney(long Amount, string Currency, string? Note = null)` must take down `BC23_…(schemaName: "Money")`; record the verbatim failure. Note in passing that `SagaCommandPayloadTests.cs:99`'s hand-typed `AssertKeys(…, "amount", "currency")` is now redundant with the row, and say whether it stays.
2. **Correct the Verification table.** `Projector.UnitTests` is **105**, the solution total is **1609**, and the +34 reconciles against the 1575 baseline line by line. Reconcile it in the report rather than declining to.
3. **Nothing else.** Ids 60, 61, 63 and 65 need no further work; they are open only because the five ride together. Do not re-touch their tests, and do not re-arm what §4 has already re-armed.

**For the leader, not the implementer:** file the id 63 residue (D2) as a numbered entry before the re-review, so the Gateway's two paced-but-unguarded loops leave an artefact rather than a paragraph — and decide whether the ledger-row instrument (A3) gets an entry too.

---

# Round 2 — 2026-09-09

**Verdict: APPROVED.** The three items round 1 left open are closed, and I re-derived each one rather than reading the report's account of it: the `SagaMoney` mutation that left 361/361 green now takes down exactly one named case, the 14-record enumeration is a true search result whose every classification line I checked individually, and the count reconciles to my own per-project run at **1610**.

One new finding, **non-blocking and routed rather than narrated**: the exact defect shape id 64 retired survives in **three more files** under the **same test name**, measured twice at full green. It is outside id 64's acceptance and outside the scope round 1 itself set, so it becomes a numbered entry rather than a rejection — F1 below, for the leader to file.

**Round 1 above is unchanged.** Nothing in §1–§7 was amended or reopened.

**This still does not close phase 13.** Id **66** (`operator_note_reaches_the_timeline`) remains `pending`, and **`SA-2` is applied byte-identically in both repositories and still owes its own commit** — `init.sh` §5d reports the shared spec byte-identical across 6 files with that hunk in place, so the amendment is coherent and simply uncommitted.

## 1. What I ran, and what I deliberately did not

Re-running a suite the implementer had just run twice is duplicated cost, so I ran the claims and not the world:

- **Ran in full, my own invocation:** all **11 container-free projects** individually (`dotnet test <project> --no-build` after `dotnet build OrderToCash.sln --no-incremental`), plus **`Orders.IntegrationTests`** — the one integration assembly that exercises the file this round mutated (`src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs`), so the restore is proved at runtime and not only at byte level. **95/95, 6 m 33 s, against real containers.**
- **Ran:** four fresh mutations of my own (M1–M4 below), `dotnet build OrderToCash.sln --no-incremental` before every measurement, `dotnet format OrderToCash.sln --verify-no-changes` (exit 0), `./init.sh` (exit 0).
- **Not re-run, and why:** the other six integration projects (Billing 86, Fulfillment 59, Gateway 48, Notifications 12, Projector 52, Seed 6 = **358**, my own round-1 figures). No file in any of them changed after round 1 closed — every `.cs` under those projects has an mtime at or before `17:52:12`, and my round-1 review file is `18:03:33`; `git status` shows the same modified set round 1 approved. The claim under test this round is about **one** new theory case in `Orders.UnitTests`, so re-running six container suites would have measured nothing that changed.
- **Not re-run:** `./quality.sh` end to end. Its coverage step is **deliberately non-enforcing** — `quality.sh:4-9` and `:80-83` say so in terms, deferring the gate to feature 34 (phase 21) — so it reports a number rather than gating one, and no `src/` shipped behaviour changed this round. The implementer did run it, twice (see A4).

## 2. Item 1 — id 64: the enumeration, the new row, and the mutation that used to pass

### The row is real and it bites

**M1 — the round-1 mutation, repeated exactly.** `SagaMoney(long Amount, string Currency)` → `SagaMoney(long Amount, string Currency, string? Note = null)` in `src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs:77`; `cp` backup first, `dotnet build OrderToCash.sln --no-incremental` (0 warnings, 0 errors — the mutation is source-compatible, so this is a genuine silent-drift shape and not a build break):

```
OrderToCash.Orders.UnitTests.SagaCommandPayloadTests.BC23_EveryPayloadRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped(schemaName: "Money", payloadType: typeof(OrderToCash.Orders.Infrastructure.Messaging.Rpc.SagaMoney)) [FAIL]
   Assert.Equal() Failure: HashSets differ
Expected: ["amount", "currency"]
Actual:   ["amount", "currency", "note"]
Failed!  - Failed:     1, Passed:   361, Skipped:     0, Total:   362
```

**361/361 green in round 1 → 1 failed / 362 now.** Exactly one case failed; nothing else moved. Restored from the backup, `cmp` identical, `sha256sum` back to `a06f7ecd8037065bf4db3fe833d75e391ad23bab8119edd2b85eb5d3297a8a73`, `git status` on the path empty (it matches `HEAD`), forced rebuild, **362/362 green**.

**M2 — substitution, because a row is two claims and M1 only tests one.** M1 proves the row reads the record; it cannot prove the row names the **right schema**. So I swapped the row's schema-name literal for a real sibling in the same spec — `{ "Money", typeof(SagaMoney) }` → `{ "OrderLine", typeof(SagaMoney) }`:

```
...BC23_EveryPayloadRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped(schemaName: "OrderLine", payloadType: typeof(...SagaMoney)) [FAIL]
Expected: ["productCode", "description", "quantity", "unitPrice", "lineDiscount"]
Actual:   ["amount", "currency"]
Failed!  - Failed:     1, Passed:   361, Skipped:     0, Total:   362
```

It fails, and — the false-negative caution this repository pays for — **the message names what I broke**, not an incidental parse error. Restored (`cmp`, `sha256` `774f95e790ff78ef5a7ac8a1181feda79d60043127c3cb5517faace022523c7e`), rebuilt, green.

### The enumeration is a search result, and every line of it is true

My own command, and its complete output — identical to the report's, 14 hits:

```
$ grep -n "record " src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs
19:public sealed record StockReserveRequestLine(string ProductCode, int Units);
22:public sealed record StockReserveRequestPayload(
35:public sealed record StockReserveReplyPayload(
44:public sealed record StockReleaseRequestPayload(string OrderReference, string Reason);
47:public sealed record StockReleaseReplyPayload(
55:public sealed record DespatchCreateRequestPayload(string OrderReference);
58:public sealed record DespatchCreateReplyPayload(
77:public sealed record SagaMoney(long Amount, string Currency);
80:public sealed record CreditHoldRequestPayload(
92:public sealed record CreditHoldReplyPayload(
104:public sealed record InvoiceIssueRequestPayload(
113:public sealed record InvoiceIssueReplyPayload(
130:public sealed record CreditReleaseRequestPayload(string OrderReference, string RetailerCode, string CompanyCode);
137:public sealed record CreditReleaseReplyPayload(
```

**I also checked the pattern itself could not have hidden a hit.** `grep -n "record "` misses a declaration written `record(` or `record\t`; re-running with `grep -nE "\brecord\b"` over the same file returns **the same 14 lines**, so the population is the file's true record set and not an artefact of the search string.

**The 13 "covered" lines are literal, not asserted.** `tests/Orders.UnitTests/SagaCommandPayloadTests.cs:191-203` holds exactly 13 rows, and they are the 13 the report classifies, one for one — including `{ "Money", typeof(SagaMoney) }` at `:197`. Each row also *passes* against a schema parsed from the real spec, so no row is a name that resolves to nothing (an unresolvable schema yields an empty expected set against a non-empty reflected set, which fails).

**The one exclusion is true, and I checked it rather than accepting it.** `specs/shared/asyncapi.yaml:3283-3295` declares `StockReserveRequestPayload.lines[]` with `items: {type: object, properties: {productCode, units}}` **inline, with no `$ref` and no named schema**, so `AsyncApiSchema.PropertyNamesOf` genuinely has nothing to look up. I also checked the exclusion is not hiding an applicable named schema: the only named line-shaped schemas in the spec are `OrderLine` (`:2057`, five properties), `DespatchLine` (`:2106`) and `InvoiceLine` (`:2117`), none of which is this record's contract. **A wrong classification line is worse than a missing one; this one is right.**

**Scope note, checked not assumed.** `ReservationRef` and `Shortage` appear in these payloads but are declared in `src/Contracts/Facts/`, outside the enumerated file, and are Contracts-owned wire types with their own guards — correctly outside a population the entry defines as Orders' own RPC payload records.

**`AssertKeys(..., "amount", "currency")` at `:99` stays, and the report's reason for keeping it is correct**: `BC23` reads the record's declared properties by reflection, `AssertKeys` reads the bytes a real instance serialises through `RpcJson`. They fail on different defects (a property set vs a casing/`JsonPropertyName`/options defect), and the file's nine other records keep both.

## 3. Item 2 — the count, off my own run

Per-project, my invocation, after `dotnet build OrderToCash.sln --no-incremental`:

```
Architecture.Tests 16 | Billing.UnitTests 232 | Contracts.UnitTests 21 | Cqrs.UnitTests 23
Fulfillment.UnitTests 124 | Gateway.UnitTests 205 | Notifications.UnitTests 70 | Orders.UnitTests 362
Projector.UnitTests 105 | Seed.UnitTests 44 | SharedKernel.UnitTests 50            => 1252 container-free
Orders.IT 95 (my own re-run) + Billing.IT 86, Fulfillment.IT 59, Gateway.IT 48,
Notifications.IT 12, Projector.IT 52, Seed.IT 6 (my round-1 figures, unchanged)   =>  358 integration
```

**Total 1610 passed, 0 failed, 0 skipped.** It reconciles to my round-1 **1609** by exactly **+1**: `Orders.UnitTests` 361 → 362, the `{ "Money", typeof(SagaMoney) }` case and nothing else. No project moved in any other direction.

**The cell that caused the original error now reads the project total.** `Projector.UnitTests` = **105** in my own unfiltered project run — not `31`, which was `ProjectorFactsConsumerTests` alone. The report's §1 table records 105 and reconstructs every row from a fresh per-project run rather than carrying the old table forward, which is the right remedy: the defect was a filtered run recorded as a project total, and copy-forward is how that recurs.

## 4. Item 3 — what the round disturbed

Nothing beyond what it claims. Verified three ways, not by reading the "Files touched" list:

- **mtimes.** Every `.cs` under `src/` and `tests/` carries an mtime at or before `17:52:12`, except `tests/Orders.UnitTests/SagaCommandPayloadTests.cs` (`18:45:00`) and `src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs` (`18:08:42`, this round's arming cycle). My round-1 review file is `18:03:33`. So ids 60, 61, 63 and 65's work was not touched after I approved it, and my settled findings are not reopened.
- **`git status` / `git diff`.** `SagaCommandPayloads.cs` is **absent from the modified list** — the arming restore returned it to its `HEAD` bytes. The `SagaCommandPayloadTests.cs` diff contains round 1's rewrite plus, this round, only the `Money` row and the doc comment that replaced the prose completeness sentence; no test was deleted, which the +1 net count independently confirms.
- **`feature_list.json`.** The only status this round changed is id 60's (`in_progress` → `in_review`). The leader's entries 66–69, id 56's rewritten acceptance and id 63's widened bullet are all present and untouched; `init.sh` §5b reports no feature lost and no `done` reverted. **`specs/shared/asyncapi.yaml`'s diff is still the single 3-line `SA-2` hunk** and nothing else.

## 5. F1 — new finding, non-blocking: the retired form survives in three more files, under the same test name (ROUTE)

Id 64 replaced *"a hand-typed key list compared against the spec, never against the record"* with reflection over the record. **The same shape is still live in three other files**, two of them in the same assembly and carrying the identical test name.

Enumeration, path-excluded at the source, content-based:

```
$ find . -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
    | xargs -0 grep -ln "BC23_TheRetypedKeyListsAgreeWithTheKeySetsParsedFromAsyncApi" | sort
./tests/Fulfillment.UnitTests/StockRpcPayloadTests.cs
./tests/Orders.UnitTests/CatalogReferenceListPayloadTests.cs
./tests/Orders.UnitTests/OrdersCancelPayloadTests.cs
```

One classification line per hit, read from the source rather than inferred:

| File | Line | Classification |
|---|---|---|
| `tests/Orders.UnitTests/OrdersCancelPayloadTests.cs` | `:66-72` | **Defective, measured.** `Assert.Equal(parsed, retyped)` — spec vs `[InlineData]` list, never the record. |
| `tests/Orders.UnitTests/CatalogReferenceListPayloadTests.cs` | `:101-107` | **Defective, same source shape.** Same two-sides-agree comparison; also carries a hand-retyped list inside its own `G5` arming (`:125`). |
| `tests/Fulfillment.UnitTests/StockRpcPayloadTests.cs` | `:162-168` | **Defective, measured, and in a second service.** Same shape, plus a hand-retyped list in its `G5` (`:192`). |

**Measured, not argued** — the identical probe that now fails against the fixed guard passes against these:

| # | Mutation | Suite | Result |
|---|---|---|---|
| M3 | `OrdersCancelReplyPayload` (`src/Orders/Presentation/Rpc/OrdersCancelPayloads.cs:24`) gains `string? Note = null` — a property `OrdersCancelReplyPayload`'s schema does not declare | `Orders.UnitTests` | **362 passed, 0 failed — GREEN** |
| M4 | `DespatchCreateReplyPayload` (`src/Fulfillment/Infrastructure/Messaging/Rpc/DespatchRpcPayloads.cs:14`) gains `string? Note = null` | `Fulfillment.UnitTests` | **124 passed, 0 failed — GREEN** |

Both restored from `cp` backups, `cmp` and `sha256sum` verified (`f6885c14…`, `b9fcf094…`), rebuilt `--no-incremental`, and re-confirmed **362/362** and **124/124**. No `git checkout --` was used on any path in this round.

**Why this does not reject id 64.** Id 64's acceptance names *"Orders' BC23 theory"* — the one feature 41's finding B1 identified, whose `notes` cite the `availableCreditAfter` defect in `SagaCommandPayloadTests`. My own round-1 re-review list said *"Nothing else"*, and the implementer's brief bounded it to that file and the payload record. Rejecting now on a scope neither the entry nor round 1 named would be moving the goalposts, and `CLAUDE.md` is explicit that an approved scope outranks a late bound. **The correct instrument for a real gap outside the closing scope is a numbered entry, not a paragraph and not a rejection** — the same route round 1 took for D2, which the leader filed as id 69.

**Proposed entry, for the leader to file (I have not written it to `feature_list.json`):** `retyped_key_list_guards_survive_in_three_more_payload_test_files`. Acceptance: all three theories derive their key set from the payload **record** by reflection, as `SagaCommandPayloadTests` and `Billing`/`Gateway` now do; each file's `G5` arming stops comparing against a hand-retyped list too; **armed** with the measured mutations above — M3 must take down a named test in `Orders.UnitTests` and M4 one in `Fulfillment.UnitTests`, each with its verbatim failure; and the enumeration is re-run at close, because the population is *"every theory comparing a parsed spec key set to a literal"*, not *"the three files this entry names"*.

**The transferable point, which is why this is worth more than the fix.** The loop's own subject is guards that cannot fail; four entries in it were found at a **site** and written as a site. Id 63's population went 4 → 6 → 8 across three rounds, each step because somebody enumerated instead of assuming; id 64 fixed one of four instances of its own class, and nobody enumerated the class because the entry named a file. **When a loop's theme is a defect class, the loop's first task should be one repository-wide enumeration of that class** — the command that found these three files took under a second.

## 6. A4 — advisory: the round-2 report's `quality.sh` line is an unfilled placeholder

`progress/impl_guard_hardening.md:414` reads *"`./quality.sh`: run in full this round; result recorded below once the run this session completed"* — and no result is ever recorded. **The run did happen**: two complete logs exist from that session, `18:37:01` and `18:43:46`, both ending `[OK] dotnet test: all tests passed` and `[OK] quality.sh finished`. So the underlying fact is fine and the *claim about it* is unread — a sentence promising a figure that never arrives, in a report whose subject is claims that cannot fail. Non-blocking, recorded because it is the same shape one size down. (Note for anyone reading those logs as a gate: `quality.sh:4-9,80-83` states the coverage threshold is **collected and printed, not enforced**, until feature 34 in phase 21.)

## 7. CHECKPOINTS.md — round 2 walk

- **C1** — [x] harness files present; [x] `progress/` files present; [x] five agent definitions with models; [x] `./init.sh` exits 0 (run by me this round).
- **C2** — [x] at most one feature in flight (only id 60 was `in_review`; none `in_progress`); [x] all statuses valid; [x] every `done` feature has passing tests (1610/0/0 by my own reckoning); [x] `progress/current.md` describes this session — its `**Feature:**` line and Status paragraph name the guard-hardening loop and its five ids; the residual drift is that it says `in_progress` where the backlog says `in_review`, a leader-owned line to update at close, no longer the stale body round 1 flagged; [x] no `blocked` feature.
- **C3** — [x] NetArchTest suite **run**, not eyeballed: `Architecture.Tests` **16/16**; [x] no cross-service DB access (nothing this round touched persistence); [x] shared runtime code still only `SharedKernel`/`Contracts`/`Cqrs`; [x] no `Domain/` reference to `OrderToCash.Cqrs`; [x] `SharedKernel` zero `PackageReference`; [x] no `decimal` in domain arithmetic — `SagaMoney` is `long Amount`; [x] Kafka-fact / NATS-RPC classification untouched; [x] no stray debug logging or context-free TODOs in the diff.
- **C4** — [~] `./quality.sh` not re-run by me in full, substituted as set out in §1 and run twice by the implementer with both logs green; [x] domain tests pure; [x] integration tests hit real containers — `Orders.IntegrationTests` 95/95 against real MsSql/Kafka/NATS in 6 m 33 s under my own invocation; [~] coverage thresholds **collected and printed, not enforced by design** (`quality.sh:4-9,80-83`, deferred to feature 34/phase 21) — and unmovable here in any case: no `src/` shipped behaviour changed and one test was added; [x] no Jest.
- **C5** — [x] no suspicious untracked files (the untracked set is this loop's two new race suites, the other writer's approved feature-56 files, and the progress reports); [x] `progress/history.md` gains this loop's entry **with its effort record**, appended at this approval; [x] `feature_list.json` reflects true state after my transition of all five to `done`; [x] the human is told what was done and how to test it (the leader's report; the manual check is `dotnet test` on the 11 container-free projects, and `docker compose up` plus `Orders.IntegrationTests` for the container path); [x] **Claude did not commit** — I ran no `git commit`, no `git push`, and no `git checkout --` on any path.
- **C6** — not applicable. All five entries are `sdd: false`; no `specs/<name>/` is owed. No ledger is owed either: none of the five ports a #7 mechanism (id 64 ports an intra-#8 form from Billing to Orders, which is not a cross-stack property transfer).
- **C7** — [x] `specs/shared/` byte-identical to #7 per `init.sh` §5d across 6 files, with the `SA-2` hunk applied identically in both repositories and untouched by this round; [x] `R<n>` ids untouched; [ ] **`SA-2` still owes its own commit and its README entry — open, and named here again so it is not lost**; n8n and API-script boxes are ids 31/33, out of scope.

## 8. Traceability — the closing map

| Entry | Named guard | Probe that proves it can fail |
|---|---|---|
| 60 | `SagaFactsConsumerTests.EachConsumedFact_DispatchedFactCopiesEveryEnvelopeFieldFromTheSource` | round 1 P1 — 10/10 cases fail on a production transposition |
| 60 | `ProjectorFactsConsumerTests.EachOfTheFourteenFacts_DispatchedEnvelopeCopiesEveryFieldFromTheSource` + `PR37_…SentinelPerField` | round 1 P2 — 15 fail on a sibling-field substitution |
| 61 | `EfCoreOrderReferenceCatalogListTests.List{Products,Retailers,Companies,Currencies}Async_ThreeRowsSeededOutOfOrder_ReturnsThemOrderedByCode` | round 1 P8 — 3 fail, retailers 3/3 in isolation (A2's caveat stands) |
| 63 | `Billing`/`FulfillmentResponderReadinessRaceTests.WithTheWait_ADelayedSubscriberNoLongerLosesTheRace`; four Orders sites via `OrdersCancelResponderReadinessRaceTests` | round 1 P9 — fails in 302 ms, a change of kind not of probability |
| 64 | `SagaCommandPayloadTests.BC23_EveryPayloadRecordCarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped` (13 cases) + `G5` | round 1 P4 (field removal, 1 fail) and **round 2 M1** (undeclared property on `SagaMoney`, 1 fail) and **M2** (schema-name substitution, 1 fail naming the intended reason) |
| 65 | `OrderReadModelMapperTests.ToOrderDetail_ReturnsTotals_WithAllThreeFieldsCarriedFromTheDocument` | round 1 P3 — exactly 1 fail on a transposition |

Fourteen mutation probes across the two rounds — ten mine in round 1, four mine in round 2 — in all three families: deletion, corruption/transposition, and substitution.

## 9. Effort record, and whether one loop for five entries was right

**Read off file mtimes on this machine, not estimated.**

| Pass | Window | Duration |
|---|---|---|
| Implementation, round 1 | 16:16 → 17:11 | ≈55 min |
| Review, round 1 (REJECTED) | 17:15 → 18:03 | ≈48 min |
| Implementation, round 2 | 18:05 → 18:45 | ≈40 min |
| Review, round 2 (APPROVED) | 18:47 → 19:20 | ≈33 min |

**4 sessions, ≈2 h 56 min wall-clock, ≈2 h 56 min traceable.** (Round 1 §6 estimated its own review pass as ending ≈18:40; the review file's mtime is `18:03:33`, so the corrected figure is ≈48 min. I record the correction here rather than amending §6, which is closed.)

**#7 filed none of these five entries, so the entire cost is #8-only process cost** — there is no ratio to report, only an absolute.

**What it bought, on the evidence rather than the report.** Six guards that provably could not fail now do — two envelope-provenance theories (P1, P2), three catalog-ordering guards (P8), a totals-transposition guard (P3), and the `BC23` wire-key theory twice over (P4, M1, M2). Eight readiness loops paced where four were latent flakes and the pacing of two is proved by a deterministic change of kind (P9). And one dropped `Money` row restored, which is the one that carried an actual unguarded contract field. The honest other half: two Gateway loops remain paced-but-unproved (routed as id 69), and F1 shows the same retyped-list defect alive in three more files.

**Was one loop for five entries right? Yes — and the loop's own blocking defect is the argument *for* grouping, not against it, once you look at what actually found it.**

The tempting reading is the opposite: id 64's prose completeness claim was an instance of the very class the five entries share, so grouping must have diluted attention. But the mechanism that **caught** it was itself a product of the grouping. D1 was not found by reading the paragraph more carefully — it was found by a mutation, and what turned that mutation from a curiosity into proof was the **control**: the identical mutation on Billing's `CreditMoney`, which failed because Billing's row exists. That control cost almost nothing because Billing's form was already in the session's context as id 64's own porting source. In five separate loops, the id-64 reviewer would have had the Orders result and no cheap way to distinguish *"my mutation is wrong"* from *"the row is missing"* — the most likely outcome being a green suite recorded as a pass.

The grouping also amortised the protocol. One arming discipline, one forced-rebuild lesson (learned once on id 61's false pass and applied to all five), one count reconciliation, one review of the whole. The round-2 fix was 40 minutes for a row, an enumeration and a corrected table.

**What the grouping did not buy, and should have.** Not one of the five entries was enumerated **as a class** across the repository. Id 63's population grew 4 → 6 → 8 only because two people enumerated instead of assuming; id 64 fixed one of four instances of its shape and nobody looked for the others until this round's F1 — which took one `grep`. So the finding for #9 is not *"group or don't group"* but a condition on grouping: **when a loop's theme is a defect class, its first task is a single repository-wide enumeration of that class, as a search result, before any fix is written.** Grouping made the loop cheaper and gave it its best instrument; only enumeration would have made it complete.

## 10. What remains after this approval

- **Id 66** (`operator_note_reaches_the_timeline`) is `pending`. Phase 13 is **not** closed by this verdict.
- **`SA-2` owes its own commit** and its README entry. It is applied byte-identically in both repositories (`init.sh` §5d green across 6 files); the amendment is coherent and simply uncommitted.
- **Id 69** (`gateway_readiness_pacing_is_unguarded`) is filed and `pending` — round 1's D2, correctly routed by the leader.
- **F1 needs a numbered entry**, proposed in §5 with acceptance and two measured mutations. I have not written it to `feature_list.json`; the leader files it.

## 11. Post-transition state — one leader action needed before the session advances

Setting all five to `done` leaves **no feature active**, and `./init.sh` §4 now fails on exactly that:

```
[FAIL]  progress/current.md claims a feature while none is active: "**Feature:** `envelope_fixture_collisions_defeat_provenance_assertions` (id 60, phase 13)"
```

This is the expected consequence of closing the loop, not a defect in it — `init.sh` exited **0** at every point during this review, and §5b's backlog tripwire is green (`no feature lost, no done reverted`) after my transition. `progress/current.md` is leader-owned; **the leader repoints it at id 66 or resets it to the template**, and `init.sh` returns to 0. I have not edited it.

Backlog after this approval: **68 features, 49 `done`**, none `in_progress`, none `in_review`.
