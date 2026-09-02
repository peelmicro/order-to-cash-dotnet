# review: money_column_width (feature 44)

**Verdict: APPROVED** — zero blocking defects, three advisories (A1–A3), one process note carried to `progress/history.md`.

`sdd: false`, so there is no `specs/<feature>/` triple-doc to walk and C6 does not apply. The feature's five `acceptance` items stand in for requirement ids; each is traced to a named test below.

---

## What this review re-ran, and what it did not

**Not re-run, deliberately.** The leader had already established three facts and instructed me not to re-prove them: the thirteen money columns reading `bigint` from `INFORMATION_SCHEMA` on the live `otc_orders`/`otc_billing`; the `grep -rnE '\(int\)[A-Za-z_.]*(Amount|Price|Discount|Limit|Total)' src/` returning nothing; and the seed's 413-row master-data re-dump hashing to the Phase 7 baseline's `7602fe78…`. I did not re-dump the master data or re-run the seed job against the live stack. The `Seed.IntegrationTests` row-count and idempotency tests did run inside `./quality.sh` (5/5), which is the in-suite half of that claim.

**Re-run in full, because the claim is about the full suite.** `./quality.sh` — the implementer claims solution-wide green, and that is a whole-suite claim, so a partial re-run would not test it.

**Run independently, because these are the claims under test.** Everything in the four sections that follow: my own numeric-type sweep across all four live databases, my own arming of both guards on columns the implementer did not arm, `has-pending-model-changes` for all four contexts, and a two-pass migration apply against freshly created empty databases.

---

## CHECKPOINTS walked

### C1 — the harness is complete

- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all present
- [x] `progress/current.md` and `progress/history.md` present
- [x] `.claude/agents/` holds six definitions (leader, spec_author, implementer, reviewer, test_maintainer, suite_runner)
- [x] every agent declares its model — `init.sh` verifies: `implementer` pins sonnet, `suite_runner`/`test_maintainer` pin haiku, `leader`/`reviewer`/`spec_author` documented as deliberately unpinned
- [x] `./init.sh` exits **0**

### C2 — state is coherent

- [x] at most one feature `in_progress` — in fact **zero**; feature 44 was `in_review`, everything else `pending` or `done` (30 / 12 / 1)
- [x] every status is in `rules.valid_status`
- [x] every `done` feature has passing tests — 166/166 green in this review's own `quality.sh` run
- [x] `progress/current.md` describes the active session (2026-09-02) and is not leftovers. *Minor incoherence, addressed to the leader, not to this feature:* it names `orders_aggregate` (id 13) as the active feature and never mentions feature 44, which ran to completion inside the same session. Fix at session close.
- [x] no `blocked` features

### C3 — architecture is respected

- [x] no framework reference inside any `Domain/` folder — **verified by running** `Architecture.Tests`, 12/12 green, not by eye
- [x] no cross-service database access, no foreign key across a service boundary — this feature changed column *types* only; no FK, index or relationship line appears anywhere in the diff (verified: the `.Designer.cs` and `ModelSnapshot.cs` diffs contain **nothing** but `b.Property<int|long>` and `HasColumnType("int"|"bigint")` lines)
- [x] no shared runtime code beyond `src/SharedKernel` and `src/Contracts` — both untouched (`git status --porcelain` on those paths is empty)
- [x] `src/SharedKernel` still has zero `PackageReference` — the single `grep` hit in `SharedKernel.csproj:10` is inside the comment forbidding them
- [x] **no `decimal` in domain arithmetic — `Money` is `long` minor units.** This is the checkpoint the feature exists to satisfy, and it now holds *in the column* as well as in the type. My independent sweep (below) found no `decimal`, `double`, `float`, `smallint` or `int` money anywhere in `src/`
- [x] Kafka-fact / NATS-RPC classification — no transport code exists yet; none introduced
- [x] no stray debug logging, no context-free TODOs — `grep -rn TODO src/Seed src/Orders src/Billing` is empty

### C4 — verification is real

