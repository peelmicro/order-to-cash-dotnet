# review_db_fulfillment — feature 10 (id 10, phase 6), assessment #8

**Verdict: APPROVED**

Zero blocking defects. Both defects that got feature 9 rejected were explicitly briefed into this feature, and **both were genuinely avoided, not merely claimed to be**: `sys.foreign_keys` on the live `otc_fulfillment` returns exactly the two constraints #7's committed DDL emits, with the right delete rules, and they are guarded by a closed-set assertion I armed myself in two ways; `despatch_number_sequences.next_value` is `int` in the entity, the configuration, the live database and the test. Four advisories, none of them a reason to hold the feature.

The interesting result is in the effort record, not the code — see the closing section.

---

## What I ran, and what I did NOT re-run

The implementer had just run the full suite. Re-running it in full would have been duplicated cost, so I spent the budget on independent evidence instead.

| Ran | Result |
|---|---|
| **Live interrogation of `otcnet-mssql` / `otc_fulfillment`** — `INFORMATION_SCHEMA.COLUMNS`, `sys.foreign_keys` + `sys.foreign_key_columns`, `sys.indexes` + `sys.index_columns`, `COLUMNPROPERTY(...,'IsIdentity')` | Table below. Primary evidence; the report's mapping table was not taken on trust |
| **Live schema diff of `outbox`/`processed_events` between `otc_orders` and `otc_fulfillment`** | Byte-identical on columns (name, ordinal, type, length, precision, nullability, identity) **and** on indexes (name, uniqueness, column order). See §3 |
| **Independent check against #7's committed DDL** — `apps/fulfillment/drizzle/0000_nappy_mad_thinker.sql`, `0001_*`, `0002_*` in the `order-to-cash-nestjs` checkout | Every column width, every FK, every index confirmed. See §2 |
| **My own arming probe 1** — delete-rule mutation on the `despatch_items` FK | Guard killed with the descriptive message |
| **My own arming probe 2** — extra table + third FK injected into the migration | Both closure assertions killed |
| `tests/Fulfillment.IntegrationTests` after restore | 19/19 |
| `tests/Architecture.Tests` (NetArchTest) | 12/12, 660 ms — run because C3's first box requires running it, not eyeballing it |
| `./quality.sh` | green end to end, 96 tests, **1 m 28.8 s** |
| `./init.sh` | exit 0 |
| `git status --porcelain --untracked-files=all` before and after my probes | Identical; `md5sum` on the restored migration matches the pre-probe backup |

**Not re-run, and why:**

