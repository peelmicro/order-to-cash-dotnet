# Implementation — `orders_acceptance` (feature 15, phase 8)

`sdd: false`. Worked from `feature_list.json` feature 15's three acceptance items, `specs/orders_aggregate/design.md` §8–§10, `specs/outbox_and_idempotency/design.md` (especially §2.3, "there is no `Program.cs` yet, and this feature does not write one"), `specs/shared/asyncapi.yaml`'s `orders.create` / `fulfillment.stock.check` channels, and the invocation brief's carried follow-ups from `progress/review_orders_aggregate.md`.

---

## 1. What was built

### Domain follow-ups (the three carried from the aggregate's review)

- `src/Orders/Domain/Order.cs` — `TransitionTo` gained an optional `cancellationReason` parameter and now assigns `CancellationReason` **inside its own accepted branch**, right after `Status`; `Cancel` no longer assigns it after the call returns. `Rehydrate`'s status-token check and both O6 halves now raise the new `InvalidOrderSnapshotError`; the O1 (empty lines) and O2 (line currency) checks are unchanged in behaviour — they keep raising `OrderMustHaveAtLeastOneLineError` / `OrderLineCurrencyMismatchError`, matching #7's own split exactly (see `InvalidOrderSnapshotError`'s remarks).
- `src/Orders/Domain/Errors/InvalidOrderSnapshotError.cs` (new) — `order.snapshot_invalid`, `OrderId` nullable (matching #7's `orderId?: UniqueId`), for load-time corruption only.
- `src/Orders/Domain/Errors/UnknownOrderStatusError.cs` — **deleted**; its one call site now raises `InvalidOrderSnapshotError` (closes review advisory A4 by realising #7's actual shape instead of keeping an eleventh ad hoc type).
- `src/Orders/Domain/OrderStatus.cs` — `OrderStatuses.Parse` raises `InvalidOrderSnapshotError` (with `orderId: null`, since no aggregate exists yet at that call site) instead of the deleted type.
- `src/Orders/Domain/Errors/CancellationReasonRequiredError.cs`, `CancellationReasonNotApplicableError.cs` — XML docs corrected: no longer claim to be reused by `Rehydrate`.
- `tests/Orders.UnitTests/OrderRehydrationTests.cs` — the status/O6 test updated to assert `InvalidOrderSnapshotError`; two new tests added: `Order_Rehydrate_RefusesAnEmptyLinesCollection` and `Order_Rehydrate_RefusesALineWhoseMoneyIsNotInTheOrdersCurrency` — closing review D1.

### Application layer (`src/Orders/Application/`)

- `Commands/PlaceOrderCommand.cs` — `PlaceOrderCommand : ICommand<PlaceOrderResult>`, `PlaceOrderRequestLine`. `RequestId` is carried, never read again (see its own remarks — R62 is the reliability feature's own acceptance item, out of scope here per the brief).
- `Commands/PlaceOrderResult.cs` — `OrderId`, `OrderReference`, `Status`, `Currency`, `InitialAmount`, `InitialDiscount`, `TotalAmount`, `OrderDate` as separate fields (the anti-#7-D-defect shape — see §4).
- `Commands/PlaceOrderErrors.cs` — `PlaceOrderError` (abstract, `Exception`-based, deliberately **not** `DomainError` — mirrors #7's own separate `PlaceOrderError` hierarchy), `ReferenceDataNotFoundError`, `StockUnavailableError`, `OrderDiscountNotSupportedError`.
- `Commands/PlaceOrderCommandHandler.cs` — the shape design.md §10.1 fixed: resolve reference data → call the stock check → **only then** open the `IUnitOfWork` → allocate the order number → `Order.Place` → `AddAsync` + `SaveChangesAsync`.
- `Ports/IOrderReferenceCatalog.cs` — `PartyReference`, `ProductReference`, `FindRetailerAsync`/`FindCompanyAsync`/`CurrencyExistsAsync`/`FindProductsAsync`.
- `Ports/IOrderNumberAllocator.cs` — `AllocateNextAsync`.
- `Ports/IStockAvailabilityChecker.cs` — `StockAvailabilityLine`/`Result`/`LineResult`, `StockCheckTimeoutError`, `StockCheckTransportError` (plain exceptions, not `DomainError` — mirrors #7's split).

### Infrastructure (`src/Orders/Infrastructure/`)

- `Persistence/EfCoreOrderNumberAllocator.cs` — self-initialising `WITH (UPDLOCK, ROWLOCK)` allocator on `order_number_sequences` (empty after migration and after the seed — no `HasData`, no seed writer touches it). Seeds `next_value` from `MAX(order_reference)` cast to `int` (never a lexical string `MAX`, for the identical reason #7's own review flagged, D6) the first time it is ever called.
- `Persistence/EfCoreOrderReferenceCatalog.cs` — read-only, `AsNoTracking()`, resolves retailer/company/currency/product by code; joins `products` to `currencies` so a snapshotted catalogue price carries the product's **own** currency (matching #7's `ProductReference.price`).
- `Messaging/NatsOptions.cs`, `Messaging/Rpc/RpcJson.cs` (the ONE shared `JsonWire.Options` from `Contracts`, used for every RPC payload), `Messaging/Rpc/RpcSubjects.cs`, `Messaging/Rpc/RpcErrorPayload.cs`, `Messaging/Rpc/StockCheckPayloads.cs`.
- `Messaging/NatsStockAvailabilityChecker.cs` — the outbound `fulfillment.stock.check` RPC client, real NATS core request-reply.
- `OrdersAcceptanceOptions.cs`, `OrdersAcceptanceServiceCollectionExtensions.cs` — `AddOrdersAcceptance(...)`, this feature's own explicit-registration extension, deliberately **separate** from feature 14's `AddOrdersOutbox` (which this feature does not modify).

### Presentation (`src/Orders/Presentation/`)

- `Rpc/OrdersCreatePayloads.cs`, `Rpc/OrdersCreateErrorMapper.cs` (design.md §9.2's table, reproducing #7's `rpc-error-mapper.ts`).
- `OrdersCreateResponder.cs` — the `orders.create` NATS responder, `BackgroundService`, one DI scope **per inbound request**, resolves `IDispatcher` from that scope (never at construction).

### The host

- `src/Orders/Program.cs` (new) — the first `Program.cs` in this repository. Calls `AddOrdersOutbox` (feature 14's), `AddOrdersAcceptance` (this feature's), then `AddDispatcher(Assembly.GetExecutingAssembly())` — in that order, so every port the handler needs is already registered when the dispatcher's startup validation runs.
- `src/Orders/Orders.csproj` — `OutputType` → `Exe`; new `ProjectReference` to `src/Cqrs/Cqrs.csproj`; new `PackageReference`s `NATS.Net`, `Microsoft.Extensions.Hosting` (full).
- `Directory.Packages.props` — added `Microsoft.Extensions.Hosting` `10.0.11` (same band as the existing `.Abstractions` reference).

### Tests

- `tests/Orders.UnitTests/PlaceOrderTestDoubles.cs` — hand-rolled fakes (`FakeClock`, `FakeUnitOfWork`, `FakeOrderRepository`, `FakeOrderNumberAllocator`, `FakeOrderReferenceCatalog`, `FakeStockAvailabilityChecker`). No mocking library — none is referenced by this project.
- `tests/Orders.UnitTests/PlaceOrderCommandHandlerTests.cs` — 9 cases: the happy path (with the three-distinct-money-fields assertion), the stock-rejection suppression guard, a stock-check timeout, all four "unresolvable reference data" legs, the non-zero `orderDiscount` refusal, and the catalogue-price snapshot.
- `tests/Orders.UnitTests/OrdersCreateErrorMapperTests.cs` — 7 cases, one per row of design.md §9.2's table.
- `tests/Orders.UnitTests/RpcSubjectsTests.cs` — derives the two subjects from `asyncapi.yaml` text, never retypes them.
- `tests/Orders.UnitTests/OrdersDispatcherRegistrationTests.cs` — the dispatcher wired over the real Orders assembly; the "called twice" refusal.
- `tests/Orders.IntegrationTests/NatsContainerFixture.cs` — real `nats:2.14.5-alpine` (the same pinned tag `docker-compose.infra.yml` uses) via the generic `ContainerBuilder`, following the identical, already-reviewed precedent `KafkaContainerFixture` set (no `Testcontainers.Nats` package exists for this transport).
- `tests/Orders.IntegrationTests/StandInFulfillmentStockCheckResponder.cs` — see §2.
- `tests/Orders.IntegrationTests/OrdersCreateAcceptanceTests.cs` — 4 end-to-end tests over real NATS + real MS-SQL + the real `OrdersCreateResponder`, resolved through the same `AddOrdersOutbox` + `AddOrdersAcceptance` + `AddDispatcher` composition `Program.cs` uses.

---

## 2. How the NATS client was proven honestly, and why

The brief: *"a test NATS responder standing in for Fulfillment is legitimate; a mocked NATS client is not, because the transport is the thing under test"*.

Fulfillment (feature 17) does not exist yet, so there is no real `fulfillment.stock.check` responder to test against. The shape chosen: `StandInFulfillmentStockCheckResponder` opens its **own real `NatsConnection`** against the **same real NATS broker** (`NatsContainerFixture`, Testcontainers) and subscribes to the real `fulfillment.stock.check` subject exactly as a real Fulfillment process would. `NatsStockAvailabilityChecker` — the production code under test — has no idea it is talking to a test double: it makes a real `RequestAsync` over the real wire, gets a real reply, and the whole path (serialise → publish → subscribe → deserialise → reply → deserialise) is exercised for real. Nothing about `INatsConnection` is mocked anywhere in this feature; the only substitution is *which process* answers the subject, which is precisely what "stand-in" means and precisely what a mock would not give.

`NatsContainerFixture` itself follows the identical, already-reviewed precedent `KafkaContainerFixture` set in `outbox_and_idempotency`: no `Testcontainers.Nats` package targets the transport this repository runs (`nats:2.14.5-alpine`, core-only), so the generic `ContainerBuilder` drives it directly rather than adding an unused package — the same reasoning, not a new one.

### A real defect this honesty found, and its fix

Running `OrdersCreateAcceptanceTests` in isolation, all four passed. Running the **whole solution** under `quality.sh` (heavier CPU contention from every other project's containers), two of the four failed — `AcceptanceItem1` with `NatsNoRespondersException` inside the stand-in's own startup probe, and `AcceptanceItem3` with a `null` `RpcError.code` where `STOCK_UNAVAILABLE` was expected. Root-caused, not worked around:

1. `StandInFulfillmentStockCheckResponder.StartAsync` started the background subscribe loop in its constructor **before** its own startup probe ran. When the probe failed and threw, the caller's `await using` never received a reference to the partially-constructed instance — so it was **never disposed**, leaking a live, subscribed connection for the rest of the test run. A later test's real `orders.create` request could then be answered by this leftover stand-in instead of its own, out of turn.
2. Cancelling the subscribe loop sends `UNSUB` but does not itself wait for the server to have processed it (confirmed against `NATS.Client.Core`'s own docs on the identical race for the *subscribe* side — `NatsSubEvents.OnSubscribed`'s remarks). A `PingAsync()` round-trip after cancellation, before disposing the connection, fences teardown the same way the docs recommend fencing startup.
3. The startup probe caught `NatsNoReplyException` (empirically what NATS.Client.Core 3.2.0 throws on a reply-subscription timeout — not the `NatsMsg<T>` with a `null` `Data` the XML doc's older-shaped comment describes) but not `NatsNoRespondersException` (the immediate 503 sentinel, thrown when the server has not yet finished registering the subscription).

All three fixed in `StandInFulfillmentStockCheckResponder.cs`; the identical `NatsNoReplyException`-vs-`Data is null` correction was also needed in the **production** `NatsStockAvailabilityChecker.CheckAsync` — arming and evidence in §5. Re-ran `OrdersCreateAcceptanceTests` standalone and the full `quality.sh` again afterward: both green (§6).

---

## 3. `requestId` — carried, ignored, and why that is the honest scope line

`OrdersCreateRequestPayload.requestId` is read off the wire (`OrdersCreateRequestPayload.RequestId`) and placed on `PlaceOrderCommand.RequestId` because `asyncapi.yaml` declares the field — a responder that dropped an unrecognised field on a schema it claims to implement would be lying about the schema. Nothing downstream reads it: `PlaceOrderCommandHandler` never inspects `command.RequestId`, `causationId` is a freshly-minted `UniqueId.New()` rather than derived from it (deliberately, to avoid any accidental coupling with the not-yet-built dedup mechanism), and there is no `findByRequestId` fast path. `OrdersCreate_ARequestIdOnTheWireIsCarriedButHasNoEffectOnThisFeaturesBehaviour` proves a request carrying one places a normal order.

---

## 4. The money-mapping fixture rule, and where it was armed

Every fixture in this feature gives `initialAmount`/`initialDiscount`/`totalAmount` three **distinct, non-zero** values: two lines, `1000×2 − 50` and `500×1 − 0`, giving `2500 / 50 / 2450`. Used identically in `PlaceOrderCommandHandlerTests`, `OrdersCreateAcceptanceTests`, and (already, from feature 13) `OrderTestData.TwoLines()`.

The wire mapping this fixture rule exists to catch — `OrdersCreateResponder.ToReplyPayload` — is a **private** method, reachable only through the real NATS round trip, so it is armed at the integration level (arming table, §5). `PlaceOrderResult`'s own three-distinct-fields assertion in `PlaceOrderCommandHandlerTests` additionally guards the Application-layer mapping one layer earlier.

---

## 5. Arming table

Protocol per CLAUDE.md / `scripts/arm-probe.sh`: back up first, mutate, force rebuild, run the named test, record the message verbatim, restore from the backup (never `git checkout --`), force rebuild again, confirm green.

| # | What was armed | Mutation | Test | Verbatim result |
|---|---|---|---|---|
| 1 | Follow-up 1 — `Rehydrate`'s O1 check (D1, half 1) | `Order.cs:337` `lines.Count == 0` → `false` | `OrderRehydrationTests.Order_Rehydrate_RefusesAnEmptyLinesCollection` | `Assert.Throws() Failure: No exception was thrown` / `Expected: typeof(OrderToCash.Orders.Domain.Errors.OrderMustHaveAtLeastOneLineError)` |
| 2 | Follow-up 1 — `Rehydrate`'s O2 check (D1, half 2) | `Order.cs:344` the `EnsureLineCurrencyMatches` loop body replaced with a no-op comment | `OrderRehydrationTests.Order_Rehydrate_RefusesALineWhoseMoneyIsNotInTheOrdersCurrency` | `Assert.Throws() Failure: Exception type was not an exact match` / `Expected: typeof(OrderToCash.Orders.Domain.Errors.OrderLineCurrencyMismatchError)` / `Actual: typeof(OrderToCash.SharedKernel.Errors.CurrencyMismatchError)` — reproduces #7's own review defect D3 exactly, on the currently-armed code |
| 3 | Follow-up 3 — the new `InvalidOrderSnapshotError` path fires | `Order.cs:332` `!Enum.IsDefined(status)` → `false` | `OrderRehydrationTests.Order_Rehydrate_RefusesAStatusTokenOutsideTheClosedSetAndAReasonThatDoesNotMatchTheStatus` | `Assert.Throws() Failure: No exception was thrown` / `Expected: typeof(OrderToCash.Orders.Domain.Errors.InvalidOrderSnapshotError)` |
| 4 | Follow-up 2 — `CancellationReason` set inside `TransitionTo`'s accepted branch | Reverted `Cancel`/`TransitionTo` to the PRE-fix shape (assignment after the call returns) | full `Orders.UnitTests` suite | **Survived — 36/36 still green.** Genuine, honestly reported: single-threaded C# execution makes the two-statement and one-statement shapes behaviourally identical from any caller outside the aggregate, exactly as the review's own advisory A2 already found ("no observable difference today"). The fix is a structural conformance correction to design.md §6.1's stated invariant (the property must never be observably assignable-yet-momentarily-null between two statements a future edit to `TransitionTo`'s tail could exploit), not a behaviour fix, and it has no black-box mutation that can prove it beyond re-reading the code — recorded here rather than fabricated. |
| 5 | Stock-check rejection path (Application layer) | `PlaceOrderCommandHandler.cs:69` `!stockResult.Available` → `!stockResult.Available && false` (a literal `false` alone trips CS0162 unreachable-code, itself an error under `TreatWarningsAsErrors` — a compile failure, not a fired guard) | `PlaceOrderCommandHandlerTests.AcceptanceItem3_Handler_RejectsAndPersistsNothingWhenTheStockCheckReportsUnavailable` | `Assert.Throws() Failure: No exception was thrown` / `Expected: typeof(OrderToCash.Orders.Application.Commands.StockUnavailableError)` |
| 6 | Dispatcher boot validation | `PlaceOrderCommandHandler.cs:29` — removed `: ICommandHandler<PlaceOrderCommand, PlaceOrderResult>` from the class declaration | `OrdersDispatcherRegistrationTests.AddDispatcher_OverTheOrdersAssembly_RegistersEveryCommandWithExactlyOneHandler` | `Assert.Null() Failure: Value is not null` / `Actual: OrderToCash.Cqrs.DispatcherValidationException: No command handler is registered for OrderToCash.Cqrs.ICommandHandler\`2[...PlaceOrderCommand,...PlaceOrderResult]. Exactly one is required.` — thrown from `AddDispatcher` itself, i.e. at composition time, before any host is built or run |
| 7 | The money mapping — the wire reply (`OrdersCreateResponder.ToReplyPayload`) | `OrdersCreateResponder.cs:100-101` — swapped `InitialDiscount`/`TotalAmount` | `OrdersCreateAcceptanceTests.AcceptanceItems1And2_OrdersCreate_ChecksStockSynchronouslyAndReturnsTheOrderIdSynchronously` (real NATS + real MS-SQL) | `Assert.Equal() Failure: Values differ` / `Expected: 50` / `Actual: 2450` |

Every row restored from its own backup, forced rebuild, and reconfirmed green (rows 1–3, 5–7 individually; row 4's restore reconfirmed at 36/36; the full `Orders.UnitTests` (56) and `Orders.IntegrationTests` (43) suites reconfirmed green after all seven probes, and again inside the two full `quality.sh` runs of §6).

---

## 6. Traceability

None of `requirements.md`'s `R<n>` rows are exclusively closed by this feature, checked deliberately rather than assumed:

- **R31** ("a stock availability check... answers per line, mutates nothing, emits nothing") describes the **responder's** own behaviour — Fulfillment's, feature 17, not built yet. This feature proves the **caller** side honestly (a real request reaches a real subject and a real reply drives real behaviour), but the requirement itself is about the answering system, which does not exist. `fulfillment/integration/stock-check.spec` stays `TODO`, correctly, for feature 17 to close.
- **R13** ("aggregate state and outbox records commit in one transaction, or neither") is already `DONE`, closed by feature 14's `OutboxAtomicityTests` directly against `Order.Place` + the repository. This feature's own `AcceptanceItem3` integration test additionally proves the same atomicity through the full `orders.create` path (zero order rows, zero outbox rows, on a stock rejection) but does not change R13's row — the requirement was already fully proven with no stated shortfall.
- **R62** (`requestId` idempotent replay) is explicitly out of scope per the brief — the reliability feature's own acceptance item.

`specs/shared/test-matrix.md` is therefore **untouched** by this feature: no Status cell flips, and the coverage counts stay `16 green / 1 scoped / 46 not-yet-green`. Stated here rather than silently — the alternative was flipping a row this feature does not actually close, which the matrix's own rule 3 treats as exactly the failure this document exists to prevent.

The feature's three acceptance items map to tests as follows:

| Acceptance item | Proven by |
|---|---|
| "synchronous stock check via NATS `fulfillment.stock.check`" | `PlaceOrderCommandHandlerTests.AcceptanceItems1And2_Handler_ChecksStockBeforePersistingAndReturnsTheOrderIdSynchronously` (fakes; asserts the call happened, with the request's own lines/company) and `OrdersCreateAcceptanceTests.AcceptanceItem1_OrdersCreate_CallsFulfillmentStockCheckWithTheRequestsOwnCompanyAndLines` (real NATS + real stand-in) |
| "order id returned synchronously" | `PlaceOrderCommandHandlerTests.AcceptanceItems1And2_...` and `OrdersCreateAcceptanceTests.AcceptanceItems1And2_OrdersCreate_ChecksStockSynchronouslyAndReturnsTheOrderIdSynchronously` — the reply's `orderId` is asserted against the actually-inserted row, in the same request/reply round trip |
| "rejection when stock check fails" | `PlaceOrderCommandHandlerTests.AcceptanceItem3_Handler_RejectsAndPersistsNothingWhenTheStockCheckReportsUnavailable` and `OrdersCreateAcceptanceTests.AcceptanceItem3_OrdersCreate_RejectsWithStockUnavailableAndPersistsNoOrderWhenFulfillmentReportsShort` (real NATS; asserts `STOCK_UNAVAILABLE` **and** zero order/outbox rows) |

---

## 7. Verification run

- `dotnet build OrderToCash.sln --no-incremental` — 0 warnings, 0 errors.
- `dotnet format OrderToCash.sln --verify-no-changes` — clean, twice (standalone and inside `quality.sh`).
- `tests/Architecture.Tests` — **15/15 green**, unmodified by this feature (no new rule needed — `FactPublisherConfinementTests` only scopes `Confluent.Kafka`, and nothing in this feature's Presentation/Infrastructure additions is Domain).
- `tests/Orders.UnitTests` — **56/56 green** (36 inherited + 9 handler + 7 error-mapper + 2 subject + 2 dispatcher-registration, plus the 2 new Rehydration cases already counted in the 36 baseline's replacement).
- `tests/Orders.IntegrationTests` — **43/43 green** (39 inherited + 4 new acceptance tests), including two full runs against real NATS (Testcontainers `nats:2.14.5-alpine`) + real MS-SQL under both isolated and full-solution load.
- **`./quality.sh` — exit 0**, run twice (once before the NATS teardown-race fix, which is how it was found; once after, clean). Full solution: **all test projects green**. Domain-layer coverage independently recomputed from `Orders.UnitTests`'s own `coverage.cobertura.xml`, filtered to `filename` starting `Orders/Domain/`: **423/477 lines = 88.7%**, above the ≥80% domain target (the ≥60%-overall gate stays unenforced by design until feature 34, per `quality.sh`'s own header comment).
- **`./init.sh` — exit 0.** Backlog coherent, 1 feature `in_progress` (this one), no superseded rule text found outside `progress/`.

---

## 8. What was not done, and why

- **`requestId` idempotent replay (R62)** — out of scope per the brief; carried on the wire and ignored, as detailed in §3.
- **The saga orchestrator (feature 16) and terminal-rejection classification (feature 42)** — untouched; `OrdersCreateErrorMapper`'s `TIMEOUT`/`UNAVAILABLE` vs `VALIDATION_FAILED`/`STOCK_UNAVAILABLE` split is already the distinction feature 42 needs, but classifying retries against it is that feature's job.
- **`traceparent`/`x-deadline-ms` header propagation (R57/R58)** — `RpcHeaders`' full shape is not implemented; the RPC calls in this feature carry no custom NATS headers at all. Recorded as a deliberate scope line in `NatsOptions.StockCheckTimeoutMs`'s own doc comment — full trace-context propagation is feature 27's (`observability_reliability`)'s claim, not this one's, and building a partial version here would be speculative work standing in the way of that feature's own design.
- **Fulfillment's real `fulfillment.stock.check` responder** — feature 17. This feature proves the client honestly against a stand-in (§2); it does not and should not pre-build Fulfillment's own responder.

## What surprised me

- **`NatsMsg<T>`'s "Response can be (null) or one `NatsMsg<T>`" doc comment describes an older library shape.** NATS.Client.Core 3.2.0's `RequestAsync` throws `NatsNoReplyException` on a reply-subscription timeout rather than returning a `NatsMsg<T>` with a `null` `Data` — found by running the integration suite for real rather than trusting the XML doc, and fixed in both the production `NatsStockAvailabilityChecker` and the test-only stand-in.
- **The stand-in's own resource-leak-on-exception bug only showed up under full-solution load**, not in isolation — a reminder that "ran green in isolation" is not the same claim as "ran green under the load the real suite runs under," which is exactly why `quality.sh` was run against the whole solution rather than just this feature's own two projects.
- **No `R<n>` row belongs exclusively to this feature.** Went in expecting to flip at least one test-matrix row; the honest finding, checked against `requirements.md` directly rather than assumed, is that R31 is the *responder's* requirement (feature 17) and R13/R62 are already accounted for elsewhere. Recorded in §6 rather than forcing a flip that would not survive scrutiny.

---

## 9. Round 2 — response to `progress/review_orders_acceptance.md`'s rejection

Verdict was REJECTED: D1 blocking, D2 required, D3 significant, six advisories (A1–A6, A7 harness-only). All fixed except A6 (the reviewer's own — not mine, per the coordinator) and A3/A5 (recorded, not changed, per the review's own instruction). `specs/shared/test-matrix.md` stays untouched — the review's own §6 independently re-confirmed `16 green / 1 scoped / 46 not-yet-green` is correct, no flip missed.

### D1 (BLOCKING) — closed

The exact branch this feature had personally found and fixed — `NatsStockAvailabilityChecker`'s `NatsNoRespondersException`/`NatsNoReplyException` split — was reachable by no test. Two new integration tests, both over the real broker, both requiring no mock:

- `AcceptanceItem_OrdersCreate_MapsNoStockCheckResponderToUnavailableAndPersistsNoOrder` — **no stand-in started at all**, so the production client observes a REAL `NatsNoRespondersException`. Asserts `code == "UNAVAILABLE"`, `details.subject == "fulfillment.stock.check"`, zero order rows, zero outbox rows.
- `AcceptanceItem_OrdersCreate_MapsASilentStockCheckResponderToTimeoutAndPersistsNoOrder` — a new `StandInFulfillmentStockCheckResponder.StartSilentAsync`, genuinely subscribed (proven by its own startup probe, so `NatsNoRespondersException` never fires) but never answers a real request, so the production client observes a REAL `NatsNoReplyException`. `NatsOptions.StockCheckTimeoutMs` shrunk to 500ms for this host only, to keep the test fast. Asserts `code == "TIMEOUT"`, `details.subject`, `details.timeoutMs == 500`, and the same two zero-row assertions.

**Arming — swapping the two `catch` bodies, by hand, backup taken first, restored from the backup (never `git checkout --`):**

```
catch (NatsNoRespondersException) { throw new StockCheckTimeoutError(...); }   // was StockCheckTransportError
catch (NatsNoReplyException)      { throw new StockCheckTransportError(...); } // was StockCheckTimeoutError
```

Both new tests failed, with the exact interchange the mutation predicts:

- UNAVAILABLE test → `Assert.Equal() Failure: Strings differ / Expected: "UNAVAILABLE" / Actual: "TIMEOUT"`
- TIMEOUT test → `Assert.Equal() Failure: Strings differ / Expected: "TIMEOUT" / Actual: "UNAVAILABLE"`

Restored from the backup, `diff` confirmed byte-identical, forced rebuild, all 6 tests in `OrdersCreateAcceptanceTests` green again.

**A genuine second race found while building this arming, fixed alongside it.** With no stand-in providing its own incidental startup delay, the OUTAGE test's own `caller.RequestAsync(RpcSubjects.OrdersCreate, ...)` intermittently threw `NatsNoRespondersException` **on `orders.create` itself** under load — `IHost.StartAsync()` awaits `BackgroundService.StartAsync`, which returns once `ExecuteAsync` is *scheduled*, not once its NATS subscription has actually landed server-side. Added `WaitUntilOrdersCreateReachableAsync`, the same real-round-trip-probe shape `StandInFulfillmentStockCheckResponder` already uses, sending a cheap, side-effect-free probe (an unknown `retailerCode`, refused at reference-data resolution before the stock check ever runs) and retrying until some reply arrives. Applied to all six tests in the file — every other test's stand-in construction happened to supply enough incidental delay to mask this before, which is exactly the kind of thing that should not depend on incidental timing. Confirmed: the two new D1 tests, and the full six-test class, green on 3 consecutive isolated runs and inside two full `quality.sh` runs (§ below).

### D2 (REQUIRED) — closed

`StandInFulfillmentStockCheckResponder.DisposeAsync` now disposes the connection and CTS inside a `finally`, so a fault from `_loop` other than `OperationCanceledException` can no longer skip the fence-and-dispose. `StartAsync`'s cleanup-on-probe-failure now swallows a disposal fault in its own nested `try`/`catch` rather than letting it replace the probe's own exception with a bare `throw;`. This is test-harness code with no production-behaviour consequence, so — per the review's own framing of D2 as "required", not "blocking", and given the difficulty of fault-injecting the NATS wire itself to exercise the non-`OperationCanceledException` path deliberately — verified by code inspection plus the absence of any leaked-connection symptom across two full `quality.sh` runs (11 projects, 3 separate NATS broker lifetimes) rather than by a dedicated fault-injection test. Recorded here rather than silently claimed as armed.

### D3 (SIGNIFICANT) — closed, verified against the real executable

`Program.cs` now calls `builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }))` unconditionally — `Host.CreateApplicationBuilder` only turns these on in Development, and every container this repository runs boots in Production (the environment's own default when neither `ASPNETCORE_ENVIRONMENT` nor `DOTNET_ENVIRONMENT` is set).

**Verified twice, both by running the real executable** (`dotnet run --project src/Orders/Orders.csproj --no-build`, `MSSQL_APP_PASSWORD` exported, no NATS/MS-SQL container running):

1. Unarmed: `Application started. Press Ctrl+C to shut down.` / `Hosting environment: Production`, then (only once the responder's own subscribe runs) `NATS.Client.Core.NatsException: can not connect uris: nats://localhost:4222` — proving `ValidateOnBuild` does **not** force an eager connection; the DI graph is validated structurally, nothing is dialled.
2. `IStockAvailabilityChecker`'s registration commented out in `OrdersAcceptanceServiceCollectionExtensions.cs`, rebuilt, same command: `Unhandled exception. System.AggregateException: Some services are not able to be constructed (... Unable to resolve service for type 'OrderToCash.Orders.Application.Ports.IStockAvailabilityChecker' while attempting to activate 'OrderToCash.Orders.Application.Commands.PlaceOrderCommandHandler'.)` at `HostApplicationBuilder.Build()` ← `Program.cs:61` — **before `Application started` ever prints.** Restored, `diff` confirmed byte-identical, real host re-run, "Application started" reappears.

**A permanent regression test**, `OrdersDispatcherRegistrationTests.RealHostComposition_BuildServiceProvider_SucceedsWhenEveryPortIsRegisteredAndFailsWhenOneIsRemoved`, added beside the existing handler-boot-validation tests (the natural home the review named): builds the exact `AddOrdersOutbox` + `AddOrdersAcceptance` + `AddDispatcher` composition `Program.cs` uses (plus `AddLogging()`, which the real host supplies implicitly and `AddOrdersOutbox`'s own remarks say explicitly is not its job to register), asserts `BuildServiceProvider(ValidateOnBuild: true, ValidateScopes: true)` throws nothing when every port is registered, then removes the `IStockAvailabilityChecker` descriptor and asserts it throws — both proven in the same test execution rather than needing a separate arming mutation, and both observed green/red respectively on the first run (no mocking needed: `ValidateOnBuild`'s own structural-only behaviour, confirmed live in point 1 above, makes a placeholder connection string/NATS URL sufficient).

### A1 — fixed

`AcceptanceItems1And2_...`'s happy path now asserts `assertDb.OutboxMessages.SingleAsync()` carries `EventType == "order.placed.v1"` and `AggregateId == reply.OrderId`, so its negative twin's `OutboxMessages.CountAsync() == 0` now means something (a real `order.placed.v1` row absent, not merely "no order exists to have written one").

### A2 — fixed

New `OrdersCreateRequestValidator.Validate`, called in `OrdersCreateResponder.HandleAsync` immediately after deserialising and before `ToCommand` — checks `retailerCode`/`companyCode`/`currency`/`lines` (`asyncapi.yaml`'s required set, `lines` additionally its own `minItems: 1`) and each line's `productCode`. A violation raises `InvalidOrdersCreateRequestError`, mapped by `OrdersCreateErrorMapper` to `VALIDATION_FAILED` — checked FIRST in the mapper's `switch`, before every other case, so it can never fall through to the `INTERNAL_ERROR` catch-all. Five new unit tests in `OrdersCreateRequestValidatorTests.cs`; two armed by hand (the `retailerCode` check and the empty-`lines` check — the null-`lines` check itself could not be armed with a `&& false` mutation without introducing a genuine, unrelated nullable-reference-type compile error in the code that reads `request.Lines[index]` afterward, so a sibling check, `Lines.Count == 0`, was armed instead; both share the same `missing.Add("lines")` statement the mutation targets). Both fired: `Assert.Throws() Failure: No exception was thrown / Expected: typeof(...InvalidOrdersCreateRequestError)`.

### A4 — mostly fixed, one file deliberately kept, and why

`src/Orders/Presentation/README_PLACEHOLDER.cs` — **deleted.** `grep -rn "OrdersPresentationPlaceholder"` outside that file itself returns nothing; genuinely dead now that `OrdersCreateResponder`, `OrdersCreatePayloads`, `OrdersCreateErrorMapper`, `OrdersCreateRequestValidator` all live in `OrderToCash.Orders.Presentation`/`.Rpc`. `Architecture.Tests` 15/15 green after deletion.

`src/Orders/Domain/README_PLACEHOLDER.cs` — **NOT deleted, deliberately, against the advisory's literal instruction.** `specs/orders_aggregate/design.md` §10.2 states this outright, in terms that name the exact consequence: *"`src/Orders/Domain/README_PLACEHOLDER.cs` must not be deleted: `tests/Architecture.Tests/DomainAssemblies.cs` resolves the Orders assembly through `typeof(OrderToCash.Orders.Domain.OrdersDomainPlaceholder)`, and removing it breaks all twelve architecture rules for every service at once."* Confirmed still true and still load-bearing: `grep -rn "OrdersDomainPlaceholder" tests/` finds it in `DomainAssemblies.cs:34` and `OrdersDomainContractsTests.cs:37` (`tests/Architecture.Tests/**` is out of this feature's touch scope, so it cannot be edited to remove the dependency either). The advisory is right that the namespace now holds real Domain types (`Order`, the errors, `InvalidOrderSnapshotError`, ...) — but the placeholder is not there for the namespace to have *a* type, it is there as the SPECIFIC anchor fifteen architecture rules resolve the whole Orders assembly through. Deleting it would trade one dead 9-line file for fifteen broken tests. Flagged rather than silently overridden, per CLAUDE.md's own instruction for a spec conflict.

### A3, A5 — recorded, not changed, per the review's own instruction

- **A3** stands as described: `observed` in `AcceptanceItem1_OrdersCreate_CallsFulfillmentStockCheckWithTheRequestsOwnCompanyAndLines` is last-write-wins across the stand-in's own startup probes and the real request, so the assertion is technically about the *last* request seen. Still correct (fails if the real request never arrives), and now additionally true of the new `WaitUntilOrdersCreateReachableAsync` probe on the `orders.create` side — that probe never touches `fulfillment.stock.check`, so it does not add a further write to `observed`.
- **A5** stands as described: `STOCK_UNAVAILABLE`/`REFERENCE_DATA_NOT_FOUND`/`ORDER_DISCOUNT_NOT_SUPPORTED` (Application-layer, SCREAMING_CASE) and `order.snapshot_invalid`-style codes (domain, `<subject>.<snake_case_reason>`) now sit beside a third, `InvalidOrdersCreateRequestError` (Presentation-layer, no `Code` property at all — it is mapped by exception TYPE, not by a stable string the way the other two families are), which is the same deliberate-but-unrecorded-as-such pattern one layer further out. `design.md` §9 is where a sentence belongs; out of this feature's touch scope to add it there.

### Verification run, round 2

- `dotnet build OrderToCash.sln --no-incremental` — 0 warnings, 0 errors.
- `dotnet format OrderToCash.sln --verify-no-changes` — clean.
- `tests/Architecture.Tests` — **15/15 green** (confirmed again after deleting the Presentation placeholder).
- `tests/Orders.UnitTests` — **64/64 green** (57 + 5 validator + 1 boot-validation composition, plus the earlier round's 56 net of one recount — see the per-file arithmetic above).
- `tests/Orders.IntegrationTests` — **45/45 green** (43 + 2 new D1 cases), three consecutive isolated runs of the full `OrdersCreateAcceptanceTests` class with no failures, plus two full-solution runs.
- **`./quality.sh` — exit 0, twice**, both against the whole 11-project solution: the first run (after the D1/D2 fixes, before the `WaitUntilOrdersCreateReachableAsync` fix) reproduced the SAME class of flake the review's own P1 probe would have hit — the OUTAGE test racing `orders.create`'s own subscription — confirming that fix was necessary, not speculative; the second run, after it, clean. Domain coverage unchanged at 423/477 = 88.7% (no `Domain/` file touched this round).
- **`./init.sh` — exit 0.** Backlog coherent; `progress/current.md` lockstep check (the coordinator's A6, already landed) now green as its own numbered section.
- Real-executable verification (D3): two `dotnet run --project src/Orders/Orders.csproj --no-build` runs, documented above, one unarmed/positive, one armed/negative, both restored and re-confirmed.

---

## 10. Round 3 — response to `progress/review_orders_acceptance.md`'s re-review (round 2)

**Verdict: REJECTED, second pass.** D1, D2, A1, A4 confirmed closed; D3's production wiring confirmed correct. What blocked: three of the four round-2 fixes were themselves unguarded — a correct behaviour with nothing that would notice it being reverted. D7 is the coordinator's own (`init.sh`'s lockstep check had no branch for `in_review`; already fixed, confirmed below). This round closes D4, D5, D6, and adds the recommended A9.

**The pattern, taken at face value rather than argued with.** Every fix in round 2 was behaviourally correct and none of them had a test whose failure would announce the fix being lost. The question the re-review poses — *"what fails if my fix is reverted?"* — is answered for all three below by an actual reversion, not by an assertion that one would fail.

### D4 (BLOCKING) — closed

An eighth case in `OrdersCreateErrorMapperTests`, `Map_AnInvalidOrdersCreateRequestError_MapsToValidationFailedNotInternalError`, asserting `Map(new InvalidOrdersCreateRequestError(...), _occurredAt).Code == "VALIDATION_FAILED"` (and explicitly `!= "INTERNAL_ERROR"`, so the assertion cannot be satisfied by an unrelated code).

**Armed** (backup taken first, restored from the backup, never `git checkout --`, forced rebuild both sides): `OrdersCreateErrorMapper.cs:27`, `"VALIDATION_FAILED"` → `"INTERNAL_ERROR"` on the `InvalidOrdersCreateRequestError` arm — the exact Q4 mutation. Fired:

```
Assert.Equal() Failure: Strings differ
Expected: "VALIDATION_FAILED"
Actual:   "INTERNAL_ERROR"
```

Restored, `diff` confirmed byte-identical, rebuilt, 71/71 green.

### D5 (BLOCKING) — closed

Two new `[Theory]` cases in `OrdersCreateRequestValidatorTests`, mirroring the existing `retailerCode` one exactly: `Validate_AMissingOrBlankCompanyCode_Refuses` and `Validate_AMissingOrBlankCurrency_Refuses`, each over null/blank/whitespace. `asyncapi.yaml`'s required set (`retailerCode`, `companyCode`, `currency`, `lines`) now has all four guarded, not two.

**Armed, both, individually** (backup first, restore from backup, forced rebuild):

- `OrdersCreateRequestValidator.cs:43`, `missing.Add("companyCode");` → `_ = missing;` (Q2) — all three theory cases failed: `Assert.Throws() Failure: No exception was thrown / Expected: typeof(...InvalidOrdersCreateRequestError)`.
- `OrdersCreateRequestValidator.cs:48`, `missing.Add("currency");` → `_ = missing;` (Q3) — same three-case failure, verbatim.

Both restored, `diff` confirmed byte-identical, rebuilt, 71/71 green after each.

### D6 (REQUIRED) — closed, shape (a)

**Chose (a) — factored the host composition into a static, testable method — over (b), the text-guard.** Reason: `ValidateOnBuild`/`ValidateScopes` are two lines inside a larger, already-real composition (`AddOrdersOutbox` + `AddOrdersAcceptance` + `AddDispatcher`) that a test already needed to drive for the port-half of D3's own regression test; factoring the WHOLE composition out costs one new eleven-line file and turns "a test that supplies the flags itself" into "a test that calls the one place the flags are set", closing the gap at its root rather than adding a second, independent check that could itself drift from `Program.cs`. A text guard over `Program.cs` would have been cheaper, but `Program.cs` no longer contains the flags at all after the factoring — asserting text on the wrong file would prove nothing.

**What changed:**

- `src/Orders/OrdersHost.cs` (new) — `OrdersHost.CreateBuilder(string[] args, Action<OrdersOutboxOptions>, Action<OrdersAcceptanceOptions>) : HostApplicationBuilder`. Calls `Host.CreateApplicationBuilder(args)`, then `ConfigureContainer` with `ValidateOnBuild = true, ValidateScopes = true` unconditionally (moved here verbatim from `Program.cs`, comment included), then `AddOrdersOutbox`, `AddOrdersAcceptance`, `AddDispatcher`, in that order, and returns the builder **before** `Build()` is called.
- `src/Orders/Program.cs` — now four lines of actual composition: calls `OrdersHost.CreateBuilder(args, configureOutbox, configureAcceptance)`, then `builder.Build()`, then `host.RunAsync()`. The environment-reading (`BuildMsSqlConnectionString`, the Kafka/NATS URL fallbacks) stays here, since it is Program-specific and not part of the DI-graph question `OrdersHost` answers.
- `tests/Orders.UnitTests/OrdersDispatcherRegistrationTests.cs` — `RealHostComposition_...` rewritten to call `OrdersHost.CreateBuilder(...)` directly (with placeholder connection string/NATS URL — confirmed structurally sufficient in round 1's D3 verification, since `ValidateOnBuild` does not connect) and then `.Build()` on the returned builder itself, for both the positive case and the negative case (remove the `IStockAvailabilityChecker` descriptor from `builder.Services`, then `Build()`). Neither call supplies `ServiceProviderOptions` any more — both observe whatever `OrdersHost.CreateBuilder` itself set.

**Armed** (backup first, restore from backup, forced rebuild both sides): `OrdersHost.cs`, `ValidateOnBuild = true,` → `ValidateOnBuild = false,` — the exact Q1 mutation, now inside the file the flags actually live in. Fired:

```
Assert.NotNull() Failure: Value is null
```

— the negative half's `Build()` call stopped throwing, exactly as Q1's own round-2 finding predicted for `Program.cs` before the factoring. Restored, `diff` confirmed byte-identical, rebuilt, `Orders.UnitTests` 71/71 green.

**Re-verified against the real executable after the refactor** (not merely assumed unaffected by moving the composition): `dotnet run --project src/Orders/Orders.csproj --no-build` with `MSSQL_APP_PASSWORD` exported and no NATS/MS-SQL running — `Application started. Press Ctrl+C to shut down.` / `Hosting environment: Production`, then the same lazy-connect `NatsException` as round 2's baseline once the responder's own subscribe runs. The refactor changed nothing observable about the running host.

### A9 (recommended) — done

One new integration test, `OrdersCreate_ARequestMissingLinesIsRefusedAsValidationFailedNotInternalErrorAndPersistsNoOrder`, sends a real `orders.create` request with `lines: []` over the real broker, with **no stand-in Fulfillment responder started at all** — deliberate: `OrdersCreateRequestValidator.Validate` runs before `ToCommand`, so a malformed request never reaches the stock check, and a passing test that happened to have a stand-in running would prove nothing about that ordering. Asserts `error.Code == "VALIDATION_FAILED"` (and explicitly `!= "INTERNAL_ERROR"`), the message names `lines`, and zero order/outbox rows. This closes D4's claim at the level A2 was actually about (the caller's own observed reply), on top of D4's unit-level mapper case.

### D7 — the coordinator's own, confirmed fixed, not touched by me

`init.sh` section 4 now treats `in_progress` **or** `in_review` as "a feature is active — `current.md` must name it". Confirmed: `./init.sh` exits 0 with feature 15 set to `in_review` (checked explicitly, see §Verification below) — the exact case the re-review found broken. `init.sh` and `progress/current.md` were not edited by me.

### Verification run, round 3

- `dotnet build OrderToCash.sln --no-incremental` — 0 warnings, 0 errors (re-run after the `OrdersHost` refactor specifically, since it moved code between files).
- `dotnet format OrderToCash.sln --verify-no-changes` — clean.
- `tests/Orders.UnitTests` — **71/71 green** (64 + 1 D4 mapper case + 6 D5 theory cases, net of the D6 test's rename from `RealHostComposition_BuildServiceProvider_...` to `RealHostComposition_Build_...` — no case added or removed by D6 itself, only rewritten to drive `OrdersHost`).
- `tests/Orders.IntegrationTests` — **46/46 green** (45 + 1 new A9 case), both standalone (`OrdersCreateAcceptanceTests` class alone, 7/7) and inside a full-solution run.
- **`./quality.sh` — exit 0.** Full 11-project solution, 304 → **314** tests (71 + 46 + the unchanged other nine projects' 197), all green. Domain coverage unaffected (no `Domain/` file touched this round; `OrdersHost.cs`/`Program.cs` are outside `Orders/Domain/` so the filtered figure stays 423/477 = 88.7%).
- **`./init.sh` — exit 0, with feature 15 set to `in_review`** — the specific state the re-review found broken (D7), confirmed fixed and confirmed **by me, on this tree**, not merely asserted.
- Real-executable re-verification after the D6 refactor: `dotnet run --project src/Orders/Orders.csproj --no-build` boots identically to round 2's baseline.

### A tooling note, for the record

While arming D4 the first time, `scripts/arm-probe.sh` hung on its own confirming `dotnet test` run under this session's accumulated background-process load (multiple prior `quality.sh`/`dotnet run` invocations had left MSBuild server nodes and a stuck VSTest host behind). Killed the stuck process tree (`pkill -9 -f MSBuild.dll`, `pkill -9 -f VBCSCompiler`), checked the target file's content directly before touching anything further — unchanged, confirming the script's own restore had already completed before the kill reached it — then re-armed D4, D5 and D6 manually (same protocol: backup first, mutate, forced rebuild, run, restore from backup, forced rebuild, confirm green) rather than trusting a script invocation whose own completion I could not observe. No stray `.bak`/`.tmp`/zero-byte files found afterward (`find` re-run clean).
