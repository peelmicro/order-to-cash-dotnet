# impl: outbox_relay_deadlock_victim_escapes_run_once (backlog id 87)

## Status

Implementation complete. All new and existing tests green. Ready for review.

## What was built

**The mechanism (established by decompiling the installed EF Core 10.0.11 assemblies — `ilspycmd`, not inference from the SQL Server hint text):**

`db.Database.CreateExecutionStrategy()`, with the write model's plain `UseSqlServer(...)` registration (no `EnableRetryOnFailure`), returns `Microsoft.EntityFrameworkCore.SqlServer.Storage.Internal.SqlServerExecutionStrategy`. Its `RetriesOnFailure` is a hard-coded `false`, and its `ExecuteAsync` body is exactly:

```csharp
try { return await operation(...); }
catch (Exception ex) when (CallOnWrappedException(ex, SqlServerTransientExceptionDetector.ShouldRetryOn))
{
    throw new InvalidOperationException(SqlServerStrings.TransientExceptionDetected, ex);
}
```

It never retries anything — it **diagnoses** a transient failure (`SqlServerTransientExceptionDetector.ShouldRetryOn`, decompiled: SQL error 1205 — deadlock victim — is explicitly one of the cases) and then only **wraps and rethrows**. That is why `RunOnceAsync` "escapes": the strategy already in force correctly identifies the failure and does nothing about it.

**The fix**: `DeadlockRetryExecutionStrategy`, a small `ExecutionStrategy` subclass constructed directly (`new DeadlockRetryExecutionStrategy(db)`), used **only** at `OutboxRelay.RunOnceAsync`'s own call site — never via a `DbContext`-wide `EnableRetryOnFailure`, which `EfCoreUnitOfWork`'s own remarks forbid (a retried delegate there could commit aggregate rows whose already-drained domain events never reach the outbox). It retries (max 3, delay up to 200ms) **only** `SqlException` number 1205, checking both the raw shape (claim SELECT, `BeginTransactionAsync`/`CommitAsync`) and the shape actually hit by the stamp step (see below).

Verified via `ilspycmd` decompilation of `Microsoft.EntityFrameworkCore.Storage.ExecutionStrategy.OnFirstExecution()` that constructing this strategy directly is safe regardless of the ambient `DbContext`'s own (non-retrying) configured strategy: the "does not support user-initiated transactions" guard is evaluated against the **instance whose `ExecuteAsync` is actually running**, not the context's registered factory, and it fires only if a transaction is already open when execution starts — never true here, since `BeginTransactionAsync` happens inside the delegate.

## Files touched

- `src/Orders/Infrastructure/Outbox/DeadlockRetryExecutionStrategy.cs` — new, canonical.
- `src/Orders/Infrastructure/Outbox/OutboxRelay.cs` — `RunOnceAsync` now constructs `DeadlockRetryExecutionStrategy` instead of `db.Database.CreateExecutionStrategy()`.
- `src/Fulfillment/Infrastructure/Outbox/DeadlockRetryExecutionStrategy.cs` — new, identical copy (see "Why Fulfillment and Billing too" below).
- `src/Fulfillment/Infrastructure/Outbox/OutboxRelay.cs` — same one-line change.
- `src/Billing/Infrastructure/Outbox/DeadlockRetryExecutionStrategy.cs` — new, identical copy.
- `src/Billing/Infrastructure/Outbox/OutboxRelay.cs` — same one-line change.
- `tests/Orders.IntegrationTests/OutboxRelayDeadlockTests.cs` — new: `OI15_RunOnceAsync_SurvivesAConstructedDeadlockVictimInsteadOfPropagatingTheSqlException`, the deterministic reproduction (bullet 1) and the arming target (bullet 5).
- `tests/Orders.UnitTests/DeadlockRetryExecutionStrategyTests.cs` — new: 5 pure tests of `ShouldRetryOn` via reflection, closing a real gap the integration test alone left open (see defeat-list row 9 below).

## Why Fulfillment and Billing too — not an assumption, a discovered guard

The brief's bounds said "src/Orders/ (and any other service the enumeration proves affected)". I initially fixed only Orders, ran `Orders.UnitTests`, and it broke two **pre-existing** tests I had not written:

```
OutboxRelayParityTests.HoldsEveryWriteModelsCopyOfTheOutboxRelayFamilyByteIdenticalToTheCanonicalAfterTheBannerAndTheNamespaceLine
  Fulfillment's OutboxRelay.cs diverges from the canonical src/Orders/Infrastructure/Outbox/OutboxRelay.cs
  outside the banner, the namespace line and the using lines, at normalised line 57:
  canonical="" vs copy="        var strategy = db.Database.CreateExecutionStrategy();".

OutboxRelayParityTests.KeepsTheCanonicalFamilyAdoptableVerbatimNamingNoServiceAndImportingNothingServiceSpecific
  src/Orders/Infrastructure/Outbox/OutboxRelay.cs:76 names 'Orders' outside the banner/namespace line
```