- **`tests/SharedKernel.UnitTests` and `tests/Contracts.UnitTests` in isolation** — no claim under review touches them, and `./quality.sh` covered them in the same pass (32 and 21 green).
- **`tests/Orders.IntegrationTests` in isolation** — feature 9's territory, closed last review; `quality.sh` covered it (12 green). I did query `otc_orders`'s reliability tables directly, but as the *reference* for the parity check, not to re-audit feature 9.
- **`dotnet ef database update` against the compose container** — the migration's effect on `otcnet-mssql` is already observable and I queried it directly, which is stronger evidence than re-running the command.
- **The implementer's four arming probes** — I deliberately ran two *different* probes rather than reproducing theirs. Reproducing a probe verbatim tests the report; a different probe tests the guard.
- **`dotnet ef migrations has-pending-model-changes`** — not re-run as a standalone command; the same property is enforced inside every test in this suite, because EF Core 10 raises `PendingModelChangesWarning` as an error at the `MigrateAsync()` step (established in feature 9's re-review).

---

## CHECKPOINTS.md — boxes walked

### C1 — The harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (plus `suite_runner`).
- [x] Every agent definition declares its model — `init.sh` confirms the pins.
- [x] `./init.sh` exits 0.

### C2 — State is coherent
- [x] At most one feature `in_progress` — zero `in_progress`, one `in_review` (feature 10) at review time; `init.sh` confirms "no feature in_progress".
- [x] Every status is in `rules.valid_status` — `init.sh` confirms.
- [x] Every `done` feature has passing tests associated with it — 9 done, and `quality.sh` is green across all five suites.
- [ ] **`progress/current.md` describes the active session** — it does not. It still reads *"Feature: `db_orders` (id 9, phase 6) / Status: in_progress — rejected on review, fix pass in flight"* while feature 10 was worked and submitted. **A4 below.** Leader's file, not the implementer's, so **not** a cause for rejection — but this is now the *second consecutive feature* in #8 where this box is open, and #7 logged it at exactly this point too. Recording it as a repeat, not a first.
- [x] Every `blocked` feature records why — none blocked.

### C3 — Architecture is respected
- [x] No EF Core / Kafka / NATS / MongoDB / AspNetCore reference inside any `Domain/` folder — **verified by running** `tests/Architecture.Tests`, 12/12. `Fulfillment.csproj` now carries `Microsoft.EntityFrameworkCore.SqlServer`, so the assembly is genuinely under the rule; it passes because `src/Fulfillment/Domain/` holds no code yet. Honest, and disclosed in the report (divergence 3).
- [x] No cross-service database access; no FK crosses a service boundary. Live check: `otc_fulfillment` has exactly two foreign keys and both are internal. `company_code`, `retailer_code`, `product_code`, `order_reference` are plain `nvarchar` business identifiers with no FK anywhere — exactly as §5 requires — and their widths (20/20/30/20) match `otc_orders` byte for byte, which is what makes message-carried identifiers safe without FKs.
- [x] No shared runtime code beyond `src/SharedKernel` and `src/Contracts`. `Fulfillment.csproj` references only those two projects.
- [x] `src/SharedKernel` still has zero `PackageReference` entries (the single grep hit is the comment forbidding them).
- [x] No `decimal` in domain arithmetic — `grep -rn "decimal\|double \|float "` over `src/Fulfillment/` and `tests/Fulfillment.IntegrationTests/` returns nothing. Every quantity column (`units`, `reserved_units`, `low_stock_threshold`) is `int`.
- [x] Every inter-service interaction classifiable as Kafka-fact or NATS-RPC — no interactions in this feature.
- [x] No stray debug logging, no context-free TODOs — `grep` for `TODO` and `Console.WriteLine` over the feature's files returns nothing.

### C4 — Verification is real
- [x] `./quality.sh` passes (format + build + test + coverage), exit 0, 96 tests, 1 m 28.8 s.
- [x] Domain tests are pure — none in this feature; the existing pure suites are untouched and green.
- [x] Integration tests use Testcontainers for .NET against real MsSql — `MsSqlContainerFixture.cs` pins `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04`, the same tag as `docker-compose.infra.yml`. Never a mock, never SQLite-in-memory. Confirmed by watching the container come up during my own probe runs.
- [~] Coverage thresholds — **reported, not enforced; pre-existing and disclosed.** `quality.sh:80` still parks enforcement behind `TODO(feature 34 — sonarqube_quality_gates, phase 21)`. Reported: 95.8% / 91.3% / 77.3% / 68.0% / 0.0% (the last is the `RegexGenerator.g.cs` artefact under `SharedKernel/obj/`, not real code). Unchanged by this feature and not its defect — but the inert gate is now two features older than when it was first flagged.
- [x] No Jest anywhere. xUnit throughout.

### C5 — The session closed cleanly
- [x] No suspicious untracked files. 76 entries, every one inside `src/Fulfillment/**`, `tests/Fulfillment.IntegrationTests/**`, the pre-existing uncommitted feature-9 set, or the four expected root/progress files. `TestResults/` is gitignored.
- [x] `progress/history.md` has an entry for the feature, **including its effort record** — appended by this review.
- [x] `feature_list.json` reflects true state — set `done` by this review.
- [ ] The human has been told what was done and how to test manually — the leader's step, next.
- [x] **Claude did not commit.** Nothing was committed or pushed by this review.

### C6 — Spec-Driven Development
**N/A.** Feature 10 is `"sdd": false`. `init.sh` confirms 0 sdd features past pending.

### C7 — Spec-reuse fidelity
Only the boxes this feature can touch:
- [x] **`specs/shared/` untouched** by this feature — `git status` is clean on `specs/`. No amendment, silent or otherwise.
- [x] **No `R<n>` claimed** (`sdd: false`); `specs/shared/test-matrix.md` untouched.
- [x] **The effort record is honest, including where it was not faster** — see the closing section; this one *was* faster, and I have said why in a way that separates spec-reuse from within-run learning.

The remaining C7 boxes (n8n workflows, the black-box API script, the README benchmark section) belong to later phases.

---

## 1. Live-database findings — my own queries, not the report's table

Connected to `otcnet-mssql`, database `otc_fulfillment`, the database the migration was actually applied to.

| Check | Expected (Databases doc §5 / §4.3 / §3) | Live database | Verdict |
|---|---|---|---|
| Tables present | 7 (`stock`, `reservations`, `despatches`, `despatch_items`, `despatch_number_sequences`, `outbox`, `processed_events`) | all 7 + `__EFMigrationsHistory`, nothing else | OK |
| **`despatch_number_sequences.next_value`** | **`int`** (§3 "single-row technical tables"; #7's `0002_*.sql:12` is `next_value int NOT NULL`) | **`int`, NOT NULL** | **OK — feature 9's D2 avoided** |
| `despatch_number_sequences.id` | §5 gives no type; #7 uses `tinyint` | `int`, NOT NULL | OK (disclosed, divergence 1) |
| **Foreign keys** | **exactly 2** per §5 and #7's `0000_nappy_mad_thinker.sql:75-76` | **exactly 2** — `reservations.stock_id → stock.id` `NO_ACTION`; `despatch_items.despatch_id → despatches.id` `CASCADE` | **OK — feature 9's D1 avoided** |
| `stock` columns | `char(36)`, `varchar(20)`, `varchar(30)`, `int`×3, `datetime`×2 | `uniqueidentifier`, `nvarchar(20)`, `nvarchar(30)`, `int`×3, `datetime2(3)`×2, all NOT NULL | OK |
| `reservations` columns | §5 + #7's widths 36/36/20/20/30/20/int/20 | `uniqueidentifier`×2, `nvarchar` 20/20/30/20, `int`, `nvarchar(20)`, `datetime2(3)`×2 | OK — widths match #7 exactly |
| `despatches` columns | 36/20/datetime/20/20/20 + audit | `uniqueidentifier`, `nvarchar(20)`, `datetime2(3)`, `nvarchar(20)`×3, `datetime2(3)`×2 | OK |
| `despatch_items` columns | 36/36/varchar(30)/int + audit | `uniqueidentifier`×2, `nvarchar(30)`, `int`, `datetime2(3)`×2 | OK |
| `outbox.seq` | `bigint unsigned autoincrement unique` | `bigint`, `COLUMNPROPERTY(...,'IsIdentity') = 1`, `IX_outbox_seq` unique | OK |
| `outbox.published_at` | nullable | `datetime2(3)`, NULL allowed — the **only** nullable column besides `trace_parent` | OK |
| `outbox.trace_parent` | `varchar(64)` nullable | `nvarchar(64)`, NULL allowed | OK |
| `outbox.payload` | `json` | `nvarchar(max)`, NOT NULL | OK (MS-SQL 2022 has no `json` type; disclosed, divergence 2) |
| `outbox.causation_id` | `char(36)` | `uniqueidentifier` NOT NULL | OK — **and present from migration 1**, where #7 added it in `0001_*` |
| Index `stock (company_code, product_code)` unique | §5 | `IX_stock_company_code_product_code`, unique, **column order correct** | OK |
| Index `reservations (order_reference, status)` | §5 | `IX_reservations_order_reference_status`, non-unique, order correct | OK |
| Index `despatches (despatch_reference)` unique | §5 | `IX_despatches_despatch_reference`, unique | OK |
| Index `despatches (order_reference)` unique | §5 "at most one despatch per order"; #7's `0002_*.sql:16` | `IX_despatches_order_reference`, unique | OK — **present from migration 1** where #7 needed a second migration |
| Index `outbox (published_at, seq)` | §4.3 relay poll index | `IX_outbox_published_at_seq`, cols in that order, non-unique | OK |
| Index `outbox (published_at, occurred_at)` | §4.3 lag metric | `IX_outbox_published_at_occurred_at`, order correct | OK |
| Index `outbox (event_id)` unique | §4.3 | `IX_outbox_event_id`, unique | OK |
| Index `processed_events (event_id, consumer)` unique | §4.3 | `IX_processed_events_event_id_consumer`, unique, order correct | OK |
| Non-spec indexes | — | `IX_reservations_stock_id`, `IX_despatch_items_despatch_id` (EF FK-supporting) + `PK_*` | OK (disclosed, divergence 4) |

Column ORDER inside every composite is genuinely correct — `(published_at, seq)` is `(published_at, seq)`, `(company_code, product_code)` is `(company_code, product_code)`, `(event_id, consumer)` is `(event_id, consumer)`. That is the sharpest available check and it is clean.

---

## 2. Independent corroboration against #7's committed DDL

I did not take the report's #7 citations on trust; I read the SQL in the `order-to-cash-nestjs` checkout.

- `apps/fulfillment/drizzle/0000_nappy_mad_thinker.sql:75-76` — exactly two `ADD CONSTRAINT ... FOREIGN KEY`, `ON DELETE no action` and `ON DELETE cascade` respectively. The report's claim is verbatim correct, including the delete rules it says it checked rather than assumed.
- `0002_despatch_number_sequence_and_order_reference_unique.sql:12` — `next_value` **int** NOT NULL, `id` tinyint. Both the type the spec requires and the `id` divergence the report discloses.
- Every column width in `0000_*.sql` matches the live `otc_fulfillment` one for one: `stock` 20/30, `reservations` 20/20/30/20/20, `despatches` 20/20/20/20, `despatch_items` 30, `outbox` 60/64, `processed_events` 50.
- **What #7 needed three migrations for, #8 shipped in one.** `0000` has 6 tables and an `outbox` without `causation_id`, `seq` or `trace_parent`; `0001` adds those three; `0002` adds `despatch_number_sequences` and the `despatches.order_reference` unique. #8's single `InitialCreate` contains all of it. That is #7's `0001` **and** `0002` never needing to be written — a larger dividend than feature 9's, and it is invisible in wall-clock.

---

## 3. Reliability-table parity with `otc_orders` — checked now, so feature 11 cannot inherit a divergence

Feature 11 owns the cross-context parity test, but a divergence found there costs two features' rework, so I compared the two live schemas directly.

```
COLUMNS:   IDENTICAL   (name, ordinal position, data type, max length,
                        datetime precision, nullability, IsIdentity —
                        outbox 12 cols, processed_events 5 cols)
INDEXES:   IDENTICAL   (index name, uniqueness, is_primary_key,
                        column name AND key_ordinal)
```

`diff` on both dumps is empty. `outbox` in `otc_fulfillment` is byte-for-byte `otc_orders`'s, down to `IX_outbox_published_at_seq` having `published_at` at ordinal 1 and `seq` at ordinal 2. Feature 11's parity test will have nothing to find between these two — which is the point of the task's instruction to copy the configurations rather than re-derive them, and the report is right that this mattered more for correctness than for speed.

---

## 4. Test quality — do the assertions read the database or the ORM?

The brief's item 3, and it was honoured without exception.

- `SchemaColumnTypeTests.cs:118` and `:195` — `SELECT ... FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_CATALOG = DB_NAME()` over a raw `SqlConnection`.
- `IndexTests.cs:58-63` — `sys.indexes` / `sys.index_columns` / `sys.columns`, `ORDER BY ic.key_ordinal`, compared with an ordered sequence match, so column order is genuinely asserted.
- `ForeignKeyTests.cs:64-73` — `sys.foreign_keys` joined to `sys.foreign_key_columns`, `sys.tables`, `sys.columns`; asserts referenced table, referenced **column** and **delete rule** per FK.
- `OutboxSeqIdentityTests.cs:28` — `COLUMNPROPERTY(OBJECT_ID('dbo.outbox'), 'seq', 'IsIdentity')`, plus a sibling test that inserts two real rows and asserts the increment.

`grep` for EF metadata access (`.Metadata`, `IDesignTimeModel`, `Model.`) across the suite returns **nothing**. Not one assertion round-trips through EF's own model. Asking EF what EF thinks would have been the reject-level shape; this is not that.

Two further quality points:

- `MigrationTests.Migration_ReApplies_Cleanly_From_Empty_When_Run_Twice` (`MigrationTests.cs:48-49`) **drops the whole database** and recreates it between the two `MigrateAsync()` calls. That is the real "from empty" claim.
- `UniqueConstraintTests` inserts genuinely conflicting rows and asserts `DbUpdateException`, and carries **two control cases** proving the `stock` and `processed_events` constraints are on the pair rather than the first column alone. More than the acceptance list asked for.

---

## 5. My own arming — two probes, deliberately different from the implementer's four

Protocol: backup copy outside git (`md5sum` verified) → introduce violation → forced rebuild (`--no-incremental`) → run named test → restore → `md5sum` + content grep → forced rebuild → re-run. Per feature 9's methodology note, the confirming run let `dotnet test` build for itself rather than pairing `--no-incremental` with `--no-build`.

Pre-probe backup md5: `b5e873caca8c3950a8afdf9cee086e41` — **the same hash the implementer's report records**, which independently confirms their own restore was genuine and not merely described.

| # | Violation | File | Test | Result (verbatim) |
|---|---|---|---|---|
| 1 | `onDelete: ReferentialAction.Cascade` → `Restrict` on `FK_despatch_items_despatches_despatch_id`. **Deliberately subtler than deleting an FK**: the count stays 2 and the referenced table stays correct, so only the delete-rule assertion can catch it | `Migrations/20260901103111_InitialCreate.cs:119` | `ForeignKeyTests.Exactly_The_Two_Spec_ForeignKeys_Exist_With_The_Right_Reference_And_DeleteAction` | **FAIL** — `despatch_items.despatch_id: expected delete action CASCADE, got NO_ACTION` (`ForeignKeyTests.cs:124`) |
| 2 | Injected a whole extra table `probe_table` carrying a **third** foreign key to `stock.id` — kills two closure assertions at once, and is a stronger probe than the report's shadow column because it tests table-set closure and FK-set closure together | same file | `ForeignKeyTests...` **and** `SchemaColumnTypeTests.No_Unexpected_Table_Or_Column_Exists` | **BOTH FAIL** — `Assert.Equal() Failure: Values differ` at `ForeignKeyTests.cs:123` (2 expected, 3 found) and `Assert.Equal() Failure: Collections differ ↓ (pos 4)` at `SchemaColumnTypeTests.cs:223`. The run reported `Failed: 2, Passed: 1` — **the whitelist test passed over the extra table while the closure test caught it**, which is feature 9's D4 gap demonstrated as real and genuinely closed here |
| — | Restored (`md5sum` identical to backup, `grep -c probe_table` = 0), forced rebuild | same | full `Fulfillment.IntegrationTests` | **19/19 passed** |

`git status --porcelain --untracked-files=all` after restore is identical to before. My probes left no residue.

**The conclusion the brief asked for:** the FK set is **guarded, not merely present**. Feature 9's fix added constraints *and* an assertion, and this feature inherited the assertion rather than only the constraints. Removing a foreign key, weakening a delete rule, or adding an unexpected one all fail a named test. The closure assertions genuinely close in both directions — missing and extra — at column level, table level and foreign-key level.

---

## 6. Acceptance-item mapping I verified

`sdd: false`, so no `R<n>`. The contract is feature 10's two-item `acceptance` array plus Databases doc §5/§4.3 and `CLAUDE.md`.

| Acceptance item | Named test(s) | Verified? |
|---|---|---|
| 1. "migrations run from empty" | `MigrationTests.Migration_Applies_Against_An_Empty_Database`, `MigrationTests.Migration_ReApplies_Cleanly_From_Empty_When_Run_Twice` | OK — the second genuinely drops the database (`fixture.DropDatabaseAsync`), not just its tables. Independently corroborated: the schema exists on the real `otcnet-mssql` container |
| 2. "round-trip integration test per table" | `RoundTripTests` ×6 covering all 7 tables (`despatches`/`despatch_items` combined) | OK — each writes through one `DbContext` and reads back through a **new** one, so the read genuinely hits the database rather than the change tracker. See **A1** for what those assertions do *not* cover |
| (task-level) §5 MS-SQL types | `SchemaColumnTypeTests.Every_Table_Has_The_Expected_Columns_And_SqlTypes` + `No_Unexpected_Table_Or_Column_Exists` + `OutboxSeqIdentityTests` ×2 | OK — `INFORMATION_SCHEMA` and `COLUMNPROPERTY`, never EF metadata; closure asserted; independently confirmed against the live database |
| (task-level) **foreign keys per §5** | `ForeignKeyTests.Exactly_The_Two_Spec_ForeignKeys_Exist_With_The_Right_Reference_And_DeleteAction` | OK — exact set of 2, referenced table, referenced column, delete rule; armed twice by me |
| (task-level) indexes per §5 | `IndexTests.Every_Spec_Index_Exists_With_The_Expected_Columns_And_Uniqueness` | OK — all 9 spec indexes, correct columns, correct order, correct uniqueness. See **A3** |
| (task-level) unique constraints reject duplicates | `UniqueConstraintTests` ×5 (3 rejections + 2 pair-controls) | OK — real conflicting inserts, real `DbUpdateException` |

---

## 7. Disclosure quality

The brief's item 7, and the question no green suite answers. **The report meets the bar**, with one small over-claim.

I went looking for an undisclosed divergence and, apart from A1 below, did not find one. All five listed divergences are real and correctly characterised, and I verified each against either the live database or #7's committed SQL:

1. `despatch_number_sequences.id` as `int` — true; #7 uses `tinyint`, §5 gives no type.
2. `payload` as `nvarchar(max)` — true and correct for MS-SQL 2022.
3. No domain aggregates — true; `src/Fulfillment/Domain/` is empty, and this is why `DomainPurityTests` passes vacuously. Saying so is what makes the 12/12 honest rather than misleading.
4. Non-spec-named indexes — true, and the count is right this time (2 EF FK indexes; feature 9's report over-declared 7 where it was 6).
5. Delete rules verified against #7's actual committed SQL rather than assumed — **I checked this claim specifically**, because "we checked rather than assumed" is exactly the sort of claim that is cheap to write and hard to verify. `0000_nappy_mad_thinker.sql:75-76` says what the report says it says. The claim holds.

The report also names its own uncertainty in the right places ("this was checked, not assumed") and does not dress an estimate as a measurement. That is the standard feature 9's corrected report set, met here on the first submission.

---

## Defects

**No blocking defects.** Four advisories.

### A1 — ADVISORY. The round-trip tests never assert a timestamp column on read-back, and the report over-claims that they do

**Files:** `tests/Fulfillment.IntegrationTests/RoundTripTests.cs` — every test writes `CreatedAt`/`UpdatedAt`/`OccurredAt`/`ProcessedAt`/`DespatchDate` (lines 39, 74, 87, 118, 122, 131, 193-194, 225-226) and **not one asserts them after the read-back** (`:45-52`, `:143-147`, `:202-206`, `:234-235`).

**Why it matters, mildly:** three separate reasons, none urgent.

1. **`progress/impl_db_fulfillment.md:165-166` says each test "asserts every field survived unchanged".** It does not — the datetime fields are excluded. This is the one place the report claims slightly more than its tests deliver. It is a small, one-sided over-claim in a report that is otherwise carefully accurate, and it is exactly the shape (claimed coverage that is not there) that made feature 9's D1 worse than a plain gap. Flagging it so the pattern does not grow.
2. **#7's equivalent covered this and named it.** #7's `progress/history.md` for `db_fulfillment` records "per-table field-level round-trip incl. outbox JSON payload and **UTC datetimes**". #8's round-trip is weaker than #7's on a point #7 thought worth writing down.
3. **There is a real forward risk behind it.** MS-SQL `datetime2` carries no timezone, so EF Core returns `DateTimeKind.Unspecified` on read-back, not `Utc`. `CLAUDE.md` requires UTC everywhere and §4.3 calls `occurred_at` *"the only ordering the read model trusts"*. Nothing in this feature is wrong today — the column type and precision are asserted, and I confirmed `datetime2(3)` on the live database — but the first feature that compares a read-back `DateTime` to a `DateTimeKind.Utc` value will meet this. **I did not probe this**; it is documented EF/SQL Server behaviour, and I am recording it as a forward risk rather than as a finding, which is the distinction `CLAUDE.md` requires for anything not proven by a committed file.

**Not blocking** because the acceptance item is "round-trip integration test per table" and that is satisfied; the missing assertions are on fields whose *type and precision* are already asserted from `INFORMATION_SCHEMA` by a different test. Fold the UTC assertion in when the Fulfillment aggregates land, and correct the report's sentence.

### A2 — ADVISORY (inherited). `ForeignKeyTests` reports the count before the diagnostic

`tests/Fulfillment.IntegrationTests/ForeignKeyTests.cs:123-124` puts `Assert.Equal(2, actual.Count)` **before** `Assert.True(failures.Count == 0, ...)`. My probe 2 injected a third FK and the failure read `Assert.Equal() Failure: Values differ` — the `failures` list saying `unexpected foreign key(s) not in the spec: [probe_table.stock_id]` was built and then discarded. The implementer's own probe 1 hit the same thing from the other direction (`Expected: 2 / Actual: 1`, no diagnostic).

This is **A2 from feature 9's re-review, copied through verbatim** into a file that was correctly modelled on feature 9's. Worth naming because it is a small illustration of the reuse dynamic at work *inside* the run: copying a good file also copies its advisories. Swapping the two lines surfaces the diagnostic. Cosmetic; the guard works either way, as both probes demonstrate.

### A3 — ADVISORY. `IndexTests` is presence-only, with no closure over the index set

`IndexTests.cs:21-36` is a whitelist of 9 expected indexes. Nothing asserts that no *unexpected* index exists. This is the D4 shape one object-type over, and it is the one place in this suite where closure was **not** applied. It is deliberate and disclosed (the two EF FK-supporting indexes would fail an exact-set assertion), and feature 9 was approved with the same treatment, so it is consistent rather than a regression. If a later feature wants it closed, the expectation set has to include the EF-generated FK indexes explicitly — which is a small cost for catching an accidental index, and worth doing once the schema stops changing.

### A4 — ADVISORY (leader's file, second consecutive occurrence). `progress/current.md` is stale

Reads *"Feature: `db_orders` (id 9, phase 6) / Status: in_progress — rejected on review, fix pass in flight"* while feature 10 was worked and submitted. C2's fourth box. The irony is sharp: the file's own Notes section contains the line *"C2's fourth box was open and it was my file... The leader is not exempt from the checkpoints it enforces"* — written last feature, about this exact miss, and then not acted on this feature. #7 logged the same thing at the same point and called it "D2 lesson, third occurrence". **The lesson was written down and still did not change the behaviour**, which says something about the difference between recording a lesson and installing a habit. Reset it at session close.

### Not a defect, recorded to prevent re-litigation

- **`Directory.Packages.props` shows as modified.** That is feature 9's `Microsoft.EntityFrameworkCore.Design 10.0.11` pin, uncommitted before this feature began. The report correctly states no change was needed here, and the diff confirms it.
- **`OrderToCash.sln` adds two test projects**, `Orders.IntegrationTests` (feature 9, uncommitted) and `Fulfillment.IntegrationTests` (this feature). Expected.
- **`infra/mssql/init/01-create-databases.sql` shows as modified** — the pre-existing RCSI change from the phase-5/infra session. Not this feature's work.
- **The migration file's mtime is later than the implementation report's.** That is my own restore, not a post-report edit. Content `md5sum` is identical to the implementer's recorded backup hash.
- **`DomainPurityTests` passing vacuously** (Fulfillment has no `Domain/` code yet): correct for a schema-only feature, and disclosed.
- **`varchar` → `nvarchar`, `char(36)` → `uniqueidentifier`, `datetime` → `datetime2(3)`**: the standing MS-SQL translation rules established and approved at feature 9, applied consistently. Not per-feature divergences.

---

## Scope

`git status --porcelain --untracked-files=all` — 76 entries, all inside the permitted envelope:

```
 M Directory.Packages.props            (pre-existing, feature 9)
 M OrderToCash.sln                     (+ Fulfillment.IntegrationTests; Orders.IntegrationTests pre-existing)
 M feature_list.json                   (status only)
 M infra/mssql/init/01-...sql          (pre-existing, infra phase)
 M progress/current.md                 (pre-existing, feature 9; see A4)
 M progress/history.md                 (pre-existing feature-9 entry + this review's)
 M src/Fulfillment/Fulfillment.csproj  (+ EFCore.SqlServer, EFCore.Design PrivateAssets=all)
 D src/Fulfillment/Infrastructure/README_PLACEHOLDER.cs
?? progress/impl_db_fulfillment.md
?? src/Fulfillment/Infrastructure/Persistence/   (7 entities, 7 configurations, context, factory, migration)
?? tests/Fulfillment.IntegrationTests/           (8 files)
... plus the pre-existing uncommitted feature-9 set under src/Orders/** and tests/Orders.IntegrationTests/**
```

Nothing under `specs/`, `src/SharedKernel/`, `src/Contracts/`, `tests/Architecture.Tests/`, `n8n/`, `docs/`, `CLAUDE.md` or the other five services was touched. Architecture suite green (12/12) with the new EF reference in scope. `./init.sh` exit 0.

---

## Timing — a real risk, raised now with the number

The brief asked me to note the suite's wall-clock, and it is worth a paragraph.

| Point | `./quality.sh` wall-clock |
|---|---|
| Before feature 9 | a few seconds |
| After feature 9 (`Orders.IntegrationTests`, 1 MS-SQL container) | **1 m 07 s** |
| After feature 10 (`+ Fulfillment.IntegrationTests`, 2 containers) | **1 m 28.8 s** |

**+22 s for the second database suite, and four more services are coming.** Linear extrapolation puts `quality.sh` past **3 minutes** by the time Billing, Notifications, Gateway and Read-Model have integration suites, and that is before any Kafka or NATS container joins the picture — those are slower to start than MS-SQL. It also **already requires a running Docker daemon** to pass at all.

Three minutes is the threshold where a gate stops being run before every change and starts being run before every commit, and #7's own history records what happens to a gate nobody runs: it goes inert and stays inert for twenty phases. Two mitigations are available cheaply *now* and expensively later: share one MS-SQL container across the database suites via a single xUnit collection fixture (the fixtures are already near-identical, and the per-test `CreateFreshDatabaseAsync` pattern means only the container is shared, not the schema), or split `quality.sh` into fast and full modes. **Raising it as an observation for the leader, not as a defect of this feature** — but it is materially cheaper to fix at feature 11, while there are two suites to reconcile rather than six.

---

## The benchmark question: did the warning work?

The brief asked whether feature 9's rejection changed feature 10's outcome, and whether that is spec-reuse or within-run learning. The answer is clean, and it is the most interesting thing in this review.

**Both briefed defects were avoided, and the evidence is that they were avoided *by design*, not by luck.**

- The foreign keys are in the **first generated migration** — there is one migration, `20260901103111_InitialCreate`, and both FKs are in it. There was no fix pass.
- The FK **test** exists and closes the set. Feature 9 had the constraints wrong *and* had no test; the review's insistence was that adding constraints without an assertion closes the symptom and leaves the defect. Feature 10 has both, and I armed the assertion myself in two ways it had not been armed.
- `next_value` is `int` in the entity from its **first version**, and `DespatchNumberSequence.cs:17-25` carries an XML doc comment that quotes the spec and names the feature-9 mistake it is avoiding. The reason lives on the type, not in a progress file — which is the difference between a lesson recorded and a lesson installed.
- The closure assertion (`No_Unexpected_Table_Or_Column_Exists`) was written **from the start**, where feature 9 needed it added in a fix pass as D4.

**These are different mechanisms and they should be counted separately.** The spec-reuse dividend here is the schema itself — 7 tables, 9 indexes, 2 FKs, every column width, all decided in advance, and shipped in one migration where #7 needed three. That dividend was equally available to feature 9, and feature 9 still got two of them wrong. The **within-run learning** dividend is the part that is new: three specific guards (FK closed-set test, `int` counter, table/column closure) that exist here only because a review found their absence one feature earlier. **The spec told both features what to build. Only the review told feature 10 how to prove it.**

That distinction has a practical consequence for #9: copying `specs/shared/` gets you the first dividend for free, but the second one is a per-run cost that has to be paid again. #9 will start with feature 9's defects unknown to it unless someone hands it this review.

---

**All applicable boxes are marked (C6 N/A; C2's fourth box open on the leader's file, A4, not a cause for rejection). Feature 10 `db_fulfillment` is APPROVED and set `done`.**
