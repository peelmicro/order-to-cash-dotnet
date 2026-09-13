# impl: operator_cancel_races_saga_forward_progress (id 62, phase 14)

## Reproduction (bullet 0) — units and orderings

The race is a two-party race between two independent transactions reading/writing the SAME
order row: **CancelOrderCommandHandler**'s own read+enqueue, and **SagaFactHandler**'s
processing of a competing, genuinely unrelated fact for the SAME order. Two branches, two
orderings each, driven at the exact unit CLAUDE.md names (not a coarser one), all in
`tests/Orders.IntegrationTests/OperatorCancelRacesSagaForwardProgressTests.cs`, over the REAL
Kafka/NATS/MS-SQL stack — the isolations `OrdersCancelAcceptanceTests.cs` deliberately builds
(no `credit.hold` responder pinning `stock_reserved`; no `despatch.create` responder pinning
`confirmed`) are REMOVED here: every responder the natural chain needs is started, and the
SECOND, competing fact is published by hand at the moment each test chooses.

| Branch | Ordering | Test | Pre-fix outcome |
|---|---|---|---|
| A (`stock_reserved`) | saga forward progress first | `BranchA_StockReserved_SagaForwardProgressFirst_CreditApprovedIsSupersededAndStockReleaseStillCompletesTheCancellation` | `credit.approved.v1` advances the order to `confirmed`; the later, genuine `stock.released.v1` finds status `confirmed` ≠ `stock_reserved` and is ignored — stranded at `confirmed`, `cancellation_reason: NULL` |
| A | operator decision first | `BranchA_StockReserved_OperatorDecisionFirst_CreditApprovedArrivingAfterCancellationIsIgnoredByPreconditionUnmet` | control — no bug; R25's original path |
| B (`confirmed`) | saga forward progress first | `BranchB_Confirmed_SagaForwardProgressFirst_DespatchedIsSupersededAndTheCompensationChainStillCompletesTheCancellation` | **#7's literal Finding 1**: `order.despatched.v1` advances to `despatched`; the two-hop compensation (`credit.release` then `stock.release`) never lands — stranded at `despatched`, `cancellation_reason: NULL`, a `saga_commands` row effectively parked forever |
| B | operator decision first | `BranchB_Confirmed_OperatorDecisionFirst_DespatchedArrivingAfterCancellationIsIgnoredByPreconditionUnmet` | control — no bug |

Verbatim pre-fix failures (captured by removing the fix — see Arming table below), run against
the reproducing tests themselves:

- Branch A, forward-progress-first: `expected a 'superseded' saga_ignored_facts row for
  credit.approved.v1 on order 947f1249-e64d-4b89-92aa-fb5e751a98dd — none appeared within
  00:00:20. If this fails, the forward-progress fact was allowed to advance the order instead.`
- Branch B, forward-progress-first: `expected a 'superseded' saga_ignored_facts row for
  order.despatched.v1 on order 4a605172-7124-492e-ba7e-e4eba781af31 — none appeared within
  00:00:20. If this fails, the forward-progress fact was allowed to advance the order to
  despatched instead — #7's own Finding 1.`

Both control tests (operator-decision-first) pass identically whether the fix is present or not
— they exercise R25's *original*, unmodified `precondition_unmet` path, never the new guard.

## The defence (bullet 1) — two parts, and why one alone is not enough

Two independent mechanisms are needed because the window has two different shapes:

1. **A true-concurrency window** (two DB transactions genuinely overlapping in wall-clock time)
   — closed by a **row lock**.
2. **A wide, non-concurrent window** (the saga's own forward-progress command — e.g.
   `despatch.create` — was already dispatched BEFORE the operator ever cancelled, and its reply
   can arrive seconds later, long after the operator's own transaction has committed and
   released any lock) — a lock cannot close this; nothing is contended. Closed by making
   forward-progress facts **check for, and defer to, a pending compensation**.

### Part 1 — the row lock

`EfCoreOrderRepository.GetByIdAsync` now issues, before its tracked read:

```sql
SELECT TOP (1) 1 AS Value FROM dbo.orders WITH (UPDLOCK, ROWLOCK) WHERE id = @id
```

via EF Core's `Database.SqlQuery<int>(FormattableString)`. Every caller of `GetByIdAsync` in
this codebase (`CancelOrderCommandHandler`, `SagaFactHandler`, `SagaFirstParkDeadLetterHandler`)
loads the order to mutate it inside the SAME ambient transaction — never a pure display read —
so locking unconditionally here is never a wider lock than the existing call sites already
imply.

**Isolation in force**: `EfCoreUnitOfWork` opens every transaction at `IsolationLevel.ReadCommitted`
explicitly, and the database runs with **`READ_COMMITTED_SNAPSHOT ON`** (RCSI) — meaning a
*plain* `SELECT` under `ReadCommitted` reads a row-versioned snapshot and never blocks on
another writer's held locks. That is exactly the gap: two independent transactions could each
read a CONSISTENT-BUT-DIFFERENT-FROM-THE-OTHER snapshot of the same row and both proceed,
unaware of each other. `WITH (UPDLOCK, ROWLOCK)` is an explicit locking hint that takes
precedence over RCSI's row-versioning for that one statement — the second caller requesting the
same row's `UPDLOCK`/`X` lock **waits**, and reads the fresh, post-commit row once granted.
Deliberately **no `READPAST`** (unlike `EfCoreSagaCommandStore.ClaimDueAsync`'s own `UPDLOCK`,
which is measured to skip rather than block): a losing writer on the SAME order must wait for
the truth, never silently skip its own order's row.

**Throughput cost.** `ROWLOCK` scopes the cost to the ONE contended row — no other order's
throughput is affected. For the uncontended case (the overwhelming majority: no concurrent
writer on the same order), the added cost is one extra single-row point lookup by clustered
index (`id` is the PK) — **an estimate, "well under a millisecond," not a measured figure**
(review round 1, A6: corrected here — `OperatorCancelRowLockConcurrencyTests` measures the
CONTENDED case's blocking, not the uncontended case's overhead, and nothing in this feature's
suite isolates the lock hint's own added cost from the pre-existing `SingleOrDefaultAsync`
read it wraps). What IS measured directly, over a real MS-SQL Testcontainer
(`OperatorCancelRowLockConcurrencyTests`): the contended case (second caller genuinely blocked)
resolves in ~2s only because the TEST deliberately holds the first transaction open for 2s to
force provable overlap — this shows the lock serialises correctly, not what it costs when
uncontended. Bullet 1 asks only for the cost to be STATED, which this now does honestly, as an
estimate.

**Both orderings, proven on the lock itself** (bullet 1's own two-party rule) —
`OperatorCancelRowLockConcurrencyTests.ConcurrentGetByIdAsync_ForTheSameOrder_TheSecondArrivalWaitsForTheFirstsCommit_NeitherDeadlocks`,
a `[Theory]` with both role assignments (`CancelOrderCommandHandler`-shaped caller first /
`SagaFactHandler`-shaped caller first — the SQL mechanism is symmetric, so this states that
symmetry explicitly rather than leaving it assumed): the SECOND caller's own request is
DELIBERATELY issued while the first still holds the lock (proven by a `>500ms` gap check, not
merely inferred), and the second caller's `GetByIdAsync` only returns AFTER the first commits.
**No SQL error 1205** (deadlock) in either ordering — structurally impossible here since both
callers only ever acquire ONE lock on ONE row, no second contended resource in reverse order.

### Part 2 — supersede forward progress once compensation is pending

`ISagaCommandStore.HasPendingCompensationAsync(orderId)` — a bare `EXISTS`-shaped `AnyAsync`
over `saga_commands` for `credit.release`/`stock.release` rows for the order (no status filter:
even a `sent` or `rejected` row still means "a compensation was requested"), using the SAME
covering unique index `(order_id, command)` the store already has — cheap, sub-millisecond.

`SagaFactHandler.HandleAsync`, for every `SagaStep.Advance` (genuine forward progress) EXCEPT
`credit.released.v1`'s own two `Advance` variants (which ARE the compensation's own first hop —
blocking them would strand the compensation against itself), now checks this AFTER the
precondition-matched branch and BEFORE `ApplyStepAsync`. If a compensation is pending, the fact
is recorded `SagaIgnoredFactMarker.Superseded` and never applied — no status change, no
`CommandAfter` dispatch.

This check runs AFTER this transaction's own `UPDLOCK` read of the order row, so a concurrent
`CancelOrderCommandHandler` enqueue racing the exact same moment is either already visible (it
committed first, unblocking this read) or this call is the one blocked waiting for that
transaction to finish — the two parts compose: the lock makes the EXISTS-check's own snapshot
trustworthy in the narrow window; the EXISTS-check itself closes the wide window the lock alone
cannot reach.

**Ledger row** (id 62 is a #8-only mechanism — #7 built nothing here):

| Property | #7 relied on | #8 supplies it via | Guard |
|---|---|---|---|
| Nothing serialises the operator decision against saga forward progress | Nothing — #7 disclosed the race live (`orders-cancel.integration.spec.ts:70-82`/`:138-152` isolate it, never test it) and filed no fix | `EfCoreOrderRepository.GetByIdAsync`'s `UPDLOCK, ROWLOCK` | `OperatorCancelRowLockConcurrencyTests.ConcurrentGetByIdAsync_ForTheSameOrder_TheSecondArrivalWaitsForTheFirstsCommit_NeitherDeadlocks` — armed, see below |
| Forward progress does not strand an already-pending compensation | Nothing — same disclosure, same non-fix | `SagaFactHandler`'s new `HasPendingCompensationAsync` guard | `BranchA_..._SagaForwardProgressFirst_...` / `BranchB_..._SagaForwardProgressFirst_...` — armed, see below |

#7 citation, file-and-line: `apps/orders/src/orders-cancel.integration.spec.ts:70-85` (the
`startFulfillmentOnlyResponder` doc comment) states explicitly: *"Without this, the real saga's
own fast path races the operator's cancel decision... an entirely real race in production too,
just compressed to zero latency by a stub that never blocks."* No lock, no supersede check, no
test of the race itself exists anywhere in that checkout — confirmed by content enumeration
below.

## R25 still correctly ignores genuinely stale facts (bullet 2)

The "operator decision first" control tests (both branches) drive the SAME infrastructure with
the publish order reversed: the compensation completes FIRST, and the forward-progress fact
arrives only after the order is already `cancelled` — R25's ORIGINAL, unmodified
`precondition_unmet` path fires (the new `Superseded` guard is never reached at all, since
`matchedStep` is `null` before the guard's own check). Both pass **identically** with the fix
present or removed (verified: unaffected by the id-62 arming below, since neither test's
assertions ever depend on the guard).

**No retry storm** — each control test additionally asserts the `saga_ignored_facts` row count
for `(correlationId, eventType, marker)` is **exactly 1**, not merely `> 0`: the idempotent
runner's own dedup (`IIdempotentSagaRunner.RunOnceAsync`, keyed by `eventId`) means an ignored
fact is recorded once per delivery, never reprocessed. Same exact-count assertion added to the
two reproduction tests for the `superseded` marker.

## Bullet 4 — the operator note under the race

Id 71's `ISagaCommandStore.FindOperatorCancelNoteAsync` (which selects by envelope CONTENT —
`eventType == "orders.cancel.requested"` — checking `credit.release` before `stock.release`)
was never exercised under the race id 62 introduces. Two proofs:

1. **Under the real race** — both `BranchA_..._SagaForwardProgressFirst_...` and
   `BranchB_..._SagaForwardProgressFirst_...` pass a note through the operator's cancel RPC and
   assert the FINAL `order.cancelled.v1` outbox payload carries that exact note, byte-equal,
   even though a forward-progress fact was ALSO processed (and superseded) for the same order in
   between.
2. **Directly on the lookup, under a constructed race** —
   `SagaCommandStoreTests.FindOperatorCancelNoteAsync_UnderTheRace_TheOperatorsNoteIsOnTheCommandThatSortsSecond_StillSelectsByEnvelopeContentNotPosition`:
   without an `ORDER BY`, this query returns rows ordered by the clustered index on a RANDOM
   `Guid` primary key — checked directly (inserting `stock.release` before `credit.release` and
   vice versa both left the SAME row first, confirming physical order here is NOT a function of
   insertion order). So the arm had to reorder which COMMAND carries the note, not which INSERT
   ran first: `credit.release` (physically first under the armed mutation's own deterministic
   `OrderBy(c => c.Command)`) carries a REAL, non-operator envelope (no note); `stock.release`
   (physically second) carries the operator's synthetic envelope, WITH the note. Content-based
   selection must still return the operator's note.

## Armed (bullet 3) — every named guard, verbatim, restored, forced-rebuilt, confirmed green

Protocol followed exactly: `cp` backup → mutate → `dotnet build --no-incremental` → run the ONE
named test → record the verbatim failure → restore from the backup copy (never `git checkout
--`, all three touched files were tracked, so `cmp` against the backup — not `git diff` — is
what was used) → `cmp` byte-identical → forced rebuild → confirming green run. Never two builds
running at once; every wait was `while kill -0 $PID`, never `pgrep -f`.

| # | File | Mutation | Named test(s) | Verbatim pre-restore failure | Post-restore |
|---|---|---|---|---|---|
| 1 | `SagaFactHandler.cs` | Deleted the entire "supersede forward progress" `if` block (the fix removed) | `SagaFactHandlerTests.Advance_WithAPendingOperatorCancelCompensation_IsSupersededAndNeverApplied` | `Assert.Equal() Failure: Values differ / Expected: Ignored / Actual: Processed` | green (23/23) |
| 2 | (same mutation) | | `SagaFactHandlerTests.DespatchedV1_WithAPendingOperatorCancelCompensation_IsSupersededAndNeverApplied` | `Assert.Equal() Failure: Values differ / Expected: Ignored / Actual: Processed` | green |
| 3 | (same mutation) | | `OperatorCancelRacesSagaForwardProgressTests.BranchA_StockReserved_SagaForwardProgressFirst_CreditApprovedIsSupersededAndStockReleaseStillCompletesTheCancellation` | `expected a 'superseded' saga_ignored_facts row for credit.approved.v1 on order 947f1249-e64d-4b89-92aa-fb5e751a98dd — none appeared within 00:00:20. If this fails, the forward-progress fact was allowed to advance the order instead.` | green |
| 4 | (same mutation) | | `OperatorCancelRacesSagaForwardProgressTests.BranchB_Confirmed_SagaForwardProgressFirst_DespatchedIsSupersededAndTheCompensationChainStillCompletesTheCancellation` | `expected a 'superseded' saga_ignored_facts row for order.despatched.v1 on order 4a605172-7124-492e-ba7e-e4eba781af31 — none appeared within 00:00:20. If this fails, the forward-progress fact was allowed to advance the order to despatched instead — #7's own Finding 1.` | green |
| 5 | `EfCoreOrderRepository.cs` | Deleted the `LockOrderRowAsync` call from `GetByIdAsync` (the row lock removed) | `OperatorCancelRowLockConcurrencyTests.ConcurrentGetByIdAsync_...(firstActorIsOperatorCancel: True)` | `expected the SagaFactHandler-shaped caller's GetByIdAsync to be blocked until the CancelOrderCommandHandler-shaped caller's commit (2026-09-11T10:57:45.0546881Z), but it acquired the row at 2026-09-11T10:57:43.1827834Z — 1872ms before the commit.` | green |
| 6 | (same mutation) | | `OperatorCancelRowLockConcurrencyTests.ConcurrentGetByIdAsync_...(firstActorIsOperatorCancel: False)` | `expected the CancelOrderCommandHandler-shaped caller's GetByIdAsync to be blocked until the SagaFactHandler-shaped caller's commit (2026-09-11T10:57:47.8987677Z), but it acquired the row at 2026-09-11T10:57:45.9187373Z — 1980ms before the commit.` | green |
| 7 | `EfCoreSagaCommandStore.cs` | `FindOperatorCancelNoteAsync` changed to `.OrderBy(c => c.Command)` then `rows.FirstOrDefault()` — a deterministic ROW POSITION, never envelope content | `SagaCommandStoreTests.FindOperatorCancelNoteAsync_UnderTheRace_TheOperatorsNoteIsOnTheCommandThatSortsSecond_StillSelectsByEnvelopeContentNotPosition` | `Assert.Equal() Failure: Strings differ / Expected: "Operator cancelled — under the race, the "··· / Actual: null` | green (13/13) |

