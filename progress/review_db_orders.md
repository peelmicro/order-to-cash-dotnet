# review_db_orders — feature 9 (id 9, phase 6), assessment #8

**Verdict: REJECTED**

Two blocking defects, both divergences from the authoritative table definitions in `Order To Cash - Databases.EN.md` §4, both undisclosed in `progress/impl_db_orders.md`, and neither guarded by any test. Everything else in this feature is good — genuinely good: the tests interrogate the real database rather than EF's model, the arming table reproduced independently, the eleven tables and all seventeen named indexes are correct down to column order, and every gate is green. The rejection is narrow and the fix is small.

---

## What I ran, and what I did not re-run

| Ran | Result |
|---|---|
| **Live-database interrogation** of `otcnet-mssql` / `otc_orders` — `INFORMATION_SCHEMA.COLUMNS`, `sys.indexes` + `sys.index_columns`, `sys.foreign_keys`, `COLUMNPROPERTY(...,'IsIdentity')` | Table below. This is the primary evidence; I did not take the report's mapping table on trust |
| **`tests/Architecture.Tests`** (NetArchTest) | 12/12 passed, 335 ms — run because C3's first box requires running it, not eyeballing it |
| **`tests/Orders.IntegrationTests`** full | 10/10 passed |
| **My own arming probe** (index removal, see below) | Named test failed with the expected message; restored, re-confirmed green |
| **`./init.sh`** | exit 0 |
| **`./quality.sh`** | green end to end, **1 m 05 s wall-clock** |
| **`git status --porcelain`** before and after my probe | Identical; my probe left no residue |

**Not re-run, and why:** I did not re-run `tests/SharedKernel.UnitTests` or `tests/Contracts.UnitTests` in isolation — no claim under review concerns them, and `./quality.sh` covered them in the same pass (32 and 21 passing). I did not re-run `dotnet ef database update` against the compose container; the migration's effect on that container is already observable and I queried it directly, which is the stronger evidence. I did not re-generate the migration from the model to check for drift.

**Timing note for later phases (C4-adjacent, worth recording):** `./quality.sh` went from a few seconds to **65 s** with this feature, because it now starts a real MS-SQL container. It also now **requires a running Docker daemon** to pass at all. With `db_fulfillment` and `db_billing` landing next on the same pattern, budget for this: either the three integration suites share one container collection, or `quality.sh` grows a fast/full split. Left as an observation, not a defect.

---

## CHECKPOINTS.md — boxes walked

### C1 — The harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (plus `suite_runner`).
- [x] Every agent definition declares its model — three inherit deliberately and say so in `description:`.
- [x] `./init.sh` exits 0.

### C2 — State is coherent
- [x] At most one feature `in_progress` — zero `in_progress`, one `in_review` (feature 9).
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it (8 done; the suites they own are green in the `quality.sh` run above).
- [ ] **`progress/current.md` describes the active session** — it does not. It still reads *"Feature: none — Phase 5 closed, awaiting Phase 6 / Status: idle"* while feature 9 was worked and submitted. This is the leader's file, not the implementer's, so it is **not** a cause of the rejection — but it is worth naming: #7's own `db_orders` review recorded exactly this, at exactly this feature, as *"D2 lesson, third occurrence"*. The reuse carried the schema across; it did not carry the habit.
- [x] Every `blocked` feature records why — none blocked.

### C3 — Architecture is respected
- [x] No EF Core / Kafka / NATS / MongoDB / AspNetCore reference inside any `Domain/` folder — **verified by running** `tests/Architecture.Tests`, 12/12. The Orders assembly is in the suite's scope (`Architecture.Tests.csproj` references all seven services), so the new `Microsoft.EntityFrameworkCore.SqlServer` reference in `Orders.csproj` is genuinely under the rule. It passes because Orders has no `Domain/` namespace yet, which is honest and is disclosed in the implementer's report.
- [x] No cross-service database access; no FK crosses a service boundary. Live check: `otc_orders` has exactly one foreign key and it is internal (`order_items.order_id → orders`). See D1 — the problem here is too few FKs, not misplaced ones.
- [x] No shared runtime code beyond `src/SharedKernel` and `src/Contracts`. `Orders.csproj` references only those two projects.
- [x] `src/SharedKernel` still has zero `PackageReference`.
- [x] No `decimal` in domain arithmetic — no domain code in this feature; every money column is `int` minor units on the wire to the database.
- [x] Every inter-service interaction classifiable as Kafka-fact or NATS-RPC — no interactions in this feature.
- [x] No stray debug logging, no context-free TODOs in the feature's files.

