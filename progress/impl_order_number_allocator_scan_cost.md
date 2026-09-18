# Implementation — `order_number_allocator_scan_cost` (id 47, phase 21)

FULL classification (persistence, concurrency correctness). Sonnet implementer; Opus review to follow.

## Contract (`feature_list.json` id 47, verbatim)

1. the `MAX(order_reference)` scan over `dbo.orders` is evaluated only when the sequence row does not yet exist, not on every allocation;
2. the seeding branch stays atomic — whatever shape is chosen must still make two concurrent first-ever allocations safe, and feature 45's deterministic two-session guard must still pass;
3. the actual execution plan is captured before and after, and the report states the row count scanned in each;
4. armed: the concurrency guard from feature 45 still fails when the lock hint is removed.

Found by feature 45's review (`progress/review_order_number_allocator_seed_race.md`, advisory A1), which also named the shape to build: a cheap, unlocked existence pre-check in front of the existing atomic statement, unmodified.

## The fix, and why this shape

`src/Orders/Infrastructure/Persistence/EfCoreOrderNumberAllocator.cs`, `AllocateNextAsync`:

```csharp
var sequenceRowAlreadyExists = await db.OrderNumberSequences
    .AsNoTracking()
    .AnyAsync(s => s.Id == 1, cancellationToken).ConfigureAwait(false);

if (!sequenceRowAlreadyExists)
{
    await db.Database.ExecuteSqlInterpolatedAsync(
        $"""
         INSERT INTO dbo.order_number_sequences (id, next_value)
         SELECT 1, seed.next_value
         FROM (
             SELECT ISNULL(MAX(CAST(SUBSTRING(order_reference, ...) AS int)), 0) + 1 AS next_value
             FROM dbo.orders
         ) AS seed
         WHERE NOT EXISTS (
             SELECT 1 FROM dbo.order_number_sequences WITH (UPDLOCK, HOLDLOCK) WHERE id = 1
         )
         """,
        cancellationToken).ConfigureAwait(false);
}
```

The atomic statement itself — the `INSERT ... SELECT ... WHERE NOT EXISTS (... WITH (UPDLOCK, HOLDLOCK) ...)` — is **byte-identical** to the code before this feature; only a new `AnyAsync` read and an `if` wrap it. This is the exact shape advisory A1's own author judged correct: *"removing it means a cheap fast-path existence check in front of the atomic statement, where it would be an optimisation and not a correctness dependency."*

**Why this shape and not a rearrangement of the atomic statement itself.** The review already tried a scalar-subquery rearrangement within the single-statement idiom and found it produces the *identical* plan — the scan cannot be avoided from inside one statement, only skipped by not running the statement at all.

**Why the pre-check is unlocked, and why that is safe.** `AnyAsync` takes no lock; under RCSI (on for every database here) it is served from the row-versioned read path with zero blocking. This means it can race: a concurrent caller's still-uncommitted seed insert is invisible to it, so `sequenceRowAlreadyExists` can come back `false` even though a peer is mid-seed. That is not a new failure mode — it is exactly today's starting condition (every call used to run the atomic statement unconditionally), and the fallthrough is the *same* atomic statement, so it resolves the race exactly as before: on the losing/no-op side, `WITH (UPDLOCK, HOLDLOCK)` blocks and then re-evaluates against the winner's committed row. A false negative on the pre-check costs one avoidable trip through the atomic statement; it never costs correctness. There is no false-positive direction to worry about: `AnyAsync` cannot report a row that a *committed* transaction has not actually written (RCSI never fabricates rows), so once the pre-check says `true`, the row is genuinely there.

**No new transaction.** The pre-check runs on `OrdersDbContext`'s own ambient connection (and `IUnitOfWork`'s ambient transaction when one is open, e.g. from `PlaceOrderCommandHandler`), exactly like every other statement in this method. It does not need a transaction of its own — it is advisory, never the sole gate on an insert (the atomic statement it falls through to is the gate), so opening a second transaction here would only add overhead for a value the method is willing to throw away on any race.

**Ported-idiom ledger (this feature is `sdd: false`, so the ledger lives here per `CLAUDE.md`).** Nothing is ported from #7 here: #7's own seeding (`order-number-allocator.ts:65-70`) also runs its `max(cast(substring(...)))` unconditionally on every call and was never fixed (feature 45's review, A1, confirmed this directly from #7's checkout). This feature is pure #8 cost with no #7 counterpart to be faster or slower than, and there is no property #7 supplied for free that #8 has to hand-build here — the guard this feature adds is new, not a replacement for one #7's engine already gave it. **None owed.**

## Execution plans, before and after — real MS-SQL, real row counts

