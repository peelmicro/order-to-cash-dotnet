# Feature 45 — `order_number_allocator_seed_race`

Status: implementation complete, `feature_list.json` transitioned to `in_review`.

## The bug and the fix

`src/Orders/Infrastructure/Persistence/EfCoreOrderNumberAllocator.cs`'s self-seed
was check-then-act (`IF NOT EXISTS (SELECT 1 ...) BEGIN INSERT ... END`) as two
engine-level operations: an unlocked read, then a conditional write. Under
`READ_COMMITTED_SNAPSHOT` — on for every database, see
`infra/mssql/init/01-create-databases.sql` — the plain read takes no lock at
all, so two genuinely concurrent first-ever callers can both see "not exists"
as true before either commits, and the loser's `INSERT` fails with a
primary-key violation on `order_number_sequences`.

**Fix chosen:** a single atomic statement —

```sql
INSERT INTO dbo.order_number_sequences (id, next_value)
SELECT 1, seed.next_value
FROM (
    SELECT ISNULL(MAX(CAST(SUBSTRING(order_reference, ...) AS int)), 0) + 1 AS next_value
    FROM dbo.orders
) AS seed
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.order_number_sequences WITH (UPDLOCK, HOLDLOCK) WHERE id = 1
)
```

`WITH (UPDLOCK, HOLDLOCK)` forces a real lock (a key-range lock on the empty
slot where `id = 1` would sit) regardless of RCSI — table hints override the
ambient row-versioned read and take an actual lock, which is SQL Server's own
documented "insert if not exists" idiom. `HOLDLOCK` holds that lock until the
ambient transaction ends, so a second caller's own existence check blocks
behind the first rather than racing it, and re-evaluates once the first
caller's insert has committed (finds the row, skips its own insert) or rolled
back (still missing, inserts). The aggregate is computed in a one-row derived
table rather than inline in the outer `WHERE`, because an aggregate with no
`GROUP BY` always returns exactly one row even over an empty `FROM` — folding
`WHERE NOT EXISTS` into the aggregating query directly would not suppress the
insert once the row exists, it would attempt one (and fail) on every single
call from the second onward.

