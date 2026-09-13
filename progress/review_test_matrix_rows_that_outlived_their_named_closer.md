# Review — `test_matrix_rows_that_outlived_their_named_closer` (backlog id 72, phase 14)

**Verdict: REJECTED** — one blocking finding. Four advisories. Every one of the five acceptance bullets is individually met and independently verified; the rejection is not about any of them, it is that **the feature's own theme is a defect CLASS and the class was never enumerated**, so it is still live in two rows of the file this feature exists to repair.

**Status transition I would make (the leader owns `feature_list.json`; I made no edit):** id 72 `in_review` → `in_progress`.

## Method — what I re-ran, and what I did not

I did not re-run `./quality.sh`; the leader's run is green and reconciled, and the claim under test here is about three rows and two test files, not about the full suite. What I ran instead: four of my own mutation probes (two of them mutations the implementer never made), an independent row-by-row re-derivation of the coverage summary from the Status column, a byte-level check that columns 1–4 are untouched, and a path-anchored enumeration of the whole defect class across all 63 status cells. Every command and its verbatim output is below. The implementer's own arming logs were read as corroboration, never as substitutes: `money_repr_arm1.log`, `money_repr_arm2.log`, `r61_arm1_corrupt.log`, `r61_arm2_subject.log`, `r61_arm2_subject_v2.log`, `r61_restored_final.log`.

## `CHECKPOINTS.md` — boxes walked

C1, C2, C5 (partially), C6 not applicable to this `sdd: false` feature beyond the boxes marked; C3/C4/C7 applicable.

- [x] C2 — at most one feature `in_progress`; id 72 is `in_review`, ids 62/73/76 `done`, statuses valid.
- [x] C2 — `progress/current.md` describes this active session (`:3`, feature named, started 2026-09-12).
- [x] C3 — no architecture surface touched: the feature adds two test files and edits one assertion in a third; `src/` is byte-identical to before it started (verified by `git status`, below).
- [x] C4 — `./quality.sh` green: 18 projects, 0 failed, **1879** total, `[OK]` at format, build, test and finish (`quality_feature72_run2.log:3,42,167,188`). Reconciles as 1878 + 1, the single new `[Fact]`; `Gateway.IntegrationTests` 59 → 60 is the only mover (`:139`).
- [x] C4 — integration tests hit real containers, not mocks: the R61 test boots the real `FulfillmentHost` against real MS-SQL/Kafka/NATS (I ran it myself, below).
- [x] C4 — no Jest; xUnit throughout.
- [x] C7 — `specs/shared/` untouched apart from `test-matrix.md`'s Status column and its coverage summary. Columns 1–4 of R1/R24/R61 are byte-identical (md5, below); `init.sh` §5d green, *"shared spec byte-identical to #7 across 6 file(s); test-matrix.md exempt"*.
- [x] C7 — the `R<n>` ids are #7's and the #8 realisation genuinely satisfies them (R1 and R61 checked against #7's own files assertion by assertion, below).
- [ ] **C5 — `progress/history.md` has no entry for this feature.** Correct at this moment (the entry is written at approval), recorded here only so the re-review knows it is still owed, with the effort record.
- [ ] **C7 — "every deviation is a recorded amendment" is not at issue, but C7's traceability spirit is: see B1.** The one box I cannot mark is the class enumeration that would let a reader trust the file as a whole rather than three rows of it.

## Traceability — one row per acceptance bullet, naming the case and the arm

| Bullet | Claim | Test case that carries it | Arm(s) seen to fail | Verdict |
|---|---|---|---|---|
| 1 | R61's API half armed both ways, then DONE | `tests/Gateway.IntegrationTests/FulfillmentStockEndToEndTests.cs` › `ReplenishStock_ThroughTheRealGatewayPipeline_ActuallyAddsUnitsInFulfillmentsRealDatabase` | corruption (`l.Units` → `l.Units + 1`, implementer's log); **substitution (`GatewaySubjects.StockReplenish` → `billing.payment.register`) — re-run by me, P6** | PASS |
| 2 | R1's API half exists as a discovering sweep, ported and armed | `tests/Gateway.IntegrationTests/MoneyRepresentationHttpTests.cs` › `EveryMonetaryFieldOfEveryResponseIsAnIntegerAccompaniedByACurrencyCode`, helper `MoneyFieldSweep.SweepForMoneyFields` | non-integer amount (implementer); vacuity (implementer); **invalid currency naming endpoint + field — my own, P4** | PASS, two advisories (A1, A2) |
| 3 | R24's cell names feature 31, and id 31 carries the obligation | no new test (API half genuinely absent) | n/a — a deferral that names someone | PASS (P9) |
| 4 | the Scoped paragraph the legend points to | n/a — document | n/a | PASS (P10) |
| 5 | the coverage summary recounted row by row | n/a — document | n/a | PASS on arithmetic **and** on per-row class (P2); but see B1 |

`R<n>` → test mapping I verified directly, by reading the cited files rather than the cells: **R1** domain half `tests/SharedKernel.UnitTests/MoneyTests.cs` › `R1_Money_Represents…`, API half as above (case name occurs literally in the file, `MoneyRepresentationHttpTests.cs:146`). **R61** domain half `tests/Fulfillment.UnitTests/StockItemTests.cs` › `R61_IncreasesUnitsByTheRequestedQuantity_…`, API half as above (`FulfillmentStockEndToEndTests.cs:136`). **R24** integration half `tests/Orders.IntegrationTests/SagaHappyPathTests.cs` › `R19_R24_HappyPath_…`; API half correctly absent and correctly deferred.

## Probes

### P1 — columns 1–4 untouched (unit: the column, per row)

```
for r in R1 R24 R61; do old=$(git show HEAD:specs/shared/test-matrix.md | grep -P "^\| \*\*$r\*\* \|" | awk -F'|' '{print $2"|"$3"|"$4"|"$5}' | md5sum); new=$(grep -P "^\| \*\*$r\*\* \|" specs/shared/test-matrix.md | awk -F'|' '{print $2"|"$3"|"$4"|"$5}' | md5sum); echo "$r old=$old new=$new"; done
R1 old=0c65fc31c3641a37d35754fb14b3dd3c  - new=0c65fc31c3641a37d35754fb14b3dd3c  -
R24 old=e12e4ca539c2e8c981e72bc92c727082  - new=e12e4ca539c2e8c981e72bc92c727082  -
R61 old=7b2035fa026244f20ee82b25d9883ccc  - new=7b2035fa026244f20ee82b25d9883ccc  -
```

Classification: identical for all three. `git diff specs/shared/test-matrix.md` is 16 lines and touches only the summary table, the new paragraphs and the three Status cells. **PASS.**

### P2 — the recount, re-derived by me from the Status column (unit: the ROW, not the total)

I parsed every `| **R<n>** |` row, took the last cell, and classified it myself against the summary's own three definitions. Result: **63 rows, no duplicates, none missing** (`R1`–`R63` all present). Classes: `R55` is the only `TODO` (web half owed to ids 29/30, both verified `pending`) → **not yet green**. `R24` (*"API half … outstanding"*, ratified at the `order_saga_orchestrator` gate) and `R56` (*"MECHANISM leg DONE, composed-stack leg unproven"*, ratified at `spec_observability_reliability` row 4, closer id 28 verified `pending`) → **scoped, both ratified**. Every other row reads `DONE` (or `RETRY-CLAUSE ROW DONE … DEAD-LETTER ROW DONE`) with no shortfall against its own requirement's wording → **green**.

So **60 + 2 + 1 = 63 ✓**, and each row's class is right, including the two the leader's own quick recount transposed: the record's enumeration (R24 and R56 scoped, R63 green) is correct and the leader's word-keyed pass was not. The per-section table also reconciles row-id by row-id: 10 + 8 + 11 + 8 + 8 + 5 + 6 + 6 + 1 = 63, with the flips landing in §1 (R1) and §4 (R61) exactly as claimed. **PASS** — the total reconciles *and* the rows underneath it are individually right, which is the distinction this bullet exists to enforce.

### P3 — the defect CLASS, enumerated across all 63 cells (unit: the STATUS CELL) — **BLOCKING**

The feature's subject is a class: *a status cell whose deferral names a closer that has since shipped*. `CLAUDE.md` (on disk, §"When the work targets a defect CLASS") requires that such a loop begin with one repository-wide enumeration **as a search result, before any fix**, because *"the instances named in the backlog are where the class was noticed, never where it ends."* The record contains no such enumeration; it repairs exactly the three rows the backlog named. I ran it:

```
python3 - <<'EOF'   # over every | **R<n>** | row's last cell; pattern: does not exist yet|no Gateway|not exist|exists yet|feature[s]? \d|owed to|belongs to|still `pending`|closed by|named closer|outstanding|when the .* exists
…22 hits, one classification line each…
EOF
```

