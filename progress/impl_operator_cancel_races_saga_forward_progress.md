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
index (`id` is the PK), well under a millisecond, before the pre-existing `SingleOrDefaultAsync`
read. Measured directly, over a real MS-SQL Testcontainer
(`OperatorCancelRowLockConcurrencyTests`): the contended case (second caller genuinely blocked)
resolves in ~2s only because the TEST deliberately holds the first transaction open for 2s to
force provable overlap — the lock itself adds no measurable overhead once granted.

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