- [x] `./quality.sh` passes — exit **0**, format clean, build 0 warnings / 0 errors, **166/166 tests**, **1 m 38.7 s** wall-clock (`real 1m38.717s`, host idle). That is 164 → 166: the two new guard tests, and nothing lost.
- [x] domain tests are pure — enforced by `Architecture.Tests`, run this pass
- [x] integration tests use Testcontainers for .NET against real MS-SQL (`mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04`, the same tag compose pins) — both new guard tests call `fixture.CreateFreshDatabaseAsync(...)` then `db.Database.MigrateAsync()`, so they read a schema a real migration really built, never a mock and never the shared compose database
- [ ] coverage thresholds met — **not evaluable.** `quality.sh` prints nine overlapping per-project line-rates (95.8 / 91.4 / 91.3 / 85.0 / 77.3 / 68.0 / 20.1 / 12.5 / 0.0) and deliberately does not gate, with an in-file rationale deferring enforcement to feature 34 (`quality.sh:2-12, 80-84`). Pre-existing, honestly disclosed, carried forward from the Phase 6 and Phase 7 reviews — **not this feature's defect**
- [x] no Jest anywhere — xUnit only

### C5 — the session closed cleanly

- [x] no suspicious untracked files — the untracked set is exactly `progress/impl_money_column_width.md`, the two new guard tests, and `specs/orders_aggregate/` (the spec author's, at a human gate, explicitly out of this feature's scope). `bin/`, `obj/` and `TestResults/` are gitignored (`.gitignore:12`)
- [x] `progress/history.md` has an entry including its effort record — appended by this review
- [x] `feature_list.json` reflects the true state — feature 44 set `done` by this review
- [x] the human will be told what was done and how to test it manually — leader's report
- [x] **Claude did not commit** — no `git commit`, no `git push`. The working tree is byte-identical to the state at the start of this review: both migration files I armed restore to the implementer's own recorded MD5s (`96fe4153f80218de6e0015d5b6b11f74` Orders, `e3415bacdc28148f51f6253ceaa68806` Billing), and the two scratch databases I created were dropped

*Recorded for accuracy:* `docs/PROCESS.md` acquired a 6-line insertion at 10:01, during this review. It is the leader's, written in parallel in the same session; `docs/` is outside `src/`, `tests/` and `apps/web/` and is the leader's to edit directly. Not mine, not this feature's, not a defect.

### C6 — spec-driven development

**Not applicable.** Feature 44 is `sdd: false`. `init.sh` confirms "0 sdd feature(s) past pending have their triple-doc" — feature 13 `orders_aggregate` is still `pending` while its spec sits at the human gate, so no `sdd: true` feature is past `pending` and every C6 box is vacuous.

### C7 — spec-reuse fidelity

Not walked as a section (it is a session/assessment-level checkpoint, not a per-feature one), but the box that this feature touches is worth marking explicitly: **`specs/shared/` is untouched.** `git status --porcelain specs/shared` is empty. This matters more than usual here, because the feature is a correction to a *misreading* of `specs/shared/` — and the correct response to a misread spec is to change the code, never the spec. That is what happened.

---

## Acceptance → test mapping, verified

| # | Acceptance item | Test / evidence | Verified how |
|---|---|---|---|
| 1 | all 13 money columns are `bigint` on the live databases, from `INFORMATION_SCHEMA` | `Orders.IntegrationTests.SchemaColumnTypeTests.Every_Table_Has_The_Expected_Columns_And_SqlTypes` (6 columns) + `Billing.IntegrationTests.SchemaColumnTypeTests.Every_Table_Has_The_Expected_Columns_And_SqlTypes` (7 columns) | Both **fail by name** when armed (§ Arming). Independently re-queried on the live stack and on two DBs I built from the migrations myself |
| 2 | initial migrations amended, not a widening migration added | `Orders.IntegrationTests.MigrationTests` / `Billing.IntegrationTests.MigrationTests` + `dotnet ef migrations has-pending-model-changes` | Migration ids unchanged (`20260901100855_InitialCreate`, `20260901110439_InitialCreate`) and each database's `__EFMigrationsHistory` holds **exactly one row**, that id — no `ALTER` scar. All four contexts report *"No changes have been made to the model since the last migration."* Applied to two empty databases and re-applied: *"No migrations were applied. The database is already up to date."* |
| 3 | every `(int)` narrowing cast on a money value **deleted**, not made checked | compile-time — the entity properties are `long`, so a surviving cast would be a visible narrowing; plus `Seed.IntegrationTests` (5/5) writing every money value end-to-end | All 13 casts gone from the diff; **no `checked` block introduced**. `grep -rnE '\((int\|short\|byte)\)' src/` returns exactly one hit, `SharedKernel/Quantity.cs:50`, which is a `double → int` *unit count* guarded by an explicit range check on lines 34–35 and is untouched by this feature |
| 4 | a test asserts no money column is `int`, so the regression cannot return silently | **`Orders.IntegrationTests.NoMoneyColumnIsIntTests.No_Money_Column_Is_Int`** and **`Billing.IntegrationTests.NoMoneyColumnIsIntTests.No_Money_Column_Is_Int`** | Armed by me, both projects, on columns the implementer did not arm; both directions of the assertion fire (§ Arming) |
| 5 | the seed re-runs and 413-row master-data parity against #7 still holds | `Seed.IntegrationTests.Row_Counts_Match_The_Datasets_Own_Declared_Sizes`, `Running_The_Seed_Twice_Is_A_No_Op` (5/5 green this pass) | Established by the leader against the live stack — same 413 rows, same combined SHA-256 `7602fe78…` as the Phase 7 baseline. Not re-proved here, per the brief |

