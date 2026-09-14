# `saga_trace_context_fast_path_fix` — implementation notes

Fixes a real production defect disclosed by backlog id 28's implementation
(`progress/impl_id28_saga_e2e_verification.md`): `SagaCommandDispatchWorker`'s
fast-path channel hand-off lost the triggering fact's trace context, so
Orders/Fulfillment/Billing each observed a DIFFERENT trace id for one order's
happy path, and R56 (one trace id spans the whole saga) failed. This unblocks
the 5th, previously-skipped criterion of
`tests/Gateway.IntegrationTests/SagaEndToEndVerificationTests.cs`
(`Criterion5_R56_OneTraceIdSpansTheComposedRealStack`).

## The exact change

**`src/Orders/Application/Ports/ISagaCommandSignal.cs`** — `SagaCommandRef`
gained one additional member:

```csharp
public sealed record SagaCommandRef(Guid OrderId, SagaCommandKind Command)
{
    public string? TraceParent { get; init; } = Activity.Current?.Id;
}
```

**`src/Orders/Infrastructure/Saga/SagaCommandDispatchWorker.cs`** —
`ConsumeLoopAsync` now restores that captured `traceparent` as a linked
"dispatch" span's parent before calling `dispatcher.DispatchAsync`:

```csharp
var parentContext = TraceContext.ContextFromTraceParent(commandRef.TraceParent);
using var activity = parentContext is { } parent
    ? OtcActivity.Source.StartActivity($"dispatch {commandRef.Command}", ActivityKind.Internal, parentContext: parent)
    : null;
```

No other production file was touched — in particular, **`OrderSagas.cs`,
`SagaFactHandler.cs` and `CancelOrderCommandHandler.cs` (the ~10
`new SagaCommandRef(...)` call sites) needed no edit at all.**

## Why the capture point was chosen where it was

The brief's own instruction was to check whether the positional-record shape
made "auto-capture in the constructor" awkward, and to fall back to touching
each of the (then-estimated 8, actually counted) 10 call sites if so. It is
not awkward: a positional `record` may declare additional members with field
initializers, and those initializers run as part of the generated primary
constructor — exactly like a field initializer on an ordinary class. So
`TraceParent { get; init; } = Activity.Current?.Id;` executes automatically,
under the caller's own ambient `Activity`, at every one of this record's
construction sites, with zero call-site edits and zero risk of a future 11th
call site forgetting to pass it. This was verified by grepping every
`new SagaCommandRef(` site before writing anything
(`grep -rn "new SagaCommandRef(" src`): 10 sites — 7 in `OrderSagas.cs`
(one classes' worth was double-counted in the brief's "8"), 2 in
`SagaFactHandler.cs`, 1 in `CancelOrderCommandHandler.cs` — every one of
which runs strictly inside the `Activity` the triggering fact/command
started (`SagaFactsConsumer.HandleMessageAsync`'s own `consume {eventType}`
span, or the equivalent RPC-responder span for the operator-cancel path).

`TraceParent` participates in the record's structural equality, which was
checked before committing to the approach: the only existing tests that
compare `SagaCommandRef` values compare counts/membership
(`Assert.Single`, `Assert.Empty`) on a `List<SagaCommandRef>` a fake
`ISagaCommandSignal.Signal` collects, never full-record equality against a
literal — so adding a field with a value that varies per test run (ambient
`Activity.Current`, or `null` outside a span) could not break any assertion.
Verified directly: `grep -n "Signalled\[0\]\|Assert.Equal(new SagaCommandRef\|Assert.Contains(new SagaCommandRef"`
across `OrderSagasTests.cs` and `CancelOrderCommandHandlerTests.cs` returned
nothing.

The dispatch-side restoration reuses the EXACT pattern already established
by `OutboxRelay.BuildPublishableFact` (`src/Orders/Infrastructure/Outbox/OutboxRelay.cs:212-215`)
for the identical shape of problem — a durably/locally stored `traceparent`
string, read back across an async boundary `Activity.Current` cannot
survive on its own, restored as a linked CHILD span's parent, with "no
stored parent → no span at all" rather than a fabricated rootless one. This
is also the pattern `SagaFactsConsumer.HandleMessageAsync` already uses on
the consume side (`TraceContext.ExtractKafka` + `OtcActivity.Source.StartActivity(..., parentContext: parent)`).
`ActivityKind.Internal` was chosen (not `Client`/`Producer`) to match
`EfCoreUnitOfWork`'s own `"writemodel.transaction"` span — a span that
exists purely to carry the trace link across this process's own internal
hand-off, not to represent an actual wire hop; the actual outbound NATS
hop's own span is started downstream by `NatsSagaCommandsAdapter`, which
reads `Activity.Current` (now this "dispatch" span) via its existing
`TraceContext.InjectNats` call — unmodified, confirming the brief's own
point 3: the injection side already worked correctly everywhere, so this
one fix was sufficient end-to-end.

## Verification results