### C4 — Verification is real
- [x] `./quality.sh` passes (format + build + test + coverage), 65 s.
- [x] Domain tests are pure — none in this feature; the existing pure suites are untouched.
- [x] Integration tests use Testcontainers for .NET against real MsSql — `MsSqlContainerFixture.cs:23-24` pins `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04`, the same tag as `docker-compose.infra.yml`. Never a mock, never SQLite-in-memory.
- [~] Coverage thresholds — **not enforced, and that is pre-existing and disclosed.** `quality.sh:80` carries an explicit `TODO(feature 34 — sonarqube_quality_gates, phase 21)` parking enforcement. Coverage is reported as INFO only (75.3% / 91.3% / 95.8% / 0.0%; the 0.0% report is a `RegexGenerator.g.cs` artifact under `SharedKernel/obj/`, not real code). Not a defect of this feature — but note that #7 found its gate had been inert for twenty phases, and this one is inert by design until phase 21. Someone must actually promote it there.
- [x] No Jest anywhere. xUnit throughout.

### C5 — The session closed cleanly
- [x] No suspicious untracked files. `git status` shows exactly the expected set (see Scope below).
- [ ] `progress/history.md` has an entry for the feature — **not yet, correctly**: the feature is rejected, so no entry is written and no effort record is closed.
- [x] `feature_list.json` reflects true state — set back to `in_progress` by this review.
- [ ] The human has been told what was done and how to test manually — the leader's step, after the fix pass.
- [x] Claude did not commit. Nothing was committed or pushed by this review.

### C6 — Spec-Driven Development
**N/A.** Feature 9 is `"sdd": false`. `init.sh` confirms 0 sdd features past pending.

### C7 — Spec-reuse fidelity
Only the boxes this feature can touch: `specs/shared/` was **not modified** by this feature (`git status` clean on `specs/`), and no `R<n>` is claimed (`sdd: false`; `specs/shared/test-matrix.md` untouched). The remaining C7 boxes belong to later phases.

---

## Live-database findings (my own queries, not the report's table)

Connected to `otcnet-mssql`, database `otc_orders`, the database the migration was actually applied to.