This is `design.md §8.3/§8.4`'s own guard (`tests/Orders.UnitTests/OutboxRelayParityTests.cs`): `OutboxRelay.cs` is one of seven files design.md holds byte-identical (after banner/namespace/using normalisation) across every service with a relational outbox — currently Orders (canonical), Fulfillment, Billing (`RequiresTheOutboxRelayFamilyFromEveryServiceThatOwnsARelationalOutboxConfiguration`, driven off which services have `Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs` — Notifications and Projector do not). This is exactly CLAUDE.md's "when the work targets a defect class, enumerate the class repository-wide before fixing any instance" — except here an **existing test enumerated it for me** and told me the fix must land in all three, not just where the notice was raised.

So: I ported the identical fix to Fulfillment and Billing, and rewrote both `OutboxRelay.cs`'s new comment block and all of `DeadlockRetryExecutionStrategy.cs` to be service-token-free (no "Orders"/"Fulfillment"/etc. outside a `using` line), matching the same "adoptable, verbatim" discipline the other six guarded files already follow. `DeadlockRetryExecutionStrategy.cs` itself is **not** in the seven-file guarded list (`_guardedFileNames` in `OutboxRelayParityTests.cs`) — adding it would be amending a different, already-closed feature's `design.md`-governed scope, which is outside this backlog entry's bounds — but I made it byte-identical across all three services anyway as a matter of the same discipline, verified with a plain `diff` after normalisation (all three empty).

Confirmed after the fix: `OutboxRelayParityTests` 3/3, `Fulfillment.UnitTests` 134/134, `Fulfillment.IntegrationTests` 64/64, `Billing.UnitTests` 242/242, `Billing.IntegrationTests` 90/90, `Architecture.Tests` 36/36, `Orders.UnitTests` 470/470, `Orders.IntegrationTests` 147/147 (146 pre-existing + OI15).

## Bullet 1 — the deterministic reproduction

**The natural OI4 race could not be reproduced, and I can show why rather than just report the failure to reproduce.** `OutboxRelay`'s claim uses `WITH (UPDLOCK, READPAST, ROWLOCK)`: READPAST means a row already locked by another session is **skipped, never waited for** — by SQL Server's own specification, not a probabilistic tendency. A statement that never waits can never be the blocked half of a lock-manager-detected cycle. And the transaction's only other lock-relevant statement — the stamp `ExecuteUpdateAsync` — only ever converts U-locks the same transaction's own claim already holds to X, a conversion SQL Server's lock manager gives priority over any new, queued, incompatible request (the entire reason Update locks exist). So a genuine two-relay row-lock deadlock cannot form through this code's own two statements, regardless of timing.

I verified this empirically before writing any fix: 40 iterations of the exact OI4 shape (two real relays, `Task.WhenAll`, fresh Testcontainers database each time) produced zero exceptions, then cross-checked against SQL Server's own `system_health` Extended Events session (`sys.dm_xe_session_targets` → `xml_deadlock_report`) for a deadlock graph that never fired even once — a scratch test, run once for evidence and deleted afterward, never landed in the repo.

**The constructed reproduction (`OI15`)** — a real, guaranteed SQL Server deadlock, not a probability — exploits the one statement that genuinely lacks a lock hint: the stamp's `ExecuteUpdateAsync`.
1. `OutboxRelay` claims rows A and B (uncontested), then pauses inside a controllable `PublishAsync` (the same seam `OI13` already established), holding UPDLOCK(A), UPDLOCK(B).
2. A second, raw ADO.NET connection takes a `HOLDLOCK`-forced S-lock on row A — compatible with U, so it is granted immediately even with the relay's transaction still open.
3. That connection's second statement asks for X on row B — genuinely blocked (X is incompatible with the relay's held U).
4. The relay is released: its stamp needs to convert U(A)→X(A) — genuinely blocked (X is incompatible with the competitor's held S).

Relay waits for the competitor (wants A, held S); the competitor waits for the relay (wants B, held U) — a real, SQL-Server-detected two-resource cycle, not a race. `SET DEADLOCK_PRIORITY HIGH` on the competitor's session (the relay's is left at the server default, NORMAL) makes **which** side is killed a deterministic property of the test, not a coin flip — "a change of KIND, not of probability" applies to the outcome, not just the occurrence.

