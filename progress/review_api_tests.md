# review: api_tests (id 31, phase 18)

**Verdict: APPROVED**, with one condition: a single sentence in the R49 Status cell must be corrected before the id 31 commit (F1 below). It is a mechanical text fix, so under CLAUDE.md's wrap-up rule the leader can make it directly. Id 31 is set to `done`, and its effort record is appended to `progress/history.md`.

Reviewer: Opus, full process. Review session 2026-09-17, 12:32Z → about 13:00Z.

## 0. What I ran and what I did not

- **I did not repeat the implementer's three full runs.** I ran the project **once**, after all arms were restored and the project was rebuilt with `--no-incremental`. Nothing was listening on 9092, 1433, 4222 or 27017 (`ss -ltn | grep -E ':(9092|1433|4222|27017)\b'` found nothing, exit 1), and `docker ps` showed 0 containers. Result: **`Passed! - Failed: 0, Passed: 77, Skipped: 0, Total: 77, Duration: 9 m 5 s`**. The TRX counters agree (`total="77" passed="77" failed="0"`), and all 7 `BlackBoxApiTests` facts passed.
- **Six arms of my own**, each a single named test (§2).
- **`tests/Architecture.Tests`:** 50/50.
- **`./init.sh`:** exit 0.
- **`dotnet format --verify-no-changes`** on the test project: exit 0.
- **Out of scope, not reviewed:** `SagaEndToEndVerificationTests.cs` and the id 104 files.

## 1. Acceptance bullets (read verbatim from `feature_list.json`) → tests

| Bullet | Test | Verified by |
|---|---|---|
| full happy path | `HappyPath_ReachesCompleted_WithTheCompletionTripleCausallyOrdered` | read; green in the full run |
| full compensation path | `NinetyNineOrder_CompensatesVisibly_WithTheCompensationChainCausallyOrdered` | read; arm B |
| duplicate paymentReference yields one payment | `DuplicatePaymentReference_YieldsExactlyOnePayment_InBillingsOwnDatabase` | read; arm F; note N4 |
| R24 API half, structural causal order | `HappyPath_…` via `AssertCausalOrder`, plus meta-guard `AssertCausalOrder_WhenAnEffectPrecedesItsCause_FailsNamingTheViolation` | arm A, run against the **production** sort, D4's shape |
| R49 API half: amount, currency and already-paid each rejected with a code, and Billing's own DB unchanged | `R49_AMismatchedAmount_…`, `R49_AMismatchedCurrency_…`, `R49_ADifferentPaymentReferenceAgainstAnAlreadyPaidInvoice_…` | arms C, D and E; the implementer's arms 2 and 3 cover the rejection-code half |

**`test-matrix.md` scope check.** I compared the working tree with `HEAD` cell by cell.
- Exactly 2 lines differ (126 and 172, the R24 and R49 rows), and in each only column index 5 (Status) changed.
- Against #7's checkout, all 63 `R<n>` rows have identical columns 1–4. For R24 and R49 the only differing column is Status.

**`feature_list.json`.** Id 31's only change is the status line. The other hunk in the diff (id 104) is the leader's light close and outside this review.

## 2. Arming, run by me

**Method.**
- I copied each file to the session scratchpad (`bk/`) and applied the mutations with an exact-match Python replace, asserting exactly one hit per replacement.
- **Deviation from the protocol, stated openly:** I applied all six mutations in one build, then ran each named test **alone** with `--no-build`, and checked that each failure fired on its intended assertion.
- Afterwards I restored all four files from backup: `cmp` reported identical for all four, and `git diff --stat` on the three `src/` files was empty. I rebuilt with `--no-incremental` and confirmed from the DLL timestamps that the rebuild happened. The full run was green (§0).

