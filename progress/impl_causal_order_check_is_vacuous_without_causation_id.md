# impl: causal_order_check_is_vacuous_without_causation_id (id 105)

Light, test-only change (CLAUDE.md Cost discipline). Filed from `progress/review_api_tests.md` N1
(id 31 review, 2026-09-17). Touches only `tests/Gateway.IntegrationTests/BlackBoxApiTests.cs`.

## What changed

`AssertCausalOrder(JsonElement events)` (line ~503 before this change) could pass having asserted
nothing: if every entry's `causationId` was absent, unparsable, or named nothing in the timeline,
the loop ran to completion without ever hitting the one `Assert.True` inside it — exactly #7's
review N7 gap, ported without its recorded caveat.

1. **`AssertCausalOrder` now counts the cause→effect edges it actually resolves and checks**
   (`edgesChecked`, incremented only when `causeIndex` is found in `indexOfEventId` — i.e. only
   when the ordering assertion for that entry actually runs). It takes a new optional
   `minimumEdgesChecked` parameter (default `1`), and after the loop asserts
   `edgesChecked >= minimumEdgesChecked`. The failure message says, verbatim, **"no causal edges
   were checked"** when the count is zero, and names the observed/required counts otherwise.
2. **The happy-path call site** (`HappyPath_ReachesCompleted_WithTheCompletionTripleCausallyOrdered`)
   now passes `minimumEdgesChecked: 3` — a MEASURED floor, not a guess (see "Determining the
   happy-path minimum" below), with a comment citing that a real run's own timeline resolves
   exactly three edges and that one of them is `order.completed.v1`'s own edge to
   `credit.released.v1` (`src/Orders/Application/Sagas/SagaStepTable.cs:260`, read directly, not
   inferred).
3. **The compensation-chain call site** (`NinetyNineOrder_CompensatesVisibly_WithTheCompensationChainCausallyOrdered`)
   keeps the default `minimumEdgesChecked: 1` — already a real improvement over the pre-existing
   "can pass on zero" shape; the brief only asked for an explicit minimum where one is justified,
   and I did not measure that scenario's own true floor (no run was spent on it — see "What I did
   not do").
4. **New fleet-free meta-guard**, beside the existing
   `AssertCausalOrder_WhenAnEffectPrecedesItsCause_FailsNamingTheViolation`:
   `AssertCausalOrder_WhenNoEntryCarriesACausationId_FailsNamingNoEdgesChecked` — a hand-built
   two-entry timeline with no `causationId` field on either entry, asserting the helper throws
   with a message containing `"no causal edges were checked"`.

## Determining the happy-path minimum (researched, not assumed)

Rather than guess a number, I instrumented the real call: I temporarily set
`AssertCausalOrder(eventsArray, minimumEdgesChecked: 1000)` at the happy-path call site, rebuilt,
and ran `HappyPath_ReachesCompleted_WithTheCompletionTripleCausallyOrdered` once against the real
fleet. It failed with:

```
AssertCausalOrder checked only 3 causal edge(s), fewer than the required minimum 1000 — events:
order.placed.v1,stock.reserved.v1,credit.approved.v1,order.confirmed.v1,order.despatched.v1,
invoice.issued.v1,payment.received.v1,credit.released.v1,order.completed.v1
```

That is the real, current floor for this scenario's nine-entry timeline: exactly 3 of the 9
entries have a `causationId` that resolves to another entry in the SAME timeline (the rest name a
command id or a cross-service fact this order's own timeline does not carry, which
`AssertCausalOrder` correctly skips rather than asserts). I set `minimumEdgesChecked: 3` and
re-ran — green (see "Verification" below). I did not further identify which three specific pairs
these are beyond the one I could verify by reading source
(`order.completed.v1 ← credit.released.v1`, `SagaStepTable.cs:260`); the comment at the call site
says only what is evidenced.

## The arm (real, once)

1. `cp src/Gateway/Infrastructure/Persistence/MongoOrderReadModel.cs` to a scratchpad backup;
   confirmed identical with `md5sum` immediately after the copy.
2. Mutated `ToEvent` (`MongoOrderReadModel.cs:124-134`) so the `causationId` field is never mapped
   — the ternary that reads `GatewayReadModelCollection.Fields.Event.CausationId` from Mongo was
   replaced with a bare `null`.
3. `dotnet build tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj -c Debug
   --no-incremental` — succeeded, 0 warnings, 0 errors.
4. Ran only `HappyPath_ReachesCompleted_WithTheCompletionTripleCausallyOrdered`. It failed, verbatim:

   ```
   no causal edges were checked — every entry's causationId was absent, unparsable, or named
   nothing in this timeline: order.placed.v1,stock.reserved.v1,credit.approved.v1,
   order.confirmed.v1,order.despatched.v1,invoice.issued.v1,payment.received.v1,
   credit.released.v1,order.completed.v1
   ```

   This is exactly the guard's own claim: with `causationId` never reaching the wire, the helper
   now fails instead of passing vacuously.
5. Restored `MongoOrderReadModel.cs` from the scratchpad backup with `cp`, then confirmed byte-
   identical with `cmp` (exit 0, no output — "CMP OK: files identical").
6. Rebuilt with `dotnet build tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj -c
   Debug --no-incremental` — succeeded, 0 warnings, 0 errors.
7. Re-ran the same fact — green (`Passed: 1, Failed: 0`).

Ports checked free before starting: `ss -ltn | grep -E ':9092|:1433|:4222|:27017'` returned nothing.

## Verification (counts)

- `dotnet build` (both the single project and the full `OrderToCash.sln`) — 0 warnings, 0 errors.
- `dotnet format --verify-no-changes` — clean, exit 0.
- The two fleet-free meta-guard facts:
  `AssertCausalOrder_WhenAnEffectPrecedesItsCause_FailsNamingTheViolation` and the new
  `AssertCausalOrder_WhenNoEntryCarriesACausationId_FailsNamingNoEdgesChecked` — both green
  together (`Passed: 2, Failed: 0`).
- The happy-path fact `HappyPath_ReachesCompleted_WithTheCompletionTripleCausallyOrdered` — green
  both before the arm (with `minimumEdgesChecked: 3`) and after restore
  (`Passed: 1, Failed: 0` each time).
- Final combined run of all three facts together: `Passed: 3, Failed: 0`.
- `./init.sh` — exits 0, "environment and state are coherent".
- `git status --short` after all edits — only `tests/Gateway.IntegrationTests/BlackBoxApiTests.cs`
  changed relative to the session's starting tree; `src/Gateway/Infrastructure/Persistence/MongoOrderReadModel.cs`
  shows no diff (confirmed restored).

## What I did not do

- Did not run `./quality.sh` or the whole project, per the brief.
- Did not measure or pass an explicit `minimumEdgesChecked` for the compensation-chain call site
  (`NinetyNineOrder_CompensatesVisibly_WithTheCompensationChainCausallyOrdered`); it keeps the
  default of `1`. The brief's acceptance bullet only names "the happy path's known minimum", and I
  did not want to spend a second fleet run measuring a number nobody asked for.
- Did not touch `feature_list.json` or `specs/shared/test-matrix.md` — the brief said to touch
  only `BlackBoxApiTests.cs` and this record; `R24`'s existing test-matrix row already cites
  `AssertCausalOrder_WhenAnEffectPrecedesItsCause_FailsNamingTheViolation` as the arming test and
  did not ask to be updated for this backlog item.
- Ran no git command that writes the index or working tree; created no commit.

## Files touched

- `tests/Gateway.IntegrationTests/BlackBoxApiTests.cs` (the fix)
- `progress/impl_causal_order_check_is_vacuous_without_causation_id.md` (this record)

`src/Gateway/Infrastructure/Persistence/MongoOrderReadModel.cs` was mutated and restored during
the arm; it carries no net change (confirmed with `cmp` against the pre-arm backup).
