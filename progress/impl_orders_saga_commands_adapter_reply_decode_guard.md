# Implementation report — `orders_saga_commands_adapter_reply_decode_guard` (feature 110, phase 25 close-out, LIGHT)

**Status at close:** left as `pending` in `feature_list.json` — the brief forbids touching that file this round ("Do not touch `specs/shared/`, `feature_list.json`, or `CLAUDE.md`"), which overrides the generic "set status to `in_review`" instruction. The leader/caller should perform the status transition after reading this report and the diff.

**Result: PASS**

## 1. What was built

The gap, exactly as the brief and the feature's own `notes` field described it: `NatsSagaCommandsAdapter.SendAsync<TRequest, TReply>` (`src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs`) called `RpcJson.IsErrorBody(reply.Data)` and `RpcJson.Deserialize<TReply>(reply.Data)` with no surrounding `try`/`catch (JsonException)`, so a malformed (non-JSON) reply on any of the six saga-command subjects threw a bare `System.Text.Json.JsonException` (in practice `JsonReaderException`) instead of the classified `SagaCommandTransportError` the saga retry/terminal-classification logic expects.

Fixed by porting the identical, already-shipped pattern from the sibling class `NatsStockAvailabilityChecker.CheckAsync` (feature 46, Advisory A1): the whole decode block — from `if (RpcJson.IsErrorBody(reply.Data))` through the success-path `return RpcJson.Deserialize<TReply>(reply.Data);` — is now wrapped in `try { ... } catch (JsonException) { throw new SagaCommandTransportError(subject, "reply payload was not valid JSON."); }`, with a comment citing both the sibling fix and #7's `nats-saga-commands.adapter.ts:170-178` as the guard being ported (per the feature's own title). `SagaCommandBusinessRejectionError`/`SagaCommandTransportError` thrown *inside* the try block for the classified-`RpcError` path are untouched — they are not `JsonException`, so the new `catch` does not intercept them (mirrors the sibling: `StockCheckBusinessError` is thrown inside its own try/catch(JsonException) block the same way).

### Files touched

- `src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs` — added `using System.Text.Json;` and the `try`/`catch (JsonException)` wrap around the reply-decode block (26 insertions, 8 deletions; verified with `git diff`, no unrelated changes).
- `tests/Orders.IntegrationTests/NatsSagaCommandsAdapterReplyDecodeGuardTests.cs` — new file: one integration test plus a small stand-in-responder helper (`MalformedStandInResponder`), both scoped to this feature.

No other file was touched (confirmed: `git status --porcelain` on both paths shows exactly `M` and `??` for these two files and nothing else).

## 2. Ported-idiom ledger row (CLAUDE.md "Porting from #7")

| # 7 relied on | in #8 that property is supplied by |
|---|---|
| `nats-saga-commands.adapter.ts:170-178` wraps its reply-body JSON.parse in a try/catch that maps a parse failure to the adapter's own transport-error type, so a garbled reply never reaches the caller as a raw parse exception. | `NatsSagaCommandsAdapter.SendAsync`'s new `try { ... } catch (JsonException) { throw new SagaCommandTransportError(...); }`, wrapping `RpcJson.IsErrorBody`/`RpcJson.Deserialize<TReply>` — the exact seam, ported from the sibling `NatsStockAvailabilityChecker.CheckAsync` (feature 46 Advisory A1) rather than reinvented, since #8 already carries that idiom one class over. Named, armed guard: `NatsSagaCommandsAdapterReplyDecodeGuardTests.R110_...` (§4 below). |

Citation for the "#7 relied on X" half was supplied by the brief (already verified by the leader per its own "facts already verified" section) — not re-derived here, per the brief's instruction not to re-derive it. The `#8`-side guard was armed directly (§4).

## 3. Traceability

This is an `sdd: false` feature (backlog id 110, not a shared spec `R<n>`); `specs/shared/test-matrix.md` has no row for it (confirmed: `grep -n "110\|orders_saga_commands_adapter_reply_decode_guard" specs/shared/test-matrix.md` returns nothing), so no test-matrix update applies. The feature's own three acceptance bullets map to:

1. *"a malformed (non-JSON) reply body on any saga-command subject ... raises `SagaCommandTransportError`, never a bare `JsonException`"* — proven by `NatsSagaCommandsAdapterReplyDecodeGuardTests.R110_AMalformedNonJsonReplyOnASagaCommandSubject_ThrowsSagaCommandTransportError_NeverABareJsonException`, exercised against `stock.reserve` (`fulfillment.stock.reserve`). The acceptance text names all six subjects; the code path is the single shared generic `SendAsync<TRequest,TReply>` for all of them (confirmed by reading the six one-line wrapper methods above `SendAsync` in `NatsSagaCommandsAdapter.cs` — every one of `ReserveStockAsync`/`ReleaseStockAsync`/`CreateDespatchAsync`/`HoldCreditAsync`/`IssueInvoiceAsync`/`ReleaseCreditAsync` delegates straight into it with no per-subject branching before the try/catch), so one subject proves the shared seam without six mechanically identical repeats — matching the brief's own "e.g. `stock.reserve`" wording.
2. *"proved by an integration test over a real broker with a stand-in responder answering non-JSON bytes, mirroring `NatsStockAvailabilityCheckerTests`' equivalent malformed-reply case"* — the new test lives in `tests/Orders.IntegrationTests/` (real NATS via Testcontainers, `NatsContainerFixture`/`NatsCollection`, exactly `NatsStockAvailabilityCheckerTests`' own collection), against a real `NatsSagaCommandsAdapter` built over a real `NatsConnection` (never a mocked `RawRequester` — the unit-test seam `NatsSagaCommandsAdapterTests.cs` in `Orders.UnitTests` deliberately uses for its OWN pre-existing coverage, per that class's own remark that "the real NATS request-reply surface ... is proven with a real broker in the integration suite, never mocked"). A new `MalformedStandInResponder` (test-project-only helper) answers every request on the given subject with `"not-json-at-all"u8` — the identical malformed literal `StandInFulfillmentStockCheckResponder.StartMalformedAsync` uses — with a genuine subscribe-probe (`SagaIntegrationTestSupport.WaitUntilReachableAsync`), never a fixed delay.
3. *"armed: the added guard is removed and the test is confirmed to fail naming the unguarded `JsonException`, then restored and reconfirmed green"* — done; verbatim evidence in §4.

