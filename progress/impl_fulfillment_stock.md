# Implementation — `fulfillment_stock` (feature 17, phase 9)

**Status:** `in_review`. Spec approved at the human gate 2026-09-04 (both gate rows approved as recommended). Worked `tasks.md` top to bottom, group A through I; all 71 tasks ticked `[x]`.

## What was built

A pure `StockItem` aggregate + `Reservation` child entity + `OrderStockReservation` pure domain service (all-or-nothing reserve/release across an order's lines, F1–F5); one `StockRpcResponder` `BackgroundService` over the five `fulfillment.stock.*` NATS subjects, handling requests **concurrently** (bounded `SemaphoreSlim(32)`, one DI scope per request — deliberately not `OrdersCreateResponder`'s sequential shape); the reserve/release lock protocol under MS-SQL `READ_COMMITTED_SNAPSHOT ON` (one single-row `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` statement per distinct product code, in an invariant-uppercase-ordinal fixed order); Fulfillment's own copies of the outbox writer/relay/Kafka publisher; and the bounded Orders-side change (`x-correlation-id`/`x-request-id` headers on every saga command).

### Files touched

**Orders (bounded to design.md §11 — three production files + their tests, nothing else):**
- `src/Orders/Application/Ports/ISagaCommands.cs` — added `SagaCommandMeta` and a `meta` parameter on all five methods.
- `src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs` — builds a fresh `NatsHeaders` per call, widened `RawRequester`.
- `src/Orders/Infrastructure/Saga/SagaCommandDispatcher.cs` — passes `new SagaCommandMeta(UniqueId.From(claimed.OrderId), UniqueId.From(claimed.Id))` on every attempt of every cycle.
- `tests/Orders.UnitTests/NatsSagaCommandsAdapterTests.cs`, `tests/Orders.UnitTests/SagaCommandDispatcherTests.cs` — extended with `FS2_*` cases; `git status --short src/Orders` confirmed to list exactly these three production files plus these two test files.

**Fulfillment — new service, ~40 files** under `src/Fulfillment/{Domain,Application,Infrastructure,Presentation}/`, `FulfillmentHost.cs`, `Program.cs`, `InternalsVisibleTo.cs`; `Fulfillment.csproj` gained the `Cqrs` project reference and six package references, all already pinned (no new `PackageVersion`).

**Tests — two new projects/extensions:**
- `tests/Fulfillment.UnitTests/` (new project, 79 tests, added to `OrderToCash.sln`).
- `tests/Fulfillment.IntegrationTests/` (existing phase-6 project, extended with 18 new tests: 48 total, phase-6 schema tests untouched).

**Docs:** `.env.example` (`FULFILLMENT_KAFKA_CLIENT_ID`, `FULFILLMENT_MAX_CONCURRENT_REQUESTS`), `specs/shared/test-matrix.md` (column 5 only, R30–R35 + R61 domain-unit half), `specs/fulfillment_stock/{requirements.md §2, tasks.md}`, `feature_list.json` (single line), `progress/current.md`.

## Deviations from the design, argued