---

## The guard — is it closed, or a name list? (concentration 1)

**It is closed, and it is closed in the right direction.** The implementer was asked to say honestly if a name list was the only robust discriminator, and instead of settling for one it inverted the problem, which is the better answer.

A positive whitelist of thirteen money-column names proves only that those thirteen stayed widened. A fourteenth money column added later and left `int` is simply not on the list, and passes in silence — the exact failure mode this feature exists to correct, reproduced in the guard meant to prevent it. `INFORMATION_SCHEMA` carries no structural signal that says "this column is money", so a *positive* semantic classification is genuinely unavailable.

`NoMoneyColumnIsIntTests` therefore enumerates **every `int`-typed column that actually exists in the real migrated schema** and asserts that set equals a short allow-list of columns that are legitimately not money, each carrying its reason inline (`NoMoneyColumnIsIntTests.cs:32-39` Orders, `:32-37` Billing). Any `int` column not on that list fails — a future money column, or an unrelated new counter, either way forcing a deliberate decision rather than relying on someone remembering to extend a money list. It also asserts the reverse (`:78-84` / `:76-82`): every allow-listed column must still exist and still be `int`, so a rename or a widening of one of the legitimate counters is caught too.

I verified the allow-lists are exact and not padded, by querying the live schema myself: Orders' five (`currencies.decimal_points`, `order_items.quantity`, `order_number_sequences.id`, `order_number_sequences.next_value`, `saga_commands.attempts`) and Billing's three (`invoice_items.units`, `invoice_number_sequences.id`, `invoice_number_sequences.next_value`) are precisely the `int` columns present — no extra name is parked there "just in case", which would have been the way to quietly re-open the hole.

The implementer's stated limit is accurate and worth preserving: the guard proves a **negative** ("no `int` column is unaccounted for"), not a positive semantic classification, and no schema-only check could do the latter. That honesty is the right disposition here.

**Residual aperture, and why it is narrow** — see advisory A1. The guard filters on `DATA_TYPE = 'int'` alone. It is narrower than it looks, though, because two neighbouring tests close most of what it leaves open: `SchemaColumnTypeTests.No_Unexpected_Table_Or_Column_Exists` (`SchemaColumnTypeTests.cs:231-239`) fails on **any** new column of **any** type, and `Every_Table_Has_The_Expected_Columns_And_SqlTypes` pins the exact `data_type` of every known column. So the only way a narrow money column reaches the schema today is if someone adds a *brand-new* column **and** declares it `smallint`/`decimal`/`money` **and** writes that type into the expected list by hand. Narrow enough not to block; cheap enough to close (A1).

---

## Did the sweep find everything? (concentration 2)

The leader's sweep was by column *name* and could not have found a monetary column named something else. I searched by **type**, in both directions, which is the complementary search.

**Live schema, all four databases, every numeric type** (`int`, `bigint`, `smallint`, `tinyint`, `decimal`, `numeric`, `money`, `smallmoney`, `float`, `real`) — 31 columns total. Every one is accounted for:

- **13 money columns, all `bigint`** — the thirteen named
- **3 `outbox.seq bigint`** (Orders / Fulfillment / Billing) — an `IDENTITY` sequence, not money, correctly `bigint` already
- **15 `int`, none of them money** — `currencies.decimal_points`, `order_items.quantity`, `invoice_items.units`, `saga_commands.attempts`, `stock.units` / `.reserved_units` / `.low_stock_threshold`, `reservations.units`, `despatch_items.units`, and the three sequence tables' `id` / `next_value` pairs
- **zero `decimal`, `numeric`, `money`, `smallmoney`, `float`, `real`, `smallint` or `tinyint` columns anywhere** — so there is no monetary value hiding behind a *different* narrow or lossy type either, which is the failure the name-based search could also have missed

