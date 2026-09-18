# Review — `order_number_allocator_scan_cost` (id 47, phase 21)

**Verdict: APPROVED**, with **one binding pre-commit condition (C1)** and **two non-blocking advisories (A1, A2)**. 0 defects in the shipped behaviour; the one defect found is in the *guard's instrument*, not in the fix, and it is routed below to a named owner rather than left as a sentence.

`sdd: false` — no spec directory, no human gate. Specification of record: `feature_list.json` id 47's four acceptance bullets, read verbatim. The feature discharges advisory **A1** of `progress/review_order_number_allocator_seed_race.md` (feature 45), which named both the defect (the `MAX(order_reference)` aggregate is evaluated on *every* allocation, not only when seeding) and the fix shape (a cheap fast-path existence check **in front of** the atomic statement, unmodified).

## What I ran myself, and what I took on trust

Per `CLAUDE.md`'s *"probe the claims, do not re-run the world"*: `./quality.sh` was **not** re-run (see C4 below for why that is the honest mark and not a gap this feature created). Everything else below I executed in this review; where a number matches the implementer's, it is because I reproduced it independently, not because I copied it.

| # | What | Result |
|---|---|---|
| 1 | Seed statement unchanged, proved at the string level | **Byte-identical** (see below) |
| 2 | Baseline `OrderNumberAllocatorTests` | **5/5 green**, 14 s |
| 3 | **Arm A** — remove `WITH (UPDLOCK, HOLDLOCK)`, run feature 45's guard ×3 | **3/3 red**, identical message and line |
| 4 | **Arm B1** — defeat the fast-path gate (`if (true \|\| …)`) | **red** |
| 5 | **Arm B2** — substitute a sibling identifier in the pre-check (`s.Id == 2`) | **red** |
| 6 | **Probe B3** — gate defeated *and* table spelled `[dbo].[orders]` | **GREEN — the guard is defeated.** See C1 |
| 7 | Independent server-side scan accounting on the **real migrated schema** | cold +1 scan, three warm allocations **+0**, old statement +1 |
| 8 | My own actual execution plan at 5,000 rows | `Index Scan`, `ActualRows="5000"`, `ActualScans="1"`, `ActualExecutions="1"`, `ActualLogicalReads="40"` |
| 9 | Full `Orders.IntegrationTests` | **156/156 green**, 12 m 10 s |
| 10 | `Architecture.Tests` (NetArchTest run, not eyeballed) | **50/50 green** |
| 11 | `dotnet format --verify-no-changes` (solution) | **exit 0** |
| 12 | `./init.sh` | **exit 0** |

## 1. The atomic seed statement is genuinely unmodified

This is the whole safety argument, so it was checked at the level that matters — the string the server receives, not the diff's shape. The diff *looks* like it rewrites the raw string literal because the whole block moved four columns right into the new `if`; in a C# raw string literal the closing `"""` delimiter sets the stripped indentation, and it moved with the content. I extracted and dedented the literal from `git show HEAD:…` and from the working tree and compared:

```
IDENTICAL: True
```

Both yield exactly:

```sql
INSERT INTO dbo.order_number_sequences (id, next_value)
SELECT 1, seed.next_value
FROM ( SELECT ISNULL(MAX(CAST(SUBSTRING(order_reference, {…}, LEN(order_reference) - {…}) AS int)), 0) + 1 AS next_value FROM dbo.orders ) AS seed
WHERE NOT EXISTS ( SELECT 1 FROM dbo.order_number_sequences WITH (UPDLOCK, HOLDLOCK) WHERE id = 1 )
```

Corroborated at the binary: `strings -el` on the `OrderToCash.Orders.dll` the **test host actually loads** shows `FROM dbo.orders` and `SELECT 1 FROM dbo.order_number_sequences WITH (UPDLOCK, HOLDLOCK) WHERE id = 1`, plus one unrelated pre-existing statement (`EfCoreOrderRepository.cs:106`, `SELECT TOP (1) 1 AS Value FROM dbo.orders WITH (UPDLOCK, ROWLOCK) WHERE id = {0}`) which is not this feature's. **No blocking finding here**; the statement never changed, so feature 45's scrutiny still stands on the same object it was applied to.