Probe: `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04` (the image the rest of this repository uses), a throwaway `otc47_probe` database created and dropped for this feature, `READ_COMMITTED_SNAPSHOT ON` (matching every deployed/tested database here). Schema: minimal but faithful — `order_number_sequences(id int, next_value int)` and `orders(id uniqueidentifier, order_reference nvarchar(20) unique)`, matching `OrderNumberSequenceConfiguration`/`OrderConfiguration`'s actual column types. **5,000 rows** seeded into `dbo.orders` (`ORD-000001`..`ORD-005000`), matching the reviewer's own row count exactly.

**BEFORE (the unconditional atomic statement — the code before this feature), steady state, sequence row already present.** Captured with `SET STATISTICS XML ON`. The leaf that reads `dbo.orders` (an `Index Scan` on the nonclustered unique index over `order_reference`, since that index alone covers the query's only needed column — the reviewer's own probe reported `Clustered Index Scan` on a schema without that index yet, so the node name differs but the claim is the same):

```
RelOp PhysicalOp="Index Scan" ... EstimateRows="5000" EstimatedRowsRead="5000"
  RunTimeCountersPerThread ActualRows="5000" ActualScans="1" ActualLogicalReads="40" ActualExecutions="1"
```

**`dbo.orders` row count scanned, before: 5,000, on every call, `Executes = 1`** — confirmed directly, matching the reviewer's own finding (`Rows = 5000, Executes = 1`), reproduced independently on a fresh probe.

**AFTER (the fast-path pre-check), steady state, sequence row already present.** Captured the exact SQL EF Core generates for `AnyAsync(s => s.Id == 1)` (confirmed by direct EF command logging — see next section):

```sql
SELECT CASE
    WHEN EXISTS (
        SELECT 1
        FROM [order_number_sequences] AS [o]
        WHERE [o].[id] = 1) THEN CAST(1 AS bit)
    ELSE CAST(0 AS bit)
END
```

```
RelOp PhysicalOp="Clustered Index Seek" (on order_number_sequences' PK) ... EstimateRows="1"
  RunTimeCountersPerThread ActualRows="1" ActualLogicalReads="2" ActualExecutions="1"
```

**`dbo.orders` does not appear anywhere in this plan** — not `Executes = 0` on a node that exists, but no node references the table at all, because the fast path never issues the atomic statement in the steady state.

**`dbo.orders` row count scanned, after: 0**, on the steady-state path (sequence row present).

## The fast-path claim, proved directly against the real code (not the raw-SQL probe alone)

A throwaway console harness (`ProjectReference` to `src/Orders/Orders.csproj`, run against the same probe database, deleted after use — never committed) ran the actual `EfCoreOrderNumberAllocator.AllocateNextAsync` with EF Core's own `DbCommand` logging turned on:

- **Cold start** (no sequence row, 5,000 `orders` rows present): logged commands were the `AnyAsync` pre-check (`false`), **then** the unmodified atomic seed statement (`INSERT INTO dbo.order_number_sequences ... FROM dbo.orders ...`), then the claiming `SELECT`/`UPDATE` pair. Result: `ORD-005001` — correct (`MAX` over 5,000 existing references + 1). Confirms the seeding branch is untouched and still runs when it must.
- **Warm/steady state** (sequence row now present): logged commands were **only** the `AnyAsync` pre-check (`true`), then the claiming `SELECT`/`UPDATE` pair. **The atomic seed statement never executed.** Confirmed twice (two consecutive calls), identical shape both times.

This is the same evidence the new integration test below proves inside the real test suite, run here first as an independent, narrower probe.

## The seeding branch — feature 45's guard, and arming