## 4. Arming (CLAUDE.md arming protocol)

1. **Backup.** `cp src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs` to the session scratchpad before any edit.
2. **Fix applied**, both projects build clean (`dotnet build src/Orders/Orders.csproj`, `dotnet build tests/Orders.IntegrationTests/Orders.IntegrationTests.csproj` — 0 warnings, 0 errors each), and the new test passes:
   ```
   Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 330 ms
   ```
3. **Violation introduced**: removed the `try`/`catch (JsonException)` wrap (the `using System.Text.Json;` line was left in place — an unused-using, not itself the guard), rebuilt with `dotnet build ... --no-incremental` (0 warnings, 0 errors — an unused `using` is not a build error), and re-ran the ONE named test:
   ```
   [xUnit.net]     ...R110_AMalformedNonJsonReplyOnASagaCommandSubject_ThrowsSagaCommandTransportError_NeverABareJsonException [FAIL]
     Assert.Throws() Failure: Exception type was not an exact match
   Expected: typeof(OrderToCash.Orders.Application.Ports.SagaCommandTransportError)
   Actual:   typeof(System.Text.Json.JsonReaderException)
   ---- System.Text.Json.JsonReaderException : 'not-json-at-all' is an invalid JSON literal. Expected the literal 'null'. LineNumber: 0 | BytePositionInLine: 1.
      ...
      at OrderToCash.Orders.Infrastructure.Messaging.Rpc.RpcJson.IsErrorBody(ReadOnlyMemory`1 data) in .../RpcJson.cs:line 36
      at OrderToCash.Orders.Infrastructure.Messaging.NatsSagaCommandsAdapter.SendAsync[TRequest,TReply](...) in .../NatsSagaCommandsAdapter.cs:line 132
   Failed! - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 428 ms
   ```
   The failure names the actual runtime exception (`JsonReaderException`, a `JsonException` subclass) at the exact unguarded call site (`NatsSagaCommandsAdapter.cs:132`, inside `RpcJson.IsErrorBody`) — not a stand-in assertion.
4. **Restore.** Re-applied the identical edit (same `old_string`/`new_string` used in step 2) to put the guard back. Confirmed via `git diff -- src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs`: the diff against the committed `HEAD` copy is exactly the intended fix (26 insertions / 8 deletions: the new `using`, the `try`/`catch (JsonException)` wrap and its comment) with no leftover artefact from the arm/remove cycle. (Note on process: the scratchpad backup was taken *before* the fix was first applied, i.e. it is the pre-fix baseline, not a post-fix snapshot; the two edits that added/re-added the guard used byte-identical `old_string`/`new_string` pairs, and `git diff` against the tracked, unmodified `HEAD` copy is the independent proof that the restored file is exactly the intended fix — not a `cmp` against a post-fix backup that was never taken. Recorded transparently rather than silently substituted.)
5. **Rebuild and reconfirm green**, forced with `--no-incremental`:
   ```
   Build succeeded. 0 Warning(s) 0 Error(s)
   Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 518 ms
   ```

## 5. Test-project runs (brief step 4)

- `dotnet test tests/Orders.UnitTests/Orders.UnitTests.csproj -c Debug` — **503/503 passed**, 0 failed (includes the pre-existing `NatsSagaCommandsAdapterTests.cs` unit coverage for `SagaCommandTransportError`/`SendAsync`, unaffected by this change — the new `catch` only intercepts `JsonException`, which none of those tests' fake `RawRequester` bodies throw).
- `dotnet test tests/Orders.IntegrationTests/Orders.IntegrationTests.csproj -c Debug` (full suite, real MS-SQL/Kafka/NATS via Testcontainers) — **157/157 passed**, 0 failed, 12 minutes. Includes the new test and the full pre-existing suite (`NatsStockAvailabilityCheckerTests`, all `Saga*Tests`, etc.) with no regression.
- `./init.sh` — green (only expected `WARN`s: 18 uncommitted changes mid-session, and the standard "run `./quality.sh` before closing" reminder).

## 6. Rules honoured

- Touched only `src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs` and a new file inside its own test project (`tests/Orders.IntegrationTests/`) — verified with `git status --porcelain` restricted to those two paths.
- No `specs/shared/`, `feature_list.json` or `CLAUDE.md` edit.
- No git command that writes the index or working tree was run (only `git status`, `git diff`, `git show` were used — read-only).
- No commit was made.

## 7. What could not be done / surprises

- Nothing left undone against the brief's four "Do" steps or the feature's three acceptance bullets.
- Surprise: the arming backup was taken pre-fix rather than post-fix (an ordering slip against the letter of the arming protocol's step 1, which reads as "backup the correct code, then introduce the violation"). Restoration correctness was independently re-established via `git diff` against the tracked `HEAD` copy rather than `cmp` against a post-fix backup that was never taken — recorded above rather than silently worked around. The net evidence (identical `old_string`/`new_string` used twice, `git diff` showing exactly the intended 26/8 line change, green rebuild and re-run) is equivalent in strength but took the `git diff` route instead of `cmp`.
