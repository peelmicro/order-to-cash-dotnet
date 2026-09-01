# impl_seed_job — feature 12, phase 7

Assessment #8 (.NET 10). `sdd: false`. Status set to `in_review`.

## What was built

`src/Seed` is now a one-shot console job (`dotnet run --project src/Seed`) that:

1. Applies the committed EF Core migrations for `OrdersDbContext`, `FulfillmentDbContext` and `BillingDbContext` (so a cold compose stack works from empty).
2. Writes the reference catalogue to `otc_orders`: 3 currencies, 12 products, 7 retailers, 22 companies.
3. Writes initial stock to `otc_fulfillment` (215 rows: saga-derived pairs plus a full per-product baseline for every company the sample sagas never touch).
4. Writes one credit line per (retailer, company) pair to `otc_billing` (154 rows — 7 "primary supplier" lines plus a baseline line for every other pair, exactly #7's `billing_credit` amendment).
5. Writes 5 completed sample orders + 1 cancelled sample order (`ORD-000001..006`) and every fact they produced (already-published `outbox` rows, reservations, despatches, invoices, payments, credit ledger entries) across the three MS-SQL databases.
6. Writes the matching `order_timeline` documents to MongoDB `otc_read_model.order_timeline`.
7. Prints a row-count summary and exits 0, or exits 1 with the exception on failure.

Ran twice against the live compose stack (`localhost:1433` / `localhost:27017`) — identical row counts both times:

```
orders:       currencies=3 products=12 retailers=7 companies=22 orders=6 orderItems=11 outbox=17
fulfillment:  stock=215 reservations=11 despatches=5 despatchItems=10 outbox=12
billing:      credits=154 creditItems=15 invoices=5 invoiceItems=10 payments=5 outbox=21
mongo:        order_timeline=6
```

## File list

**Production** (`src/Seed/`):

- `Domain/Deterministic/DeterministicId.cs` — the SHA-256-derived UUID-shaped id, ported byte-for-byte from #7's `deterministicId`, including the deliberately-preserved `hex[12]`-skip wart, commented as such.
- `Domain/Deterministic/Gs1Identifiers.cs` — `MakeGln`/`MakeEan13`, ported from #7's `makeGln`/`makeEan13`. `MakeGln` obtains its check digit by constructing `OrderToCash.SharedKernel.GLN` itself (trying candidate digits 0-9 until one validates) rather than reimplementing the mod-10 algorithm — per the task's explicit instruction to reuse `GLN`'s own check-digit logic.
- `Domain/Deterministic/BusinessReference.cs` — `DES-`/`INV-`/`CR-` formatting (SharedKernel only has `OrderNumber`/`ORD-`; touching SharedKernel was out of scope for this feature, so the same zero-pad-6 format is reproduced locally).
- `Domain/Data/{CurrencySeed,ProductSeed,RetailerSeed,CompanySeed,CreditSeed,StockSeed}.cs` — the reference-data records and builders, ported from #7's `data/*.data.ts`, same codes/names/countries/VATs/currencies/prices, same iteration order for GLN and EAN sequencing.
- `Domain/Sagas/OrderSagaFixture.cs`, `Domain/Sagas/SagaFixtures.cs` — the 5 completed + 1 cancelled saga fixtures, ported from #7's `sagas.data.ts`: order lines, outbox rows (typed against the real `OrderToCash.Contracts.Facts.Payloads.*` records), reservations, despatch, credit ledger, invoice/payment, and the `order_timeline` entries with their causal chain (`causationId`).
- `Domain/Data/MasterDataTimestamp.cs`, `Domain/SeedDomainPlaceholder.cs` (kept — `tests/Architecture.Tests/DomainAssemblies.cs` resolves `typeof(OrderToCash.Seed.Domain.SeedDomainPlaceholder).Assembly` and that file was out of scope to edit).
- `Application/SeedDataset.cs` — a single read-only aggregation surface over the Domain builders, consumed by both Infrastructure writers and tests.
- `Infrastructure/Persistence/{SeedDbConfig,EfUpsert,OrdersSeedWriter,FulfillmentSeedWriter,BillingSeedWriter}.cs` — reuse the real `OrdersDbContext`/`FulfillmentDbContext`/`BillingDbContext` (never modified), upsert-by-id via `EfUpsert.UpsertAsync` (find-or-create, apply, `SaveChanges`), payloads serialized with the shared `OrderToCash.Contracts.Wire.JsonWire.Options`.
- `Infrastructure/Mongo/{SeedMongoConfig,OrderTimelineDocument,MongoSeedWriter}.cs` — the `order_timeline` document shape and the `replaceOne(..., upsert: true)` writer, plus the partial unique index on `orderReference`.
- `Presentation/SeedRunner.cs`, `Program.cs` — orchestration (mirrors #7's `index.ts`) and the console entry point.
- `Seed.csproj` — added `OutputType=Exe`, project references to `Orders`/`Fulfillment`/`Billing` (to reuse their DbContexts, per the task's explicit instruction — never modified), and package references to `Microsoft.EntityFrameworkCore.SqlServer` and `MongoDB.Driver` (both already centrally pinned in `Directory.Packages.props`, no version change).

**Tests** (`tests/Seed.IntegrationTests/`):

- `DeterministicParityTests.cs` — the parity oracle: `deterministicId`/`makeGln`/`makeEan13` against hardcoded values obtained from #7's own TypeScript, plus a second test tying the actual production dataset (`Currencies.All`, `Retailers.All`, `SagaFixtures.All`) to the same oracle (added during arming — see below).
- `DatasetTests.cs` — pure, in-memory: row counts against the dataset's own declared sizes, every retailer/company GLN validated against `GLN`, GLN uniqueness, stock coverage invariants, the cancelled saga's `.99` rule.
- `SeedContainersFixture.cs` — one shared `Testcontainers.MsSql` container (`mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04`, same tag as the compose stack) + one shared `Testcontainers.MongoDb` container (`mongo:8.3.8`, same tag), collection-scoped.
- `SeedIntegrationTests.cs` — migrations, row counts against real MS-SQL/Mongo, idempotency (run twice, compare counts + a SHA-256 checksum over key columns), and the `order_timeline` document round-trip (every §8 field, including `headerComplete`, `statusRank`, `processedEventKeys`, `timelineOrderVersion`, and the cancelled order's compensation sequence).
- `Seed.IntegrationTests.csproj` — new project, added to `OrderToCash.sln` under the `tests` solution folder via `dotnet sln add`.

**Other**: `Directory.Packages.props` (added `Testcontainers.MongoDb` `4.14.0`, same major/minor band as the already-pinned `Testcontainers.MsSql`), `OrderToCash.sln` (new project entry), `feature_list.json` (status only: `pending` → `in_review`).

## How the parity oracle values were obtained

Per the task's requirement, every hardcoded expected value in `DeterministicParityTests.cs` was produced by running #7's **own** TypeScript, copy-pasted verbatim, from the `order-to-cash-nestjs` checkout — never by running this C# port and recording its own output. Exact commands used:

```bash
cd /home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs

node -e "
const { createHash } = require('node:crypto');
function deterministicId(namespace) {
  const hex = createHash('sha256').update('otc-seed:' + namespace).digest('hex');
  const timeLow = hex.slice(0, 8);
  const timeMid = hex.slice(8, 12);
  const timeHiAndVersion = '4' + hex.slice(13, 16);
  const variantNibble = ((parseInt(hex[16], 16) & 0x3) | 0x8).toString(16);
  const clockSeqAndReserved = variantNibble + hex.slice(17, 20);
  const node = hex.slice(20, 32);
  return \`\${timeLow}-\${timeMid}-\${timeHiAndVersion}-\${clockSeqAndReserved}-\${node}\`.toLowerCase();
}
function makeEan13(sequence) {
  const body = '590100' + String(sequence).padStart(6, '0');
  let sum = 0;
  for (let i = 0; i < body.length; i++) {
    const digit = Number(body[body.length - 1 - i]);
    sum += digit * (i % 2 === 0 ? 3 : 1);
  }
  const checkDigit = (10 - (sum % 10)) % 10;
  return \`\${body}\${checkDigit}\`;
}
console.log(deterministicId('currency:USD'));
// ... (currency:EUR, currency:GBP, retailer:CarrefourEs, order:1, product:PRD-0001, stock:IBERFOODS:PRD-0002)
console.log(makeEan13(1));
console.log(makeEan13(12));
"

node -e "
function computeCheckDigit(body) {
  let sum = 0;
  for (let distanceFromRight = 0; distanceFromRight < body.length; distanceFromRight++) {
    const digit = Number(body[body.length - 1 - distanceFromRight]);
    const weight = distanceFromRight % 2 === 0 ? 3 : 1;
    sum += digit * weight;
  }
  return (10 - (sum % 10)) % 10;
}
function makeGln(sequence) {
  const body = String(540000000000 + sequence);
  return body + computeCheckDigit(body);
}
for (const seq of [1,2,3,4,5,6,7,21]) console.log(seq, makeGln(seq));
"
```

The `makeGln`/`computeCheckDigit` script is copy-pasted verbatim from `packages/shared-kernel/src/domain/gln.ts`'s `GLN.computeCheckDigit` (identical algorithm to `OrderToCash.SharedKernel.GLN`'s own private method) — not reimplemented.

## Dataset sizes actually written

| Table | Count | Composition |
|---|---|---|
| `otc_orders.currencies` | 3 | USD, EUR, GBP |
| `otc_orders.products` | 12 | 8 EUR, 2 GBP, 2 USD |
| `otc_orders.retailers` | 7 | fixed list, GLN sequences 1-7 |
| `otc_orders.companies` | 22 | fixed list, GLN sequences 21-42 |
| `otc_orders.orders` | 6 | 5 completed + 1 cancelled |
| `otc_orders.order_items` | 11 | 5×2 + 1×1 |
| `otc_orders.outbox` | 17 | 5×3 (placed/confirmed/completed) + 1×2 (placed/cancelled) |
| `otc_fulfillment.stock` | 215 | saga-derived pairs + full per-product baseline for non-saga companies |
| `otc_fulfillment.reservations` | 11 | one per order line |
| `otc_fulfillment.despatches` | 5 | one per completed saga |
| `otc_fulfillment.outbox` | 12 | 5×2 + 1×2 |
| `otc_billing.credits` | 154 | 7 retailers × 22 companies (primary + baseline) |
| `otc_billing.credit_items` | 15 | 5×3 (hold/consume/release), none for the cancelled saga |
| `otc_billing.invoices` / `payments` | 5 / 5 | one per completed saga |
| `otc_billing.outbox` | 21 | 5×4 (approved/issued/received/released) + 1×1 (rejected) |
| MongoDB `order_timeline` | 6 | one document per order |

## `order_timeline` field mapping against Databases doc §8

| §8 field | Written as |
|---|---|
| `_id`, `orderId` | the saga's deterministic order id, `Guid.ToString()` (lowercase, hyphenated — same shape #7's string id is) |
| `orderReference`, `orderDate` | `ORD-000001` etc.; ISO-8601 string, 3 fraction digits + literal `Z` (same format as `Contracts.Wire.InstantJsonConverter`, matching #7's `.toISOString()`) |
| `retailer`, `company` | `{ code, name, gln }` snapshots |
| `status`, `cancellationReason`, `currency` | plain fields |
| `totals` | `{ initialAmount, initialDiscount, totalAmount }`, `long` minor units |
| `items[]` | `{ productCode, name, quantity, unitPrice, lineDiscount }` |
| `references` | `{ despatchReference, invoiceReference, paymentReference }`, all `null` for the cancelled order |
| `events[]` | `{ eventId, eventType, occurredAt, summary, detail?, causationId }`, sorted by `occurredAt` defensively (not trusted from construction order) |
| `headerComplete` | always `true` — the seed never writes placeholder documents |
| `updatedAt` | ISO string |
| `statusRank` (internal) | local copy of the projector's PR12 rank table, applied to `saga.Status` |
| `processedEventKeys[]` (internal) | `projector:<eventId>`, sorted ordinally |
| `timelineOrderVersion` | local copy of the projector's `TIMELINE_ORDER_VERSION` (2), stamped so the projector's boot migration never touches a seeded document |

The partial unique index `uq_order_reference` (restricted to `orderReference` being a string) is created idempotently before each seed run, matching #7's own reasoning about placeholder documents from the projector (not yet built) never colliding.

## Arming table

| # | What was broken | File (backed up to `/tmp/arming_backups/`, restored after) | Forced rebuild | Test that failed | Failure message |
|---|---|---|---|---|---|
| 1 | Namespace string `"currency:USD"` → `"currency:USD-BROKEN"` in production data | `src/Seed/Domain/Data/CurrencySeed.cs` | `dotnet build src/Seed/Seed.csproj --no-incremental` | `DeterministicParityTests.The_Seeded_Datasets_Own_Ids_Match_The_Value_Number7s_TypeScript_Produced` | `Assert.Equal() Failure ... Expected: 8a2ac568-0944-4507-872a-38acbce9724c Actual: 6874a29f-6907-44f4-8979-2f36ff360c17` |
| 2 | GLN check-digit search restricted to trying only digit `0` (instead of `0..9`) | `src/Seed/Domain/Deterministic/Gs1Identifiers.cs` | `dotnet build src/Seed/Seed.csproj --no-incremental` | `DatasetTests.Every_Seeded_Gln_Is_Valid_Per_Gln` | `TypeInitializationException ... InvalidOperationException: makeGln: no valid check digit found for body 540000000002` — the "fail loudly rather than write an invalid party identifier" guard, exercised for real |
| 3 | `EfUpsert.UpsertAsync` always `Add`s, never looks up the existing row by id | `src/Seed/Infrastructure/Persistence/EfUpsert.cs` | `dotnet build src/Seed/Seed.csproj --no-incremental` | `SeedIntegrationTests.Running_The_Seed_Twice_Is_A_No_Op` | `InvalidOperationException: The instance of entity type 'Currency' cannot be tracked because another instance with the same key value for {'Id'} is already being tracked` — thrown on the second `RunSeedAsync` call |

Each mutation was applied, forced a `--no-incremental` rebuild, ran the specific named test to observe the failure and its message (recorded above), then was restored from the `/tmp/arming_backups/*.bak` copy (never `git checkout --`), confirmed by re-reading the changed line, and the solution rebuilt + `dotnet format --verify-no-changes` re-checked clean.

Arm #1 also produced a permanent fix: the original parity tests only called `DeterministicId.Of(@namespace)` with a hand-typed literal, so a broken namespace *inside* production code (as opposed to inside the test file) would have slipped past undetected. `The_Seeded_Datasets_Own_Ids_Match_The_Value_Number7s_TypeScript_Produced` was added to `DeterministicParityTests.cs` to close that gap — it reads `Currencies.All`, `Retailers.All` and `SagaFixtures.All` directly and asserts against the same oracle values.

## Self-verification

- `dotnet build OrderToCash.sln` — 0 warnings, 0 errors.
- `dotnet format OrderToCash.sln --verify-no-changes` — clean.
- `./quality.sh` — full run, all green: `SharedKernel.UnitTests` 32/32, `Contracts.UnitTests` 21/21, `Architecture.Tests` 12/12, `Notifications.IntegrationTests` 7/7, `Orders.IntegrationTests` 12/12, `Fulfillment.IntegrationTests` 19/19, `Billing.IntegrationTests` 22/22, `Seed.IntegrationTests` 38/38 (post-arming; 37 before the extra parity test was added).
- `./init.sh` — exits 0, "no feature in_progress" (status is `in_review`).
- Ran `dotnet run --project src/Seed` twice against the live compose stack (`localhost:1433` MS-SQL, `localhost:27017` MongoDB) — identical row counts both times (see the block above), confirming idempotency outside the Testcontainers harness too, not only inside it.

## Traceability to the four acceptance items

1. **"same currencies, products, retailers, companies, GLNs, credit limits and stock as #7"** — `DatasetTests` (row counts, GLN validity/uniqueness, credit coverage) + `DeterministicParityTests` (ids/GLNs/EANs against #7's own oracle values) + the live two-run comparison above.
2. **"sample completed orders and one cancelled order, with their order_timeline documents"** — `DatasetTests.Five_Completed_Sagas_And_One_Cancelled_Saga_Are_Seeded`, `SeedIntegrationTests.Order_Timeline_Documents_Carry_Every_Field_With_The_Right_Types` (including the compensation-sequence assertion for the cancelled order).
3. **"idempotent — running twice is a no-op"** — `SeedIntegrationTests.Running_The_Seed_Twice_Is_A_No_Op` (row counts + SHA-256 checksum unchanged across two runs against real containers) + the live double-run above. Armed (#3 in the table).
4. **"row-count and checksum comparison against #7's seeded databases passes"** — **explicitly deferred**, see below.

## Explicitly deferred — the #7-comparison half

The fourth acceptance item needs #7's MySQL container running alongside #8's stack, which the task brief says is a decision the human has not yet taken. Nothing here blocks adding it later:

- The parity oracle values in `DeterministicParityTests.cs` are already isolated as named `[InlineData]` constants obtained directly from #7's algorithms — a second data source (a live query against #7's MySQL) could be added as additional `[InlineData]` rows, or as a separate `[Theory]`/`[Fact]` reading from #7's containers, without touching the existing assertions.
- `SeedIntegrationTests.ComputeChecksumAsync` is a single, named, reusable method — a second overload/variant that computes the same checksum shape over #7's MySQL rows (translating column names/types where MySQL and MS-SQL differ) is a mechanical follow-on, not a redesign.
- Row-count expectations are already asserted as named constants against the dataset's own declared sizes (`Row_Counts_Match_The_Datasets_Own_Declared_Sizes`), so a second assertion diffing them against a live count from #7 is additive.

## What surprised me / where #7's dataset could not be reproduced exactly

- **Stock coverage is NOT uniform per company.** My first draft of `DatasetTests` assumed every company got a stock row for every product; #7's own `stock.data.ts` only gives FULL per-product coverage to companies the sample sagas never touch — a saga-touched company only carries rows for the specific products its own sagas reserved. This is deliberate in #7 (the header comment on `stock.data.ts` explains it), not a place where I deviated; the test was wrong, not the port.
- **`DespatchReference`/`InvoiceReference`/`CreditLineReference` do not exist in this repository's `SharedKernel`** (only `OrderNumber` does — the other three business-reference value objects are a #7-only, not-yet-ported concept). Since touching `SharedKernel` was out of scope for this feature, the identical `<PREFIX>-######` zero-pad-6 format is reproduced locally in `Domain/Deterministic/BusinessReference.cs` rather than as a shared value object. This is a real (if narrow) place where #8's shape currently differs from #7's — #7 has real value objects, #8 has three plain formatting functions — worth flagging for whichever future feature is expected to add those value objects to `SharedKernel`.
- **GLN's check-digit algorithm is private in `SharedKernel.GLN`.** The task said "GLN already computes the GS1 check digit. Use it rather than reimplementing," but `ComputeCheckDigit` is a private static method with no public surface, and `SharedKernel` was out of scope to modify. Resolved by constructing `GLN` itself with all ten candidate trailing digits and keeping the one that validates — genuinely uses `GLN`'s own check-digit rule as the single source of truth (never a duplicated formula), just via its public validating constructor rather than a hypothetical public `ComputeCheckDigit`.
- **The order-sequence tables are deliberately left untouched.** Like #7, this seed does not update `order_number_sequences`/`despatch_number_sequences`/`invoice_number_sequences` — it "owns" sequences 1-6 on an empty database and must run before any live order. Since those tables/domain features (`orders_aggregate`, phase 8+) have not landed yet, there is nothing to reconcile against yet; noted here so whoever builds live order placement does not discover the precondition the hard way.

---

## Rejection round 2 — `progress/review_seed_job.md`, D1 (blocking) and D3/D4/D5/D6

Feature 12 was rejected on the first review. **D1 was the only blocking finding**; D2 belongs to Contracts and was explicitly withdrawn from this feature by the leader. This section documents what was actually wrong, the fix, and the re-verification. Nothing in the seeded data, the writers, `DeterministicId.cs`, `Gs1Identifiers.cs`, the four `DbContext`s, `Contracts`, or `quality.sh` was touched, per the reviewer's explicit instruction.

### D1 (BLOCKING) — what it actually was

`Order_Timeline_Documents_Carry_Every_Field_With_The_Right_Types` (the only test guarding the `order_timeline` documents' *contents*) was a presence/shape test, not a value test. Its only assertion on `causationId` was `Assert.NotEqual(Guid.Empty.ToString(), e.CausationId)`, which is true for `""`, for `null`, and for any wrong-but-non-empty GUID. Its only assertion on `totals` was `Assert.True(completed.Totals!.TotalAmount > 0)`. The reviewer's independent mutation probes proved this concretely: blanking every `causationId` in every seeded document (probe B) and corrupting every total by one cent (probe D) both left the 38-test suite green. `items[]` fields, `retailer.name`/`retailer.gln`/`company.name`/`company.gln`, and `orderDate`/`updatedAt` beyond not-null were never asserted at all.

This mattered specifically because the artefact is unreachable by any other guard: the projector — the only thing that would ever read these documents — is Phase 12, five phases away, and #7's own `mongo.writer.ts` states that a document seeded without real `causationId` values "stays on the eventId fallback permanently, even after the projector's own boot migration runs, because the migration never invents an edge that was never recorded." A wrong value here is silent and unrecoverable, exactly the class of defect CLAUDE.md's "double force where the branch has no live caller yet" rule exists for.

### The fix

**`tests/Seed.IntegrationTests/OracleFixtures/order_timeline_from_number7.json`** — the array of 6 expected `order_timeline` documents, produced by executing **#7's own `apps/seed/src/writers/mongo.writer.ts#toTimelineDocument`** over **#7's own `SAGAS`**, not by retyping or by running this C# port. Method (recorded here in full, mirroring the technique the reviewer independently used and the technique `DeterministicParityTests.cs` already used for the 17 derivation values):

1. Copied #7's own source files verbatim into a scratch directory: `apps/seed/src/deterministic.ts`, `apps/seed/src/clock.ts`, `apps/seed/src/data/*.data.ts`, `apps/seed/src/writers/mongo.writer.ts`, and the whole of `packages/shared-kernel/src/domain/*.ts` + its `index.ts` barrel.
2. Repointed only the two unresolvable module specifiers: `@otc/shared-kernel` → a relative path to the copied `shared-kernel/index.ts`; `mongodb` was never resolved at all — `openMongo`/`orderTimelineCollection`/`seedMongoTimelines`/`countMongoTimelines` (the only functions that touch the driver) were deleted from the scratch copy, since `toTimelineDocument` itself — the function under test — never imports `MongoClient`.
3. Appended `.ts` to every extension-less relative import (Node's native TypeScript support, unlike a bundler, requires the specifier to resolve to a real file on disk) and rewrote `shared-kernel`'s internal `.js`-suffixed imports (a NodeNext compiled-output convention) to `.ts` for the same reason.
4. Ran `node --experimental-transform-types run.mjs`, where `run.mjs` is `import { toTimelineDocument } from './seed/writers/mongo.writer.ts'; import { SAGAS } from './seed/data/sagas.data.ts'; console.log(JSON.stringify(SAGAS.map(toTimelineDocument), null, 2));` — exit 0, 6 documents.
5. The output matched the reviewer's own independently-obtained values exactly on every spot check reported in `review_seed_job.md` (totals `16130`/`10374`/`19450`/`23972`/`10055`/`24999`, party names/GLNs, event counts 9/9/9/9/9/5, `statusRank` 98/99) — a second independent confirmation of the same oracle, not merely a restatement of the first.

**`tests/Seed.IntegrationTests/SeedIntegrationTests.cs` — new test `Order_Timeline_Documents_Match_The_Values_Number7s_ToTimelineDocument_Produced`**: runs the real seed against real MS-SQL + MongoDB containers, fetches all 6 live documents, and for each one (matched by `orderReference`) asserts, field by field, against the oracle fixture:

- `_id`, `orderId`, `orderReference`, `orderDate`, `status`, `cancellationReason`, `currency`
- `retailer.code`/`name`/`gln`, `company.code`/`name`/`gln`
- `totals.initialAmount`/`initialDiscount`/`totalAmount`
- every `items[]` entry's `productCode`/`name`/`quantity`/`unitPrice`/`lineDiscount`, in order
- `references.despatchReference`/`invoiceReference`/`paymentReference` (including the three `null`s on the cancelled order)
- every `events[]` entry's `eventId`/`eventType`/`occurredAt`/`summary`/**`causationId`**, in order, plus `detail` (key-by-key, including the two events that carry one) when present and asserted absent when not
- `headerComplete`, `updatedAt`, `statusRank`, `timelineOrderVersion`, `processedEventKeys` (full ordered list)

The original `Order_Timeline_Documents_Carry_Every_Field_With_The_Right_Types` test was kept as-is (it still proves the document count and a few structural properties the new test doesn't restate as a separate concern) — the new test is the value-level guard the reviewer required, not a replacement.

### Re-armed with the reviewer's own probes, plus one of my own

| # | Mutation | File / line | Forced rebuild | Named test | Result |
|---|---|---|---|---|---|
| B (reviewer's) | `CausationId = entry.CausationId.ToString()` → `CausationId = string.Empty` | `src/Seed/Infrastructure/Mongo/MongoSeedWriter.cs:130` | `dotnet build src/Seed/Seed.csproj --no-incremental` | `Order_Timeline_Documents_Match_The_Values_Number7s_ToTimelineDocument_Produced` | **FAILED**: `Assert.Equal() Failure: Strings differ ↓ (pos 0) Expected: "0914f64b-f91e-4af3-927c-f9227fb92077" Actual: ""` |
| D (reviewer's) | `TotalAmount = saga.TotalAmount` → `TotalAmount = saga.TotalAmount + 1` | `src/Seed/Infrastructure/Mongo/MongoSeedWriter.cs:97` | `dotnet build src/Seed/Seed.csproj --no-incremental` | `Order_Timeline_Documents_Match_The_Values_Number7s_ToTimelineDocument_Produced` | **FAILED**: `Assert.Equal() Failure: Values differ Expected: 16130 Actual: 16131` |
| E (mine, on `items[]`) | `Quantity = line.Quantity` → `Quantity = line.Quantity + 1` | `src/Seed/Infrastructure/Mongo/MongoSeedWriter.cs:105` | `dotnet build src/Seed/Seed.csproj --no-incremental` | `Order_Timeline_Documents_Match_The_Values_Number7s_ToTimelineDocument_Produced` | **FAILED**: `Assert.Equal() Failure: Values differ Expected: 5 Actual: 6` |

Each was applied to `src/Seed/Infrastructure/Mongo/MongoSeedWriter.cs` (backed up first to `/tmp/arming_backups/MongoSeedWriter.cs.bak`), rebuilt with `--no-incremental`, run, its failure message recorded verbatim above, then restored with `cp /tmp/arming_backups/MongoSeedWriter.cs.bak src/Seed/Infrastructure/Mongo/MongoSeedWriter.cs` (never `git checkout --`), confirmed by re-reading the changed line back to its original text (and, after probe E, a `diff` against the backup confirming byte-identical restoration), then rebuilt clean each time.

### D3 — fixed. Exact counts where they were weak.

`SeedIntegrationTests.cs`'s `Row_Counts_Match_The_Datasets_Own_Declared_Sizes` now asserts `fulfillmentCounts.Stock == 215` (was `> 0`), and adds `fulfillmentCounts.Reservations == 11`, `fulfillmentCounts.DespatchItems == 10`, `billingCounts.InvoiceItems == 10` — none of which were asserted at all before. Every count in that test is now exact.

### D4 — fixed. The near-vacuous wart test now proves something.

The old `DeterministicId_Fails_The_Parity_Oracle_If_The_Skipped_Hex_Character_Is_Reintroduced` asserted `NotEqual` against the correct value with its *last* character hand-edited (the wart affects the *third* hyphen group, not the last), then `Equal` against the correct value (redundant with the `[Theory]` above it) — it could not fail in any way that would indicate the wart specifically. Replaced with `DeterministicId_Would_Differ_If_The_Skipped_Hex_Character_Were_Not_Skipped` (`tests/Seed.UnitTests/DeterministicParityTests.cs`): it independently recomputes the "fixed" (un-warted) derivation locally — `hex[12..15]` instead of production's actual `hex[13..16]` — and asserts `DeterministicId.Of("currency:USD")` does **not** equal that alternate value, using the reviewer's own re-derived "fixed" value (`8a2ac568-0944-4250-...`, third group `4250` vs production's `4507`) as the comparison target. This is a genuine, non-redundant assertion that the wart is load-bearing in the shipped code.

### D5 — fixed. Report path corrected.

`progress/impl_seed_job.md`'s file list said `Domain/MasterDataTimestamp.cs`; corrected to `Domain/Data/MasterDataTimestamp.cs`, matching where the file actually is.

### D6 — fixed: the pure tests moved to a new project, `tests/Seed.UnitTests`.

`DatasetTests.cs` and `DeterministicParityTests.cs` — no DB, no broker, pure in-memory assertions over `Domain`/`Application` types — were moved out of `tests/Seed.IntegrationTests` (which carries `Testcontainers.MsSql`, `Testcontainers.MongoDb`, EF Core and the MongoDB driver) into a new `tests/Seed.UnitTests`, mirroring the existing `SharedKernel.UnitTests`/`Contracts.UnitTests` pattern exactly. `Seed.UnitTests.csproj` references only `src/Seed/Seed.csproj` and carries no `Testcontainers.*` package reference, so it builds and runs without a container runtime — the project's name and its actual dependency graph now agree, which was D6's complaint. This genuinely reduces cost (34 pure tests run in ~140 ms with zero container startup) rather than being a cosmetic rename, so it was worth doing rather than just noting. Both projects were registered in `OrderToCash.sln` via `dotnet sln add`, under the `tests` solution folder, matching every other test project's registration. This is the one place this round touched a path outside the literal `src/Seed/**` / `tests/Seed.IntegrationTests/**` scope line — `tests/Seed.UnitTests/**` — done because the coordinator's own D6 instruction ("move them if it is genuinely a move") requires a new project directory to execute; nothing in `src/Orders|Fulfillment|Billing|Notifications`, `src/Contracts`, or any other feature's test project was touched.

### Re-verification after all of the above

- `dotnet build OrderToCash.sln --no-incremental` — 0 warnings, 0 errors (17 projects, including the new `Seed.UnitTests`).
- `dotnet format OrderToCash.sln --verify-no-changes` — clean.
- `./quality.sh` (full) — **all green, 164 tests**: `SharedKernel.UnitTests` 32/32, `Contracts.UnitTests` 21/21, `Seed.UnitTests` 34/34 (new project), `Notifications.IntegrationTests` 7/7, `Orders.IntegrationTests` 12/12, `Fulfillment.IntegrationTests` 19/19, `Billing.IntegrationTests` 22/22, `Seed.IntegrationTests` 5/5, `Architecture.Tests` 12/12.
- `./init.sh` — exits 0; `1 feature in_progress: seed_job` (set back to `in_progress` by the rejection; this session sets it to `in_review` again below).

### Updated file list (paths that changed since the first submission)

- `tests/Seed.IntegrationTests/DatasetTests.cs`, `tests/Seed.IntegrationTests/DeterministicParityTests.cs` → **moved** to `tests/Seed.UnitTests/` (D6), namespace `OrderToCash.Seed.IntegrationTests` → `OrderToCash.Seed.UnitTests`.
- `tests/Seed.UnitTests/Seed.UnitTests.csproj` — new project (D6).
- `tests/Seed.IntegrationTests/OracleFixtures/order_timeline_from_number7.json` — new (D1), `CopyToOutputDirectory=PreserveNewest` in the csproj, mirroring `Contracts.UnitTests/GoldenEnvelopes`.
- `tests/Seed.IntegrationTests/SeedIntegrationTests.cs` — new test `Order_Timeline_Documents_Match_The_Values_Number7s_ToTimelineDocument_Produced` + its comparison helpers (D1); exact counts added to `Row_Counts_Match_The_Datasets_Own_Declared_Sizes` (D3).
- `tests/Seed.UnitTests/DeterministicParityTests.cs` — `DeterministicId_Fails_The_Parity_Oracle_If_The_Skipped_Hex_Character_Is_Reintroduced` replaced with `DeterministicId_Would_Differ_If_The_Skipped_Hex_Character_Were_Not_Skipped` (D4).
- `OrderToCash.sln` — `tests/Seed.UnitTests/Seed.UnitTests.csproj` added under the `tests` solution folder.
- `feature_list.json` — status only, set back to `in_review`.

Not touched, as instructed: the seeded data, `src/Seed/Infrastructure/Persistence/*Writer.cs`, `src/Seed/Domain/Deterministic/DeterministicId.cs`, `src/Seed/Domain/Deterministic/Gs1Identifiers.cs`, the four `DbContext`s, `src/Contracts/**`, `quality.sh`.