**Entity types, independently.** I read every `*/Infrastructure/Persistence/Entities/*.cs` and listed every numeric property. All 13 money properties are `long`. The remaining `int` properties are `Units`, `ReservedUnits`, `LowStockThreshold`, `Quantity`, `DecimalPoints`, `Attempts`, `Id`/`NextValue` on the three sequence entities — all counts. `src/Seed/Infrastructure/Mongo/OrderTimelineDocument.cs` was already `long` for `InitialAmount`, `InitialDiscount`, `TotalAmount`, `UnitPrice`, `LineDiscount` (`:95-119`) and `int` only for `StatusRank`, `TimelineOrderVersion`, `Quantity` — correct, and confirming the implementer's claim that the narrowing was specific to the MS-SQL entity boundary.

**`otc_fulfillment` and `otc_notifications` have no money column** — the implementer asserted this from the entity classes; I confirmed it from the live schema, dumping every column of `despatches`, `despatch_items`, `stock` and `reservations`. A despatch carries `despatch_reference`, dates and the four business codes, and no value at all. So the absence of a guard in those two projects is currently vacuous rather than wrong (advisory A2).

**Conclusion: the sweep was complete.** This is *not* the shape of feature 9's missing-foreign-keys defect, where the search was narrower than the requirement — here the requirement ("no money is narrow") was checked against a type-space enumeration, not a name pattern, and the two searches agree on the same 13.

---

## Migrations amended in place (concentration 3)

- **No drift, every context.** `dotnet ef migrations has-pending-model-changes` for `Orders`, `Billing`, `Fulfillment` and `Notifications`: all four report *"No changes have been made to the model since the last migration."* (The command needs `MSSQL_APP_PASSWORD` exported — the design-time factory refuses to guess it, review D6 of `db_orders`, and it still refuses loudly.)
- **Applies to an empty database, and re-applies cleanly.** I created `otc_rev44_orders` and `otc_rev44_billing` empty on the live stack and ran `dotnet ef database update` twice against each: pass 1 *"Applying migration '20260901100855_InitialCreate'"* / *"…'20260901110439_InitialCreate'"*, pass 2 *"No migrations were applied. The database is already up to date."* Both scratch databases dropped afterwards.
- **The schema those migrations build is right, not just the one that happens to be live.** Querying my two freshly built databases for `DATA_TYPE='int'` returned exactly the eight allow-listed non-money columns and nothing else — so the `bigint` widening lives in the migration, not in a hand-patched live database.
- **Genuinely amended, not superseded.** One migration file per context, ids unchanged and matching their filenames, and each database's `__EFMigrationsHistory` holds exactly one row. The `.Designer.cs` and `ModelSnapshot.cs` diffs contain **only** `b.Property<int|long>` and `HasColumnType("int"|"bigint")` lines — no `ProductVersion` bump, no annotation churn, no reordering. That is the cleanest possible form of the human gate's ruling.

---

## Arming — mine, not the implementer's (concentration 1, continued)

Full protocol each time: backup taken by `cp` to the scratchpad (never `git checkout --`), violation introduced in the **migration** file (the guards read the migrated schema, so that is the level the regression lives at), `touch` + `dotnet build --no-incremental` **before every run**, restore from the backup, `md5sum` **and** re-read of the changed line, then a second forced rebuild before the confirming green.

I deliberately armed **different columns from the implementer's** (they used `products.price`), and I armed **Billing**, which they had not armed at all.