Classification of all 22 hits: **R1** (3 hits) and **R61** (3) — closed by this feature, correct. **R24** (6) — corrected by this feature to name id 31, correct. **R55** (3) — names ids 29/30, both verified `pending`, correct. **R56** (1) — names id 28, verified `pending`, correct. **R29** (1), **R46** (1) — not deferrals (provenance of a split; a no-live-caller note). **R48** (2) and **R49** (1) — **stale, same class, untouched.**

- `specs/shared/test-matrix.md` R48 cell: *"DONE — the Gateway's `POST /invoices/:id/payments` (features 25/29) **does not exist yet**, so this is proven one layer below the sketch's `API` level"*.
- `specs/shared/test-matrix.md` R49 cell: *"same honest one-layer-down substitution as R48 (**no Gateway yet**)"*.

Both premises are false today. The endpoint exists and is served: `src/Gateway/Presentation/Endpoints/InvoicesEndpoints.cs:32`, `app.MapPost("/invoices/{id}/payments", …)`, shipped by **feature 25 `gateway_rest_auth`, status `done`** — the very feature whose shipping-without-closing is this backlog entry's entire finding. Why it matters rather than being pedantry: this is the identical failure mode the entry was filed against, in the identical file, and it is now carrying a factual falsehood about the system rather than merely an unhelpful pointer. It is also closeable today with a named closer exactly as R24 was — #7's own R48 cell reads *"now genuinely at the sketch's own `api/` level (feature 31), behind the real Gateway HTTP endpoint"*, and #8's id 31 `api_tests` already carries the obligation in substance at acceptance bullet 3 (*"duplicate paymentReference yields one payment"*). So the correction is the same three-line shape the feature already performed once, not new work.

A second question the enumeration raises and which the re-review should answer rather than inherit: R48/R49 are counted **green** while their own cells state a shortfall (*"proven one layer below the sketch's `API` level … no claim of API-level coverage is made"*). Under the summary's own definitions that reads closer to **scoped** — *"the named test exists and is green, but proves less than the requirement says, with the shortfall stated explicitly in the cell."* I am not ruling on it here, because it predates this feature and the totals do not move either way; I am recording that a recount which never inspected those two cells cannot have decided it.

### P4 — R1, my own arm: a money field that loses its currency (unit: the FIELD in the failure message)

The acceptance requires each R1 arm to fail *"with a message naming the endpoint and the field"*. The implementer's currency arm (`OrderReadModelMapper.ToOrderDetail`'s `Currency` → `null`) removes the key entirely, so every finding for that endpoint disappears and only the **vacuity** guard fires — a message naming the endpoint and **no field**, which is visible verbatim in their own table row 2 and in the cell. I therefore armed the same clause the other way: a currency that is present but not ISO-4217-shaped.

```
# src/Gateway/Domain/Projection/OrderReadModelMapper.cs:94   doc.Currency → doc.Currency?.ToLowerInvariant()
dotnet build --no-incremental tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj   # 0 Error(s)
dotnet test … --filter "FullyQualifiedName~MoneyRepresentationHttpTests"
  Error Message:
   GET /orders/{id} $.totals.initialAmount: R1 requires an ISO 4217 alpha-3 currency code accompanying the amount, got "eur"
     at …MoneyRepresentationHttpTests.AssertEveryFindingIsIntegerMinorUnitsWithCurrency… line 139
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1
```

Classification: the currency clause **is** armed, and it names endpoint and field. Restored from backup, `cmp` identical, forced rebuild, re-run green (`Passed! … Total: 1`). The bullet's bar is met by the guard even though it is not met by the arm the implementer chose — hence advisory A1, not a blocking finding.

### P5 — R1, my own probe: is the canonical `amount`-beside-`currency` rule exercised at all? (unit: the CLAUSE)

Ledger row 3 claims #7's rule is ported *"independent of `ValueKind`"*, and the header of both #7's and #8's helper says this is *"what lets the sweep catch a `Money.amount` that regressed to a decimal STRING, which a number-typed-only rule would silently miss."* That is a countable claim, and nothing armed it. I neutralised the clause and left everything else intact:

```
# tests/Gateway.IntegrationTests/MoneyFieldSweep.cs:122
#   var isCanonicalMoneyAmount = property.Name == CanonicalAmountKey && hasOwnCurrency;
# → var isCanonicalMoneyAmount = false && …
dotnet build --no-incremental …   # 0 Error(s)
dotnet test … --filter "FullyQualifiedName~MoneyRepresentationHttpTests"
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 854 ms
```

Classification: **green with the clause disabled** — on today's swept data every `amount` under a currency is also a JSON *number*, so the second disjunct already catches it and the canonical rule is redundant. The port is faithful (I read #7's `money-field-sweep.ts:93` against `MoneyFieldSweep.cs:122` — same rule, same position, same intent) and #7's own data has the identical property, so this is not a translation defect. It is a ledger row whose distinguishing property has no case behind it. Advisory A2. Restored, `cmp` identical, line 122 re-read.

### P6 — R61, the contested arm, re-run by me (unit: the SUBJECT named in the failure)

```
# src/Gateway/Application/Rpc/GatewaySubjects.cs:17  "fulfillment.stock.replenish" → "billing.payment.register"
dotnet build --no-incremental …   # 0 Error(s)
dotnet test … --filter "…FulfillmentStockEndToEndTests.ReplenishStock_ThroughTheRealGatewayPipeline_ActuallyAddsUnitsInFulfillmentsRealDatabase"
  Error Message:
   expected /stock/replenish to succeed; got 503 ServiceUnavailable: {"type":"about:blank","title":"The owning context is unreachable","status":503,"detail":"RPC call to \"billing.payment.register\" failed: no responder is subscribed to this subject.","code":"UPSTREAM_UNAVAILABLE","correlationId":"39a54938-…","occurredAt":"2026-09-12T09:43:04.850Z"}
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 3 s
```

Classification: reproduced exactly, against real MS-SQL/Kafka/NATS containers. The failure **names the substituted subject**, and it is the broker's immediate *no responder* refusal rather than a timeout — so it satisfies backlog id 82's bar (a failure counts only if its message names what you broke) and CLAUDE.md's own false-negative caveat for substitution probes. Restored, `cmp` identical, forced rebuild, both facts green: `Passed! - Failed: 0, Passed: 2, Total: 2`.

**On whether the durable fix is a strengthening or a weakening — the question I was asked to judge.** `FulfillmentStockEndToEndTests.cs:169-171` replaced `response.EnsureSuccessStatusCode()` with `Assert.True(response.IsSuccessStatusCode, $"expected /stock/replenish to succeed; got … {responseBody}")`. **Legitimate strengthening.** The pass/fail predicate is unchanged (`IsSuccessStatusCode` is exactly what `EnsureSuccessStatusCode` throws on); only the diagnostic changes, by reading the Problem-JSON body the old call discarded. It cannot mask a failure — I proved that in both directions in this same probe: it still fails under the substitution, and it still passes when restored. The rejected first attempt is recorded honestly in the implementer's table row 4 rather than quietly replaced, which is the right handling of an arm that failed for the wrong reason.

### P7 — tree integrity after my probes

```
cmp …/rev72_bak/OrderReadModelMapper.cs src/Gateway/Domain/Projection/OrderReadModelMapper.cs   → cmp mapper: identical
cmp …/rev72_bak/GatewayRpcPayloads.cs   src/Gateway/Application/Rpc/GatewayRpcPayloads.cs       → cmp payloads: identical
cmp …/rev72_bak/GatewaySubjects.cs      src/Gateway/Application/Rpc/GatewaySubjects.cs          → cmp GatewaySubjects: identical
cmp …/rev72_bak/MoneyFieldSweep.cs      tests/Gateway.IntegrationTests/MoneyFieldSweep.cs       → cmp sweep: identical
git status --porcelain src/Gateway tests/Gateway.IntegrationTests
 M tests/Gateway.IntegrationTests/FulfillmentStockEndToEndTests.cs
 M tests/Gateway.IntegrationTests/OperatorNoteReachesTimelineEndToEndTests.cs
?? tests/Gateway.IntegrationTests/MoneyFieldSweep.cs
?? tests/Gateway.IntegrationTests/MoneyRepresentationHttpTests.cs
```

Classification: every file I mutated is byte-identical to its backup, restored from `cp`, never `git checkout --`. The four remaining entries are the feature's own two new files, its one strengthened assertion, and one file belonging to id 62 which is not mine to judge. **No residue.**

### P8 — the port of #7's R1 files, checked assertion by assertion myself