| Check | Expected (Databases doc §4) | Live database | Verdict |
|---|---|---|---|
| Tables present | 11 (`currencies`, `products`, `retailers`, `companies`, `orders`, `order_items`, `order_number_sequences`, `outbox`, `processed_events`, `saga_commands`, `saga_ignored_facts`) | all 11 + `__EFMigrationsHistory` | ✅ |
| `outbox.seq` | `bigint` unsigned autoincrement unique | `bigint`, `IsIdentity = 1`, `IX_outbox_seq` unique | ✅ |
| `outbox.occurred_at` | `datetime(3)` | `datetime2(3)`, NOT NULL | ✅ |
| `outbox.published_at` | nullable | `datetime2(3)`, NULL allowed | ✅ |
| `outbox.payload` | `json` | `nvarchar(max)`, NOT NULL | ✅ (MS-SQL 2022 has no `json` type; correct translation) |
| `outbox.causation_id` | `char(36)` | `uniqueidentifier` NOT NULL | ✅ — and note this is a **reuse dividend**: #7 shipped `db_orders` *without* `causation_id` and had to add it in migration `0002` at feature 14 |
| All ids | `char(36)` UUID | `uniqueidentifier` everywhere except `order_number_sequences.id` | ✅ |
| Money columns | `int` minor units | `initial_amount`, `initial_discount`, `total_amount`, `price`, `discount`, `quantity` all `int` NOT NULL | ✅ |
| All timestamps | `datetime` / `datetime(3)` | every one `datetime2(3)`; nullability exactly as §4 (`disabled_at`, `published_at`, `cancellation_reason`, `notes`, `last_error`, `next_attempt_at`, `sent_at`, `saga_ignored_facts.order_id`/`observed_status`/`expected_status` nullable; everything else NOT NULL) | ✅ |
| `saga_commands` defaults | `status` default `pending`, `attempts` default 0 | `def=(N'pending')`, `def=((0))` | ✅ |
| Varchar widths | §4's exact widths | every one matches (3/3/5, 30/13/100/255, 20/100/2/15/13, 20/20/100, 255, 60, 50, 20/30/10, 60/20/20/20, 64) | ✅ |
| **`order_number_sequences.next_value`** | **`int`** (§4.2 verbatim: *"single-row counter (`id = 1`, `next_value int`)"*; #7's `order-number-sequences.schema.ts:20` is `int`) | **`bigint`** | ❌ **D2** |
| Index `outbox (published_at, seq)` | that order | `IX_outbox_published_at_seq`, cols `published_at, seq`, non-unique | ✅ column order correct |
| Index `outbox (published_at, occurred_at)` | — | `IX_outbox_published_at_occurred_at`, correct order | ✅ |
| Index `outbox (event_id)` unique | — | `IX_outbox_event_id`, unique | ✅ |
| Index `processed_events (event_id, consumer)` unique | — | `IX_processed_events_event_id_consumer`, unique, correct order | ✅ |
| Index `saga_commands (order_id, command)` unique | — | unique, correct order | ✅ |
| Index `saga_commands (status, created_at)` / `(status, next_attempt_at)` | — | both present, correct order | ✅ |
| Index `saga_ignored_facts (correlation_id)` | — | present, non-unique | ✅ |
| Index `orders (retailer_id, status)` / `(status, order_date)` | — | both present, correct order | ✅ |
| Unique `currencies.code`, `products.code`, `products.ean`, `retailers.code`, `companies.code`, `orders.order_reference` | — | all present and unique | ✅ |
| **Foreign keys** | **8** per §4.1/§4.2 and §3, matching #7's `0000_bizarre_champions.sql:114-121` | **1** (`FK_order_items_orders_order_id`, cascade) | ❌ **D1** |

Every index the Databases document names exists, with the correct columns **in the correct order** and the correct uniqueness. That was the sharpest thing I was asked to check and it is clean — `(published_at, seq)` is genuinely `(published_at, seq)`, not `(seq, published_at)`.

---

## Test quality — the "guard that does not guard" check

This was the other thing I was asked to be hardest on, and the implementer got it right.

- `SchemaColumnTypeTests.cs:168-173` issues `SELECT ... FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_CATALOG = DB_NAME()` over a **raw `SqlConnection`** and compares against a hand-written expectation table. It does **not** round-trip through EF's model metadata. Asking EF what EF thinks would have been the reject-level shape; this is not that.
- `IndexTests.cs:63-72` reads `sys.indexes` / `sys.index_columns` / `sys.columns` ordered by `ic.key_ordinal`, and `IndexTests.cs:104-105` matches with `SequenceEqual` — so **column order is genuinely asserted**, and `(seq, published_at)` would not satisfy `(published_at, seq)`.
- `OutboxSeqIdentityTests.cs:28` uses `COLUMNPROPERTY(OBJECT_ID('dbo.outbox'), 'seq', 'IsIdentity')`, and the sibling test inserts two real rows and asserts `second.Seq > first.Seq > 0`. Both halves — the declaration and the behaviour.
- `UniqueConstraintTests.cs` inserts genuinely conflicting rows and asserts `DbUpdateException`, and adds **two control cases** proving the constraint is on the pair rather than the first column alone. That control pair is what stops the rejection test passing for the wrong reason, and it is more than the brief asked for.
- `MigrationTests.Migration_ReApplies_Cleanly_From_Empty_When_Run_Twice` (`MigrationTests.cs:47-48`) **drops the whole database** and recreates it between the two `MigrateAsync()` calls, rather than truncating tables. That is the real "from empty" claim, not a weaker one wearing its name.

**The one structural gap:** `SchemaColumnTypeTests` is a **whitelist**. It proves every expected column exists with the right type, but never asserts that no *unexpected* column or table exists. #7's equivalent carried an "exact 9-table assert". An accidental extra column or a stray table would pass here silently. Advisory (D4), not blocking.

---

## Arming — my own probe

Protocol followed exactly as `CLAUDE.md` requires: backup copy outside git → introduce violation → **forced rebuild** (`--no-incremental`) → run named test → restore from backup → `touch` + forced rebuild → re-run.

| Violation | File | Test | Result |
|---|---|---|---|
| Deleted the `CreateIndex("IX_orders_retailer_id_status", ...)` block (lines 255-259) | `src/Orders/Infrastructure/Persistence/Migrations/20260901094309_InitialCreate.cs` | `IndexTests.Every_Spec_Index_Exists_With_The_Expected_Columns_And_Uniqueness` | **FAIL** — `orders(retailer_id,status): no index found on these columns in this order` (at `IndexTests.cs:119`) |
| Restored (md5 verified identical to backup), forced rebuild | same | full `Orders.IntegrationTests` | **10/10 passed** |

This independently reproduces the implementer's row 1 verbatim, including the failure message, and confirms the forced-rebuild protocol was genuinely used rather than described. `git status --porcelain` after restore is byte-identical to before.

**What could not be armed, and that is the point of D1:** there is no test that asserts foreign keys, so there is nothing to arm. Deleting a foreign key from the migration leaves the suite green. Seven of them are already deleted, and the suite is green.

---

## Defects

### D1 — BLOCKING. Seven of the eight foreign keys named in Databases doc §4 are absent

**Files:**
- `src/Orders/Infrastructure/Persistence/Configurations/ProductConfiguration.cs:26` — `currency_id` mapped as a bare `Guid`, no `HasOne`/`HasForeignKey`
- `src/Orders/Infrastructure/Persistence/Configurations/RetailerConfiguration.cs:24` — same
- `src/Orders/Infrastructure/Persistence/Configurations/CompanyConfiguration.cs:24` — same
- `src/Orders/Infrastructure/Persistence/Configurations/OrderConfiguration.cs:27-29` — `company_id`, `retailer_id`, `currency_id`, all three bare
- `src/Orders/Infrastructure/Persistence/Configurations/OrderItemConfiguration.cs:18` — `product_id` bare
- `src/Orders/Infrastructure/Persistence/Migrations/20260901094309_InitialCreate.cs:224` — the **only** `table.ForeignKey` in the entire migration

**Evidence:** `SELECT ... FROM sys.foreign_keys` on the live `otc_orders` returns exactly one row: `FK_order_items_orders_order_id | order_items.order_id -> orders | del=CASCADE`. #7's committed `apps/orders/drizzle/0000_bizarre_champions.sql:114-121` emits eight: `products.currency_id`, `retailers.currency_id`, `companies.currency_id`, `orders.company_id`, `orders.retailer_id`, `orders.currency_id`, `order_items.order_id` (cascade), `order_items.product_id`.

**Why it matters, in four ways:**

1. **It contradicts the authoritative definition.** §4.1 and §4.2 annotate each of these columns explicitly — `currency_id | char(36) FK → currencies`, `company_id | char(36) FK → companies`, `retailer_id | char(36) FK → retailers`, `product_id | char(36) FK → products` — and §3 states the rule directly: *"Foreign keys are used freely inside one database (e.g. `order_items.order_id → orders.id`) and never across databases."* The task framing was "#8 translates types; it does not redesign tables." Dropping seven constraints is a redesign.
2. **It is a real behavioural difference, not a cosmetic one.** In #7 an `orders` row referencing a non-existent retailer is rejected by the engine. Here it is accepted. The `seed` feature, `orders_aggregate` (phase 8) and every future repository lose a guarantee they can lean on in #7, and the failure surfaces as corrupt data rather than an insert error.
3. **It is undisclosed.** `progress/impl_db_orders.md` claims the model covers "every table in Databases doc §4", and its per-table mapping records `currency_id uniqueidentifier` with no mention that the FK is gone. The "What could not be done, and why" section names the *FK-generated index* on `order_items.order_id` but never the seven missing constraints. An undisclosed divergence from the reused spec is the exact failure mode `CLAUDE.md`'s opening rule exists to prevent, and it is worse here than a disclosed one would be.
4. **Nothing guards it.** There is no FK assertion anywhere in `tests/Orders.IntegrationTests/`. The seven missing constraints produce a fully green suite. This is the guard-that-does-not-guard shape one level up: not a dead test, but a live requirement with no test at all.

### D2 — BLOCKING. `order_number_sequences.next_value` is `bigint`; the spec says `int`

**Files:**
- `src/Orders/Infrastructure/Persistence/Entities/OrderNumberSequence.cs` — `public long NextValue`
- `src/Orders/Infrastructure/Persistence/Configurations/OrderNumberSequenceConfiguration.cs:21` — no explicit type, so EF maps `long` → `bigint`
- `tests/Orders.IntegrationTests/SchemaColumnTypeTests.cs:103` — `new("order_number_sequences", "next_value", "bigint")`

**Evidence:** live database reports `order_number_sequences.next_value | bigint | null=NO`. Databases doc §4.2 says verbatim *"single-row counter (`id = 1`, `next_value int`)"*; #7's `apps/orders/src/infrastructure/persistence/schema/order-number-sequences.schema.ts:20` is `int('next_value').notNull()`.

**Why it matters:** on its own a widened counter is harmless — but this is the reuse run, and the deviation is (a) undisclosed, (b) **locked in by a test that asserts the wrong value**, and (c) about to be copied twice, into `despatch_number_sequences` and `invoice_number_sequences`, by features 10 and 11 which are explicitly told to follow this feature's pattern. Three tables silently disagreeing with the spec is a harder thing to unwind than one. The implementer's report has a whole "What surprised me" section about `order_number_sequences.id` not being a UUID — the thinking was clearly done on this table, and this one column slipped through it unremarked.

Note the neighbouring column is *fine*: `order_number_sequences.id` is `int` where #7 used `tinyint`. §4 gives no type for `id`, only the literal `1`, so `int` is a legitimate translation, and it **is** disclosed and reasoned about in the report. That is the standard D2 needed to meet.

### D3 — ADVISORY (forward risk, do not fix in this feature). `orders.request_id` will be needed later

#7's `orders` table also carries `request_id char(36)` nullable plus `uq_orders_request_id` — added late, in `apps/orders/drizzle/0005_sticky_goblin_queen.sql:5`, by the observability/reliability work (RI1/R62 client idempotency, where MySQL's "UNIQUE admits many NULLs" behaviour is load-bearing). The Databases doc §4.2 does not list it, so it is correctly **out of this feature's contract** and its absence is not a defect. Flagging it now because MS-SQL's unique index does **not** admit multiple NULLs the way MySQL's does — a filtered index (`WHERE request_id IS NOT NULL`) or a unique constraint variant will be required. Better discovered here than at phase 21.

### D4 — ADVISORY. The column test is a whitelist with no closure assertion

`tests/Orders.IntegrationTests/SchemaColumnTypeTests.cs:191-218` iterates only over `_expected`. Nothing asserts the table count is 11 or that no unexpected column exists. #7's equivalent had an "exact 9-table assert". Consider adding an exact-set assertion in the fix pass while the file is open — cheap now, and it is the assertion that catches an accidental EF-inferred shadow column.

### D5 — ADVISORY. `progress/current.md` is stale

Reads "Feature: none — Phase 5 closed, awaiting Phase 6 / Status: idle" while feature 9 was in flight and submitted. C2's fourth box. Leader's file. #7 logged this same miss on this same feature as its third occurrence.

### D6 — ADVISORY. A credential default is duplicated in source

`src/Orders/Infrastructure/Persistence/OrdersDbContextFactory.cs:28` hardcodes `"Otc_App_Dev_Password_123!"` as the fallback. It is the committed dev value from `.env.example:42`, so nothing is leaked — but it is now stated in two places and will drift. Prefer failing loudly when `MSSQL_APP_PASSWORD` is unset, since this factory is design-time tooling only.

### Not a defect, recorded to prevent re-litigation

- **`infra/mssql/init/01-create-databases.sql` shows as modified.** That is the pre-existing RCSI change from the phase-5/infra session, uncommitted before this feature began, exactly as the implementer states. Not this feature's work and not counted against its scope.
- **`payload` as `nvarchar(max)`** rather than a JSON type: correct for MS-SQL 2022, consistent with the `CLAUDE.md` wire-shape ruling recorded at feature 8.
- **Domain purity passing vacuously** (Orders has no `Domain/` namespace yet): correct for a schema-only feature, and disclosed.

---

## Scope

`git status --porcelain` matches the expected envelope exactly:

```
 M Directory.Packages.props        (+ Microsoft.EntityFrameworkCore.Design 10.0.11, same band)
 M OrderToCash.sln                 (+ Orders.IntegrationTests project)
 M feature_list.json               (status only)
 M infra/mssql/init/01-...sql      (pre-existing, not this feature)
 D src/Orders/Infrastructure/README_PLACEHOLDER.cs
 M src/Orders/Orders.csproj
?? progress/impl_db_orders.md
?? src/Orders/Infrastructure/Persistence/
?? tests/Orders.IntegrationTests/
```

No file outside `src/Orders/**`, `tests/Orders.IntegrationTests/**` and the four expected root/progress files was touched. `specs/`, `src/SharedKernel/`, `src/Contracts/`, `tests/Architecture.Tests/`, the other five services and `CLAUDE.md` are all untouched. ✅

---

## Acceptance-item mapping I verified

`sdd: false`, so no `R<n>`. The contract is feature 9's three-item `acceptance` array plus the Databases doc's table shapes.

| Acceptance item | Named test(s) | Verified? |
|---|---|---|
| 1. "migration applies against the composed mssql container and re-applies cleanly from empty" | `MigrationTests.Migration_Applies_Against_An_Empty_Database`, `MigrationTests.Migration_ReApplies_Cleanly_From_Empty_When_Run_Twice` | ✅ — and the second genuinely drops the database (`MsSqlContainerFixture.DropDatabaseAsync`), it does not merely truncate. Independently corroborated: the schema exists on the real `otcnet-mssql` container |
| 2. "MS-SQL types per the spec: uniqueidentifier, datetime2(3), nvarchar, bigint IDENTITY for outbox.seq" | `SchemaColumnTypeTests.Every_Table_Has_The_Expected_Columns_And_SqlTypes`, `OutboxSeqIdentityTests.Outbox_Seq_Is_An_Identity_Column`, `OutboxSeqIdentityTests.Outbox_Seq_Really_Increments_Across_Inserted_Rows` | ✅ for the acceptance item as written; the tests read `INFORMATION_SCHEMA` and `COLUMNPROPERTY`, not EF metadata. ❌ against the spec on `next_value` (D2) |
| 3. "indexes match the shared spec" | `IndexTests.Every_Spec_Index_Exists_With_The_Expected_Columns_And_Uniqueness` | ✅ — all 17, correct columns, correct order, correct uniqueness, confirmed independently against the live database |
| (task-level) "unique constraints genuinely reject duplicates" | `UniqueConstraintTests` ×4 (2 rejections + 2 pair-controls) | ✅ — real inserts, real `DbUpdateException` |
| (spec-level) foreign keys per §4.1/§4.2 | **none** | ❌ D1 |

---

## What must change before re-review

1. **Add the seven missing foreign keys** so `otc_orders` carries all eight from Databases doc §4 / #7's `0000_bizarre_champions.sql:114-121`: `products.currency_id → currencies.id`, `retailers.currency_id → currencies.id`, `companies.currency_id → currencies.id`, `orders.company_id → companies.id`, `orders.retailer_id → retailers.id`, `orders.currency_id → currencies.id`, `order_items.product_id → products.id`. All `ON DELETE NO ACTION` — only `order_items.order_id` cascades, and it already does correctly. In EF this is `HasOne(...).WithMany().HasForeignKey(...).OnDelete(DeleteBehavior.Restrict)`; do **not** let EF invent navigation properties on the POCOs beyond what is needed.
2. **Regenerate the migration** (do not hand-edit it), and re-apply to the running `otcnet-mssql` so the live database matches. Since the existing migration has been applied there, either drop and recreate `otc_orders` or add a second migration — either is acceptable, say which in the report.
3. **Add an FK test to `tests/Orders.IntegrationTests/`** that reads `sys.foreign_keys` / `sys.foreign_key_columns` and asserts the exact set of eight, each with its referenced table and its delete action. Then **arm it**: remove one FK from the migration, forced rebuild, record the verbatim failure, restore, forced rebuild, re-confirm green. A requirement with no test is what let D1 through.
4. **Change `next_value` to `int`** — `OrderNumberSequence.NextValue` to `int`, and `SchemaColumnTypeTests.cs:103` to expect `"int"`. If there is a real argument for `bigint`, it is a spec amendment: raise it at the human gate, do not decide it inside a feature.
5. **Update `progress/impl_db_orders.md`**: correct the mapping table, and add both changes to the arming table with verbatim failure messages under the forced-rebuild protocol.
6. Optional but recommended while the files are open: D4's exact-set assertion.

Items 3 and 4 are small. Item 1 is the substance and it is one focused pass. Nothing else in this feature needs to be revisited — the tests are well built, the arming was real, and the eleven tables and seventeen indexes are otherwise exactly right.

---

## For the eventual history entry (not written — the feature is not closed)

Recorded here so it is not reconstructed later. Two genuine reuse dividends are already visible in this feature and should be named when it closes:

- **#8 built the final schema in one pass.** #7's `db_orders` shipped **9** tables and had to add `saga_commands` and `saga_ignored_facts` later, at the saga features. #8 shipped all **11** immediately, because the Databases doc describes the end state.
- **#8 shipped `outbox` correct on the first attempt.** #7's `db_orders` review recorded two binding carry-forwards to feature 14: `outbox` lacked `causation_id`, and `occurred_at` was `DATETIME(0)` with no deterministic relay tiebreak. #8 has `causation_id uniqueidentifier NOT NULL`, `occurred_at datetime2(3)`, and `seq bigint IDENTITY` with the `(published_at, seq)` poll index — all present at `db_orders` time. That is #7's migration `0002` never needing to be written, and it is the clearest instance so far of the reuse paying in avoided rework rather than in typing speed.

The counter-observation for the same entry: the two blocking defects are both places where the fully-decided spec was **not** followed. When the shape is handed to you, the remaining risk moves entirely into fidelity — and fidelity is exactly what a green test suite does not measure unless someone writes the assertion.

**#7 baseline for comparison when this closes:** 1 session, ~1.5 h (implementation ~1 h, review ~0.5 h), APPROVED first pass, 5 integration tests, 9 tables.

---
---

# Re-review — fix pass, 2026-09-01

**Verdict: APPROVED**

D1, D2, D4 and D6 are all genuinely closed. D1 in particular is closed *correctly*: the constraints are present **and** guarded, and I armed the guard myself in two independent ways. The original verdict and all six defects above are left standing unedited as the record.

## What I re-ran, and what I did not

The coordinator established against the live database, before handing me the re-review, that `sys.foreign_keys` on `otc_orders` now returns the exact set of 8 matching #7 child-column for child-column, and that `next_value` is now `int`. **I did not re-prove either of those** — re-running an established live query is duplicated cost. I spent the budget on the two things nobody had established: whether the FK set is *guarded* rather than merely present, and whether the **migration regeneration** silently regressed anything I approved in round 1.

| Ran | Result |
|---|---|
| **Two independent arming probes on `ForeignKeyTests`** (configuration-level and migration-level) | Both killed — details below |
| **Arming probe on `No_Unexpected_Table_Or_Column_Exists`** (D4 closure) | Killed |
| **D6 loud-failure probe** — `dotnet ef migrations list` with `MSSQL_APP_PASSWORD` unset | Throws with an actionable message; verified myself, not taken from the report |
| **Live re-verification of all 17 spec indexes + `outbox.seq` identity** | No regression from the regenerated migration — this is the surface the coordinator's checks did not cover |
| `tests/Architecture.Tests` | 12/12 |
| `tests/Orders.IntegrationTests` (clean rebuild) | 12/12 |
| `./quality.sh` | green, 77 tests, **1 m 07 s** |
| `./init.sh` | exit 0 |
| `git status --porcelain --untracked-files=all` | within the permitted envelope |

**Not re-run:** the `sys.foreign_keys` set and the `next_value` type as facts about the live database (established by the coordinator); `SharedKernel.UnitTests` / `Contracts.UnitTests` in isolation (no claim under review touches them; `quality.sh` covered them, 32 and 21 green).

## 1. Is the FK set guarded, or merely present? — GUARDED

`tests/Orders.IntegrationTests/ForeignKeyTests.cs` is the right shape. It reads `sys.foreign_keys` joined to `sys.foreign_key_columns`, `sys.tables` and `sys.columns` over a raw `SqlConnection` (`ForeignKeyTests.cs:63-80`) — the live database, never EF's model. It asserts the referenced table, the referenced **column**, and the **delete rule** per FK (`:106-116`), which is what the coordinator specifically asked for.

I armed it twice, deliberately not repeating the implementer's own probe.

**Probe A — configuration level** (the probe the coordinator asked for). Removed the `HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict)` block from `OrderItemConfiguration.cs`, forced rebuild, ran the named test:

```
System.InvalidOperationException : An error was generated for warning
'Microsoft.EntityFrameworkCore.Migrations.PendingModelChangesWarning':
The model for context 'OrdersDbContext' has pending changes.
Add a new migration before updating the database.
   at ForeignKeyTests...() in ForeignKeyTests.cs:line 54
```

**FAIL, and better than I expected.** I had gone in expecting to *report a gap*: these tests build their schema by `MigrateAsync()`, so my prior was that a configuration edit would leave the migration — and therefore the created database — untouched, and the test would pass green over a model/migration drift. EF Core 10 raises `PendingModelChangesWarning` as an error, so **model-vs-migration drift is guarded for free**. Editing the configuration without regenerating the migration cannot produce a green suite. That is a real property of this stack worth recording, and it closes a hole I was preparing to open.

**Probe B — migration level, and deliberately subtler than deleting an FK.** Deleting a constraint trips the `Assert.Equal(8, actual.Count)` line and proves only that *something* is counted. Instead I changed `FK_order_items_orders_order_id`'s `onDelete` from `ReferentialAction.Cascade` to `Restrict` in the migration — the count stays at 8, the referenced table stays correct, and **only the delete-rule assertion can catch it**:

```
order_items.order_id: expected delete action CASCADE, got NO_ACTION
   at ForeignKeyTests...() in ForeignKeyTests.cs:line 130
```

**FAIL, with the descriptive message.** The delete-rule half of the assertion is live, not decorative. Editing only the migration leaves the model snapshot consistent, so this probe bypasses Probe A's drift check and exercises the `sys.foreign_keys` comparison itself — the two probes together prove both layers.

Both files restored from pre-probe copies (md5 verified identical), forced rebuild, suite re-confirmed green.

## 2. Does the FK test assert closure? — YES

`ForeignKeyTests.cs:119-127` collects any actual FK not in the expected set and fails with `unexpected foreign key(s) not in the spec: [...]`, and `:129` asserts `actual.Count == 8`. A ninth FK, or one pointing at the wrong table, fails. This is not an additive existence check.

## 3. D2 — the three now agree

- `src/Orders/Infrastructure/Persistence/Entities/OrderNumberSequence.cs` — `public int NextValue`, carrying an XML doc comment that quotes §4.2 verbatim and cites #7's `order-number-sequences.schema.ts:20`, so the *reason* lives on the type rather than only in a progress file. That is the right place for it.
- `OrderNumberSequenceConfiguration.cs:21` — no `HasColumnType` override; `int` → `int` is EF's default, consistent with every other plain `int` column here.
- `tests/Orders.IntegrationTests/SchemaColumnTypeTests.cs:103` — now expects `"int"`.
- Live database: `next_value` is `int`.

Entity, configuration, migration, database and test all agree. The specific failure mode of D2 — the guard asserting the wrong value and locking the divergence in — cannot recur silently.

## 4. D4 — closure verified by probe, not by reading

`SchemaColumnTypeTests.No_Unexpected_Table_Or_Column_Exists` asserts exactly 11 tables, that the actual table-name set equals the expected set, and per table that the column sets are equal in both directions. It also carries a non-vacuity guard on its own expectation table (`Assert.Equal(11, expectedColumnsByTable.Count)`) — the assertion that stops the test passing over an empty or truncated whitelist.

Armed it: injected a `shadow_probe` column into `order_number_sequences` in the migration, forced rebuild.

```
order_number_sequences: unexpected columns [shadow_probe]
   at SchemaColumnTypeTests.No_Unexpected_Table_Or_Column_Exists() in SchemaColumnTypeTests.cs:line 299
```

**FAIL** — and note the run reported `Failed: 1, Passed: 1`: the original whitelist test passed over the same shadow column while the new closure test caught it. That is D4 demonstrated as a real gap that is now genuinely closed, rather than asserted to be.

## 5. D6 — the failure is loud

`OrdersDbContextFactory.cs` no longer has a password fallback; it throws `InvalidOperationException`. I ran `dotnet ef migrations list` with the variable unset myself:

```
Unable to create a 'DbContext' of type 'OrdersDbContext'. The exception
'MSSQL_APP_PASSWORD is not set. Export the value from .env before running
'dotnet ef' against OrdersDbContext, e.g.: export $(grep -E
'^MSSQL_(APP_PASSWORD|APP_USER|DB_ORDERS|HOST_PORT)=' .env | xargs)'
was thrown while attempting to create an instance.
```

Loud, and actionable — it names the variable and gives the command. Not a silent empty string, and not a connection failure fifteen seconds later with an opaque login error. `grep -rn "Otc_App_Dev_Password" src/ tests/` returns nothing.

## 6. No regression from the migration regeneration

The migration was regenerated (`20260901094309` deleted, `20260901100855` created), so **every finding in my round-1 table came from a file that no longer exists**. The coordinator's checks covered FKs and `next_value` but not the rest, so I re-queried the live database:

| Re-checked after regeneration | Result |
|---|---|
| All 17 spec-named indexes | present, correct columns, **correct column order**, correct uniqueness — `(published_at, seq)` still `(published_at, seq)` |
| `outbox.seq` | `bigint`, `IsIdentity = 1`, `IX_outbox_seq` unique |
| Table count | 11 (+ `__EFMigrationsHistory`) |
| FK count | 8 |

Nothing regressed. The regeneration was clean.

## 7. Disclosure quality — now honest

This was the re-review question no test answers, and the corrected report meets the bar. The "Corrected divergence list" (report §"Corrected divergence list", 4 items) is complete against my own live queries: `order_number_sequences.id` as `int`, `payload` as `nvarchar(max)`, POCOs-not-aggregate, and the non-spec-named EF indexes. I looked for a fifth divergence and did not find one. Critically, the report **names its own earlier failure** — *"the previous report claimed completeness it did not have"* — rather than quietly correcting the table. That is the disclosure standard this repository needs, and it is the difference between D1 being an omission and D1 being a false claim.

Two small factual notes, neither blocking:

- **A1 (advisory, factual).** The divergence list says *"7 EF-generated single-column indexes on the FK columns just added"*. It is **6**: `IX_companies_currency_id`, `IX_products_currency_id`, `IX_retailers_currency_id`, `IX_orders_company_id`, `IX_orders_currency_id`, `IX_order_items_product_id`. There is no `IX_orders_retailer_id` because EF correctly recognised that the existing spec index `IX_orders_retailer_id_status` already leads with `retailer_id` and reused it. The report over-declares rather than under-declares, which is the safe direction, but the actual behaviour is more interesting than the claim.
- **A2 (advisory, ergonomics).** `ForeignKeyTests.cs:129-130` puts `Assert.Equal(8, actual.Count)` **before** `Assert.True(failures.Count == 0, ...)`. When an FK is missing, the count assertion fires first and reports `Expected: 8 / Actual: 7`, discarding the `failures` list that says *which* one. Swapping the two lines would surface the diagnostic. Cosmetic; the guard works either way.

## 8. Scope and regressions

`git status --porcelain --untracked-files=all` is within the permitted envelope: `src/Orders/**`, `tests/Orders.IntegrationTests/**`, `OrderToCash.sln`, `Directory.Packages.props`, `progress/impl_db_orders.md`, `feature_list.json`, plus `progress/current.md` (D5, the leader's fix) and `infra/mssql/init/01-create-databases.sql` (pre-existing RCSI work from the infra phase, unchanged by this feature). The superseded migration and its `.Designer.cs` are deleted, not left alongside. Architecture suite 12/12 — no EF type reached a `*.Domain` namespace; the seven `HasOne(...)` calls live entirely in `Infrastructure/Persistence/Configurations/`. `./init.sh` exit 0. `./quality.sh` green.

## A methodology note worth recording

Mid-probe, a `--no-build` run reported 4 failures with `System.MissingMethodException: Method not found: '...et_Default.get_ProcessedEvents()'`, and an immediately preceding run of the same command reported a *different* count. Both were false: a clean rebuild (`rm -rf bin obj`, then `dotnet test` building for itself) gives 12/12. The cause was my own probe cycle — alternating `dotnet build --no-incremental` with `dotnet test --no-build` left the test assembly and `OrderToCash.Orders.dll` out of step.

This is **D6 from #7's history** (*"a check is only worth what the artefact it ran against is worth"*) reappearing in a new disguise, and in the false-**red** direction this time. `CLAUDE.md`'s arming protocol mandates the forced rebuild after a restore; the gap it does not cover is that `--no-build` on the *confirming* run can consume the artefact the forced rebuild just invalidated. Worth folding into the protocol: **after restoring, let `dotnet test` do its own build — do not pair `--no-incremental` with `--no-build`.** The non-determinism between two consecutive identical commands is the tell.

## CHECKPOINTS re-walk (deltas from the original walk only)

- **C2** — [x] `progress/current.md` now reflects the session (was the one open box; the leader fixed it, D5).
- **C3** — [x] all boxes still hold; architecture suite re-run, 12/12. The eight FKs are all internal to `otc_orders`; none crosses a service boundary.
- **C4** — [x] `quality.sh` green; integration tests still Testcontainers against real MS-SQL; 77 tests. Coverage still reported-not-enforced (77.3% overall, up from 75.3%), which remains parked at feature 34 by `quality.sh:80` and is not this feature's defect.
- **C5** — [x] no suspicious untracked files; [x] `feature_list.json` set to `done` by this review; [x] `progress/history.md` entry with effort record appended; [x] Claude did not commit. The "human told what was done / how to test" box is the leader's next step.
- **C6** — N/A (`sdd: false`). **C7** — `specs/shared/` untouched by this feature; no `R<n>` claimed.

**All applicable boxes are marked. Feature 9 `db_orders` is APPROVED and set `done`.**
