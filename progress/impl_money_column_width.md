# impl: money_column_width (feature 44)

## What was wrong, and what changed

Three phases carried an unjustified `int` money column width, on the mistaken
belief that it was "spec parity" with #7. `specs/shared/requirements.md` R1
and `specs/shared/domain-model.md` M1 both say only "integer minor units" —
never a width. `int` was #7's own MySQL choice (irrelevant to #8, whose
`Money` is `long`), and it forced an unchecked `long -> int` narrowing cast at
every money boundary in the Seed writers.

Per the human-gate ruling: the thirteen money columns are now `bigint` in
both `otc_orders` and `otc_billing`, the two Phase 6 initial migrations were
**amended in place** (not a widening migration), and all thirteen narrowing
casts are deleted, not made checked.

## The thirteen columns, before/after, read from the live databases

Verified directly against `INFORMATION_SCHEMA.COLUMNS` on the live compose
stack (`otcnet-mssql`) after applying the amended migrations:

**`otc_orders`**

| Table | Column | Before | After |
|---|---|---|---|
| `products` | `price` | `int` | `bigint` |
| `orders` | `initial_amount` | `int` | `bigint` |
| `orders` | `initial_discount` | `int` | `bigint` |
| `orders` | `total_amount` | `int` | `bigint` |
| `order_items` | `price` | `int` | `bigint` |
| `order_items` | `discount` | `int` | `bigint` |

**`otc_billing`**

| Table | Column | Before | After |
|---|---|---|---|
| `credits` | `credit_limit` | `int` | `bigint` |
| `credit_items` | `amount` | `int` | `bigint` |
| `invoices` | `amount` | `int` | `bigint` |
| `invoices` | `discount` | `int` | `bigint` |
| `invoices` | `total_amount` | `int` | `bigint` |
| `invoice_items` | `price` | `int` | `bigint` |
| `payments` | `amount` | `int` | `bigint` |

`otc_fulfillment` and `otc_notifications` were checked (entity classes read,
not assumed): neither has a money column — `Stock.Units`,
`Stock.ReservedUnits`, `Stock.LowStockThreshold`, `Reservation.Units`,
`DespatchItem.Units` are all unit counts, never minor-units amounts.

Non-money `int` columns that stay `int` on purpose (verified against the live
schema, see "the guard" below): `currencies.decimal_points` (a
count of decimal places), `order_items.quantity` /
`invoice_items.units` (unit counts), `order_number_sequences.id` /
`.next_value` and `invoice_number_sequences.id` / `.next_value` (sequence
counters, explicitly `int` per the Databases doc and feature `db_orders`
review D2), `saga_commands.attempts` (a retry counter).

## Files touched

**Entities** (`int` → `long` on the money property only; unit/count
properties like `Quantity`/`Units` were left `int`):
`src/Orders/Infrastructure/Persistence/Entities/{Order,OrderItem,Product}.cs`,
`src/Billing/Infrastructure/Persistence/Entities/{Credit,CreditItem,Invoice,InvoiceItem,Payment}.cs`.

No EF `Configuration` class needed a change: none of them declared
`HasColumnType` for a money column explicitly (they only do that for
`datetime2(3)`/`char(3)`/`nvarchar(n)`), so EF Core infers `bigint` from the
`long` property automatically — confirmed by regenerating the migrations and
diffing.

**Migrations amended in place** (same migration id, same filename, same
timestamp — regenerated via `dotnet ef migrations remove` +
`dotnet ef migrations add InitialCreate --output-dir
Infrastructure/Persistence/Migrations`, then the auto-timestamped
filename/`[Migration("...")]` id were manually restored to the original
`20260901100855_InitialCreate` / `20260901110439_InitialCreate` so the diff
is the type change only, not a migration rename):
- `src/Orders/Infrastructure/Persistence/Migrations/20260901100855_InitialCreate.cs`
- `src/Orders/Infrastructure/Persistence/Migrations/20260901100855_InitialCreate.Designer.cs`
- `src/Orders/Infrastructure/Persistence/Migrations/OrdersDbContextModelSnapshot.cs`
- `src/Billing/Infrastructure/Persistence/Migrations/20260901110439_InitialCreate.cs`
- `src/Billing/Infrastructure/Persistence/Migrations/20260901110439_InitialCreate.Designer.cs`
- `src/Billing/Infrastructure/Persistence/Migrations/BillingDbContextModelSnapshot.cs`