I read both of #7's files in full (`apps/gateway/src/test-support/money-field-sweep.ts`, `apps/gateway/src/money-representation.integration.spec.ts`) against #8's two, rather than checking that the record's table exists. The effective-currency walk (`:82` ↔ `:106`), the canonical-amount exception (`:93` ↔ `:122`), the number-under-currency-context rule (`:94` ↔ `:123`), leaf semantics (`:98` ↔ `:128`), indexed array paths (`:74` ↔ `:93`), the plain-object gate (`:77` ↔ `:100`) and the non-asserting return (`:114` ↔ `:79`) all correspond one-for-one. The test side likewise: the vacuity guard, `Number.isInteger` → `TryGetInt64`, `/^[A-Z]{3}$/` → the same regex, the five endpoints, the deliberately off-vocabulary `/credits` quartet, and the uniform per-endpoint loop. The one classified deviation — #7's spawned NestJS app with real Mongo/NATS versus #8's real Kestrel with faked `IRpcClient`/`IOrderReadModel` — is disclosed with its reason in both the record (row 4) and the test's own header, and is the seam `OrdersHttpTests`/`InvoicesHttpTests` already use. #7's own cell makes the same narrowing explicit (*"the Gateway alone"*). **PASS.**

### P9 — R24's closer and its counterpart (unit: the acceptance BULLET)

`feature_list.json` id 31 `api_tests` (phase 18, `pending`) acceptance bullet 4 carries the R24 obligation verbatim — the completion triple present **and** causally ordered, applied structurally rather than as a hand-written expected sequence, citing #7's amendment A1. The R24 cell names that feature and that id. **A deferral that names someone. PASS.**

### P10 — the Scoped paragraph against the legend's promise and #7's shape

The legend (`test-matrix.md`, Coverage summary, *Scoped* bullet) promises that *"the paragraph under the table says which rows hold which"*, and rule 3(b) requires a ratified row to name **who accepted the deferral and where**, plus **what closing it would take**. The new paragraph delivers all three for both scoped rows — R24 (gate of 2026-09-04, `progress/spec_order_saga_orchestrator.md` row 13; closer feature 31; closing = that feature shipping) and R56 (gate of 2026-09-10, `progress/spec_observability_reliability.md` row 4; closer id 28; closing = six processes plus a real Jaeger observing one trace id) — and additionally names R55's not-yet-green web half with its owners. Shape matches #7's own paragraph, which likewise names its single scoped row, its standing and its closing condition. The second paragraph narrates how the counts moved, which is the class the document's own reuse recipe (step 4) anticipates and tells the next assessment to delete. No stack-specific vocabulary leaks outside column 5. **PASS.**

## Findings

**B1 (BLOCKING) — the defect class was never enumerated, and it is still live in two rows.** `specs/shared/test-matrix.md` R48 (*"the Gateway's `POST /invoices/:id/payments` (features 25/29) does not exist yet"*) and R49 (*"no Gateway yet"*) are rows of exactly this feature's class, with premises that are false today: the endpoint ships at `src/Gateway/Presentation/Endpoints/InvoicesEndpoints.cs:32`, from feature 25, `done`. Neither the record nor the cells mention them, because the feature fixed the three rows the backlog named and ran no enumeration. `CLAUDE.md` is explicit that a class-themed loop's first task is one repository-wide enumeration as a search result *before any fix*, precisely so the entry is not closed while the class is still live — which is what would happen here. Why it matters beyond tidiness: the row's stale premise is the justification for R48/R49 not being proven at their sketch's own `API` level, so a false premise is propping up a coverage classification, in the file that is the project's traceability spine and the artefact #9 inherits.

**A1 (advisory) — the R1 currency arm names no field, and the bullet asked for one.** Acceptance bullet 2 requires each arm to fail *"with a message naming the endpoint and the field"*; the chosen mutation (currency forced to `null`) can only trip the vacuity guard, which names the endpoint alone. Disclosed verbatim rather than glossed, and the guard does meet the bar under a currency-invalidating mutation (P4), so this is a choice-of-arm defect, not a guard defect. Worth one sentence in the record and in the cell: with this design, a *total* loss of currency is detectable only as vacuity, and a *partial* loss — one money field losing its currency while others keep theirs — is silently dropped by the sweep on any response carrying more than one currency scope.

**A2 (advisory) — ledger row 3's distinguishing property has no case behind it.** Measured, not suspected: disabling `isCanonicalMoneyAmount` leaves the named test green (P5). The rule is faithfully ported and would bite on a decimal string, but no swept response contains an `amount` that is not already a JSON number, so nothing exercises it. One stub value (an `amount` emitted as a string) would turn the row's claim into a guard that can fail.

**A3 (advisory) — R61's API-half cell is green on a narrower assertion than its own column-4 sketch.** The sketch says *"tops up a stock item **without emitting a fact**, without touching any reservation and without advancing any order"*; the cited Gateway test asserts only `Units == 35`. The other two clauses are genuinely proven, in `tests/Fulfillment.IntegrationTests/StockReplenishTests.cs` (`ReservedUnits` unchanged, the reservation still `reserved`, `OutboxMessages` count `0`) — which is also where #7 proves them (`apps/fulfillment/src/stock-replenish.integration.spec.ts:19`). #8's evidence is strictly stronger than #7's gateway test, which asserts a stubbed reply's echoed `units` and never reaches a database. Citing that Fulfillment test in the R61 cell alongside the Gateway one would make the row's green rest on the whole sketch rather than a third of it.

**A4 (advisory) — #7's guards for the ported mechanism were not enumerated.** The port ledger for R1 is thorough and assertion-by-assertion, and it is the model. Nothing equivalent was done for R61, whose API half was also ported: #7's own R61 guards live in `apps/gateway/src/billing-fulfillment.integration.spec.ts:62` and `apps/fulfillment/src/stock-replenish.integration.spec.ts:19`, and neither is mentioned. I enumerated them myself and found nothing lost (A3 is where the difference lands), so this cost nothing this time — but the rule exists because two phase-13 features shipped branches whose #7 guard had quietly disappeared.

## What must change before re-review

1. **Run the enumeration and record it as a search result** — the command, its complete output, and one classification line per hit — across all 63 status cells, on the wording of the claim being retired (*"does not exist yet"*, *"no Gateway"*, *"closed by the gateway feature"*, *"features 25/29"*), not on the wording of the correction.
2. **Correct R48 and R49** in the same shape R24 was corrected: drop the false premise, name the real closer (feature 31 `api_tests`, whose acceptance bullet 3 already carries the duplicate-`paymentReference` obligation), and state what closing takes. #7's own R48 cell is the precedent for what the closed version reads like.
3. **Decide, in the cell, whether R48/R49 remain green** now that their disclosed shortfall is visible against the summary's own definitions, and if either moves, re-derive §6 and the Total. If they stay green, say why in one sentence — the shortfall is about the sketch's *level*, not about the requirement's wording — so the next reader does not have to re-open the question.
4. **A1–A4 are advisory**: fix or answer them in the record, but none of them blocks.

Re-review will be cheap: bullets 1–5 are verified above with my own probes and do not need re-doing. What I will re-check is the enumeration's completeness, the two corrected cells, and any count that moves.

## Time

Review round 1: 2026-09-12, ≈11:33 → 11:47 CEST (end time read off the clock, not estimated), ≈14 min, one session. Of that, roughly half was the four mutation probes — three builds plus one containerised run of the real Fulfillment host — and the rest the independent row-by-row recount, the class enumeration and the port comparison against #7's two files. No `./quality.sh` re-run, by design: the claim under test was three rows and two test files, not the full suite.

---

# Re-review (round 2) — after fix rounds 2 and 3

**Verdict: REJECTED** — one blocking finding (**B2**), new, and of exactly the class this entry exists to close. Round 1's B1 is **resolved**; A1–A4 are all **resolved**, and A2's closure was probed by me rather than read off the record. The three corrected cells (R48, R49, R24) are accurate in every file:line and feature-status claim they now make, and none of them has acquired a new unverified premise. The counts are right. The Green → Scoped ruling is right. **What is not done is the class**: a fourth live instance sits in `specs/shared/test-matrix.md`'s R46 cell, it was *hit by the round-2 enumeration and classified away*, and #7's own corrected R46 cell — on disk, one `grep` away — says in as many words that #7 had the identical stale sentence and fixed it.

**Status transition I would make (the leader owns `feature_list.json`; I made no edit):** id 72 stays **`in_progress`**.

## Method — what I ran, and what I took on the leader's verification