Verified stable: 4 consecutive green runs of `OI15` alone (one during development, three in an explicit repeat loop) plus every full-suite run reported above.

**A genuine defect found and fixed in the test's own construction, not in production code**: my first attempt used `SET payload = 'contended-by-oi15'` for the competitor's second statement, a real, non-JSON write. Once the relay's first attempt was correctly killed and automatically retried, its retry re-claimed row B and tried to publish it — hitting a `JsonReaderException` from the now-corrupted `payload` column, an entirely self-inflicted defect in the harness (a plain `SET payload = payload` — a genuine X-lock-taking write that changes nothing — closes the cycle without corrupting anything the retry later reads).

## Bullet 2 — the class, enumerated

`find . -not -path '*/bin/*' -not -path '*/obj/*' \( -iname '*.cs' \) -print0 | xargs -0 grep -n "CreateExecutionStrategy()\|UseSqlServer("`, classified one line per hit (excluding test-side `MsSqlContainerFixture`/`*DbContextFactory` files, which own no retry-strategy call themselves):

| Site | Retrying strategy configured? | What a deadlock victim does there |
|---|---|---|
| `src/Orders/Infrastructure/Outbox/OutboxRelay.cs:67` (now `DeadlockRetryExecutionStrategy`) | **Yes, now** (was: no) | Retried up to 3× on error 1205 only; everything else unchanged |
| `src/Fulfillment/Infrastructure/Outbox/OutboxRelay.cs:68` (now fixed, this entry) | **Yes, now** | same |
| `src/Billing/Infrastructure/Outbox/OutboxRelay.cs:68` (now fixed, this entry) | **Yes, now** | same |
| `src/Orders/Infrastructure/Persistence/EfCoreUnitOfWork.cs:35` | No | Wraps and rethrows (same `InvalidOperationException` shape) — **judged acceptable as-is; see below** |
| `src/Fulfillment/Infrastructure/Persistence/EfCoreUnitOfWork.cs:28` | No | same — acceptable as-is, same reasoning |
| `src/Billing/Infrastructure/Persistence/EfCoreUnitOfWork.cs:27` | No | same — acceptable as-is, same reasoning |
| `src/Notifications/Infrastructure/Persistence/EfCoreUnitOfWork.cs:26` | No | same — acceptable as-is, same reasoning |
| `src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:126` (`ClaimDueAsync`) | No | Wraps and rethrows — **not fixed here; flagged for a new backlog entry, see below** |
| `src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs` `TryClaimAsync`/`MarkSentAsync`/`ParkAsync`/etc. | No explicit strategy — bare `ExecuteUpdateAsync`/`SaveChangesAsync` outside any transaction | EF Core internally wraps each such call in the context's own **configured** (non-retrying) strategy; same wrap-and-rethrow shape on a deadlock | 
| `src/Orders/Infrastructure/OrdersOutboxServiceCollectionExtensions.cs:30` (`AddDbContext<OrdersDbContext>(db => db.UseSqlServer(...))`) | No `EnableRetryOnFailure` | The registration this whole class sits under — deliberately left unchanged (see fix scoping above) |
| `src/Fulfillment/Infrastructure/FulfillmentServiceCollectionExtensions.cs:30`, `src/Billing/Infrastructure/BillingServiceCollectionExtensions.cs:30`, `src/Notifications/Infrastructure/NotificationsServiceCollectionExtensions.cs:31` | No `EnableRetryOnFailure` | same — unchanged |
| `*.DbContextFactory.cs` (design-time factories, four services) | No | Design-time only, never on a live request path — not applicable |
| `tests/**/MsSqlContainerFixture.cs`, `tests/**/*.csproj`-referenced test-only `UseSqlServer(...)` calls | No | Test infrastructure, not production — not applicable |

**Relay is where it was noticed, not where the class ends.** The identical `db.Database.CreateExecutionStrategy()` shape sits in every `EfCoreUnitOfWork` (four services) and in `EfCoreSagaCommandStore.ClaimDueAsync`/its bare `ExecuteUpdateAsync` calls. All are enumerated; only the three `OutboxRelay` copies are fixed here, for the reasons in bullet 6.

## Bullet 3 — the mechanism, established

