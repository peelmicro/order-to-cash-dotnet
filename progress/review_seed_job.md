# review_seed_job — feature 12 (`seed_job`), phase 7

**Verdict: REJECTED**

One blocking defect, in the one artefact this feature's own `progress/current.md` correctly named as its highest risk. Everything else in this feature is excellent, and I want that on the record before the defect: I independently reproduced #7's *entire* seed dataset by executing #7's own TypeScript modules, and **every row #8 wrote matches it exactly** — 413 reference rows, 6 orders, 11 order lines, 50 outbox rows with their payloads, and all 6 `order_timeline` documents field-for-field. The port is right. The **guard over the port is not**, and the guard is the whole reason this feature carries a test suite at all.

---

## What I ran, and what I deliberately did not

| Ran | Why |
|---|---|
| `./init.sh` | exit **0** ("no feature in_progress", 11/42 done, 6 agents, backlog coherent) |
| `./quality.sh` (full) | the claim under test *is* about the full suite. exit **0**, all green, **163 tests**, wall-clock **1 m 45 s** (`real 1m45.007s`) |
| Executed #7's own `deterministic.ts`, `gln.ts`, `*.data.ts` and `mongo.writer.ts` under `node --experimental-transform-types` | the parity-oracle claim (concentration point 1). Not a retyping — #7's real source files, imports rewritten only to resolve `@otc/shared-kernel` and `mongodb` |
| Direct `sqlcmd` against live `otcnet-mssql` (all 3 DBs) and `mongosh` against live `otcnet-mongodb` | concentration points 2 and 3. Full row dumps, diffed in Python against the #7 oracle |
| 4 of my own mutation probes (A–D), built and executed | concentration point 7. Probes B–D built and run against real containers in an out-of-tree copy of the repo — **the working tree was never modified** (`git status` at the end is byte-identical to `git status` at the start) |
| `Architecture.Tests` (12/12) inside the `quality.sh` pass | C3 requires running NetArchTest, not eyeballing it. `OrderToCash.Seed.Domain` **is** in `tests/Architecture.Tests/DomainAssemblies.cs`, so the new Domain folder is genuinely in scope |

| Did NOT run | Why |
|---|---|
| The row-count/checksum comparison against **#7's live MySQL** | acceptance item 4; needs #7's containers alongside #8's, a decision the human has not taken. Correctly deferred by the implementer — see "the deferred half" below |
| A second `dotnet run --project src/Seed` against the live stack | the implementer ran it twice; I verified the *result* (the live databases) directly and at value level, which is strictly stronger than re-running the producer |
| Web / Playwright / n8n | no web or Gateway surface exists at phase 7 |

---

## Concentration point 1 — is the parity oracle independent? **YES. Confirmed, and it is better than claimed.**

I did not re-run the implementer's recorded `node -e` snippet (a retyped snippet proves nothing about the file it was retyped from). I executed **#7's actual source files**: `apps/seed/src/deterministic.ts`, `packages/shared-kernel/src/domain/gln.ts` and all of `apps/seed/src/data/*.data.ts`, copied verbatim into a scratch directory with only the two unresolvable module specifiers (`@otc/shared-kernel`, `mongodb`) repointed. Node 24, `--experimental-transform-types`, exit 0.

`#7's own apps/seed/src/deterministic.spec.ts contains no hardcoded expected values` — I checked. So these values could not have been copied out of #7's tests; they can only have come from executing #7's code, exactly as claimed.

Every one of the 17 hardcoded expectations in `tests/Seed.IntegrationTests/DeterministicParityTests.cs` matches:

| Test line | Namespace / sequence | Expected in `DeterministicParityTests.cs` | #7's own module produced | |
|---|---|---|---|---|
| 31 | `currency:USD` | `8a2ac568-0944-4507-872a-38acbce9724c` | `8a2ac568-0944-4507-872a-38acbce9724c` | match |
| 32 | `currency:EUR` | `23ab1a2b-bce4-4b83-8304-6b5a1084990c` | `23ab1a2b-bce4-4b83-8304-6b5a1084990c` | match |
| 33 | `currency:GBP` | `ac45711f-e2ac-456b-975f-8aef85070564` | `ac45711f-e2ac-456b-975f-8aef85070564` | match |
| 34 | `retailer:CarrefourEs` | `0e47f181-c92e-416a-bff1-5c8d497768b1` | `0e47f181-c92e-416a-bff1-5c8d497768b1` | match |
| 35 | `order:1` | `1741d5aa-cfba-4205-a1c0-82e7a5cb8984` | `1741d5aa-cfba-4205-a1c0-82e7a5cb8984` | match |
| 36 | `product:PRD-0001` | `1164d610-b1d9-493c-980e-1b4a37d00e1e` | `1164d610-b1d9-493c-980e-1b4a37d00e1e` | match |
| 37 | `stock:IBERFOODS:PRD-0002` | `9ad0863b-e61e-4e1d-9488-c60850b82779` | `9ad0863b-e61e-4e1d-9488-c60850b82779` | match |
| 78, 79 | `makeEan13(1)`, `(12)` | `5901000000012`, `5901000000128` | identical | match |
| 86–93 | `makeGln(1..7)`, `(21)` | `5400000000010` … `5400000000072`, `5400000000218` | identical | match |

**Not self-referential.** The namespaces are also #7's own (`currency:${code}`, `retailer:${code}`, `order:${sequence}`, `product:${code}`, `stock:${company}:${product}` — read out of #7's `*.data.ts`), so the scheme is verified, not only the hash.

## Concentration point 6 — the skipped hex character. **Preserved, commented, and load-bearing.**

`src/Seed/Domain/Deterministic/DeterministicId.cs:36` is `var timeHiAndVersion = "4" + hex[13..16];`, preceded by a 9-line `// WART, PRESERVED DELIBERATELY` comment naming #7's file and explaining that parity is judged by resulting bytes. Correct.

**Probe A (analytic, no build needed — the conclusion is exact):** recomputing the derivation with `hex[12..15]` instead:

| Namespace | With the wart (= #7) | "Fixed" to `hex[12..15]` |
|---|---|---|
| `currency:USD` | `8a2ac568-0944-**4507**-872a-38acbce9724c` | `8a2ac568-0944-**4250**-872a-38acbce9724c` |
| `order:1` | `1741d5aa-cfba-**4205**-a1c0-82e7a5cb8984` | `1741d5aa-cfba-**4120**-a1c0-82e7a5cb8984` |
| `retailer:CarrefourEs` | `0e47f181-c92e-**416a**-bff1-5c8d497768b1` | `0e47f181-c92e-**4716**-bff1-5c8d497768b1` |

All 7 `[InlineData]` rows would fail. The wart is genuinely guarded. (My Python re-implementation of the warted derivation also reproduces the oracle exactly — a third independent confirmation.)

---

## Concentration point 3 — dataset fidelity. **Exact, at value level, on every table I could reach.**

I dumped #7's dataset by executing its modules, dumped #8's live tables with `sqlcmd`, and set-diffed them in Python on **every business column** (not counts).

| Table | #8 live rows | #7 oracle rows | Columns compared | Missing | Extra |
|---|---|---|---|---|---|
| `otc_orders.currencies` | 3 | 3 | id, code, isoNumber, symbol, decimalPoints | 0 | 0 |
| `otc_orders.products` | 12 | 12 | id, code, **ean**, name, description, **price**, currency | 0 | 0 |
| `otc_orders.retailers` | 7 | 7 | id, code, name, country, vat, **gln**, currency | 0 | 0 |
| `otc_orders.companies` | 22 | 22 | id, code, name, country, vat, **gln**, currency | 0 | 0 |
| `otc_billing.credits` | 154 | 154 | id, code, retailer, company, **creditLimit**, currency | 0 | 0 |
| `otc_fulfillment.stock` | 215 | 215 | id, company, product, **units**, reservedUnits, **lowStockThreshold** | 0 | 0 |
| `otc_orders.orders` | 6 | 6 | id, ref, orderDate, company, retailer, currency, initialAmount, initialDiscount, totalAmount, status, **cancellationReason**, updatedAt | 0 | 0 |
| `otc_orders.order_items` | 11 | 11 | orderRef, product, description, quantity, **price**, discount | 0 | 0 |
| `otc_orders.outbox` | 17 | 17 | id, eventId, eventType, aggregateId, correlationId, causationId, occurredAt, publishedAt, **payload (parsed JSON)** | 0 | 0 |
| `otc_fulfillment.outbox` | 12 | 12 | same | 0 | 0 |
| `otc_billing.outbox` | 21 | 21 | same | 0 | 0 |

Spot checks the brief asked for, all confirmed against #7: `PRD-0001` price `24999` / EUR; `PRD-0009` `379` / **GBP**; `PRD-0011` `1749` / **USD**; `CarrefourEs` GLN `5400000000010`; `IBERFOODS` GLN `5400000000218`; credit limit `500000` on all 154 lines; stock `500` units / threshold `20`; the master-data timestamp `2026-01-01T00:00:00.000Z` matches #7's `MASTER_DATA_TIMESTAMP` exactly.

**Sample orders — 5 completed + the 1 cancelled, all present with the right cancellation reason:**

| Ref | Status | Retailer → Company | Total | cancellation_reason |
|---|---|---|---|---|
| ORD-000001 | completed | CarrefourEs → IBERFOODS | 16130 | NULL |
| ORD-000002 | completed | CarrefourFr → FRESHFR | 10374 | NULL |
| ORD-000003 | completed | LeroyMerlinEs → TOOLIBERIA | 19450 | NULL |
| ORD-000004 | completed | AldiDe → GERMANFOODS | 23972 | NULL |
| ORD-000005 | completed | AldiGb → UKDISTRIB | 10055 | NULL |
| ORD-000006 | **cancelled** | CarrefourEs → IBERFOODS | **24999** (`% 100 == 99`) | **`credit_rejected`** |

## Concentration point 5 — GLNs. **All 29 valid, and the failure path is real.**

All 7 retailer + 22 company GLNs round-trip through `SharedKernel.GLN`'s own validating constructor (`DatasetTests.Every_Seeded_Gln_Is_Valid_Per_Gln`, and again live: every `gln` column value equals #7's `makeGln(n)`). `Gs1Identifiers.MakeGln` (`src/Seed/Domain/Deterministic/Gs1Identifiers.cs:36-70`) obtains the check digit *from `GLN` itself* by trying the ten candidates and keeping the one the constructor accepts — a single source of truth, not a duplicated mod-10 formula — and throws `InvalidOperationException` rather than returning an unvalidated value. The implementer's arming entry #2 exercised that throw for real. Accepted.

---

## Concentration point 2 — the `order_timeline` documents. **Content exact; guard absent.**

I generated #7's expected documents by executing **#7's own `toTimelineDocument`** (from `apps/seed/src/writers/mongo.writer.ts`), dumped all 6 live documents from `otc_read_model.order_timeline`, and diffed them key-by-key after normalising BSON `Long` to integer.

**Result: zero key differences and zero value differences across all 6 documents.** Walked against Databases doc §8:

| §8 field | Live BSON type | Value vs #7 | |
|---|---|---|---|
| `_id`, `orderId` | string, string | identical, and `_id == orderId` | ok |
| `orderReference` | string | `ORD-000001`…`006` | ok |
| `orderDate` | **string** (not BSON date — matches #7's `.toISOString()`) | identical, `…T09:00:00.000Z` | ok |
| `retailer`, `company` | subdocument `{code,name,gln}` | identical incl. `Carrefour España` / `5400000000218` | ok |
| `status` | string | identical | ok |
| `cancellationReason` | **null** on completed, `"credit_rejected"` on ORD-000006 | identical | ok |
| `currency` | string | identical | ok |
| `totals` | `{initialAmount, initialDiscount, totalAmount}` as **Int64** | values identical | ok (see A2) |
| `items[]` | `{productCode, name, quantity(int32), unitPrice(Int64), lineDiscount(Int64)}` | identical | ok |
| `references` | `{despatchReference, invoiceReference, paymentReference}` — all three **null** on the cancelled order, `DES-000001`/`INV-000001`/`PAY-SEED-000001` on ORD-000001 | identical | ok |
| `events[]` | `{eventId, eventType, occurredAt, summary, detail?, causationId}`, `detail` omitted when absent | identical; 9 events on completed, 5 on cancelled, ordered by `occurredAt` | ok |
| `headerComplete` | boolean `true` | identical | ok |
| `updatedAt` | string | identical | ok |
| `statusRank` (internal) | int32 — 98 completed / **99** cancelled | identical to #7's PR12 table | ok |
| `processedEventKeys[]` (internal) | array of string, `projector:<eventId>`, ordinal-sorted | identical | ok |
| `timelineOrderVersion` | int32 `2` | identical (#7 Amendment A1) | ok |

Cancelled-order compensation sequence in the live document: `order.placed.v1 → stock.reserved.v1 → credit.rejected.v1 → stock.released.v1 → order.cancelled.v1`, `detail` present on `credit.rejected.v1` and `order.cancelled.v1`. Matches `saga.md` §4.2 Path B and #7 byte for byte.

Index: `uq_order_reference`, `unique: true`, `partialFilterExpression: { orderReference: { $type: 2 } }` — the partial-index reasoning ported correctly.

**So the artefact is right. Now the guard.**

---

## My arming probes — the blocking finding

Probes B–D were applied to an **out-of-tree copy** of the repository (`git status` unchanged before and after), rebuilt, and run against real Testcontainers.

| # | Mutation | File / line | Suite result | Verdict |
|---|---|---|---|---|
| A | `hex[13..16]` → `hex[12..15]` (un-skip the wart) | `src/Seed/Domain/Deterministic/DeterministicId.cs:36` | all 7 `[InlineData]` rows would fail (shown analytically, values above) | **CAUGHT** |
| C | `HeaderComplete = true` → `false` | `src/Seed/Infrastructure/Mongo/MongoSeedWriter.cs:133` | `Order_Timeline_Documents_Carry_Every_Field_With_The_Right_Types` **FAILED** at `SeedIntegrationTests.cs:179` (`Assert.True() Failure — Expected: True, Actual: False`) | **CAUGHT** |
| **B** | **`CausationId = entry.CausationId.ToString()` → `string.Empty`** — i.e. every causal edge in every seeded timeline blanked | `src/Seed/Infrastructure/Mongo/MongoSeedWriter.cs:130` | **`Passed! Failed: 0, Passed: 38`** | **SURVIVED** |
| **D** | **`TotalAmount = saga.TotalAmount` → `saga.TotalAmount + 1`** — every seeded timeline total off by one cent, and now inconsistent with `otc_orders.orders.total_amount` | `src/Seed/Infrastructure/Mongo/MongoSeedWriter.cs:97` | **`Passed! Failed: 0, Passed: 38`** (run with B simultaneously; green means neither was caught, so there is no attribution ambiguity) | **SURVIVED** |

---

## Defects

### D1 — BLOCKING. The `order_timeline` documents' *values* are unguarded: two independent mutations survive a green 38-test suite.

- **Where:** `tests/Seed.IntegrationTests/SeedIntegrationTests.cs:148–200` (`Order_Timeline_Documents_Carry_Every_Field_With_The_Right_Types`), against `src/Seed/Infrastructure/Mongo/MongoSeedWriter.cs:85–140` (`ToTimelineDocument`).
- **What:** the test is a *presence and shape* test wearing a value test's name. The specific holes my probes went through:
  - **`SeedIntegrationTests.cs:185** — `Assert.All(completed.Events, e => Assert.NotEqual(Guid.Empty.ToString(), e.CausationId))` is the **only** assertion on `causationId` anywhere in the repository. It passes for `""`, for `null`, and for any wrong-but-non-zero GUID. Probe B blanked all 50 causal edges in all 6 documents and the suite stayed green.
  - **`SeedIntegrationTests.cs:172`** — `Assert.True(completed.Totals!.TotalAmount > 0)`. Probe D corrupted every total and the suite stayed green.
  - **`SeedIntegrationTests.cs:173`** — `Assert.NotEmpty(completed.Items)`: no `productCode`, `name`, `quantity`, `unitPrice` or `lineDiscount` is ever asserted.
  - **`SeedIntegrationTests.cs:165–168`** — only `Retailer.Code` and `Company.Code`; `Name` and `Gln` (the party identifiers the whole GLN machinery exists for) are never asserted in the document.
  - **`SeedIntegrationTests.cs:164, 180`** — `OrderDate` and `UpdatedAt` are only `NotNull` / `NotEmpty`; any wrong instant survives.
- **Why it matters, and why it is blocking rather than an advisory:**
  1. **CLAUDE.md is explicit and doubles the force here:** "Every branch that emits … a domain fact must be guarded by a test that fails when the emission is deleted … **with double force where the branch has no live caller yet, because integration harnesses cannot reach it.** #7 learned this twice, on two different features, both correct code with no guard." That is this situation precisely: the code is correct, the guard is absent, and there is no live caller — the projector is Phase 12.
  2. **`causationId` is not decoration.** #7's own `mongo.writer.ts` header states that without it "a seeded document's tie groups have no causal edges and stay on PR31's eventId fallback permanently (PR35) **even after the projector's own boot migration runs, because the migration NEVER invents an edge that was never recorded**." A blank `causationId` here is silently unrecoverable later.
  3. **`progress/current.md` (this session's own note) says it best:** "a wrong shape here would surface five phases later as a projector bug, which is an expensive place to find it." The risk was correctly identified and then not covered by a test.
  4. The implementer's own arming table has three entries, **none** of which touches the timeline document's contents — the highest-risk artefact is the one artefact that was not armed.

### D2 — Advisory (Contracts, not seed_job). JSON key order diverges from #7 on 14 of 50 outbox payloads.

- **Where:** `src/Contracts/Facts/Payloads/StockReservedPayload.cs:8–12` declares `RetailerCode` **last** (it carries a `= null` default), so the seed emits `{"orderReference","companyCode","reservations","retailerCode"}` where #7 emits `{"orderReference","companyCode","retailerCode","reservations"}`. All 50 payloads are **semantically identical** to #7's (I parsed and deep-compared every one: 0 differences); 14 differ only in key order.
- **Why it matters:** CLAUDE.md says "The JSON wire shape must match #7 **byte for byte**." Key order is part of "byte for byte". This is not a seed_job defect — it originates in an already-approved feature and the seed merely serialises the Contracts records — but this feature is the first place it becomes observable, and the seeded rows are already `published_at`-stamped, so the practical impact is nil today. **Record against Contracts / the phase that owns wire-parity proof; do not fix it inside this feature.**

### D3 — Advisory. Weak count assertions where exact ones are free.

`SeedIntegrationTests.cs:98` asserts `fulfillmentCounts.Stock > 0` where the live and oracle value is exactly **215**. `reservations` (11), `despatch_items` (10) and `invoice_items` (10) are never asserted at all, though every other count on that test is exact.

### D4 — Advisory. Near-vacuous test.

`DeterministicParityTests.cs:109–115` (`DeterministicId_Fails_The_Parity_Oracle_If_The_Skipped_Hex_Character_Is_Reintroduced`) asserts `NotEqual` against the correct value with its last character hand-edited, then `Equal` against the correct value. The second line makes the first redundant and the first proves nothing about the wart (probe A shows the real un-warting changes the *third* group, not the last character). Harmless, but it is a test whose name claims more than its body does.

### D5 — Advisory. Report/filesystem mismatch.

`progress/impl_seed_job.md:35` lists `Domain/MasterDataTimestamp.cs`; the file is at `src/Seed/Domain/Data/MasterDataTimestamp.cs`. Trivial, but the file list is the artefact a reviewer navigates by.

### D6 — Advisory. Pure tests live in a container-carrying project.

`DatasetTests.cs` and `DeterministicParityTests.cs` are pure (no DB, no broker) but sit in `tests/Seed.IntegrationTests`, which references `MongoDB.Driver`, EF Core and Testcontainers. They pass and they are genuinely framework-free in their bodies, so C4's "domain tests are pure" holds in substance — but the project name misreports what they are, and it means they cannot be run without the integration project's dependency graph.

---

## Acceptance-item traceability (`sdd: false` — the contract is `feature_list.json` #12's four items)

| # | Acceptance item | Named test(s) | My independent verification | Guarded? |
|---|---|---|---|---|
| 1 | "same currencies, products, retailers, companies, GLNs, credit limits and stock as #7" | `DeterministicParityTests` ×5 (17 oracle values), `DatasetTests.Three_Currencies_Are_Seeded` / `At_Least_Ten_Products…` / `Seven_Retailers…` / `At_Least_Twenty_Companies…` / `Every_Retailer_Company_Pair_Has_A_Baseline_Credit_Line` / `Every_Seeded_Gln_Is_Valid_Per_Gln` / `Every_Seeded_Gln_Is_Unique…` / `Every_Non_Saga_Company_Has_Full_Stock_Coverage_Per_Product`, `SeedIntegrationTests.Row_Counts_Match_The_Datasets_Own_Declared_Sizes` | **413 rows value-diffed against #7's executed modules — 0 differences** | **YES** (probe A caught) |
| 2 | "sample completed orders and one cancelled order, with their `order_timeline` documents" | `DatasetTests.Five_Completed_Sagas_And_One_Cancelled_Saga_Are_Seeded`, `The_Cancelled_Saga_Total_Ends_In_Point_Ninety_Nine_Cents`, `SeedIntegrationTests.Order_Timeline_Documents_Carry_Every_Field_With_The_Right_Types` | 6 orders + 11 items + 50 outbox rows + 6 timeline documents all value-identical to #7 | **NO — D1.** Probes B and D survive |
| 3 | "idempotent — running twice is a no-op" | `SeedIntegrationTests.Running_The_Seed_Twice_Is_A_No_Op` | The test **does** run the seed twice against the same live stack and compares a real SHA-256 checksum over row *values* (`ComputeChecksumAsync`, lines 202–252: orders, orders-outbox **including full payload**, stock, reservations, credits, credit_items, timeline docs) — **not merely row counts.** Concentration point 4 satisfied. Armed by the implementer (arm #3, `EfUpsert` always-`Add` → `InvalidOperationException` on the second run). Weakness: the checksum omits products/retailers/companies/order_items/despatches/invoices/payments and all `updated_at` columns, so a *changing* rewrite confined to those would pass — noted, not blocking | **YES** |
| 4 | "row-count and checksum comparison against #7's seeded databases passes" | — | **Deferred, correctly and honestly.** See below | n/a |

**Acceptance item 4 — is the deferral structured so #7 can be added later without a rewrite? Yes, and the report is honest about it.** The oracle values are isolated as `[InlineData]` rows on standalone `[Theory]`s (`DeterministicParityTests.cs:30–43, 77–97`), so a second source is additive; `ComputeChecksumAsync` is a single named static with an explicit column list, so a MySQL-shaped sibling is mechanical; and `Row_Counts_Match_The_Datasets_Own_Declared_Sizes` asserts named constants a live #7 count can be diffed against. `progress/impl_seed_job.md:173-181` states plainly that item 4 is **not proven now** and says why. That is the right call and the right disclosure — it is **not** part of this rejection.

---

## CHECKPOINTS walked

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist
- [x] `progress/current.md` and `progress/history.md` exist
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (6 definitions, incl. `suite_runner`)
- [x] every agent declares its model — `init.sh` verifies each explicitly
- [x] `./init.sh` exits **0**

### C2 — state is coherent
- [x] at most one feature `in_progress` — zero (12 was `in_review`); set back to `in_progress` by this review
- [x] every status is in `rules.valid_status`
- [x] every `done` feature has passing tests — 163 tests green across 8 suites
- [x] `progress/current.md` describes the active session (`seed_job`, 2026-09-01), not leftovers — *advisory: its `**Status:** in_progress` line was one transition stale against `in_review`; now accurate again*
- [x] no `blocked` features

### C3 — architecture is respected
- [x] no `Microsoft.EntityFrameworkCore` / `Confluent.Kafka` / `NATS.*` / `MongoDB.*` / `Microsoft.AspNetCore.*` in any `Domain/` — **verified by running** `Architecture.Tests` (12/12 green) with `OrderToCash.Seed.Domain` confirmed present in `tests/Architecture.Tests/DomainAssemblies.cs:38`
- [x] no cross-service DB access — the seed writes all four stores, which is what a seed job is; it does so through **each service's own `DbContext`**, exactly mirroring #7's `apps/seed` importing `apps/orders/src/infrastructure/persistence/*`. **The four `DbContext`s are unmodified** — `git status` shows no change under `src/Orders`, `src/Fulfillment`, `src/Billing`, `src/Notifications`
- [x] no shared runtime code beyond `src/SharedKernel` and `src/Contracts` — `Seed.csproj`'s references to Orders/Fulfillment/Billing are a *job* consuming services, not services sharing code; identical shape to #7
- [x] `src/SharedKernel` still has zero `PackageReference` — `SharedKernelHasNoPackagesTests` green
- [x] no `decimal` in domain arithmetic — `DomainDecimalTests` green; `Money`/timeline totals are `long` minor units throughout
- [x] Kafka-fact vs NATS-RPC — n/a at phase 7 (no transport in this feature); the 50 seeded outbox rows are correctly shaped as facts with `published_at` already set
- [x] no stray debug logging, no context-free TODOs — the `Console.WriteLine` calls in `Program.cs`/`SeedRunner.cs` are the job's own summary output, mirroring #7's `index.ts`

### C4 — verification is real
- [x] `./quality.sh` passes — exit 0, format clean, 0 warnings, 163/163 tests, **1 m 45 s**
- [x] domain tests are pure — no framework in the bodies of `DatasetTests` / `DeterministicParityTests` (*advisory D6 on their project placement*)
- [x] integration tests use Testcontainers for .NET against real MsSql (`2022-CU26-ubuntu-22.04`) and MongoDB (`mongo:8.3.8`) — same tags as compose, no mocked stores
- [ ] **coverage thresholds met** — *cannot be evaluated.* `quality.sh` prints seven overlapping per-project line-rates (95.8 / 91.5 / 91.3 / 85.0 / 77.3 / 68.0 / 20.1 / 0.0) and **deliberately does not gate**, with an in-file rationale deferring enforcement to feature 34 (`quality.sh:2-12, 80-84`). Pre-existing, honestly disclosed, carried forward from the Phase 6 review — **not this feature's defect and not part of this rejection**
- [x] no Jest anywhere — xUnit only

### C5 — the session closed cleanly
- [x] no suspicious untracked files — the untracked set is exactly this feature's sources and tests; `bin/`/`obj/`/`TestResults/` are ignored
- [ ] **`progress/history.md` has an entry for the feature, including its effort record** — not appended: the feature is not closed
- [x] `feature_list.json` reflects the true state — set back to `in_progress` by this review
- [x] the human will be told what was done and how to test it manually — leader's report
- [x] **Claude did not commit** — no `git commit`, no `git push`; working tree byte-identical to the start of this review

### C6 — Spec-Driven Development
**N/A** — feature 12 is `sdd: false`. `init.sh` confirms "0 sdd feature(s) past pending", so no `specs/<name>/` triple is owed.

### C7 — spec-reuse fidelity (noted, not fully walked; most boxes belong to later phases)
- [x] the `R<n>`-equivalent claim holds: the reused *values* (ids, GLNs, EANs, references, timeline shape) are genuine claims about identical behaviour, and I verified them against #7's executable source rather than accepting the label
- [x] no amendment to `specs/shared/` was made or needed by this feature — the only recorded deviation is `BusinessReference.cs`, disclosed at `impl_seed_job.md:186` (#7 has `DespatchReference`/`InvoiceReference`/`CreditLineReference` as SharedKernel value objects; #8 has three local formatting functions, because SharedKernel was out of scope). **Correctly disclosed, correct not to have fixed it here, and it must be carried forward** to whichever feature adds those value objects
- [ ] `progress/history.md` effort record — owed on approval

---

## What must change before re-review

**One thing is required. Everything else on this page is an advisory.**

1. **Close D1: give the `order_timeline` documents a value-level guard that fails when a field is blanked or perturbed.** The narrowest sufficient fix is a single new test in `tests/Seed.IntegrationTests` that asserts the *contents* of the seeded documents against expected values, in the same style as `DeterministicParityTests` — hardcoded from #7's own `toTimelineDocument` output, not from #8's. At minimum it must fail for each of:
   - every `events[].causationId` blanked or wrong (probe B),
   - `totals.initialAmount` / `initialDiscount` / `totalAmount` off by one (probe D),
   - `items[].productCode` / `name` / `quantity` / `unitPrice` / `lineDiscount` changed,
   - `retailer.name` / `retailer.gln` / `company.name` / `company.gln` changed,
   - `orderDate` / `updatedAt` changed,
   - `events[].eventId` / `eventType` / `occurredAt` / `summary` changed, and a `detail` block dropped.

   Asserting the whole document against a per-order expected object is simpler than twelve separate assertions and is what makes the guard total rather than a list of remembered fields. **The expected values must come from #7** — `apps/seed/src/writers/mongo.writer.ts`'s `toTimelineDocument` over `SAGAS`, executed, exactly as the existing oracle values were obtained. For convenience: `node --experimental-transform-types` over #7's own sources works with only two import specifiers repointed (`@otc/shared-kernel`, `mongodb`), which is how I produced my reference set.

2. **Re-arm and record it.** Add probes B and D (verbatim: `CausationId = string.Empty` and `TotalAmount = saga.TotalAmount + 1` at `MongoSeedWriter.cs:130` and `:97`) to the arming table in `progress/impl_seed_job.md`, with the forced `--no-incremental` rebuild, the named test that now fails, and its verbatim message.

3. **Optional, cheap, same edit session:** D3 — assert `215` instead of `> 0` at `SeedIntegrationTests.cs:98`, and add exact counts for `reservations` (11), `despatch_items` (10), `invoice_items` (10). D5 — fix the path in the report's file list.

**Do not** touch the seeded data, the writers, `DeterministicId.cs`, `Gs1Identifiers.cs`, the `DbContext`s, `Contracts`, or `quality.sh`. The dataset is correct and I have proven it row by row; this rejection is about the test, not the seed. D2 belongs to Contracts and D4/D6 are cosmetic — leave all three for the leader to route.

---

## Benchmark note (for the history entry that will be written on approval)

Not appended to `progress/history.md` — the feature is not closed. Recording the measurement here so it is not lost:

- **#7 baseline** (`order-to-cash-nestjs/progress/history.md`, `seed_job` id 12, 2026-08-20): 1 session, **~0.5 h wall-clock** — implementation ~22 min by file timestamps, review ~1 h. **Approved on the first pass, zero defects.**
- **#8**: 1 session so far. Implementation file timestamps run **15:01:01 → 15:35:07 = ~34 min** of authoring (plus reading), i.e. **~1.5× #7's authoring time**, and now a rejection round on top.
- **The finding, and it is the Phase 6 finding again, verbatim.** #7 had to *invent* the derivation scheme, choose the namespaces, fabricate 22 companies and six saga histories, and reason out the read-model shape from scratch. #8 had all of it handed over **in executable form** — and still took longer to author, and is not closing on the first pass. The saving did not appear where the reuse was, because the reuse was in the *data*, which was never the expensive part. What was expensive in #7 was the same thing that is expensive here: **proving the data is right.** Phase 6 closed with "the specification reused, the verification did not." Phase 7 says it more sharply: **#7's dataset was reusable as bytes but its *oracle* was not reusable as a test** — #8 had to reconstruct, by hand, seventeen expected values that #7 had never written down (I confirmed #7's `deterministic.spec.ts` contains no hardcoded values), and the seventeen it reconstructed cover the derivation helpers but not the artefact that actually matters five phases downstream.
- **The transferable lesson for #9:** when the reference implementation hands over data, the thing to port is not the data — it is the *assertion set over the data*. #7 has no such set for `order_timeline` either, which is exactly why this gap reproduced. **#9 should generate its expected timeline documents from #7's `toTimelineDocument` as a fixture file, checked in, before writing any writer code.** That is a 10-minute step that would have made this rejection impossible in both #8 and #9.

---
---

# Re-review — round 2

**Verdict: APPROVED**

The verdict and all six defects above are left standing verbatim as the record of round 1. This section records what changed, what I verified myself, and the two findings that are **not** the implementer's: **D2 is withdrawn** (my error, explained below), and the `quality.sh` wall-clock figure I am handing to Phase 21 needed re-measuring.

---

## D2 — WITHDRAWN. My error, and the reason matters more than the retraction.

**D2 is withdrawn in full. `src/Contracts/Facts/Payloads/StockReservedPayload.cs` is correct as written, has not been touched, and must not be changed on account of this review.**

I cited `CLAUDE.md` as *"The JSON wire shape must match #7 **byte for byte**"* and treated payload key order as inside that claim. That wording was **superseded in Phase 5, at the human gate.** The rule on disk (`CLAUDE.md:83`) reads:

> **The JSON wire shape must match #7 — envelope byte-exact, payload semantically equal.**

and `CLAUDE.md:87` records the reasoning inline: #7's payload key ordering ("ordered by key length then alphabetically") is **MySQL's `json` column normalisation** leaking onto its wire, because #7's relay reads the payload back out of that column before republishing — a storage artifact that became part of the apparent contract, verified on a single `eventId` present in both stores. #8 keeps payloads in `nvarchar(max)`, which preserves insertion order; matching #7 would mean emulating another engine's storage quirk permanently, and #9 on PostgreSQL's `jsonb` could not comply at all. `CLAUDE.md:89` states it is a #8 convention, human-gated, and explicitly **not** a `specs/shared/` amendment.

**My own round-1 evidence is therefore proof of compliance, not of a defect.** I parsed and deep-compared all 50 outbox payloads against #7's fixtures and found **0 semantic differences** — same keys, same values, same types, same casing. That is exactly the standard the amended rule sets. The 14 key-order differences I flagged are the expected and required consequence of the amendment.

### Why I got it wrong, and whether the wording invites the mistake

The coordinator asked directly, so: **no, the wording does not invite the mistake.** `CLAUDE.md:83` is unambiguous, the distinction between envelope and payload is drawn in the same sentence, and the four lines beneath it give the engine-specific reasoning, the verification and the gate. A reader of the file could not reasonably land where I landed.

**The failure was mine and it was mechanical: I cited the copy of `CLAUDE.md` auto-injected into my context rather than the file on disk, and the injected copy was stale.** It carried the `b15e5cc` text ("byte for byte", the original harness copy from #7); the amendment landed at `ba0ca8b` (*"feat(contracts): hand-written wire types proven against 12 real #7 envelopes"*, Phase 5) and has been on disk ever since — `git status` shows `CLAUDE.md` unmodified, so the amended text is committed and was committed throughout both of my review rounds. `git log -S` confirms both revisions and their commits.

**The generalisable correction, which I am adopting and recommend be made explicit for every agent in this harness:** *never quote a convention from the injected `CLAUDE.md`; `grep` the file on disk for any rule you are about to enforce.* An injected snapshot is a cache, and this repository amends its own conventions at human gates mid-project by design — so the cache is expected to go stale, and a reviewer enforcing a stale rule is a guard that fires on the wrong thing. That is the same failure family as the guard-that-does-not-guard, inverted: a guard that guards something no longer true. It cost this feature one spurious advisory; on a rule with teeth it would have cost a spurious rejection.

I have left D2 visible above with this cross-reference rather than deleting it, so the next reader sees both the claim and its retraction.

---

## D1 — the fix. **Verified independently, and it is a real value guard, not the two published probes armed.**

### The oracle fixture's provenance — confirmed by a second, independent derivation

`tests/Seed.IntegrationTests/OracleFixtures/order_timeline_from_number7.json` (6 documents, 24,983 bytes) is claimed to be #7's own `apps/seed/src/writers/mongo.writer.ts#toTimelineDocument` executed over #7's own `SAGAS`.

I did not take that on trust and I did not need to re-derive it: **I still had the file I generated myself in round 1**, by the same technique, before the implementer had written any of this. I diffed them:

```
mine docs: 6   fixture docs: 6
ids equal: True
document differences: 0
```

**Zero differences across all six documents and every field.** Two independent executions of #7's source, hours apart, by different agents, producing identical output. The fixture is genuinely #7's, not #8's own output written down — which was the exact failure mode this feature's whole value rests on avoiding.

### The new test

`tests/Seed.IntegrationTests/SeedIntegrationTests.cs:233–361` — `Order_Timeline_Documents_Match_The_Values_Number7s_ToTimelineDocument_Produced` seeds real MS-SQL + MongoDB containers, loads all 6 live documents, matches each to the oracle by `orderReference` and asserts **values**, not shapes: `_id`/`orderId`/`orderDate`/`status`/`cancellationReason`/`currency`; `retailer.code|name|gln` and `company.code|name|gln`; all three `totals`; every `items[]` entry's five fields **in order**; all three `references` including the cancelled order's three `null`s; every `events[]` entry's `eventId`/`eventType`/`occurredAt`/`summary`/**`causationId`** in order, with `detail` compared key-by-key when present and asserted `null` when absent; `headerComplete`; `updatedAt`; `statusRank`; `timelineOrderVersion`; and the full ordered `processedEventKeys` list. The original shape test was kept alongside rather than replaced.

### My own mutation probes — five, of which three were never published

The coordinator's specific concern was a fix that arms exactly the two probes I published and leaves the rest of the document as unguarded as before. **It is not that fix.** All five probes were applied to an **out-of-tree copy** of the repository; the working tree was never modified, and the copy was restored from a pristine snapshot between probes and `diff`-confirmed byte-identical to the repository file at the end.

| # | Mutation | `MongoSeedWriter.cs` | Published in round 1? | Result | Failure message (verbatim) |
|---|---|---|---|---|---|
| P1 | `TotalAmount = saga.TotalAmount + 1` | :97 | **yes** (probe D) | **FAILED** | `Assert.Equal() Failure: Values differ — Expected: 16130 / Actual: 16131` |
| P2 | `ProductCode = line.ProductCode` → `"PRD-9999"` | :103 | **no** | **FAILED** | `Assert.Equal() Failure: Strings differ — Expected: "PRD-0002" / Actual: "PRD-9999"` |
| P3 | `Retailer … Name = retailer.Name` → `retailer.Code` | :88 | **no** | **FAILED** | `Assert.Equal() Failure: Strings differ — Expected: "Carrefour España" / Actual: "CarrefourEs"` |
| P4 | `UpdatedAt = Iso(saga.UpdatedAt)` → `Iso(saga.OrderDate)` | :134 | **no** | **FAILED** | `Assert.Equal() Failure: Strings differ — Expected: "2026-06-02T09:00:10.000Z" / Actual: "2026-06-01T09:00:00.000Z"` |
| P5 | `Company … Gln = company.Gln` → `retailer.Gln` | :89 | **no** | **FAILED** | `Assert.Equal() Failure: Strings differ — Expected: "5400000000218" / Actual: "5400000000010"` |

Every one caught, by name, by the new test. **Probe B (`causationId` blanked) I did not re-run — the coordinator reproduced it under the forced-rebuild protocol with an explicit compile check and reported it failing 1-of-5 with green restoration, and the implementer's own arming table records its verbatim message.** That is the one claim in this round I took from another agent rather than executing myself, and I am naming it as such.

**Coordinator checks 2 and 3 answered directly:** party identifiers are now asserted with real expected values — P3 and P5 prove `retailer.name` and `company.gln` specifically, and the test asserts all six party fields (`SeedIntegrationTests.cs:282–290`). `orderDate` and `updatedAt` are asserted as **exact instants** against the oracle strings (`:275`, `:353`), not not-null — P4 proves `updatedAt` fires on a wrong-but-plausible timestamp taken from the same document.

---

## D3, D4, D5, D6 — all verified fixed

**D3 — exact counts.** `SeedIntegrationTests.cs:103–105, 111` now assert `Stock == 215` (was `> 0`), `Reservations == 11`, `DespatchItems == 10`, `InvoiceItems == 10`. All four match the values I derived from #7's executed modules in round 1. Every count in that test is now exact, each with a composition comment.

**D4 — the vacuous wart test is now a real one.** `tests/Seed.UnitTests/DeterministicParityTests.cs:119–137` (`DeterministicId_Would_Differ_If_The_Skipped_Hex_Character_Were_Not_Skipped`) recomputes the un-warted derivation locally from SHA-256 — `hex[12..15]` against production's `hex[13..16]` — asserts that reconstruction equals `8a2ac568-0944-**4250**-872a-38acbce9724c`, then asserts `DeterministicId.Of("currency:USD")` differs from it and equals `…-**4507**-…`. The `4250` value is **exactly what my own round-1 re-derivation produced**, so the assertion's target is independently corroborated. The test now fails if the wart is "fixed" *and* fails if the reconstruction drifts — non-redundant with the `[Theory]` above it, unlike the version it replaces.

**D5 — report path corrected.** `progress/impl_seed_job.md:35` now reads `Domain/Data/MasterDataTimestamp.cs`, matching disk.

**D6 — the split is real, not a rename.** `tests/Seed.UnitTests/` contains only `DatasetTests.cs` (13 facts) and `DeterministicParityTests.cs` (7 facts/theories) — all thirteen original `DatasetTests` names preserved, nothing dropped in the move. Verified by grep: **no** `Testcontainers`, `MongoClient`, `IMongoDatabase`, `DbContext`, `MsSql`, `Docker`, `HttpClient`, `Kafka` or `Nats` reference anywhere in the project; its only usings are `OrderToCash.*`, `Xunit`, and `System.Security.Cryptography`/`System.Text` (D4's local SHA-256 reconstruction — BCL, not a framework). `Seed.UnitTests.csproj` carries **no** `Testcontainers.*` package reference and a single `ProjectReference` to `src/Seed`. Registered in `OrderToCash.sln:44` with full Debug/Release × AnyCPU/x64/x86 configuration mappings and nested under the `tests` solution folder (`{0AB3BF05-…}`), so `quality.sh`'s solution-level `dotnet test` picks it up — confirmed in the run below: `OrderToCash.Seed.UnitTests.dll … Passed: 34, Duration: 192 ms`.

**The split also pays for itself.** `Seed.IntegrationTests` went from 31 s (38 tests) to **17 s** (5 tests) while *gaining* the new oracle test, because 34 pure tests no longer wait behind two container starts. That is a genuine cost reduction, not a cosmetic reorganisation.

---

## No regression

| Check | Result |
|---|---|
| Four `DbContext`s unmodified | **yes** — `git status` over `src/Orders`, `src/Fulfillment`, `src/Billing`, `src/Notifications`: nothing |
| `src/Contracts/**` unmodified | **yes** — nothing (and D2 withdrawn, so nothing is owed there) |
| `src/SharedKernel`, `src/Gateway`, `src/Projector`, `quality.sh`, `init.sh`, `CLAUDE.md`, `specs/` unmodified | **yes** — nothing |
| Architecture suite | **12/12 green**, with `OrderToCash.Seed.Domain` in scope via `DomainAssemblies.cs:38` |
| `./init.sh` | exit **0** |
| `dotnet format --verify-no-changes` | clean |
| `dotnet build` | 0 warnings, 0 errors |
| `./quality.sh` | exit **0**, **164 tests**, all green (SharedKernel 32, Contracts 21, **Seed.UnitTests 34**, Architecture 12, Notifications 7, Orders 12, Fulfillment 19, Billing 22, **Seed.IntegrationTests 5**) |
| Round-1 dataset findings | unaffected — the seeded data, the writers, `DeterministicId.cs` and `Gs1Identifiers.cs` were not touched, so the 413-row + 6-document value-level parity I proved in round 1 stands |

### `quality.sh` wall-clock — the number Phase 21 has to design around, and a warning attached to it

**1 m 40.8 s** (`real 1m40.820s`), exit 0, 164 tests, on an otherwise-idle host.

**But I measured it twice and got 1 m 40.8 s and 13 m 03.3 s on identical code.** The first attempt ran while the host was still saturated from my own five container-backed mutation probes (1-minute load average **41.65**, 15-minute **66.21**); every container-backed suite inflated ~4× in lockstep — Notifications 8 s→31 s, Orders 12 s→59 s, Fulfillment 15 s→1 m 05 s, Billing 17 s→1 m 15 s, Seed 17 s→1 m 28 s. I waited for the 1-minute load to fall below 6 and re-ran clean. There were no leftover probe containers (12 running = the 12 compose services).

**Phase 21 should take two things from this, not one.** The number is 1 m 40 s — *lower* than Phase 6's 2 m 39 s despite two more test projects and 39 more tests, because D6's split removed 34 tests from behind a container start. And the **7.8× spread on identical code** is the more important figure: this gate's wall-clock is dominated by contended container startup, not by test work, so any Phase 21 fast/full split or CI timeout budget set from a single measurement on a loaded machine will be wrong by most of an order of magnitude. Measure it on an idle host, and record the host state alongside the number.

---

## Advisories carried forward (none blocking, none owed by this feature)

- **A1** — `tests/Seed.IntegrationTests/Seed.IntegrationTests.csproj:32` refers to `OrderTimelineOracleTests.cs`; the test actually lives in `SeedIntegrationTests.cs`. A stale comment from an earlier shape of the fix. One line, cosmetic.
- **A2** — the `detail` comparison (`SeedIntegrationTests.cs:331–345`) iterates the **expected** object's keys, so an *extra* key added to a live `detail` block would not be caught. Both current `detail` blocks are single- and double-key, so the exposure is small; worth closing when the projector starts writing `detail` for real (Phase 12).
- **A3** — the idempotency checksum (`ComputeChecksumAsync`, `:363–413`) still covers a subset: orders, orders-outbox (with full payload), stock, reservations, credits, credit_items, timeline headers. Products, retailers, companies, order_items, despatches, invoices, payments and all `created_at`/`updated_at` columns are outside it. It compares run 1 against run 2, so it catches *drift*; it would not catch a rewrite confined to the omitted tables. Raised in round 1, still true, still not blocking.
- **A4** — the coverage gate remains inert by design, with the seven overlapping per-project line-rates (95.8 / 91.4 / 91.3 / 85.0 / 77.3 / 68.0 / 20.1 / 12.5 / 0.0) not summing to anything gateable. `quality.sh:2-12, 80-84` defers enforcement to feature 34 and forbids faking a gate meanwhile. Correct, honest, and unchanged — carried from Phase 6, addressed to feature 34.
- **A5 (process, mine)** — the injected-`CLAUDE.md`-is-a-stale-cache finding from the D2 withdrawal above. Addressed to the leader: worth stating in the harness so every agent reads conventions from disk.

---

## CHECKPOINTS — re-walked

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist
- [x] `progress/current.md` and `progress/history.md` exist
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (6 definitions)
- [x] every agent definition declares its model
- [x] `./init.sh` exits **0**

### C2 — state is coherent
- [x] at most one feature `in_progress` — zero after this approval
- [x] every status is in `rules.valid_status`
- [x] every `done` feature has passing tests — 164 green across 9 suites
- [x] `progress/current.md` describes the active session
- [x] no `blocked` features

### C3 — architecture is respected
- [x] no banned framework reference in any `Domain/` — **NetArchTest run**, 12/12, `Seed.Domain` in scope
- [x] no cross-service DB access; the four `DbContext`s **unmodified**, verified by `git status`
- [x] no shared runtime code beyond `src/SharedKernel` and `src/Contracts`
- [x] `src/SharedKernel` still has zero `PackageReference`
- [x] no `decimal` in domain arithmetic
- [x] Kafka-fact vs NATS-RPC — n/a at phase 7; the 50 seeded outbox rows are correctly shaped facts
- [x] no stray debug logging, no context-free TODOs

### C4 — verification is real
- [x] `./quality.sh` passes — exit 0, 164 tests, **1 m 40.8 s** idle-host
- [x] domain tests are pure — **and now live in a project whose dependency graph says so** (D6)
- [x] integration tests use Testcontainers against real MsSql `2022-CU26-ubuntu-22.04` and MongoDB `8.3.8`, same pins as compose
- [ ] coverage thresholds met — **not evaluable**; gate deliberately deferred to feature 34 with an in-file rationale (A4). Pre-existing, disclosed, not this feature's
- [x] no Jest anywhere

### C5 — the session closed cleanly
- [x] no suspicious untracked files
- [x] `progress/history.md` has an entry for the feature **including its effort record** — appended by this review
- [x] `feature_list.json` reflects the true state — feature 12 set `done`
- [x] the human will be told what was done and how to test it manually — leader's report
- [x] **Claude did not commit** — no `git commit`, no `git push`

### C6 — Spec-Driven Development
**N/A** — `sdd: false`; `init.sh` confirms no `specs/<name>/` triple is owed.

### C7 — spec-reuse fidelity (partial; most boxes belong to later phases)
- [x] the reused ids/values are genuine behavioural claims, verified against #7's executable source rather than accepted as labels
- [x] no `specs/shared/` amendment made or needed. The one recorded deviation — `BusinessReference.cs` as three local formatting functions where #7 has SharedKernel value objects — is disclosed at `impl_seed_job.md:186` and **carried forward** to whichever feature adds `DespatchReference`/`InvoiceReference`/`CreditLineReference` to `SharedKernel`
- [x] effort record complete and honest, including that this feature was **not** faster — appended below

---

## What I did NOT re-run in round 2, and why

| Not re-run | Why |
|---|---|
| The 413-row reference-data value diff and the 50-payload comparison | round 1 proved them; nothing in round 2 touched the seeded data, the writers or the derivation helpers. Re-running would be duplicated cost against an unchanged artefact |
| Probe B (`causationId` blanked) | the coordinator reproduced it under the forced-rebuild protocol with a compile check and green restoration. Explicitly flagged above as the one claim I inherited rather than executed — and P1–P5 independently establish that the same test is a genuine value guard |
| The live compose-stack seed run | the live databases were already verified at value level in round 1 and the writers are unchanged |
| The #7-live-MySQL comparison (acceptance item 4) | still correctly deferred pending the human's decision; the tests are structured to accept it additively |