| Arm | Mutation | Test | Verbatim failure |
|---|---|---|---|
| **A**, R24 / D4 shape (reverse the tiebreak) | `src/Projector/Infrastructure/Persistence/TimelineOrder.cs:105` `[DepthField] = 1` → `-1` (effects sort before causes within a tie) | `HappyPath_…` | `causal order violated: order.confirmed.v1 (index 2) names causationId 45e5f1d1-…, but its cause (index 3) does not precede it — events: order.placed.v1,stock.reserved.v1,order.confirmed.v1,credit.approved.v1,order.despatched.v1,invoice.issued.v1,order.completed.v1,credit.released.v1,payment.received.v1` at `BlackBoxApiTests.cs:525` ← `:193` |
| **B**, compensation reason (payload corruption) | `src/Orders/Domain/CancellationReason.cs:30` `CreditRejected => "credit_rejected"` → `"stock_rejected"` (wire token only) | `NinetyNineOrder_…` | `Assert.Equal() Failure: Strings differ / Expected: "credit_rejected" / Actual: "stock_rejected"` at `:221` |
| **C**, R49 currency (the implementer did not arm this one) | `src/Billing/Domain/Invoice.cs:326` currency guard → `if (false && …)` | `R49_AMismatchedCurrency_…` | `Expected: UnprocessableEntity / Actual: Created` at `:395` |
| **D**, R49 amount, the **database** half | `Invoice.cs:331` amount guard neutralised **and** the two HTTP assertions at `:362-363` commented out, so the database half is reached | `R49_AMismatchedAmount_…` | `Assert.Equal() Failure: Strings differ / Expected: "issued" / Actual: "paid"` at `AssertBillingDatabaseUnchangedAsync :466` |
| **E**, R49 already-paid, the **database** half | `Invoice.cs:321` B8 guard neutralised **and** `:450-451` commented out | `R49_ADifferentPaymentReference…` | `Expected: 1 / Actual: 2` at `:454` (a real second `payments` row) |
| **F**, duplicate count (substituted identifier) | test-side: count predicate `== paymentReference` → `== paymentReference + "-X"` | `DuplicatePaymentReference_…` | `Expected: 1 / Actual: 0` at `:314` |

**What these arms add to the implementer's.**
- The implementer's R24 arm (arm 5) exercised only the helper. **Arm A** runs D4's own shape against the shipped projector, the same probe #7's review 3 ran (#7 `progress/review_api_tests.md` §4). It fails inside the helper, on a tie group nobody wrote a hand-written check for.
- The completion triple is fully reversed in that output. So the order is set by ledger row L4's depth key, not by chance: a green result with `+1` and a red result with `-1` probe the claim in both directions.
- The implementer's arms 2 and 3 fail on the HTTP status, which is asserted *before* the database checks, so those checks had never been seen to fail. **Arms D and E** show the database half reads real state and catches a write.
- The implementer's arm 1 failed by timeout (the domain precondition refused the corrupted reason). **Arm B** corrupts the wire value instead, and the failure names the claim.

## 3. Black-box?

**Yes.**
- **The Gateway is real.** `GatewayTestHost.StartAsync` (`tests/Gateway.IntegrationTests/GatewayTestHost.cs:62-88`) builds the real `GatewayHost` with `--urls http://127.0.0.1:0`, calls `app.StartAsync()` (real Kestrel), and returns an `HttpClient` bound to `app.Urls.First()`.
- **Nothing is overridden.** `SagaFleet.BuildAsync` passes **no** `overrideServices` (`SagaEndToEndVerificationTests.cs:752-757`), so there is no stand-in for `IRpcClient`. Auth goes through the real `/auth/login`.
- **The test writes nothing directly.**
  - Command: `grep -n "SaveChanges\|ExecuteSql\|ExecuteUpdate\|ExecuteDelete\|\.Add(\|Remove(\|ProduceAsync\|IRpcClient\|overrideServices\|StandIn" tests/Gateway.IntegrationTests/BlackBoxApiTests.cs`
  - Output: nothing (exit 1). There are no write APIs, no broker production and no stubs in the file.
  - Every direct database read is `AsNoTracking` and read-only: Billing through `OpenBillingDb`, Fulfillment through `mssql.CreateDbContext`.
  - Every other interaction goes over HTTP, including invoice resolution (`GET /invoices?orderReference=`).
- **Fixture seeding** (the Fulfillment catalog and the Billing credit line) happens in `SagaFleet` setup. #7 also seeded by hand (#7 spec `:40-48`, raw SQL), so this matches.

## 4. Inventory of #7's assertions

**Command:** `grep -c "expect(" ../order-to-cash-nestjs/apps/gateway/src/black-box-api.integration.spec.ts` → **33**.

| Where | Lines | Count |
|---|---|---|
| Scenario 1 | 489, 490, 491, 494, 497, 508, 509, 512, 521 | 9 |
| Scenario 2 | 535, 538, 562–566, 570, 579, 596, 597 | 11 |
| Scenario 3 | 607, 619, 620, 623, 624, 625, 633 | 7 |
| Scenario 4 | 640, 643, 646 | 3 |
| Helpers | 157 (`assertCausalOrder`), 479, 481 (`findInvoice`) | 3 |
| **Total** | | **33** |