## 2–3. Arm A — feature 45's concurrency guard, re-armed by me

Protocol followed exactly: `cp` backup (`md5sum 40078ad15d5b2c8ddadc892e5ddad646`, matching the working file) → mutate → `touch` → `dotnet build --no-incremental` → **verify the armed SQL in the built binary** (`strings -el`: `SELECT 1 FROM dbo.order_number_sequences WHERE id = 1`, hint gone from the existence check, the claiming `SELECT`'s own `WITH (UPDLOCK, ROWLOCK)` untouched) → run the ONE named test three times → restore → `cmp` → rebuild → green.

`AllocateNextAsync_ConcurrentFirstEverAllocations_TheSecondCallerBlocksOnTheSeedLockInsteadOfRacingIt`, runs 1, 2 and 3, verbatim and identical:

```
  Error Message:
   Microsoft.Data.SqlClient.SqlException : Violation of PRIMARY KEY constraint 'PK_order_number_sequences'. Cannot insert duplicate key in object 'dbo.order_number_sequences'. The duplicate key value is (1).
   at OrderToCash.Orders.Infrastructure.Persistence.EfCoreOrderNumberAllocator.AllocateNextAsync(…) in …/EfCoreOrderNumberAllocator.cs:line 133
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1
```

Restore: `cmp` exit 0, `md5sum` identical to the pre-arm backup, rebuilt `--no-incremental`, `strings -el` shows the hint back, `OrderNumberAllocatorTests` **5/5 green**. `git checkout`/`restore`/`stash` were **not** used at any point.

**This arm is worth more than a repetition of feature 45's.** Line 133 is the seed `ExecuteSqlInterpolatedAsync` **inside the new `if`**. So the armed failure is direct, live evidence for the fix's central correctness claim: in the genuinely-concurrent first-ever case, the unlocked pre-check returned `false` (session A's insert is uncommitted and invisible under RCSI) and control **fell through into the atomic statement**. The fast path does not bypass the gate under the exact interleaving the gate exists for — observed, not argued.

## 4–6. Arm B — the fast-path guard, two mutation families, and one that defeats it

`AllocateNextAsync_OnceTheSequenceRowAlreadyExists_TheSecondCallNeverReadsOrders` is a real guard on the code as it ships:

- **B1, delete the behaviour** (`if (true || !sequenceRowAlreadyExists)`): **red** — `Assert.DoesNotContain() Failure: Filter matched in collection`, four logged commands with the seed `INSERT` among them.
- **B2, substitute a sibling identifier** (`AnyAsync(s => s.Id == 2)`): **red** — the pre-check can never be satisfied, the seed statement runs on every call, `dbo.orders` reappears on the wire.

**B3 is the finding.** With the gate defeated *exactly as in B1* — so the full 5,000-row scan happens on every allocation, the precise regression this feature exists to prevent — and the table written `FROM [dbo].[orders]` instead of `FROM dbo.orders`, the guard reports:

```
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1
```

That is defeat-list row 11 (*"write it in a form the instrument doesn't recognise"*) landing on a shipped guard. It matters beyond word-games: the plausible future regression here is not someone editing the literal, it is someone replacing the raw statement with EF-generated SQL (`db.Orders.MaxAsync(…)`), and **every** statement EF Core's SQL Server provider generates is bracket-quoted. The guard would stay green through exactly the rewrite most likely to reintroduce the scan. See **C1**.

**Record correction owed.** `progress/impl_order_number_allocator_scan_cost.md:171` states defeat-list row 11 is *"addressed directly … arm 2 is confirmed via EF Core's actual wire-level command log"*. Capturing the wire is necessary but not sufficient: the matcher over that capture is a single-spelling literal substring, and B3 shows it losing. The claim should be corrected, not deleted.

## 7–8. The scan-cost claim, verified independently of the implementer's instruments

I did not trust the reported plan numbers and did not reuse the test's EF client-side log. Instead: a throwaway `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04` container, a database created `READ_COMMITTED_SNAPSHOT ON`, the **real `OrdersDbContext` migrations** applied (not a hand-made lookalike schema), 5,000 real `dbo.orders` rows, and the **real `EfCoreOrderNumberAllocator`** driven through a scratch harness outside the repository. The instrument is server-side: `sys.dm_db_index_usage_stats` for `OBJECT_ID('dbo.orders')`, read between calls, encoded as `user_scans*1e6 + user_seeks*1e3 + user_lookups`.

```
orders rows: 5000
[before any allocation]                          orders index-usage code = 1000000     (my own COUNT(*) = 1 scan)
[after allocation 1 -> ORD-005001]               orders index-usage code = 2000000     (cold: +1 scan — the seed ran)
[after allocation 2 -> ORD-005002]               orders index-usage code = 2000000     (+0)
[after allocation 3 -> ORD-005003]               orders index-usage code = 2000000     (+0)
[after allocation 4 -> ORD-005004]               orders index-usage code = 2000000     (+0)
[after re-running the OLD unconditional stmt]    orders index-usage code = 3000000     (+1 scan)
```

Three consecutive steady-state allocations read `dbo.orders` **zero** times — no scan, no seek, no lookup — while the same counter moves by exactly 1 for the cold allocation and by exactly 1 for the pre-feature statement. The instrument carries its own positive control in the same run, so "0" here is a measured 0, not an instrument that never fires. `ORD-005001` on the cold call also confirms the seeding branch still computes `MAX + 1` correctly over a non-empty table.

**The "before" row count, from my own plan capture** (`SET STATISTICS XML ON` on the old unconditional statement, sequence row already present):

```
<RelOp NodeId="6" PhysicalOp="Index Scan" EstimateRows="5000" EstimatedRowsRead="5000" …>
  <Object Database="[otc47_review]" Schema="[dbo]" Table="[orders]" Index="[IX_orders_order_reference]" IndexKind="NonClustered" …>
  <RunTimeCountersPerThread Thread="0" ActualRows="5000" ActualScans="1" ActualLogicalReads="40" ActualRowsRead="5000" ActualExecutions="1" …>
```

This reproduces the implementer's figures **to the logical read** (40) and independently confirms its "what surprised me" note: the optimizer picks an `Index Scan` on the covering nonclustered `IX_orders_order_reference`, not the `Clustered Index Scan` feature 45's review saw. Acceptance bullet 3's two numbers — **5,000 rows scanned before, 0 after** — are therefore mine as well as theirs.

**One instrument of my own that I discarded, disclosed rather than smoothed.** My harness also counted seed-statement executions from `sys.dm_exec_query_stats` filtered by `st.text LIKE '%FROM dbo.orders%' AND st.text LIKE '%INSERT INTO dbo.order_number_sequences%'`. It incremented once per warm allocation, which would have contradicted the index-usage evidence — because the **monitoring query's own text contains both literals and counted itself**. Self-matching instrument, discarded; the index-usage numbers above stand on their own and disagree with nothing.

## 5 (brief item). The pre-check's correctness boundary, checked against the code rather than the comment

- **False negative (pre-check misses an existing/uncommitted row) must be harmless.** It is, and it is proved live, not documented: Arm A's failure originates at line 133, i.e. the fallthrough into the atomic statement executed under the exact two-session interleaving. With the fix intact the same test is green (`ORD-000001`), meaning caller B blocked on `WITH (UPDLOCK, HOLDLOCK)`, re-evaluated, found A's committed row and skipped its insert.
- **False positive (pre-check reports a row that then is not there) would break `SingleAsync`.** It cannot occur: RCSI never fabricates uncommitted rows, and nothing ever deletes the row. Enumerated, as a search result — `grep -rniE "delete[^a-z]*(from)?[^a-z]*(dbo\.)?order_number_sequences|OrderNumberSequences.Remove" src/ tests/` → **exit 1, zero hits**.
- **Could a transaction isolation level turn the advisory read into a hazard?** No `SNAPSHOT` transaction exists to worry about. Enumerated: `grep -rn --include=*.cs "BeginTransactionAsync(" src/` excluding `bin/`/`obj/` at the source → **8 of 8 call sites** pass `IsolationLevel.ReadCommitted` (`Orders/Infrastructure/Persistence/EfCoreUnitOfWork.cs`, `Orders/Infrastructure/Outbox/OutboxRelay.cs`, `Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs`, and the Billing/Fulfillment/Notifications equivalents). The pre-check is therefore a statement-level RCSI read, whose only failure direction is the harmless one.
- **A lock that used to be taken and now is not.** Before this feature every allocation took a U/range lock on `id = 1` via the seed statement's hinted existence check; in the steady state it no longer does. This does not weaken serialisation: the very next statement is the unchanged claiming `SELECT … WITH (UPDLOCK, ROWLOCK) WHERE id = 1`, held to commit inside the same ambient transaction, which is what makes allocation gap-free. The serialisation point moves a few statements later, and the number of distinct lock acquisitions on the one shared resource goes **down**, so no new deadlock cycle is reachable. `AllocateNextAsync_ConcurrentAllocationsYieldAGapFreeDuplicateFreeSequence` (24 concurrent callers over a pre-seeded row) is the test for exactly this and is green, unmodified.

## Acceptance bullets → evidence

`specs/shared/test-matrix.md` correctly has **no** row to flip: `grep -niE "allocator|order_number|ORD-######"` returns nothing, and no `R<n>` is claimed in the new test. Traceability is against the four bullets.

| Bullet (verbatim) | Named test / evidence I verified |
|---|---|
| "the `MAX(order_reference)` scan over `dbo.orders` is evaluated only when the sequence row does not yet exist, not on every allocation" | `AllocateNextAsync_OnceTheSequenceRowAlreadyExists_TheSecondCallNeverReadsOrders` (`tests/Orders.IntegrationTests/OrderNumberAllocatorTests.cs:266-317`), red under B1 and B2. Independently: probe 7's server-side index-usage deltas — cold +1 scan, three warm allocations +0. |
| "the seeding branch stays atomic — … two concurrent first-ever allocations safe, and feature 45's deterministic two-session guard must still pass" | Statement byte-identical (§1). `AllocateNextAsync_ConcurrentFirstEverAllocations_TheSecondCallerBlocksOnTheSeedLockInsteadOfRacingIt` green in baseline and in the 156/156 run; `…SixteenConcurrentFirstEverAllocations…` and `…AgainstATableAlreadyHoldingOrd000009…` green and untouched (the test diff is purely additive: one method plus two `using` directives). |
| "the actual execution plan is captured before and after, and the report states the row count scanned in each" | The report states 5,000 → 0; **I captured my own plan**: `ActualRows="5000" ActualScans="1" ActualExecutions="1"` on `[dbo].[orders].[IX_orders_order_reference]` before, and `dbo.orders` absent from the steady-state path after (probe 7, server-side). |
| "armed: the concurrency guard from feature 45 still fails when the lock hint is removed" | Arm A above: **3/3** verbatim-identical failures, armed binary verified by `strings -el`, restore verified by `cmp` + `md5sum`, green rebuild. |

## Condition, defects and advisories

**C1 (binding, must be discharged before this feature's commit; mechanical — route to `test_maintainer`, not to a rejection round).** `tests/Orders.IntegrationTests/OrderNumberAllocatorTests.cs:316` asserts `text.Contains("dbo.orders", StringComparison.OrdinalIgnoreCase)`. Replace the single-spelling literal with a matcher that catches any form of the table reference — `"orders"` case-insensitively is sufficient and safe here, because the only other Orders table any statement on this path names is `order_number_sequences`, which does not contain the substring `orders`. (The statements on this path are the `AnyAsync` pre-check, the claiming `SELECT … FROM dbo.order_number_sequences WITH (UPDLOCK, ROWLOCK)` and EF's `UPDATE` of that same table; under B1 a fourth, the seed `INSERT`, appears — that is the collection of four the armed failure printed.) Then **re-arm with probe B3**, not just B1: gate defeated *and* `FROM [dbo].[orders]`, expecting red. Ship the record correction to `progress/impl_order_number_allocator_scan_cost.md:171` in the same edit. **If the leader decides not to discharge C1 before the commit, it becomes a numbered backlog entry with these words as its acceptance bullets — it does not become a sentence in a review.** (I do not write `feature_list.json`'s backlog; this is the routing, and it names an owner.)

**A1 (non-blocking).** The armed failure message — `Assert.DoesNotContain() Failure: Filter matched in collection`, with the collection elided to four truncated strings — does not itself name the claim; only the test's *name* does. `CLAUDE.md`'s arming protocol wants the message to name the claim. A `Assert.True(!offenders.Any(), $"…still read dbo.orders: {string.Join(…, offenders)}")` shape would print the offending SQL. Worth folding into C1's edit while the file is open.

**A2 (non-blocking, record only).** The class-level doc comment at `OrderNumberAllocatorTests.cs:16` still ends *"Test-only — the allocator itself is not touched"*, and line 36 still `<see cref>`s a member that no longer exists. Both were already raised as feature 45's advisory A2 and routed to `test_maintainer`; they survived this feature untouched. Not this feature's defect, but this is the second review to find them, and the next `test_maintainer` pass over this file should clear them with C1.

**On the implementer's record.** Every load-bearing number in it that I could reproduce, I did, and they matched exactly — the plan's `ActualRows="5000"`/`ActualLogicalReads="40"`, the arm's failure text and its `line 133`, `156/156`, `50/50`, `dotnet format` exit 0. Including the non-obvious `Index Scan`-not-`Clustered Index Scan` observation, which is strong evidence the probe was genuinely run rather than reconstructed. The record is honest; the single correction it needs is the defeat-list row 11 claim (C1).

## Scope

`git status --porcelain` confirms this feature touched exactly: `src/Orders/Infrastructure/Persistence/EfCoreOrderNumberAllocator.cs`, `tests/Orders.IntegrationTests/OrderNumberAllocatorTests.cs`, the new `progress/impl_order_number_allocator_scan_cost.md`, and one `status` line in `feature_list.json`. The other modified paths in the tree (`.gitignore`, `package.json`, `quality.sh`, `progress/current.md`, `sonar-scan.properties`, `progress/impl_sonarqube_quality_gates.md`) belong to id 34 and id 107 and were neither reviewed nor attributed here. `specs/` and `n8n/` are clean (`git status --porcelain specs/ n8n/` → empty). `src/Orders/Infrastructure/Persistence/EfCoreOrderNumberAllocator.cs`'s mtime now reads **09:20:33**, which is *my* restore after arming, not an implementer write — recorded so the effort record is not misread.

## CHECKPOINTS walk

### C1 — harness
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all present; `progress/current.md` and `progress/history.md` present.
- [x] `.claude/agents/` holds the five agents, each declaring a model.
- [x] `./init.sh` exits **0** (run in this review, before the approval transition).
- [ ] **After** the transition it exits **1**, and this is the one red check left in the repository: `progress/current.md` still says *"id 47 … `in_progress`"* while `feature_list.json` now has no active feature. **Left for the leader, and required before anything else** — `current.md` is the leader's file (`CLAUDE.md`: the reviewer does not edit it), and this is the identical hand-off feature 45's review closed with. Reset it to its own template and re-run `init.sh`.

### C2 — state
- [x] At most one feature `in_progress` — `init.sh`: *no feature in_progress*; id 47 sits at `in_review` until this verdict flips it.
- [x] Every status in `rules.valid_status` (init.sh section 5).
- [x] Every `done` feature has passing tests: for id 47, `Orders.IntegrationTests` 156/156 and `Architecture.Tests` 50/50, run here.
- [x] `progress/current.md` describes the active session (the phase-21 brief naming id 47).
- [x] No `blocked` feature introduced.

### C3 — architecture
- [x] NetArchTest suite **run**, 50/50 — not eyeballed.
- [x] No cross-service database access: the change is one statement inside Orders' own `Infrastructure/Persistence`.
- [x] No new shared runtime code; no `Domain/` file touched, so no `OrderToCash.Cqrs` reach-in and no `SharedKernel` package added.
- [x] No `decimal` in domain arithmetic — nothing in the diff touches money.
- [x] Kafka-fact / NATS-RPC classification unaffected (no interaction added).
- [x] No stray debug logging; the one new comment block is substantive, not a context-free TODO.

### C4 — verification
- [ ] **`./quality.sh` not re-run in this review.** Deliberate and disclosed: `quality.sh` is itself modified in the working tree by id 34's in-flight work, so a run would measure that feature, not this one; and the implementer's own green run is recorded as *their* claim. What I ran instead, in full: `Orders.IntegrationTests` 156/156, `Architecture.Tests` 50/50, `dotnet format --verify-no-changes` exit 0, `init.sh` exit 0.
- [x] Domain tests unaffected and still pure (no `Domain/` file touched).
- [x] Integration tests use real Testcontainers MS-SQL — `MsSqlContainerFixture` with `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04` and `READ_COMMITTED_SNAPSHOT ON`, no mocks. My own probe used the same image and the real migrations.
- [ ] Coverage thresholds not re-measured here (they are id 34's subject this phase); this feature adds one integration test and no uncovered production branch beyond the `if`, which both arms exercise.
- [x] No Jest anywhere; xUnit only.

### C5 — session close
- [x] No suspicious untracked files attributable to id 47; my backup, probe harness, plan XML and logs live in the session scratchpad **outside** the repository, and my probe container (`otc47_review_mssql`) and database were removed (`docker rm -f`, confirmed).
- [x] `progress/history.md` entry appended at approval **with its effort record**.
- [x] `feature_list.json` reflects true state — id 47 `in_review` → `done` by editing **that one line**.
- [x] The human is told what was done and how to test it manually: `dotnet test tests/Orders.IntegrationTests --filter OrderNumberAllocatorTests`.
- [x] **Claude did not commit.** No `git commit`, `push`, `checkout`, `restore`, `stash` or `reset` at any point in this review.

### C6 — SDD
Not applicable: `sdd: false`. No `specs/order_number_allocator_scan_cost/` is expected and none exists; `init.sh`'s SDD coherence check is green.

### C7 — reuse fidelity
- [x] `specs/shared/` untouched (`git status --porcelain specs/` empty); `init.sh` reports byte-identical parity with #7 across 6 files.
- [x] No amendment, silent or otherwise.
- [x] No `R<n>` claimed, and `test-matrix.md` has no allocator row to claim — enumerated, zero hits.
- [x] `n8n/` and the API script untouched.
- [x] **Ported-idiom ledger present and checked, not merely present.** The record (`impl_…:49`) claims "None owed", citing #7's `order-number-allocator.ts:65-70` as running its `max(cast(substring(…)))` unconditionally too. I did not re-read #7's file in this review — feature 45's review did, with a file-and-line citation (`order-number-allocator.ts:71-78`, an unconditional `INSERT … ON DUPLICATE KEY UPDATE`), and the two citations agree on the property that matters: **#7 has no fast path, so #8 is not hand-building a property #7's engine supplied.** The "None owed" is therefore sound *in the direction that matters* — this feature removes work #7 still does, rather than dropping a guarantee #7 got free. Recorded as verified-by-inheritance, with the inherited citation named.
- [x] Effort record complete and honest, including that this feature has **no #7 counterpart** and is pure #8 cost.

## What must change before the commit

Only **C1** (and, while that file is open, **A1** and **A2**). Nothing in `src/` requires a change: the fix is correct, minimal, and the statement it protects is unmodified.