Covered in "What was built" above. In short: the current strategy diagnoses transient failures correctly and never retries; the fix is a narrowly-scoped, explicitly-constructed retrying strategy, never a `DbContext`-wide `EnableRetryOnFailure` (which `EfCoreUnitOfWork`'s own remarks — verified still present and unchanged — forbid).

## Bullet 4 — is the claim's own ordering the cause?

**No, and it is provably not, not merely "not observed to be."** Both relays' claim SELECTs scan `ORDER BY seq ASC` — the *same* direction, not opposite — and even if they scanned oppositely, `READPAST` means neither can ever become a blocked waiter on a row another transaction holds; it skips instead. A classic "opposite lock order" cycle requires **both** parties to be capable of waiting; this code's claim step structurally cannot. See the `DeadlockRetryExecutionStrategy.cs` remarks (`THE ORDERING QUESTION`) for the full argument, backed by the 40-iteration empirical probe against `system_health`. Reordering the claim would fix nothing, because the claim was never the vulnerable statement — the un-hinted stamp `ExecuteUpdateAsync` is, and `OI15` targets exactly that.

## Bullet 5 — armed

**Production guard (`OI15`)**: backed up `OutboxRelay.cs`, reverted `RunOnceAsync`'s strategy line to `db.Database.CreateExecutionStrategy()` (pre-fix behaviour), forced a clean rebuild (`dotnet build --no-incremental`), ran `OI15` by name. **Failed**, verbatim:

```
System.InvalidOperationException : An exception has been raised that is likely due to a transient failure. Consider enabling transient error resiliency by adding 'EnableRetryOnFailure' to the 'UseSqlServer' call.
---- Microsoft.Data.SqlClient.SqlException : Transaction (Process ID 62) was deadlocked on lock resources with another process and has been chosen as the deadlock victim. Rerun the transaction.
...
Error Number:1205,State:51,Class:13
```

Restored from the backup, confirmed with `cmp` (byte-identical) and `git diff` (showed only the intended fix against `HEAD`, nothing else), forced a rebuild (`touch` + `--no-incremental`), reran `OI15`: green.

**Pure-logic guard (`DeadlockRetryExecutionStrategyTests`)**: mutated `ShouldRetryOn` to `=> false`, rebuilt, ran the five tests — the two "should retry" tests failed (`Assert.True() Failure — Actual: False`), the three "should not retry" tests stayed green (expected: a false negative would not manifest through them). Mutated to `=> true`, rebuilt, reran — the three "should not retry" tests failed (`Assert.False() Failure — Actual: True`), the two "should retry" tests stayed green. Restored from backup, `cmp` byte-identical, forced rebuild, reran: 5/5 green. Both mutation directions are recorded because a single direction would have left half the claim (the false-positive half, or the false-negative half) unarmed.

## Bullet 6 — judged acceptable as-is, stated explicitly

**`EfCoreUnitOfWork` (all four services): left unfixed, deliberately.** A deadlock victim there means the *whole* aggregate-write transaction rolled back — nothing partially committed, by definition of a transaction. The `SqlException`/wrapped `InvalidOperationException` propagates out of the command handler to the RPC responder, which (per this repository's own saga design) reports failure to its caller; the caller (the saga orchestrator's own retry/backoff, or the original REST caller via the Gateway) is the layer that already owns "try the whole request again". Retrying *inside* `EfCoreUnitOfWork` is the one thing its own remarks explicitly forbid (a retried delegate could commit aggregate rows whose already-drained domain events never reach the outbox — `design.md §4.5`'s OI9 hazard) — so the safe fix this backlog entry applies to `OutboxRelay` does **not** transfer here without redesigning how domain events are drained, which is out of scope for a deadlock-handling fix. **Consequence for the caller's own loop**: an aggregate-write deadlock surfaces as an ordinary command failure, exactly as any other transient persistence error already does today — no new failure mode, and the existing retry/backoff at the RPC/saga layer already covers it.

**`EfCoreSagaCommandStore.ClaimDueAsync` and its sibling bare `ExecuteUpdateAsync`/`SaveChangesAsync` calls: enumerated, not fixed, and I am recommending a new backlog entry rather than leaving this as prose.** Per CLAUDE.md's rule that a disclosure whose root cause spans more than the entry that found it becomes a numbered backlog entry, not a sentence in a review: this shares the exact `SqlServerExecutionStrategy`-wraps-and-rethrows shape, and `ClaimDueAsync`'s own claim+stamp pattern is structurally similar enough to `OutboxRelay`'s (a `READPAST`-protected `UPDLOCK` claim, a same-transaction stamp) that the same category of fix likely applies — but I have not constructed a reproduction against it, and id 80's own review already established that `TryClaimAsync`'s races are "by design" for a *different* reason (silent no-op on zero rows affected, not an escaping exception). Fixing it here would be scope creep past what this entry's reproduction proves; I am not the leader and cannot add to `feature_list.json` per this entry's bounds, so I am naming it here for the leader to file.

## The ten-row defeat list

Run against `OI15` and `DeadlockRetryExecutionStrategyTests` together (search: `grep -n "defeat list" CLAUDE.md`, table read from `CLAUDE.md` on disk, not from a stale injected copy):

| # | Attack | Run? | Result / why it does not apply |
|---|---|---|---|
| 1 | Delete the behaviour | **Armed** | See bullet 5 — reverted to pre-fix `CreateExecutionStrategy()`, `OI15` failed with the verbatim deadlock message |
| 2 | Corrupt a payload field the test supplied | Partially N/A | This guard's claim is behavioural (no exception escapes), not a data field on the wire — but I DID find and fix a real self-inflicted corruption in the harness itself (the competitor's original `SET payload = 'contended-by-oi15'`, which broke the relay's own retry) while building `OI15`; documented above |
| 3 | Substitute a valid sibling identifier | **Armed** (adapted) | `DeadlockRetryExecutionStrategyTests` substitutes error 1205 for a real, different SQL Server error number (1222, lock request timeout) and confirms `ShouldRetryOn` returns `false` — proving the check is keyed to the *specific* number, not "any SqlException" |
| 4 | Shadow the pattern from a comment or string literal | N/A | Neither guard is a text scanner; both exercise real code (a real deadlock, a reflection-invoked method on a real instance) |
| 5 | Hide in a dead region (`#if false`) | N/A | No conditional compilation involved |
| 6 | Hide in a raw/verbatim string | N/A | Same reason as row 4 |
| 7 | Drop an optional element entirely | N/A | No optional element in this guard's shape |
| 8 | Compare a literal to a literal | N/A | `OI15` asserts on a REAL exception from a REAL SQL Server deadlock; `DeadlockRetryExecutionStrategyTests` invokes the REAL method via reflection on a REAL instance — neither compares two hard-coded literals |
| 9 | Satisfy the closer half of a two-part claim and leave the premise half stale | **Found and closed** | `OI15` alone only exercises the WRAPPED shape (`ExecuteUpdateAsync`'s own internal non-retrying strategy wraps the SqlException in `InvalidOperationException` before `DeadlockRetryExecutionStrategy` ever sees it — confirmed from `OI15`'s own failure trace during development). `DeadlockRetryExecutionStrategyTests` was added specifically to prove the OTHER premise — a RAW, unwrapped `SqlException` — is retried too, with both directions (retry / do-not-retry) armed for both shapes |
| 10 | Let a build-output copy join the population | N/A | Neither guard enumerates a population from the filesystem |

## Traceability

This is `sdd: false` — no `R<n>` requirements and no `specs/shared/test-matrix.md` row apply. Backlog id 87's six acceptance bullets are addressed one-by-one above.

## What I could not do / left open

- Did not fix `EfCoreSagaCommandStore.ClaimDueAsync` or `EfCoreUnitOfWork` (all four services) — see bullet 6 for the explicit judgement on each, and the recommendation to file a new backlog entry for `ClaimDueAsync`.
- Did not add `DeadlockRetryExecutionStrategy.cs` to `OutboxRelayParityTests`'s guarded seven-file list — that list is `design.md §8.3`-governed, from an already-closed feature, and widening it is a scope decision beyond this entry; I kept the three copies byte-identical anyway as a matter of the same discipline.

## What surprised me

- The natural OI4 race is not merely "hard to reproduce" — it is **structurally impossible** via the claim step, provably from `READPAST`'s own specification and SQL Server's own U-lock-conversion-priority guarantee. I did not expect to be able to *prove* a negative this cleanly from documented lock semantics alone; the 40-iteration empirical probe against `system_health` was corroboration, not the main evidence.
- The stamp's `ExecuteUpdateAsync` wraps itself in the `DbContext`'s own configured (non-retrying) execution strategy **independently** of whatever outer strategy is calling `RunOnceAsync` — meaning my first `ShouldRetryOn` (checking only `exception is SqlException`) silently failed to retry, because what actually reached it was already an `InvalidOperationException`. This was only visible by instrumenting the test with `ITestOutputHelper` logging and reading the real exception at each step, not by reasoning about the code alone — and it is exactly the "wrong-shape wrapping" class this backlog's own bullet 3 asked me to establish rather than assume.
- An existing, unrelated parity test (`OutboxRelayParityTests`) caught the scope of the fix for me — it is worth noting as a positive example of the "ported-idiom ledger" style discipline this repository already has in place for exactly this failure mode (a fix landed in one service's copy of a file the codebase deliberately keeps identical across several).