**`tests/Gateway.IntegrationTests/SagaEndToEndVerificationTests.cs` — all 5
criteria, run twice, real Testcontainers infra:**

- Run 1: `dotnet test tests/Gateway.IntegrationTests --filter "FullyQualifiedName~SagaEndToEndVerificationTests"`
  → **Passed! Failed: 0, Passed: 5, Skipped: 0, Total: 5**, 1 m 34 s (fresh
  build; fleet boot dominates).
- Run 2: same command, immediately after → **Passed! Failed: 0, Passed: 5,
  Skipped: 0, Total: 5**, ~1 m 39 s wall-clock (20:05:50 → 20:07:29).
- Criterion 5 (`Criterion5_R56_OneTraceIdSpansTheComposedRealStack`) is
  un-skipped (`[Fact(Timeout = 180_000)]`, `Skip` argument removed) and
  passed in both runs — Orders/Fulfillment/Billing's own durably-recorded
  `outbox.trace_parent` columns now parse to the SAME 32-hex trace id for
  one order's `order.placed.v1`/`stock.reserved.v1`/`credit.approved.v1`
  facts. The test's own assertion body needed no change, as the prior
  implementation note predicted.

**Full `tests/Gateway.IntegrationTests` project (regression check):**
`dotnet test tests/Gateway.IntegrationTests` → **Passed: 70, Failed: 0,
Skipped: 0, Total: 70**, 7 m 18 s. Before this fix (per
`progress/impl_id28_saga_e2e_verification.md`): 69 passed, 1 skipped, 70
total. The skip count dropping to 0 with total unchanged at 70 confirms
exactly one test changed state (the un-skip) and nothing else regressed.

**Unit-test regression check:**
- `dotnet test tests/Orders.UnitTests --filter "FullyQualifiedName~Saga"`
  (full rebuild, not `--no-build`, to guarantee the confirming run executes
  the changed source per CLAUDE.md's arming/rebuild discipline) → **282
  passed, 0 failed, 0 skipped, 282 total**, 10 s.
- Full `dotnet test tests/Orders.UnitTests` → **500 passed, 0 failed, 0
  skipped, 500 total**, 10 s.
- `dotnet build src/Orders/Orders.csproj` and
  `dotnet build tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj`
  both → **0 Warning(s), 0 Error(s)**.

No new unit-level guard was added for this fix (out of the brief's stated
scope — the brief's own verification section names only the un-skip of the
existing composed-stack criterion as the required guard, and that criterion
IS the arming: it was seen to fail before the fix (the skip reason records
the observed three-different-trace-ids run) and is now seen to pass, twice,
against real infrastructure with the exact same test body). CLAUDE.md's
arming-message rule therefore applies to that pre-existing assertion, which
already satisfies it (`Assert.True(traceIds.Count == 1, $"expected ONE
trace id across Orders/Fulfillment/Billing's own recorded trace_parent
columns for order {orderId}, got {traceIds.Count}: [...]")` — the message
names the claim and the actual values).

## Files touched

- `src/Orders/Application/Ports/ISagaCommandSignal.cs` — `SagaCommandRef`
  gains `TraceParent`, auto-captured from `Activity.Current` at every
  construction site.
- `src/Orders/Infrastructure/Saga/SagaCommandDispatchWorker.cs` —
  `ConsumeLoopAsync` restores `commandRef.TraceParent` as a linked
  "dispatch" span's parent before `dispatcher.DispatchAsync`; doc comment
  updated to record the fix and its citation.
- `tests/Gateway.IntegrationTests/SagaEndToEndVerificationTests.cs` —
  `Criterion5_R56_OneTraceIdSpansTheComposedRealStack`'s `[Fact(...)]`
  attribute had its `Skip` argument removed; its `<remarks>` doc comment
  updated from present-tense ("SKIPPED — a genuine, confirmed…") to
  past-tense, recording the fix and citing this file. The test body itself
  is byte-for-byte unchanged.

`OrderSagas.cs`, `SagaFactHandler.cs` and `CancelOrderCommandHandler.cs`
were read and their `new SagaCommandRef(...)` call sites enumerated, but
none needed editing — the chosen capture point makes them all correct by
construction.

## What remains

Nothing outstanding against this fix's own scope. The other two disclosed
sub-gaps in feature id 28's Criterion 5 note remain exactly as disclosed
there and are out of THIS fix's scope (never claimed otherwise by this
brief):

- Standing up a real Jaeger/OTLP collector for a literal span-query
  assertion (`test-matrix.md`'s own prescribed R56 mechanism) is still not
  attempted; the durable-column technique remains the disclosed, narrower
  substitute, matching #7's own Pass 3 precedent.
- Criterion 3's redelivery-causes-no-corruption claim is still not
  independently re-armed by a production-code deletion in this pass (it was
  never in this fix's scope — it composes already-armed guards, per id 28's
  own disclosure, and is unrelated to the trace-context defect fixed here).