**How the record's inventory compares.**
- **All 30 scenario assertions are classified**, plus `:157` through the `assertCausalOrder` row, so **31 of 33**.
- **Two are not classified: `:479` and `:481`**, `findInvoice`'s status assertion and its non-empty assertion. Both are ported in form: `FindInvoiceAsync` calls `EnsureSuccessStatusCode()` and polls until `items` is non-empty. See N2.
- **The "verbatim structure" row overstates the port.** #7's `:566` asserts `creditRejectedIndex < orderCancelledIndex` directly, and #8 does not. #8's two assertions imply it transitively, so no coverage is lost (N2).
- **The scenario 4 dispositions are correct.** The cited tests exist at the cited lines: `AuthAndRateLimitHttpTests.cs:46` (`Me_WithoutABearerToken_Returns401`), and `OrdersHttpTests.cs:139` (404) and `:151` (400, with a doc comment naming #7's scenario 4).

**Ledger rows spot-checked against the files:**
- **L1:** #7 spec `:1-13` does describe the real, spawned Gateway reached only through supertest, and `:55` imports `spawnRealService`. The #8 half, `GatewayTestHost.cs:62-88`, is as described. **Holds.**
- **L3:** #7 spec `:44-48` does set `PRD-0001` to 24999 "precisely so quantity 1 totals .99". **Holds.**
- **L5:** `src/Orders/Application/Sagas/SagaFactHandler.cs:292` is exactly `order.Cancel(cancel.Reason(fact), cancel.CompensationSteps(fact), fact.OccurredAt, UniqueId.From(fact.EventId), note);`. **Holds.**
- **L4**, the row most likely to be taken on trust, is **probed by arm A**. The depth key in `TimelineOrder.cs` is what puts the ties in causal order. `causationId` reaches the wire through `OrderReadModelEvent.CausationId` (`src/Gateway/Domain/Projection/OrderReadModelDocument.cs:23-29`); arm A's failure message shows the helper resolved real edges.

## 5. The collection split

**The explanation is sound in substance.**
- Both classes keep a static `SagaFleet` that is torn down only by a collection-level teardown fixture. In one shared collection, the second class's fleet starts while the first is still alive.
- A separate collection means xUnit disposes `SagaE2ECollection`'s fixtures (including `SagaFleetTeardown`) before `BlackBoxApiCollection` starts, because assembly-level `DisableTestParallelization = true` runs collections one at a time.
- One phrase in the record is imprecise: "serialises collections, not classes within one collection". In xUnit v2, classes within one collection also run one at a time; the real problem is that the two fleets were **alive at the same time**, and the record's next sentence says exactly that (N3).

**The two fleets are independent.**
- **Containers:** `BlackBoxApiCollection` (`BlackBoxApiTests.cs:658-665`) declares its own `ICollectionFixture<>` for Kafka, MS-SQL, NATS and Mongo. xUnit creates collection fixtures per collection, and none of the four fixture types holds a static container (`grep -n static` finds only a private static helper in `KafkaContainerFixture.cs:100`). So each fleet has its own broker, SQL server, NATS server and Mongo.
- **Databases:** names carry a fresh GUID (`otc_*_e2e_{Guid:N}`, `SagaEndToEndVerificationTests.cs:688,696,715,735`).
- **Topics:** the names are the same constants, but they live on different brokers.
- Neither suite can pass on the other's data.

## 6. CHECKPOINTS.md

### C1
- [x] The five root files exist; `init.sh` exit 0 (run by me).
- [x] The `progress/` files exist.
- [x] The agent definitions are present. `init.sh` checks this and passed.
- [x] Every agent declares its model (checked by `init.sh`; not re-read by eye).

### C2
- [x] No feature is `in_progress`. After this edit, id 31 is `done`.
- [x] Every status is valid (`init.sh`).
- [x] Every `done` feature has passing tests. Id 31's 7 facts are green.
- [x] `progress/current.md` describes this phase.
- [x] No `blocked` features.

### C3
- [x] Domain purity: `Architecture.Tests` 50/50 (NetArchTest, run by me).
- [x] No cross-service database access in production. The test's direct reads of Billing and Fulfillment are test-only verification, the same idiom as `SagaEndToEndVerificationTests` (ledger L2).
- [x] No new shared runtime code; no `src/` change remains from this feature (the `git diff` of the mutated files is empty).
- [x] `SharedKernel` and `Cqrs` untouched.
- [x] No `decimal` introduced (the test uses `long` amounts).
- [x] No new interaction. The suite drives existing HTTP → NATS RPC → Kafka paths.
- [x] No debug logging or TODOs in the new file.

### C4
- [ ] `./quality.sh`: **not run by me.** It is the leader's wrap-up gate, per the brief. I ran instead: the full Gateway integration project, `Architecture.Tests`, and `dotnet format` on the project.
- [x] Domain tests are untouched.
- [x] Integration tests use Testcontainers, with the developer infrastructure shown to be down.
- [n/a] Coverage: the new file is an integration test and does not affect the domain gates.
- [x] No Jest.

### C5
- [x] No stray untracked files. The three new untracked files are this feature's and id 104's.
- [x] The `history.md` entry and effort record are appended by me.
- [x] `feature_list.json`: id 31 set to `done`, one line.
- [ ] Human told what was done and how to test it: the leader's job at report time. Manual check: `dotnet test tests/Gateway.IntegrationTests --filter FullyQualifiedName~BlackBoxApiTests`.
- [x] No commit.

### C6
N/A as a feature spec: id 31 is `sdd: false`, so its ledger is correctly in `progress/impl_api_tests.md`. The one applicable box:
- [x] R24 and R49 each map to named, armed tests (§1).

### C7
- [x] `specs/shared/` matches #7 except the `test-matrix.md` Status column. `diff -rq` differs only in `test-matrix.md`, and a row-by-row comparison of all 63 `R<n>` rows shows identical columns 1–4. The file-level line-count difference (227 vs 247) already exists at `HEAD`, outside this feature.
- [x] No new deviation from `specs/shared/`.
- [x] The R24 and R49 realisations meet the requirement text (arms A, C, D, E).
- [n/a] n8n: untouched.
- [x] The black-box script proves the same saga steps, facts and compensation as #7's scenarios 1–3. Scenario 4 is covered elsewhere, with citations (§4). R49 goes beyond #7, which never closed it at API level.
- [x] Effort record: appended, including the comparison with #7.
- [n/a] README benchmark: updated at wrap-up.

## 7. Findings

**F1 (condition of approval; mechanical; fix before the id 31 commit).** The R49 Status cell in `specs/shared/test-matrix.md:172` says: *"Armed, two of three (the third is the identical code path as the first)"*.
- **This is false.** The currency refusal is a separate branch, `Invoice.cs:326-329`, with its own error type, `InvoicePaymentCurrencyMismatchError`. Only the downstream classification (→ 422 `PAYMENT_MISMATCH`) is shared. If that branch were deleted, the amount test would not notice.
- **Why it matters.** A Status cell is the trilogy's traceability record, and this one gives a wrong reason for an unarmed guard.
- **The guard itself is sound.** Arm C shows it fails as it should.
- **Fix:** replace the parenthetical with arm C's evidence: *"`Invoice.cs:326`'s currency guard neutralised → the currency test fails 'Expected: UnprocessableEntity / Actual: Created' (armed at review)"*. "Two of three" becomes "all three".
- This is an edit to one Status cell only, not an SA.

**N1 (non-blocking; recommend a backlog entry).** The helper can pass without checking anything. `AssertCausalOrder` (`BlackBoxApiTests.cs:503-531`) skips every entry whose `causationId` is absent or names nothing in the timeline. If `causationId` stopped reaching the wire, `HappyPath_…` would stay green on a broken system.
- #7's approving review recorded the same risk as N7, with the fix: one counter, asserting edges checked > 0.
- The port carried the helper but not that recorded caveat, which is the "port the guards too" shape.
- Today it is not vacuous: arm A's failure names a resolved edge.
- **Disposition:** leader files a test-only, light backlog entry ("`AssertCausalOrder` asserts at least one edge was checked; armed by dropping `CausationId` from the Gateway's `OrderReadModelEvent` mapping"). It does not stem from `specs/shared/`.

**N2 (non-blocking; record accuracy).** The inventory classifies 31 of #7's 33 `expect(` calls.
- `:479` and `:481` (inside `findInvoice`) are missing, although both are ported in form.
- The scenario 2 "verbatim structure" row omits that `:566`'s direct `credit.rejected < order.cancelled` assertion is only implied transitively in #8.
- No coverage is lost. **Accepted, not fixed.**

**N3 (non-blocking).** The collection-split explanation says xUnit "serialises collections, not classes within one collection". Classes within a collection also run one at a time; the real cause is that both static fleets are alive together. The code comment at `BlackBoxApiTests.cs:38-45` states this correctly. **Accepted, not fixed.**

**N4 (non-blocking; accepted with evidence).** Nothing can make the duplicate-count guard's "more than one" direction fail except this assertion itself.
- Two things always fire first: the HTTP 200/`duplicate` assertion at `:302`, and the unique index `IX_payments_payment_reference` (`src/Billing/Infrastructure/Persistence/Configurations/PaymentConfiguration.cs:22`).
- Arm F proves the count reads real rows.
- **Re-open only if** the unique index is ever dropped.

**N5 (non-blocking).** Several count assertions (`:314`, `:436`, `:454`, `:459`) use bare `Assert.Equal` with no message. Their failures ("Expected: 1 / Actual: 2") name the claim only through the test name and line number. **Accepted, not fixed.**

**N6 (process).** `progress/impl_api_tests.md` has no effort record. I rebuilt it from the subagent transcript timestamps (see `history.md`).

**Nothing rooted in `specs/shared/`.** No SA is needed, and no gap is being deferred to "the next feature".

## 8. Before commit

- F1: the one-cell text correction. The leader makes it, or routes it to `test_maintainer`.
- N1: the leader files the backlog entry with its disposition. The reviewer does not write `feature_list.json` beyond id 31's status.
