# Dead-letter `FirstFailedAt` semantics — guard hardening (backlog id 75, #8 half)

## What was wrong

`tests/Orders.UnitTests/FactRetryDispatcherTests.cs`'s
`DeadLetterPublication_FirstFailedAt_IsTheFirstAttemptsClockReading_NeverALaterOne`
drove a fixture (`AdvancingClockFailingProcess`, old shape) whose own comment
said *"invocation 1 leaves the clock at t0"* — so the dispatcher's entry read
(`enteredAt = clock.UtcNow` at `FactRetryDispatcher.cs:73`) and attempt 1's
own failure read (`firstFailedAt ??= clock.UtcNow` at `:97`) observed the
**same** clock value, t0. The assertion `FirstFailedAt == t0` could not tell
"the entry instant" from "attempt 1's own failure instant" apart, so it was
vacuous on exactly the distinction SA-3
(`specs/shared/asyncapi.yaml:2227-2229`) exists to protect: *"`x-first-failed-at`
is the instant the FIRST processing attempt failed — never the instant
processing began."*

Production code was already correct and is unchanged by this task.

## What changed (tests only)

`tests/Orders.UnitTests/FactRetryDispatcherTests.cs`:

1. **Renamed and rewrote** the existing test to
   `DeadLetterPublication_FirstFailedAt_IsTheFirstAttemptsFailureInstant_NeverTheEntryInstantNorALaterOne`.
   The fixture now advances the clock **during attempt 1 itself**, before it
   throws, so entry (t0) and attempt 1's failure (t1) are two different
   values; the clock keeps advancing on attempts 2 and 3 (t2, t3) so the last
   attempt's reading is distinct too. Asserts `FirstFailedAt == t1` (not t0,
   not t3) and `FailedAt == t3`.
2. **Added** `DeadLetterPublication_FirstFailedAt_EqualsFailedAt_WhenOnlyOneAttemptWasMade`
   — SA-3's second clause: with `MaxAttempts = 1`, `FirstFailedAt` and
   `FailedAt` must be equal (both are necessarily the same clock call's
   instant, since the loop runs exactly once).
3. **Rewrote** the shared fixture `AdvancingClockFailingProcess` so it
   advances the clock to the invocation-indexed value on **every** attempt,
   including the first (`clock.UtcNow = advanceOnEachAttempt[Invocations - 1]`
   then throw) — the old fixture only advanced from the second invocation
   onward, which is exactly what made attempt 1's failure indistinguishable
   from entry.

No other file was touched. `src/Orders/Infrastructure/Messaging/FactRetryDispatcher.cs`
is unmodified — `git status --porcelain` and `git diff --stat` on it both
return empty after this task.

## Traceability

- SA-3 (`specs/shared/asyncapi.yaml:2227-2229`) — proven by both new/rewritten
  tests above.

## Arming (backlog id 75's own requirement)

Mutated `src/Orders/Infrastructure/Messaging/FactRetryDispatcher.cs:97` from:

```csharp
firstFailedAt ??= clock.UtcNow;
```

to:

```csharp
firstFailedAt ??= enteredAt; // ARMED MUTATION (backlog id 75) — renders the entry instant, not the first attempt's own failure instant.
```

Ran `dotnet test tests/Orders.UnitTests/Orders.UnitTests.csproj --filter "FullyQualifiedName~FactRetryDispatcherTests"` against the mutation. Result: **2 failed, 7 passed, 9 total.**

Verbatim failure output:

```
[xUnit.net 00:00:00.42]     OrderToCash.Orders.UnitTests.FactRetryDispatcherTests.DeadLetterPublication_FirstFailedAt_IsTheFirstAttemptsFailureInstant_NeverTheEntryInstantNorALaterOne [FAIL]
[xUnit.net 00:00:00.46]     OrderToCash.Orders.UnitTests.FactRetryDispatcherTests.DeadLetterPublication_FirstFailedAt_EqualsFailedAt_WhenOnlyOneAttemptWasMade [FAIL]
  Failed OrderToCash.Orders.UnitTests.FactRetryDispatcherTests.DeadLetterPublication_FirstFailedAt_IsTheFirstAttemptsFailureInstant_NeverTheEntryInstantNorALaterOne [32 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 2026-09-11T10:00:01.0000000+00:00
Actual:   2026-09-11T10:00:00.0000000+00:00
  Stack Trace:
     at OrderToCash.Orders.UnitTests.FactRetryDispatcherTests.DeadLetterPublication_FirstFailedAt_IsTheFirstAttemptsFailureInstant_NeverTheEntryInstantNorALaterOne() in /home/juanpabloperez/Work/Projects/Assessments/order-to-cash-dotnet/tests/Orders.UnitTests/FactRetryDispatcherTests.cs:line 249
--- End of stack trace from previous location ---
  Failed OrderToCash.Orders.UnitTests.FactRetryDispatcherTests.DeadLetterPublication_FirstFailedAt_EqualsFailedAt_WhenOnlyOneAttemptWasMade [1 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 2026-09-11T10:00:01.0000000+00:00
Actual:   2026-09-11T10:00:00.0000000+00:00
  Stack Trace:
     at OrderToCash.Orders.UnitTests.FactRetryDispatcherTests.DeadLetterPublication_FirstFailedAt_EqualsFailedAt_WhenOnlyOneAttemptWasMade() in /home/juanpabloperez/Work/Projects/Assessments/order-to-cash-dotnet/tests/Orders.UnitTests/FactRetryDispatcherTests.cs:line 283
--- End of stack trace from previous location ---

Failed!  - Failed:     2, Passed:     7, Skipped:     0, Total:     9, Duration: 67 ms - OrderToCash.Orders.UnitTests.dll (net10.0)
```

Both guards fired on the exact behaviour SA-3 forbids.

**Restore:** `cp` from the pre-mutation backup taken before the edit, then
`cmp` against the backup confirmed byte-identical, then `git diff --stat` and
`git status --porcelain` on the file both returned empty (it matches the last
committed state exactly — no `git checkout` was used). Forced the rebuild
with `touch` on the restored file before the confirming run; the confirming
run's build output shows `Orders -> …/OrderToCash.Orders.dll` recompiled
(not skipped), so the confirming green run executed the restored source, not
a stale binary.

Confirming run after restore + forced rebuild:
`Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 66 ms`.

## Test counts

- Filtered (`FactRetryDispatcherTests`) before mutation: **9/9 passed**.
- Filtered after mutation (armed): **2 failed, 7 passed, 9 total**.
- Filtered after restore: **9/9 passed** (rebuild confirmed, see above).
- Full `Orders.UnitTests` suite: **471/471 passed**, Duration 10 s. Reconciles
  exactly against the task's stated baseline of 470 (470 + 1 new test added
  = 471; the pre-existing test was renamed and rewritten in place, not
  duplicated).

## What was not done / out of scope

Nothing outside `tests/Orders.UnitTests/FactRetryDispatcherTests.cs` was
touched, per the task's bounds. `feature_list.json` was not edited. No
commit was made.

## Surprises

None — the task's own precedent (the #7 half's rejected call-ordinal fake)
correctly predicted the failure mode: the existing fixture's "advance from
attempt 2 onward" shape was exactly the same blind spot as a call-ordinal
fake, just phrased as a clock instead of a counter. Reusing the repository's
own `AdvanceOnceThenFailProcess` pattern (already present a few lines below
in the same file, added for D8/review round 3) — advance the clock inside
the invocation before throwing — was sufficient to fix both this test and
supply the single-attempt clause test with no new abstractions.
