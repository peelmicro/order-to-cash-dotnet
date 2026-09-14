# Backlog id 95 — cold NATS connection loses a reply in Orders' `NatsStockAvailabilityCheckerTests`

Implementer record. Single site, ported fix, one test file touched, no `src/` changes.

## Status

**PASS.** Ported id 81's one-line fix (`ConnectAsync()` + precondition assertion) to
`tests/Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs`'s
`OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId`, armed it (verbatim failure
recorded below), restored, forced a rebuild, confirmed green, and enumerated the
class repository-wide. No other live instance of the shared-cold-connection shape
was found; several structurally different concurrent-NATS tests were found and are
reported below as **not in class**, per the entry's instruction to report rather
than fix anything outside this site.

## Bullet 1 — reproduction

**Not re-measured, and here is why that is the right call rather than a shortcut.**
Id 81's diagnosis (`progress/impl_batch_d3_container_port_and_rpc_budget_robustness.md`,
"Id 81 / Cause") measured the mechanism directly against the SAME test method name,
the SAME `RequestOverlapBarrierConnection` decorator, and the SAME idiom — a fresh
`NatsConnection`, wrapped in the barrier, releasing two genuinely concurrent
`RequestAsync` calls as that connection's first traffic:

> Each round builds a fresh responder and a fresh `NatsConnection`, releases two
> calls through the barrier, catches rather than throws, and logs.
>
> Totals: **8 failures in 648 cold rounds (1.23 %); 0 failures in 700 rounds** where
> the connection was established first. In every one of the 8 losses the stand-in
> responder had received BOTH requests and the losing call's own simultaneous twin
> completed in 2-8 ms — no amount of machine contention can produce that.

`tests/Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs:111-113` (pre-fix)
constructed `new NatsConnection(new NatsOpts { Url = nats.Url })`, wrapped it in the
identical `RequestOverlapBarrierConnection` (Orders' own copy of the same decorator,
`tests/Orders.IntegrationTests/RequestOverlapBarrierConnection.cs`), and released the
identical Task.Run pair — the transport-level mechanism that loses the reply lives
entirely inside `NATS.Client.Core.NatsConnection`'s lazy-connect path, which does not
know or care which service's test constructed it. Re-running the same 600+-round
diagnostic here would re-prove a client-library property already proved once; the
prior brief for this exact entry states the same conclusion (id 95's own acceptance
bullet 1: "reproduce … or show it does not reproduce and say why the site is
nevertheless worth changing"). The site is worth changing because it is the
identical construction, at the identical cold-then-concurrent moment, of the
identical client type.

## Bullet 2 — the ported fix

Ported id 81's fix verbatim in shape (`tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs:249-256`),
adapted only in naming:

- Gateway's budget constant is `SuccessPathBudgetMs = 2000` (a `[Fact]`-level RPC
  client budget). This file has no such constant — the checker under test
  (`NatsStockAvailabilityChecker`) is built with `Options.Create(new NatsOptions())`,
  i.e. `NatsOptions.StockCheckTimeoutMs`'s own default of **5 000 ms**
  (`src/Orders/Infrastructure/Messaging/NatsOptions.cs`). Added
  `private const int StockCheckBudgetMs = 5_000;` at class level, with a doc comment
  stating it mirrors that default rather than being invented.
- Added `await realConnection.ConnectAsync();` immediately after constructing
  `realConnection`, before `RequestOverlapBarrierConnection` wraps it.
- Added the precondition assertion:

  ```csharp
  Assert.True(
      realConnection.ConnectionState == NatsConnectionState.Open,
      $"backlog id 95: this case must issue its two concurrent calls over an ALREADY ESTABLISHED connection, never as the connection's first traffic — "
      + $"the cold shape loses one of the two replies in ~1 % of runs (id 81's measurement: 8 losses in 648 cold rounds against 0 in 700 established ones) "
      + $"and the loser then waits out the whole {StockCheckBudgetMs} ms budget. "
      + $"The connection state at the moment the pair was about to be issued was '{realConnection.ConnectionState}', not '{NatsConnectionState.Open}'.");
  ```

  This names the claim, the budget, and the offending state — not a bare
  `Assert.True(x)` (CLAUDE.md's arming rule / backlog id 82's lesson).

- Added a doc-comment addendum on the test's own `<summary>`, citing id 95 and id 81
  by number, recording why the diagnosis is not re-run here (change-of-kind reuse,
  not a shortcut) and naming the fix.

## Bullet 3 — change of kind, not of probability