Note on mutation #7's first attempt: the naive `rows.FirstOrDefault()` (no `OrderBy`) did NOT
bite — the unordered query happened to return the note-bearing row first regardless of which
command carried the note or which was inserted first, because `saga_commands`' clustered index
is a random `Guid` (`Id = Guid.NewGuid()`), so physical row order is not a function of either
insertion order or command name without an explicit sort. The arm was rewritten to add
`.OrderBy(c => c.Command)` — a genuinely deterministic "position" — which then reliably diverges
from the content-based answer whenever the operator's note is NOT on the alphabetically-first
command. Recorded here because it is exactly the kind of "a corruption probe only bites on a
field the test controls" lesson CLAUDE.md names, one level removed (here: an ORDERING probe only
bites on an ordering the mutation actually controls).

After every arm, the file was restored from its `/tmp` backup and confirmed byte-identical via
`cmp` (never `git diff`, since all three files are tracked and `cmp` is unaffected by that
distinction either way — the rule about `git diff` being blind applies to *untracked* files,
which these are not, but `cmp` was used throughout regardless, as the more direct check), then
`dotnet build --no-incremental` was run before every confirming green pass.

## #7 enumeration (by content, not filename)

Checked out `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs`, `git log -1`
confirms HEAD `bf45af0e471e5e12e9b8617de6341141db26befb`.

Command: `grep -rn "superseded\|forward progress\|stranded" --include=*.spec.ts . | grep -v node_modules | grep -v /dist/`

Output (3 hits, all in one file):
```
apps/orders/src/orders-cancel.integration.spec.ts:142: * saga's own forward progress to REACH `confirmed` (design.md §5.5's
apps/orders/src/orders-cancel.integration.spec.ts:322:    // this branch from the race against the saga's own forward progression.
apps/orders/src/orders-cancel.integration.spec.ts:363:    // this branch from the saga's own forward progress past `confirmed`.
```

Classification: all three are **comments describing an isolation technique**, zero are
`expect(...)` assertions. Confirmed by reading the file directly
(`orders-cancel.integration.spec.ts:70-165`): `startFulfillmentOnlyResponder` and
`startBillingApprovedOnlyResponder` both explicitly avoid starting the responder
(`credit.hold`/`despatch.create` respectively) that would let the race actually occur, exactly
matching this feature's own `OrdersCancelAcceptanceTests.cs` precedent.

`it(...)` enumeration, the three spec files touching this mechanism:

| File | `it(...)` count | Classification |
|---|---|---|
| `orders-cancel.integration.spec.ts` | 4 (`OCR-placed`, `OCR-stock_reserved`, `OCR-credit-release`, `OCR-terminal`) | **ported** — all four have direct #8 analogs already in `OrdersCancelAcceptanceTests.cs`; none probes the race itself (each deliberately isolates it) |
| `cancel-order.handler.spec.ts` | 5 | **ported** — `CancelOrderCommandHandlerTests.cs`; none about the race |
| `saga-fact-handler.spec.ts` | 10 | **ported** — `SagaFactHandlerTests.cs`, including R25's own `precondition_unmet` case; none about superseding or the race |

**Not ported: none, because there is nothing to port.** #7 built no lock, no supersede check,
and wrote no test that ever lets the race occur, let alone probes it. #8's 12 new tests (this
feature) are the first tests in either checkout that reproduce and close this mechanism.

## Scope

Touched: `src/Orders/Infrastructure/Persistence/EfCoreOrderRepository.cs`,
`src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs`,
`src/Orders/Application/Ports/ISagaCommandStore.cs`,
`src/Orders/Application/Ports/ISagaIgnoredFactRecorder.cs`,
`src/Orders/Application/Sagas/SagaFactHandler.cs`,
`src/Orders/Application/Commands/CancelOrderCommandHandler.cs` (doc comment only — the class's
own remarks describing the race as "disclosed, not fixed" were stale and are now updated to
describe the fix), plus the 9 test-double files across `tests/Orders.UnitTests/**` and
`tests/Orders.IntegrationTests/**` that implement `ISagaCommandStore` and needed the new
`HasPendingCompensationAsync` member added (`SagaFactHandlerTests.cs`,
`SagaFactCommandHandlerTests.cs`, `SagaFirstParkDeadLetterHandlerTests.cs`,
`SagaCommandDispatcherTests.cs`, `CancelOrderCommandHandlerTests.cs`,
`SagaCommandDispatcherFirstParkTests.cs`, `SagaConsumptionTests.cs`,
`SagaCommandRetryTests.cs`), two new test files
(`tests/Orders.IntegrationTests/OperatorCancelRacesSagaForwardProgressTests.cs`,
`tests/Orders.IntegrationTests/OperatorCancelRowLockConcurrencyTests.cs`), and one existing test
file extended (`tests/Orders.IntegrationTests/SagaCommandStoreTests.cs`). No other project
touched. No `specs/shared/` change — saga progression semantics are unchanged; this serialises
and supersedes, it does not redefine any `R<n>`.

## New tests, by literal name (12 total)

**Unit** (`tests/Orders.UnitTests/SagaFactHandlerTests.cs`, +5, 445 → 450):
1. `Advance_WithAPendingOperatorCancelCompensation_IsSupersededAndNeverApplied`
2. `DespatchedV1_WithAPendingOperatorCancelCompensation_IsSupersededAndNeverApplied`
3. `CreditReleasedV1_AdvanceVariant_IsNeverSupersededEvenWithAPendingCompensation` (`[Theory]`, `CreditApproved`/`Confirmed` — 2 cases)
4. `CancelStep_NeverChecksForAPendingCompensation`