Per the brief I did not re-run `./quality.sh` or any full suite: no `.cs` file has changed since the round-2 green run (newest `.cs` under `src/`+`tests/` is `MoneyFieldSweep.cs` at 12:02, which is that run's own subject; round 3 was markdown-only), and the claims under test are cells and one test. I did not re-do round 1's bullets 1–5 — they were verified there with my own probes.

What I ran myself: **one full arming cycle on A2's new test** (mutate → `--no-incremental` build → named test → verbatim failure → `cp` restore → `cmp` → forced rebuild → green), an **independent enumeration of the population** (63 Status cells, both halves of every deferral), an **independent re-derivation of the counts by reading each cell**, a **path-anchored enumeration of the retired wording** across all 63 cells, **byte checks of columns 1–4 across the whole 63-row population** (not the five touched rows), **exact-line verification of every file:line the three corrected cells cite**, a **content check of the completeness claims** those cells make (four Gateway tests, no Gateway test naming `order.completed.v1`), and a **cross-read of #7's own test-matrix** for rows where #7 had already retired the same premise. Every command and its output is below.

Taken on the leader's independent verification, not re-done: the 1880 reconciliation by name, `./init.sh` exit 0 (§5d/§5b), `feature_list.json` untouched by the implementer, A3's Fulfillment citation read at source. I re-derived the columns-1–4 md5 anyway because it is one command: `7fec0fc060a6120f84e1297737ee8856` on both sides, 63 rows both sides. Confirmed.

## `CHECKPOINTS.md` — boxes walked

- [x] **C2** — at most one feature `in_progress` (`id 72`, and only 72; 83 entries, every status in `rules.valid_status`); `progress/current.md` describes this session and this feature.
- [x] **C3** — no architecture surface touched by this feature: it adds two test files, strengthens one assertion, and edits five Status cells. My own mutation was restored (`cmp` identical, line re-read, forced rebuild, green).
- [x] **C4** — no re-run owed; the round-2 `quality.sh` is green (18 projects, 0 failed, 1880) and no `.cs` has changed since. Integration tests hit real containers; no Jest; xUnit throughout.
- [x] **C7** — `specs/shared/` touched only in `test-matrix.md`'s Status column, the coverage summary and the two paragraphs under it. **Columns 1–4 byte-identical across all 63 rows**, HEAD vs working tree.
- [x] **C7** — the `R<n>` ids are #7's, and the corrected rows' realisations genuinely satisfy them (R48/R49 checked against #7's own cells; R24 against #7's amendment A1).
- [ ] **C7 — "no silent fork" in spirit, not in bytes: see B2.** `test-matrix.md` is exempt from parity by design, which is precisely why a divergence from #7's *corrected* cell is invisible to `init.sh` and has to be caught here.
- [ ] **C5 — `progress/history.md` still has no entry for this feature**, with or without its effort record. Expected at approval; the timestamps for it are gathered below so the round is not spent re-deriving them.

## 1. The enumeration's completeness, on the standard round 2 failed

**Population, counted by me:** 63 Status cells; ids `R1`–`R63` present, contiguous as a union, no duplicates (`python3` over `^\| \*\*(R\d+)\*\* \|`, printing one line per row — 63 lines, id set equal to `range(1,64)`).

I then tested **both halves of every cell that makes a deferral or an absence claim** — the closer half (*who closes it*) and the premise half (*why it is not closed yet*) — against the tree as it stands today.

| Row | Closer half | Premise half — tested against today's tree | Verdict |
|---|---|---|---|
| `R1` | historical only ("was closed by the gateway feature… closed here") | the A2 premise: *no swept response carries the standalone `Money{amount,currency}` shape, it appears only in `RegisterPaymentRequest`* — **true**: `grep -n "schemas/Money" specs/shared/openapi.yaml` returns exactly one hit, `:1792`, inside `RegisterPaymentRequest.amount`; `RegisterPaymentResponse` (`:1800`) has no money-shaped member. Measured, too: with the clause disabled the HTTP sweep stayed green (round 1, P5) | live, correct |
| `R24` | feature 31 `api_tests`, `pending` — verified in `feature_list.json` | corrected in round 3, and **every citation is exact**: `OrdersEndpoints.cs:21` *is* `app.MapGet("/orders/{id}", GetOrderAsync);`, `:80` *is* `GetOrderAsync`'s signature, `OrderReadModelMapper.cs:22` *is* `public sealed record OrderDetailView(` and its member list carries `IReadOnlyList<OrderReadModelEvent> Events` (`:34`, fed from `doc.Events` at `:98`), `StreamEndpoints.cs:43` *is* `app.MapGet("/orders/stream", StreamAsync);`. The remaining-gap claim is **true and complete**, and not only on the string the cell cites: no Gateway test names `order.completed.v1` (14 files, all Orders/Projector/Notifications/Contracts), and a content search of `tests/Gateway.*` for `causal`/`causationId`/`completed` finds only mapper-level unit assertions (`OrderReadModelMapperTests.cs:139`, `MongoOrderReadModelMappingTests.cs:143`) — a pass-through mapping, never the completion triple's presence and order | live, correct |
| `R46` | "feature 22's seam" — **feature 22 `billing_remittance_intake` is `done`, phase 10** | *"No live caller yet"* — **FALSE.** See **B2** | **STALE — blocking** |
| `R48` | feature 31 `api_tests` bullet 3, `pending` — verified | `InvoicesEndpoints.cs:32` *is* `app.MapPost("/invoices/{id}/payments", …)`; the four cited cases *are* at `:117`, `:133`, `:153`, `:169` (character-exact, I counted them out of the file); they *do* stub `IRpcClient` (`services.RemoveAll<IRpcClient>()` at `:97`); and the set is **complete** — `InvoicesHttpTests.cs` is the only file under `tests/` containing `/payments` at all | live, correct |
| `R49` | feature 31 `api_tests` bullet 5, `pending` — verified | same as R48, plus a claim about #7: *#7's R49 still reads "same NATS-level caveat as R48 (no Gateway yet)" after #7's R48 stopped carrying it* — **true**, read out of #7's checkout: #7's R49 cell begins `DONE — same NATS-level caveat as R48 (no Gateway yet)` while #7's R48 reads `DONE — now genuinely at the sketch's own api/ level (feature 31)…` | live, correct |
| `R55` | ids 29/30, both `pending` — verified | web half genuinely absent | live, correct |
| `R56` | id 28 `saga_e2e_verification`, `pending` — verified | composed-stack leg genuinely unproven | live, correct |
| `R29` | — | provenance prose, no closer | not applicable |

**Enumeration on the retired wording** (the rule: enumerate on the claim being retired, not the one being written), path-free because the population is one file's Status column:

```
python3 …  # over every | **R<n>** | row's last cell
pats = no live caller | uncalled | has no caller | no caller | does not exist | no Gateway |
       the gateway feature | until feature | not built by | owed to | seam | ships uncalled |
       no live path | not yet exist | exists yet | never built
population (Status cells): 63
R1  | [the gateway feature] …Was closed by "the gateway feature" (id 25, phase 13), which shipped without naming this row — closed here per feature 72.
R24 | [no Gateway]          …outstanding — **not** because no Gateway/API surface for order timelines exists (it does: …)
R24 | [no Gateway]          …returns Orders, Projector, Notifications and Contracts files only — no Gateway one.
R24 | [the gateway feature] …"The gateway feature" (id 25, phase 13) shipped the Gateway surface without naming this row…
R61 | [the gateway feature] …Was closed by "the gateway feature" (id 25, phase 13)…
R46 | [no live caller]      …No live caller yet (feature 22's seam) — the fact-emission rule applied with double force (`F8`), armed by deletion
R46 | [seam]                …(feature 22's seam)…
R48 | [does not exist]      …Was carrying a false premise ("does not exist yet") inherited unchanged…
R49 | [no Gateway]          …#7's own R49 cell … still reads "same NATS-level caveat as R48 (no Gateway yet)"…
R49 | [no Gateway]          …Was carrying a false premise ("no Gateway yet") inherited unchanged…
R55 | [not built by]        …owed to `apps/web` (features 29/30 … both still `pending`), not built by this feature.
R55 | [owed to]             …owed to `apps/web` (features 29/30 …)
total hits: 12
```

Classification, one line per hit: R1 ×1, R24 ×3, R61 ×1, R48 ×1, R49 ×2 — **all narration of a premise already retired by this feature**, correct. R55 ×2 — **live and true**. R46 ×2 — **stale, B2**.

## 2. The three corrected cells

Verified above, row by row, citation by citation. **No new unverified premise in any of them.** The strongest of the new claims are completeness claims (*"the four Gateway tests"*, *"no Gateway one"*) and both are true as **search results**, not only as prose — which is the right shape.

## 3. The Green → Scoped ruling, and whether any other cell has the same shape

The legend (`test-matrix.md:67`) defines Scoped as *"the named test exists and is green, but proves less than the requirement says, with the shortfall stated explicitly in the cell"*, and rule 3 (`:22`) requires the shortfall stated as *which leg is unproven* plus *what closing it would take*, and a named ratification. R48 and R49 stated their shortfall (*"proven one layer below the sketch's `API` level"*) and were counted Green. **The ruling is correct**, both halves of rule 3 are now satisfied in both cells (leg named; closing condition named; ratification named with date, review finding and backlog bullet), and the shortfall is real rather than rhetorical: the four Gateway tests stub the RPC client, so no test at the sketch's `API` level touches Billing.

**Is any other cell Green while stating a shortfall?** I swept all 63 cells for shortfall vocabulary (`only`, `unproven`, `one layer`, `narrower`, `no test`, `outstanding`, `yet`, `stub`, `does not reach`, `substitut`, `remains`, `deferred`, `pending`, `no claim`, `partial`, `shortfall`, `does not exist`, `no live caller`, `TODO`, `SCOPED`) and read every hit in context. Hits on R8, R9, R14, R44, R54, R60, R63 are the word *only*/*pending* inside **test case names** — not shortfalls. R1 and R61's *only*/*stub* hits describe **a superseded arm** and **#7's weaker test**, not a gap in #8's coverage: R61's sketch now has all three clauses proven across two named tests (A3), so Green is right. R24/R48/R49/R55/R56 are the four Scoped plus the one TODO. **No further Green-with-shortfall cell exists.** R46's hit is a false premise, not a shortfall — it does not move a count (see B2).

## 4. The counts, re-derived by reading

I classified each of the 63 cells by **reading the cell**, not by its leading token — the trap the brief names: `R24` leads with `INTEGRATION HALF DONE` and `R56` with `MECHANISM leg DONE, composed-stack leg unproven`, and both are Scoped, not Green.

Non-Green rows, by name: **`R24` Scoped**, **`R48` Scoped**, **`R49` Scoped**, **`R56` Scoped**, **`R55` not yet green**. Everything else Green. 63 − 5 = **58 Green, 4 Scoped, 1 not yet green**. Per section: §1 10/10/0/0, §2 8/8/0/0, §3 11/10/1/0, §4 8/8/0/0, §5 8/8/0/0, **§6 5/3/2/0**, §7 6/5/0/1, §8 6/5/1/0, §8.1 1/1/0/0. Green column sums 10+8+10+8+8+3+5+5+1 = **58**; Scoped 1+2+1 = **4**; not-yet-green **1**; 58+4+1 = **63**. **Matches the committed table exactly**, including §6's 5/3/2/0.

## 5. A1–A4, with A2 probed rather than read

**A2 — probed, and it is real.** The record's arming row is reproducible and the new test is genuinely discriminated by the clause:

```
# tests/Gateway.IntegrationTests/MoneyFieldSweep.cs:122
#   var isCanonicalMoneyAmount = property.Name == CanonicalAmountKey && hasOwnCurrency;
# → var isCanonicalMoneyAmount = false && property.Name == CanonicalAmountKey && hasOwnCurrency;
dotnet build --no-incremental tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj   # 0 Error(s)
dotnet test … --filter "FullyQualifiedName~MoneyFieldSweepCanonicalAmountTests"
  Failed  …MoneyFieldSweepCanonicalAmountTests.ACanonicalMoneyAmountThatHasRegressedToADecimalStringIsStillDiscovered [12 ms]
  Error Message:
   Assert.Single() Failure: The collection was empty
     at …MoneyFieldSweepCanonicalAmountTests.cs:line 43
Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1
```

Restored from my own `cp` backup (never `git checkout --`), `cmp` **identical**, line 122 re-read verbatim, `touch` + `dotnet build --no-incremental`, re-run: `Passed! - Failed: 0, Passed: 1, Total: 1`. The clause is the **only** path that can reach a string-typed `amount` (`isNumberUnderCurrencyContext` requires `ValueKind == Number`), the fixture is shaped like `openapi.yaml`'s own `Money` example, and the test asserts the finding's path, kind, value **and** currency — so it is a corruption-family guard, not only a deletion-family one. **A2 closed.**

**A1 — closed.** The cell now cites the currency-*shape* arm (`doc.Currency?.ToLowerInvariant()`) whose failure names endpoint **and** field — the message I observed myself in round 1's P4, character for character — and explicitly demotes the old `null` arm to a second, disclosed guard that satisfies nothing. Honest and exact.

**A3 — closed.** `tests/Fulfillment.IntegrationTests/StockReplenishTests.cs › HappyPath_UnitsUp_ReservedUnitsAndReservationsUntouched_OutboxEmpty` exists literally (`grep -c` → 1) and is now cited in R61 alongside the Gateway test, with the split of clauses stated. The leader read its assertions at source; I did not re-read them.

**A4 — closed.** #7's R61 guards are enumerated assertion by assertion in both files, each classified ported / not ported (with reason) / not applicable, including the honest *"#7 has no arming here at all"* line.

## B2 (BLOCKING) — the fourth instance: `R46` says there is no live caller, and there has been one since feature 22

`specs/shared/test-matrix.md`, R46's Status cell, verbatim:

> DONE — `tests/Billing.UnitTests/InvoiceTests.cs` › `R46_AllowsOnlyTheTransitionFromIssuedToPaid_…`. **No live caller yet (feature 22's seam)** — the fact-emission rule applied with double force (`F8`), armed by deletion

Every clause of that premise is false today, and the code says so in its own words:

- `src/Billing/Application/PaymentRegisterService.cs:12-13` — *"feature 22, the sole **LIVE caller** of `Invoice.MarkPaid` and `BuyerCredit.Release`"*; the call is at `:131` (`var paymentEventId = invoice.MarkPaid(markPaidInput, ctx, UniqueId.New);`) and its persistence at `:144`.
- It is **wired and reachable**: `src/Billing/Presentation/BillingRpcResponder.cs:68` subscribes `InvoiceSubjects.PaymentRegister` (`billing.payment.register`, `InvoiceSubjects.cs:16`), `:214` dispatches it, `:326` sends `RegisterPaymentCommand`.
- An **integration harness does reach the branch**: `tests/Billing.IntegrationTests/PaymentRegisterTests.cs` — its own header reads *"This is the sole LIVE caller of `Invoice.MarkPaid` … this file is what proves their existing guards hold through the live path, not only through their unit tests"* — against real MS-SQL, real NATS and real Kafka. That test is cited by name in **R47's and R49's own cells**, two rows below.
- `feature_list.json` id 22 `billing_remittance_intake`, phase 10, **`done`**.

**Why this is the same class and not pedantry.** It is shape (a) of acceptance bullet 6 precisely: a cell whose stated reason is an absence tied to a named feature, where the named feature has since shipped. It is the R24 defect exactly — the cell's *closer* half is fine (feature 22 is named and did the work) while its *premise* half asserts something about the system that stopped being true two phases ago. And the consequence is not cosmetic: the sentence is the justification for treating R46 under the fact-emission rule's **double-force** clause (*"applies with double force to branches that have no live caller yet, since integration harnesses… cannot reach them"*, `test-matrix.md:3`). That clause no longer applies, the live evidence exists, and the cell tells a reader the opposite.

**What makes it blocking rather than advisory is where the answer was.** #7's own R46 cell, in the checkout on this machine, reads:

> **Live-path evidence, added in the Phase 25 traceability pass.** This row used to close with "`markPaid` ships uncalled by any live path" — **stale since feature 22**: the caller is `apps/billing/src/application/payment-register.handler.ts` … and the live transition is proven against real infrastructure by `apps/billing/src/payment-register.integration.spec.ts` …

#7 had the identical stale sentence, found it, and replaced it with live-path evidence. #8 inherited the pre-correction wording and kept it. The same `grep` finds two siblings #7 also corrected in that pass and #8 does **not** carry (`R40` *"`consumeHold` has no caller until feature 21"*, `R41` *"`releaseHold` has no caller until features 22/25"* — #8's R40/R41 cells are one line each, clean), so the check is cheap and its result is a single row:

```
cd …/order-to-cash-nestjs && python3 …  # over #7's 63 Status cells
pats = used to close | stale since | traceability pass | live-path evidence | no longer
R40 | used to close → "consumeHold has no caller until feature 21" — stale since feature 21   (#8: clean)
R41 | used to close → "releaseHold has no caller until features 22/25" — stale: both shipped  (#8: clean)
R46 | used to close → "markPaid ships uncalled by any live path" — stale since feature 22     (#8: STILL CARRIES IT)
R55 | this cell previously ended "the web half remains owed to feature 26" — stale            (#8: names 29/30, correct)
R58 | DONE → SCOPED in the Phase 25 pass, then closed with per-site guards                     (#8: DONE, six services cited)
```

That is the third consecutive round in which the **search** worked and the **classification** lost an instance. Round 2's own hit list contains `R46 | pattern='no live caller'`, and the classification line written against it — *"Not applicable … a note about the fact-emission rule's double-force clause (no integration harness reaches the branch yet)"* — is itself the false claim, restated. The round-3 lesson (*"a deferral cell makes two claims, and hardening one does not harden the other"*) was drawn but applied only to R24; R46 was dismissed on a justification nobody checked against the tree.

**And this feature's own R49 correction states the principle it then missed here**: *"#7's own R49 cell shows what that residue costs … a stale pointer #8 must not inherit."* R46 is the mirror image — #7 **stopped** carrying the residue and #8 did not notice.

## Advisories

**A5 (advisory) — the same stale sentence lives in production source.** `src/Billing/Domain/Invoice.cs:24`: *"feature 22's seam. Delivered and unit-tested here; **uncalled until `billing.payment.register` exists**."* — that subject exists, is subscribed and is exercised. Two further instances of the same wording are correct in their own scope because they say *"in this feature"* rather than *"until"* (`BuyerCredit.cs:255`, `StockItem.cs:145`) — I classified them rather than leaving them unlisted. Fixing `Invoice.cs:24` is one comment line and is the natural companion to B2, but it is a `src/` edit, so it is the leader's call whether it belongs in this entry's scope or a backlog line.

**A6 (advisory) — the record's transferable lesson should be stated one level up.** Round 3 concluded *"when a hit's closer half was corrected in a prior round, re-read its premise half."* That is true and too narrow: R46's closer half was never corrected by anybody, and it was still lost. The lesson that would have caught all four is **every hit's premise half is a claim about today's tree and must be executed against it — a classification line that says "not applicable" is itself a claim and needs its own evidence**, plus the free cross-check this round added: **where #7's counterpart cell exists, read it, because #7 may have already retired the premise you are about to keep.**

## What must change before re-review

1. **Correct R46's Status cell** in the shape R24/R48/R49 were corrected: drop *"No live caller yet (feature 22's seam)"*, state the true fact with citations (`src/Billing/Application/PaymentRegisterService.cs:12`,`:131`; `src/Billing/Presentation/BillingRpcResponder.cs:68`,`:214`), and cite the live-path evidence that already exists — `tests/Billing.IntegrationTests/PaymentRegisterTests.cs › R47_RecordsThePayment_MovesTheInvoiceToPaid_ReleasesTheCreditHold_AndEmitsPaymentReceivedThenCreditReleasedInThatOrder` — exactly as #7's own R46 cell does. Keep the deletion-arming note; it is still true and still worth having. **R46 stays Green and no count moves** — verify that and say so.
2. **Record the enumeration correction as a search result**, including this round's #7 cross-check, and the corrected classification line for R46. State A6's lesson at the level it actually bites.
3. **Answer A5** — fix `src/Billing/Domain/Invoice.cs:24` or file it, but do not leave it unstated.
4. No suite re-run is owed if the change is markdown-only. If `Invoice.cs` is touched, a doc-comment-only edit still needs a build, and nothing else of mine or anyone else's may be building at the time.

Re-review will again be cheap: everything in sections 1–5 above is verified with my own commands and does not need re-doing. What I will re-check is R46's corrected cell, the corrected classification line, and the counts (which should not move).

## For the effort record, when this closes

`progress/history.md` has no entry for this feature yet (C5 unmarked above). Timestamps gathered while I was here, so the closing round does not have to re-derive them: feature started after id 76 closed ≈11:10 CEST 2026-09-12; round-1 implementation artefacts 11:08 (`FulfillmentStockEndToEndTests.cs`) → 11:14 (`MoneyRepresentationHttpTests.cs`); review round 1 11:33 → 11:47; fix round 2 11:43 (`GatewaySubjects.cs` restore) → 12:02 (`MoneyFieldSweep.cs`), with `MoneyFieldSweepCanonicalAmountTests.cs` at 12:01; leader verification then fix round 3 → `test-matrix.md` 12:32, record 12:33; re-review round 2 ≈12:35 → 12:5x. Three implementation passes, two reviews, one leader-found defect between them — and that shape is itself the finding worth recording against #7, whose own Phase 25 traceability pass is where it did this same work in one sweep.

## Time

Re-review round 2: 2026-09-12, ≈12:35 → 12:55 CEST, ≈20 min, one session. Of that, one full arming cycle (two `--no-incremental` builds and two filtered runs, ≈45 s of machine time), the rest enumeration and cross-reading against #7's checkout. No `./quality.sh`, no full suite, no source left mutated: `cmp` clean, line re-read, forced rebuild, green.

---

# Re-review (round 3) — after fix round 4

**Verdict: APPROVED.** `R46`'s corrected cell is accurate in every file:line and status claim it makes, it has acquired **no new unverified premise**, the counts do not move and are right when re-derived by reading, and **no fifth instance of the class exists** — tested by three independent instruments, one of which does not depend on the wording of the claim at all. Three advisories, all in `progress/impl_*.md` rather than in `specs/shared/test-matrix.md`; **A7 and A8 are two one-line record corrections I ask the leader to route before the closing commit**, not a reason to hold the feature.

**Status transition I would make (the leader owns `feature_list.json`; I made no edit):** id 72 `in_progress` → **`done`**.

## Method — what I ran, and what I took on the leader's verification

Per the brief I re-ran no suite and started no build: no `.cs` file changed in round 4 (newest `.cs` under `src/`+`tests/` is stamped 12:48, the git-incident restat, not an edit — content proven identical by the leader's `git diff a0460a6` → 0 files differing), and the claims under test are one cell, one classification line and one restated lesson. I ran **only read-only commands**: `grep`, `find`/`xargs`, `python3`, and `git show`/`git diff`/`git status`/`git log`. **No `git stash`, `reset`, `restore`, `clean` or `checkout`** — I read `CLAUDE.md:78-88` on disk first, and every "as committed" read in this round was `git show HEAD:<path>`.

Taken on the leader's independent verification, not re-done: `./init.sh` exit 0, the 1879/1 + isolated 61/61 suite position and its backlog id 85 routing (I did read id 85's entry: the class is enumerated in its acceptance — 8 fixture files, 12 bindings, 8 `GetFreeTcpPort` copies, with the five `NatsContainerFixture` files named as the in-tree control — so the routing is real, not a sentence). I re-derived the whole-population columns-1–4 md5 anyway because it is one command: **`7fec0fc060a6120f84e1297737ee8856`, 63 rows, both sides of HEAD**. Rows differing from HEAD: exactly `R1`, `R24`, `R46`, `R48`, `R49`, `R61` — the six this feature has touched across four rounds, and nothing else.

## `CHECKPOINTS.md` — boxes walked

- [x] **C2** — exactly one feature `in_progress` (id 72, verified by reading `feature_list.json`: 84 entries, `in_progress: [72]`); `progress/current.md` describes this session and this feature, and carries the leader's round-4 ruling at `:1549-1561`.
- [x] **C3** — no architecture surface touched by round 4: `specs/shared/test-matrix.md` Status column only. `src/Billing/Domain/Invoice.cs` is **byte-identical to HEAD** (md5 `19ce38a72f92…` both sides), so A5's stale comment was correctly left for id 78 rather than fixed here.
- [x] **C4** — no re-run owed: no `.cs` changed in round 4. Integration evidence cited by the corrected cell hits real containers (`PaymentRegisterTests.cs` boots the real Billing host against real MS-SQL/NATS/Kafka); xUnit throughout; no Jest.
- [x] **C7** — `specs/shared/` touched only in `test-matrix.md`; columns 1–4 byte-identical across all 63 rows; `git status --porcelain specs/shared/` shows one file.
- [x] **C7** — the `R<n>` ids are #7's, and `R46`'s realisation genuinely satisfies `B8`/`B9` (below). The #7 cross-check that produced round 2's B2 is now discharged: #7's corrected `R46` and #8's now say the same thing for the same reason.
- [ ] **C5 — `progress/history.md` still has no entry for this feature.** Owed at approval, with the effort record. The timestamps are gathered in re-review round 2's closing section and extended below; **nothing beyond the effort record is owed there**, in my view — the class enumeration, the #7 cross-check and the four instances all live in `progress/impl_*.md` and in this file, and duplicating them into history would create a fourth copy to go stale. One line I would ask for: that this feature took **four implementation passes and three reviews**, because that shape is the measurement, and #7 did the same work in one Phase 25 sweep.

## 1. `R46`'s corrected cell — every claim checked at source

Cell as it now stands (`specs/shared/test-matrix.md:169`), claim by claim:

| Claim in the cell | How I checked it | Verdict |
|---|---|---|
| `tests/Billing.UnitTests/InvoiceTests.cs` › `R46_AllowsOnlyTheTransitionFromIssuedToPaid_…` | literal-occurrence check of all 132 cited case names in this file against their cited test files (script, §2) — this one occurs | exact |
| *"armed by deletion (`F8`) when no live caller existed"* | unchanged in substance from the pre-round-4 cell; now tensed as history, which is what it is | correct, and correctly demoted to a historical note |
| *"Live since feature 22 `billing_remittance_intake` (`done`, phase 10)"* | read out of `feature_list.json`: id 22, `billing_remittance_intake`, `done`, phase 10 | exact |
| `src/Billing/Application/PaymentRegisterService.cs:12` | `sed -n '12p'` → `/// LIVE caller of <see cref="Invoice.MarkPaid"/> and <c>BuyerCredit.Release</c>,` | exact |
| `…:131` | `sed -n '131p'` → `var paymentEventId = invoice.MarkPaid(markPaidInput, ctx, UniqueId.New);` | exact |
| **"is the *sole* live caller of `Invoice.MarkPaid`"** — the one genuinely NEW claim, and a completeness claim | path-anchored enumeration, not prose: `find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \| xargs -0 grep -n "MarkPaid"` → 62 hits, read one by one. In `src/`, exactly **one** call of the domain method: `PaymentRegisterService.cs:131`. Everything else is `IInvoiceRepository.MarkPaidAsync`/`EfCoreInvoiceRepository`/`PaymentRowMapper` (the persistence method, a different member), doc-comments naming it, or `src/Orders/Domain/Order.cs:193` + `SagaStepTable.cs:246` — **`Order.MarkPaid`, a different aggregate's method**, which is the one way this claim could have been wrong and is not | **true, and verified as a search result** |
| `src/Billing/Presentation/BillingRpcResponder.cs:68`, `:214` | `sed -n '68p'` → `SubscribeLoopAsync(InvoiceSubjects.PaymentRegister, stoppingToken),`; `sed -n '214p'` → the dispatch arm `InvoiceSubjects.PaymentRegister => await HandlePaymentRegisterAsync(…)` | exact |
| *"wired"* — i.e. actually reachable, not merely present | the responder is a `BackgroundService` (`:50,:54`) **registered** at `src/Billing/Infrastructure/BillingServiceCollectionExtensions.cs:85` (`services.AddHostedService<BillingRpcResponder>();`), and `PaymentRegisterService` at `:57`. A subscription in an unregistered class would have satisfied the cited lines and nothing else | true — I checked the registration, not only the subscription |
| *"Proven against real infrastructure by `PaymentRegisterTests.cs` › `R47_RecordsThePayment_…InThatOrder`"* | the case exists at `:26`; read its body in full. It seeds an invoice **`"issued"` with `paidAt: null`** (`:37`), drives the real `billing.payment.register` subject over a real NATS connection against the real host, and then asserts **`invoiceRow.Status == "paid"` and `invoiceRow.PaidAt` non-null read back out of MS-SQL** (`:61-63`), plus the reply's own `"paid"`/`PaidAt` (`:55-56`) | **true, and it is genuinely R46's own subject** — `issued → paid` with `paidAt` set, at the live path, not a neighbouring property |

**Has it acquired a new unverified premise while shedding the old one?** One candidate — *"sole live caller"* — and it is the strongest kind of claim to make carelessly, because it is a completeness claim about the whole tree. It was written as a search result and it survives mine. **No other new premise.** Note what the corrected cell does *not* do, which is right: it does not claim the integration test arms anything, does not claim `R46` is proven at API level, and does not restate the double-force clause as still applying.

## 2. The fifth instance — three instruments, one of them wording-independent

The brief's first-order concern: three rounds running, the search found the hit and the classification cleared it wrongly. So I did not re-run their sweep and read their classification; I built my own population and tested **both halves of every deferral or absence claim in all 63 cells**.

**Instrument 1 — I read all 63 Status cells in full.** Extracted mechanically (`python3` over `^\| \*\*(R\d+)\*\* \|`, last cell), 63 rows, ids `R1`–`R63` contiguous and unique, dumped and read end to end. The cells making a deferral or an absence claim are exactly nine: `R1`, `R24`, `R29`, `R46`, `R48`, `R49`, `R54`, `R55`, `R56`.

**Instrument 2 — an absence/modality sweep with MY OWN vocabulary**, chosen without reference to the implementer's pattern list (`will`, `once`, `awaits`, `future`, `TODO`, `missing`, `lacks`, `cannot`, `not exist`, `absent`, `still`, `yet`, `when … ships`, `unbuilt`, `unarmed`, `nobody`, `no test`, `none of`, `never`, `remains`, `pending`, `deferr`, `owed`, `unproven`, `one layer`, `does not reach`, `has no`, `no live`, `not built`, `unclosed`, `outstanding`, `shortfall`, `scoped`). Population 63 cells, **54 hits**, complete output captured, every hit read in context. It returns the same nine rows plus `R19`/`R23`/`R58`/`R60`/`R63`, whose hits are inside **test case names and descriptions of what a test does** (`…AndDoesNothingOnASecondPark`, "the owed-nothing absence", "never a faked failure", "scoped to `POST /auth/login`") — classified, not a deferral among them.

**Instrument 3 — the wording-independent one, which is the answer to "three rounds of classification failures".** A sweep keyed on vocabulary can only find a stale premise whose author happened to phrase it in the vocabulary. So I enumerated, for every one of the 63 cells, **every feature id and every backlog feature-name it mentions, and resolved each against `feature_list.json` as it stands today**. Ten rows name a feature; every status is what the cell says it is:

```
R1  : 25=gateway_rest_auth:done(ph13), 72=…:in_progress(ph14)          → historical closer reference; correct
R24 : 16=done, 25=done, 31=api_tests:pending(ph18), 72=in_progress      → closer 31 pending; correct
R29 : 16=done, 27=observability_reliability:done(ph14)                  → provenance; correct
R46 : 22=billing_remittance_intake:done(ph10)                           → cell says done, phase 10; correct
R48 : 25=done, 31=pending, 72=in_progress                               → closer 31 pending; correct
R49 : 25=done, 31=pending, 72=in_progress                               → closer 31 pending; correct
R54 : 25=gateway_rest_auth:done(ph13)                                   → "Gateway half DONE"; correct
R55 : 25=done, 26=gateway_sse_push:done(ph13), 29=pending, 30=pending   → web half owed to 29/30; correct
R56 : 28=saga_e2e_verification:pending(ph15)                            → closer 28 pending; correct
R61 : 25=done, 72=in_progress                                           → historical closer reference; correct
```

**This is the instrument that would have caught `R48`/`R49`/`R24`/`R46` without anyone guessing the right search word**, because it keys on the *thing that goes stale* (a feature's status) rather than on the *sentence that describes it*. It is one `python3` block, and I recommend it below as the standing method.

**Premise halves, tested against today's tree rather than against a prior classification:**

| Row | Premise half | Evidence I ran this round | Verdict |
|---|---|---|---|
| `R1` | the standalone `Money{amount,currency}` shape appears only in `RegisterPaymentRequest` | `grep -n "schemas/Money" specs/shared/openapi.yaml` → **one hit, `:1792`**, inside `RegisterPaymentRequest.amount` | live, true |
| `R24` | the surface exists; what is missing is a Gateway-level test asserting the completion triple | `find tests -name '*.cs' -not -path '*/bin/*' … \| xargs grep -ln "order\.completed\.v1"` → **14 files, zero under `tests/Gateway.*`**; `OrdersEndpoints.cs:21,:80`, `OrderReadModelMapper.cs:22`, `StreamEndpoints.cs:43` all exact (round 2) | live, true |
| `R29` | split inherited from #7's gate, carried without re-raising per `requirements.md` §1.1 | `requirements.md` carries R29 at `:227` and the invariant mapping at `:560`/`:563`/`:564`; ratification row 1 of `progress/spec_order_saga_orchestrator.md` | provenance, no closer — not applicable |
| `R46` | *"live since feature 22"* | §1 above, every clause | **live, true — the round-2 defect is closed** |
| `R48`/`R49` | endpoint ships; the four Gateway tests stub `IRpcClient` and never reach Billing's DB | `InvoicesEndpoints.cs:32` is `app.MapPost("/invoices/{id}/payments", …)`; `:97` is `services.RemoveAll<IRpcClient>();`; `:117`, `:133`, `:153`, `:169` are the four case signatures, character-exact; **`InvoicesHttpTests.cs` is the only file under `tests/` containing `/payments`** | live, true |
| `R54` | Gateway half done; ratified at the `projector_read_model` gate, open point 2 | `progress/spec_projector_read_model.md:22` is that row, verdict **RATIFY**, and it says in as many words that approving the spec *is* the ratification | live, true |
| `R55` | the web half is owed to ids 29/30 and built by nobody | ids 29/30 `pending`; **`apps/` does not exist in this repository at all** (`ls apps/` → no such file or directory) | live, true |
| `R56` | composed-stack leg unproven, deferred to id 28 | id 28 `pending`; `find tests … \| xargs grep -lni jaeger` → **zero hits**; ratification row is `progress/spec_observability_reliability.md:33`, verbatim *"the split is already written into the shared matrix's own `R56` row and defers the composed-stack half to feature 28"* | live, true |

**Two further citation-level sweeps, because a stale premise can also hide as a citation that no longer resolves:**

- **Every path cited in any Status cell**: 148 distinct citations, all exist, **except two which are #7 paths** (`apps/gateway/src/money-representation.integration.spec.ts`, `apps/fulfillment/src/stock-replenish.integration.spec.ts`) — both present in #7's checkout, both cited as #7's, correct.
- **Every cited test case name occurs literally in its cited file**: 132 names checked, **zero drift**. One apparent mismatch was my script's own defect, not the file's: `R60` cites brace-expanded paths (`tests/{Orders,Notifications,Projector}.UnitTests/KafkaHealthCheckLongRunningTests.cs`), which my "nearest preceding path" heuristic mis-associated; the name exists in all three real files, and `MsSqlHealthCheckPoolingTests.cs:43` carries its own cited name. Classified, not waved through.

**Conclusion: there is no fifth instance.** Both halves of all nine deferral/absence cells hold against today's tree.

## 3. The corrected classification line, and whether the earlier conclusion is properly marked wrong

**The corrected line exists and is right**, at `progress/impl_…md:424`: it states that `R46`'s only remaining hit is its own past-tense historical clause, that the row was read in full rather than pattern-matched, and that the premise is now true. I confirmed the "only remaining hit" claim independently — the leader's sweep and both of mine agree.

**Was the earlier conclusion quietly edited?** No — and that matters, so I say it plainly: `:207`'s original R46 classification (*"Not applicable … a note about the fact-emission rule's double-force clause"*) is **still there, verbatim**, and `:211` still carries the round-2 result marked `(INCORRECT — see Fix round 3 below)`. Nothing was rewritten to look right in hindsight. That is the correct instinct and the harder one.

**But the correction has itself gone stale — see A7.** `:211` says *"It was three."* It was four. And `:207`'s disproved line carries no forward pointer, so a reader who stops at §1 reads the false classification with no marker on it. The true count is stated at `:365` ("the fourth stale instance"), a hundred and fifty lines later.

## 4. A6 restated — does the statement bite, and would it have caught `R46`?

Round 4's §4 states it as: *every hit a sweep returns is a claim about today's tree, and the claim must be executed against today's tree — grepped, read at the cited line, checked against `feature_list.json` — every time the sweep runs, regardless of whether the row was edited in a prior round; a classification line saying "not applicable" is itself a claim and needs its own evidence; and #7's corrected cells are the cheapest oracle available and should be diffed against before, not after, a rejection names the gap.*

**Would it have caught `R46`? Yes, and I checked that rather than assuming it.** `R46` **was** in round 2's hit list — the classification table at `:199-207` contains an `R46` row, matched on `no live caller`. So the failure was entirely in the classification step, and the restated rule attaches the obligation to exactly that step: *"not applicable" is itself a claim and needs its own evidence*. Applied at round 2, one `grep` for `MarkPaid` would have ended it. Round 3's narrower wording (*"when a hit's closer half was corrected in a prior round…"*) demonstrably would **not** have caught it, since no round had touched `R46`'s closer half. The restatement is at the right level.

**Where it is still one level short, and this is an advisory rather than a finding (A10):** it hardens *classification* and leaves *enumeration* keyed on vocabulary. A stale premise phrased in words nobody guessed never becomes a hit, and a rule about hits cannot reach it. Instrument 3 above closes that gap mechanically for shape (a), and it costs one `python3` block.

## 5. The counts, re-derived by READING each cell

I classified every one of the 63 cells by reading it, not by its leading token — the trap the brief names, which has caught the leader twice: `R24` opens `INTEGRATION HALF DONE` and `R56` opens `MECHANISM leg DONE, composed-stack leg unproven`, and both are **Scoped**, not Green.

Non-Green rows, by name: **`R24` Scoped** (API half outstanding, ratified, closer id 31), **`R48` Scoped**, **`R49` Scoped** (both state the one-layer-below shortfall and name id 31), **`R56` Scoped** (composed-stack leg, closer id 28), **`R55` not yet green** (`TODO`, web half). Every other cell states no shortfall against its own requirement's wording. 63 − 5 = **58 Green, 4 Scoped, 1 not yet green**.

Per section, read row by row: §1 `R1`–`R10` 10/10/0/0 · §2 `R11`–`R18` 8/8/0/0 · §3 `R19`–`R29` 11/10/1/0 · §4 `R30`–`R36`+`R61` 8/8/0/0 · §5 `R37`–`R44` 8/8/0/0 · **§6 `R45`–`R49` 5/3/2/0** (`R45`,`R46`,`R47` Green; `R48`,`R49` Scoped) · §7 `R50`–`R55` 6/5/0/1 · §8 `R56`–`R60`+`R62` 6/5/1/0 · §8.1 `R63` 1/1/0/0. Green 10+8+10+8+8+3+5+5+1 = **58**; Scoped 1+2+1 = **4**; not-yet-green **1**; 58+4+1 = **63**.

**Matches the committed table exactly, including §6's 5/3/2/0 and the "Four rows" paragraph, which names `R24`/`R48`/`R49`/`R56` and never `R46`.** The leader's ruling that `R46` stays Green is right on the legend's own terms: Scoped is *"the named test exists and is green, but proves **less** than the requirement says"*, and `R46`'s requirement is the `issued → paid` transition with `paidAt` set (`B8`/`B9`) — which its unit test proves in full and its integration test now proves again at the live path. *"No live caller"* was never a shortfall against that; it was a note about how much arming the `F8` double-force clause demanded, and that clause's premise is simply spent.

## Advisories

**A7 (record; I ask the leader to route it before the closing commit).** `progress/impl_…md:211` reads *"It was three."* — it was four, and the sentence sits in the record's own corrective voice, immediately after a marker announcing that the previous count was wrong. `CLAUDE.md`: *a claim of completeness is a count, and a count is a reading*; *a number that does not reconcile is a finding, not a footnote*. This is the entry's own defect class one level up, in the artefact that documents the class: a correction that outlived its own correctness. Two one-line edits fix it — change `:211` to "It was four (`R24` here, and `R46` in fix round 4)", and append to `:207`'s R46 row a forward pointer such as *"**DISPROVED in fix round 4** — see §1 of that round."* No code, no build, no re-run; `test_maintainer` tier.

**A8 (record; same routing).** Round 4's §2 presents its enumeration as *"Complete output: **29 hits**, rows `R1`(1), `R24`(5), `R29`(3), `R61`(1), `R46`(1), `R48`(5), `R49`(5), `R55`(4), `R56`(3) — 29 total, hand-counted against the printed list, matches the script's own `total hits: 29`."* **The per-row list sums to 28, not 29** — I reproduced the exact command and pattern set: total **29**, and `R24` has **6** hits, not 5. So the row classifications are right and the tally that vouches for them is not, and *"hand-counted against the printed list"* cannot have been done against a printed list, because the list was not printed. That is the precise reason the standard asks for **complete output and one classification line per hit**: a compressed per-row summary is the one form in which a miscount is invisible, and the hits' text is exactly what a reader needs to check a classification. Recommend correcting the tally to `R24`(6) and, in future enumerations, printing the hit lines.

**A9 (informational, no action).** `R46` cites `BillingRpcResponder.cs:68,214`; at HEAD those lines are `:67,:213`, because an **uncommitted** one-line `using OrderToCash.Contracts.Rpc;` insertion from another in-flight feature (id 76) shifted the file by one. The cell is correct against the working tree, which is this repository's convention for every line citation in the file, and `PaymentRegisterService.cs:131` is unshifted. Recorded only so that nobody re-derives it as a mismatch if id 72's markdown-only commit lands before id 76's.

**A10 (recommendation for the record, and for #9).** Make instrument 3 the standing method for this class: *for every Status cell, resolve every feature id and name it mentions against `feature_list.json` today*. It is wording-independent, it produces a table a reader can check line by line, and it would have found all four instances of shape (a) in one pass at round 1. Worth a sentence in `progress/impl_…md` §4 beside the restated A6, since A6 governs classification and this governs enumeration.

**A5 — confirmed routed, not narrated.** `src/Billing/Domain/Invoice.cs:24` still carries *"uncalled until `billing.payment.register` exists"* (byte-identical to HEAD, correctly untouched by this feature), and **backlog id 78 acceptance bullet 7 names it explicitly** — file, line, the false clause, the evidence that it is false, and, crucially, an instruction to enumerate the retired wording across `src/` and `tests/` **by path exclusion** before any edit, because *"id 72's own enumeration found the test-matrix half of this class; the `src/` half was never swept."* That is a numbered entry that outlives this feature, which is what the rule requires; I read it rather than taking the routing on report.

## Effort record — for the leader to write into `progress/history.md`

Extending re-review round 2's gathered timestamps: fix round 4 → `specs/shared/test-matrix.md` 12:48, `progress/impl_…md` 12:50; the git incident at 12:48:59 and its diagnosis; the post-incident `./quality.sh` (1879 passed / 1 failed / 1880, the failure environmental and filed as id 85) and the isolated `Gateway.IntegrationTests` re-run 61/61; re-review round 3 13:05 → 13:25 CEST. **Whole feature: four implementation passes, three reviews, one leader-found defect between rounds 2 and 3, one git incident.** Against #7, whose Phase 25 traceability pass did this same work in a single sweep — that comparison is the number worth recording, more than the wall-clock.

## Time

Re-review round 3: 2026-09-12, ≈13:05 → 13:25 CEST, ≈20 min, one session. No build, no test run, no mutation — the claims under test were a cell, a classification line and a lesson, and all three are answerable by reading and by enumeration. Read-only git throughout (`show`/`diff`/`status`/`log`); nothing left alive; nothing left mutated because nothing was mutated.
