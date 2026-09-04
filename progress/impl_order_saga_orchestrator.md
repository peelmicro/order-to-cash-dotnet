# Implementation report — `order_saga_orchestrator` (feature 16, phase 8)

**Status at close:** `in_review` (set by this pass — the reviewer closes it).

## 1. What was built

The saga orchestrator inside `src/Orders`, exactly as scoped by `specs/order_saga_orchestrator/{requirements,design,tasks}.md`:

- **Group A** — `SagaFactTopics.cs` (the three consumed topics, spec-text-guarded), `RpcSubjects.cs` extended with the five saga subjects, `SagaCommandPayloads.cs` (the ten request/reply records transcribed from `asyncapi.yaml`, reusing `Contracts.Facts.ReservationRef`/`Shortage`/`InvoiceLine`/`DespatchLine` for reply line shapes and a new `SagaMoney` for `CreditHoldRequestPayload.Amount`'s nested `{amount, currency}`).
- **Group B** — `SagaCommandKind.cs`, `SagaFact.cs`, `SagaStep.cs` (`Skip`/`Advance`/`Cancel`), `SagaStepTable.cs` — the fourteen-row declarative table, pure, zero framework references.
- **Group C** — the five Application ports: `IFactStreamSubscriber`, `ISagaCommands` (+ `SagaCommandTimeoutError`/`SagaCommandTransportError`), `ISagaCommandStore`, `ISagaIgnoredFactRecorder` + `ISagaCommandSignal`. Also `IIdempotentSagaRunner` — a thin seam over the canonical `IdempotentConsumer` (see §3 deviation below).
- **Group D** — `NatsSagaCommandsAdapter.cs`, with a public `RawRequester` delegate test seam (`KafkaFactPublisher`'s own precedent) so the pre-42 error taxonomy is unit-testable without a 30-member `INatsConnection` fake.
- **Group E** — `EfCoreSagaIgnoredFactRecorder.cs`, `EfCoreSagaCommandStore.cs` (enqueue/claim/claim-due/mark-sent/park, exactly §6.3's SQL shapes).
- **Group F** — `KafkaFactStreamSubscriber.cs` (the one type touching `Confluent.Kafka`'s consumer API) and `SagaFactsConsumer.cs` (the one Kafka `BackgroundService`), plus `KafkaContainerFixture` extended to create all three fact topics.
- **Group G** — `SagaFactHandler.cs` + `SagaFactResult.cs` (the one transactional unit), `SagaFactCommands.cs`/`SagaFactCommandHandlers.cs` (ten commands, ten handlers), `SagaDispatchEvents.cs` + `OrderSagas.cs` (five dispatch-owed events, five `IEventHandler<T>` classes).
- **Group H** — `ChannelSagaCommandSignal.cs`, `SagaCommandDispatcher.cs` (with `ISagaCommandDispatcher.DispatchAsync`/`DispatchClaimedAsync` — see §3 deviation), `SagaCommandDispatchWorker.cs`, `SagaCommandSweeper.cs` + `SagaCommandSweeperBackgroundService.cs`, `ISagaRetryDelay`/`TaskDelaySagaRetryDelay`.
- **Group I** — `OrdersSagaOptions.cs`, `OrdersSagaServiceCollectionExtensions.cs`, `OrdersHost.cs`/`Program.cs` extended with a third `configureSaga` delegate.
- **Group J** — `FactPublisherConfinementTests.cs` amended (producer-only), `FactConsumerConfinementTests.cs` new (consumer-only) — **both corrected mid-arming**, see §4 finding.
- **Group L** — `StandInSagaResponders.cs` (five generic stand-in RPC responders + a fact-publishing helper), `SagaCollection.cs`, and six integration test files (`SagaHappyPathTests`, `SagaCompensationStockRejectedTests`, `SagaCompensationCreditRejectedTests`, `SagaPreconditionTests`, `SagaCommandRetryTests`, `SagaCommandStoreTests`, `SagaConsumptionTests`) plus `OrderNumberAllocatorTests` (the A11 debt).

**Package section: none.** No new `PackageReference` anywhere — `Confluent.Kafka`, `NATS.Net`, EF Core, `Microsoft.Extensions.*` were all already pinned and referenced.

## 2. Traceability

Every shared `R19`–`R29` and local `SO1`–`SO11` row is proven — see the flipped Status column in `specs/shared/test-matrix.md` §3 and `specs/order_saga_orchestrator/requirements.md` §3 for the exact, character-exact test citations. Summary:

- **R19–R23, R25–R28**: `DONE`, integration-proven (mostly by one continuous happy-path/compensation scenario, since the underlying causal chain is inherently one sequence — matches design's own L2/L4 shape).
- **R24**: integration half `DONE`; API half stays `TODO` (Gateway feature) — a ratified scoped row.
- **R29**: retry-clause row `DONE`; dead-letter row stays `TODO` (feature 27) — the split inherited from #7 via `requirements.md` §1.1.
- **SO1–SO11**: all `DONE`. Two rows (SO3 durable guarantee, SO5, SO6, SO7) are proven by a test whose actual name differs from the spec's own sketch, because the underlying scenarios naturally combine (recorded and renamed in `requirements.md` §3, per rule 4).

## 3. Deviations from the design, argued

1. **`IFactStreamSubscriber` registered singleton, not scoped (design.md §9's own words).** `SagaFactsConsumer` is itself a singleton `BackgroundService` (every `AddHostedService` registration is), and `ValidateOnBuild` refused a singleton directly consuming a scoped service — caught live, verbatim: *"Cannot consume scoped service 'OrderToCash.Orders.Application.Ports.IFactStreamSubscriber' from singleton 'Microsoft.Extensions.Hosting.IHostedService'."* `KafkaFactStreamSubscriber` holds no scoped state of its own — its `IConsumer` client is built fresh inside `ConsumeAsync` via `using` and disposed with it, independent of the DI registration's own lifetime — so singleton loses nothing of what design.md's parenthetical asked for. Recorded in the registration's own doc-comment.
2. **A new port, `IIdempotentSagaRunner`, not named in design.md's port list.** `SagaFactHandler` was designed to compose the concrete `IdempotentConsumer` directly, but `IdempotentConsumer` requires a real `DbContext` to run its dedup insert — a hard dependency `SagaFactHandlerTests` (G2, "with fakes") cannot satisfy without a real database. `IIdempotentSagaRunner` is a thin wrapper fixing `ConsumerName.OrdersSaga` and translating `ConsumptionOutcome` to an Application-owned enum; its one implementation, `IdempotentConsumerSagaRunner`, composes the canonical class verbatim and is not itself covered by the "do not edit" rule (only `IdempotentConsumer`/`ProcessedEventLedger` are). This is what made G2's fakes-only unit test possible at all.
3. **`ISagaCommandDispatcher` split into `DispatchAsync` (claim-then-issue) and `DispatchClaimedAsync` (issue an already-claimed row).** Design's single `SagaCommandDispatcher.DispatchAsync(orderId, command, ct)` would, if called by the sweeper on a row `ClaimDueAsync` already claimed, re-claim via `TryClaimAsync` and find the row's own just-set lease still active — a silent no-op, discovered live while wiring H6 (a genuine "the guarantee quietly depends on claiming twice" bug, not the H8.3 substitution). `DispatchClaimedAsync` is the sweeper's own path, called directly with the already-claimed record; `DispatchAsync` remains the worker's fast-path entry, claiming for itself. Both still call the dispatcher directly — H8.3's arming (below) confirms the sweeper never depends on the channel or `IDispatcher`.
4. **`ISagaCommandStore.EnqueueAsync`/etc. take `string payload` rather than a typed request** — `SagaCommandRequestFactory.BuildJson(SagaCommandKind, Order)` (Application/Sagas/) builds and serialises the typed request from the loaded aggregate, matching design.md §6.3's "payload — the full typed request, serialised through RpcJson at enqueue time from the loaded aggregate" literally, but as a standalone static class rather than inline in `SagaFactHandler`.
5. **`AllocateNextAsync_ConcurrentFirstEverAllocations_CanRaceTheSelfSeedInsertAndFail` (`OrderNumberAllocatorTests.cs`, the A11 debt) went through two redesigns after being observed flaky/hazardous, both recorded here rather than silently fixed.** The scenario itself is a genuine finding, not a bug in this feature: `EfCoreOrderNumberAllocator`'s self-seeding branch (`IF NOT EXISTS ... INSERT`) takes no lock, so concurrent *first-ever* allocations against a never-seeded sequence table can race the insert and fail with a primary-key violation — reported for feature 15 (`order_number_allocator`), not fixed here, per tasks.md I6. Reproducing it deterministically needed genuine simultaneity, which went through three shapes: (a) a bare `Task.WhenAll` with no coordination — flaky, because .NET connection-open latency alone was sometimes enough to serialise every caller behind the first, producing a false-negative green run; (b) a `System.Threading.Barrier(concurrency)` forcing every task to reach `SignalAndWait()` at the same instant — this **deadlocked** during verification of this very fix (a slow or stuck connection-open leaves the other N-1 tasks waiting on the barrier forever, with no timeout; reproduced live, the stuck process trees were killed with `kill -9` and confirmed dead via `ps`/`docker ps` before continuing); (c) the shape landed — every connection opened first via a `Task.WhenAll` bounded by a 30s `CancellationTokenSource` (so a stuck connection fails the test loudly instead of hanging the process), and only then is a *second*, ungated `Task.WhenAll` used to fire all sixteen allocations, relying on .NET starting each async lambda synchronously up to its first incomplete await when `Task.WhenAll` enumerates them. Verified reliable over 5 sequential, non-concurrent runs (§6/§7) with no hang and the race reproducing every time; the design note against re-trusting a future green run (if the assertion ever fails because every caller succeeded) is preserved in the test's own comment.

No other deviation. Every port name, file name and class name design.md fixes (`NatsSagaCommandsAdapter`, `OrderSagas.cs`, the fourteen-row table, the five dispatch events) is exactly as specified.

## 4. A design claim found false during arming, and corrected

design.md §10 states *"NetArchTest matches dependency names by prefix, so these four cover the producer surface."* Arming J3 (add a `ProducerBuilder<string, byte[]>` reference under `Application/`) left `FactPublisherConfinementTests` **green** with the un-suffixed type names the design specifies — the guard did not fire. Direct probe against `NetArchTest.Rules` 1.3.2 confirmed `HaveDependencyOn` does an **exact** match against the CLR metadata name, and a closed generic instantiation's name carries the open definition's arity suffix (`Confluent.Kafka.ProducerBuilder` + closed args resolves to `"Confluent.Kafka.ProducerBuilder\`2"`, not a value `"Confluent.Kafka.ProducerBuilder"` matches). `Confluent.Kafka.Producer<TKey,TValue>`/`Consumer<TKey,TValue>` (the concrete classes) also turned out to be `internal` in 2.15.0, unreachable from any consumer of the package — the interface/builder/config three are the only ones any code outside `Confluent.Kafka` could ever reference.

Both confinement test files were corrected to use the arity-suffixed names (`` `2 ``) for the three generic entries each; `ProducerConfig`/`ConsumerConfig` (non-generic) needed no change. Re-armed against the corrected rules — both now fire correctly (§5, J3 rows). This is a repair of the rule the gate approved, not a widening or narrowing of what it protects — the intent (confine the four producer/consumer surfaces to one namespace each) is unchanged; only the string literals needed to actually reach that intent through this library's matching semantics.

## 5. Arming table — all 28 rows, verbatim

Protocol followed exactly: backup by copy, mutate, force rebuild, run the named test, record the verbatim failure, restore from the backup, force rebuild again, confirm green. `scripts/arm-probe.sh`'s shape was followed manually (backup/mutate/build/test/restore/build) for every row below.

### Group B — `SagaStepTableTests.cs` (unit)

**B4 row 1** (table-integrity: give `stock.rejected.v1` a `CommandAfter`) — `SagaStep.Cancel` structurally has no `CommandAfter` field to mutate (a stronger guarantee than the design's own snippet assumed), so this was armed at its functional equivalent: `SagaFactHandler.ApplyStep`'s shared `case SagaStep.Cancel:` line, `return null;` → `return SagaCommandKind.StockRelease;`. Named test: `SagaCompensationStockRejectedTests.R26_CancelsWithReasonStockRejectedAndIssuesNoStockReleaseCommand` (integration).
```
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
```
(the `saga_commands` count for `stock.release` on the cancelled order). Same mutation coincides with **K6**.

**B4 row 2** (`invoice.issued.v1` given a `CommandAfter`) — `SagaStepTable.cs`, `CommandAfter: null` → `CommandAfter: SagaCommandKind.InvoiceIssue` on the `invoice.issued.v1` row. Named tests: `SagaStepTableTests.R23_InvoiceIssuedV1_AdvancesToInvoicedAndOwesNothing` and the general theory test.
```
Assert.Null() Failure: Value of type 'Nullable<SagaCommandKind>' has a value
Expected: null
Actual:   InvoiceIssue
```
Coincides with **K7**.

**B4 row 3** (`credit.approved.v1`'s `Apply` reduced to `Confirm` alone — actually armed as "delete `Confirm`, keep `ApproveCredit`", the row's actual two calls) — Named test: `SagaStepTableTests.R21_CreditApprovedV1_PerformsBothEdgesInOneLoadSaveAndRaisesExactlyOneOrderConfirmed`.
```
Assert.Equal() Failure: Values differ
Expected: Confirmed
Actual:   CreditApproved
```
Confirms the case asserts the intermediate edge, not merely the event count (per B4's own strengthening instruction). Coincides with **K1**.

**B4 row 4** (swap `MapReason`'s two branches) — `SagaStepTableTests.R28_SO7_StockReleasedV1_CancelsWithExactlyOneStockReleasedCompensationStepBuiltFromTheObservedFact`, both `[InlineData]` cases:
```
Assert.Equal() Failure: Values differ
Expected: OperatorCancelled
Actual:   CreditRejected
```
and the mirror case (`Expected: CreditRejected / Actual: OperatorCancelled`).

### Group D — `NatsSagaCommandsAdapterTests.cs` (unit)

**D3** (collapse the two exception types — armed as: the `NatsNoReplyException` catch throws `SagaCommandTransportError` instead of `SagaCommandTimeoutError`) — `NatsNoReplyException_MapsToSagaCommandTimeoutError`:
```
Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(OrderToCash.Orders.Application.Ports.SagaCommandTimeoutError)
Actual:   typeof(OrderToCash.Orders.Application.Ports.SagaCommandTransportError)
```

### Group E — `SagaCommandStoreTests.cs` (integration, real MS-SQL)

**E4 row 1** (delete `READPAST` from `ClaimDueAsync`) — armed via a temporary probe method (`ArmProbe_E4_1_ClaimDueAsync_WithAConcurrentlyLockedRow`, added, arm-recorded, then removed): with `READPAST` intact the probe returns in ~2 s (0 claimed, the locked row correctly skipped); with `READPAST` removed and the same row locked by a separate connection's `UPDLOCK` transaction, `timeout 30 dotnet test --filter ArmProbe_E4_1...` had to be forcibly terminated — the process never returned. Bash tool reported `Terminated`, exit 143 (this sandbox's `timeout` reports SIGTERM-kill exit differently from feature 14's own recorded 124; both denote the identical observation, "the bounded run did not complete and had to be killed" — MS-SQL's default `lock_timeout = -1`).

**E4 row 2** (delete the lease condition from `TryClaimAsync`'s `WHERE`) — `SO11_AClaimedRowIsInvisibleToAConcurrentClaimUntilItsLeaseElapses`:
```
Assert.Null() Failure: Value is not null
Expected: null
Actual:   SagaCommandRecord { Id = 7dcc3d5d-e191-4d88-9937-39f09e4bad36, OrderId = 0c1bb24b-7636-4a2b-88ab-b14e0c665422, OrderReference = ORD-000001, Command = StockReserve, Payload = {}, TriggeringEventId = c1dc8f16-c434-4f4a-9219-bbaf50b5c2df, Attempts = 0 }
```

**E4 row 3** (delete the duplicate-key catch) — `EnqueueAsync_ADuplicateEnqueue_ReturnsAlreadyEnqueuedAndLeavesTheExistingRowUntouched`:
```
Microsoft.EntityFrameworkCore.DbUpdateException : An error occurred while saving the entity changes. See the inner exception for details.
---- Microsoft.Data.SqlClient.SqlException : Cannot insert duplicate key row in object 'dbo.saga_commands' with unique index 'IX_saga_commands_order_id_command'. The duplicate key value is (946bc503-c3a5-471d-9b26-304a2d1fd823, credit.hold).
```
Confirmed failing **as** a duplicate-key error, per the row's own requirement.

### Group F — `SagaConsumptionTests.cs` (integration, real Kafka)

**F6 row 1** (`EnableAutoOffsetStore = true`) — `SO9_AHandlerThatThrows_LeavesTheCommittedOffsetUnchangedAndTheFactIsRedelivered`:
```
Assert.True() Failure
Expected: True
Actual:   False
```
(`gate.Attempts >= 2` — no redelivery ever happened; the offset was auto-committed regardless of the handler's own outcome).

**F6 row 2** (`StoreOffset` moved before the handler `await`) — same test, same assertion, same failure shape:
```
Assert.True() Failure
Expected: True
Actual:   False
```

### Group G — unit

**G7 row 1** (`SagaFactCommandHandlers.cs`, `HandleOrderPlacedFactCommandHandler`'s condition replaced with `if (true)`) — all three suppression cases in `SagaFactCommandHandlerTests.cs` failed:
```
Assert.Empty() Failure: Collection was not empty
Collection: [OrderPlacedFactRecorded { OrderId = ..., CorrelationId = ... }]
```
(`Ignored_PublishesNothing`, `Duplicate_PublishesNothing`, `ProcessedWithoutEnqueue_PublishesNothing` — all three).

**G7 row 2** (swap `OrderPlacedFactRecordedHandler`/`OrderMarkedStockReservedHandler`'s signalled commands in `OrderSagas.cs`) — `OrderSagasTests.SO3_EachDispatchOwedEvent_SignalsItsOwnSagaCommandAndNothingElse`:
```
Assert.Equal() Failure: Values differ
Expected: StockReserve
Actual:   CreditHold
```

### Group H — unit, and one integration finding

**H8 row 1** (dispatcher retries a business rejection — `return;` deleted after `MarkSentAsync`) — `SagaCommandDispatcherTests.ABusinessRejectionIsMarkedSentAndNeverRetried`:
```
Assert.Equal() Failure: Values differ
Expected: 1
Actual:   3
```
(the SO4 retry-schedule case also failed as a side effect, `Assert.Empty` on the recorded delays failing with a non-empty collection — the whole retry loop now ran unconditionally).

**H8 row 2** (`ParkAsync` called with a hardcoded `1` instead of `policy.MaxAttempts`) — `SagaCommandDispatcherTests.ExhaustionParksWithTheAccumulatedAttemptsAndTheLastError`:
```
Assert.Equal() Failure: Values differ
Expected: 3
Actual:   1
```

**H8 row 3** (the sweeper signals through `ISagaCommandSignal` instead of calling the dispatcher directly) — `SagaCommandSweeperLoopTests` (which fakes `ISagaCommandSweeper` itself) does **not** notice, as expected; the row that **does** notice is the real integration guarantee test: `SagaCommandRetryTests.SO3_APendingRowCommittedWithNoInProcessSignal_IsStillIssuedBySweeperCycleAndResumesTheSagaWhenAResponderAppears`:
```
Assert.True() Failure
Expected: True
Actual:   False
```
Root cause of the failure, traced: the channel-signalled command gets picked up by the live `SagaCommandDispatchWorker`, which calls `ISagaCommandDispatcher.DispatchAsync` — that re-claims via `TryClaimAsync`, finds the row's lease (just set by the sweeper's own `ClaimDueAsync`) still active, and silently no-ops. The substitution is not merely "against the spirit of §5.5" — it actively breaks the crash-window recovery path it would appear to preserve.

### Group I — unit

**I5** (delete `services.AddScoped<ISagaIgnoredFactRecorder, EfCoreSagaIgnoredFactRecorder>();` from `AddOrdersSaga`) — `OrdersDispatcherRegistrationTests.RealHostComposition_Build_SucceedsWhenEveryPortIsRegisteredAndFailsWhenOneIsRemoved` (the **positive** case) failed at `Build()`, not at first message:
```
System.AggregateException
 ---> System.InvalidOperationException: Error while validating the service descriptor 'ServiceType: OrderToCash.Cqrs.ICommandHandler`1[OrderToCash.Orders.Application.Commands.HandlePaymentReceivedFactCommand] Lifetime: Transient ImplementationType: OrderToCash.Orders.Application.Commands.HandlePaymentReceivedFactCommandHandler': Unable to resolve service for type 'OrderToCash.Orders.Application.Ports.ISagaIgnoredFactRecorder' while attempting to activate 'OrderToCash.Orders.Application.Sagas.SagaFactHandler'.
```
(two of the four registration tests failed this way, each naming a different command handler chain reaching the same missing port).

### Group J — `Architecture.Tests` (see §4 for the correction this arming forced)

**J3 row 1** (`ProducerBuilder<string, byte[]>` reference added under `Application/Sagas/`) — first attempt, against the UN-corrected rule, stayed green (the finding in §4). Re-armed against the corrected rule: `FactPublisherConfinementTests.OnlyTheOutboxAdapterMayReferenceTheFactStreamProducerClient`:
```
Only *.Infrastructure.Outbox types may depend on Confluent.Kafka.ProducerBuilder`2 — R14's "no command handler, aggregate or domain service publishes directly" (design.md §10). Offending types: OrderToCash.Orders.Application.Sagas.ArmProbeJ3Producer
```

**J3 row 2** (`ConsumerBuilder<string, byte[]>` reference added under `Presentation/`) — `FactConsumerConfinementTests.OnlyTheFactStreamConsumerAdapterMayReferenceTheKafkaConsumerClient`:
```
Only *.Infrastructure.Messaging.Consumers types may depend on Confluent.Kafka.ConsumerBuilder`2 (design.md §10). Offending types: OrderToCash.Orders.Presentation.ArmProbeJ3Consumer
```

### Group K — the eight fact-emission branches (design.md §11.2)

| # | Branch | Mutation | Named test | Live caller? |
|---|---|---|---|---|
| K1 | `credit.approved.v1` emits `order.confirmed.v1` | delete `Confirm` call | `SagaStepTableTests.R21_...` — see B4 row 3 | no |
| K2 | `credit.released.v1` emits `order.completed.v1` | delete `Complete` call (`Apply` reduced to `{ }`) | general theory test — `Expected: Completed / Actual: Paid` | no |
| K3 | `stock.rejected.v1` emits `order.cancelled.v1` | delete the shared `order.Cancel(...)` call in `SagaFactHandler.ApplyStep` | `SagaCompensationStockRejectedTests.R26_...` — `System.TimeoutException : Order ... never reached status 'cancelled' within 00:00:20. Last observed: 'placed'.` (the same mutation also breaks R28's test, since the call site is shared — noted, not separately re-run) | no |
| K4 | `stock.released.v1`, `credit_rejected` branch, compensation steps | `CompensationStepsFrom` forced to return `[]` | `SagaStepTableTests.R28_SO7_...`, both cases — `Assert.Single() Failure: The collection was empty` | no |
| K5 | `stock.released.v1`, `order_cancelled` branch | delete the `"order_cancelled"` case from `MapReason` | `SagaStepTableTests.R28_SO7_...` (`order_cancelled` case) — `System.ArgumentOutOfRangeException : stock.released.v1 carried a reason outside the closed set {credit_rejected, order_cancelled}. (Parameter 'fact') Actual value was order_cancelled.` | **no — double force: no producer until feature 25** |
| K6 | `stock.rejected.v1` suppresses `stock.release` | see B4 row 1 | `SagaCompensationStockRejectedTests.R26_...` | no |
| K7 | `invoice.issued.v1` suppresses any command | see B4 row 2 | `SagaStepTableTests.R23_...` | no |
| K8 | `order.placed.v1`/`credit.rejected.v1` suppress status change | gave each row a status-changing `Apply` | `order.placed.v1`: general theory test, `Expected: Placed / Actual: StockReserved`. `credit.rejected.v1`: general theory test (`Expected: StockReserved / Actual: CreditApproved`) **and** `SagaStepTableTests.R27_CreditRejectedV1_LeavesTheStatusUntouchedAndOwesStockRelease` (`Assert.Null() Failure: Value is not null / Actual: Action\`2 {...}`) | yes (`order.placed.v1`); no (`credit.rejected.v1`) |

### Group L — integration

**L7 row 1** (disable the sweeper, `Sweeper.Enabled = false`) — confirmed via the ALREADY-written companion test `SagaCommandRetryTests.SO3_DisablingTheSweeper_LeavesTheCrashWindowRowUnresolved`, which asserts the negative directly (status stays `pending` after the same window that resolves it with the sweeper enabled) rather than re-running `SO3_...IsStillIssuedBySweeperCycle...` under a disabled sweeper — the same property, proven as its own permanent regression test rather than a one-off mutation probe. That test is green today; disabling the sweeper is exactly what it exercises.

**L7 row 2** (enqueue moved outside the transaction, after `RunOnceAsync` returns) — no EXISTING test noticed under normal operation (the happy path and the K/B arming rows all still passed against this mutation), confirming the design's own "if none does, add one" warning. **Added** `SagaCommandRetryTests.SO3_CommitBeforeIssue_WhenEnqueueFailsInsideTheTransactionTheAggregateChangeRollsBackToo` — a permanent test using a decorated `ISagaCommandStore` that throws on `EnqueueAsync` for `credit.hold`, proving atomicity by forcing the enqueue to fail and checking whether the aggregate's own status change rolled back with it. Against the mutation:
```
Assert.Equal() Failure: Strings differ
Expected: "placed"
Actual:   "stock_reserved"
```
Restored, re-run: green (`Duration: 15 s`). This is a genuinely new, permanent test, not a throwaway probe — it now guards commit-before-issue for every future change to `SagaFactHandler`.

## 6. Test counts

- `Orders.UnitTests`: **233** passed, 0 failed (was 205 before this feature; +28 from this feature's own files, net of the pre-existing 205 minus none removed — exact delta not separately tracked, 233 is the observed total).
- `Architecture.Tests`: **16** passed, 0 failed (14 pre-existing + `FactConsumerConfinementTests` new; `FactPublisherConfinementTests` amended in place).
- `Orders.IntegrationTests`: **63** passed, 0 failed, `5m 15s` (a clean, isolated run — see §7 for a caveat on a second, concurrent run that produced spurious `BadImageFormatException` failures from colliding build output, not a real regression; discarded).

## 7. `./quality.sh` and `./init.sh`

Both re-run clean, non-concurrent, after the flakiness investigation below (§3 deviation 5) was resolved.

**`./quality.sh` — exit 0.**

1. Format check: `dotnet format --verify-no-changes` — clean.
2. Build: `dotnet build` — 0 warnings, 0 errors, all 18 projects (6 services + `Contracts`/`Cqrs`/`SharedKernel`/`Seed` + 8 test projects).
3. Test: `dotnet test` at solution level — **all passed**, no failures, across every project:
   `Cqrs.UnitTests` 23, `SharedKernel.UnitTests` 47, `Contracts.UnitTests` 21, `Orders.UnitTests` 233, `Seed.UnitTests` 34, `Notifications.IntegrationTests` 7 (25s), `Seed.IntegrationTests` 6 (18s), `Fulfillment.IntegrationTests` 19 (1m6s), `Architecture.Tests` 16 (2s), `Billing.IntegrationTests` 23 (1m15s), `Orders.IntegrationTests` **63** (5m8s).
   This is a clean, isolated, single-invocation run of the *whole solution* — the earlier `BadImageFormatException`/coverlet PDB-lock noise reported against `OutboxRelayTests`/`OutboxWireParityTests` does **not** reproduce here, confirming it was a build-output collision from a concurrent `dotnet test` invocation I had left running in parallel, not a real regression.
4. Coverage summary printed (not gated — feature 34 owns the gate, per the script's own header comment): eleven `coverage.cobertura.xml` reports, line rates from 0.0% (a project with no line-coverable code reached by this run's assertions, e.g. host/Program entry assemblies) up to 97.2%; `Orders` — the layer this feature actually touched — reports in the 63–90% band across its several coverage-report shards. Not independently re-derived per-project into a single Orders-domain number here; the script's own TODO for feature 34 stands.

**`./init.sh` — exit 0** (only expected `WARN`s, zero `FAIL`s):
`1 feature in_progress: order_saga_orchestrator`, `SDD coherence: 3 sdd feature(s) past pending have their triple-doc`, `progress: 17/43 features done`, `progress/current.md is in lockstep with the backlog`, `54 uncommitted change(s) — expected mid-session`, and the standing `quality.sh` reminder (not run inside `init.sh` itself, by design, to keep it fast — run separately above).

**The flakiness investigation that preceded this clean run** (full account in §3 deviation 5): the A11-debt test `AllocateNextAsync_ConcurrentFirstEverAllocations_CanRaceTheSelfSeedInsertAndFail` was rewritten from a `System.Threading.Barrier`-based design (discovered live to deadlock — a slow connection-open leaves the other 15 tasks waiting forever, no timeout) to a two-phase `Task.WhenAll` design: all sixteen connections opened first, bounded by a 30s `CancellationTokenSource`, then a second, ungated `Task.WhenAll` fires all sixteen allocations. Verified over 5 sequential (never concurrent) `dotnet test --filter` runs, all green, ~2–3s each, no hangs. The stuck process trees from the deadlocked `Barrier` attempts were killed (`kill -9`) and confirmed fully dead (`ps aux`, `pgrep -P`) before this section's runs were started; `docker ps` showed no orphaned Testcontainers left behind (Ryuk had already reaped `quirky_lalande`/`peaceful_wing` and their sidecars).

## 8. M3 — live-stack walkthrough, recorded

Ran against the **existing, already-running** compose infra stack (Kafka, NATS, MS-SQL containers up before this session started), which already held `order.placed.v1` for `ORD-000007`, `ORD-000008` and `ORD-000009` at status `placed`, with zero `saga_commands`/`saga_ignored_facts` rows — confirmed by direct query before starting the host:

```
order_reference      status
ORD-000007           placed
ORD-000008           placed
ORD-000009           placed
saga_commands_count: 0
ignored_count: 0
```

Ran `dotnet run --project src/Orders/Orders.csproj` against this stack (no stand-in responders — the genuine "no Fulfillment yet" condition). Observed, from the structured logs:

- Three in-line retry sequences, one per order, each exactly three attempts (`attempt 1/3`, `2/3`, `3/3`) at `warn` level, each carrying `OrderToCash.Orders.Application.Ports.SagaCommandTransportError: fulfillment.stock.reserve: transport failure: no responder is subscribed to fulfillment.stock.reserve.`
- Three `fail`-level park entries: `Saga command StockReserve for order <id> parked after 3 attempts: ...` — one per order.
- Final DB state, queried after killing the process:

```
order_reference   command          status    attempts  last_error
ORD-000007        stock.reserve    parked    3         fulfillment.stock.reserve: transport failure: no responder is subscribed to fulf...
ORD-000008        stock.reserve    parked    3         fulfillment.stock.reserve: transport failure: no responder is subscribed to fulf...
ORD-000009        stock.reserve    parked    3         fulfillment.stock.reserve: transport failure: no responder is subscribed to fulf...
```

- Order status unchanged for all three (`placed`), confirming R19/R29's "status never touched while retrying/parked".

This is exactly design.md §8.2's predicted steady state. No clean-slate recreate was used — the existing state was the more informative one to observe, and matched the prediction exactly.

## 9. The §3.2 budget derivation, as actually verified

Confirmed live: three sequential in-line retry sequences (one order at a time, per the single serial consume loop, §3.4) completed well inside the poll loop's own budget — the whole three-order backlog resolved in well under a minute of wall-clock time in the live run, and the process never rebalanced or restarted. `max.poll.interval.ms` (300 000 ms default) was never approached, confirming SO10's decoupling holds: the ~16.5 s worst case per order sits on the dispatch worker, never the Kafka consume loop.

## 10. What could not be done, and why

Nothing in scope was left undone. Two things are explicitly **not** done, per the design's own out-of-scope list, and are not gaps: feature 27's DLQ/`order.saga_failed.v1` machinery, and feature 42's `RpcError`-body classification split (the seam for both is left exactly where design.md §6.5/§12 puts it).

## 11. Package section

**None.**

---

## 12. Fix round — D1, D2, and the four advisories (`progress/review_order_saga_orchestrator.md`)

### D1 — `SO9`'s committed-offset clause, rewritten and armed

**`EnableAutoCommit` stays `true`** in `KafkaFactStreamSubscriber.BuildConsumerConfig` — untouched, per the review's own instruction. The defect was entirely in the test's missing assertion, not in the source.

**What changed.**

- `tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs` — two new helpers: `ReadCommittedOffsetsAsync` (+ the `ReadCommittedOffsetTotalAsync` convenience wrapper) reads the `"orders.saga"` consumer group's **committed offset from the broker**, summed over every partition of a topic, via a throwaway `IConsumer<Ignore, byte[]>` configured with the same `GroupId` and `Committed(partitions, timeout)` — an OffsetFetch request that neither joins the group nor disturbs the real subscriber's assignment. `WaitForCommittedOffsetToExceedAsync` polls it. `AdminClient.ListConsumerGroupOffsetsAsync` (the review's other suggested option) was tried first and **crashed the whole test host natively** (no managed stack trace, `Test host process crashed`) when queried against a group with no prior member — reproduced twice, both on the very first call. `IConsumer.Committed` does not exhibit this and is what shipped.
- `tests/Orders.IntegrationTests/SagaConsumptionTests.cs` — `SO9_AHandlerThatThrows_LeavesTheCommittedOffsetUnchangedAndTheFactIsRedelivered` rewritten to assert **both halves** from the broker: the committed offset is unchanged across the failed first delivery, and it advances past that baseline only after the redelivery succeeds. `ThrowOnceGate`/`ThrowOnceSagaCommandStore` were extended so the **second** call to `EnqueueAsync` (the redelivery) now **blocks deterministically** on a `TaskCompletionSource` until the test calls `gate.Release()`, rather than proceeding the instant it arrives. This was not cosmetic: a first rewrite that read the broker's offset after a **fixed 7 s sleep** following the first throw was itself unguarded in the wrong direction — it was long enough that the real redelivery (`SagaFactsConsumer`'s own 2 s fixed retry delay, plus rejoin and a successful second attempt) had **already completed and committed** inside that window, so the "not before" assertion was silently comparing the wrong two points in time and failed on correct code (`Expected: 2, Actual: 3` reading two live baseline/after-failure snapshots one run apart). Caught by running the rewritten test once, before any arming — not by a mutation, and recorded here because a test that fails on correct code is exactly the kind of thing this feature's own standing rule (CLAUDE.md's arming protocol) exists to catch before it reaches a reviewer. The gate-based rewrite removes the wall-clock guess entirely: the test observes `gate.Attempts == 2` (the redelivery has arrived and is now provably blocked before it can touch the inner store), reads the broker, and only then releases it.

**Arming rows (forced rebuild after every restore, restored from a byte-identical backup copy each time, confirmed via `md5sum` against the backup before rebuilding):**

| # | Mutation | Result | Verbatim message |
|---|---|---|---|
| 1 | `EnableAutoCommit = false` (the reviewer's own probe, `KafkaFactStreamSubscriber.cs:115`) | **FAILS** | `the 'orders.saga' group's committed offset on 'otc.orders.facts.v1' never advanced past 0 after the successful redelivery (last observed 0) — SO9's 'only after success' half is unproven.` |
| 2 | F6 row 1 — `EnableAutoOffsetStore = true` (`KafkaFactStreamSubscriber.cs:116`) | **FAILS** | `the redelivery never reached the decorated store a second time within the wait budget.` — the wrongly-early-stored offset gets committed on `Close()` the instant the first handler throws, so the SAME message is never redelivered at all; `gate.Attempts` never reaches 2. |
| 3 | F6 row 2 — `StoreOffset` moved to **before** `await handler(...)` (`KafkaFactStreamSubscriber.cs:93-95`) | **FAILS** | identical message to row 2 — same mechanism (offset stored, then committed on the throwing consumer's `Close()`), different code path to get there. |

Confirming green after each restore: `SagaConsumptionTests` 2/2 (row-by-row, and together), then the **whole** `Orders.IntegrationTests` suite twice in full (63/63 both times), then the full solution via `./quality.sh` (below). One `SagaPreconditionTests.R25_...` failure was observed in the first whole-suite run (`Assert.Equal() Failure: Expected: 0, Actual: 1`, a test this fix round never touched); reproduced in isolation immediately after with **0 failures**, and the second whole-suite run passed clean at 63/63 — attributed to this session's own heavy concurrent resource pressure (a live compose stack plus the Testcontainers fleet, `free -h` showing swap fully exhausted at the time), not a regression, and not re-litigated further per instruction 4 ("nothing else").

### D2 — bookkeeping

`specs/order_saga_orchestrator/tasks.md:136` — **M5 ticked**. No other task's content changed; F5's own line already read the correct assertion (*"read the group's committed offset from the broker; do not infer it from the redelivery alone"*) — the gap was in the shipped test, not in the task's own wording, so F5's checkbox needed no further edit beyond what D1 repaired.

### Advisories

- **A1 (`EfCoreOrderNumberAllocator` self-seed race, feature 15).** Judgement: real, and it does need a backlog entry outside a test comment — the review's own diagnosis (`IF NOT EXISTS ... INSERT` with no lock hint, no duplicate-key handling, first-ever-allocation-only exposure) is correct and I have not re-derived it further; I did not touch `OrderNumberAllocatorTests.cs` or the allocator, per this feature's own hard boundary (`tasks.md` I6: "test-only ... a finding to report, not to fix in this feature"). Recommended wording for the leader to add to `feature_list.json` (not added by me — out of scope for this role):
  > `"title": "EfCoreOrderNumberAllocator: lock the self-seed existence check (WITH (UPDLOCK, HOLDLOCK) or duplicate-key retry) so two genuinely concurrent first-ever ORD allocations cannot both lose the seed race", "sdd": false`, phase/owner: feature 15, found by feature 16's `OrderNumberAllocatorTests.AllocateNextAsync_ConcurrentFirstEverAllocations_CanRaceTheSelfSeedInsertAndFail`.
- **A2 (race test polarity).** No code change — the test's own XML doc already states the polarity (asserts the race **reproduces**, goes red when the allocator is fixed) and the review confirms it reliable (6/6 across idle and 16-core-saturated runs). Recorded here for whichever session fixes A1: **invert, don't delete**, when that lands.
- **A3 (poisoned payload retries forever).** Judgement unchanged from the review: in scope for feature 27 (the DLQ feature), not this one — `SagaFactsConsumer.HandleMessageAsync`'s malformed-payload path and `design.md` §3.5's three routing outcomes do not cover a payload that throws during deserialization itself, and the seam is `SagaFactsConsumer.cs:123` plus its own `ExecuteAsync` catch/retry loop, exactly as the review found it. No code change.
- **A4 (design-vs-code divergences, now disclosed in §3).** Both were already correctly argued in code comments (`ISagaCommandDispatcher`'s own doc-comment for the claim-by-`(order_id, command)` identity; `design.md` §6.1's own text for `ISagaCommands`/`SagaCommandRequestFactory` referencing `Infrastructure.Messaging.Rpc` types directly) but missing from `impl_order_saga_orchestrator.md` §3 as the review noted. Recorded here rather than editing §3 retroactively, so this fix-round section stays the single place a re-reviewer needs to read for what changed and why: (i) `TryClaimAsync` claims by `(order_id, command)`, not `id` as `design.md` §6.3's illustrative claim SQL shows — the only identity a channel signal (`SagaCommandRef`) carries, a direct consequence of deviation 3's split, covered in substance by the port's own doc-comment. (ii) `ISagaCommands`/`SagaCommandRequestFactory` sit in `Application/` and reference `Infrastructure.Messaging.Rpc` types directly rather than a port-local DTO shape — `design.md` §6.1 chose this explicitly and it passed the gate; it is the first inward-pointing-arrow exception in the repository and no `Architecture.Tests` rule currently watches it. No code change this round; if a later feature wants it enforced, the rule belongs in `Architecture.Tests`.

### `./quality.sh` / `./init.sh`

- **`./quality.sh` — exit 0.** Format clean; build 0 warnings/0 errors across all 18 projects; `dotnet test` at solution level all green: `Cqrs.UnitTests` 23, `SharedKernel.UnitTests` 47, `Contracts.UnitTests` 21, `Orders.UnitTests` 233, `Seed.UnitTests` 34, `Architecture.Tests` 16, `Notifications.IntegrationTests` 7, `Seed.IntegrationTests` 6, `Fulfillment.IntegrationTests` 19, `Billing.IntegrationTests` 23, `Orders.IntegrationTests` **63** — no failures. Coverage summary printed (still not gated — feature 34 owns that, per the script's own header).
- **`./init.sh` — exit 0.** `1 feature in_progress: order_saga_orchestrator` (at the time init.sh was run mid-fix; `feature_list.json` was flipped to `in_review` immediately after, per this role's own closing step), SDD coherence and backlog counts unchanged, only the expected `WARN`s (uncommitted changes; `quality.sh` not run inside `init.sh` by design).

### What fails if this fix is reverted

- Revert D1's test to infer SO9 from `gate.Attempts` alone (the pre-fix-round shape): `EnableAutoCommit = false` stays **green** — the exact defect this round exists to close.
- Delete the `ThrowOnceGate.Release()` gating and go back to a fixed sleep: the test becomes flaky in the "not before" direction (proven live, above) — it can fail on **correct** code, which is worse than not testing the property at all, because it teaches the next person to raise the sleep instead of fixing the ordering.
- Revert `tasks.md` M5's tick: `CHECKPOINTS.md` C6 fails its own "every task ticked" check on the next `init.sh`/reviewer pass.

---

## 13. Fix round 2 — D3, and A5/A6 (`progress/review_order_saga_orchestrator.md` §2.5–2.6)

### D3 — `SagaPreconditionTests.DriveToCompletedAsync` synchronised, and the fix is armed

**Root cause, confirmed exactly as the review diagnosed it.** `order.placed.v1` reaches Kafka only through the outbox relay's own poll cycle; `DriveToCompletedAsync` published `stock.reserved.v1` **directly** to Kafka the instant `PlaceOrderAsync` returned, with no wait for the relayed `order.placed.v1` to have been consumed first. Both facts carry the precondition `OrderStatus.Placed` (`SagaStepTable.cs` rows 1–2), and `stock.reserved.v1`'s `Apply` moves the order to `StockReserved` while `order.placed.v1`'s does not (`Apply: null`) — so whichever is consumed first decides the outcome. If `stock.reserved.v1` wins (the common case, since it is published with no relay delay at all), the later `order.placed.v1` finds the order already past `Placed` and is correctly recorded `precondition_unmet` — which is exactly what `R25_...`'s `Assert.Equal(0, ...)` at line 45 was built to prove doesn't happen.

**Fix.** `DriveToCompletedAsync` (`tests/Orders.IntegrationTests/SagaPreconditionTests.cs:89-99`) now waits for the `stock.reserve` saga command to reach `sent` — the observable proof that `order.placed.v1` has been consumed and the saga has already acted on it — before publishing `stock.reserved.v1`, mirroring exactly the synchronisation `SagaHappyPathTests.cs:79` already uses:

```csharp
// review D3: wait for the relay-published order.placed.v1 to
// have been CONSUMED (observed via the stock.reserve command
// it issues) before publishing stock.reserved.v1 directly. ...
await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(connectionString, mssql, id, "stock.reserve", "sent", _wait);

await StandInSagaResponders.PublishFactAsync(kafka.BootstrapServers, SagaFactTopics.FulfillmentFacts, "stock.reserved.v1", id, ...);
```

Once `order.placed.v1` is guaranteed to have already been processed (a harmless no-op — `Apply: null` — before `stock.reserved.v1` arrives), the race the review found cannot occur: `order.placed.v1` never contends for the `Placed` precondition against a fact that has already moved the order past it.

**Arming — reviewer's own reproduction vehicle, widened `Relay.PollIntervalMs`.** The reviewer's exact mutation (200 ms → 6 s) did not reproduce reliably on this machine — 6/6 clean and later 5/5 clean at that value across two independent batches — but the SAME mechanism reproduces reliably at **1,500 ms**, which is still well above the production default and is the value used below. This machine appears to complete `DriveToCompletedAsync`'s six further transitions faster than the reviewer's environment did (their own report records the failing run at a 10 s total test duration; this machine's clean runs complete in 15–16 s including container-shared setup, but the actual place-and-drive portion that matters for the race is evidently faster here), so a shorter relay delay is what lands the redelivered `order.placed.v1` inside the same window. The mechanism — not the specific millisecond value — is what the review names as the defect, and it reproduces under it:

| # | State | Runs | Result |
|---|---|---|---|
| 1 | **Unfixed** `DriveToCompletedAsync` (my fix reverted to the original, unsynchronised shape) + `Relay.PollIntervalMs = 1_500` in `SagaIntegrationTestSupport.cs:43` | 12 sequential runs of `R25_...` alone | **11 PASS, 1 FAIL (run 11)** — `System.InvalidOperationException : Sequence contains no elements.` at `SagaPreconditionTests.cs:73` (the per-`eventType` `SagaIgnoredFacts...FirstAsync()` inside the redelivery loop) |
| 2 | **Fixed** `DriveToCompletedAsync` + the same `Relay.PollIntervalMs = 1_500` | 15 sequential runs of `R25_...` alone | **15/15 PASS** |

Row 1's failure is a **second, distinct manifestation of the same D3 root cause**, not a new defect: with the relay-delayed `order.placed.v1` racing the directly-published `stock.reserved.v1`, the stray `order.placed.v1` ignored row (marker `precondition_unmet`, unfiltered by event type) can satisfy the loop's `WaitForSagaIgnoredFactCountAsync(..., "precondition_unmet", ...)` gate for a *different* loop iteration before that iteration's own redelivered fact has itself been recorded, so the per-`eventType`-filtered `FirstAsync()` at line 73 finds no row yet and throws `InvalidOperationException: Sequence contains no elements` instead of failing the `Assert.Equal(0, ...)` at line 45. Both symptoms — the review's `Expected: 0 / Actual: 1` at line 45, and this run's `Sequence contains no elements` at line 73 — disappear under the identical fix (row 2), which is the confirmation that they share one cause rather than being two separate flakes.

Verbatim failure (row 1, run 11), full text preserved (not truncated):

```
[xUnit.net 00:00:31.20]     OrderToCash.Orders.IntegrationTests.SagaPreconditionTests.R25_EachOfTheTenConsumedFacts_RedeliveredAfterCompletion_IsIgnoredAndRecordedWithObservedAndExpectedStatus [FAIL]
  Failed OrderToCash.Orders.IntegrationTests.SagaPreconditionTests.R25_EachOfTheTenConsumedFacts_RedeliveredAfterCompletion_IsIgnoredAndRecordedWithObservedAndExpectedStatus [16 s]
  Error Message:
   System.InvalidOperationException : Sequence contains no elements.
  Stack Trace:
     at Microsoft.EntityFrameworkCore.Query.ShapedQueryCompilingExpressionVisitor.SingleAsync[TSource](IAsyncEnumerable`1 asyncEnumerable, CancellationToken cancellationToken)
   at OrderToCash.Orders.IntegrationTests.SagaPreconditionTests.R25_EachOfTheTenConsumedFacts_RedeliveredAfterCompletion_IsIgnoredAndRecordedWithObservedAndExpectedStatus() in .../SagaPreconditionTests.cs:line 73
```

**Restore, forced rebuild, confirming green.** `SagaIntegrationTestSupport.cs` was restored to `Relay.PollIntervalMs = 200` from a backup copy taken before this round's first mutation; `md5sum` against that backup after restore: **identical** (`7c71c079ab1d95ab11c0bd102c5c02ce` both). `touch`ed and rebuilt with `dotnet build --no-incremental` before every subsequent run — never relying on incremental compilation to pick up a restore. `SagaPreconditionTests.cs` was likewise restored to the fixed (D3-repaired) shape from its own backup, `diff`-confirmed identical.

**Whole-suite confirmation, at the normal 200 ms interval, run twice as instructed:**

| Run | Result |
|---|---|
| 1 | `Orders.IntegrationTests`: **Passed! Failed: 0, Passed: 63, Total: 63, Duration: 5m 15s** |
| 2 | `Orders.IntegrationTests`: **Passed! Failed: 0, Passed: 63, Total: 63, Duration: 5m 10s** |

### §12's attribution, superseded (not deleted)

§12's closing sentence attributed the single `SagaPreconditionTests.R25_...` failure observed during the D1/D2 fix pass to "this session's own heavy concurrent resource pressure ... not a regression, and not re-litigated further per instruction 4." **That attribution was wrong, and this section supersedes it rather than replacing it**, per the review's own instruction: the failure was a genuine, reproducible race in the test's own missing synchronisation (D3, above), not an artefact of memory pressure. Load did not create the failure; it only widened the timing window the missing wait already left open — the identical failure is producible on an otherwise idle run by delaying the relay alone, with no other change.

**What I would do differently.** A red integration test inside this feature's own suite is inside this feature's own scope, full stop — "nothing else" in a fix-round brief bounds which *other* files get touched, it was never license to leave a red run in this feature's own named requirement test unexamined. The right move when §12's whole-suite run produced that one failure was to isolate `SagaPreconditionTests` alone, immediately, and re-run it in a tight loop before writing any attribution at all; a single re-run "in isolation" that happened to pass is not evidence of "environmental," it is exactly what a race looks like on a lucky draw, and treating it as closed is the more expensive mistake of the two available in a distributed test suite — silently shipping a defect a five-minute isolation-and-loop would have caught, dressed as a clean report.

### A5 — SO9's baseline read, still unreproduced, no change made

The review recorded A5 as analysis, not a reproduction: `SagaConsumptionTests` shares the `orders.saga` consumer group and the fact topics with every other test in `SagaCollection`, sequentially, and a backlog left uncommitted by one test's host stopping could in principle be drained and auto-committed (the 5 s `auto.commit.interval.ms` library default) between the SO9 test's baseline read and its "not before" read, inflating the baseline out from under the "not before" assertion. I did not attempt to reproduce this — the review itself did not, and the brief does not ask for a fix, only a judgement. **Judgement: no code change.** The review's own suggested mitigation (require two baseline reads more than one auto-commit interval apart to agree before accepting either) is real but adds real runtime to a test that has not been observed to need it across this feature's now-several full-suite runs (two in this round alone, plus every run in round 1's own §12 record); adding it pre-emptively would be defending against a failure mode that has never been seen, at the cost of the exact kind of unexamined complexity this feature has twice been rejected for. If SO9 is ever seen to fail on correct code, this is the first place to look, and A5's own analysis above is the map.

### A6 — the same shape checked in both compensation tests; fixed in one, verified already-correct in the other

- **`SagaCompensationCreditRejectedTests.cs:50-55`** had the exact unsynchronised shape D3 named: `stock.reserved.v1` published directly to Kafka immediately after `PlaceOrderAsync`, with no wait for the relayed `order.placed.v1` to have been consumed first. **Fixed** with the identical pattern used for D3 — `WaitForSagaCommandCountAsync(..., "stock.reserve", "sent", ...)` inserted before the publish. This test does not currently assert an ignored-fact count (per the review's own note, "neither is currently exposed"), so it was not observed failing; the fix removes the defect class before a future assertion exposes it, exactly as instructed ("fix the shape wherever it exists rather than only where it was caught").
- **`SagaCompensationStockRejectedTests.cs`** — checked and found **already correct**, not matching A6's blanket description. Its first fact publish (`stock.rejected.v1`, line 44) is already preceded by `WaitForSagaCommandCountAsync(..., "stock.reserve", "sent", ...)` at line 41 — the same synchronisation this fix round adds elsewhere — so `order.placed.v1` is already guaranteed consumed before the test's own fact is published. This file's later redelivery of `stock.rejected.v1` against the already-`cancelled` order (lines 78-88) is a deliberate, already-synchronised R25 probe, not an instance of D3's shape. **No change made** to this file; A6's advisory text names it as a sibling with the same problem, but on inspection its own synchronisation predates this fix round and was already sound.

### `./quality.sh` / `./init.sh`, this round

- **`./quality.sh` — exit 0.** Full solution build (0 warnings, 0 errors) and full `dotnet test` at solution level, all green: `Orders.IntegrationTests` **63/63** (5 m 17 s), `Orders.UnitTests` 233, `Architecture.Tests` 16, `Fulfillment.IntegrationTests` 19, `Billing.IntegrationTests` 23, `Notifications.IntegrationTests` 7, `Seed.UnitTests` 34, `Seed.IntegrationTests` 6 — no failures anywhere. Coverage summary printed (still not gated — feature 34 owns that gate).
- **`./init.sh` — exit 0**, both before and after the `feature_list.json` transition below; only the expected `WARN`s (uncommitted changes mid-session).
- **`feature_list.json` id 16 → `in_review`**, per `tasks.md:136` M5 ("Set `order_saga_orchestrator` to `in_review` ... and stop"), which this brief's scope explicitly authorises as the one permitted change to that file. No other field or feature touched.

### What fails if D3's fix is reverted

- Revert `DriveToCompletedAsync`'s added wait, keep `Relay.PollIntervalMs = 1_500`: **FAILS**, reproduced 1 time in 12 (run 11, `System.InvalidOperationException : Sequence contains no elements.` at `SagaPreconditionTests.cs:73`) — the exact defect class the review's own P3 reproduced at line 45 with a different mutation value.
- Keep the fix, same `Relay.PollIntervalMs = 1_500`: **15/15 PASS** — the fix removes the race regardless of which of the two observed symptoms the timing happens to produce.
- `SagaCompensationCreditRejectedTests.cs`'s added wait is currently defensive (no assertion exposes it yet), so it cannot be armed by a red/green pair without first adding the ignored-fact assertion A6 itself declines to add; recorded as inspected-and-fixed, not arm-and-record, consistent with the review's own framing of A6 ("worth fixing ... not worth a round on its own").

---

## 14. Fix round 3 — D4, D5, and the attribution habit (`progress/review_order_saga_orchestrator.md` §3.4–3.9)

### §13, superseded (not deleted) — D5

§13's arming table row 1, and the paragraph beginning *"Row 1's failure is a **second, distinct manifestation of the same D3 root cause**"*, are **wrong** and are corrected here rather than edited in place, exactly as §13 itself correctly did for §12.

**What was actually true.** Round 3's review read `WaitForSagaIgnoredFactCountAsync` and showed that from iteration 2 of the ten-fact sweep onward the loop's wait is satisfied by an **earlier iteration's own row** — it takes no event-type and returns as soon as `count > 0` for `(correlationId, marker)` alone — so it gates nothing from iteration 2 onward, with or without D3's stray row. Run 11's `System.InvalidOperationException : Sequence contains no elements.` at `SagaPreconditionTests.cs:73` is therefore **not** a manifestation of D3; it is a second, independent unsynchronised gate in the same loop, reproduced by the reviewer on the D3-fixed code at the shipped 200 ms interval by removing only the incidental `db.Orders.SingleAsync` status read between the wait and the assertion (§3.4's Q4). That is **D4**, addressed below.

**Consequence for D3's record.** With row 1 reassigned to D4, §13 as originally written contains **no reproduction of D3's own symptom** (`Assert.Equal() Failure: Values differ / Expected: 0 / Actual: 1` at `SagaPreconditionTests.cs:45`). D3's fix is nonetheless correct — its guarantee is structural (§3.2 of the review: the `stock.reserve`/`sent` row is a **state gate**, proof by construction that `order.placed.v1` was consumed while the order was still `placed`, not a timing guess) — and round 3's own Q1/Q2 supply the arming §13 lacked: **unfixed 1 FAIL / 3 runs at `Relay.PollIntervalMs = 6_000`** (`SagaPreconditionTests.cs:45`, round 2's exact signature), **fixed 6 PASS / 6 runs** at the same interval. Round 4 should treat D3 as closed on the review's Q1/Q2, not on this report's superseded §13.

### D4 — the vacuous per-iteration wait, fixed and armed structurally

**Root cause.** `SagaPreconditionTests.R25_...`'s redelivery loop published each of the ten facts in turn and then called `WaitForSagaIgnoredFactCountAsync(connectionString, mssql, orderId, "precondition_unmet", _wait)` — a wait keyed on `(correlationId, marker)` only. From the loop's second iteration onward that predicate is already satisfied by the **previous** iteration's own ignored-fact row, so the call returns immediately without waiting for anything, and the subsequent event-type-filtered `FirstAsync()` at line 73/77 can run before the current iteration's own row has been written, throwing `Sequence contains no elements`.

**Fix — event-type-filtered overload, not an `atLeast` counter.** The review named two acceptable shapes and asked for the exact one, "because it is exact rather than ordinal": `WaitForSagaIgnoredFactCountAsync` in `SagaIntegrationTestSupport.cs` is now two overloads —

- the original `(connectionString, mssql, correlationId, marker, timeout)` signature is kept, now implemented as a thin forward to the new overload with `eventType: null` (`f.EventType == eventType` is skipped when `eventType` is `null`), so every existing call site compiles unchanged and keeps its original, already-adequate behaviour;
- a new `(connectionString, mssql, correlationId, eventType, marker, timeout)` overload filters on `f.CorrelationId == correlationId && f.Marker == marker && (eventType == null || f.EventType == eventType)`.

`SagaPreconditionTests.cs`'s loop now calls the filtered overload with **this iteration's own `eventType`** — the exact `(correlationId, eventType, marker)` triple the assertion at line 77 (formerly line 73) then reads. The wait and the read are now the same predicate, so the wait cannot return before the row the assertion needs exists.

**Other call sites checked, not changed, and here is why each is fine as `count > 0`:**

- `SagaPreconditionTests.cs` (SO8, `unknown_order` marker) — publishes exactly one candidate fact for a `correlationId` that matches no order; there is no "earlier iteration" to satisfy the wait early, so `count > 0` is already exact.
- `SagaCompensationStockRejectedTests.cs`'s redelivery probe (line 88, unchanged) — publishes exactly one redelivered fact (`stock.rejected.v1`) for the order's `correlationId` after it is already `cancelled`; again there is no other candidate row to race against, so `count > 0` is adequate. Read in full for this pass (not inferred), same conclusion the round-3 review reached independently for A6.

**Arming — the reviewer's own Q4 vehicle, reproduced independently first, then the repair proven to remove it.**

All three runs below are at the **shipped** configuration (`Relay.PollIntervalMs = 200`, the production default `SagaIntegrationTestSupport.cs` ships with) — no relay-interval mutation was needed for D4, unlike D3. Every mutation was backed up by copy first (`md5sum 676ecc6916d4f8ca9d929bf460ac8fea`), restored from that backup, `touch`ed and rebuilt with `dotnet build tests/Orders.IntegrationTests/ --no-incremental` before every subsequent run, and re-verified byte-identical to the backup by `md5sum` after the final restore.

| # | State | Runs | Result |
|---|---|---|---|
| 1 | **D4 unfixed** (loop calls the marker-only overload — i.e. the fix below reverted) **and** the incidental `db.Orders.SingleAsync`/status-assert lines removed (the review's Q4 vehicle, applied by me independently rather than only cited) | 4× `R25_...` alone | **3 FAIL / 1 PASS.** All three failures: `System.InvalidOperationException : Sequence contains no elements.` at `Microsoft.EntityFrameworkCore.Query.ShapedQueryCompilingExpressionVisitor.SingleAsync`, `SagaPreconditionTests.cs:77` — byte-identical exception type and site to the review's Q4 and to the original fix round's run-11 |
| 2 | **D4 fixed** (event-type-filtered wait in place) **and** the same incidental lines removed — i.e. exactly the review's Q4 configuration | 4× `R25_...` alone | **4/4 PASS** — the incidental query is no longer load-bearing, which is what the review asked the fix to prove |
| 3 | **D4 fixed**, incidental lines restored (the file's normal shape) | 4× `R25_...` alone | **4/4 PASS** |

Row 1's verbatim failure (run 1 of 4, full text not truncated):

```
[xUnit.net 00:00:56.38]     OrderToCash.Orders.IntegrationTests.SagaPreconditionTests.R25_EachOfTheTenConsumedFacts_RedeliveredAfterCompletion_IsIgnoredAndRecordedWithObservedAndExpectedStatus [FAIL]
  Failed OrderToCash.Orders.IntegrationTests.SagaPreconditionTests.R25_EachOfTheTenConsumedFacts_RedeliveredAfterCompletion_IsIgnoredAndRecordedWithObservedAndExpectedStatus [21 s]
  Error Message:
   System.InvalidOperationException : Sequence contains no elements.
  Stack Trace:
     at Microsoft.EntityFrameworkCore.Query.ShapedQueryCompilingExpressionVisitor.SingleAsync[TSource](IAsyncEnumerable`1 asyncEnumerable, CancellationToken cancellationToken)
   at Microsoft.EntityFrameworkCore.Query.ShapedQueryCompilingExpressionVisitor.SingleAsync[TSource](IAsyncEnumerable`1 asyncEnumerable, CancellationToken cancellationToken)
   at OrderToCash.Orders.IntegrationTests.SagaPreconditionTests.R25_EachOfTheTenConsumedFacts_RedeliveredAfterCompletion_IsIgnoredAndRecordedWithObservedAndExpectedStatus() in .../SagaPreconditionTests.cs:line 77
   ... (repeats through the loop's remaining iterations, then) ...
   at OrderToCash.Orders.IntegrationTests.SagaPreconditionTests.R25_EachOfTheTenConsumedFacts_RedeliveredAfterCompletion_IsIgnoredAndRecordedWithObservedAndExpectedStatus() in .../SagaPreconditionTests.cs:line 128

Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 21 s
```

Restore verified: `md5sum` of `SagaPreconditionTests.cs` after the final restore equals the pre-mutation backup (`676ecc6916d4f8ca9d929bf460ac8fea`, both), `touch`ed and rebuilt with `--no-incremental` before every confirming run.

**Reading the result honestly.** Row 1's 3-of-4 is a stronger, not weaker, separation than the review's own 2-of-4 reproduction of the same failure — consistent with the same failure being a genuine, near-deterministic defect in the vacuous wait once the incidental query that was accidentally hiding it is removed, not a rare race. Row 2 and row 3 both show 4/4 with the fix in place, with and without the incidental query present, which is the property the review asked the fix to establish: the loop's own wait, not an unrelated `SELECT`'s latency, is what gates the assertion now.

### The attribution habit, in my own words

This is the third time in this feature's record that a red run in this feature's own suite was closed by **attribution** — explaining why a failure was believed to be a known, already-fixed cause — rather than by **isolation**: loading the specific mutation that would either reproduce or rule out that explanation before writing it down. §12 attributed a whole-suite `R25_...` failure to "memory pressure"; it was D3. §13 attributed a differently-shaped `R25_...` failure ("`Sequence contains no elements`" instead of D3's own "`Expected: 0, Actual: 1`") to "the same D3 root cause" on the strength of a 15/15 clean re-run after the fix; it was D4, a second and independent defect in the same loop that D3's fix does not touch and never could, because D3's fix only changes when `stock.reserved.v1` is published — it does not change what `WaitForSagaIgnoredFactCountAsync` waits for.

Both attributions were made by someone who had, immediately beforehand, correctly diagnosed the *previous* defect — which is exactly what made each attribution feel safe rather than reckless: the shape of "I just fixed a race in this test, and here is another red run in the same test" reads as confirmation, not as a second, unrelated hypothesis to isolate first. Fifteen clean runs is not weak evidence in general; it is specifically weak evidence **against an unreproduced alternative hypothesis that was never loaded**, and a 1-in-~15-to-40 race is exactly what fifteen clean runs looks like whether or not the attributed cause is the real one. The cheap, mechanical rule that would have caught this each time, and that I applied on this round only after the reviewer named the pattern rather than on my own initiative: **before writing a sentence that explains a red run in this feature's own suite, isolate the failing test and loop it under the mutation that the explanation implies should reproduce it — not the mutation that was already believed to be the cause, but a probe of the specific new failure's own shape** (here: does the loop's wait, not the publish-order race, gate the assertion that actually threw?). A green re-run, however many times repeated, answers "does this still happen sometimes" and never answers "is this the same cause as last time" — only a targeted mutation of the *new* hypothesis does, and that step was skipped twice running before this round finally did it.

### `./quality.sh` / `./init.sh`, this round

- **`./quality.sh` — exit 0.** Full solution build (0 warnings, 0 errors) and full `dotnet test` at solution level, all green: `SharedKernel.UnitTests` 47, `Cqrs.UnitTests` 23, `Contracts.UnitTests` 21, `Orders.UnitTests` 233, `Seed.UnitTests` 34, `Architecture.Tests` 16, `Notifications.IntegrationTests` 7, `Seed.IntegrationTests` 6, `Fulfillment.IntegrationTests` 19, `Billing.IntegrationTests` 23, `Orders.IntegrationTests` **63/63** (5 m 39 s) — no failures anywhere. Coverage summary printed (still not gated — feature 34 owns that gate).
- **`./init.sh` — exit 0**, before and after the `feature_list.json` transition below.
- **`feature_list.json` id 16 → `in_review`**, per `tasks.md:136` M5, applied as a single-line, non-round-tripped edit (only the `"status"` value changed — `diff` against the pre-edit copy confirms exactly one line differs, no re-escaping, no reflow) specifically to avoid the mechanism recorded in the review's §3.13 incident. No other field, and no other feature, touched.

**`Orders.IntegrationTests`, run in full twice, as instructed:**

| Run | Result |
|---|---|
| 1 | **Passed! Failed: 0, Passed: 63, Total: 63, Duration: 7 m 52 s** |
| 2 | **Passed! Failed: 0, Passed: 63, Total: 63, Duration: 6 m 18 s** |

### What fails if D4's fix is reverted

- Revert the loop's wait to the marker-only overload, keep the incidental status-check lines removed (the review's own Q4 vehicle): **FAILS 3 of 4 runs**, `System.InvalidOperationException : Sequence contains no elements.` at `SagaPreconditionTests.cs:77`.
- Keep the fix, same lines removed: **4/4 PASS.**
- Keep the fix, lines restored (shipped shape): **4/4 PASS**, and separately, the full suite **63/63 twice**.

### Scope discipline this round

Per the review's §3.12 instruction 4, nothing under `src/`, `KafkaConsumptionTests.cs`-adjacent files, `SagaCompensationStockRejectedTests.cs`, or `specs/` was touched. Only `tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs` (the new overload), `tests/Orders.IntegrationTests/SagaPreconditionTests.cs` (the loop's call site), this report, and the single-line `feature_list.json` status transition were changed. D3 was not re-armed — round 3 §3.2's Q1/Q2 stand as its arming.
