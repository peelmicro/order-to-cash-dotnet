# review_db_billing — feature 11 (id 11, phase 6), assessment #8 — **closes Phase 6**

**Verdict: APPROVED**

Zero blocking defects. The distinctive deliverable — the cross-context reliability-table parity test — was attacked with three of my own mutation probes covering both failure modes the brief named, and it survived all three with precise, both-sides-named diagnostics. It is **not** too loose (it catches a length change and an index drop, not just a column name) and it is **not** silently narrow (`otc_notifications` is genuinely inside the `processed_events` half, in both the column comparison and the index comparison — proven by mutating Notifications and watching the test die). The two defects that got feature 9 rejected were avoided again: `invoice_number_sequences.next_value` is `int` everywhere, and the exact three-FK set is present and closed.

Five advisories, none a reason to hold the feature. The interesting result is again in the effort record, and it is **not** a win — see the Phase 6 closing section.

---

## What I ran, and what I did NOT re-run

The implementer had just run the full solution suite. Re-running it in full duplicates cost, so the budget went on independent evidence.

| Ran | Result |
|---|---|
| **Live interrogation of `otcnet-mssql` / `otc_billing` and `/ otc_notifications`** — `INFORMATION_SCHEMA.TABLES`, `INFORMATION_SCHEMA.COLUMNS`, `sys.foreign_keys` + `sys.foreign_key_columns`, `sys.indexes` + `sys.index_columns`, `COLUMNPROPERTY(...,'IsIdentity')` | §1 table. Primary evidence; the report's mapping table was not taken on trust |
| **Live 4-way schema diff of `outbox`/`processed_events`** across `otc_orders`, `otc_fulfillment`, `otc_billing`, `otc_notifications` — columns *and* indexes, dumped and `diff`ed | All five diffs empty. §3 |
| **Independent check against #7's committed DDL** — `apps/billing/drizzle/0000_brown_hammerhead.sql`, `0001_*`, `0002_*` in the `order-to-cash-nestjs` checkout | Every FK, every delete rule, every column width, every index confirmed. §2 |
| **Independent check against the Databases doc** §6, §7, §3, §4.3 | Every table, column, type and named index confirmed. §2 |
| **My own arming probe A** — `nvarchar(50)` → `nvarchar(80)` **and** an index drop, both in **Notifications** | Parity test killed on both counts, naming Notifications. §5 |
| **My own arming probe B** — dropped `IX_outbox_published_at_seq` from **Billing** | Parity test **and** `IndexTests` both killed. §5 |
| **My own arming probe C** — injected a rogue `outbox` table into **Notifications** | Both closure guards killed, in two different test projects. §5 |
| `tests/Billing.IntegrationTests` after restore | 22/22 |
| `tests/Notifications.IntegrationTests` after restore | 7/7 |
| `tests/Architecture.Tests` (NetArchTest) | 12/12, 457 ms — run because C3's first box requires running it, not eyeballing it |
| `./quality.sh` | exit 0, 125 tests, all 7 suites individually `Passed!`, **2 m 39 s**. §7 |
| `./init.sh` | exit 0 |
| `md5sum` on both migrations before and after my probes | Identical to the implementer's recorded backup hashes (`0e5acff44846bea8b6ff4a7ddc966e4b`, `4f2ca4bf9404c4b582c77be0f8a36702`) — which independently confirms *their* restore was genuine and not merely described |
| `git status --porcelain` before and after my probes | Identical |
| **File-mtime audit of `src/Orders/**` and `src/Fulfillment/**`** | Latest source mtime in either tree is `12:52:36` (feature 10's session). Feature 11's first artefact is `13:02:39`. **Neither tree was touched.** §8 |

**Not re-run, and why:**

- **`tests/SharedKernel.UnitTests` and `tests/Contracts.UnitTests` in isolation** — no claim under review touches them; `./quality.sh` covered them in the same pass (32 and 21 green).
- **`tests/Orders.IntegrationTests` and `tests/Fulfillment.IntegrationTests` in isolation** — features 9 and 10's territory, both closed. `quality.sh` covered them (12 and 19 green). I did query `otc_orders` and `otc_fulfillment` directly, but as the *reference* side of the parity check, not to re-audit closed features.
- **`dotnet ef database update` against the compose container** — the migrations' effect on `otcnet-mssql` is already observable and I queried it directly, which is stronger evidence than re-running the command.
- **`dotnet ef migrations has-pending-model-changes` as a standalone command** — the same property is enforced inside every test in both suites, because EF Core 10 raises `PendingModelChangesWarning` as an error at the `MigrateAsync()` step (established in feature 9's re-review, and the reason my probes deliberately mutated the *migration* files rather than the configurations).
- **The implementer's five arming probes** — I ran three *different* ones. Reproducing a probe verbatim tests the report; a different probe tests the guard. In particular I attacked the parity test at **length**, at **index-set** and at **Notifications inclusion**, where the implementer had attacked it only at length-in-Billing.

---

## CHECKPOINTS.md — boxes walked

### C1 — The harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (plus `suite_runner`) — `init.sh` reports 6.
- [x] Every agent definition declares its model — `init.sh` confirms the pins and the three documented deliberate inherits.
- [x] `./init.sh` exits 0.

### C2 — State is coherent
- [x] At most one feature `in_progress` — zero `in_progress`, one `in_review` (feature 11) at review time; `init.sh` confirms "no feature in_progress".
- [x] Every status is in `rules.valid_status` — `init.sh` confirms.
- [x] Every `done` feature has passing tests associated with it — 10 done, and `quality.sh` is green across all seven suites.
- [x] **`progress/current.md` describes the active session.** It names feature 11, its goal, its decisions and its blockers, and it was rewritten at `13:00:12` as feature 11 opened. **This box was open on the two previous features and is closed here.** One nit: it says `Status: in_progress` while `feature_list.json` said `in_review` — one transition stale, not a leftover from a previous session, so the box is genuinely met. **A5** below.
- [x] Every `blocked` feature records why — none blocked.

### C3 — Architecture is respected
- [x] No EF Core / Kafka / NATS / MongoDB / AspNetCore reference inside any `Domain/` folder — **verified by running** `tests/Architecture.Tests`, 12/12, 457 ms. `Billing.csproj` and `Notifications.csproj` now both carry `Microsoft.EntityFrameworkCore.SqlServer`, so both assemblies are genuinely under the rule; both pass because `src/Billing/Domain/` and `src/Notifications/Domain/` still hold only `README_PLACEHOLDER.cs`. Vacuous, honest, and disclosed (divergence 3).
- [x] No cross-service database access; no FK crosses a service boundary. Live check: `otc_billing` has exactly three foreign keys, all internal; `otc_notifications` has **zero** (`SELECT COUNT(*) FROM sys.foreign_keys` → 0). `company_code`, `retailer_code`, `product_code`, `order_reference` are plain `nvarchar` business identifiers with no FK anywhere, and their widths (20/20/30/20) are byte-identical to `otc_orders`'s and `otc_fulfillment`'s — which is what makes message-carried identifiers safe without FKs.
- [x] No shared runtime code beyond `src/SharedKernel` and `src/Contracts`. `Billing.csproj` and `Notifications.csproj` reference only those two. The three cross-service references in `Billing.IntegrationTests.csproj` are **test-only**, exist solely for the parity test, and are commented as such. See **A4** for the structural note.
- [x] `src/SharedKernel` still has zero `PackageReference` entries.
- [x] No `decimal` in domain arithmetic — `grep -rn "decimal\|double \|float "` over `src/Billing/`, `src/Notifications/` and both new test projects returns **nothing**. Every money and quantity column (`credit_limit`, `amount`, `discount`, `total_amount`, `price`, `units`) is `int` minor units, per Databases doc §3.
- [x] Every inter-service interaction classifiable as Kafka-fact or NATS-RPC — no interactions in this feature.
- [x] No stray debug logging, no context-free TODOs — `grep` for `TODO` and `Console.WriteLine` over the feature's files returns nothing.

### C4 — Verification is real
- [x] `./quality.sh` passes (format + build + test + coverage), exit 0, 125 tests, **2 m 39 s**. `dotnet format --verify-no-changes` clean; build 0 warnings, 0 errors.
- [x] Domain tests are pure — none in this feature; the existing pure suites are untouched and green (32 + 21).
- [x] Integration tests use Testcontainers for .NET against real MsSql — both new `MsSqlContainerFixture.cs` files pin `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04`, the same tag as `docker-compose.infra.yml` and the two existing fixtures. Never a mock, never SQLite-in-memory. Confirmed by watching containers come up during my own probe runs.
- [~] Coverage thresholds — **reported, not enforced; pre-existing and disclosed.** `quality.sh:80` still parks enforcement behind `TODO(feature 34 — sonarqube_quality_gates, phase 21)`. Unchanged by this feature and not its defect. See **A6** for a finding about the *shape* of what is currently reported, which feature 34 will have to fix before it can gate anything.
- [x] No Jest anywhere — `grep` for `jest` across the repository returns nothing. xUnit throughout.

### C5 — The session closed cleanly
- [x] No suspicious untracked files. 27 `git status` entries, every one inside `src/{Billing,Notifications}/Infrastructure/Persistence/`, the two new test projects, the pre-existing uncommitted feature-9/10 set, or the expected root/progress files. `TestResults/` is gitignored.
- [x] `progress/history.md` has an entry for the feature, **including its effort record** — appended by this review.
- [x] `feature_list.json` reflects true state — set `done` by this review.
- [ ] The human has been told what was done and how to test manually — the leader's step, next. **This also closes Phase 6, so the leader owes a phase report, not just a feature report.**
- [x] **Claude did not commit.** Nothing was committed or pushed by this review.

### C6 — Spec-Driven Development
**N/A.** Feature 11 is `"sdd": false`. `init.sh` confirms 0 sdd features past pending.

### C7 — Spec-reuse fidelity
Only the boxes this feature can touch:
- [x] **`specs/shared/` untouched** by this feature — `git status` is clean on `specs/`. No amendment, silent or otherwise.
- [x] **No `R<n>` claimed** (`sdd: false`); `specs/shared/test-matrix.md` untouched. R47/R48 are *named in a comment* on `IndexTests` and `UniqueConstraintTests` as the reason `payments.payment_reference` is unique — that is a forward pointer, not a coverage claim, and the matrix was correctly not ticked.
- [x] **The effort record is honest, including where it was not faster** — see the closing section. **This one was not faster**, and that is written up rather than smoothed over.

The remaining C7 boxes (n8n workflows, the black-box API script, the README benchmark section) belong to later phases.

---

## 1. Live-database findings — my own queries, not the report's table

Connected to `otcnet-mssql`, the databases the migrations were actually applied to.

### `otc_billing`

| Check | Expected (Databases doc §6 / §4.3 / §3, corroborated by #7's DDL) | Live database | Verdict |
|---|---|---|---|
| Tables present | 8 (`credits`, `credit_items`, `invoices`, `invoice_items`, `payments`, `invoice_number_sequences`, `outbox`, `processed_events`) | all 8 + `__EFMigrationsHistory`, nothing else | OK |
| **`invoice_number_sequences.next_value`** | **`int`** (§3 "single-row technical tables"; #7's `0002_*.sql:14` is `next_value int NOT NULL`) | **`int`, NOT NULL** | **OK — feature 9's D2 avoided, third and last sequence table** |
| `invoice_number_sequences.id` | §6 gives no type; #7 uses `tinyint` | `int`, NOT NULL | OK (disclosed, divergence 1) |
| **Foreign keys** | **exactly 3** per §6 and #7's `0000_brown_hammerhead.sql:94-96` | **exactly 3** — `credit_items.credit_id → credits.id` NO_ACTION; `invoice_items.invoice_id → invoices.id` **CASCADE**; `payments.invoice_id → invoices.id` NO_ACTION. All `ON UPDATE NO_ACTION` | **OK — feature 9's D1 avoided** |
| `credits` columns | 36/30/20/20/int/char(3)/datetime×2 | `uniqueidentifier`, `nvarchar(30)`, `nvarchar(20)`×2, `int`, `char(3)`, `datetime2(3)`×2, all NOT NULL | OK — widths match #7 exactly |
| `credit_items` columns | 36/36/20/int/20/datetime×3 | `uniqueidentifier`×2, `nvarchar(20)`, `int`, `nvarchar(20)`, `datetime2(3)`×3 | OK |
| `invoices` columns | 36/20/datetime/20/20/20/int×3/char(3)/20/datetime nullable/datetime×2 | 14 columns, exactly as specified; **`paid_at` is the only nullable business column** | OK |
| `invoice_items` columns | 36/36/30/int/int/datetime×2 | `uniqueidentifier`×2, `nvarchar(30)`, `int`×2, `datetime2(3)`×2 | OK |
| `payments` columns | 36/30/36/int/char(3)/datetime/20/datetime, **no `updated_at`** | 8 columns, **no `updated_at`** — §3's append-only-ledger rule honoured | OK |
| **`payments.payment_reference`** | **`varchar(30)` unique** — the remittance idempotency key, R47/R48 | **`nvarchar(30)` NOT NULL**, `IX_payments_payment_reference` **unique** | **OK** |
| `outbox.seq` | `bigint unsigned autoincrement unique` | `bigint`, `COLUMNPROPERTY(...,'IsIdentity') = 1`, `IX_outbox_seq` unique | OK |
| `outbox.published_at` / `trace_parent` | nullable | the only two nullable columns in `outbox` | OK |
| `outbox.payload` | `json` | `nvarchar(max)`, NOT NULL | OK (MS-SQL 2022 has no `json` type; disclosed, divergence 2) |
| Indexes, all 12 spec-named | §6 + §4.3 | all present with **correct column order** (`(retailer_code, company_code)`, `(credit_id, order_reference)`, `(status, invoice_date)`, `(published_at, seq)`, `(published_at, occurred_at)`, `(event_id, consumer)` all verified at `key_ordinal` level) and correct uniqueness | OK |
| Non-spec indexes | — | `IX_invoice_items_invoice_id`, `IX_payments_invoice_id` (EF FK-supporting) + `PK_*`. `credit_items`'s FK column reuses the spec composite. **14 non-PK indexes total = 12 spec + 2** | OK — the report's count is exactly right (disclosed, divergence 4) |

### `otc_notifications`

| Check | Expected (Databases doc §7) | Live database | Verdict |
|---|---|---|---|
| **Tables present** | **`processed_events` and nothing else** | **exactly `processed_events`** + `__EFMigrationsHistory`. **No `outbox`.** | **OK** |
| Foreign keys | none | `SELECT COUNT(*) FROM sys.foreign_keys` → **0** | OK |
| `processed_events` columns | §4.3, 5 columns | `uniqueidentifier`, `uniqueidentifier`, `nvarchar(50)`, `datetime2(3)`, `datetime2(3)` — all NOT NULL | OK |
| Unique index | `(event_id, consumer)` | `IX_processed_events_event_id_consumer`, unique, correct order | OK |

---

## 2. Independent corroboration — #7's committed DDL and the Databases doc

I did not take the report's citations on trust; I read both sources.

**#7's `apps/billing/drizzle/`:**

- `0000_brown_hammerhead.sql:94-96` — exactly three `ADD CONSTRAINT ... FOREIGN KEY`, with `ON DELETE no action`, `ON DELETE cascade`, `ON DELETE no action` respectively. The report's claim is verbatim correct, **including the delete rules it says it checked rather than assumed**. That specific claim — "checked, not assumed" — is cheap to write and hard to verify, so I verified it. It holds.
- `0002_invoice_sequences_and_order_uniqueness.sql:14` — `next_value` **int** NOT NULL, `id` tinyint. Both the type the spec requires and the `id` divergence the report discloses.
- Every column width in `0000_*.sql` matches the live `otc_billing` one for one: `credits` 30/20/20/char(3), `credit_items` 20/20, `invoices` 20/20/20/20/char(3)/20, `invoice_items` 30, `payments` 30/char(3)/20, `outbox` 60/64, `processed_events` 50.
- **What #7 needed three migrations for, #8 shipped in one.** #7's `0000` has 7 tables and an `outbox` with no `causation_id`, `seq` or `trace_parent`; `0001` adds those three plus `idx_outbox_unpublished_seq`; `0002` adds `invoice_number_sequences`, the `invoices.order_reference` unique (#7's own B7 advisory, opened at `db_billing` and closed at feature 21) and `idx_invoices_status_invoice_date`. #8's single `InitialCreate` contains **all** of it. Same one-pass dividend feature 10 recorded, and it is again invisible in wall-clock.

**Databases doc:** §6's four tables-with-columns, the two `invoice_items` / `invoice_number_sequences` prose paragraphs, §7's one-table rule and §4.3's two reliability tables all check out line for line against the live schema. §3's `int`-minor-units, no-`updated_at`-on-append-only-ledgers and single-row-counter conventions are all honoured. One point worth naming: **§3 promises parity across *three* databases** ("byte-identical definitions in `otc_orders`, `otc_fulfillment` and `otc_billing`"), and the feature's acceptance item says **four**. The implementation resolves this correctly and explicitly — `outbox` across three, `processed_events` across four — which is *stronger* than the document, not a deviation from it.

---

## 3. Live 4-way parity diff — checked independently of the test

The parity test is the deliverable, but a test that passes on a wrong schema is worth nothing, so I diffed the live databases directly.

```
outbox columns              otc_orders vs otc_fulfillment   IDENTICAL
outbox columns              otc_orders vs otc_billing       IDENTICAL
processed_events columns    otc_orders vs otc_notifications IDENTICAL
outbox+pe indexes           otc_orders vs otc_billing       IDENTICAL
processed_events indexes    otc_orders vs otc_notifications IDENTICAL
```

Dumps compared on column name, ordinal position, data type, character max length, datetime precision, nullability **and `IsIdentity`**; indexes on name, uniqueness, column name and `key_ordinal`. Every `diff` is empty. `outbox` is 12 columns in all three databases that have one; `processed_events` is 5 columns in all four.

Note that my dump includes `IsIdentity`, which the test's column comparison does **not** read. See **A2**.

---

## 4. Test quality — do the assertions read the database or the ORM?

Honoured without exception, in both new projects.

- `grep` for EF metadata access (`.Metadata`, `IDesignTimeModel`, `GetEntityTypes`, `Model.`) across both suites returns **nothing**. Not one assertion round-trips through EF's own model.
- `ReliabilityTableParityTests.cs:189-196` and `:221-229` — raw `SqlConnection`, `INFORMATION_SCHEMA.COLUMNS` and the `sys.indexes` / `sys.index_columns` / `sys.columns` catalogue, `ORDER BY ic.key_ordinal`, compared with `SequenceEqual`.
- `ForeignKeyTests.cs` — `sys.foreign_keys` joined to `sys.foreign_key_columns`, `sys.tables`, `sys.columns`; asserts referenced table, referenced **column** and **delete rule** per FK, closed-set on both sides.
- `SchemaColumnTypeTests` (both projects) — `INFORMATION_SCHEMA.COLUMNS WHERE TABLE_CATALOG = DB_NAME()`, plus a separate closure test.
- `OutboxSeqIdentityTests` — `COLUMNPROPERTY(OBJECT_ID('dbo.outbox'), 'seq', 'IsIdentity')` plus a real two-row insert asserting the increment.
- `MigrationTests.Migration_ReApplies_Cleanly_From_Empty_When_Run_Twice` (`MigrationTests.cs:44-47`) **drops the whole database** and recreates it between the two `MigrateAsync()` calls. That is the real "from empty" claim, in both projects.
- `UniqueConstraintTests` inserts genuinely conflicting rows and asserts `DbUpdateException`, with **two control cases** proving the `credits` and `processed_events` constraints are on the pair rather than the first column alone. `Payments_Rejects_A_Duplicate_PaymentReference` is a real conflicting insert on the R47/R48 idempotency key.
- **`ForeignKeyTests.cs:133-134` now puts `Assert.True(failures...)` before `Assert.Equal(3, actual.Count)`** — feature 10's advisory A2, which that review flagged as "a good file's warts copied along with its shape", is **closed here**. My probes did not need it, but the implementer's probe 1 message (`credit_items.credit_id: no foreign key found`) is measurably better than feature 10's equivalent (`Expected: 2 / Actual: 1`). A one-line fix, noticed while the file was open, and the only place this feature improved on the inherited pattern rather than copying it.

**The parity test specifically.** It compares, per column: name, **ordinal position**, data type, **character maximum length**, **datetime precision**, and nullability; and per index: name, **uniqueness**, and the **ordered** column list — with set-difference reporting in both directions on both column names and index names. Every divergence message names both sides (`Orders=60 vs Billing=40`), never a bare `Assert.Equal() Failure`. `Notifications` appears as a fourth entry in **both** `processed_events` dictionaries (`ReliabilityTableParityTests.cs:87` and `:94`) and is excluded from the two `outbox` dictionaries by construction, with the exclusion commented and independently backstopped by `Notifications_Database_Contains_Only_ProcessedEvents`.

---

## 5. My own arming — three probes, all different from the implementer's five

Protocol: backup copy outside git (`md5sum`-verified against the implementer's recorded hashes) → introduce violation → forced rebuild (`--no-incremental`) → run named test → restore → `md5sum` + content `grep` → forced rebuild → re-run. Migration files, not configurations, so `PendingModelChangesWarning` does not mask the probe.

| # | Violation | File | Test | Result (verbatim) |
|---|---|---|---|---|
| **A** | Two at once, **both in Notifications**: `consumer` `nvarchar(50)` → `nvarchar(80)`, **and** deleted the whole `CreateIndex("IX_processed_events_event_id_consumer", ...)` block. Chosen because it attacks *both* of the brief's failure modes at once — a **length** divergence (not just a name), and whether **Notifications is genuinely in the comparison** rather than quietly skipped | `Notifications/.../Migrations/20260901110547_InitialCreate.cs` | `ReliabilityTableParityTests.Outbox_And_ProcessedEvents_Are_Defined_Identically_Across_All_Four_DbContexts` | **FAIL, two lines, both naming Notifications:**<br>`processed_events.consumer: character_maximum_length Orders=50 vs Notifications=80`<br>`processed_events: index 'IX_processed_events_event_id_consumer' present in Orders but missing in Notifications` |
| **B** | Deleted `CreateIndex("IX_outbox_published_at_seq", ...)` from **Billing** — an index drop in the `outbox` half, which probe A did not touch | `Billing/.../Migrations/20260901110439_InitialCreate.cs` | parity test **and** `IndexTests` | **BOTH FAIL** — parity: `outbox: index 'IX_outbox_published_at_seq' present in Orders but missing in Billing`; `IndexTests`: `outbox(published_at,seq): no index found on these columns in this order` (`IndexTests.cs:118`) |
| **C** | Injected a whole rogue `outbox` table into **Notifications** — the closure probe: does anything actually stop `otc_notifications` growing a second table? | `Notifications/.../Migrations/20260901110547_InitialCreate.cs` | two tests in **two different projects** | **BOTH FAIL** — `Notifications.IntegrationTests.SchemaColumnTypeTests.No_Unexpected_Table_Or_Column_Exists`: `Expected: ["processed_events"] / Actual: ["outbox", "processed_events"]` (`:159`); `Billing.IntegrationTests.ReliabilityTableParityTests.Notifications_Database_Contains_Only_ProcessedEvents`: `Expected: ["processed_events"] / Actual: ["processed_events", "outbox"]` (`:179`). **The parity test's `outbox` half passed over it, exactly as designed** — which confirms the report's honest characterisation that the absence is proven by a dedicated closure test, not by omission from a dictionary |
| — | Restored both files (`md5sum` identical to backup, `grep -c` confirms the deleted blocks are back and the injected one is gone), forced rebuild | both | full `Billing.IntegrationTests` + `Notifications.IntegrationTests` | **22/22** and **7/7** |

**The conclusions the brief asked for, stated plainly:**

1. **The parity test is not too loose.** It catches a length change (`50` vs `80`) and an index drop, in addition to type and name. Types, lengths, nullability, datetime precision, ordinal position and the full index set with column order are all compared. It is a whole parity test, not half of one.
2. **The parity test is not silently narrow.** `otc_notifications` is genuinely inside the `processed_events` comparison — mutating Notifications kills the test, and the failure names Notifications. Both halves of the Notifications comparison (columns and indexes) fired. This is precisely the guard-that-does-not-guard shape this build has hit repeatedly, and it is **not** present here.
3. **`otc_notifications`'s one-table closure genuinely closes**, in two independent places, in two different projects, and the report is right that the parity test alone would not have caught it.

`git status --porcelain` after restore is identical to before. My probes left no residue.

---

## 6. Acceptance-item mapping I verified

`sdd: false`, so no `R<n>`. The contract is feature 11's three-item `acceptance` array plus Databases doc §6/§7/§4.3/§3 and `CLAUDE.md`.

| Acceptance item | Named test(s) | Verified? |
|---|---|---|
| 1. "both migrations apply and re-apply cleanly" | `Billing.IntegrationTests.MigrationTests` ×2, `Notifications.IntegrationTests.MigrationTests` ×2 | OK — the re-apply tests genuinely **drop the database** (`fixture.DropDatabaseAsync`), not just its tables. Independently corroborated: both schemas exist on the real `otcnet-mssql` |
| 2. **"outbox/processed_events parity test across all four DbContexts passes"** | `ReliabilityTableParityTests.Outbox_And_ProcessedEvents_Are_Defined_Identically_Across_All_Four_DbContexts` | **OK — armed by me three ways (§5), plus an independent live 4-way `diff` (§3)**. Reads `INFORMATION_SCHEMA` and `sys.indexes`, never EF metadata; migrates four fresh real databases; names both sides on failure |
| 3. "otc_notifications contains processed_events and nothing else" | `ReliabilityTableParityTests.Notifications_Database_Contains_Only_ProcessedEvents` **and** `Notifications.IntegrationTests.SchemaColumnTypeTests.No_Unexpected_Table_Or_Column_Exists` | OK — **closed-set** assertions, not presence checks, in two projects; armed by probe C; corroborated live |
| (task-level) §6 MS-SQL types | `Billing.IntegrationTests.SchemaColumnTypeTests` ×2 + `OutboxSeqIdentityTests` ×2 | OK — `INFORMATION_SCHEMA` and `COLUMNPROPERTY`; whitelist **and** closure; live-corroborated |
| (task-level) **`invoice_number_sequences.next_value` is `int`** | `SchemaColumnTypeTests.Every_Table_Has_The_Expected_Columns_And_SqlTypes` | OK — `int` in the entity (`InvoiceNumberSequence.cs:24`), in the configuration, in the migration, in the live database and in the test. **The reason is an XML doc comment on the property itself** (`:15-23`), citing §6, #7's `0002_*.sql` and feature 9's D2 by name — feature 10's "the reason lives on the type" pattern, repeated deliberately |
| (task-level) **the exact three-FK set** | `ForeignKeyTests.Exactly_The_Three_Spec_ForeignKeys_Exist_With_The_Right_Reference_And_DeleteAction` | OK — exact set of 3, referenced table, referenced column, delete rule; closed on both sides; corroborated against `sys.foreign_keys` live and against #7's `0000_*.sql:94-96` |
| (task-level) indexes per §6 | `IndexTests.Every_Spec_Index_Exists_With_The_Expected_Columns_And_Uniqueness` | OK — all 12 spec indexes, correct columns, correct order, correct uniqueness; armed by probe B. See **A1** |
| (task-level) **unique constraints genuinely reject duplicates**, incl. `payments.payment_reference` | `UniqueConstraintTests` ×5 (Billing) + ×2 (Notifications) | OK — real conflicting inserts, real `DbUpdateException`, two pair-controls. `Payments_Rejects_A_Duplicate_PaymentReference` covers the R47/R48 idempotency key, and the implementer's own probe 2 showed it fails (`No exception was thrown`) when the unique index is removed |
| (task-level) round-trip per table | `RoundTripTests` ×7 (Billing, all 8 tables) + ×1 (Notifications) | OK — each writes through one `DbContext` and reads back through a **new** one. See **A3** |

---

## 7. Disclosure quality

The brief's item 7, and the question no green suite answers. **The report meets the bar, and this time with no over-claim.**

I went looking for an undisclosed divergence and did not find one. All seven listed divergences are real and correctly characterised, and I verified each against either the live database or #7's committed SQL. Three things raise this report above its two predecessors:

1. **The round-trip timestamp gap is disclosed rather than over-claimed.** Feature 10's report said its round-trips "assert every field survived unchanged" when they excluded the datetimes (that review's A1). This report says plainly that timestamps are *not* asserted, names feature 10's review as where the gap was first recorded, and declines to re-file it as new. That is the correct handling of an inherited, already-characterised gap.
2. **The self-inflicted false-green during arming is reported.** A `quality.sh` run launched concurrently with `--no-incremental` builds had coverlet fail to find `OrderToCash.Billing.IntegrationTests.dll`; the run still summarised as success because **that project's 22 tests never executed**. The implementer caught it by counting per-project `Passed!` lines, discarded the run, and re-ran serially. This is a *silent gap* — the hardest of the three failure signals to notice — and reporting it rather than quietly re-running is the behaviour the harness exists to produce. My own `quality.sh` run (serial, no probes in flight) independently shows all seven suites reporting `Passed!`, so the recorded run is reproducible.
3. **Divergence 6 records a fix to an inherited advisory** and explains why the inherited version was worse. Reports usually list what diverges from spec; this one also lists what diverges from the *previous feature's file*, in the improving direction.

The report claims no measurement it did not take, and marks its estimates as estimates.

---

## Defects

**No blocking defects.** Six advisories, four of them inherited and one of them mine to hand to feature 34.

### A1 — ADVISORY (inherited, third consecutive feature). `IndexTests` is presence-only, with no closure over the index set

`tests/Billing.IntegrationTests/IndexTests.cs:25-44` is a whitelist of 12 expected indexes. Nothing asserts that no *unexpected* index exists. This is feature 10's A3 and feature 9's D4-shape, one object type over, now at its third occurrence. It remains deliberate and disclosed — an exact-set assertion would fail on the two EF-generated FK-supporting indexes — and both previous features were approved with the same treatment, so it is consistent rather than a regression.

**Why it still matters:** the schema is now finished for three of the four databases. The reason to defer ("the schema is still changing") is running out, and the expectation set only needs the two EF FK indexes named explicitly to close. Twelve spec indexes across four databases is exactly the size where an accidental extra index goes unnoticed. Worth closing at the first aggregate feature that touches Billing, not later.

### A2 — ADVISORY. The parity test compares six column properties but not `IsIdentity`, and `outbox.seq` is the one column where that matters

`ReliabilityTableParityTests.cs:32` — `ColumnShape` carries `Name, Ordinal, DataType, MaxLength, DatetimePrecision, Nullable`. It does **not** carry `COLUMNPROPERTY(..., 'IsIdentity')`. `outbox.seq` is `bigint IDENTITY(1,1)` in all three outbox-bearing databases, and §4.3 calls it *"strictly increasing publication order the relay polls by"*. If one service's `seq` were ever declared `bigint` without `IDENTITY`, **the parity test would pass**: same type, same length, same nullability, same ordinal, and the unique index on `seq` would still exist.

**Not blocking**, for three reasons: `OutboxSeqIdentityTests` asserts `IsIdentity` per-context in each of the three suites, so the property is guarded — just not by the *parity* test; I confirmed live that all three `outbox.seq` columns are genuinely `IsIdentity = 1` (§3, my dump included the column the test omits); and no current code path can produce the divergence. But the parity test's whole value proposition is "these definitions are identical", and there is one property of one column where "identical" is asserted three times separately rather than once comparatively. Adding `IsIdentity` to `ColumnShape` is a two-line change and would make the claim complete. Record it against the outbox relay feature (id 14), which is the first feature whose correctness actually depends on it.

### A3 — ADVISORY (inherited, correctly disclosed). Round-trip tests do not assert timestamp columns on read-back

`tests/Billing.IntegrationTests/RoundTripTests.cs` and `tests/Notifications.IntegrationTests/RoundTripTests.cs` — every test writes `CreatedAt`/`UpdatedAt`/`PaidAt`/`OccurredAt`/`ProcessedAt`/`CreditDate`/`ValueDate` and none asserts them after read-back. Identical to feature 10's A1, **and the report says so itself** rather than claiming otherwise, which is the improvement. The forward risk behind it is unchanged and unprobed: MS-SQL `datetime2` carries no timezone, so EF Core returns `DateTimeKind.Unspecified`, and `CLAUDE.md` requires UTC everywhere. `payments.value_date` and `invoices.paid_at` will both meet this at the remittance feature. Recorded as a forward risk, not a finding — no committed file proves it yet.

### A4 — ADVISORY (structural, for Phase 21). The parity test lives inside `Billing.IntegrationTests`, which therefore project-references three other services

`tests/Billing.IntegrationTests/Billing.IntegrationTests.csproj:27-32` references `Orders.csproj`, `Fulfillment.csproj` and `Notifications.csproj`. This is **test-only**, sanctioned by the task brief, clearly commented, and **not** a C3 violation — no runtime code is shared. But it has two consequences worth naming now rather than discovering later:

- **Build coupling.** Any change to `OrdersDbContext` or `FulfillmentDbContext` now forces `Billing.IntegrationTests` to rebuild, and can break it. `quality.sh`'s build log confirms the ordering dependency.
- **Ownership.** The cross-context invariant is owned by whichever service happened to be implemented last. #7 put its equivalent in a neutral place (`apps/seed/outbox-parity.spec.ts`), which is the better shape. A `tests/Reliability.ParityTests` project referencing all contexts would cost one `.csproj` and would stop the parity test migrating again when a fifth context appears.

Not worth churning now — the test works and is well armed. Worth doing when the Gateway and Read-Model suites land.

### A5 — ADVISORY (leader's file, much improved). `progress/current.md` is one status transition stale

It reads `Status: in_progress` while `feature_list.json` said `in_review`. **C2's fourth box is nevertheless met**, and this is a real change: it was open on features 9 and 10 with *leftovers from a previous feature*, and here it names the correct feature, the correct goal, the correct decisions and the correct blockers, written at `13:00:12` as the feature opened. The file's own Notes section states the corrected rule ("it moves at every **status transition**, not every phase") and then missed one transition. That is a habit two-thirds installed rather than a lesson merely recorded — a genuine improvement over the previous two features, and worth saying so rather than filing it as a third repeat.

### A6 — ADVISORY (mine, for feature 34). The coverage numbers `quality.sh` prints cannot be gated on in their current shape

Not this feature's defect — the gate is parked at feature 34 by design — but the output is now misleading enough to be worth recording before someone tries to enforce it.

`quality.sh` prints **seven** per-test-project line-rates over **overlapping** assembly sets. This run: 95.8% (Contracts alone), 91.3% (SharedKernel alone), **85.0%** (Billing.IntegrationTests, over six assemblies), 77.3% (Orders.IntegrationTests, over three), 68.0% (Fulfillment.IntegrationTests, over three), **20.1%** (Notifications.IntegrationTests, over three), 0.0% (Architecture.Tests, over nine).

The 20.1% is **not** a Notifications problem — it is a Notifications *test project* measured against `SharedKernel` + `Contracts` + `Notifications`, where the seven tests touch one table and none of SharedKernel. `OrderToCash.SharedKernel` appears in six of the seven reports at six different rates. **"≥60% overall" is not computable from this output at all**, and "≥80% domain" is not either. Feature 34 needs a *merged* report (one `coverage.cobertura.xml` across the solution, or `--merge-with`), not a threshold applied to seven incommensurable numbers — and `CLAUDE.md`'s requirement that the gate be "verified to fail when breached" cannot be honoured until the number being gated means something. Recording this here because feature 34 is nineteen features away and the observation is cheapest now.

### Not a defect, recorded to prevent re-litigation

- **`Directory.Packages.props` shows as modified.** Feature 9's `Microsoft.EntityFrameworkCore.Design 10.0.11` pin, uncommitted before this feature began. The `git diff` is exactly that one `PackageVersion` and its comment; the report's claim that no change was needed here is correct.
- **`OrderToCash.sln` adds four test projects** — Orders (feature 9), Fulfillment (feature 10), Billing and Notifications (this feature), all uncommitted. Expected.
- **`src/Orders/Orders.csproj` and `src/Fulfillment/Fulfillment.csproj` show as modified** — features 9 and 10's EF package references, uncommitted. Confirmed by mtime (`11:40:25` and `12:29:26`) to predate this feature.
- **`infra/mssql/init/01-create-databases.sql` shows as modified** — the pre-existing RCSI change from the phase-5/infra session.
- **`DomainPurityTests` passing vacuously** for Billing and Notifications (neither has `Domain/` code yet): correct for a schema-only feature, and disclosed.
- **`varchar` → `nvarchar`, `char(36)` → `uniqueidentifier`, `datetime` → `datetime2(3)`, `json` → `nvarchar(max)`**: the standing MS-SQL translation rules established and approved at feature 9. Not per-feature divergences. `char(3)` for `currency_code` is preserved as `char(3)`, correctly.
- **`Notifications_Database_Contains_Only_ProcessedEvents` asserts an ordered collection against an unordered query** (`ReliabilityTableParityTests.cs:167-179`, no `ORDER BY`), where the sibling `Notifications.IntegrationTests.SchemaColumnTypeTests:157-158` sorts both sides. With exactly one expected row it cannot produce a false failure, and probe C showed it fails correctly. Cosmetic inconsistency only; noted so nobody re-files it.

---

## 8. Scope

`git status --porcelain` — 27 entries, all inside the permitted envelope:

```
 M Directory.Packages.props                   (pre-existing, feature 9)
 M OrderToCash.sln                            (+ Billing/Notifications.IntegrationTests; two others pre-existing)
 M feature_list.json                          (status only)
 M infra/mssql/init/01-create-databases.sql   (pre-existing, infra phase)
 M progress/current.md                        (feature 11's own session record)
 M progress/history.md                        (pre-existing entries + this review's)
 M src/Billing/Billing.csproj                 (+ EFCore.SqlServer, EFCore.Design PrivateAssets=all)
 M src/Notifications/Notifications.csproj     (same two)
 D src/{Billing,Notifications}/Infrastructure/README_PLACEHOLDER.cs
?? src/Billing/Infrastructure/Persistence/        (8 entities, 8 configurations, context, factory, migration)
?? src/Notifications/Infrastructure/Persistence/  (1 entity, 1 configuration, context, factory, migration)
?? tests/Billing.IntegrationTests/                (9 files)
?? tests/Notifications.IntegrationTests/          (6 files)
... plus the pre-existing uncommitted feature-9/10 set
```

**The scope constraint the brief singled out is met.** `src/Orders/**` and `src/Fulfillment/**` were referenced, never edited: the latest source mtime in either tree is `src/Fulfillment/.../20260901103111_InitialCreate.cs` at **12:52:36**, from feature 10's session; feature 11's first artefact is `src/Billing/.../Credit.cs` at **13:02:39**. Ten minutes and a feature boundary separate them. The `M` marks on `Orders.csproj` (mtime `11:40:25`) and `Fulfillment.csproj` (mtime `12:29:26`) are features 9 and 10's uncommitted work.

Nothing under `specs/`, `src/SharedKernel/`, `src/Contracts/`, `tests/Architecture.Tests/`, `n8n/`, `docs/`, `CLAUDE.md` or the other four services was touched. Architecture suite green (12/12) with two new EF references now in scope. `./init.sh` exit 0.

---

## 9. Suite wall-clock — the risk the last review flagged, now with a bigger number

The brief asked for the actual duration, and it has moved again.

| Point | `./quality.sh` wall-clock | MS-SQL containers |
|---|---|---|
| Before feature 9 | a few seconds | 0 |
| After feature 9 | **1 m 07 s** | 1 |
| After feature 10 | **1 m 28.8 s** | 2 |
| **After feature 11 (measured this review)** | **2 m 39 s** (159 s, exit 0, 125 tests) | **4** |

**+70 s for two more database suites — a steeper jump than feature 10's +22 s**, because the parity test alone migrates four fresh databases. Per-suite test time from my run: Billing 23 s, Fulfillment 21 s, Orders 16 s, Notifications 12 s = **72 s of container time**, against 16 s of build and a few seconds of format check. Four *more* services get integration suites, and Kafka and NATS containers start slower than MS-SQL.

Feature 10's review recommended sharing one MS-SQL container across the database suites **"at feature 11, with two suites to reconcile rather than six."** That was not done, and the mitigation now costs four fixtures instead of two — four near-identical `MsSqlContainerFixture.cs` files, each spinning its own container, each with the same `CreateFreshDatabaseAsync` / `DropDatabaseAsync` pair. This is not a defect of feature 11 (the advisory was addressed to the leader, and the feature correctly copied the established pattern rather than inventing a new one mid-feature), but the observation is now stronger, not weaker: **the cheapest moment to consolidate has passed once, and it will pass again.** At the current trajectory `quality.sh` crosses 4 minutes before Phase 21. Three minutes is where a gate stops being run per change; #7's history records what happens to a gate nobody runs.

Two options remain, both cheaper now than later: one shared container behind a single solution-level xUnit collection fixture (only the container is shared, not the schema — `CreateFreshDatabaseAsync` already isolates per test), or split `quality.sh` into fast and full modes. Phase 21 needs this number.

---

## Phase 6 — closing assessment

Three features, three databases-plus-one, 20 tables, 60 integration tests, one rejection.

| | #7 (NestJS) | #8 (.NET) | Ratio |
|---|---|---|---|
| `db_orders` | ~1.5h, approved first pass | **~2.3h**, **REJECTED**, fix pass, re-review | 1.5× **slower** |
| `db_fulfillment` | ~1.25h, approved first pass | **~0.55h**, approved first pass | **2.3× faster** |
| `db_billing` | ~0.75h, approved first pass | **~0.75h**, approved first pass | **1.0× — parity** |
| **Phase 6 total** | **~3.5h**, 3 databases, 22 tables, ~23 tests | **~3.6h**, **4** databases, 20 tables, **60** tests | **~1.03× — no saving** |

### Did the feature-10 effect persist, decay, or was feature 10 just the easier schema?

Feature 10's review concluded: *"The spec told both features what to build; only the review told the second how to prove it."* Feature 11 is the third data point, and the honest reading is **the effect persisted on quality and vanished on speed** — and those are two different questions that the ratio column blurs together.

**On quality, the effect persisted, and it is not attributable to an easier schema.** Billing is the *largest* of the four schemas: 8 tables against Fulfillment's 7, three foreign keys against two, twelve spec indexes against nine, plus a second `DbContext` and a cross-context test neither predecessor had to write. Every guard feature 10 introduced under review pressure appears here **unprompted and first-time**: FKs in the first generated migration with a closed-set assertion; `next_value` as `int` with the reason on the type; table/column closure written from the start; assertions read from `INFORMATION_SCHEMA`, never EF. And it went one step further — feature 11 **closed** an advisory it inherited (`ForeignKeyTests`'s assertion ordering, feature 10's A2) rather than copying it through a third time, and it **disclosed** the round-trip timestamp gap that feature 10's report had over-claimed. That is not a feature merely repeating a pattern; it is a feature reading the previous review and acting on the parts that were left open. The mechanism feature 10's review identified — review-driven, within-run learning — is still operating at feature 11.

**On speed, the dividend converged to zero, and the reason is instructive rather than disappointing.** #7's own `db_billing` took ~0.75h because *#7 had already learned the pattern twice*; it was #7's third database and #7 was already at its floor. #8 could not beat that floor because the floor is set by how much code has to be typed, not by how much thinking has to be done — and #8 had strictly *more* to type: two `DbContext`s instead of one, twenty-nine tests instead of ten, and a parity test that #7 had written earlier and elsewhere (`apps/seed/outbox-parity.spec.ts`, not in `db_billing` at all). **#8 matched #7's best time while delivering roughly three times the test surface and one extra database.** That is a real result, and it is not visible in the ratio.

So: **not decay, and not an easier schema.** The pattern feature 10 identified describes a *quality* effect, and it held. What decayed was the *speed* differential, and it decayed for a boring, predictable reason — the baseline it was being measured against had already absorbed the same learning one run earlier. **The reuse dividend is largest where the baseline was still learning, and smallest where the baseline had already learned.** Feature 9 (#7 learning, #8 rejected) 1.5× slower; feature 10 (#7 half-learned, #8 fully briefed) 2.3× faster; feature 11 (#7 fully learned, #8 fully briefed) parity. That is a coherent curve across three points, and it predicts that the *later* features in a phase will show smaller reuse gains than the middle ones — which is the opposite of the naive expectation that reuse compounds.

**The one number that should not be smoothed over:** Phase 6 as a whole cost #8 **~3.6h against #7's ~3.5h**, on a phase where the entire schema was handed over in advance, in a document, with #7's committed SQL available for corroboration. The specification reused; the verification did not. The rejection round at feature 9 ate the whole dividend of features 10 and 11 combined. This is the fifth, sixth and seventh instances of the phase-5/6 pattern, and it now reads: **the spec is free, the proof is not, and one rejection costs more than two clean features save.**

---

**All applicable boxes are marked (C6 N/A; C5's fourth box is the leader's next step). Feature 11 `db_billing` is APPROVED and set `done`. Phase 6 is complete.**