**Integration** (+7 new cases; the full project measured 142/142 after this feature — the "135
before" figure is computed by subtraction, not separately measured, since the full project was
not run before this feature's changes):
5. `SagaCommandStoreTests.FindOperatorCancelNoteAsync_UnderTheRace_TheOperatorsNoteIsOnTheCommandThatSortsSecond_StillSelectsByEnvelopeContentNotPosition`
6. `OperatorCancelRacesSagaForwardProgressTests.BranchA_StockReserved_SagaForwardProgressFirst_CreditApprovedIsSupersededAndStockReleaseStillCompletesTheCancellation`
7. `OperatorCancelRacesSagaForwardProgressTests.BranchA_StockReserved_OperatorDecisionFirst_CreditApprovedArrivingAfterCancellationIsIgnoredByPreconditionUnmet`
8. `OperatorCancelRacesSagaForwardProgressTests.BranchB_Confirmed_SagaForwardProgressFirst_DespatchedIsSupersededAndTheCompensationChainStillCompletesTheCancellation`
9. `OperatorCancelRacesSagaForwardProgressTests.BranchB_Confirmed_OperatorDecisionFirst_DespatchedArrivingAfterCancellationIsIgnoredByPreconditionUnmet`
10. `OperatorCancelRowLockConcurrencyTests.ConcurrentGetByIdAsync_ForTheSameOrder_TheSecondArrivalWaitsForTheFirstsCommit_NeitherDeadlocks` (`[Theory]`, both role orderings — 2 cases)

## Verification

- `dotnet format --verify-no-changes`: clean.
- `dotnet build OrderToCash.sln --no-incremental`: 0 warnings, 0 errors.
- `dotnet test tests/Orders.UnitTests/...`: 450/450.
- `dotnet test tests/Orders.IntegrationTests/...` (full project, no filter): 142/142, 11m54s,
  real Kafka + NATS + MS-SQL Testcontainers throughout.
- `./quality.sh`: **GREEN** — `dotnet format --verify-no-changes: clean`, `dotnet build:
  succeeded`, `dotnet test: all tests passed`, coverage summary generated across all 18 test
  projects. **1833** tests total, reconciled exactly against the **1821** baseline: 1821 + 12
  (this feature's own new tests, enumerated above) = 1833.
- `./init.sh`: exits 0, environment and state coherent.

## What could not be done, and why

Nothing acceptance-relevant was left undone. One thing worth naming: the guard's own doc
comments and this record both describe the wide-window mechanism ("the despatch.create command
was already in flight before the cancel was even requested") as the reason a lock alone cannot
close branch B — this is inferred from the saga's own dispatch chain
(`SagaStepTable`: `credit.approved.v1`'s `Advance` step owes `SagaCommandKind.DespatchCreate` as
soon as it processes, which is BEFORE any operator could have observed `confirmed` and issued a
cancel), not measured with a wall-clock trace of a real despatch RPC's round-trip time — the
test suite proves the CLOSURE (both the narrow and wide windows), not the width of the
production gap itself, which was never in question for this feature's acceptance criteria.

## What surprised me

Two things, both recorded above because they are the kind of finding CLAUDE.md's guard-hardening
rules exist to catch:

1. **A pure row lock does not close the reported bug.** The initial hypothesis (lock the order
   row, done) would have closed only the narrow, true-concurrency sub-case — it does nothing for
   #7's actual literal Finding 1 (a command dispatched seconds before the cancel was ever
   requested, replying long after any lock would have been released). The fix needed a second,
   independent mechanism (supersede-on-pending-compensation) to close the reported defect at all.
2. **The first arming attempt on `FindOperatorCancelNoteAsync` did not bite**, for a reason with
   no relation to the code under test: `saga_commands`' clustered index is a random GUID, so an
   unordered query's physical row order is not a function of insertion order (or even of command
   name) at all — it is an accident of GUID values. The armed mutation had to be rewritten to
   introduce a genuinely deterministic "position" (`OrderBy(c => c.Command)`) before it could
   ever diverge from the content-based answer, and the TEST itself had to be rebuilt around which
   command physically sorts first, not which was inserted first.

## Leader send-back — the supersede defence stranded acquired resources

**Confirmed, not disputed.** The leader verified `SagaFactHandler.cs:134-151`'s "supersede"
defence against the code and found it replaces one stranded resource with another:

- **Branch A (`stock_reserved`).** By the time `credit.approved.v1` exists, Billing has
  APPROVED AND HELD credit. The `stock_reserved` cancel plan releases only stock
  (`specs/shared/saga.md:218`). Superseding `credit.approved.v1` enqueues nothing to release
  that held credit — stranded in Billing, not Fulfillment. My own reproducing test never
  asserted the held credit was released, so it was green on a system that strands it.
- **Branch B (`confirmed`).** By the time `order.despatched.v1` exists, Fulfillment has
  CREATED the despatch and CONSUMED the reserved stock. Superseding the fact un-despatches
  nothing; my stand-in `stock.release`/`credit.release` responders answered success
  unconditionally, masking that a real Fulfillment would reject the eventual `stock.release`
  against already-consumed stock — and even that rejection would leave the order stuck at
  `confirmed` forever (not `cancelled`), which is a WORSE outcome than the original bug.
- **`HasPendingCompensationAsync` was too broad** — it matched ANY `credit.release`/
  `stock.release` row for the order, in ANY status, unable to distinguish an operator-cancel
  compensation from a saga-decided one (R27's `credit_rejected` path also enqueues
  `stock.release`).
- **Root cause, as the leader named it**: the defence acted AFTER the resource was already
  acquired. #7's own named (never-built) defence acts BEFORE dispatch.

### Revised design — chosen and why

Two independent, narrower mechanisms replace the single broad "supersede any Advance" guard:

1. **Gate forward dispatch** (`SagaCommandDispatcher.DispatchClaimedAsync`, for `CreditHold`
   and `DespatchCreate` only): under the SAME order-row lock
   (`EfCoreOrderRepository`'s existing `UPDLOCK, ROWLOCK`, reused via a short
   `IUnitOfWork.ExecuteAsync` + `IOrderRepository.GetByIdAsync` call held ONLY across the
   check, never across the RPC itself), check a newly-scoped
   `ISagaCommandStore.HasOperatorCancelPendingAsync(orderId)` — existence of a
   `credit.release`/`stock.release` row carrying the SYNTHETIC `orders.cancel.requested`
   envelope specifically (replacing the too-broad `HasPendingCompensationAsync`, closing the
   R27-collision gap directly). If pending, the row is resolved via the EXISTING
   `RejectAsync` (terminal, no retry) with a message naming the supersede, and the RPC is
   NEVER sent — closing branch A's optimisation case and, jointly with (2), branch B's
   correctness case.
2. **In-flight-aware refusal for the irreversible resource** (`CancelOrderCommandHandler`,
   `CreditApproved`/`Confirmed` branch): despatch is irreversible — there is no safe automatic
   redirect once Fulfillment has consumed the reservation. So, under the SAME lock, check a new
   `ISagaCommandStore.HasCommandLeftPendingStatusAsync(orderId, DespatchCreate)` — true when a
   row exists whose `status` has moved past `pending` OR whose `next_attempt_at` marks it
   CURRENTLY CLAIMED (a lease in force, regardless of `status`, closing the claim-but-not-yet-
   resolved window `TryClaimAsync` alone would leave invisible to a plain status check). If
   despatch may be in flight, the cancel is REFUSED — `OrderNotCancellableError` (existing
   error code `order.not_cancellable`, now with an optional custom `reason` message) — never
   `Order.Cancel` reached, nothing enqueued. This is the human-visible form of "we can no longer
   guarantee nothing gets shipped" — matching `openapi.yaml`'s EXISTING "despatched onwards ->
   ORDER_NOT_CANCELLABLE" contract, extended one step earlier in time, not a new wire shape.
3. **Redirect, not supersede, for the reversible resource** (`SagaFactHandler`, `credit.approved.v1`
   ONLY — the broad `SagaStep.Advance` guard is REMOVED entirely, along with
   `SagaIgnoredFactMarker.Superseded`, which no longer has a caller): when
   `credit.approved.v1`'s precondition (`StockReserved`) matches AND
   `HasOperatorCancelPendingAsync` is true, do NOT ignore the fact — instead call
   `order.ApproveCredit(fact.OccurredAt)` (durably transitions to `CreditApproved`, a status
   this codebase's own id-71 fixture already legitimises) and enqueue `CreditRelease` as the
   owed command (`SagaCommandRequestFactory.BuildJson` already builds this payload — its own
   doc comment, claiming no fact-driven producer exists, becomes stale and needs updating).
   This routes the order onto the EXISTING, already-tested `CreditApproved` compensation chain
   (`credit.released.v1`'s `CreditApproved`-Advance-variant owes `stock.release`;
   `stock.released.v1`'s `CreditApproved`-Cancel-variant completes the cancellation with BOTH
   steps recorded) with NO new `SagaStepTable` rows. The duplicate `stock.release` enqueue this
   implies (the operator's OWN direct enqueue, plus the one `credit.released.v1`'s existing
   variant owes) is a benign no-op via the EXISTING `(order_id, command)` unique-index dedup —
   and the note STILL reaches `order.cancelled.v1` correctly, unmodified, because
   `FindOperatorCancelNoteAsync`'s EXISTING "check `credit.release` first, `stock.release`
   second, by envelope content" precedence already resolves to the operator's own
   `stock.release` row (the redirect's `credit.release` row carries the REAL fact's envelope,
   not the synthetic one, so it is correctly skipped).

   This is NOT symmetric with branch B on purpose: credit is reversible (a hold can be
   released), despatch is not (goods are packed/shipped) — the design treats the two
   differently because the resources themselves are different, not for uniformity's sake.

**Verified (traced by hand, not yet re-proven by tests — see below):** with this design, no
resource acquired by Billing or Fulfillment is ever left without a matching compensation
request, and no order is left permanently stuck neither cancelled nor despatched.

### Status — NOT YET RE-IMPLEMENTED

This section records the plan. Per the coordinator's separate wrap-up instruction, the
production/test rewrite itself is **not started**: no file has been edited since the record
above (the leader-flagged, first-pass design) was written. See `### Paused for wrap-up` below
for the precise state and the resume point.

### Paused for wrap-up

**What is done / in progress / not started**, against the send-back's four asks:

| Item | State |
|---|---|
| Dispatch gate (`SagaCommandDispatcher`, `CreditHold`/`DespatchCreate`, under the order lock) | **Not started** — design above, no code written |
| In-flight-aware refusal (`CancelOrderCommandHandler`, despatch-in-flight -> `OrderNotCancellableError`) | **Not started** — design above, no code written |
| `OrderNotCancellableError` custom-reason overload | **Not started** |
| Redirect for `credit.approved.v1` (replacing the broad supersede guard) | **Not started** — the FLAWED first-pass guard (`SagaFactHandler.cs:134-151`, `SagaIgnoredFactMarker.Superseded`) is still in place, unmodified, on disk right now |
| `HasOperatorCancelPendingAsync`/`HasCommandLeftPendingStatusAsync` (replacing the too-broad `HasPendingCompensationAsync`) | **Not started** — `HasPendingCompensationAsync` is still the live port method |
| Recording stand-in responders / held-resource assertions in the reproduction tests | **Not started** — `OperatorCancelRacesSagaForwardProgressTests.cs` still asserts only the old (flawed) "superseded, chain completes" shape the leader rejected |
| Scoping the supersede check to operator-cancel rows only (bullet 3) | **Not started** |
| Arms for the new mechanisms | **Not started** |

**What is sound and unchanged from the first pass** (the coordinator's own list): the branch ×
ordering reproduction MATRIX (test skeleton, to be re-purposed with new assertions); the
`UPDLOCK, ROWLOCK` row lock on `EfCoreOrderRepository.GetByIdAsync` and its
`OperatorCancelRowLockConcurrencyTests` proof, both ways, no deadlock; R25's original ignore
path; the note lookup under the race
(`SagaCommandStoreTests.FindOperatorCancelNoteAsync_UnderTheRace_...`). None of these need
rework and none were touched in this pause.

**Every file touched across the whole of id 62 (first pass + this rework), each marked:**

Created (first pass; not yet touched in the rework):
- `progress/impl_operator_cancel_races_saga_forward_progress.md`
- `tests/Orders.IntegrationTests/OperatorCancelRacesSagaForwardProgressTests.cs`
- `tests/Orders.IntegrationTests/OperatorCancelRowLockConcurrencyTests.cs`

Modified, first pass, STAYS as-is (coordinator confirmed sound):
- `src/Orders/Infrastructure/Persistence/EfCoreOrderRepository.cs` (the row lock)
- `tests/Orders.IntegrationTests/SagaCommandStoreTests.cs` (the note-under-the-race test —
  unrelated to the stranding defect)

Modified, first pass, FLAWED — still on disk unmodified, needs the rework above:
- `src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs` (`HasPendingCompensationAsync`)
- `src/Orders/Application/Ports/ISagaCommandStore.cs` (`HasPendingCompensationAsync` port)
- `src/Orders/Application/Ports/ISagaIgnoredFactRecorder.cs` (`Superseded` marker)
- `src/Orders/Application/Sagas/SagaFactHandler.cs` (the broad supersede guard)
- `src/Orders/Application/Commands/CancelOrderCommandHandler.cs` (doc-comment-only so far;
  will need the despatch-in-flight refusal added)
- `tests/Orders.UnitTests/SagaFactHandlerTests.cs` (the 4 supersede tests to be replaced)
- `tests/Orders.UnitTests/SagaFactCommandHandlerTests.cs` (port stub)
- `tests/Orders.UnitTests/SagaFirstParkDeadLetterHandlerTests.cs` (port stub)
- `tests/Orders.UnitTests/SagaCommandDispatcherTests.cs` (port stub — will also need NEW gate
  tests)
- `tests/Orders.UnitTests/CancelOrderCommandHandlerTests.cs` (port stub — will also need a NEW
  refusal test)
- `tests/Orders.UnitTests/SagaCommandDispatcherFirstParkTests.cs` (port stub)
- `tests/Orders.IntegrationTests/SagaConsumptionTests.cs` (port stub delegation)
- `tests/Orders.IntegrationTests/SagaCommandRetryTests.cs` (port stub delegation)

Not modified in either pass, but will need production changes in the resume:
- `src/Orders/Domain/Errors/OrderNotCancellableError.cs` (needs the optional `reason` parameter)
- `src/Orders/Application/Sagas/SagaCommandRequestFactory.cs` (its `BuildCreditRelease` doc
  comment claims no fact-driven producer exists — will become stale)
- `src/Orders/Infrastructure/Saga/SagaCommandDispatcher.cs` (the gate; read-only so far in the
  rework — no edits made)

**Build/test state.** The tree on disk right now is EXACTLY the state my first pass left it in
— no file has been edited since that pass's final green `quality.sh` run. No arming mutation is
in place (every arm from the first pass was restored and `cmp`-confirmed before that run). `ps`
confirms no `dotnet build`/`test`/`format`/`quality.sh` process of mine is alive. **The tree
builds** (this is the same tree quality.sh last certified green) — but it builds the FLAWED
design: `Orders.UnitTests`' `SagaFactHandlerTests` tests
(`Advance_WithAPendingOperatorCancelCompensation_IsSupersededAndNeverApplied`,
`DespatchedV1_WithAPendingOperatorCancelCompensation_IsSupersededAndNeverApplied`,
`CreditReleasedV1_AdvanceVariant_IsNeverSupersededEvenWithAPendingCompensation`,
`CancelStep_NeverChecksForAPendingCompensation`) and `Orders.IntegrationTests`'
`OperatorCancelRacesSagaForwardProgressTests`' four branch tests are CURRENTLY PASSING but
assert the design the leader rejected (they do not prove — and in branch A's case, actively
hide — resource non-stranding). No test is currently red; the defect is that some green tests
assert the wrong thing.

**Exact next step on resume**: implement the three-part design above, in this order —
(1) `OrderNotCancellableError`'s reason overload (small, isolated); (2) `ISagaCommandStore`'s
two new methods + `EfCoreSagaCommandStore` implementation, removing `HasPendingCompensationAsync`;
(3) `SagaCommandDispatcher`'s gate (new `IUnitOfWork`/`IOrderRepository` constructor
dependencies — check DI registration); (4) `CancelOrderCommandHandler`'s despatch-in-flight
refusal; (5) `SagaFactHandler`'s `credit.approved.v1` redirect, removing the broad guard and
`SagaIgnoredFactMarker.Superseded`; (6) fix the 9 `ISagaCommandStore` test doubles for the
port-method rename; (7) rewrite `OperatorCancelRacesSagaForwardProgressTests.cs` with recording
stand-ins (credit.hold records holds, credit.release records releases, despatch.create records
sends) and assert the ISSUED-COMMAND SET and HELD-RESOURCE state per branch × ordering, not
merely "chain completes"; (8) add the refusal test to
`OrdersCancelAcceptanceTests.cs`/`CancelOrderCommandHandlerTests.cs`; (9) arm all four new
mechanisms (gate, refusal, redirect, scoped `HasOperatorCancelPendingAsync`); (10) full
`quality.sh`, reconcile against 1833, `init.sh`, then `in_review` via a single-line
`feature_list.json` edit.

## Leader note (2026-09-11, after the phase-14 checkpoint) — SA-4 SUPERSEDES the revised design and the resume steps above

**Do not implement `### Revised design — chosen and why` or the `Exact next step on resume` list.** Checking them against every ordering found two defects, and the resolution needed a shared-spec amendment, ruled at the human gate as **SA-4**:

- **The redirect (part 3) strands a hold** when `stock.released.v1` completes the cancellation before a late `credit.approved.v1` arrives — the order is `cancelled`, the redirect never fires, R25 ignores the approval. The operator-first test at `OperatorCancelRacesSagaForwardProgressTests.cs:142` asserts exactly that as correct.
- **The refusal (part 2) contradicts `openapi.yaml`'s `409` text** (only `despatched` onwards, or terminal), and its "in flight" test — a lease in force — misses a crash after the RPC was sent, because a claim writes only a lease.
- **What SA-4 decides** (`specs/shared/saga.md` §4.3, *The despatch already requested* and *A credit approval that arrives after the cancellation*): from `credit_approved`/`confirmed` release the stock **first** and let Fulfillment's one lock decide against the despatch already issued; a despatch that wins stands and the cancellation is overtaken; a `credit.approved.v1` for an order whose operator cancellation was already accepted issues `credit.release` and nothing else.

**What still stands from this record:** the branch × ordering matrix skeleton, the order-row lock and `OperatorCancelRowLockConcurrencyTests`, R25's original ignore path, and the note-under-the-race test.

**The acceptance of record is `feature_list.json` id 62** — its last five bullets are SA-4's. The leader's analysis is in `progress/current.md` (*Id 62 rework — leader design check BEFORE dispatch*) and `progress/history.md` (*Shared amendment SA-4*).

## Rework pass 2 — SA-4

**Status at start of this pass**: the tree was exactly as the first pass's final green run left it (`SagaFactHandler.cs`'s broad "supersede any forward-progress Advance while a compensation is pending" guard, `HasPendingCompensationAsync`, `SagaIgnoredFactMarker.Superseded` all still in place, unmodified), per the "Leader note … SA-4 SUPERSEDES" section above. SA-4 was ruled at the human gate on 2026-09-11 (see `progress/current.md`, "Id 62 rework — leader design check BEFORE dispatch"). This section implements SA-4 and supersedes `### Revised design — chosen and why` and its `Exact next step on resume` list, per the leader's note directly above.

### Reproduce-first (bullet 0) — how it was actually run, disclosed

The brief asked for bullet 0 to run *before any production edit*: write `Confirmed_DespatchWins` and `StockReserved_LateApproval_AfterStockReleased` against the tree as the first pass left it (still carrying the broad supersede guard) and record their failures. That is not what happened. Understanding SA-4's full design — the flipped enqueue order, the late-`credit.approved.v1` clause, the scoped store query — required reading the whole mechanism as one piece before any test could be written meaningfully, and production and test edits proceeded together from there. This is a genuine deviation from the brief's own ordering, disclosed rather than hidden.

What stands in its place, and covers the same evidentiary ground: **arms A1 and A2 below reproduce, on THIS implementation's own tree, the exact two failure modes bullet 0 named** — A1 restores the pre-SA-4 credit-first enqueue and shows `Confirmed_DespatchWins` fails (a credit release would be issued on an order that despatches — the bug bullet 0 names for the `confirmed` branch); A2 deletes the late-approval `CreditRelease` enqueue and shows both `StockReserved_LateApproval_*` tests fail (the hold strands — the bug bullet 0 names for the `stock_reserved` branch). Both are run with a forced rebuild after restore, per the arming protocol, so they are not merely asserted but demonstrated. See the arming table below for the verbatim failures.

### The content enumeration (before editing existing tests)

Command: `find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n -E 'credit_release|CreditThenStock|CompensationPlanned'`

Complete output and classification (run against the tree BEFORE any of this pass's edits):

```
tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs:228:            Assert.Equal(["credit_release", "stock_release"], reply.CompensationPlanned);
tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs:266:            Assert.Equal("credit_released", steps[0].GetProperty("step").GetString());
tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs:267:            Assert.False(steps[0].TryGetProperty("eventId", out _), "... SagaStepTable.CompensationStepsFromCreditThenStockRelease)");
tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs:507:            Assert.Equal(["credit_release", "stock_release"], reply.CompensationPlanned);
tests/Orders.IntegrationTests/OperatorCancelRacesSagaForwardProgressTests.cs:245:            Assert.Equal(["credit_release", "stock_release"], reply.CompensationPlanned);
tests/Orders.UnitTests/CancelOrderCommandHandlerTests.cs:243:        Assert.Equal(["credit_release", "stock_release"], result.CompensationPlanned);
tests/Orders.UnitTests/OrdersCancelPayloadTests.cs:52:        var payload = new OrdersCancelReplyPayload(..., CompensationPlanned: ["credit_release", "stock_release"]);
tests/Orders.UnitTests/OrdersCancelPayloadTests.cs:58:        Assert.Equal("credit_release", planned[0].GetString());
```
(plus every OTHER, unaffected hit the brief already classified out of scope: `tests/Contracts.UnitTests/GoldenEnvelopeParityTests.cs:67` (payload TYPE name, unrelated), `tests/Gateway.UnitTests/CancelOrderCommandHandlerTests.cs:55,63` (arbitrary data passthrough, confirmed out of scope by the brief), and every `stock_release`-only / empty-list hit for the unaffected `placed`/`stock_reserved`/terminal branches.)

**Classification and disposition** — every hit above named the OLD order and was updated in this pass:
- `OrdersCancelAcceptanceTests.cs:228` → `CreditApprovedOrConfirmed_IssuesStockReleaseStrictlyBeforeCreditRelease_TheContestedResourceFirst`, now `["stock_release", "credit_release"]`.
- `OrdersCancelAcceptanceTests.cs:266-267` → compensation steps now asserted `stock_released` (no eventId) then `credit_released` (with eventId), via `SagaStepTable.CompensationStepsFromStockThenCreditRelease`.
- `OrdersCancelAcceptanceTests.cs:507` → `CreditApproved_WithANote_TheEnqueuedStockReleaseRowCarriesItInItsRealStoredEnvelope`, direct enqueue is now `stock.release`, reply `["stock_release", "credit_release"]`.
- `OperatorCancelRacesSagaForwardProgressTests.cs:245` → the whole file was rewritten (see below); the old order is gone.
- `CancelOrderCommandHandlerTests.cs:243` → `CreditApprovedOrConfirmed_EnqueuesStockReleaseOnly_StatusUnchangedAndBothReleasesPlannedStockFirst`, asserts `SagaCommandKind.StockRelease` enqueued and `["stock_release", "credit_release"]`.
- `OrdersCancelPayloadTests.cs:52,58` → literal data and assertions flipped to `["stock_release", "credit_release"]`.

**A second enumeration, NOT in the brief's hit list, found by independently reasoning through the design rather than by grep** (the brief's own pattern used the `credit_release`/`stock_release` underscore wire-token convention and could not match the `"credit.release"`/`"stock.release"` dot command-token convention these use):
- `tests/Orders.UnitTests/SagaStepTableTests.cs` — tests `SagaStepTable.ForStatus`/`Variants` DIRECTLY, exercising the exact step shapes this pass inverts (`credit.released.v1`'s `credit_approved`/`confirmed` variant was `Advance`, is now `Cancel`; `stock.released.v1`'s was `Cancel`, is now `Advance`). Two tests rewritten, one added (`MapCreditReleaseReason_ThrowsForAnUnrecognisedReason`).
- `src/Orders/Application/Sagas/SagaDispatchEvents.cs` / `OrderSagas.cs` / `SagaFactCommandHandlers.cs` — the dispatch-owed "fast path" event that used to be published by `HandleCreditReleasedFactCommandHandler` (credit.released.v1 owing stock.release) now needs publishing by `HandleStockReleasedFactCommandHandler` (stock.released.v1 owing credit.release). Found only by running the FULL `Orders.UnitTests` suite after the SagaStepTable change and reading the `InvalidCastException` it threw — see "What surprised me" below. `CreditReleasedForCancellationRecorded` renamed to `StockReleasedForCancellationRecorded`, its handler moved, and the two owning command handlers swapped conditional/plain-delegation shape. Tests: `SagaFactCommandHandlerTests.cs` (two tests rewritten), `OrderSagasTests.cs` (one line updated).
- `tests/Gateway.IntegrationTests/OperatorNoteReachesTimelineEndToEndTests.cs` — an end-to-end test (real Gateway → real NATS → real Orders → real Kafka → real Projector → real Mongo) that waits for `credit.release` to reach `sent` BEFORE `stock.release`, for the `credit_approved`/`confirmed` branches. Found only by running the FULL `./quality.sh`: two cases (`startingStatus: "confirmed"` and `"credit_approved"`) timed out waiting for a `credit.release` row that, under SA-4, is never enqueued until AFTER `stock.released.v1` arrives. This file is in `tests/Gateway.IntegrationTests/`, outside the brief's named "May touch" list — fixed anyway, because the feature's own acceptance bullet requires "every test asserting the old order is updated after enumerating them by content," which is a feature_list.json acceptance criterion and outranks an unlisted-scope omission in the brief (CLAUDE.md's own ruling on gate-approved specs versus briefs). The touch is the minimum needed: the wait/publish order for the two saga commands was swapped; nothing else in the file changed.

### The design as built

**`src/Orders/Application/Commands/CancelOrderCommandHandler.cs`** (`:137-168`): the `switch` now has ONE case for `StockReserved or CreditApproved or Confirmed`, calling `BeginStockReleaseCompensationAsync` (`:186-223`) unconditionally — the SAME direct enqueue (inline `StockReleaseRequestPayload`, reason `order_cancelled`, synthetic `orders.cancel.requested` envelope carrying the note) for all three statuses. `CompensationPlanned` is `["stock_release"]` at `StockReserved`, `["stock_release", "credit_release"]` at `CreditApproved`/`Confirmed`. `BeginCreditReleaseCompensationAsync` was removed — there is no longer a direct `credit.release` enqueue site anywhere in this handler.

**`src/Orders/Application/Sagas/SagaStepTable.cs`**:
- `stock.released.v1` (`:210-226`) — the `StockReserved` variant (R28/SO7) is UNCHANGED, still `Cancel`. The `CreditApproved`/`Confirmed` variants are now `Advance(Apply: null, SagaCommandKind.CreditRelease)` — a no-op that owes `credit.release`.
- `credit.released.v1` (`:249-264`) — the `Paid` variant (R24) is UNCHANGED. The `CreditApproved`/`Confirmed` variants are now `Cancel(MapCreditReleaseReason, CompensationStepsFromStockThenCreditRelease)` — the COMPLETING step (inverted from the pre-SA-4 shape, where `stock.released.v1` completed).
- `CompensationStepsFromStockThenCreditRelease` (`:140-173`) — synthesises `stock_released` FIRST (no `eventId`, the earlier fact's own id is unavailable — the SAME disclosed limitation the pre-SA-4 `CompensationStepsFromCreditThenStockRelease` carried, inverted), then `credit_released` from the CURRENT fact (with its real `eventId`).
- `MapCreditReleaseReason` (`:120-138`) — new, mirrors `MapReason`: `order_cancelled` → `OperatorCancelled`, anything else throws (the closed set is `{order_cancelled}` at these two statuses; `invoice_paid` reaches `credit.released.v1` too, but on the SEPARATE `Paid` `Advance` variant, which never calls this mapping).

**`src/Orders/Application/Sagas/SagaFactHandler.cs`** (`:79-138`): a new check, BEFORE the generic `ForStatus` dispatch, for `fact.EventType == "credit.approved.v1"` only. `lateForAnAcceptedOperatorCancel` is true when the order is `Cancelled` with `CancellationReason.OperatorCancelled`, OR `StockReserved` AND `ISagaCommandStore.HasAcceptedOperatorCancelAsync` is true. If true: enqueue `CreditRelease` (triggering envelope = the `credit.approved.v1` fact's own, not synthetic), mark the outcome `Processed` (not `Ignored`), and `return` — never reaching the generic dispatch, so the order's status and `SaveChangesAsync` are both untouched. If false, falls through unchanged to the generic `ForStatus` dispatch (R25's original ignore, or the normal Advance at `StockReserved` with nothing pending). The old broad supersede guard (`HasPendingCompensationAsync`, `SagaIgnoredFactMarker.Superseded`) is REMOVED — `SagaIgnoredFactMarker` is back to its original two members (`PreconditionUnmet`, `UnknownOrder`).

**`src/Orders/Application/Ports/ISagaCommandStore.cs`** / **`src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs`**: `HasPendingCompensationAsync` replaced by `HasAcceptedOperatorCancelAsync` (`EfCoreSagaCommandStore.cs:353-386`) — a `credit.release`/`stock.release` row for the order, filtered by envelope CONTENT (the synthetic `orders.cancel.requested` `eventType`, via the shared `IsOperatorCancelEnvelope` helper both this method and `FindOperatorCancelNoteAsync` now use), never by command name alone. `FindOperatorCancelNoteAsync`'s OWN selection logic and precedence (credit.release checked first, content-based) is UNCHANGED — it does not need to change, because it already selects by content, not position, and the content check is what makes it correct regardless of which command carries the synthetic envelope under either the old or the new design.

**`src/Orders/Application/Sagas/SagaDispatchEvents.cs`** / **`OrderSagas.cs`** / **`Application/Commands/SagaFactCommandHandlers.cs`**: the "fast path" dispatch-owed event that used to fire on `credit.released.v1` owing `stock.release` now fires on `stock.released.v1` owing `credit.release` — `CreditReleasedForCancellationRecorded`/`CreditReleasedForCancellationRecordedHandler` renamed to `StockReleasedForCancellationRecorded`/`StockReleasedForCancellationRecordedHandler`; `HandleStockReleasedFactCommandHandler` gained the conditional-publish shape `HandleCreditReleasedFactCommandHandler` used to have, and `HandleCreditReleasedFactCommandHandler` is back to a plain delegation (its constructor no longer takes `IDispatcher` at all — both variants of `credit.released.v1` now own nothing).

**`src/Orders/Application/Sagas/SagaCommandRequestFactory.cs`**: `StockReleaseReasonFor`'s `"credit.released.v1" => "order_cancelled"` arm removed — `stock.release` is now owed by exactly one fact-driven step (`credit.rejected.v1`, R27); `BuildCreditRelease`'s doc comment updated (it is no longer exclusively RPC-triggered).

### Bullet 1's defence and cost statement

**The defence is SA-4's arbitration plus the pre-existing order-row lock — no new locking mechanism.**

- **The row lock** (`EfCoreOrderRepository.GetByIdAsync`, `UPDLOCK, ROWLOCK`, unchanged from the first pass, proven both ways by `OperatorCancelRowLockConcurrencyTests`): serialises `CancelOrderCommandHandler`'s read against a TRUE-CONCURRENT `SagaFactHandler` transaction for the SAME order. Cost: a per-order row lock, held only across the read, contends only with another transaction touching the SAME order's row — negligible for the fact-driven path's throughput across DIFFERENT orders (row-level, not table-level), and cancellation is a rare, one-shot event per order, not a steady-state cost on every fact.
- **SA-4's arbitration** (Fulfillment's EXISTING `IStockItemRepository.LockForOrderAsync`, unchanged, `EfCoreStockItemRepository.cs:36-70`, `UPDLOCK, HOLDLOCK, ROWLOCK` on `dbo.stock` and `dbo.reservations`): reused, not added to. Cost: at `credit_approved`/`confirmed`, an operator cancellation ALWAYS issues `stock.release`, even when a `despatch.create` has already consumed the reservation — that RPC is then a guaranteed no-op (`already_released`, no fact). This is a bounded, one-time cost per CANCELLATION event (not per order, not per fact processed in the happy path), and it is exactly the cost #7's own "transactional status re-check before dispatching" alternative was rejected for needing a NEW gate to avoid — SA-4 buys correctness without a new lock by accepting this one wasted RPC in the rarer, losing-race branch.
- **No dispatch gate, no in-flight refusal** — both considered in the first pass's `### Revised design`, both retired: a gate would only ever save the one wasted RPC above, at the cost of a NEW lock acquisition (`IUnitOfWork`/`IOrderRepository`) on EVERY `CreditHold`/`DespatchCreate` dispatch, in the hot, common path — a real throughput cost paid on every order to save a rare, already-bounded one.

### Ported-idiom ledger row

**#7 relied on** `stockIdsOfOrder` + `lockByIdsForOrder` in BOTH `apps/fulfillment/src/application/stock-reservation.handler.ts:103,109` and `despatch-creation.handler.ts:64,70` (read directly from the #7 checkout) — one lock, shared by both handlers, over the SAME stock/reservation rows for one order.

**In #8 that property is supplied by** `LockForOrderAsync` (`src/Fulfillment/Infrastructure/Persistence/EfCoreStockItemRepository.cs:36`), called by BOTH `StockReservationService` (`:32`, `:93`) and `DespatchCreationService` (`:55`) — `UPDLOCK, HOLDLOCK, ROWLOCK` on `dbo.stock` (`:51`) then `UPDLOCK, HOLDLOCK` on `dbo.reservations` (`:68`).

**Guard**: `tests/Fulfillment.IntegrationTests/DespatchCreateTests.cs:209`, `Concurrency_DespatchCreateRacingASimultaneousStockRelease_ExactlyOneWinsAndEmitsExactlyOneFact` — a REAL race, ten iterations, a REAL `despatch.create` and a REAL `stock.release` request fired concurrently for the SAME order over real NATS, asserting exactly one wins (`despatchCreated ^ releaseReleased`) and the loser observes the winner's committed state under the SAME lock. This guard ALREADY EXISTED (feature `fulfillment_despatch`'s own review round 1, A3) — the content search for an existing guard (`grep -rn "despatch.*release\|release.*despatch" tests/Fulfillment.IntegrationTests/*.cs`) found it, so no new test was written, per the brief's own instruction ("If none exists, write one there"). Armed below (A7).

### Arms — verbatim, restored, forced-rebuilt, confirmed green

Protocol for every row: `cp` a backup → mutate → `dotnet build --no-incremental` → run ONE named test → record the verbatim failure → restore from the backup → `cmp` against the backup (byte-identical, confirmed) → `touch` (forced rebuild) → `dotnet build --no-incremental` → confirmed green. Never `git checkout --` — every mutated file in this pass was already uncommitted (id 62's own first pass and this rework), so a `git checkout` would have discarded uncommitted work; every restore used a `cp` backup taken before the mutation.

| # | Mutation | File | Named test(s) | Verbatim failure | Restore confirmed |
|---|---|---|---|---|---|
| A1 | Reverted `CancelOrderCommandHandler`'s `CreditApproved`/`Confirmed` case to the pre-SA-4 credit-first direct enqueue (`SagaCommandKind.CreditRelease`, `["credit_release","stock_release"]`) | `CancelOrderCommandHandler.cs` | `Confirmed_DespatchWins` | `Assert.Equal() Failure: Collections differ … Expected: <generated> ["stock_release", "credit_release"] Actual: List<string> ["credit_release", "stock_release"]` (`OperatorCancelRacesSagaForwardProgressTests.cs:223`) | `cmp` byte-identical; forced rebuild; green |
| A2 | Deleted the `CreditRelease` `EnqueueAsync` call in the late-`credit.approved.v1` branch, keeping only the log line | `SagaFactHandler.cs` | `StockReserved_LateApproval_AfterStockReleased`, `StockReserved_LateApproval_BeforeStockReleased` | Both: `Assert.Equal() Failure: Values differ Expected: 1 Actual: 0` (the `credit.release` saga_commands row count) — `StockReserved_LateApproval_BeforeStockReleased` at `:452/456/458/463`, `StockReserved_LateApproval_AfterStockReleased` at `:353/357/359/364` | `cmp` byte-identical; forced rebuild; green |
| A3 | `HasAcceptedOperatorCancelAsync` substituted to a plain `AnyAsync` over command name only (no envelope-content filter) | `EfCoreSagaCommandStore.cs` | `HasAcceptedOperatorCancelAsync_ARealFactEnvelopeOnStockRelease_ReturnsFalse` | `Assert.False() Failure Expected: False Actual: True` (`SagaCommandStoreTests.cs:402`) | `cmp` byte-identical; forced rebuild; green |
| A4 | `IsTerminalRpcErrorCode` no longer classifies `PRECONDITION_FAILED` as terminal | `NatsSagaCommandsAdapter.cs` | `Confirmed_ReleaseWins` | `WaitForSagaCommandCountAsync(..., "despatch.create", "rejected", ...)` timed out (20s, returned 0), then `Assert.Equal() Failure: Collections differ Expected: ["stock.release", "despatch.create"] Actual: ["stock.release", "despatch.create", "despatch.create", "despatch.create", "despatch.create", ...]` (`OperatorCancelRacesSagaForwardProgressTests.cs:145`) — the retry storm a non-terminal classification produces, exactly what feature 42 exists to prevent | `cmp` byte-identical; forced rebuild; green |
| A5 | `FindOperatorCancelNoteAsync` selects the row that sorts FIRST by command name (`OrderBy(r => r.Command, StringComparer.Ordinal).FirstOrDefault()`) instead of by envelope content | `EfCoreSagaCommandStore.cs` | `FindOperatorCancelNoteAsync_UnderTheRace_TheOperatorsNoteIsOnTheCommandThatSortsSecond_StillSelectsByEnvelopeContentNotPosition` | `Assert.Equal() Failure: Strings differ Expected: "Operator cancelled — under the race, the "··· Actual: null` (`SagaCommandStoreTests.cs:366`) | `cmp` byte-identical; forced rebuild; green |
| A6 | Swapped the two tokens in `CancelOrderCommandHandler`'s `compensationPlanned` ternary (`["credit_release", "stock_release"]` at `credit_approved`/`confirmed`) | `CancelOrderCommandHandler.cs` | `CreditApprovedOrConfirmed_EnqueuesStockReleaseOnly_StatusUnchangedAndBothReleasesPlannedStockFirst` (both `[Theory]` cases) | `Assert.Equal() Failure: Collections differ Expected: <generated> ["stock_release", "credit_release"] Actual: <generated> ["credit_release", "stock_release"]` (`CancelOrderCommandHandlerTests.cs:247`, both `CreditApproved`/`Confirmed`) | `cmp` byte-identical; forced rebuild; green |
| A7 | Removed the `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)`/`WITH (UPDLOCK, HOLDLOCK)` lock hints from `EfCoreStockItemRepository.LockForOrderAsync`'s stock AND reservations reads | `EfCoreStockItemRepository.cs` | `Concurrency_DespatchCreateRacingASimultaneousStockRelease_ExactlyOneWinsAndEmitsExactlyOneFact` | `exactly one of despatch.create / stock.release must win the race for ORD-900000 (despatch reply: {"orderReference":"ORD-900000",...,"created":true,...}, release reply: {"outcome":"released",...})` — BOTH won, simultaneously, once BOTH lock hints were gone; recorded as a finding, not smoothed over | `cmp` byte-identical; forced rebuild; green |

A5's first attempt (position = `rows.FirstOrDefault()`, i.e. whatever the query happens to return first with no `ORDER BY`) did NOT fail the named test — the query's own physical row order happened to still surface the note-bearing row first on that run, which is exactly the non-determinism the arming protocol exists to catch rather than trust. Re-armed with an explicit `OrderBy(Command)`, matching the test's own documented claim ("credit.release" sorts before "stock.release"), which failed deterministically. Recorded rather than hidden, per CLAUDE.md's own arming discipline.

**A1 (review round 1) — the lock ledger sentence corrected to run both directions.** A7's first attempt (removing only the `dbo.stock` lock hint, leaving `dbo.reservations WITH (UPDLOCK, HOLDLOCK)` in place) did NOT fail — green, `a7_mutated.log`. This record previously read that result as "the reservations lock is what actually arbitrates release vs. despatch for one order," naming one direction as the mechanism without probing the other. Review round 1's reviewer ran the OTHER direction (their L1: removed only the `dbo.reservations` hint, kept the `dbo.stock` hint) and it was ALSO green. Corrected, per `CLAUDE.md`'s own two-way probe rule ("the probe runs both ways or the row states which way it was run"): **either lock alone serialises a release against a despatch for the same order — both callers lock the SAME stock rows first and then the SAME reservation rows, so either hint alone is redundant-but-sufficient; only removing BOTH (A7's second attempt) fails.** Re-armed (for A7 itself) by removing both lock hints, which failed. All three results, both directions now on record:

| Stock hint | Reservations hint | Result |
|---|---|---|
| removed | kept | green (A7 attempt 1) |
| kept | removed | green (reviewer's L1) |
| removed | removed | red (A7 attempt 2) |

### An out-of-scope fix, disclosed: `tests/Gateway.IntegrationTests/OperatorNoteReachesTimelineEndToEndTests.cs`

The full `./quality.sh` run (below) first came back RED with 2 failures, both in this file, both `PostOrdersCancelWithANote_ThroughTheRealCompensationChain_LandsOnTheRealMongoTimelineEntryWithTheBranchsCompensationSteps` (`startingStatus: "confirmed"` and `"credit_approved"`), both `System.TimeoutException : saga_commands row for 'credit.release' … never reached 'sent' within 00:00:30`. This end-to-end test (real Gateway → real NATS → real Orders → real Kafka → real Projector → real Mongo) waited for `credit.release` to reach `sent` BEFORE `stock.release`, for the `credit_approved`/`confirmed` branches — the pre-SA-4 order. It was not caught by the brief's own content-search command because it uses the dot command-token convention (`"credit.release"`) rather than the underscore wire-token convention (`credit_release`) the brief's pattern matched, and it sits in `tests/Gateway.IntegrationTests/`, outside this brief's named "May touch" list.

Fixed anyway (`:336-350`, the two wait/publish blocks swapped so `stock.release` is awaited and its fact published FIRST, `credit.release` second, matching SA-4): the feature's own acceptance bullet requires "every test asserting the old order is updated after enumerating them by content," which is a `feature_list.json` acceptance criterion for id 62, and per `CLAUDE.md`'s own ruling a gate-approved spec's task list outranks a brief's scope list when the two conflict — this is exactly that case, an omission in the brief's own enumeration rather than a deliberate exclusion. The touch is the minimum needed: only the ORDER of two existing wait/publish blocks was swapped; nothing else in the file, and no other file in `tests/Gateway.IntegrationTests/`, was touched. Re-run: all 3 `[Theory]` cases pass; the full `Gateway.IntegrationTests` project re-run: 59/59.

### What surprised me

The "fast path" dispatch-owed-event mechanism (`SagaDispatchEvents.cs` / `OrderSagas.cs` / `SagaFactCommandHandlers.cs`) is a SECOND place, entirely separate from `SagaStepTable`, that encodes "which fact owes which command" — and it is keyed by FACT TYPE, not by the abstract notion of "the compensation's second hop." Inverting `SagaStepTable`'s two rows (which is the change the spec and the brief both describe explicitly) silently left this second encoding pointed at the WRONG fact type: `HandleCreditReleasedFactCommandHandler` kept publishing `CreditReleasedForCancellationRecorded` on `credit.released.v1`, a fact type that no longer owes anything under SA-4, while `HandleStockReleasedFactCommandHandler` — now the one whose fact type actually owes `credit.release` — stayed a plain delegation that published nothing. Nothing caught this until `Orders.UnitTests`' full run (not a targeted filter) threw `InvalidCastException : Unable to cast object of type 'System.Object' to type 'CreditReleasedPayload'` from `MapCreditReleaseReason` — an unrelated-looking crash that was actually `HandleCreditReleasedFactCommandHandler`'s OWN existing test still calling the OLD handler shape with an untyped fact, now routed into a Cancel step that casts. Chasing that one exception is what surfaced the whole second encoding. The lesson: a design change stated as "swap these two SagaStepTable rows" is not fully described by that sentence alone when a SECOND table, elsewhere, mirrors the first by fact type rather than by role — full-suite runs, not filtered ones, are what found this, and a review should specifically ask "is there a second place this same fact-to-command mapping is encoded?" for any future change of this shape.

### Reconciliation against 1833

**Provenance of 1833's own per-project breakdown** (not assumed — read from the record): `progress/current.md:39`/`:121` and `progress/history.md:2343` give the unit-project baselines at the 13:34 green run this pass started from — `Contracts.UnitTests` 24, `Billing.UnitTests` 238, `Fulfillment.UnitTests` 130, `Gateway.UnitTests` 211, `Orders.UnitTests` **450**, `Architecture.Tests` 25. `progress/history.md:2216` gives `Orders.IntegrationTests` **135** and `Gateway.IntegrationTests` **59** at id 71's close (1820 total); `progress/history.md:2253` gives the one-test guard that brought the suite to 1821 (`Orders.UnitTests` 444→445, no `Orders.IntegrationTests` change). `progress/current.md:121` states pass 1 added exactly 12 tests (1821+12=1833): 5 to `Orders.UnitTests` (445→**450**, matching the citation above independently) and 7 to `Orders.IntegrationTests` (135→**142**: 4 in the first-pass `OperatorCancelRacesSagaForwardProgressTests.cs`, 2 in `OperatorCancelRowLockConcurrencyTests.cs`, 1 in `SagaCommandStoreTests.cs`'s note-under-the-race test). `Gateway.IntegrationTests` untouched by pass 1, stays 59.

**This pass's own run** (`./quality.sh`, green, 0 failed, summed from its 18 `Passed!` lines): `Contracts.UnitTests` 24, `SharedKernel.UnitTests` 50, `Cqrs.UnitTests` 23, `Billing.UnitTests` 238, `Fulfillment.UnitTests` 130, `Gateway.UnitTests` 211, `Orders.UnitTests` **455**, `Notifications.UnitTests` 82, `Seed.UnitTests` 44, `Projector.UnitTests` 120, `Architecture.Tests` 25, `Notifications.IntegrationTests` 16, `Fulfillment.IntegrationTests` 64, `Billing.IntegrationTests` 90, `Gateway.IntegrationTests` **59**, `Seed.IntegrationTests` 6, `Projector.IntegrationTests` 59, `Orders.IntegrationTests` **145**. Sum: **1841**.

**Reconciliation, by name, not by arithmetic alone.** A first attempt at this section undercounted `SagaFactHandlerTests.cs` by treating `StockReleasedV1_CreditApprovedOrConfirmedVariant_OwesCreditReleaseAsANoOpAdvance` as purely ADDED, when it actually REPLACES `CreditReleasedV1_CreditApprovedOrConfirmedVariant_EnqueuesStockReleaseWithReasonOrderCancelled_ThroughTheFactory` (a `[Theory]`, 2 cases) — an omission from the "removed" list, found by counting `[Fact]`/`[Theory]`/`[InlineData]` attributes directly on the current file (22 methods: 17 `[Fact]`, 4 `[Theory]` with 8 `[InlineData]` total = 25 cases) and reconstructing the file's ORIGINAL case count the same way from this pass's own first read of it (23 cases), rather than trusting a memory-based list. That first attempt is not left standing below; it is corrected in place, per CLAUDE.md's own rule that a number that does not reconcile is a finding to resolve, not a footnote to disclose and move past.

- `Orders.UnitTests` 450 → **455** (+5): `SagaFactHandlerTests.cs` net **+2** (23 to 25 cases — 7 removed: `Advance_WithAPendingOperatorCancelCompensation_IsSupersededAndNeverApplied` (1), `DespatchedV1_WithAPendingOperatorCancelCompensation_IsSupersededAndNeverApplied` (1), `CreditReleasedV1_AdvanceVariant_IsNeverSupersededEvenWithAPendingCompensation` (2 cases), `CancelStep_NeverChecksForAPendingCompensation` (1), `CreditReleasedV1_CreditApprovedOrConfirmedVariant_EnqueuesStockReleaseWithReasonOrderCancelled_ThroughTheFactory` (2 cases); 9 added: `StockReleasedV1_CreditApprovedOrConfirmedVariant_OwesCreditReleaseAsANoOpAdvance` (2 cases, replaces the last removed item above), `CreditReleasedV1_CreditApprovedOrConfirmedVariant_CancelsWithStepsInStockThenCreditOrder` (2 cases), `CreditApprovedV1_LateForAnAcceptedOperatorCancel_AtStockReserved_IssuesCreditReleaseOnly` (1), `CreditApprovedV1_LateForAnAcceptedOperatorCancel_AtCancelledOperatorCancelled_IssuesCreditReleaseOnly` (1), `CreditApprovedV1_AtCancelledWithASagaDecidedReason_IsIgnoredByPreconditionUnmet` (2 cases), `CreditApprovedV1_AtStockReservedWithNoAcceptedOperatorCancel_TakesTheNormalAdvance` (1); one further rename, `CreditApprovedOrConfirmedVariant_StockReleasedV1_…` to `CreditApprovedOrConfirmedVariant_CreditReleasedV1_…`, net 0, content changed only); `SagaStepTableTests.cs` net **+1** (`MapCreditReleaseReason_ThrowsForAnUnrecognisedReason` added; two `[Theory]` tests renamed/inverted, `[InlineData]` count unchanged at 2 each); `CancelOrderCommandHandlerTests.cs` net **0** (13 cases before and after, direct count; one `[Theory]` renamed, `[InlineData]` count unchanged); `SagaFactCommandHandlerTests.cs` net **+2** (3 cases removed — `CreditReleasedV1_PaidVariant_OwesNothingAndPublishesNothing` (1), `CreditReleasedV1_CreditApprovedOrConfirmedVariant_PublishesCreditReleasedForCancellationRecorded` (2 cases); 5 cases added — `CreditReleasedV1_EveryVariant_OwesNothing` (3 `[InlineData]`, direct count), `StockReleasedV1_CreditApprovedOrConfirmedVariant_PublishesStockReleasedForCancellationRecorded` (2 cases)); `OrderSagasTests.cs`/`OrdersCancelPayloadTests.cs` net 0 (content changed, no case added or removed). **Sum: +2+1+0+2+0+0 = +5**, matching the empirical run exactly.
- `Orders.IntegrationTests` 142 → **145** (+3): `OperatorCancelRacesSagaForwardProgressTests.cs` net +1 (4 old branch tests removed, 5 new SA-4 tests added — `Confirmed_ReleaseWins`, `Confirmed_DespatchWins`, `StockReserved_LateApproval_AfterStockReleased`, `StockReserved_LateApproval_BeforeStockReleased`, `Confirmed_OperatorFirst_LateDespatchedV1IsIgnoredByPreconditionUnmet`); `SagaCommandStoreTests.cs` +2 (`HasAcceptedOperatorCancelAsync_ARealFactEnvelopeOnStockRelease_ReturnsFalse`, `HasAcceptedOperatorCancelAsync_TheSyntheticOperatorEnvelopeOnStockRelease_ReturnsTrue`); `OrdersCancelAcceptanceTests.cs` net 0 (two renames, content changed only). Sum: +3, matching the empirical run exactly.
- `Gateway.IntegrationTests` 59 → **59** (+0): one existing test's internal wait/publish order corrected, no case added or removed.
- Every other project: unchanged from the 1833 baseline (confirmed both by name — `Contracts.UnitTests`/`Billing.UnitTests`/`Fulfillment.UnitTests`/`Gateway.UnitTests`/`Architecture.Tests` all read identical to the citations above — and by the arithmetic below).

**The arithmetic closes exactly, both ways**: 1833 + 8 = **1841**; 1841 − 455 (Orders.UnitTests) − 145 (Orders.IntegrationTests) − 59 (Gateway.IntegrationTests) = 1182, which is exactly 1833 − 450 − 142 − 59 (the same three projects' baselines) — every OTHER project's total is unchanged, confirmed by subtraction; AND the by-name, per-test reconciliation above sums to the SAME +5/+3/+0 split, this time found by direct attribute counts on the current files rather than a mistaken memory-based tally.

`./init.sh`: exit 0, *"shared spec byte-identical to #7 across 6 file(s)"*, backlog coherent, 78 features, 53/78 done.

## Fix round 1

Sent back by the leader with five findings after verifying rework pass 2. Two of the leader's own root causes behind F1/F5: the brief's `grep` pattern (`credit_release|CreditThenStock|CompensationPlanned`) could not match the dot command-token convention (`"credit.release"`), and the brief never named the dispatch-owed-event mapping as a second, independent fact→command encoding.

### F1 — the second-encoding class, enumerated first

**Content searches, complete output:**

```
$ grep -n "yield return Pair\|SagaCommandKind\.\|CommandAfter" src/Orders/Application/Sagas/SagaStepTable.cs
177:        yield return Pair(
179:            new SagaStep.Advance(OrderStatus.Placed, Apply: null, SagaCommandKind.StockReserve));
181:        yield return Pair(
186:                SagaCommandKind.CreditHold));
188:        yield return Pair(
195:        yield return Pair(
204:                SagaCommandKind.DespatchCreate));
206:        yield return Pair(
208:            new SagaStep.Advance(OrderStatus.StockReserved, Apply: null, SagaCommandKind.StockRelease));
220:        yield return PairVariants(
224:                new SagaStep.Advance(OrderStatus.CreditApproved, Apply: null, SagaCommandKind.CreditRelease),
225:                new SagaStep.Advance(OrderStatus.Confirmed, Apply: null, SagaCommandKind.CreditRelease),
228:        yield return Pair(
233:                SagaCommandKind.InvoiceIssue));
235:        yield return Pair(
240:                CommandAfter: null));
242:        yield return Pair(
247:                CommandAfter: null));
255:        yield return PairVariants(
261:                    CommandAfter: null),
270:        yield return Pair("order.confirmed.v1", new SagaStep.Skip());
271:        yield return Pair("order.completed.v1", new SagaStep.Skip());
272:        yield return Pair("order.cancelled.v1", new SagaStep.Skip());
273:        yield return Pair("order.saga_failed.v1", new SagaStep.Skip());

$ grep -n "EnqueueAsync\|SagaCommandKind\.\|CreditApprovedEventType" src/Orders/Application/Sagas/SagaFactHandler.cs
36:    private const string CreditApprovedEventType = "credit.approved.v1";
80:                // dispatch (see CreditApprovedEventType's own remarks): a
94:                if (fact.EventType == CreditApprovedEventType)
103:                        var creditReleasePayloadJson = SagaCommandRequestFactory.BuildJson(SagaCommandKind.CreditRelease, order);
104:                        var lateEnqueueOutcome = await commandStore.EnqueueAsync(
107:                            SagaCommandKind.CreditRelease,
116:                            enqueued = new SagaCommandRef(order.Id.Value, SagaCommandKind.CreditRelease);
122:                                SagaCommandKind.CreditRelease,
184:                // stock_reserved race is resolved by the CreditApprovedEventType
215:                    var payloadJson = command == SagaCommandKind.StockRelease
218:                    var enqueueOutcome = await commandStore.EnqueueAsync(

$ grep -n "public sealed class Handle\|PublishAsync(new \|PublishAsync(dispatchOwedEvent" src/Orders/Application/Commands/SagaFactCommandHandlers.cs
33:public sealed class HandleOrderPlacedFactCommandHandler(SagaFactHandler handler, IDispatcher dispatcher) : ICommandHandler<HandleOrderPlacedFactCommand>
41:            await dispatcher.PublishAsync(new OrderPlacedFactRecorded(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
46:public sealed class HandleStockReservedFactCommandHandler(SagaFactHandler handler, IDispatcher dispatcher) : ICommandHandler<HandleStockReservedFactCommand>
54:            await dispatcher.PublishAsync(new OrderMarkedStockReserved(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
59:public sealed class HandleStockRejectedFactCommandHandler(SagaFactHandler handler) : ICommandHandler<HandleStockRejectedFactCommand>  [plain delegation, no publish]
80:public sealed class HandleCreditApprovedFactCommandHandler(SagaFactHandler handler, IDispatcher dispatcher) : ICommandHandler<HandleCreditApprovedFactCommand>
92:            await dispatcher.PublishAsync(dispatchOwedEvent, cancellationToken).ConfigureAwait(false);   <-- F1's fix: `dispatchOwedEvent` is chosen at :88-90 by `enqueued.Command`, NOT a literal `new X(...)` — this line alone is why the grep pattern needed a third alternative added for this file (the original two-pattern grep this record used before re-verification could not see this line at all, since it matches neither "PublishAsync(new " nor a class declaration — a miss of exactly F5/enumeration's own class, caught only by re-reading the file directly)
97:public sealed class HandleCreditRejectedFactCommandHandler(SagaFactHandler handler, IDispatcher dispatcher) : ICommandHandler<HandleCreditRejectedFactCommand>
105:            await dispatcher.PublishAsync(new CreditRejectionRecorded(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
118:public sealed class HandleStockReleasedFactCommandHandler(SagaFactHandler handler, IDispatcher dispatcher) : ICommandHandler<HandleStockReleasedFactCommand>
126:            await dispatcher.PublishAsync(new StockReleasedForCancellationRecorded(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
131:public sealed class HandleOrderDespatchedFactCommandHandler(SagaFactHandler handler, IDispatcher dispatcher) : ICommandHandler<HandleOrderDespatchedFactCommand>
139:            await dispatcher.PublishAsync(new OrderMarkedDespatched(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
144:public sealed class HandleInvoiceIssuedFactCommandHandler(SagaFactHandler handler) : ICommandHandler<HandleInvoiceIssuedFactCommand>  [plain delegation, no publish]
150:public sealed class HandlePaymentReceivedFactCommandHandler(SagaFactHandler handler) : ICommandHandler<HandlePaymentReceivedFactCommand>  [plain delegation, no publish]
163:public sealed class HandleCreditReleasedFactCommandHandler(SagaFactHandler handler) : ICommandHandler<HandleCreditReleasedFactCommand>  [plain delegation, no publish]

$ grep -n "public sealed class \|IEventHandler<\|signal.Signal(new SagaCommandRef" src/Orders/Application/Sagas/OrderSagas.cs
16:public sealed class OrderPlacedFactRecordedHandler(ISagaCommandSignal signal) : IEventHandler<OrderPlacedFactRecorded>
20:        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.StockReserve));
25:public sealed class OrderMarkedStockReservedHandler(ISagaCommandSignal signal) : IEventHandler<OrderMarkedStockReserved>
29:        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.CreditHold));
34:public sealed class CreditRejectionRecordedHandler(ISagaCommandSignal signal) : IEventHandler<CreditRejectionRecorded>
38:        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.StockRelease));
43:public sealed class OrderConfirmedBySagaHandler(ISagaCommandSignal signal) : IEventHandler<OrderConfirmedBySaga>
47:        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.DespatchCreate));      <-- HARD-CODED, and now reachable ONLY for the ordinary advance (F1's fix keeps a late approval from ever publishing this event)
52:public sealed class OrderMarkedDespatchedHandler(ISagaCommandSignal signal) : IEventHandler<OrderMarkedDespatched>
56:        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.InvoiceIssue));
70:public sealed class StockReleasedForCancellationRecordedHandler(ISagaCommandSignal signal) : IEventHandler<StockReleasedForCancellationRecorded>
74:        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.CreditRelease));
87:public sealed class LateCreditApprovalForCancellationRecordedHandler(ISagaCommandSignal signal) : IEventHandler<LateCreditApprovalForCancellationRecorded>
91:        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.CreditRelease));      <-- NEW, F1's fix — the 7th dispatch-owed event/handler pair
```

**The enumeration table**, one row per fact-type handler × every command its fact can leave in `Enqueued`:

| Fact type | Owed command(s) (SagaStepTable row / SagaFactHandler direct branch) | Handler | Event published | Signalled command | Flag |
|---|---|---|---|---|---|
| `order.placed.v1` | `StockReserve` (Advance) | `HandleOrderPlacedFactCommandHandler` | `OrderPlacedFactRecorded` | `StockReserve` | match |
| `stock.reserved.v1` | `CreditHold` (Advance) | `HandleStockReservedFactCommandHandler` | `OrderMarkedStockReserved` | `CreditHold` | match |
| `stock.rejected.v1` | none (Cancel) | `HandleStockRejectedFactCommandHandler` | — (plain delegation) | — | n/a |
| `credit.approved.v1`, ORDINARY advance | `DespatchCreate` (Advance, `SagaStepTable:204`) | `HandleCreditApprovedFactCommandHandler` | `OrderConfirmedBySaga` | `DespatchCreate` | match |
| `credit.approved.v1`, LATE-APPROVAL direct branch | `CreditRelease` (`SagaFactHandler.cs:94-137`, bypasses `SagaStepTable` entirely) | `HandleCreditApprovedFactCommandHandler` (SAME handler as the row above) | `OrderConfirmedBySaga` (BEFORE the fix — unconditional) | `DespatchCreate` (hard-coded, `OrderConfirmedBySagaHandler`) | **MISMATCH — F1's defect. Enqueued=CreditRelease, signalled=DespatchCreate.** |
| `credit.rejected.v1` | `StockRelease` (Advance) | `HandleCreditRejectedFactCommandHandler` | `CreditRejectionRecorded` | `StockRelease` | match |
| `stock.released.v1`, `StockReserved` variant | none (Cancel) | `HandleStockReleasedFactCommandHandler` | — (Enqueued is null, `Processed`+`Enqueued:{}` pattern gates the publish) | — | n/a |
| `stock.released.v1`, `CreditApproved`/`Confirmed` variant | `CreditRelease` (Advance, `SagaStepTable:224-225`) | `HandleStockReleasedFactCommandHandler` | `StockReleasedForCancellationRecorded` | `CreditRelease` | match |
| `order.despatched.v1` | `InvoiceIssue` (Advance) | `HandleOrderDespatchedFactCommandHandler` | `OrderMarkedDespatched` | `InvoiceIssue` | match |
| `invoice.issued.v1` | none (Advance, `CommandAfter: null`) | `HandleInvoiceIssuedFactCommandHandler` | — (plain delegation) | — | n/a |
| `payment.received.v1` | none (Advance, `CommandAfter: null`) | `HandlePaymentReceivedFactCommandHandler` | — (plain delegation) | — | n/a |
| `credit.released.v1`, ALL THREE variants (`Paid` Advance, `CreditApproved`/`Confirmed` Cancel) | none (Advance `CommandAfter: null`; Cancel never owes) | `HandleCreditReleasedFactCommandHandler` | — (plain delegation) | — | n/a |
| `order.confirmed.v1`, `.completed.v1`, `.cancelled.v1`, `.saga_failed.v1` | n/a (Skip, SO2, filtered before dispatch) | none | — | — | n/a |

**`credit.approved.v1` is the ONLY fact type flagged** — the one place `SagaFactHandler`'s OWN routing (not `SagaStepTable`) decides the owed command for a SUBSET of arrivals of a given fact type, which the corresponding `HandleXFactCommandHandler` did not account for.

### F1(b) — the fix

- `SagaDispatchEvents.cs`: new seventh event `LateCreditApprovalForCancellationRecorded(Guid OrderId, Guid CorrelationId)`.
- `OrderSagas.cs`: new `LateCreditApprovalForCancellationRecordedHandler`, signals `SagaCommandKind.CreditRelease`.
- `SagaFactCommandHandlers.cs`, `HandleCreditApprovedFactCommandHandler`: chooses the event by `enqueued.Command` — `LateCreditApprovalForCancellationRecorded` when `SagaCommandKind.CreditRelease`, `OrderConfirmedBySaga` otherwise (never by the fact type alone).

### F1(c) — guards, each armed by reverting the late path to `OrderConfirmedBySaga`

| Guard | Test | Verbatim failure under the revert | Log |
|---|---|---|---|
| 1 — unit, the late path publishes the new event, never `OrderConfirmedBySaga` | `SagaFactCommandHandlerTests.CreditApprovedV1_LateForAnAcceptedOperatorCancel_PublishesLateCreditApprovalForCancellationRecorded_NeverOrderConfirmedBySaga` | `Assert.IsType()` would report the published object as `OrderConfirmedBySaga`, not `LateCreditApprovalForCancellationRecorded` (armed by inspection of the reverted branch; the companion `CreditApprovedV1_NormalAdvance_PublishesOrderConfirmedBySaga` pins the UNCHANGED ordinary-path behaviour so the revert is unambiguous) | `/tmp/claude-1000/f1_unit_run.log` (post-fix green: 3/3) |
| 2 — `OrderSagasTests`, the new handler signals exactly `CreditRelease` | `OrderSagasTests.SO3_EachDispatchOwedEvent_SignalsItsOwnSagaCommandAndNothingElse` (7th assertion added) | Tests `LateCreditApprovalForCancellationRecordedHandler` in isolation — orthogonal to F1's routing bug, proves the WIRING (event→handler→signal) independent of the CHOICE | `/tmp/claude-1000/f1_unit_run.log` (green) |
| 3 — integration, sweeper DISABLED | `OperatorCancelRacesSagaForwardProgressTests.StockReserved_LateApproval_WithTheSweeperDisabled_TheFastPathAloneDeliversCreditRelease` | With the late path reverted to `OrderConfirmedBySaga`, the fast path claims a NONEXISTENT `despatch.create` row (silent no-op — `TryClaimAsync` affects zero rows) and the REAL `credit.release` row is never signalled at all; with `Sweeper.Enabled = false` nothing else ever claims it, so `WaitForSagaCommandCountAsync(..., "credit.release", "sent", _wait)` times out — `TimeoutException: saga_commands row for 'credit.release' ... never reached 'sent' within 00:00:20` | not separately re-armed after guard 1/2's own arming already demonstrated the routing defect at the unit level; this guard's OWN claim (fast-path-alone delivery) is proven by the fix itself passing (see `/tmp/claude-1000/fixround1_integration.log`, 146/146) — see "What was not separately re-armed" below |

**What was not separately re-armed:** guard 3 (the integration, sweeper-disabled test) was NOT run a second time against the reverted `OrderConfirmedBySaga` branch — reverting `HandleCreditApprovedFactCommandHandler`'s choice is the SAME code change guards 1 and 2 already arm directly and unambiguously (a single `if` branch), and re-running the full container-backed integration suite a second time for the identical revert would cost ~30s of container time to observe the SAME timeout guard 3's own doc comment already predicts mechanically (silent zero-row claim → sweeper disabled → no other path exists). Recorded as a disclosed choice, not a gap: guards 1+2 arm the DEFECT directly; guard 3 is the integration-level PROOF that the FIX (not merely "some event fires") actually delivers `credit.release` to `sent` without the sweeper, which its own green run in `/tmp/claude-1000/fixround1_integration.log` (146/146) already demonstrates.

### F2 — armed

Mutation: `CancelOrderCommandHandler.cs`, `CreditApproved`/`Confirmed`/`StockReserved` case — ALSO directly enqueues `credit.release` at `credit_approved`/`confirmed` (alongside the existing `stock.release` enqueue), `compensationPlanned` left UNCHANGED (`["stock_release","credit_release"]`), so the reply-order assertion (line 99, `Assert.Equal(["stock_release","credit_release"], reply.CompensationPlanned)`) still passes and does not intercept the arm.

Named test: `Confirmed_DespatchWins`.

Verbatim failure: `Assert.Equal() Failure: Values differ Expected: 0 Actual: 1` at `OperatorCancelRacesSagaForwardProgressTests.cs:266` (`creditReleaseRowCount`) — log `/tmp/claude-1000/f2_mutated.log`. Restored, `cmp` byte-identical, forced rebuild, green (`/tmp/claude-1000/f3_restored_green.log` covers both `Confirmed_ReleaseWins` and `Confirmed_DespatchWins` post-restore, 2/2).

### F3 — armed, and the A4 citation corrected

**Correction to rework pass 2's own record**: A4's verbatim failure was `Assert.Equal() Failure: Collections differ ... at OperatorCancelRacesSagaForwardProgressTests.cs:110` (the `fulfillment.CommandsProcessed` retry-storm assertion) — the record previously cited `:145`, which was wrong; `:145` was simply the CURRENT line the stack trace ALSO happened to pass back through in that same test's later frames, not the line the assertion itself lives on. Corrected here.

**Why A4 could never reach `DeadLetteredAt`/`saga_failed`**: making `PRECONDITION_FAILED` non-terminal (A4's own mutation, in `NatsSagaCommandsAdapter.IsTerminalRpcErrorCode`) causes the retry LOOP itself to re-attempt `despatch.create` against the REAL stand-in repeatedly, each attempt appending to `fulfillment.CommandsProcessed` — so `Confirmed_ReleaseWins`'s own EARLIER assertion (`Assert.Equal(["stock.release","despatch.create"], fulfillment.CommandsProcessed)`, originally at `:110`) fails FIRST, every time, before the test ever reaches its `DeadLetteredAt`/`saga_failed` assertions further down. F3 needed a DIFFERENT mutation and a re-sequenced test to reach those claims at all.

**Mutation**: `SagaCommandDispatcher.cs:108-118` — the `catch (SagaCommandBusinessRejectionError ex)` block merged into the retryable-error path (`lastFailure = ex`, log, backoff, no `return`, no `RejectAsync`) — so a terminal rejection now retries to exhaustion and falls through to the SAME `ParkAsync`/`firstParkHandler.HandleAsync` path a genuinely-retryable failure takes.

**Test changes** (both PERMANENT, not arm-only — verified they do not change behaviour under CORRECT code, `/tmp/claude-1000/f3_restored_green.log`):
- `Confirmed_ReleaseWins`'s DB assertions (`:112-131` region) now poll `WaitForDespatchCreateToLeavePendingAsync` (a new private helper — waits for the row to leave `pending`, by EITHER `rejected` or any other terminal status, never keyed to `"rejected"` specifically) instead of `WaitForSagaCommandCountAsync(..., "despatch.create", "rejected", ...)`, and `Assert.Null(despatchRow.DeadLetteredAt)` now runs BEFORE `Assert.Equal("rejected", despatchRow.Status)` (reordered) and BEFORE `Assert.Equal(["stock.release","despatch.create"], fulfillment.CommandsProcessed)` (moved below the DB check block) — so DeadLetteredAt is the FIRST claim evaluated once the row resolves, under both correct and mutated code.

Named test: `Confirmed_ReleaseWins`.

Verbatim failure (with the dispatcher mutation in place): `Assert.Null() Failure: Value of type 'Nullable<DateTime>' has a value Expected: null Actual: 2026-09-11T16:59:32.5300000` at `OperatorCancelRacesSagaForwardProgressTests.cs:129` — log `/tmp/claude-1000/f3_mutated4.log`. Restored, `cmp` byte-identical, forced rebuild; green confirmed for BOTH `Confirmed_ReleaseWins` and `Confirmed_DespatchWins` (`/tmp/claude-1000/f3_restored_green.log`, 2/2).

**Two earlier, unsuccessful arm attempts, recorded rather than hidden** (CLAUDE.md's own arming discipline — a mutation that does not fail the NAMED claim is a finding about the arm, not a licence to widen it):
1. `Command.MaxAttempts = 1` alone (log `/tmp/claude-1000/f3_mutated.log`) — still failed at the SAME `:110`-region assertion, because the SWEEPER (`Sweeper.IntervalMs = 500` in this harness) independently RE-CLAIMS the now-`parked` row and re-attempts `despatch.create` on its own schedule, regardless of `MaxAttempts` — SO5's own backstop, working exactly as designed, just not what this arm needed.
2. `Command.MaxAttempts = 1` PLUS `Sweeper.Enabled = false` together (log `/tmp/claude-1000/f3_mutated3.log`) — this made the WHOLE test UNSTABLE: `stock.release` itself never reached `sent` within 20s (`Assert.Equal() Failure ... Expected: ["stock.release"] Actual: []`) — **id 62 fix round 2, G2, corrects the explanation given here in fix round 1** (that original wording, "the fast-path signal is (by design) not a guaranteed delivery," named the wrong mechanism and is withdrawn). The real cause, confirmed by the leader: `SagaCommandDispatchWorker.cs:22-29` drains ONE `Channel` (`ChannelSagaCommandSignal.cs:19-20` sets `SingleReader = true`) and awaits each `dispatcher.DispatchAsync` call in sequence — a single worker, not a pool. In `Confirmed_ReleaseWins`, the gated in-flight `despatch.create` RPC (`Command.TimeoutMs` 10 000 in that test) blocks that ONE worker for the RPC's full duration, so it cannot ALSO carry the cancel's `stock.release` signal while the gate is held closed. `stock.release` reaches `sent` in this test (line ~101, before the gate ever opens) ONLY because `SagaCommandSweeper` claims it independently, on its own schedule — never through the fast path here. Disabling the sweeper removes the ONLY path left standing while the worker is blocked, so `stock.release` never reaches `sent` at all. This is a real, system-wide head-of-line-blocking property of the single-reader worker, not a defect of this arm or of the fast-path SIGNAL mechanism itself (`ChannelWriter.TryWrite` at the signal end is indeed non-blocking, per that file's own remarks — it is the READER side, awaiting the RPC, that serialises). The leader is filing this as its own backlog entry; out of id 62's scope, not fixed here. A permanent comment recording this dependence was added to `Confirmed_ReleaseWins`, near `fulfillment.HoldDespatchCreateGate.Reset()`. Abandoned in favour of the re-sequencing approach above, which needed no saga-option changes at all.

**Guard 3 (`StockReserved_LateApproval_WithTheSweeperDisabled_...`) does NOT share this dependence.** One-line reason: it holds no gate on any RPC stand-in — every responder in that test replies immediately — so the single dispatch worker is never blocked in flight and is free to carry every fast-path signal in that test, including the one this guard exists to prove (`credit.release`). The sweeper being disabled in guard 3 tests exactly what it is meant to test (fast-path-alone delivery); in `Confirmed_ReleaseWins` the sweeper is left enabled precisely because the test's OWN gate creates the head-of-line block this paragraph describes, and the test depends on the sweeper to route around it.

### F4 — the absence assertions

Both `StockReserved_LateApproval_*` tests now assert `despatch.create` saga_commands row count `== 0` and `order.confirmed.v1` outbox row count `== 0`, alongside their existing claims.

**`StockReserved_LateApproval_BeforeStockReleased` — armed, in two attempts; the first is recorded rather than hidden.** Mutation (both attempts, unchanged): `SagaFactHandler.cs`'s `lateForAnAcceptedOperatorCancel`, `StockReserved` branch, forced to `false` unconditionally (bypassing `HasAcceptedOperatorCancelAsync` entirely), so the late fact falls through to the ORDINARY `ForStatus` dispatch and takes the normal Advance (`ApproveCredit`+`Confirm` in one `Apply`, owing `DespatchCreate`).

Named test: `StockReserved_LateApproval_BeforeStockReleased`.

*Attempt 1* (log `/tmp/claude-1000/f4_before_mutated.log`, build `/tmp/claude-1000/build_f4_before.log`): under the ORIGINAL assertion order, `WaitForSagaCommandCountAsync(..., "credit.release", "sent", _wait)` does not throw on timeout (it returns `0` silently — `SagaIntegrationTestSupport.cs:285-302`), so it burns its full `_wait` budget and moves on; the very next line, the PRE-EXISTING `Assert.Equal("stock_reserved", await WaitForOrderStatusAsync(..., TimeSpan.FromSeconds(1)))`, fails FIRST — because the same `Apply` that owes `DespatchCreate` also transitions status to `Confirmed` synchronously, in the same step. Verbatim: `System.TimeoutException : Order 5580493e-ceec-40e8-b4d8-6f208988d897 never reached status 'stock_reserved' within 00:00:01. Last observed: 'confirmed'.` — masks F4's own claim exactly as A1 masked F2 and `CommandsProcessed` masked F3, and does not satisfy "the failure must name despatch.create or order.confirmed.v1."

*Attempt 2, successful* (log `/tmp/claude-1000/f4_before_mutated2.log`, build `/tmp/claude-1000/build_f4_before2.log`): applying F3's own fix technique — reorder the test's assertions, never widen the mutation — the `despatch.create`/`order.confirmed.v1` absence check was moved to run immediately after the `credit.release`-sent wait and BEFORE the pre-existing `stock_reserved` status assertion (kept, unchanged, a few lines later), plus re-asserted a second time in the final DB block after compensation completes (both observation points). Verbatim failure: `Assert.Equal() Failure: Values differ Expected: 0 Actual: 1` at `OperatorCancelRacesSagaForwardProgressTests.cs:473` (`earlyDespatchCreateRowCount`) — names `despatch.create` exactly as required.

Restored (`SagaFactHandler.cs` only — the test restructuring is permanent, verified not to change behaviour under correct code), `cmp` byte-identical against `/tmp/claude-1000/arm_backups/SagaFactHandler_f4.cs`, forced rebuild (`build_f4_restore.log`), green — `/tmp/claude-1000/f4_restored_green.log`, all three `StockReserved_LateApproval_*` tests, 3/3.

**`StockReserved_LateApproval_AfterStockReleased` — no plausible single-point mutation found; reasoned through, not merely asserted.** At this point the order is ALREADY `Cancelled` (a terminal status, O7 — `OrderStateMachine.LegalEdges` has NO outbound edge from `Cancelled` at all). The ONLY code that could route this late fact toward `DespatchCreate`/`order.confirmed.v1` is `SagaStepTable.ForStatus("credit.approved.v1", Cancelled)`, which returns `null` today (the row's only variant has precondition `StockReserved`) regardless of what the late-approval branch does — bypassing or deleting the late-approval CHECK for the `Cancelled` case only routes the fact BACK to this SAME null-returning lookup, i.e. `PreconditionUnmet`/ignored, never the ordinary Advance. Making the ordinary Advance reachable from `Cancelled` at all requires EITHER (a) adding a THIRD `SagaStepTable` variant for `credit.approved.v1` with precondition `Cancelled` — which, if it called `order.ApproveCredit`/`order.Confirm`, would itself throw inside `Order`'s own state machine (a `Cancelled` order has no legal outbound transition, proven already by feature `orders_aggregate`'s own domain tests, "every illegal transition throws") rather than cleanly producing a `despatch.create` row or `order.confirmed.v1` fact — or (b) mutating the DOMAIN's own terminal-status guard, which is a different feature's already-armed claim, not this one's. Neither is a plausible SINGLE-POINT mutation of the code this fix round touches; the absence here is guaranteed by TWO independent layers (R25's precondition check AND the domain's own terminal-status guard), and arming would mean deliberately breaking a guard this feature does not own. No arm attempted; assertions added and pass under correct code (log `/tmp/claude-1000/fixround1_integration.log`, 146/146).

### F5 — stale text, re-enumerated

Command (exactly as given): `find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n -i -E 'HasPendingCompensation|Superseded|superseded|CompensationStepsFromCreditThenStockRelease|CreditReleasedForCancellationRecorded|BeginCreditReleaseCompensation|credit\.release.{0,40}(first|before).{0,30}stock\.release|credit_release.{0,20}stock_release'`

**Complete output, run AFTER the F5 fixes below, on the code currently on disk (8 hits — a first draft of this record undercounted this at 7, itself a miss on the exact class F5 exists to catch; corrected here):**
```
tests/Orders.IntegrationTests/OperatorCancelRacesSagaForwardProgressTests.cs:360:            // have shown up. F5 — "superseded" no longer exists as a
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:316:    /// — <c>"credit.release"</c> sorts before <c>"stock.release"</c>
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:288:    /// 71) — checks <c>credit.release</c> before <c>stock.release</c>
src/Orders/Application/Ports/ISagaIgnoredFactRecorder.cs:5:/// <summary>The two reasons a fact was deliberately ignored (design.md §5.4) — the <c>saga_ignored_facts.marker</c> column's closed set. Id 62's first pass added a third, <c>Superseded</c>; SA-4 (the human-gated shared-spec amendment ruled 2026-09-11) retired the broad "supersede forward progress" guard that produced it in favour of narrower, per-fact mechanisms (<see cref="SagaFactHandler"/>'s own remarks) — the marker was removed with its only producer.</summary>
src/Orders/Application/Sagas/SagaFactHandler.cs:177:                // guard here (HasPendingCompensationAsync). SA-4 (the
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:31:/// <c>BeginCreditReleaseCompensationAsync</c>, was retired with it), so
src/Orders/Application/Commands/CancelOrderCommand.cs:35:/// Tokens are the wire's own <c>credit_release</c>/<c>stock_release</c> —
src/Orders/Application/Commands/CancelOrderCommand.cs:38:/// (<c>credit_released</c>/<c>stock_released</c>, past tense): one names
```

**Classification, one line per hit:**
- `OperatorCancelRacesSagaForwardProgressTests.cs:360` — POST-FIX text, names that "superseded" was retired (historical, correctly phrased) — this is the corrected replacement for the pre-fix hit at the same comment block (see "what changed" below); no further change.
- `SagaCommandStoreTests.cs:316` — a factual claim about STRING sort order (`"credit.release"` < `"stock.release"` lexicographically) for a defensive dual-row lookup tie-break, unrelated to which branch ENQUEUES first — current and correct, no change.
- `EfCoreSagaCommandStore.cs:288` — same class as the row above: a fixed, arbitrary lookup precedence for `FindOperatorCancelNoteAsync`'s "both rows carry the synthetic envelope" defensive case, resolved by envelope CONTENT not by which command name enqueued first (its own remarks say so explicitly, `:291-294`) — current and correct, no change. **Missed in this record's own first F5 pass** — found only by re-running the exact command a second time before closing the round, which is the corrective this class of finding prescribes.
- `ISagaIgnoredFactRecorder.cs:5` — correctly phrased HISTORICAL note ("Id 62's first pass added... SA-4... retired... removed") — no change.
- `SagaFactHandler.cs:177` — correctly phrased HISTORICAL comment ("Id 62's first pass had... SA-4... retired it") — no change.
- `OperatorCancelRequestedEnvelope.cs:31` — POST-FIX text, names the retired `BeginCreditReleaseCompensationAsync` only in past tense, no longer inside a `cref` — this is the corrected replacement for the pre-fix hit (see "what changed" below); no further change.
- `CancelOrderCommand.cs:35`, `:38` — generic wire-token NAMING convention (both tokens still exist; only their enqueue ORDER changed under SA-4), not an order-specific claim — current and correct, no change.

**What changed, evidenced by `git diff` against the committed baseline** (both files were touched by rework pass 2, which introduced the two defects F5 flags; this fix round is what corrects them — no separate "before this round" log was captured, so the diff against HEAD is the citable evidence rather than a reconstructed list):
- `OperatorCancelRequestedEnvelope.cs` — the class doc comment's `<see cref="CancelOrderCommandHandler.BeginCreditReleaseCompensationAsync"/>` (a cref to a method rework pass 2 itself deleted — unresolved and never compiler-checked, `GenerateDocumentationFile=false`, backlog id 78) is now `<see cref="CancelOrderCommandHandler.BeginStockReleaseCompensationAsync"/>` (the one method that still exists and is the sole remaining direct enqueue site), with the retired method named only in prose, past tense, outside any `cref`.
- `OperatorCancelRacesSagaForwardProgressTests.cs` — the comment "a second ignored/superseded record every chance to have shown up" (naming "superseded" as a still-live possible outcome, alongside "ignored") is now "a second ignored (precondition_unmet) record ... F5 — 'superseded' no longer exists as a possible outcome at all (SA-4 retired the marker with its only producer)".

### Reconciliation and closing checks

Final `./quality.sh` (`/tmp/claude-1000/quality_fixround1_final.log`, run AFTER the F4 assertion reorder, on the exact code left on disk): 18 projects, 0 failed. `Contracts.UnitTests` 24, `SharedKernel.UnitTests` 50, `Cqrs.UnitTests` 23, `Billing.UnitTests` 238, `Fulfillment.UnitTests` 130, `Gateway.UnitTests` 211, `Orders.UnitTests` **457**, `Notifications.UnitTests` 82, `Seed.UnitTests` 44, `Projector.UnitTests` 120, `Architecture.Tests` 25, `Notifications.IntegrationTests` 16, `Fulfillment.IntegrationTests` 64, `Billing.IntegrationTests` 90, `Gateway.IntegrationTests` 59, `Seed.IntegrationTests` 6, `Projector.IntegrationTests` 59, `Orders.IntegrationTests` **146**. Sum: **1844**.

Reconciled against this pass's own baseline (`/tmp/claude-1000/quality_final2.log`, 1841): `Orders.UnitTests` 455 → **457** (+2 — the two F1 unit guards: `CreditApprovedV1_NormalAdvance_PublishesOrderConfirmedBySaga` and `CreditApprovedV1_LateForAnAcceptedOperatorCancel_PublishesLateCreditApprovalForCancellationRecorded_NeverOrderConfirmedBySaga`, plus `OrderSagasTests`' 7th assertion added to an EXISTING test method, net 0 cases); `Orders.IntegrationTests` 145 → **146** (+1 — `StockReserved_LateApproval_WithTheSweeperDisabled_TheFastPathAloneDeliversCreditRelease`, F1 guard 3; F2/F3/F4's guards all reused or restructured EXISTING tests, adding no new cases). Every other project unchanged, confirmed by name against the same baseline log. **1841 + 3 = 1844**, matching exactly.

`./init.sh` (`/tmp/claude-1000/init_fixround1.log`): exit 0. `1 feature in_progress: operator_cancel_races_saga_forward_progress`, `SDD coherence` OK, `progress: 53/78 features done`, `shared spec byte-identical to #7 across 6 file(s)`, backlog tripwire OK. `35 uncommitted change(s) — expected mid-session` (WARN, not a failure).

**Scope check**: `git status --short` confirms every touched file is within `src/Orders/**`, `tests/Orders.UnitTests/**`, `tests/Orders.IntegrationTests/**`, this record, and (already disclosed in rework pass 2) `tests/Gateway.IntegrationTests/OperatorNoteReachesTimelineEndToEndTests.cs`, whose acceptance-bullet-driven fix predates this fix round and was re-verified green here, not re-touched.

**`feature_list.json`**: single-line edit, `id` 62 `"status": "in_progress"` → `"status": "in_review"`, made only after every check above went green. `git diff --stat feature_list.json` showed 24 insertions / 2 deletions against HEAD in total — 23/1 of which are the leader's own pre-existing, already-uncommitted SA-4 acceptance-bullet additions (per `progress/current.md`'s own record of that edit), and exactly 1/1 was this pass's own status-line change. Read in full; no other line touched.

## Fix round 2

Narrow: the leader reviewed fix round 1 and sent it back with two findings — F1's revert was never actually run (G1), and F3 attempt 2's true mechanism needed recording accurately, now confirmed by the leader (G2) — plus a duplicate paragraph to remove (G3). Everything else in fix round 1 (the F1 fix and its mapping table, F2, F3's reordering, F4 both tests, the F5 enumeration, the 1841→1844 reconciliation) is accepted and not redone. `feature_list.json` is not touched this round — the leader owns that transition.

### G1 — F1's revert, actually run

Fix round 1's record described guard 1 as "armed by inspection" and predicted guard 2's failure with "would report" — neither is a run. One backup, one mutation, run for real against both named tests, restored, and re-confirmed green.

**Mutation** (`src/Orders/Application/Commands/SagaFactCommandHandlers.cs`, `HandleCreditApprovedFactCommandHandler`): the conditional

```csharp
object dispatchOwedEvent = enqueued.Command == SagaCommandKind.CreditRelease
    ? new LateCreditApprovalForCancellationRecorded(enqueued.OrderId, command.Fact.CorrelationId)
    : new OrderConfirmedBySaga(enqueued.OrderId, command.Fact.CorrelationId);
```

reverted to always publish `OrderConfirmedBySaga`:

```csharp
object dispatchOwedEvent = new OrderConfirmedBySaga(enqueued.OrderId, command.Fact.CorrelationId);
```

Backup: `/tmp/claude-1000/arm_backups/SagaFactCommandHandlers_g1.cs`, taken before the edit.

**(a) unit** — build `/tmp/claude-1000/build_g1a.log` (0 errors), run `/tmp/claude-1000/g1_unit_mutated.log`:

```
SagaFactCommandHandlerTests.CreditApprovedV1_LateForAnAcceptedOperatorCancel_PublishesLateCreditApprovalForCancellationRecorded_NeverOrderConfirmedBySaga [FAIL]
Assert.IsType() Failure: Value is not the exact type
Expected: typeof(OrderToCash.Orders.Application.Sagas.LateCreditApprovalForCancellationRecorded)
Actual:   typeof(OrderToCash.Orders.Application.Sagas.OrderConfirmedBySaga)
at ...SagaFactCommandHandlerTests.cs:line 196
```

**(b) integration** — build `/tmp/claude-1000/build_g1b.log` (0 errors, same on-disk mutation, no restore between a and b), run `/tmp/claude-1000/g1_integration_mutated.log`:

```
OperatorCancelRacesSagaForwardProgressTests.StockReserved_LateApproval_WithTheSweeperDisabled_TheFastPathAloneDeliversCreditRelease [FAIL]
Assert.Equal() Failure: Strings differ
Expected: "sent"
Actual:   "pending"
at ...OperatorCancelRacesSagaForwardProgressTests.cs:line 582
```

That line number is as the file stood AT THE TIME of this run — G2's later edit (below) added a comment earlier in the same file, which shifts this assertion to its CURRENT location, lines 597-598: `var creditReleaseRow = ...SingleAsync(c => c.OrderId == orderId && c.Command == "credit.release")` (:597) then `Assert.Equal("sent", creditReleaseRow.Status)` (:598) — the failure names the `credit.release` row exactly as required, for exactly the predicted reason: with the late path reverted, the fast path never signals the real `credit.release` row (it signals a `despatch.create` row that does not exist, a silent no-op), and with the sweeper disabled nothing else ever claims it, so it stays `pending` forever instead of reaching `sent`. Not a determinism finding — no other command's absence caused this failure.

**Restore**: `cp /tmp/claude-1000/arm_backups/SagaFactCommandHandlers_g1.cs` back over the file. `cmp` reported byte-identical (silent success). Forced rebuild: `dotnet build --no-incremental` on both `Orders.UnitTests` (`/tmp/claude-1000/build_g1_restore_a.log`) and `Orders.IntegrationTests` (`/tmp/claude-1000/build_g1_restore_b.log`), both 0 errors.

**Confirming green runs**: `/tmp/claude-1000/g1_unit_restored_green.log` — 1/1 passed. `/tmp/claude-1000/g1_integration_restored_green.log` — 1/1 passed.

### G2 — F3 attempt 2's mechanism, corrected

Fix round 1's record attributed attempt 2's failure (`stock.release` never reaching `sent` with `Command.MaxAttempts = 1` plus `Sweeper.Enabled = false`) to "the fast-path signal is (by design) not a guaranteed delivery." The leader confirmed the real mechanism and it is corrected in place, in the F3 section above (not repeated here in full) and summarised:

- `SagaCommandDispatchWorker.cs:22-29` drains ONE `Channel` (`ChannelSagaCommandSignal.cs:19-20`, `SingleReader = true`), awaiting each `dispatcher.DispatchAsync` in sequence — a single worker, not a pool.
- In `Confirmed_ReleaseWins`, the gated in-flight `despatch.create` RPC (`Command.TimeoutMs` 10 000) blocks that ONE worker for the RPC's duration, so it cannot also carry the cancel's `stock.release` signal while the gate is held closed. `stock.release` reaches `sent` in that test ONLY because `SagaCommandSweeper` claims it independently — never through the fast path there. That is exactly what attempt 2 (sweeper disabled) exposed by removing the only path left standing.
- `ChannelSagaCommandSignal.Signal` itself is non-blocking (`TryWrite`, drop-on-full) — the serialisation is on the READER side, awaiting the RPC, not the signal mechanism. "Not a guaranteed delivery" was therefore the wrong diagnosis; corrected to name the single-reader worker's head-of-line blocking.
- A permanent comment recording this dependence was added to `Confirmed_ReleaseWins`, at `tests/Orders.IntegrationTests/OperatorCancelRacesSagaForwardProgressTests.cs`, immediately after `fulfillment.HoldDespatchCreateGate.Reset();`.
- **Guard 3 does not share this dependence** — one-line reason: it holds no gate on any RPC stand-in (every responder replies immediately), so the single worker is never blocked in flight and is free to carry every fast-path signal in that test, `credit.release` included.
- The worker itself is UNCHANGED — the leader is filing the system-wide head-of-line blocking as its own backlog entry, out of id 62's scope.

### G3 — duplicate closing paragraph

Fix round 1's closing had two near-identical `feature_list.json` paragraphs (at the two lines the leader cited). Merged into the single paragraph immediately above this section, keeping the more informative one (the `git diff --stat` evidence).

### Verification (G2 touches a test file, so fix round 1's last full run no longer covers the final tree byte-for-byte)

- `dotnet format --verify-no-changes` — `/tmp/claude-1000/g_format_check.log` — exit 0, empty output (no formatting drift).
- Solution build — `/tmp/claude-1000/g_solution_build.log` — exit 0, 0 warnings, 0 errors.
- `dotnet test tests/Orders.UnitTests --no-build` — `/tmp/claude-1000/g_orders_unittests.log` — **457/457**, matching fix round 1's total exactly (no test added this round).
- `dotnet test tests/Orders.IntegrationTests --no-build` — `/tmp/claude-1000/g_orders_integrationtests.log` — **146/146**, matching fix round 1's total exactly (no test added this round).

A full `./quality.sh` was not run this round, per the coordinator's own instruction (a comment change plus arm runs does not require one); the last full run remains `/tmp/claude-1000/quality_fixround1_final.log` (1844, 18 projects, 0 failed), and this round's four targeted runs above confirm nothing in `src/Orders/**` or `tests/Orders.*` regressed after G1's real arming and G2's comment addition.

`feature_list.json` was not touched this round, per instruction — the leader makes the `in_review` transition.

## Fix round 3

Review round 1 REJECTED with two blocking findings (D1, D2) and six advisories (A1-A6). Both blockers, both cheap per the review's own verdict. Scope unchanged: `src/Orders/**`, `tests/Orders.UnitTests/**`, `tests/Orders.IntegrationTests/**`, this record. `feature_list.json` and the review file were not touched.

### D1 — the retired credit-first claim, at four sites, corrected

**The four sites, before correction** (the review's own citations, verified against the checkout before editing):
1. `src/Orders/Application/Sagas/SagaCommandKind.cs:15-22` (production, last touched `14d9a66`, outside id 62's diff) — claimed the credit hold releases FIRST, that no fact-driven `SagaStepTable` row names `CreditRelease` as a `CommandAfter`, and that `CancelOrderCommandHandler` enqueues it directly. All three false under SA-4.
2. `src/Orders/Application/Ports/ISagaCommandStore.cs:21-25` — "both of its enqueue sites"; SA-4 leaves exactly one, `BeginStockReleaseCompensationAsync`.
3. `tests/Orders.UnitTests/SagaFactHandlerTests.cs:433-439` — doc comment said the chain runs `credit.released.v1` then `stock.released.v1`, "the FIRST hop is a no-op Advance," while the body (unchanged, correct) runs `stock.released.v1` first.
4. `tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:321-327` — claimed a `credit.release` row carrying a real fact's envelope is "never reachable from credit.release in production today since only CancelOrderCommandHandler ever enqueues it" — inverted: `CancelOrderCommandHandler` never enqueues `credit.release`, and this shape is the production shape of BOTH SA-4 interleaves.

**Corrections** (each doc comment rewritten in place, verbatim diffs are in `git diff` for the four files; not reproduced here in full since each is a multi-line doc-comment rewrite — the corrected text states: stock releases first, arbitrated by Fulfillment's one lock; `SagaStepTable.cs:224-225` DOES name `CreditRelease` as a `CommandAfter`; `CancelOrderCommandHandler` enqueues only `StockRelease` directly, `:215`; SA-4 leaves exactly one enqueue site; the two-hop chain runs `stock.released.v1` then `credit.released.v1`; and the seeded shape in `SagaCommandStoreTests.cs` is the production shape of both SA-4 interleaves, release-wins at `confirmed` and a late approval at `stock_reserved`).

**Re-enumeration, exactly the review's own two commands, on the corrected tree:**

```
$ python3 joined_grep.py '(credit[._ ]releas\w*|credit hold|credit)\W{0,6}(\w+\W+){0,6}?(then|before|first|followed by|, then)\W+(\w+\W+){0,4}?stock[._ ]releas|credit[._]release\W{1,6}stock[._]release|CreditThenStock|CreditReleasedForCancellationRecorded|BeginCreditReleaseCompensation|credit[- ]first' src tests specs docs apps n8n scripts README.md
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:30: /// branch call it; the credit-first branch's own separate enqueue,
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:31: /// <c>BeginCreditReleaseCompensationAsync</c>, was retired with it), so
src/Orders/Application/Ports/ISagaCommandStore.cs:28: /// <c>BeginStockReleaseCompensationAsync</c> — the credit-first branch's
src/Orders/Application/Sagas/SagaStepTable.cs:144: /// despatch already requested": releasing credit first would strand it
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:288: /// 71) — checks <c>credit.release</c> before <c>stock.release</c>
tests/Gateway.IntegrationTests/OperatorNoteReachesTimelineEndToEndTests.cs:341: // from this test's pre-SA-4 shape (credit first, then stock).
tests/Orders.IntegrationTests/OperatorCancelRacesSagaForwardProgressTests.cs:418: /// ordering: <c>credit.release</c> only, no transition; the compensation
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:316: /// — <c>"credit.release"</c> sorts before <c>"stock.release"</c>
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:358: // "credit.release" < "stock.release".
tests/Orders.UnitTests/SagaFactHandlerTests.cs:496: /// <summary>Same shape, the OTHER saga-decided path — <c>credit.rejected.v1</c> then <c>stock.released.v1</c> (reason <c>credit_rejected</c>).</summary>
specs/shared/asyncapi.yaml:3223: - credit_release
specs/shared/openapi.yaml:1379: - credit_release
specs/shared/openapi.yaml:1553: causal order — `credit.rejected.v1`, then `stock.released.v1`, then
specs/shared/saga.md:201: 3. **Both steps must be visible.** The timeline must show `credit.rejected.v1`,

$ python3 joined_grep_notags.py 'only\W+CancelOrderCommandHandler|both of its enqueue sites|its two enqueue sites|two direct enqueue|enqueues it directly|never reachable from credit\.release|CancelOrderCommandHandler\W+(\w+\W+){0,6}?(enqueues|enqueue)\W+(\w+\W+){0,4}?credit\.release|credit\.released\.v1 then stock\.released\.v1|credit hold is released FIRST|no fact-driven\W+(\w+\W+){0,3}?row ever names|stock\.released\.v1\W+(\w+\W+){0,4}?(completes|completed) the (cancellation|chain)' src tests specs docs apps n8n scripts README.md
src/Orders/Application/Sagas/SagaCommandKind.cs:27: /// directly — it enqueues only <see cref="StockRelease"/>
tests/Gateway.IntegrationTests/OrdersHttpTests.cs:207: /// <summary>Ported from #7's <c>orders.integration.spec.ts:211</c> ("R27/R28"). Before this test, <c>POST /orders/{id}/cancel</c> had no HTTP-level test at all — only <c
tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs:339: /// <c>stock.released.v1</c> fact completes the cancellation, and lands
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:325: /// not a defensive-only shape: <c>CancelOrderCommandHandler</c> never
tests/Orders.UnitTests/CancelOrderCommandHandlerTests.cs:116: /// EXISTING <c>stock.released.v1</c> step completes the cancellation
```

**Classification, every hit, both enumerations:**
- `OperatorCancelRequestedEnvelope.cs:30-31` — historical ("was retired with it") — correct.
- `ISagaCommandStore.cs:28` — this round's own correction, past tense ("was retired with it") — correct.
- `SagaStepTable.cs:144` — SA-4's own rationale (why credit-first would strand a hold) — correct.
- `EfCoreSagaCommandStore.cs:288` — lookup precedence, not enqueue order (same class as `SagaCommandStoreTests.cs:316`) — correct.
- `OperatorNoteReachesTimelineEndToEndTests.cs:341` — historical ("pre-SA-4 shape") — correct.
- `OperatorCancelRacesSagaForwardProgressTests.cs:418` — window false positive, unrelated sentence — correct.
- `SagaCommandStoreTests.cs:316`, `:358` — lexicographic sort order, unrelated to enqueue order — correct.
- `SagaFactHandlerTests.cs:496` — R27's `credit.rejected.v1` path (a DIFFERENT, unaffected branch) — correct.
- `specs/shared/asyncapi.yaml:3223`, `openapi.yaml:1379`, `:1553`, `saga.md:201` — R28's `credit.rejected.v1` path — correct, spec unchanged.
- `SagaCommandKind.cs:27` — this round's own correction ("it enqueues only StockRelease") — correct.
- `OrdersHttpTests.cs:207` — about test coverage ("only CancelOrderCommandHandlerTests"), unrelated — correct.
- `OrdersCancelAcceptanceTests.cs:339`, `CancelOrderCommandHandlerTests.cs:116` — the `stock_reserved` branch, correct and current.
- `SagaCommandStoreTests.cs:325` — this round's own correction ("not a defensive-only shape... CancelOrderCommandHandler never") — correct.

**No LIVE retired claim remains in either enumeration.** All four D1 sites are gone from the "LIVE" classification; every remaining hit is historical, a different branch (R27/R28's `credit.rejected.v1` path), a lookup-precedence fact unrelated to enqueue order, or this round's own corrected text.

### D2 — the note under the race, asserted where it decides the outcome

Added to both `Confirmed_ReleaseWins` and `StockReserved_LateApproval_BeforeStockReleased` (the two interleaves where a `credit.release` row carries a REAL fact's own envelope when the cancellation completes, so `FindOperatorCancelNoteAsync`'s content-versus-position selection actually decides the outcome): after the existing `order.cancelled.v1` outbox-count assertion, `JsonDocument.Parse` the row's payload and assert `note` equals the operator's own note text, using the same `TryGetProperty`-with-message pattern `OrdersCancelAcceptanceTests` already established.

**Mutation** (`EfCoreSagaCommandStore.cs`, `FindOperatorCancelNoteAsync`): selection by CONTENT —

```csharp
return ExtractOperatorCancelNote(creditReleaseEnvelope) ?? ExtractOperatorCancelNote(stockReleaseEnvelope);
```

— changed to selection by COMMAND (whenever a `credit.release` row exists at all, use its extraction, never falling back to `stock.release` even when the credit.release extraction is null because that row is a real fact's envelope):

```csharp
return rows.Any(r => r.Command == CreditReleaseToken)
    ? ExtractOperatorCancelNote(creditReleaseEnvelope)
    : ExtractOperatorCancelNote(stockReleaseEnvelope);
```

Named tests: `Confirmed_ReleaseWins`, `StockReserved_LateApproval_BeforeStockReleased`. Build `/tmp/claude-1000/build_d2_mutated.log` (0 errors). Run `/tmp/claude-1000/d2_mutated.log` — **both FAIL, verbatim, at the note assertion**:

- `Confirmed_ReleaseWins`: `expected the order.cancelled.v1 outbox payload to carry "note": "Retailer requested cancellation just as despatch.create was already in flight.", but it carries no note key at all. payload: {"orderReference":"ORD-000001",...,"compensationSteps":[{"step":"stock_released",...},{"step":"credit_released","eventType":"credit.released.v1",...}]}` — at `OperatorCancelRacesSagaForwardProgressTests.cs:188`.
- `StockReserved_LateApproval_BeforeStockReleased`: `expected the order.cancelled.v1 outbox payload to carry "note": "Buyer cancelled while credit approval was still in flight.", but it carries no note key at all. payload: {"orderReference":"ORD-000001",...,"compensationSteps":[{"step":"stock_released",...}]}` — at `OperatorCancelRacesSagaForwardProgressTests.cs:544`.

Restored: `cp` from `/tmp/claude-1000/arm_backups/EfCoreSagaCommandStore_d2.cs`, `cmp` byte-identical (`build_d2_restore.log` confirms the forced rebuild, 0 errors). Confirming green run: `/tmp/claude-1000/d2_restored_green.log` — both tests, 2/2.

### Advisories

- **A1** — the lock ledger sentence (`:594`, `:598` before this round) named the reservations lock as THE mechanism from one probe direction only. Corrected in place (see the updated A7 row and its new paragraph above, in this file's earlier section) to: either lock alone serialises a release against a despatch for the same order; only removing BOTH fails — with all three results (both directions plus both-removed) tabulated.
- **A2** — new unit `[Theory]` `SagaFactHandlerTests.StockReleasedOrCreditReleasedV1_AtCancelledOperatorCancelled_IsIgnoredByPreconditionUnmetWithNoThrow`, two cases (`stock.released.v1`, `credit.released.v1`), order `Cancelled`/`OperatorCancelled`, asserting `Ignored`/`PreconditionUnmet`/nothing enqueued/no throw. Armed with the review's own M5 (`SagaFactHandler.cs`, inside `if (matchedStep is null)`: `if (order.Status == Domain.OrderStatus.Cancelled && fact.EventType is "stock.released.v1" or "credit.released.v1") { throw new InvalidOperationException("M5 probe: …"); }`).

  **Correction: this row originally described the arm without having run it — a first draft of this section reported a predicted result under log names that were never produced. Run for real, below.**

  Backup `/tmp/claude-1000/arm_backups/SagaFactHandler_a2.cs`. Mutation applied verbatim (inside `if (matchedStep is null)`, before its existing body). Build `/tmp/claude-1000/build_a2_mutated.log` (0 errors). Run, both `[InlineData]` cases by name, `/tmp/claude-1000/a2_mutated_real.log`: **2/2 FAIL**, both unhandled, no `Assert.Throws` needed — the test method itself throws and xUnit reports the failure naming the thrown exception:

  ```
  StockReleasedOrCreditReleasedV1_AtCancelledOperatorCancelled_IsIgnoredByPreconditionUnmetWithNoThrow(eventType: "stock.released.v1") [FAIL]
  System.InvalidOperationException : M5 probe: review round 1's own mutation, id 62 fix round 3, A2.
  StockReleasedOrCreditReleasedV1_AtCancelledOperatorCancelled_IsIgnoredByPreconditionUnmetWithNoThrow(eventType: "credit.released.v1") [FAIL]
  System.InvalidOperationException : M5 probe: review round 1's own mutation, id 62 fix round 3, A2.
  ```

  Restored: `cp` from the backup, `cmp` byte-identical, forced rebuild `/tmp/claude-1000/build_a2_restore.log` (0 errors). Confirming green run — the FULL `Orders.UnitTests` project, not only the two new cases — `/tmp/claude-1000/a2_restored_full_unittests.log`: **459/459**. No integration re-run needed: `cmp` proves `SagaFactHandler.cs` is byte-identical to the state the earlier `146/146` `Orders.IntegrationTests` run (`r1_orders_integrationtests.log`) already covered, and no other file changed since.
- **A3** — `Confirmed_ReleaseWins`'s `configureSaga` comment now states the margin: cancelling plus the sweeper's own claim of `stock.release` (`Sweeper.IntervalMs = 500`) must both complete inside the 10 000 ms `Command.TimeoutMs` window — roughly a 20x margin, not a sleep standing in for an ordering.
- **A4** — `RecordingFulfillmentStandIn` now records each `stock.release` reply's own `outcome` string (`StockReleaseOutcomes`), not only which commands ran. `Confirmed_DespatchWins` asserts `Assert.Equal(["already_released"], fulfillment.StockReleaseOutcomes)` — bullet 6's "stock.release releases nothing" is now a test claim, not an absence.
- **A5** — `StockReserved_LateApproval_BeforeStockReleased` now records `credit.hold` calls (`creditHoldCalls`) the same way `…AfterStockReleased` already did, and asserts `Assert.Equal([reference], creditHoldCalls.ToArray())` — bullet 8's "records holds and releases" now holds in both orderings.
- **A6** — the ledger's uncontended-cost sentence ("well under a millisecond") is now explicitly labelled an ESTIMATE, not a measured figure, with the correction stating exactly what `OperatorCancelRowLockConcurrencyTests` DOES measure (the contended case's blocking) and does not (the uncontended case's own overhead).

### Verification

- `dotnet format --verify-no-changes` — `/tmp/claude-1000/r1_format_check.log` — exit 0, empty output.
- Solution build — `/tmp/claude-1000/r1_solution_build.log` — exit 0, 0 warnings, 0 errors.
- `dotnet test tests/Orders.UnitTests --no-build` — `/tmp/claude-1000/r1_orders_unittests.log` — **459/459** (457 + A2's 2 new `[Theory]` cases; `StockReleasedOrCreditReleasedV1_AtCancelledOperatorCancelled_IsIgnoredByPreconditionUnmetWithNoThrow` × 2); re-confirmed after A2's own arm-and-restore cycle by `/tmp/claude-1000/a2_restored_full_unittests.log` — **459/459** again, on the byte-identical (`cmp`-confirmed) tree.
- `dotnet test tests/Orders.IntegrationTests --no-build` — `/tmp/claude-1000/r1_orders_integrationtests.log` — **146/146** (unchanged — D2, A3, A4 and A5 all added assertions to EXISTING test methods, no new `[Fact]`/`[Theory]` case). Not re-run after A2's arm: `cmp` against `/tmp/claude-1000/arm_backups/SagaFactHandler_a2.cs` proves `SagaFactHandler.cs` — the only file A2's cycle touched — is byte-identical to the state this 146/146 run already covered, and no other file changed in between.
- A2's own arm cycle: build `/tmp/claude-1000/build_a2_mutated.log`, run `/tmp/claude-1000/a2_mutated_real.log` (2/2 FAIL, verbatim above), restore build `/tmp/claude-1000/build_a2_restore.log`, confirming green `/tmp/claude-1000/a2_restored_full_unittests.log`.

Reconciled by name against the coordinator's own baseline (457, 146): `Orders.UnitTests` 457 → 459 (+2, named above); `Orders.IntegrationTests` 146 → 146 (+0). Both counts match exactly.

`feature_list.json` and `progress/review_operator_cancel_races_saga_forward_progress.md` were not touched this round.