| # | Violation | Test | Result | Message (verbatim) |
|---|---|---|---|---|
| 1 | `orders.total_amount` `bigint` → `int` (`20260901100855_InitialCreate.cs:213`) | `Orders…NoMoneyColumnIsIntTests.No_Money_Column_Is_Int` | **FAILED** | `Found int column(s) not accounted for as known non-money columns: orders.total_amount. If this is a monetary amount, widen it to bigint. If it is legitimately not money (a count, a sequence value, a retry counter), add it to _knownNonMoneyIntColumns with a reason.` |
| 2 | same violation | `Orders…SchemaColumnTypeTests.Every_Table_Has_The_Expected_Columns_And_SqlTypes` | **FAILED** | `orders.total_amount: expected data_type 'bigint', got 'int'` |
| 3 | `payments.amount` `bigint` → `int` (`20260901110439_InitialCreate.cs:160`) | `Billing…NoMoneyColumnIsIntTests.No_Money_Column_Is_Int` | **FAILED** | `Found int column(s) not accounted for as known non-money columns: payments.amount. …` |
| 4 | same violation | `Billing…SchemaColumnTypeTests.Every_Table_Has_The_Expected_Columns_And_SqlTypes` | **FAILED** | `payments.amount: expected data_type 'bigint', got 'int'` |
| 5 | `order_items.quantity` `int` → `bigint` (`:252`) — the **reverse** assertion, an allow-listed counter drifting | `Orders…NoMoneyColumnIsIntTests.No_Money_Column_Is_Int` | **FAILED** | `Known non-money int column(s) no longer exist in the schema (rename or type change?): order_items.quantity. Update _knownNonMoneyIntColumns to match.` |
| — | all restored, forced rebuild | full `./quality.sh` | **GREEN**, 166/166, exit 0 | — |

Probe 5 is the one worth noting: it proves the guard is not merely a one-sided "nothing extra is `int`" check that a schema could satisfy by having no `int` columns at all. Both halves are load-bearing.

**Probes 2 and 4 also answer concentration 4 directly.** In feature 9 a test asserting the wrong value locked a defect in place; the inverted risk here was `SchemaColumnTypeTests` being flipped to `"bigint"` as a bookkeeping edit that no longer discriminated. It does discriminate: it failed by name, on the exact column, with the exact expected/actual pair. And `invoice_number_sequences.next_value` correctly **stayed** `"int"` in that file — the edit was surgical, not a blanket find-and-replace, which is the tell that would have suggested otherwise.

---

## Nothing else moved (concentration 5)

`git status --porcelain` on `specs/shared`, `infra`, `src/Contracts`, `src/SharedKernel`, `init.sh`, `quality.sh`, `AGENTS.md`, `CHECKPOINTS.md`, `n8n`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `OrderToCash.sln`, `docker-compose.infra.yml` — **empty**. All untouched.