**Only the seeding branch changed.** The allocation path's own
`WITH (UPDLOCK, ROWLOCK)` claim (feature 15's, correct, out of scope) is
untouched — confirmed by diff and by
`AllocateNextAsync_AgainstATableAlreadyHoldingOrd000009_ContinuesAtOrd000010`
and `AllocateNextAsync_ConcurrentAllocationsYieldAGapFreeDuplicateFreeSequence`
(pre-existing row concurrency claim) still passing unmodified.

## Why this fix, against the two rejected

- **Rejected: `MERGE ... WITH (HOLDLOCK)`.** Looks like the canonical MS-SQL
  upsert, but `MERGE` has a well-documented optimizer gap where the matching
  and the insert are not always serialised the way `HOLDLOCK` implies, and
  concurrent `MERGE` statements against the same key are known to still throw
  duplicate-key violations in practice (this is the exact same failure mode
  this feature exists to close — reaching for the statement most associated
  with it is the wrong lesson to draw).
- **Rejected: attempt the insert, catch 2627/2601, retry.** Correct in
  outline, but turns one SQL statement into C# control flow with a retry
  loop, and its correctness depends on `XACT_ABORT` being `OFF` (the default)
  so a caught statement-level error leaves the ambient transaction
  continuable — an ambient session setting to depend on silently rather than
  a property of the statement itself.
- **Under this database's snapshot isolation:** what's "on for all four
  databases" here is `READ_COMMITTED_SNAPSHOT` (RCSI), a database-level
  default for *un-hinted* reads — not per-transaction `SNAPSHOT` isolation.
  Explicit locking hints (`UPDLOCK`, `HOLDLOCK`) always force real locks
  regardless of RCSI, which is exactly why the chosen fix works. True
  `SNAPSHOT` isolation transactions can conflict with locking hints instead
  of blocking, but nothing here opens a transaction at that level —
  `IUnitOfWork` and every test use `ReadCommitted` — so this is a boundary
  the fix does not need to cross.

Full reasoning is recorded as an XML doc `<remarks>` block on
`EfCoreOrderNumberAllocator` itself, so it survives independently of this
report.

## The lost-in-translation cause (as instructed, not re-derived — one sentence)

This is the **third** time in this build that something safe in #7's idiom
(`INSERT ... ON DUPLICATE KEY UPDATE`, atomic because the statement itself is
the unit of atomicity) lost its safety when rendered as MS-SQL's two-statement
`IF NOT EXISTS ... INSERT`, joining JSON key ordering and money column widths
on the list of things that looked like a faithful translation and were not.

## The inverted test, and how it was armed — honestly

`AllocateNextAsync_ConcurrentFirstEverAllocations_CanRaceTheSelfSeedInsertAndFail`
is renamed to
`AllocateNextAsync_ConcurrentFirstEverAllocations_TheSecondCallerBlocksOnTheSeedLockInsteadOfRacingIt`
and its assertion inverted, per the acceptance bullet's explicit instruction
("INVERTED, not deleted").

**The obvious inversion was rejected.** "Sixteen concurrent first-ever
allocations all succeed" is deterministic once fixed but only *probabilistic*
as a detector of the bug — reverting the fix makes a failure *likely*, not
*certain*, which is exactly the trap called out (a 1-in-12 reproduction was
rejected as proof by a previous feature's review). I still wrote that test
(`AllocateNextAsync_SixteenConcurrentFirstEverAllocations_AllSucceedWithDistinctReferences`)
because the acceptance bullet names it literally, but I do **not** rely on it
for arming, and its doc comment says so.

**The deterministic signal used instead:** two explicit sessions, one
interleaving forced as a *checked fact*, not a timing hope.

1. Session A (raw ADO, not the allocator) inserts the counter row directly
   inside its own open, uncommitted transaction — occupying the exact state a
   genuinely concurrent first caller is in mid-seed. SQL Server always takes
   a real exclusive lock on a newly `INSERT`ed key regardless of RCSI (row
   versioning governs reads, never writes), so from the moment the `INSERT`
   returns, session A is *demonstrably* holding the row.
2. Session B calls the real, unmodified `AllocateNextAsync` inside its own
   ambient transaction, started but not yet awaited.
3. The test polls `sys.dm_exec_requests` until it **observes** session B
   reported as `blocking_session_id`-blocked by session A (via
   `SqlConnection.ServerProcessId` on each), bounded by a 10s timeout that
   fails the test loudly rather than silently passing on an interleaving that
   never happened. This is the fact that replaces the hope.
4. Only then does the test commit session A and await session B.

With the bug present, B is blocked either way (on its own unlocked read
finding "not exists" true, then blocking on its own `INSERT` against the
still-locked key), and once A commits, B's blocked `INSERT` resolves against
a now-committed duplicate key and throws — **every** time, because step 3
forces B to already be at that exact point before A is ever allowed to
commit. With the fix, B blocks on its own `WITH (UPDLOCK, HOLDLOCK)` check
instead, which re-evaluates once A's lock releases, finds the row A
committed, and skips its insert — no exception, ever, under the same forced
sequence. That is a change in **kind**, not likelihood.

**Arming run, verbatim.** Reverted `EfCoreOrderNumberAllocator.cs` to
feature 15's `IF NOT EXISTS ... INSERT` (backup taken first, restored from
that backup after, `touch`ed + `dotnet build --no-incremental` on both the
`Orders` and `Orders.IntegrationTests` projects before every run — never
`git checkout --` on this file). Ran the inverted test **three times** against
the armed binary:

```
Microsoft.Data.SqlClient.SqlException : Violation of PRIMARY KEY constraint
'PK_order_number_sequences'. Cannot insert duplicate key in object
'dbo.order_number_sequences'. The duplicate key value is (1).
The statement has been terminated.
Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1
```

Failed 3/3, identical error, identical location
(`EfCoreOrderNumberAllocator.AllocateNextAsync` line 107, the seed
`ExecuteSqlInterpolatedAsync` call). Restored from the pre-arming backup,
confirmed byte-for-byte identical with `diff` (`RESTORE MATCHES BACKUP BYTE
FOR BYTE`), forced a rebuild, then ran the confirming green suite:
`OrderNumberAllocatorTests` 4/4 passed. The deterministic test alone was also
run 5 times before arming and 3 times after restoring, all green, ~2-3s each
— reliable, not flaky.

## Test counts

| | Before | After |
|---|---|---|
| `OrderNumberAllocatorTests` | 3 | 4 (one renamed+inverted, one new) |

Full suite (`quality.sh`): all projects green both before and after (the
feature-45 diff is isolated to `EfCoreOrderNumberAllocator.cs` and
`OrderNumberAllocatorTests.cs`; nothing else in `src/` or `tests/` changed).

## `specs/shared/test-matrix.md`

Not touched. Grepped for any row referencing `OrderNumberAllocator`,
`order_number`, or this feature — none exists. This is a defect fix in
already-closed feature 15 under `sdd: false`; the specification of record is
`feature_list.json` id 45's acceptance bullets, which name no `R<n>`, so
there is no row to flip from `TODO`.

## `./quality.sh` / `./init.sh`

- `./quality.sh`: **green**. `dotnet format --verify-no-changes: clean`,
  `dotnet build: succeeded`, `dotnet test: all tests passed` (every project,
  including `Orders.IntegrationTests` 65/65), coverage summary reported
  per-assembly with no gate breach.
- `./init.sh`: **green**, both before starting and after closing out
  (`feature_list.json parsed — 45 features`, `no feature in_progress`,
  `progress/current.md is in lockstep with the backlog`).

## A mistake made and corrected, in the interest of honesty

While transitioning `feature_list.json`'s status field, I used
`json.load`/`json.dump` in Python rather than a targeted string edit. Two
problems resulted: (1) `json.dump`'s default `ensure_ascii=True` re-escaped
every em-dash in the *entire file* to `—`, a much wider diff than
intended; (2) on noticing this, I ran `git checkout -- feature_list.json` to
undo it — which is explicitly forbidden in this repository's conventions,
and for exactly the reason it bit here: the file was mid-session and
uncommitted, so the checkout silently reverted legitimate prior state
(features 42 and 44 marked `done`, feature 46 added) back to a stale commit,
not just my own change.

I caught this from `git diff`'s output (captured earlier in the same
session, before the checkout) and reconstructed the lost state precisely
from that diff text — restoring feature 42 to `done` and re-adding feature
46's block verbatim, then applying only the one authorised change (feature
45 to `in_review`) via targeted `Edit` calls, not another full-file rewrite.
Verified by `python3 -c "json.load(...)"` (valid JSON), `git diff` (showing
exactly the three intended semantic changes and nothing else), and `./init.sh`
(green, `progress/current.md is in lockstep with the backlog`, correct
`19/45 done` / `45 features` counts matching the pre-mistake state). No
source or test file was affected by this incident — it was contained to
`feature_list.json`, and it is fully recovered.

## Files touched

- `src/Orders/Infrastructure/Persistence/EfCoreOrderNumberAllocator.cs` — the fix
- `tests/Orders.IntegrationTests/OrderNumberAllocatorTests.cs` — inverted test (renamed) + new literal-acceptance test
- `feature_list.json` — status `pending` → `in_review` for id 45 (plus recovery of ids 42/46 per above)
- `progress/impl_order_number_allocator_seed_race.md` — this file