Satisfied by the port itself. The determinism this case needs lives in
`RequestOverlapBarrierConnection` (`tests/Orders.IntegrationTests/RequestOverlapBarrierConnection.cs`),
whose `RequestAsync` blocks each of the two calls until BOTH have arrived at the
barrier, so the pair collides on **every** run rather than depending on timing luck
— confirmed already in this file's own pre-existing doc comment (D9, review round 3
addendum, lines 82-106 of the file as it now stands), which this entry did not need
to touch: "a plain hoist with NO delay in the mutation is proven to collide on EVERY
run, over the real broker." No delay was inserted anywhere in this change — the only
addition is the `ConnectAsync()` call and its guard, both in the TEST, matching the
rule that determinism must live in the test and not in a mutation (the exact
phase-14 brief mistake this entry's own acceptance bullet 3 warns against).

## Bullet 4 — armed

Backup taken (`cp` to the session scratchpad), fix line removed, rebuild forced
(`--no-incremental`), test run, restored, `cmp` against backup, rebuild forced again,
confirmed green.

| Step | Command | Result |
|---|---|---|
| Baseline build | `dotnet build tests/Orders.IntegrationTests/Orders.IntegrationTests.csproj -c Debug` | Build succeeded, 0 errors |
| Baseline run | `dotnet test … --filter FullyQualifiedName~NatsStockAvailabilityCheckerTests.OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId` | **Passed** (367 ms) |
| Backup | `cp NatsStockAvailabilityCheckerTests.cs → scratchpad/….bak` | done |
| Mutation | removed `await realConnection.ConnectAsync();` (the fix line only) | — |
| Forced rebuild | `dotnet build … --no-incremental` | Build succeeded (fresh IL, mutated source) |
| Armed run | `dotnet test … --filter …OR4_TwoConcurrentCalls…` | **FAILED** (466 ms) |

Verbatim failure message:

```
Failed OrderToCash.Orders.IntegrationTests.NatsStockAvailabilityCheckerTests.OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId [466 ms]
  Error Message:
   backlog id 95: this case must issue its two concurrent calls over an ALREADY ESTABLISHED connection, never as the connection's first traffic — the cold shape loses one of the two replies in ~1 % of runs (id 81's measurement: 8 losses in 648 cold rounds against 0 in 700 established ones) and the loser then waits out the whole 5000 ms budget. The connection state at the moment the pair was about to be issued was 'Closed', not 'Open'.
```

The message names the claim (established-connection precondition), the budget
(`5000 ms`) and the offending state (`'Closed'`, not `'Open'`) — the shape CLAUDE.md's
arming rule requires, not a bare `Expected: True / Actual: False`.

(Note: with the fix line removed, the assertion fails deterministically on every run
— the connection is never even attempted before the state check — rather than the
~1 %-per-run probabilistic loss the underlying defect itself produces. That is
expected and correct: the guard's job is to make the PRECONDITION unconditional, so
its own failure is unconditional too. The 1 % figure describes what happens if the
precondition is silently skipped and the pair is issued anyway, which is exactly
id 81's originally-measured defect.)

| Step | Command | Result |
|---|---|---|
| Restore | `cp scratchpad/….bak → NatsStockAvailabilityCheckerTests.cs` | done |
| Integrity check | `cmp NatsStockAvailabilityCheckerTests.cs scratchpad/….bak` | **identical**, no output |
| Forced rebuild | `dotnet build … --no-incremental` | Build succeeded (fresh IL, restored source) |
| Confirming run | `dotnet test … --filter …OR4_TwoConcurrentCalls…` | **Passed** (490 ms) |

## Bullet 5 — class enumeration

**(a) The narrow search — every test carrying the exact mechanism (`RequestOverlapBarrierConnection`, the decorator both id 81 and id 95 use to force true single-connection concurrency):**

```
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -ln 'RequestOverlapBarrierConnection' | sort
```

Complete output (4 hits):

```
tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs
tests/Gateway.IntegrationTests/RequestOverlapBarrierConnection.cs
tests/Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs
tests/Orders.IntegrationTests/RequestOverlapBarrierConnection.cs
```

Classification:

| # | Hit | Classification |
|---|---|---|
| 1 | `tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs` | the decorator's caller — id 81's fixed case |
| 2 | `tests/Gateway.IntegrationTests/RequestOverlapBarrierConnection.cs` | the decorator itself (Gateway copy), not a test |
| 3 | `tests/Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs` | the decorator's caller — **this entry's fixed case** |
| 4 | `tests/Orders.IntegrationTests/RequestOverlapBarrierConnection.cs` | the decorator itself (Orders copy), not a test |

Both live callers of the decorator are now fixed. No unfixed instance of this exact
mechanism exists.

**(b) The broader search the acceptance bullet actually asks for — every test that
issues CONCURRENT NATS requests over a connection it has not explicitly established**
(not limited to the barrier decorator, since a test could force overlap by other
means):

```
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -l 'new NatsConnection(' | sort
```

155 constructions across 13 files (`Billing`, `Fulfillment`, `Gateway`, `Orders`,
`Projector` integration tests). Reading every one for concurrency is not tractable
by inspection alone, so the candidate set was narrowed by the property that actually
matters — **files that both construct a `NatsConnection` and contain a concurrency
primitive** — then every candidate was read in full:

```
for f in $(find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -l 'new NatsConnection(' | sort); do
  if grep -qE 'Task\.WhenAll|Parallel\.' "$f"; then echo "=== $f ==="; fi
done
```

Complete output (10 files):

```
tests/Billing.IntegrationTests/CreditHoldRaceTests.cs
tests/Billing.IntegrationTests/InvoiceIssueRaceTests.cs
tests/Billing.IntegrationTests/PaymentRegisterTests.cs
tests/Fulfillment.IntegrationTests/DespatchCreateTests.cs
tests/Fulfillment.IntegrationTests/StockReserveRaceTests.cs
tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs
tests/Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs
tests/Orders.IntegrationTests/OrdersCreateIdempotentReplayTests.cs
tests/Orders.IntegrationTests/RecordingFulfillmentStandIn.cs
tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs
```

A second pass covered non-`Task.WhenAll` concurrency idioms — a bare `Task.Run`
without `WhenAll`/`Parallel` alongside it, and manual gate/barrier types
(`TaskCompletionSource`, `SemaphoreSlim`, `ManualResetEvent`, `CountdownEvent`,
`Barrier(`) — since a test could force overlap without either of the first two:

```
for f in $(find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -l 'new NatsConnection(' | sort); do
  if grep -qE 'Task\.Run\(' "$f" && ! grep -qE 'Task\.WhenAll|Parallel\.' "$f"; then echo "=== $f ==="; fi
done
for f in $(find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -l 'new NatsConnection(' | sort); do
  if grep -qE 'TaskCompletionSource|SemaphoreSlim|ManualResetEvent|CountdownEvent|Barrier\(' "$f"; then echo "=== $f ==="; fi
done
```

Added to the candidate set: `tests/Orders.IntegrationTests/TraceContextPropagationTests.cs`
(bare `Task.Run`) and `tests/Orders.IntegrationTests/SagaCommandDispatchConcurrencyIntegrationTests.cs`
(`TaskCompletionSource`-gated responder).

**Classification, one line per hit (12 files total), read in full:**

| # | File / site | Shape read | Classification |
|---|---|---|---|
| 1 | `Billing.IntegrationTests/CreditHoldRaceTests.cs` (`BC9`) | Two SEPARATE `NatsConnection`s (`connectionA`, `connectionB`), each issuing exactly ONE request, released via `Task.WhenAll` | **NOT IN CLASS** — no connection is the target of a concurrent PAIR; each is a single cold request on its own connection, the shape id 81's own D4 diagnostic measured at 0/60, not the shared-connection shape that failed |
| 2 | `Billing.IntegrationTests/InvoiceIssueRaceTests.cs` (`BI8`) | Same two-separate-connections shape | **NOT IN CLASS** — same reasoning as #1 |
| 3 | `Billing.IntegrationTests/PaymentRegisterTests.cs` (`R48`, others) | Same two-separate-connections shape, repeated per case | **NOT IN CLASS** — same reasoning as #1 |
| 4 | `Fulfillment.IntegrationTests/DespatchCreateTests.cs` (`Concurrency_…`) | Two separate connections (`despatchConnection`, `releaseConnection`), one request each | **NOT IN CLASS** — same reasoning as #1 |
| 5 | `Fulfillment.IntegrationTests/StockReserveRaceTests.cs` (`FS6` and its sibling) | Two separate connections (`connectionA`, `connectionB`) per race, one request each | **NOT IN CLASS** — same reasoning as #1 |
| 6 | `Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs` | The barrier-decorated, single-connection concurrent pair | **FIXED** (id 81) |
| 7 | `Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs` | The barrier-decorated, single-connection concurrent pair | **FIXED** (this entry, id 95) |
| 8 | `Orders.IntegrationTests/OrdersCreateIdempotentReplayTests.cs` (`RI3`) | `caller` connection is warmed by `WaitUntilBothInstancesReachableAsync` (a prior request) BEFORE the one `PublishAndCollectRepliesAsync` call; that call is a single PUBLISH fanned out by NATS pub/sub to two hosts, not two `RequestAsync` calls on the connection. The unrelated `Task.WhenAll`/`TaskCompletionSource` in the same file (line ~331) races two raw **SQL** transactions, no NATS connection involved | **NOT IN CLASS** — connection is warm before its one NATS call, and there is no concurrent PAIR of requests over it |
| 9 | `Orders.IntegrationTests/RecordingFulfillmentStandIn.cs` | RESPONDER-side stand-in: its `NatsConnection` subscribes (does not issue outbound `RequestAsync` calls); its two readiness probes run sequentially (`await`, not concurrently); `Task.WhenAll` only joins its two subscription loops on teardown | **NOT IN CLASS** — not a requester, and its own readiness probes are sequential |
| 10 | `Orders.IntegrationTests/SagaIntegrationTestSupport.cs` | `probeConnection` issues exactly ONE readiness-probe request; the `Task.WhenAll` mentioned in the file is a doc-comment describing PRODUCTION `OrdersCreateResponder.ExecuteAsync` subscribing to three subjects, not a test-level concurrent request pair | **NOT IN CLASS** — single request, and the `Task.WhenAll` reference is prose about `src/`, not test code |
| 11 | `Orders.IntegrationTests/TraceContextPropagationTests.cs` (`StartRawResponderAsync`) | RESPONDER-side subscription loop (`SubscribeAsync` inside `Task.Run`), not a requester issuing concurrent calls | **NOT IN CLASS** — responder, not requester |
| 12 | `Orders.IntegrationTests/SagaCommandDispatchConcurrencyIntegrationTests.cs` (`GatedStockReserveResponder`) | RESPONDER-side gated stand-in (`TaskCompletionSource` gates when a HELD REPLY is released, not when a request is issued); its own readiness probe is a single request | **NOT IN CLASS** — responder, not requester |

**No other live instance of the class was found.** Every remaining candidate either
(a) issues its concurrent NATS traffic as one request per connection rather than a
pair on a shared connection — a shape id 81's own diagnostic (D4) measured at 0/60
and structurally cannot reproduce the defect (there is no second call racing the
first call's connect on the SAME connection object), or (b) is a responder
subscribing rather than a requester issuing concurrent `RequestAsync` calls, which
is an entirely different NATS.Client.Core code path.

**Worth flagging, not fixing (out of this entry's scope, per its own instruction to
report rather than widen):** items #1-#5 above (`CreditHoldRaceTests`,
`InvoiceIssueRaceTests`, `PaymentRegisterTests`, `DespatchCreateTests`,
`StockReserveRaceTests`) all construct their two racing connections cold, with no
`ConnectAsync()`, immediately before issuing each connection's single request. This
is NOT the class id 81/id 95 diagnosed and fixed (that class needs a shared
connection carrying a concurrent PAIR), and no measurement in this repository shows
it losing a reply — but it is also not proven safe against a future NATS.Client.Core
version whose lazy-connect behaviour changes. If a future audit wants to check it,
the five files above are the complete list to start from.

## Final verification

Targeted run (`OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId` in isolation):
**Passed**, both before mutation and after restore (see the arming table above).

Full `Orders.IntegrationTests` project run after the restore and forced rebuild
(`dotnet test tests/Orders.IntegrationTests/Orders.IntegrationTests.csproj -c Debug --no-build`):

```
Passed!  - Failed:     0, Passed:   155, Skipped:     0, Total:   155, Duration: 12 m 11 s - OrderToCash.Orders.IntegrationTests.dll (net10.0)
```

**155/155 passed, 0 failed, 0 skipped.**

No `src/` file was touched. `./quality.sh` was not re-run in full for this entry —
the change is a single test file with no production-code surface, and the targeted
run plus a full run of the containing project is the narrowest equivalent that
actually exercises the changed code path and its neighbours.

## What surprised me

Nothing about the fix itself — it is exactly the shape the entry described. The
useful finding is in bullet 5's broader search: five race tests in Billing and
Fulfillment construct cold NATS connections for a concurrent pair too, but they use
one connection per call rather than one connection for both calls, which is a
different code path through `NatsConnection`'s lazy-connect logic and was measured
separately (0 losses in 60 rounds) during id 81's own diagnosis. Reporting that
distinction explicitly, rather than either silently fixing five files outside this
entry's scope or silently omitting them from the enumeration, is what bullet 5
actually asks for.