Diffed against a pre-change backup: every changed line is exactly one of the
thirteen `int`/`"int"` → `long`/`"bigint"` pairs, nothing else moved.

`dotnet ef migrations has-pending-model-changes` reports **no drift** for
both `OrdersDbContext` and `BillingDbContext`. Both migrations were applied
to freshly-emptied databases on the live compose stack and re-applied a
second time cleanly (`No migrations were applied. The database is already up
to date.`).

**Narrowing casts deleted** — 13 total, not the 3 the brief named (the brief
undercounted; both Orders and Billing seed writers had them):

`src/Seed/Infrastructure/Persistence/OrdersSeedWriter.cs` (6):
- line 64: `entity.Price = (int)product.Price;` → `entity.Price = product.Price;`
- line 137: `entity.InitialAmount = (int)saga.InitialAmount;` → unwrapped
- line 138: `entity.InitialDiscount = (int)saga.InitialDiscount;` → unwrapped
- line 139: `entity.TotalAmount = (int)saga.TotalAmount;` → unwrapped
- line 159: `entity.Price = (int)line.UnitPrice;` → unwrapped
- line 161: `entity.Discount = (int)line.LineDiscount;` → unwrapped

`src/Seed/Infrastructure/Persistence/BillingSeedWriter.cs` (7):
- line 46: `entity.CreditLimit = (int)credit.CreditLimit;` → unwrapped
- line 74: `entity.Amount = (int)entry.Amount;` → unwrapped
- line 94: `entity.Amount = (int)invoice.Amount;` → unwrapped
- line 95: `entity.Discount = (int)invoice.Discount;` → unwrapped
- line 96: `entity.TotalAmount = (int)invoice.TotalAmount;` → unwrapped
- line 115: `entity.Price = (int)item.Price;` → unwrapped
- line 129: `entity.Amount = (int)payment.Amount;` → unwrapped

`FulfillmentSeedWriter.cs` and `MongoSeedWriter.cs` (Mongo read-model
document, `OrderTimelineDocument`) were checked and had none — the Mongo
document's `InitialAmount`/`UnitPrice`/etc. were already `long`, so this
narrowing was specific to the MS-SQL entity boundary.

**Schema tests corrected** (were asserting the wrong value, per the brief's
own comparison to `order_number_sequences.next_value` in feature 9):
- `tests/Orders.IntegrationTests/SchemaColumnTypeTests.cs`: `products.price`,
  `orders.initial_amount`/`initial_discount`/`total_amount`,
  `order_items.price`/`discount` — `"int"` → `"bigint"`.