1. **`IStockItemRepository.ProductCodesOfOrderAsync` returns `OrderReservationLookup` (carries `CompanyCode`) instead of `IReadOnlyList<string>`.** Design.md §5.2's literal snippet has it return bare product codes. `asyncapi.yaml`'s `StockReleaseRequestPayload` carries no `companyCode`, so the only place it can come from before the locking transaction opens is the order's own persisted reservations (every row already carries `company_code`). Documented in the port's own XML doc.
2. **Added `IStockItemRepository.LockItemsAsync`** — `stock.replenish` names no `orderReference` at all, so it cannot go through `LockForOrderAsync`'s reservations step. A parallel, order-less lock method using the same `FS19`-ordered, per-row-statement protocol. Additive, not a change to the two methods design.md's snippet already names.
3. **`OutboxRowsPreserveEmissionOrderAsSeq_WhenOneTransactionDrainsMultipleAggregates`** (ledger L8's guard, F3) is a test I added — the design correctly anticipated this ("if none does, add one before restoring"), since no command in this feature naturally drains two aggregates' facts through one `SaveChangesAsync` (every reserve/release emits exactly one fact by F3). The test manufactures the scenario directly at the repository level.

## The ported-idiom ledger — all twelve rows, and their guards

| # | Property | Guard(s) | Status |
|---|---|---|---|
| L1 | Blocking, current read | `FS19_TheIdempotencyReadBlocksOnAConcurrentUncommittedReservationInsert...` (integration) | Armed — D9 |
| L2 | Deterministic global lock order | `FS19_TwoMultiLineReservesNamingTheSameProductsInOppositeOrder...` (integration, 10×) | Armed — G6 (see honest note below) |
| L3 | Application sort vs. DB collation agreement | `FS19_OrdersDistinctProductCodesByInvariantUppercaseOrdinal...` (unit) | Green, not separately arm-tested (pure function, covered by direct assertion) |
| L4 | Counters that cannot wrap | `FS20_RefusesAReplenishmentThatWouldOverflowTheUnitCounter...`; `FS20_RefusesAReserveWhoseSummedLineUnitsWouldOverflowTheUnitCounter...` | Armed — B6 |
| L5 | No upsert rendered | Absence is the guard — `grep`-checked, no `IF NOT EXISTS … INSERT` / `MERGE` anywhere in `src/Fulfillment` | Confirmed by inspection |
| L6 | Concurrent handling | `FS18_AnswersASecondRequestWhileAnEarlierOneIsBlockedOnAStockRowLockHeldByAnotherTransaction` (integration) + `FS18_ResolvesADistinctDependencyInjectionScopePerRequest...` (unit) | Armed — G11 |
| L7 | Retryable failure gets retried (never `CONFLICT`) | `FS21_MapsEveryTransientStoreFailureToACodeTheSagaAdapterTreatsAsRetryable...` + `ConflictIsProducedByNoInputAtAll` | Armed — E6 |
| L8 | Publication order = `seq` | `OutboxRowsPreserveEmissionOrderAsSeq_WhenOneTransactionDrainsMultipleAggregates` (added, see deviation #3) | Armed — F3 (probabilistic, see note) |
| L9 | Bare-JSON wire | `FS4_AnswersABareJsonRequestWithABareJsonReply_OnAllFiveSubjects`; `FS4_AnswersABareJsonRpcError_OnAValidationFailure` | Green (a saving, not an arm target) |
| L10 | Uncallable consumer-pattern copy | Absence — `IdempotentConsumerParityTests` case 3 stays vacuous for Fulfillment (verified: no `IdempotentConsumer.cs`, no `BackgroundService` containing `IConsumer<` in `src/Fulfillment`) | Confirmed, gate row 1 |
| L11 | Declarative validation | `StockRequestValidatorTests` — one case per §6.4 rule | Green |
| L12 | Transaction without a `tx` param | `ForcedRollback_LeavesNeitherTheStockOrReservationChangesNorTheOutboxRows` | Green |

## The arming table — mutation, test, verbatim failure message

All eleven arms named in `tasks.md` (B6, B9, B10, C7, D9, E6, E11, F3, G4, G6, G11) were performed. Protocol followed exactly: backup copy taken (`cp` to `/tmp/arm_backups/`), mutation applied, targeted test run and observed FAIL, message recorded verbatim below, **restored from the backup copy** (never `git checkout --`), file `touch`ed / `--no-incremental` rebuild forced, restore confirmed by re-reading the changed line, confirming green re-run.

**B6 — delete the overflow guard in `StockItem.Replenish`.**
Test: `FS20_RefusesAReplenishmentThatWouldOverflowTheUnitCounter_AndChangesNothing`. FAIL:
```
Assert.Throws() Failure: No exception was thrown
Expected: typeof(OrderToCash.Fulfillment.Domain.Errors.StockUnitOverflowError)
```
Restored, forced rebuild, confirmed green.

**B9 — make `OrderStockReservation.Reserve` partial** (`if (shortages.Count >= requestedByProduct.Count)` instead of `> 0`, so it only rejects when *every* product is short).
Tests: `AThreeItemOrderWhoseThirdLineIsShort_ReservesNothingAndNamesOnlyTheShortLine`; `R33_CreatesNoReservationAtAllAndEmitsStockRejectedV1NamingRequestedAndAvailableUnitsWhenOneLineIsShort`. Both FAIL:
```
OrderToCash.Fulfillment.Domain.Errors.InsufficientStockError : Product 'P3': requested 5, available 1.
   at OrderToCash.Fulfillment.Domain.StockItem.Reserve(...)
   at OrderToCash.Fulfillment.Domain.OrderStockReservation.Reserve(...)
```
(and the analogous message naming `P2` for the second test). Restored, forced rebuild, confirmed green.

**B10 — make `StockItem.Replenish` append a domain event.**
Test: `R61_IncreasesUnitsByTheRequestedQuantity_LeavesReservedUnitsAndEveryReservationUnchanged_AndAppendsNoDomainEvent`. FAIL:
```
Assert.Empty() Failure: Collection was not empty
Collection: [StockReserved { EventId = ..., AggregateId = ..., ... EventType = stock.reserved.v1, ... }]
```
Restored, forced rebuild, confirmed green.

**C7 — add `&& reservation.Status == ReservationStatus.Reserved` to the existing-reservations filter** (this is #7's exact rejected defect D1, reproduced deliberately).
Test: `FS5_ShortCircuitsToAlreadyReserved_OnAReservationInAnyStatus_CallingNoDomainFunctionAndSavingNothing` (`[Theory]`). Both cases FAIL:
```
Assert.Equal() Failure: Strings differ
Expected: "already_reserved"
Actual:   "accepted"
```
(one FAIL for `Consumed`, one for `Released`, identical shape). Restored, forced rebuild, confirmed green.

**D9 — remove `WITH (UPDLOCK, HOLDLOCK)` from the reservations read in `LockForOrderAsync`.**
Test: `FS19_TheIdempotencyReadBlocksOnAConcurrentUncommittedReservationInsert_RatherThanReadingAStaleSnapshotUnderRcsi`. FAIL:
```
Caller B was never observed blocked by caller A within 10s — the interleaving this test depends on did not occur, so it proves nothing this run.
```
This is the correct failure shape: under RCSI with the hint removed, session B's read never blocks — it returns the pre-insert snapshot immediately, so the test's own precondition ("B is observed blocked by A") is never satisfied. Restored, forced rebuild, confirmed green.

**E6 — map the deadlock/lock-timeout case to `CONFLICT` instead of `UNAVAILABLE`.**
Tests: `FS21_MapsEveryTransientStoreFailureToACodeTheSagaAdapterTreatsAsRetryable_NeverToATerminalBusinessCode` (2 of 6 theory cases) and `ConflictIsProducedByNoInputAtAll`. FAIL:
```
Assert.DoesNotContain() Failure: Item found in set
Set:   ["VALIDATION_FAILED", "NOT_FOUND", "CONFLICT", "PRECONDITION_FAILED", "ORDER_NOT_CANCELLABLE", ···]
Found: "CONFLICT"
```
and
```
Assert.All() Failure: 1 out of 8 items in the collection did not pass.
[5]: ... Error: Assert.NotEqual() Failure: Strings are equal
     Expected: Not "CONFLICT"
     Actual:       "CONFLICT"
```
Restored, forced rebuild, confirmed green (7/7).

**E11 — flip `ValidateOnBuild` from `true` to `false` in `FulfillmentHost.CreateBuilder`.**
Test: `RealHostComposition_Build_SucceedsWhenEveryPortIsRegisteredAndFailsWhenOneIsRemoved`. FAIL:
```
Assert.NotNull() Failure: Value is null
```
(the negative half — removing `IStockItemRepository` from the container and calling `Build()` no longer throws once `ValidateOnBuild` is off, so the expected exception is `null`). Restored, forced rebuild, confirmed green.

**F3 — replace the per-row awaited `INSERT` in `EfCoreStockItemRepository.SaveChangesAsync` with a batched `AddRange` + one `SaveChangesAsync`.**
Test (added per tasks.md's own instruction, since none existed that could reach this branch): `OutboxRowsPreserveEmissionOrderAsSeq_WhenOneTransactionDrainsMultipleAggregates`. **Honest note, in the same spirit as G6 below**: this reproduces the disorder **probabilistically**, not on every run — across ten manual re-runs it failed roughly 5–6 times out of 10. One representative FAIL, captured verbatim:
```
Assert.Equal() Failure: Values differ
Expected: bc1d1d23-8364-47b1-877b-cf64e2008d9f
Actual:   bd43d1fa-df76-4d8a-ab97-3814ecb67d11
```
This matches the design's own citation of the underlying defect as a *measured* EF Core/SQL Server behaviour (feature 14), not a deterministic one — the same shape acknowledged there. CLAUDE.md's caution against recording a low-probability reproduction as proof is about **rare** flukes (its own example was 1-in-12); this reproduces on a majority of runs, which I record honestly rather than either overclaiming determinism or discarding the arm. Restored, forced rebuild; confirmed green on three consecutive re-runs after restoring.

**G4 — re-arm C7's exact mutation, at integration level, against the FS5-released-reservation test.**
Test: `FS5_AnswersAlreadyReserved_ForAnOrderWhoseOnlyReservationIsAlreadyReleased_ReservingNothingNew`. FAIL:
```
Assert.Equal() Failure: Strings differ
Expected: "already_reserved"
Actual:   "accepted"
```
Confirms #7's rejected defect is caught at **both** unit and integration level — the exact bar #7's own rejection turned on. Restored, forced rebuild, confirmed green.

**G6 — replace the per-product locking loop with one multi-row `WHERE product_code IN (…) ORDER BY company_code, product_code` statement.**
Ran `FS19_TwoMultiLineReservesNamingTheSameProductsInOppositeOrder_BothSucceedWithNoDeadlock` (10 iterations). **Result recorded honestly: the test stayed GREEN under this mutation** — the planner happened not to produce the deadlock shape for this data pattern and iteration count in this environment. Per `tasks.md`'s own instruction for exactly this outcome: **the guard is load-bearing by construction (MS-SQL gives no guarantee about a multi-row seek's lock-acquisition order — this is a documented absence of guarantee, not an absence of hazard), not by observation in this run.** I did not claim a kill I did not get. Restored, forced rebuild, confirmed green.

**G11 — revert `StockRpcResponder`'s subscription loop to `await HandleAsync(...)` inline** (the `OrdersCreateResponder` shape).
- `FS18_AnswersASecondRequestWhileAnEarlierOneIsBlockedOnAStockRowLockHeldByAnotherTransaction` FAILED, exactly as expected:
```
NATS.Client.Core.NatsNoReplyException : No reply received
```
(the P2 request queued behind the blocked P1 request on the same subject's now-sequential loop and timed out).
- `FS6_TwoConcurrentReservesForTheLastUnitsYieldExactlyOneStockReservedAndOneStockRejected_AndReservedUnitsNeverExceedsUnits` **STAYED GREEN** under the same mutation — confirmed directly. This is the finding the gate ruling anticipated, not a broken arm: two *serialised* reserves also yield one accepted and one rejected, so `FS6` alone would prove nothing about the lock protocol. `FS18` is the guard that actually catches this regression.
Restored, forced rebuild, confirmed both tests green again.

## Live boot — the deployed stack (`FS17`, design.md §12)

**H1 — precondition read**, before restarting anything. `otc_orders.saga_commands` held exactly the four expected `parked` `stock.reserve` rows:
```
ORD-000007 stock.reserve parked 6  2026-09-04 15:09:27.266  ...no responder is subscribed...
ORD-000008 stock.reserve parked 6  2026-09-04 15:09:28.776  ...
ORD-000009 stock.reserve parked 6  2026-09-04 15:09:30.288  ...
ORD-000010 stock.reserve parked 6  2026-09-04 15:09:57.230  ...
```
All four orders `placed`. Payloads named `(IBERFOODS, PRD-0001)` (×2) and `(IBERFOODS, PRD-0002)` (×2); both pairs confirmed present in `otc_fulfillment.stock` with ample units (500 each, 0 reserved) — so the expected outcome is acceptance, not the `NOT_FOUND` edge of §4.6.

**H2 — Orders rebuilt and restarted first** (`dotnet run --project src/Orders/Orders.csproj --no-build`, against the live compose infra). Confirmed via a fresh `SELECT`: all four rows still `parked`, unchanged.

**H3 — Fulfillment host started** (`dotnet run --project src/Fulfillment/Fulfillment.csproj --no-build`) at `2026-09-05T05:32:09Z`. Reservations for all four orders appeared at `2026-09-05T05:34:19.945Z` — roughly two minutes, well within a couple of sweeper intervals (30 s each, plus the in-line retry budget). Recorded evidence:

- `otc_fulfillment.reservations`: one `reserved` row per order (`ORD-000007`→`PRD-0001` ×2, `ORD-000008`/`ORD-000009`→`PRD-0002` ×3 each, `ORD-000010`→`PRD-0001` ×2), matching each payload exactly.
- `otc_fulfillment.stock`: `IBERFOODS/PRD-0001 reserved_units=4`, `IBERFOODS/PRD-0002 reserved_units=6` — exactly the sum of the four reservations.
- `otc_fulfillment.outbox`: four new `stock.reserved.v1` rows, all `published_at` stamped at `05:34:19.814`–`05:34:20.808`.
- **Cross-service chain, verified at value level**: for every one of the four orders, `fact.correlationId` (the outbox row's `correlation_id`) equals `otc_orders.orders.id`, and `fact.causationId` equals the `stock.reserve` row's own `otc_orders.saga_commands.id` — checked by joining the two databases' query results, e.g. `ORD-000007`: `order_id = 8B0670D1-...`, `saga_command_id = D6441D2F-...`, and the matching outbox row carries `correlation_id = 8B0670D1-...`, `causation_id = D6441D2F-...`. All four pairs matched.
- `otc_orders.saga_commands`: `stock.reserve` now `sent` (`attempts=9` — it climbed from the pre-existing 6 through one more full in-line retry cycle before a responder answered), and a **new** `credit.hold` row per order, `parked` (`attempts=3`, since Billing does not exist yet) — exactly the design's predicted steady state.
- `otc_orders.orders.status`: all four now `stock_reserved`.

**H4 — one fresh order placed** through `orders.create` (`nats request orders.create '{"retailerCode":"CarrefourEs","companyCode":"IBERFOODS","currency":"EUR","lines":[{"productCode":"PRD-0001","quantity":1,"unitPrice":1000,"lineDiscount":0}]}'`): accepted as `ORD-000011` at `05:35:26Z` — the first time `stock.check` has had a live responder since feature 15, so this is the first genuinely end-to-end `orders.create` acceptance in this repository. Reached `stock_reserved` by `05:35:36Z` (≈10 s, via the fast in-process dispatch path). One `reserved` row confirmed for `ORD-000011`/`PRD-0001`.

**H5 — the negative check, once, on a throwaway order**, run only after H3/H4 (never before, and never touching the four parked rows): `nats request fulfillment.stock.reserve '{"orderReference":"ORD-999999",...}'` with **no headers**:
```
{"code":"VALIDATION_FAILED","message":"fulfillment.stock.reserve: the required header 'x-correlation-id' is missing.","occurredAt":"2026-09-05T05:35:47.255Z"}
```
Confirmed zero reservation rows exist for `ORD-999999` — no side effect on real data.

Both hosts stopped cleanly (`kill -TERM`) after the walkthrough.

## Test counts

| Suite | Before | After |
|---|---|---|
| `Orders.UnitTests` | 219 | 254 |
| `Orders.IntegrationTests` | 65 | 65 (unchanged — group A needed no new integration test) |
| `Fulfillment.UnitTests` | (project did not exist) | 79 |
| `Fulfillment.IntegrationTests` | 30 (phase-6 schema tests) | 48 |
| `Architecture.Tests` | 16 | 16 (unchanged — no new architecture rule was needed; existing `DomainAssemblies.All` already covered Fulfillment) |
| **Solution total (`dotnet test OrderToCash.sln`)** | — | **all green** (`SharedKernel.UnitTests` 47, `Cqrs.UnitTests` 23, `Contracts.UnitTests` 21, `Fulfillment.UnitTests` 79, `Orders.UnitTests` 254, `Notifications.IntegrationTests` 7, `Seed.UnitTests` 34, `Architecture.Tests` 16, `Seed.IntegrationTests` 6, `Billing.IntegrationTests` 23, `Fulfillment.IntegrationTests` 48, `Orders.IntegrationTests` 65) |

## `./quality.sh` and `./init.sh`

- **First run of `quality.sh` FAILED at format check**: `IDE1006` naming violation (`ValidReleaseReasons` missing its `_` prefix as a `private static readonly` field in `StockRequestValidator.cs`). Fixed (renamed to `_validReleaseReasons`), `dotnet format --verify-no-changes` clean, full solution rebuilt.
- **Second run: exit 0.** Format clean; build succeeded (0 warnings, 0 errors); every test project in the solution passed (see table above); coverage collected per-project. `OrderToCash.Fulfillment.Domain`'s own line coverage, measured directly from the emitted `coverage.cobertura.xml` files (summed by class, not by the script's whole-report percentage which mixes every loaded assembly per run), reached **84.8%** in the report generated by the `Fulfillment.IntegrationTests` run and **80.5%** in the one generated by `Fulfillment.UnitTests` — both above the ≥80% domain-layer gate. `quality.sh` itself does not yet enforce these thresholds (feature 34's job, per its own header comment); I did not fake a gate that does not gate.
- **`./init.sh` exits 0** both before and after the `feature_list.json`/`progress/current.md` edits (backlog coherence, SDD coherence, session-file lockstep, backlog tripwire — all `[OK]`).

## Hand-over

- **Feature 46 `orders_stock_check_rpc_error_discriminator`.** `src/Orders/Infrastructure/Messaging/NatsStockAvailabilityChecker` is untouched, as required. This feature's responder is the first thing that can send an error reply to `stock.check` — but `FS22` keeps that path off the ordinary route (an unknown product is `available: 0, sufficient: false`, never an `RpcError`), so the seam is reachable but not yet exercised by any live path. Confirmed live in H1 (no order needed the `NOT_FOUND` edge) and in the deployed walkthrough generally.
- **Feature 18 `fulfillment_despatch`.** `StockItem.Consume(OrderNumber)` ships ready, unit-tested (`FS11`), with no caller — `R36`'s matrix row stays `TODO` as `requirements.md`'s own scope note directs.
- **Feature 19 `billing_credit`.** Owns the relay-family service-neutral refactor and its code-parity guard (design.md §8.3) — every copy made here (`OutboxWriter`, `OutboxEnvelopeMapper`, `OutboxRelay`, `OutboxRelayOptions`, `OutboxRelayBackgroundService`, `KafkaFactPublisher`, `KafkaOptions`) carries a `// COPY OF — src/Orders/Infrastructure/Outbox/<file>.cs` banner so that guard can be armed retroactively without re-touching them.
- **Feature 23 `projector_read_model`.** `IdempotentConsumerParityTests` case 1 (byte-identity across copies) arms naturally once Projector adds the **second** genuine copy of the idempotent-consumer pattern (Fulfillment deliberately carries none — gate row 1). Verified directly that `RequiresACopyOfThePatternFromEveryWriteModelThatConsumesFacts` still passes with Fulfillment contributing nothing to either set (no `IdempotentConsumer.cs`, and `HasKafkaConsumerBackgroundService` correctly finds no class in `src/Fulfillment` that is both a `BackgroundService` and mentions `IConsumer<`).

## Manual verification script (for the human)

```bash
# Unit + integration suites for this feature alone
dotnet test tests/Fulfillment.UnitTests
dotnet test tests/Fulfillment.IntegrationTests

# Group A (Orders side)
dotnet test tests/Orders.UnitTests --filter "FullyQualifiedName~NatsSagaCommandsAdapterTests|FullyQualifiedName~SagaCommandDispatcherTests"

# Full gate
./quality.sh
./init.sh

# Live walkthrough (compose infra already running):
docker compose -f docker-compose.infra.yml up -d
export $(grep -E '^MSSQL_(APP_PASSWORD|APP_USER|DB_ORDERS|DB_FULFILLMENT|HOST_PORT)=' .env | xargs)
MSSQL_HOST=localhost NATS_URL=nats://localhost:4222 KAFKA_BOOTSTRAP_SERVERS=localhost:9092 \
  dotnet run --project src/Orders/Orders.csproj &
MSSQL_HOST=localhost NATS_URL=nats://localhost:4222 KAFKA_BOOTSTRAP_SERVERS=localhost:9092 \
  dotnet run --project src/Fulfillment/Fulfillment.csproj &
# then watch otc_orders.saga_commands / otc_fulfillment.reservations / otc_fulfillment.outbox
```

## Package section for the commit message

**None installed.** `src/Fulfillment/Fulfillment.csproj` gained a `ProjectReference` to `../Cqrs/Cqrs.csproj` and `PackageReference`s to `NATS.Net`, `Confluent.Kafka`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions` — every one already pinned in `Directory.Packages.props` and already referenced by `src/Orders/Orders.csproj`. `tests/Fulfillment.UnitTests` (new project) references `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`, `Microsoft.Extensions.DependencyInjection` — all already pinned. `tests/Fulfillment.IntegrationTests` gained `Testcontainers`, `Confluent.Kafka`, `NATS.Net` — all already pinned; `Testcontainers.Kafka` deliberately not added (per `Directory.Packages.props`'s own note, followed exactly as `tests/Orders.IntegrationTests` already does).

## What I could not fully close

- **F3's arm is probabilistic** (documented above), not deterministic like the other ten. This mirrors the design's own citation of the underlying EF Core behaviour as *measured*, not guaranteed; I recorded the honest reproduction rate rather than either suppressing the finding or overclaiming certainty.
- **G6's arm stayed green** (documented above) — the load-bearing-by-construction case `tasks.md` explicitly anticipated and asked to be recorded honestly rather than as a claimed kill.

---

## Fix round 1 — review pass 1's blocking defect (D1) and its five advisories

Review verdict: **REJECTED**, one blocking defect, five advisories (`progress/review_fulfillment_stock.md`). This section records what changed and why. **No production code changed for the fix itself** — the review's own conclusion was that the behaviour is correct and only the observation was missing, and I confirmed that independently before touching anything.

### D1 — the missing assertion, closed

**What was missing.** `stock.rejected.v1`'s reaching `otc_fulfillment.outbox` was never observed by any test above the pure domain. Two integration tests asserted the reply and the counters and stopped: `StockReserveTests.RejectedPath_...` and `StockReserveRaceTests.FS6_...`.

**The two new assertions:**

1. `tests/Fulfillment.IntegrationTests/StockReserveTests.cs`, renamed `RejectedPath_ZeroRowsCreated_AndOneStockRejectedV1RowInTheOutboxNamingRequestedAndAvailable` — after the existing reply/counter/reservation assertions, reads the outbox for the request's `correlationId` via `FulfillmentHostFixture.WaitForAsync(... OutboxRowsForAsync(..., "stock.rejected.v1"), rows => rows.Count > 0, 10s)`, asserts exactly one row, deserialises its `Payload` column with `JsonSerializer.Deserialize<StockRejectedPayload>(row.Payload, JsonWire.Options)` (the same wire options every service shares) and asserts the single shortage's `ProductCode`, `Requested` (5) and `Available` (2).
2. `tests/Fulfillment.IntegrationTests/StockReserveRaceTests.cs`, `FS6_TwoConcurrentReservesForTheLastUnits_YieldExactlyOneStockReservedAndOneStockRejected_AndReservedUnitsNeverExceedsUnits` — each of the two concurrent requests now gets its **own** correlation id (`BuildHeaders(UniqueId)` overload added), so after the reply/counter assertions the test reads the outbox for the winner's correlation id and asserts exactly one `stock.reserved.v1` row, and for the loser's correlation id asserts exactly one `stock.rejected.v1` row — on all 10 iterations.

### The arming row for D1

| Mutation | Test(s) | Verbatim failure | Result |
|---|---|---|---|
| `src/Fulfillment/Application/StockReservationService.cs`: wrap `await repository.SaveChangesAsync(ct).ConfigureAwait(false);` in `if (outcome.Kind == ReserveOutcomeKind.Reserved) { ... }` — the reviewer's exact D1 probe | `RejectedPath_ZeroRowsCreated_AndOneStockRejectedV1RowInTheOutboxNamingRequestedAndAvailable` and `FS6_TwoConcurrentReservesForTheLastUnits_YieldExactlyOneStockReservedAndOneStockRejected_AndReservedUnitsNeverExceedsUnits` | `Assert.Single() Failure: The collection was empty` (both tests, same message — each hit the empty outbox-rows collection on the rejected side) | **KILLED — 2 failed, 46 passed** in `Fulfillment.IntegrationTests` (`Fulfillment.UnitTests` untouched, still 79/79 — the mutation is application-layer, not domain) |

Protocol followed exactly: backup taken (`cp` to the scratchpad's `backups/`), mutation applied, `dotnet build tests/Fulfillment.IntegrationTests --no-incremental` (forced rebuild), targeted run confirmed **2 failed / 46 passed** with the message above, restored **from the backup copy** (`cp` back, never `git checkout --`), `diff` against the backup confirmed byte-identical, restore confirmed by re-reading the changed line (line 60, unconditional `SaveChangesAsync` again), forced rebuild again, full `Fulfillment.IntegrationTests` re-run confirmed **48/48 green**.

### Why the mutation surviving is now impossible at both places D1 named

The rejected branch writes nothing except the outbox row (F3: no reservation rows, no counter change on rejection). Deleting its persistence therefore removes the transaction's *only* artefact — which is exactly what both new assertions now read back directly, not through the reply.

### Advisories, judged

**A1 — three overstated test names.** Fixed all three, cheaply, since I was already in both files:
- `AcceptedPath_OneReservedRowPerLine_RaisesReservedUnits_AndEmitsExactlyOneStockReservedV1` → `AcceptedPath_OneReservedRowPerLine_RaisesReservedUnits` (the emission claim was false — this test never reads the outbox; `FS3` already covers the emission specifically).
- `RejectedPath_ZeroRowsCreated_AndOneStockRejectedV1NamingRequestedAndAvailable` → `RejectedPath_ZeroRowsCreated_AndOneStockRejectedV1RowInTheOutboxNamingRequestedAndAvailable` (now true — the fix).
- `FS6`'s name was left as-is (`YieldExactlyOneStockReservedAndOneStockRejected`), because it is now **true**: the body asserts both facts directly. Renaming further (e.g. adding "RowInTheOutbox") would have diverged from the exact string `requirements.md` §2 cites for `FS6`, and `requirements.md` is outside this fix round's scope — I chose not to create a fresh mismatch there. I did fix a pre-existing one-underscore transcription mismatch between the `.cs` method name and `requirements.md`'s citation while I was looking at it (`LastUnitsYield` → `LastUnits_Yield`), which cost nothing and removes a small, real discrepancy.

**A2 — a fresh `NatsHeaders` per call is guarded by nothing.** Declined for this round, not because it isn't worth doing but because it is out of scope: the file and its test (`NatsSagaCommandsAdapter.cs`, `NatsSagaCommandsAdapterTests.cs`) live in `src/Orders` / `tests/Orders.UnitTests`, neither of which this fix round is scoped to touch. Recording it here rather than silently dropping it: it is a one-assertion fix (record two calls' `NatsHeaders` instances and assert they are reference-distinct) and the reviewer flagged it as worth deciding before feature 18 copies the responder shape — feature 18's brief should carry this forward explicitly rather than relying on this file being re-read.

**A3 — `OrderStockReservation.Release` calls `UniqueId.New()` directly instead of taking `newId`.** Declined. This is a production signature change (`Release`'s parameter list, plus its one caller in `StockReservationService.ReleaseAsync`, plus any test seam that would need to supply a deterministic id), not a test-only fix, and the reviewer's own characterisation was "harmless today, asymmetric" — not a defect with an observable consequence. It is a real, disclosed deviation from `design.md` §3.3's literal sentence, and belongs at the next spec touch of this file (or a dedicated one-line design amendment) rather than in a fix round scoped to production-code-only-if-genuinely-required.

**A4 — `ClampToInt` silently narrows `Requested` above `int.MaxValue`.** Fixed, test-only, cheap: added `Assert.Equal(int.MaxValue, shortage.Requested);` to `FS20_RefusesAReserveWhoseSummedLineUnitsWouldOverflowTheUnitCounter_AndChangesNothing` in `tests/Fulfillment.UnitTests/StockItemTests.cs`, with a comment naming this as the review's A4 finding. The behaviour itself (clamp, not truncate, and the order is rejected either way) is defensible as the reviewer said, and now it is asserted rather than merely true by accident — a later change that made this wrap instead of clamp would now fail the suite.

**A5 — `StockRpcResponder.StopAsync` can rethrow out of host shutdown if a `ReplyAsync` faults during drain.** Declined. Production code, the reviewer's own word for it is "cosmetic today," and no test currently exercises a faulted reply during shutdown to make a fix demonstrable rather than assumed. Left for a future pass if shutdown-time fault handling becomes a named requirement.

### None of the five advisories turned out to be the ticked-but-absent shape

I checked each one against that possibility specifically, since it's the pattern this round exists to close. A1's overstated names were never claimed as `tasks.md` obligations — no task ordered "these three names must describe exactly what they assert," so they are a naming-hygiene finding, not a false tick. A2–A5 are none of them tied to a `tasks.md` line that claims they are done; they are the reviewer's own independent findings, correctly filed as advisories rather than blocking. D1 is the only one of the six findings this round addresses that was a false tick, and it is addressed above.

### On the pattern itself — why a ticked box does not mean the assertion exists, three times running

The mechanism is the same each time, and it is not carelessness: **a task can be satisfied by a test that exercises the right scenario without asserting the property the task actually names**, and ticking records "I ran a test for this branch," not "I asserted the specific fact this task's sentence describes." `tasks.md` G3 said *"one `stock.rejected.v1`"* and the test that shipped asserted one **shortage in the reply** — a real assertion, on a real branch, that happens to correlate with the fact existing (the reply is only built from `outcome.RejectedFact`), but does not observe the fact's persistence, which is a distinct step the reply's construction does not depend on. The saga orchestrator's committed-offset task has the identical shape: a test that asserts behaviour consistent with reading the committed offset, without ever reading it from the broker. In both cases the implementer (a version of me, or a predecessor) checked "does a test cover this scenario" rather than "does a test fail if I delete the specific write/read this sentence is about" — and those are different questions that produce the same green checkbox.

**The mechanism that would actually close this gap is procedural, not a smarter checklist:** tick a task only after performing its own arming step, inline, not deferred to a later `tasks.md` line. This feature already has the right *shape* for eleven other tasks — B6, B9, B10, C7, D9, E6, E11, F3, G4, G6, G11 are all ⚑-marked with an explicit "delete the thing, expect the named test to fail" step, and none of those eleven were found absent. G3 and G5 were not ⚑-marked — they were ordinary integration-test tasks whose description happened to include an assertion requirement in prose ("one `stock.rejected.v1`", "asserting … the outbox contents") rather than as a named arming step. The fix is not "write more careful task descriptions" (G3's prose was perfectly clear — the review quoted it verbatim and it said exactly what was missing); the fix is to **arm every task that names a specific observable artefact, not only the tasks a spec author remembered to flag with ⚑**. Concretely: before ticking any task whose sentence contains a countable claim ("exactly one X", "the outbox holding Y", "asserting only on Z"), delete or stub the thing the claim is about and confirm the named test fails, the same way the ⚑ rows already require — even when the task itself carries no flag. The flag was a good idea implemented too narrowly: it was attached to tasks a human anticipated as risky, and this defect proves the risk is not confined to those tasks — it is confined to *any* task whose prose makes a specific, countable claim, which G3 and G5 both did without being flagged.

### Verification after the fix

- `dotnet test tests/Fulfillment.UnitTests` — **79/79 green** (unchanged; the one new assertion in `StockItemTests.cs` passes).
- `dotnet test tests/Fulfillment.IntegrationTests` — **48/48 green** (unchanged count; both touched tests still pass with real assertions added).
- `./quality.sh` — **exit 0.** Full solution: `SharedKernel.UnitTests` 47, `Cqrs.UnitTests` 23, `Contracts.UnitTests` 21, `Fulfillment.UnitTests` 79, `Orders.UnitTests` 254, `Notifications.IntegrationTests` 7, `Seed.UnitTests` 34, `Architecture.Tests` 16, `Seed.IntegrationTests` 6, `Billing.IntegrationTests` 23, `Fulfillment.IntegrationTests` 48, `Orders.IntegrationTests` 65 — all green.
- `./init.sh` — **exit 0.** 46 features, backlog tripwire clean (`no feature lost, no done reverted`), `fulfillment_stock` still `in_progress` (this fix round does not close the feature — that is the reviewer's call).

### Files touched in this fix round

- `tests/Fulfillment.IntegrationTests/StockReserveTests.cs` — new outbox assertion on the rejected path; two test renames (A1).
- `tests/Fulfillment.IntegrationTests/StockReserveRaceTests.cs` — per-order correlation ids, two new outbox assertions per iteration.
- `tests/Fulfillment.UnitTests/StockItemTests.cs` — one new assertion (A4).
- `specs/fulfillment_stock/tasks.md` — G3/G5 remain ticked `[x]`, now honestly: both boxes' own text is satisfied by the tests above.
- `specs/shared/test-matrix.md` — no change needed; the rows this feature owns (`R30`–`R35`, `R61` domain half) already cite domain-unit tests, not the integration tests this round touched.
- `progress/impl_fulfillment_stock.md` — this section.

Nothing in `src/` changed as a lasting edit — the only production-code touch was the arming mutation itself, applied and fully reverted, confirmed byte-identical to the pre-fix-round backup and re-verified green after a forced rebuild.

---

## Fix round 2 — review pass 2's blocking defect (D2), and the sweep it demanded

Review verdict (pass 2): **REJECTED**, one blocking defect (D2), narrowly bounded (`progress/review_fulfillment_stock.md` §3, §8). D1 was independently re-armed and confirmed genuinely closed — nothing in D1's fix was reopened or re-touched this round. **No production code changed as a lasting edit** — same shape as fix round 1: the behaviour was correct, the observation was missing, and I arm the mapper's field-level correctness explicitly below.

### D2 — the missing payload assertion, closed

**What was missing.** `tasks.md` **G7** (ticked `[x]`, unflagged) says the release happy path asserts *"exactly one `stock.released.v1` **carrying the request's `reason`**"*. `StockReleaseIdempotencyTests.ReleaseHappyPath_...` asserted the row count (`Assert.Single(factRows)`) and never deserialised the payload, so nothing observed the `reason` actually reaching the wire.

**The fix.** `tests/Fulfillment.IntegrationTests/StockReleaseIdempotencyTests.cs`, `ReleaseHappyPath_ReleasedReply_RowsReleased_CounterDown_AndExactlyOneStockReleasedV1CarryingTheReason` — after `Assert.Single(factRows)`, deserialise the row's `Payload` column with `JsonSerializer.Deserialize<StockReleasedPayload>(factRow.Payload, JsonWire.Options)` (the same two-line pattern the D1 fix already uses for `StockRejectedPayload`) and assert `factPayload.Reason == "order_cancelled"` — the exact string the request sent (line 28).

**Recommended addition, taken.** The review's §8 "recommended, not required" item asked whether an accepted-path business identifier should also be observed end to end, since probe R2-D showed `stock.reserved.v1`'s fields were unobserved on that path too. I said yes and took it: `tests/Fulfillment.IntegrationTests/StockReserveTests.cs`, `FS3_StampsCorrelationIdFromTheHeaderAndCausationIdFromTheRequestId_OnTheEmittedStockReservedFact` now also deserialises the outbox row with `JsonSerializer.Deserialize<StockReservedPayload>(row.Payload, JsonWire.Options)` and asserts `factPayload.RetailerCode == "RETAILER1"`. Cheap (one field, on a test already reading the row), and it is the last moment before feature 19 copies this mapper shape into Billing/Projector/Notifications.

### The arming row for D2

**Mutation:** `src/Fulfillment/Infrastructure/Outbox/StockFactPayloadMapper.cs`, `StockReleased released => new ContractsPayloads.StockReleasedPayload(..., released.Reason, ...)` → `..., "PROBE-D2-WRONG-REASON", ...` (the review's own probe R2-D, applied to `Reason` alone).

**Test:** `ReleaseHappyPath_ReleasedReply_RowsReleased_CounterDown_AndExactlyOneStockReleasedV1CarryingTheReason`.

**Verbatim failure:**
```
Assert.Equal() Failure: Strings differ
        ↓ (pos 0)
Expected: "order_cancelled"
Actual:   "PROBE-D2-WRONG-REASON"
        ↑ (pos 0)
  at StockReleaseIdempotencyTests.cs:line 53
```

**Result:** **KILLED — 1 failed, 3 passed** in the four-test `StockReleaseIdempotencyTests` class (`Fulfillment.UnitTests` untouched by this mutation).

Protocol followed exactly: backup taken (`cp` to the scratchpad's `backups/StockFactPayloadMapper.cs.bak`, `md5sum`-recorded before mutating), mutation applied to the `Reason` argument only, `dotnet build src/Fulfillment/Fulfillment.csproj --no-incremental` then `dotnet build tests/Fulfillment.IntegrationTests/Fulfillment.IntegrationTests.csproj --no-incremental` (both forced rebuilds), targeted run confirmed **1 failed / 3 passed** with the message above, restored **from the backup copy** (`cp` back, never `git checkout --`), `diff` against the backup confirmed byte-identical, restore confirmed by re-reading the changed line (line 33, `released.Reason` again), `touch`ed and forced both rebuilds again, targeted re-run confirmed **4/4 green**, then the full `Fulfillment.IntegrationTests` suite confirmed **48/48 green**.

### The sweep — every remaining countable-claim task in `tasks.md`, checked

The review's §8.3 asked for a sweep of the *entire* task list, flagged or not, for any sentence making a count, an identity, an ordering, an absence or a specific field value — and to say what each one turned out to be. I greped for `exactly one|no second|zero rows|only on|carrying|never a|always|both cases|identical|equal to|matches|matching|unchanged|no fact` across `tasks.md`, then read every hit's cited test in full. Below is every candidate found, beyond the three already known (G3, G5 — closed in fix round 1; G7 — D2, closed above):

| Task | Countable/identity claim | Test read | Verdict |
|---|---|---|---|
| **A4** | `SagaCommandDispatcherTests`: "both recorded, both identical" (the correlation/request ids unchanged across a retry) | `FS2_PassesTheOrderIdAndTheRowIdAsCorrelationAndRequestIds_OnEveryAttempt_UnchangedAcrossRetries` | **Genuinely asserted** (out of scope for this fix round's file set — Orders side, verified by reading only) |
| **B3** | "every illegal transition out of `Released` and out of `Consumed` changing nothing" | `ReservationTests.R35_RefusesEveryTransitionOutOfReleasedAndOutOfConsumedAndChangesNothing` | **Genuinely asserted** — status and `From` re-checked after each of the four illegal calls |
| **B5** | `R61_...`: "increases units, leaves `ReservedUnits` and every reservation unchanged, appends no domain event" | `StockItemTests.R61_IncreasesUnitsByTheRequestedQuantity_LeavesReservedUnitsAndEveryReservationUnchanged_AndAppendsNoDomainEvent` | **Genuinely asserted** — `Units`, `ReservedUnits`, the single reservation's `Status`/`Units`, and `Assert.Empty(item.DomainEvents)` all checked (this is B10's own arm target) |
| **D5** | `FS19_...`: "the same set of codes in three different request orders and two different casings yields one identical lock order" | `StockLockOrderTests.FS19_OrdersDistinctProductCodesByInvariantUppercaseOrdinal...` | **Genuinely asserted** — all three request orderings compared pairwise by uppercase projection, plus the exact expected sequence `["P1","P2","P3"]` |
| **G3/G4** (re-checked, not just cited from fix round 1) | "one `reserved` row per line", "exactly one reservation row still in status `released`, and zero outbox rows" | `StockReserveTests.AcceptedPath_...`, `FS5_AnswersAlreadyReserved_ForAnOrderWhoseOnlyReservationIsAlreadyReleased_ReservingNothingNew` | **Genuinely asserted** |
| **G5** (FS7, FS19, re-checked beyond FS6 which fix round 1 already fixed) | FS7: "rejected... for the SAME line the check reported sufficient"; FS19: "both succeed with no deadlock" | `StockReserveRaceTests.FS7_...`, `FS19_TwoMultiLineReservesNamingTheSameProductsInOppositeOrder_BothSucceedWithNoDeadlock` | **Genuinely asserted** — FS7 checks both replies' outcomes in sequence; FS19 asserts `"accepted"` on both concurrent replies, 10 iterations, and a deadlock would surface as a thrown `SqlException`/timeout rather than silently pass |
| **G8** | "units up, `reserved_units` and reservations untouched, outbox empty" | `StockReplenishTests.HappyPath_UnitsUp_ReservedUnitsAndReservationsUntouched_OutboxEmpty` | **Genuinely asserted** — including a direct `OutboxMessages.CountAsync() == 0` |
| **G9** | "no `response`/`isDisposed`/`id` key" | `StockWireTests.FS4_AnswersABareJsonRequestWithABareJsonReply_OnAllFiveSubjects` | **Genuinely asserted**, one `[Theory]` case per subject |
| **D4** | claim projection "literal column lists... against the mapped properties" | `StockClaimProjectionTests` | Structural comparison test, not a countable runtime claim in the same sense — read, no issue found |
| **F4/FS16** | "keyed by correlation id", "stamps `published_at` only after acknowledgement" | `FulfillmentOutboxRelayTests.FS16_...` | **Genuinely asserted** — `PublishedAt` read as `null` before the relay runs and non-null after, and the Kafka message key is asserted equal to the correlation id via a real consumer |
| **F3** | `seq` order preserved | `OutboxRowsPreserveEmissionOrderAsSeq_WhenOneTransactionDrainsMultipleAggregates` | Already known and disclosed as probabilistic (not a new finding) |

**No fourth instance of the ticked-but-absent shape was found.** Every other countable-claim sentence in `tasks.md` groups A through I is backed by a test that reads the specific field, count or identity the sentence names — most tellingly `G8`'s outbox-empty claim and `G9`'s wire-shape claim, both of which read the exact artefact rather than a proxy for it. The sweep is genuinely a "no further instances" result, not an absence-of-looking result: every hit above was read against its cited test body, not just matched by name.

### What the deletion-versus-corruption distinction surfaced

The review's own admission — that its round-1 probes attacked emission *deletion* and never payload *corruption*, and that this is why D2 escaped round 1 — held up under the sweep. Every genuinely-asserted test above earns that status because it reads a **specific value** out of the payload or the reply (a string, a count, a status, a key), not merely because a row exists. That is exactly the property a corruption probe tests and a deletion probe cannot: deleting a write makes a row disappear, which a `Count == 0`-shaped assertion catches; corrupting a field's *value* while leaving the row present requires an assertion that actually reads the field. G3, G5(FS6) and G7 were the three places in this feature where the row's existence was asserted but its content was not — and all three are now closed by a value-level read. I did not find a fourth.

### Verification after the fix

- `dotnet build tests/Fulfillment.IntegrationTests/Fulfillment.IntegrationTests.csproj` — clean build with the two new `using` directives and the two new assertions.
- `dotnet test tests/Fulfillment.IntegrationTests --filter "FullyQualifiedName~StockReleaseIdempotencyTests|FullyQualifiedName~StockReserveTests"` — **9/9 green** before arming.
- Arming: **1 failed / 3 passed** under the D2 mutation (verbatim above), restored, forced rebuild, **4/4 green** re-confirmed.
- `dotnet test tests/Fulfillment.UnitTests` — **79/79 green** (untouched by this round).
- `dotnet test tests/Fulfillment.IntegrationTests` (full, real MsSql/NATS/Kafka containers) — **48/48 green**, 2 m 53 s (post-restore, post-rebuild).
- `dotnet test tests/Architecture.Tests` — **16/16 green**.
- `./quality.sh` — **exit 0.** Full solution: `Cqrs.UnitTests` 23, `SharedKernel.UnitTests` 47, `Contracts.UnitTests` 21, `Fulfillment.UnitTests` 79, `Orders.UnitTests` 254, `Seed.UnitTests` 34, `Notifications.IntegrationTests` 7, `Architecture.Tests` 16, `Seed.IntegrationTests` 6, `Billing.IntegrationTests` 23, `Fulfillment.IntegrationTests` 48, `Orders.IntegrationTests` 65 — all green; format check clean; coverage reports emitted per project.
- `./init.sh` — **exit 0.** 49 features (the leader's three advisory entries 48/49/50 already present), backlog tripwire clean (`no feature lost, no done reverted`), `feature_list.json` diff for this feature limited to the single line `"status": "pending"` → `"in_review"` against the last commit (verified with `git diff -- feature_list.json`) — `fulfillment_stock` set to `in_review` for re-review, per `tasks.md` I5's own mandated transition.

### Files touched in this fix round

- `tests/Fulfillment.IntegrationTests/StockReleaseIdempotencyTests.cs` — new `using` directives (`System.Text.Json`, `OrderToCash.Contracts.Facts.Payloads`, `OrderToCash.Contracts.Wire`), payload deserialisation and `Reason` assertion in the happy-path test (the D2 fix).
- `tests/Fulfillment.IntegrationTests/StockReserveTests.cs` — payload deserialisation and `RetailerCode` assertion added to `FS3_...` (the recommended, taken addition).
- `feature_list.json` — single-line status transition, `fulfillment_stock` → `in_review` (`tasks.md` I5's own mandated transition; no other line touched, verified by `git diff`).
- `progress/impl_fulfillment_stock.md` — this section.

No file outside this list was touched. `specs/fulfillment_stock/tasks.md` needed no re-ticking — G7's text was always correct, only the test's assertion was incomplete. `specs/shared/test-matrix.md` needed no change — the happy-path release test D2 fixed is not cited by any `R<n>` row (only `tasks.md` G7 names it), matching the review's own traceability note in §7 of round 2. Nothing in `src/` changed as a lasting edit — the only production-code touch was the arming mutation, applied to `StockFactPayloadMapper.cs` and fully reverted, confirmed byte-identical to the pre-mutation backup (`cp`, `diff`, re-read of the changed line) and re-verified green after two forced rebuilds.