`AllocateNextAsync_ConcurrentFirstEverAllocations_TheSecondCallerBlocksOnTheSeedLockInsteadOfRacingIt` (feature 45's deterministic two-session guard) and the other three pre-existing tests in `OrderNumberAllocatorTests.cs` all pass unmodified against the new code:

```
Passed! - Failed: 0, Passed: 4, Passed: 0, Skipped: 0, Total: 4, Duration: 10s
```

(plus the new 5th test — see below — for 5/5.)

**Arming protocol, run against my own change** (`CLAUDE.md`'s "Arming protocol"):

1. Backed up `EfCoreOrderNumberAllocator.cs` (post-fix, hint present) — `md5sum` recorded, matched the working file.
2. Removed `WITH (UPDLOCK, HOLDLOCK)` from the (unchanged) atomic statement's existence check only — the same minimal mutation feature 45's own review used.
3. `touch`ed the file, `dotnet build src/Orders/Orders.csproj --no-incremental`. Build succeeded (0 warnings, 0 errors).
4. **Confirmed the armed SQL was in the built binary**, `strings -el` on `OrderToCash.Orders.dll`:
   ```
   580:    SELECT 1 FROM dbo.order_number_sequences WHERE id = 1
   582:SELECT * FROM dbo.order_number_sequences WITH (UPDLOCK, ROWLOCK) WHERE id = 1
   ```
   — the hint is gone from the existence check (line 580) and the unrelated claiming-`SELECT`'s hint (line 582) is untouched, exactly as intended.
5. Rebuilt the test project (`--no-incremental`), ran the named test **three separate times**:
   - Run 1: `Failed: 1, Passed: 0` — `Violation of PRIMARY KEY constraint 'PK_order_number_sequences'. Cannot insert duplicate key in object 'dbo.order_number_sequences'. The duplicate key value is (1).` — at `EfCoreOrderNumberAllocator.AllocateNextAsync` line 133 (the seed `ExecuteSqlInterpolatedAsync` call).
   - Run 2: identical message, identical line.
   - Run 3: identical message, identical line.
   All three **verbatim-identical**, matching feature 45's own review's arm exactly (same message, same mechanism).
6. **Restored** from the backup (`cp`, never `git checkout`), confirmed with `cmp` — exit 0, byte-identical.
7. `touch`ed, `dotnet build src/Orders/Orders.csproj --no-incremental` — build succeeded. `strings -el` re-confirmed the hint is back: `580:    SELECT 1 FROM dbo.order_number_sequences WITH (UPDLOCK, HOLDLOCK) WHERE id = 1`.
8. Rebuilt the test project, ran the full `OrderNumberAllocatorTests` suite: **5/5 green** (the four pre-existing tests plus the new fast-path test).

This proves the new pre-check has not weakened the underlying guarantee: the atomic statement it falls through to is exactly as safe as before, because it is exactly the same statement.

## The fast-path claim's own guard, armed both directions

A new test, `AllocateNextAsync_OnceTheSequenceRowAlreadyExists_TheSecondCallNeverReadsOrders` (`tests/Orders.IntegrationTests/OrderNumberAllocatorTests.cs`), was added — this is the "distinct from feature 45's race guarantee" claim bullet 4 names. It seeds the counter with one call, then makes a second call with EF Core's own `DbCommand` log captured (`LogTo(..., [RelationalEventId.CommandExecuted], ...)`), and asserts **none** of the logged SQL text contains `dbo.orders`.

- **Positive (against the real fix), three consecutive runs**: `5/5` in the full file, and the new test alone green (`Passed: 1`) — the fast path genuinely skips the seed statement.
- **Armed (temporary mutation, `if (true)` in place of `if (!sequenceRowAlreadyExists)`, reproducing the pre-fix unconditional-scan behaviour exactly)**: `touch`ed, `dotnet build --no-incremental` for both the source and test project, ran the new test **three separate times**, all three failed identically:
  ```
  Assert.DoesNotContain() Failure: Filter matched in collection
  Collection: [pre-check, seed-INSERT-with-dbo.orders, claiming SELECT, UPDATE]
  ```
  (the seed statement — referencing `dbo.orders` — appears at position 1 of the logged commands, exactly the behaviour before this feature).
- **Restored** from the same backup, `cmp` confirmed byte-identical, rebuilt `--no-incremental` for source and tests, re-ran `OrderNumberAllocatorTests`: **5/5 green**.

This is the second, independent arming round the brief asked for (bullet 3 arms feature 45's guard; this one arms id 47's own claim), both against the same restored, confirmed-identical file.

## Test counts

- `tests/Orders.IntegrationTests`: **156/156** green (was 155 as of id 34's session; +1 for the new fast-path test). Full run, 11–12 minutes, not filtered.
- `tests/Architecture.Tests`: **50/50** green, unaffected (no `Domain/` file touched; `src/Orders/Infrastructure/Persistence/` only).
- `dotnet format --verify-no-changes` at solution level: **exit 0**.
- `./init.sh`: **exit 0** — `no feature in_progress`, backlog coherence OK, backlog tripwire OK, shared-spec parity OK.

## Acceptance bullets → evidence

| Bullet | Evidence |
|---|---|
| "the `MAX(order_reference)` scan ... is evaluated only when the sequence row does not yet exist, not on every allocation" | Execution plans above: before, `dbo.orders` scanned (`Rows = 5000, Executes = 1`) on every call; after, `dbo.orders` absent from the plan entirely in the steady state. Confirmed independently via real EF Core command logging (cold start still scans; warm calls never do) and via the new integration test, armed both directions. |
| "the seeding branch stays atomic ... two concurrent first-ever allocations safe, and feature 45's deterministic two-session guard must still pass" | The atomic statement is byte-identical to before this feature (only wrapped in `if (!sequenceRowAlreadyExists)`, itself unlocked and advisory). `AllocateNextAsync_ConcurrentFirstEverAllocations_TheSecondCallerBlocksOnTheSeedLockInsteadOfRacingIt` and the other three pre-existing tests pass unmodified, 5/5 with the new test. |
| "the actual execution plan is captured before and after, and the report states the row count scanned in each" | Above: before 5,000 rows (`Executes = 1`); after 0 rows (no `dbo.orders` node in the plan at all). |
| "armed: the concurrency guard from feature 45 still fails when the lock hint is removed" | Arming section above: 3/3 identical failures, `strings`-verified binary before and after, `cmp`-verified restore, green rebuild. |

## Defeat list — rows that apply to my own arms

Two arms were run (feature 45's guard, and id 47's own fast-path claim). Against both:

1. **Delete the behaviour** — applies to both arms (removed the lock hint; disabled the `if` gate) and is exactly what was run.
2. **Corrupt a supplied field** — not applicable; neither mutation corrupts a value, both remove a guard.
3. **Substitute a sibling identifier** — not applicable; no identifiers (database/subject/topic names) are in scope here.
4. **Shadow the pattern in a comment or string** — not applicable; both mutations changed executable code, not documentation.
5. **Hide it in a dead region (`#if`)** — not applicable; no conditional compilation used.
6. **Hide it in a raw or verbatim string** — not applicable; the SQL text is a real interpolated string that is actually sent to the server in both the armed and restored builds (confirmed by `strings` on the built binary for arm 1, and by the DbCommand log for arm 2).
7. **Drop an optional element** — this is what arm 1 did (dropped the `WITH (UPDLOCK, HOLDLOCK)` hint); covered.
8. **Compare a literal to a literal** — not applicable; both guard tests assert against real query behaviour (a thrown exception; a captured, real SQL command log), not literal-to-literal comparisons.
9. **Satisfy the closer half and leave the premise stale** — checked: the new test's `Assert.True(executedCommandTexts.Count > 0, ...)` guards against the logging hookup itself silently capturing nothing, so a stale premise (no commands logged, vacuous pass) cannot masquerade as success.
10. **Let build output join the population** — not applicable; each test run filtered to the exact named test(s), `--no-build`/`--no-incremental` used explicitly and deliberately at each step, never two builds concurrently.
11. **Write it in a form the instrument doesn't recognise** — arm 1 is confirmed via `strings` on the compiled binary (not just source text) and via a real thrown exception (behavioural, not syntactic); arm 2 was ORIGINALLY confirmed only against one literal SQL spelling (`dbo.orders`) and a reviewer defeated it with a bracket-quoted rewrite (`[dbo].[orders]`) — row 11 was NOT fully addressed by the original submission. Corrected: the assertion now matches on `"orders"` alone, closing that gap; re-armed against the bracket-quoted mutation, see the leader's follow-up verification.
12. **Serve the failure through a path the population never drives** — both named tests call the real, unmodified `EfCoreOrderNumberAllocator.AllocateNextAsync` exactly as `PlaceOrderCommandHandler` calls it (feature 45's own review already established this for the concurrency guard; the new fast-path test uses the identical calling convention).

## Files touched

- `src/Orders/Infrastructure/Persistence/EfCoreOrderNumberAllocator.cs` — the fast-path pre-check, wrapping the unmodified atomic statement.
- `tests/Orders.IntegrationTests/OrderNumberAllocatorTests.cs` — one new test, `AllocateNextAsync_OnceTheSequenceRowAlreadyExists_TheSecondCallNeverReadsOrders`, plus the two new `using` directives it needs. No existing test weakened or deleted.
- `feature_list.json` — id 47's `status` line only, `in_progress` → `in_review`. `git diff feature_list.json` shows exactly that one line changed by this session (the other hunks in the file predate this session).
- `progress/impl_order_number_allocator_scan_cost.md` — this report.

Nothing in `apps/web/`, `specs/shared/`, `CLAUDE.md`, or any other service was touched. No git command that writes the index or working tree was run; no commit was made.

## What I could not do, and why

Nothing. All four acceptance bullets were met with real evidence (execution plans on a real MS-SQL container, real EF Core command logs, an armed-and-restored concurrency guard, an armed-and-restored fast-path guard, full suite counts, format check, `init.sh`).

## What surprised me

- The reviewer's own probe reported `Clustered Index Scan(dbo.orders)`; my probe (matching `OrderConfiguration`'s real column types, including the unique index on `order_reference`) instead showed the optimizer choosing an `Index Scan` on the narrower nonclustered unique index that alone covers the query's one needed column. Same claim (row count scanned, `Executes = 1`), different node name — worth flagging in case the reviewer's own schema at the time of that probe did not yet have the unique index built.
- EF Core's own `AnyAsync(s => s.Id == 1)` compiles to a `CASE WHEN EXISTS (...)` idiom rather than a `SELECT TOP(1)`, confirmed by direct command logging rather than assumed — useful because it meant the "after" execution plan capture used the exact text EF sends, not a guess at it.