- `tests/Billing.IntegrationTests/SchemaColumnTypeTests.cs`:
  `credits.credit_limit`, `credit_items.amount`,
  `invoices.amount`/`discount`/`total_amount`, `invoice_items.price`,
  `payments.amount` — `"int"` → `"bigint"`. `invoice_number_sequences.next_value`
  stays `"int"` (a sequence counter, not money — unchanged and correctly
  documented in the file's own header comment).

## The guard (acceptance 4)

New: `tests/Orders.IntegrationTests/NoMoneyColumnIsIntTests.cs` and
`tests/Billing.IntegrationTests/NoMoneyColumnIsIntTests.cs`
(`No_Money_Column_Is_Int`).

**On the discriminator, as asked**: a positive whitelist of the thirteen
money-column names cannot be closed — a fourteenth money column, added later
and left `int` by mistake, would simply not be on the list and would pass
silently. There is no structural signal in `INFORMATION_SCHEMA` that marks a
column "this one is money" (no comment/extended-property convention exists in
this schema), so the check is closed the other way round instead: it
enumerates **every** `int`-typed column that actually exists in the real,
migrated database, and asserts that set is *exactly* a short, named allow-list
of columns that are legitimately not money, each with its reason in a
comment (a unit count, a retry counter, a decimal-places count, a sequence's
own `id`/`next_value`). Any `int` column not on that allow-list — whether a
future money column or an unrelated new counter — fails the test and forces a
deliberate decision, rather than requiring someone to remember to extend a
money list. This is closed over "all int columns", which is what the acceptance
criterion asks for; it is not closed over "the meaning of every column", which
no schema-only check could be. I want to be explicit that this is the
honest limit: nothing at the `INFORMATION_SCHEMA` level can *prove* a given
`bigint` column is money either — the guard proves the negative ("no int
column is unaccounted for"), which is what stops the regression from
returning silently, not a positive semantic classification.

Orders allow-list (5, matches the live schema exactly):
`currencies.decimal_points`, `order_items.quantity`,
`order_number_sequences.id`, `order_number_sequences.next_value`,
`saga_commands.attempts`.

Billing allow-list (3, matches the live schema exactly):
`invoice_items.units`, `invoice_number_sequences.id`,
`invoice_number_sequences.next_value`.

The test also asserts the reverse direction (every allow-listed column still
exists and is still `int`), so a rename or a type change on one of the
"legitimately int" columns is caught too, not just additions.

**Arming, per the CLAUDE.md protocol** (Orders side; Billing follows the same
shape and was not separately armed — the two tests are structurally
identical, differing only in the connection/allow-list):

1. Backed up
   `src/Orders/Infrastructure/Persistence/Migrations/20260901100855_InitialCreate.cs`
   to the scratchpad (`md5sum 96fe4153f80218de6e0015d5b6b11f74`).
2. Edited the *migration file* (not the entity — the guard reads the real,
   migrated schema, so the regression has to be at that level) to change
   `products.price` back to `table.Column<int>(type: "int", ...)`.
3. `touch`ed the file, ran `dotnet build --no-incremental`, then ran
   `dotnet test --filter NoMoneyColumnIsIntTests` alone.
4. **Confirmed FAIL**, message verbatim: `Found int column(s) not accounted
   for as known non-money columns: products.price. If this is a monetary
   amount, widen it to bigint. If it is legitimately not money (a count, a
   sequence value, a retry counter), add it to _knownNonMoneyIntColumns with
   a reason.`
5. Restored from the scratchpad backup (not `git checkout --`, since the
   migration file is not tracked in a green state at this point in the
   session anyway; used `cp` from the pre-arming copy).
6. Confirmed the restore by `md5sum` (`96fe4153f80218de6e0015d5b6b11f74` on
   both sides) and by re-reading the changed line (`price =
   table.Column<long>(type: "bigint", nullable: false)`).
7. `touch`ed the file again, ran `dotnet build --no-incremental` (forced
   rebuild before the confirming run, per the protocol), then re-ran the
   filtered test.
8. **Confirmed GREEN**: `Passed
   OrderToCash.Orders.IntegrationTests.NoMoneyColumnIsIntTests.No_Money_Column_Is_Int`.

## Seed / 413-row parity (acceptance 5)

Ran the seed job against the live compose stack
(`dotnet run --project src/Seed/Seed.csproj`, after `dotnet ef database
update` had applied the amended, now-empty-again migrations):

```
orders:       currencies=3 products=12 retailers=7 companies=22 orders=6 orderItems=11 outbox=17
fulfillment:  stock=215 reservations=11 despatches=5 despatchItems=10 outbox=12
billing:      credits=154 creditItems=15 invoices=5 invoiceItems=10 payments=5 outbox=21
mongo:        order_timeline=6
```

`3 + 12 + 7 + 22 + 154 + 215 = 413` — the same master-data row count history.md
records for the Phase 7 diff against #7, unchanged. Directly spot-checked
`products.price` on the live `otc_orders` database against the twelve source
values in `src/Seed/Domain/Data/ProductSeed.cs` (e.g. `PRD-0001 = 24999`,
`PRD-0012 = 895`) — exact match, byte for byte. None of the dataset's money
values were ever close to `int.MaxValue`, so the old `int` cast never
actually truncated anything in this seed data; the risk it carried was
latent, and this feature removes it rather than having "proven" it by a
before/after value diff (there was nothing to diff — the values are
identical because no truncation ever occurred, not because I compared two
different runs).

Also ran the Testcontainers-backed `Seed.IntegrationTests` and
`Seed.UnitTests` suites (fresh, isolated databases per run, not the shared
compose stack) — 5/5 and 34/34 green, including
`Row_Counts_Match_The_Datasets_Own_Declared_Sizes` and
`Running_The_Seed_Twice_Is_A_No_Op`.

## Full verification

- `dotnet format OrderToCash.sln --verify-no-changes`: clean.
- `dotnet build OrderToCash.sln`: 0 warnings, 0 errors.
- `dotnet test OrderToCash.sln --collect:"XPlat Code Coverage"`: full solution
  green — `SharedKernel.UnitTests` 32/32, `Contracts.UnitTests` 21/21,
  `Seed.UnitTests` 34/34, `Notifications.IntegrationTests` 7/7,
  `Fulfillment.IntegrationTests` 19/19, `Orders.IntegrationTests` 13/13,
  `Billing.IntegrationTests` 23/23, `Seed.IntegrationTests` 5/5,
  `Architecture.Tests` 12/12. 166 tests total, 0 failed.
- `./init.sh`: still exits 0.

## What I found beyond the brief's own count

The brief said "the three [narrowing casts] in `OrdersSeedWriter.cs`" — there
were actually **six** in that file (it undercounted its own example) plus
**seven** more in `BillingSeedWriter.cs` that the brief's "Do not touch"
constraints explicitly listed `src/Seed/**` as in-scope for but did not
itemize. All thirteen map one-to-one to the thirteen widened columns, which
is a reasonable closure check in itself: 13 columns, 13 casts, 13 deletions.

## Traceability

This feature is `"sdd": false`; its five acceptance criteria map to:
1. `NoMoneyColumnIsIntTests.No_Money_Column_Is_Int` (both projects) +
   `SchemaColumnTypeTests.Every_Table_Has_The_Expected_Columns_And_SqlTypes`
   (both projects, now asserting `bigint`) — all read from
   `INFORMATION_SCHEMA.COLUMNS` on a real, migrated database.
2. `dotnet ef migrations has-pending-model-changes` (no drift, reported
   above) plus the unchanged migration ids/filenames.
3. Grep-verified: `grep -rn "(int)" src/Seed` returns nothing under
   `OrdersSeedWriter.cs`/`BillingSeedWriter.cs`.
4. `NoMoneyColumnIsIntTests.No_Money_Column_Is_Int`, armed per above.
5. The seed job re-run against the live stack, plus
   `Seed.IntegrationTests.Row_Counts_Match_The_Datasets_Own_Declared_Sizes`.

No `specs/shared/` requirement id changes hands here — R1/M1 were already
correct; this feature aligns the implementation with the requirement's
actual text, not a specification amendment.

## An unrelated accident during this session, corrected

While updating `feature_list.json`'s status field at the end, an errant
`git checkout -- feature_list.json` (intended to discard a bad
`json.dump` re-serialization that had re-escaped every em-dash in the file
to `—`) instead reverted the file to its last **committed** state,
which predates this feature's entry entirely — silently dropping feature
44's own uncommitted addition to the backlog. This was caught immediately
(`git diff` came back empty and the feature count dropped from 43 to 42),
and was recoverable only because the original JSON block for feature 44 was
captured verbatim in this session's own tool output before the mistake, so
it could be reinserted by hand, text-for-text, rather than by re-deriving it.
The final `git diff feature_list.json` is a clean 16-line insertion: the
original feature-44 block, unchanged, with `"status"` set to `"in_review"`.
Flagging this because it is exactly the class of mistake CLAUDE.md's arming
protocol warns about (destructive git operations on an uncommitted file with
no backup taken first) — it just happened outside the arming protocol's own
context.