The modified set is exactly: 8 entity files, 6 migration/snapshot files, 2 seed writers, 2 schema tests, 2 new guard tests, plus `CLAUDE.md` / `progress/current.md` / `feature_list.json` (the leader's). `specs/orders_aggregate/` is untracked and belongs to the spec author's parallel work at a human gate — not this feature's, not a defect, and not read as one.

---

## Defects

**None blocking.** No file/line defect was found in the implementation.

### Advisories

**A1 — the guard discriminates on `DATA_TYPE = 'int'` only** (`tests/Orders.IntegrationTests/NoMoneyColumnIsIntTests.cs:59`, `tests/Billing.IntegrationTests/NoMoneyColumnIsIntTests.cs:57`).
*Why it matters:* the feature's principle is "a storage type narrower than the domain type is a defect", and `smallint`, `tinyint`, `decimal`, `numeric`, `money`, `smallmoney`, `float` and `real` are all narrower or lossier than `long` minor units — `money` and `float` especially, since they are the two a well-meaning contributor is most likely to reach for. As shipped, a new money column declared in one of those types would need `SchemaColumnTypeTests`' closure test to catch it (which it would, as a new column), so the aperture is narrow. But the one-word fix closes it completely and costs nothing: widen the predicate to `DATA_TYPE IN ('int','smallint','tinyint','decimal','numeric','money','smallmoney','float','real')`. My own sweep shows **zero** existing columns in any of those types across all four databases, so the allow-lists would not need a single new entry today. *Not blocking:* the acceptance criterion is literally "no money column is `int`", which is met and armed.

**A2 — no equivalent guard in `Fulfillment.IntegrationTests` / `Notifications.IntegrationTests`.**
*Why it matters:* it is correct today — I confirmed from the live schema that neither database has a money column of any type — so the guard would be vacuous, and a vacuous test is worse than none. But `otc_fulfillment` is the database most likely to gain one (a despatch value, a carriage charge), and it would arrive with no guard watching. Worth adding when and if that happens, alongside the column, rather than as speculative coverage now. Recorded so the next person to touch that schema knows the asymmetry is deliberate.

**A3 — the implementer armed one guard of two, and inferred the other** (`progress/impl_money_column_width.md:171-173`: *"Billing follows the same shape and was not separately armed — the two tests are structurally identical"*).
*Why it matters:* the arming protocol exists precisely because a guard that *looks* correct can fail to fire, and "structurally identical" is an eyeball judgement of the kind the protocol replaces. The two files are not in fact identical — different connection fixtures, different allow-lists, different failure text — and the Billing allow-list is where a copy-paste slip would land. I armed Billing myself and it fired correctly, so **no defect resulted**; the practice should still change. *Rule, for the next implementer:* arm every guard you ship, not one representative of a family. The marginal cost is one rebuild.

---

## Effort

Wall-clock estimated from file timestamps, not a stopwatch — recorded as an estimate.

- **Total ~0.7 h** (09:17 → 10:00, 2026-09-02)
- Human gate + `CLAUDE.md` Money-row amendment: 09:17:47
- Implementation: 09:20:00 (first entity file) → 09:37:51 (`impl_money_column_width.md`) ≈ **0.33 h** — entities 09:20, snapshots 09:21–09:22, seed writers 09:25, schema-test corrections 09:25, new guards 09:27, report 09:37
- Review: 09:38 → 10:00 ≈ **0.35 h** — five arming probes across two projects with four forced full rebuilds, four `has-pending-model-changes` runs, a two-pass migration apply against two scratch databases, an independent numeric-type sweep of all four live databases, and one full `quality.sh`
- **1 session, APPROVED on the first pass**

The review took slightly longer than the implementation. That is the expected ratio for a feature whose entire deliverable is a guard: the change itself is thirteen type edits, and everything of value is in proving the guard fires.

---

## For the human — how to test this manually

```bash
# 1. every money column is bigint, everywhere, and nothing narrow is hiding
docker exec otcnet-mssql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
  -Q "SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE FROM otc_orders.INFORMATION_SCHEMA.COLUMNS
      WHERE DATA_TYPE IN ('int','smallint','decimal','money','float') ORDER BY 1,2;"

# 2. the guards fire — break one column and watch them fail by name
#    edit src/Orders/.../Migrations/20260901100855_InitialCreate.cs, products.price -> int
dotnet build OrderToCash.sln --no-incremental
dotnet test tests/Orders.IntegrationTests --filter NoMoneyColumnIsIntTests   # expect FAIL, then restore

# 3. no model drift, and the migrations still apply from empty
export $(grep -E '^MSSQL_' .env | xargs -d '\n')
for p in Orders Billing Fulfillment Notifications; do dotnet ef migrations has-pending-model-changes --project src/$p/$p.csproj; done

# 4. the whole suite
./quality.sh    # exit 0, 166/166, ~1m40s on an idle host
```

---

## The finding worth carrying to #9

This is the **second** time this build has mistaken a detail of #7's *implementation* for a requirement of the *shared specification*. The first was JSON payload key ordering in Phase 5 — which turned out to be MySQL's `json` column normalisation leaking onto #7's wire through its outbox relay's read-back, not a serializer decision and not a spec requirement. The second is this: `int` money columns, justified in the plan as "spec parity", where `specs/shared/requirements.md` R1 says only *"an integer count of minor units"* and `domain-model.md` M1 says only *"integer minor units only"*. Neither specifies a width. `int` was MySQL's, and #7 pays nothing for it because JavaScript numbers are doubles.

Both errors have the same shape and the same cost signature: a property of the reference implementation's **storage engine** was read as a property of the **specification**, and the cost landed on the port. In Phase 5 it nearly forced #8 to emulate another engine's key ordering forever. Here it forced a `long → int` narrowing at every money boundary — thirteen of them — a truncation risk #8 invented for itself in order to copy something the spec never asked for. Neither was caught by reading the spec, because the spec is silent in both cases; both were caught by asking *why* the reference does it that way.

**The transferable instruction for #9:** when the reference implementation exhibits a detail the specification does not mention, the default assumption is that it is an artifact of #7's stack, not a requirement — and the burden of proof runs the other way round. Grep `specs/shared/` for the constraint before writing "spec parity" in a plan. Silence in the spec is not a licence to copy; it is a signal to decide on the target stack's own terms. And note the direction the correction ran: the code changed, `specs/shared/` did not. That is what "the shared spec is read-only" means in practice.
