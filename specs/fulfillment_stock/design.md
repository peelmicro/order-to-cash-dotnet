# `fulfillment_stock` — Design (.NET 10 / C# 14 / EF Core / MS-SQL, assessment #8)

> **Stack-specific.** This file is where the .NET, EF Core, MS-SQL, `NATS.Net`, `Confluent.Kafka`, `src/Cqrs` and Testcontainers detail lives. Nothing here belongs in `specs/shared/`; #7 wrote its own equivalent against the same `R30` – `R36`, `R61`, and #9 will write a third.
>
> **This is a port with a delta analysis.** #7's `specs/fulfillment_stock/design.md`, its gate record (`progress/spec_fulfillment_stock.md`, 18 open points, 7 ruled on by a human) and its review (`progress/review_fulfillment_stock.md`, **REJECTED** on defect D1) were read first. Everything stack-agnostic is ported as content; the effort went into §4, §6.2, §9, §11 and §15 — the places .NET and MS-SQL genuinely differ.
>
> Authorities: [`specs/shared/domain-model.md`](../shared/domain-model.md) §4 (`StockItem`, `Reservation`, **F1** – **F5**, the lifecycle §4.2), §7.1 (the envelope), §8 (cross-cutting rules — rule 6 is argued against **F3** in §4.2); [`specs/shared/saga.md`](../shared/saga.md) §2 (command vocabulary), §4.1 (the intentional race), §6 layer 3; [`specs/shared/asyncapi.yaml`](../shared/asyncapi.yaml) (the five `stock*` channels, `RpcHeaders`, `RpcError`, `StockReserved`/`StockRejected`/`StockReleased`, the `fulfillmentFacts` topic); [`specs/outbox_and_idempotency/design.md`](../outbox_and_idempotency/design.md) (the unit of work, repository-drains-aggregate, the outbox writer, the relay, `OI9`'s drained-events hazard — **copied, never re-designed**); [`specs/order_saga_orchestrator/design.md`](../order_saga_orchestrator/design.md) §6 (the caller this responder answers, and the terminal/transient `RpcError` split feature 42 added).

## 1. Scope

**In scope.**

- The **`StockItem` aggregate** and its `Reservation` child entity, pure domain: `Reserve`, `Release`, `Consume`, `Replenish`, the reservation state machine, **F1**/**F2**/**F4**/**F5** enforced inside the aggregate; the order-scoped pure domain service that makes reservation **all-or-nothing across lines** (**F3**) and builds the three facts (§3).
- The **five NATS responders** — `fulfillment.stock.check`, `.reserve`, `.release`, `.list`, `.replenish` — as one `BackgroundService` over one transport, dispatching through the **existing `src/Cqrs` dispatcher**, request and reply bare JSON exactly per `asyncapi.yaml` (§5, §6).
- **Concurrent request handling with a bounded degree and one DI scope per request** (`FS18`) — the property #7 got free from its framework's server and #8's only responder precedent deliberately does **not** have (§6.2).
- The **authoritative reservation transaction** under MS-SQL with `READ_COMMITTED_SNAPSHOT ON`: an application-fixed lock order, explicit `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` on every decision-bearing read, and the honest statement that a check-then-reserve rejection is a designed outcome (§4).
- **Responder idempotency** by `orderReference` (`saga.md` §6 layer 3): a re-issued `stock.reserve` answers `already_reserved` **whatever the status of the existing rows**; a `stock.release` for an already-released or never-reserved order is a success no-op (§4.5).
- The **Fulfillment copies** of the unit of work, the outbox writer, the relay and the Kafka publisher, the rule that governs copying, and what is (and is not) parity-guarded (§8).
- One **bounded Orders-side change** the first real responder forces: the `x-correlation-id` / `x-request-id` request headers (§11).
- The **designed first boot** against the live compose stack: what the four parked `stock.reserve` commands do when a responder finally answers (§12).
- The **ported-idiom ledger** (§15) — binding since the Phase 8 gate, and this feature is the first to carry one.

**Out of scope, and owned elsewhere.**

| Not here | Owned by |
|---|---|
| `despatch.create`, the `DespatchAdvice` aggregate, `order.despatched.v1`, the `despatches` / `despatch_items` / `despatch_number_sequences` tables' runtime | feature 18 `fulfillment_despatch` (this feature leaves `Consume()` ready, with tests, and nothing calls it) |
| Billing responders (`credit.hold`, `invoice.issue`, `payment.register`) and the credit simulator | features 19 – 22 |
| The `RpcError` discriminator on `NatsStockAvailabilityChecker` | **feature 46 `orders_stock_check_rpc_error_discriminator`** — §13 |
| Consumer retry-to-DLQ, metrics, OpenTelemetry, `traceparent` / `x-deadline-ms` on RPC headers | feature 27 |
| The Gateway's `GET /stock` and `POST /stock/replenish`, and `R61`'s API test row | feature 25 |
| A code-parity guard over the relay copies, and the service-neutral refactor of the canonical | feature 19 (the third copy) — §8.3 |
| The idempotent-consumer copy | **not made here at all** — §9, and the gate row that rules on it |

## 2. Where everything lives

```
src/Fulfillment/
  Domain/
    README_PLACEHOLDER.cs                  KEPT — tests/Architecture.Tests/DomainAssemblies.cs selects this type by name
    StockItem.cs                           aggregate root (§3.1): Reserve / Release / Consume / Replenish, F1/F2/F4/F5
    Reservation.cs                         child entity (§3.2)
    ReservationStatus.cs                   enum + ReservationStatuses token map (the OrderStatuses convention)
    StockItemSnapshot.cs                   StockItemSnapshot + ReservationSnapshot — the plain shapes the mapper reconstitutes from
    OrderStockReservation.cs               pure domain service: all-or-nothing reserve/release across an order's items, builds the facts (§3.3)
    Events/StockDomainEvent.cs             abstract base — the OrderDomainEvent shape
    Events/StockReserved.cs                stock.reserved.v1
    Events/StockRejected.cs                stock.rejected.v1
    Events/StockReleased.cs                stock.released.v1
    Errors/*.cs                            six DomainError subclasses with stable codes (§3.4)
  Application/
    README_PLACEHOLDER.cs                  KEPT (same reason)
    Ports/IClock.cs                        copy of Orders'
    Ports/IUnitOfWork.cs                   copy of Orders' (§8.1)
    Ports/IFactPublisher.cs + PublishableFact.cs   copies of Orders'
    Ports/IStockItemRepository.cs          the locking write-side port (§5.2)
    Ports/IStockReadPort.cs                the non-locking read side (§5.2)
    Queries/CheckStockQuery.cs, ListStockQuery.cs + their IQueryHandler classes
    Commands/ReserveStockCommand.cs, ReleaseStockCommand.cs, ReplenishStockCommand.cs + their ICommandHandler classes
    StockReservationService.cs             the reserve/release transactional unit as a plain class the handlers delegate to (§5.3)
    StockReplenishService.cs               the replenish transactional unit (§5.3)
  Infrastructure/
    SystemClock.cs                         copy of Orders'
    FulfillmentOptions.cs                  MS-SQL / NATS / Kafka / relay / responder settings (§11.1)
    FulfillmentServiceCollectionExtensions.cs   AddFulfillment* — explicit registration, one port at a time
    Persistence/                           UNCHANGED from phase 6 (DbContext, entities, configurations, migration)
    Persistence/EfCoreUnitOfWork.cs        copy of Orders', over FulfillmentDbContext
    Persistence/EfCoreStockItemRepository.cs    the lock protocol (§4.3, §4.4) + save + outbox drain (§7)
    Persistence/EfCoreStockReadRepository.cs    check + paged list, plain AsNoTracking queries (§7.3)
    Persistence/StockRowMapper.cs          rows <-> StockItem (snapshot in, snapshot out)
    Outbox/FulfillmentFactTopic.cs         otc.fulfillment.facts.v1, guarded by a read-the-spec test
    Outbox/StockFactPayloadMapper.cs       domain event -> Contracts payload record (the OrderFactPayloadMapper shape)
    Outbox/OutboxWriter.cs                 copy of Orders' (StockDomainEvent in place of OrderDomainEvent)
    Outbox/OutboxEnvelopeMapper.cs         copy of Orders'
    Outbox/OutboxRelay.cs                  copy of Orders' (FulfillmentDbContext), banner per §8.3
    Outbox/OutboxRelayOptions.cs           copy of Orders'
    Outbox/OutboxRelayBackgroundService.cs copy of Orders'
    Outbox/KafkaFactPublisher.cs + KafkaOptions.cs   copies of Orders'; ClientId default otc-fulfillment
    Messaging/Rpc/StockRpcPayloads.cs      the ten request/reply records, transcribed from asyncapi.yaml (§6.3)
    Messaging/Rpc/RpcJson.cs               copy of Orders' — the one shared JsonWire.Options
    Messaging/Rpc/RpcErrorPayload.cs       copy of Orders'
    Messaging/NatsOptions.cs               copy of Orders'
  Presentation/
    README_PLACEHOLDER.cs                  KEPT (same reason)
    StockRpcResponder.cs                   ONE BackgroundService, five subjects, bounded concurrency, scope per request (§6.1, §6.2)
    Rpc/StockSubjects.cs                   the five subject constants, guarded by a read-the-spec test
    Rpc/StockRequestValidator.cs           hand-rolled validation (§6.4)
    Rpc/StockErrorMapper.cs                exception -> RpcError, FS21's transient/terminal discipline (§6.5)
    Rpc/RpcMeta.cs                         x-correlation-id / x-request-id extraction and refusal (§6.6)
  FulfillmentHost.cs                       composition root — the OrdersHost shape (§11.2)
  Program.cs                               NEW — the first runnable Fulfillment host
  Fulfillment.csproj                       gains Cqrs, NATS.Net, Confluent.Kafka, Hosting, Options, Logging.Abstractions

src/Orders/                                §11 — bounded to FS2 and nothing else
  Application/Ports/ISagaCommands.cs               each method gains SagaCommandMeta
  Infrastructure/Messaging/NatsSagaCommandsAdapter.cs   sends the two headers
  Infrastructure/Saga/SagaCommandDispatcher.cs     passes { CorrelationId = row.OrderId, RequestId = row.Id } on every attempt

tests/Fulfillment.UnitTests/               NEW project (added to OrderToCash.sln)
tests/Fulfillment.IntegrationTests/        EXISTING project, extended (schema tests from phase 6 stay untouched)
```

**No migration.** Every table this feature writes exists since phase 6: `stock`, `reservations`, `outbox`, `processed_events` (`src/Fulfillment/Infrastructure/Persistence/Migrations/20260901103111_InitialCreate.cs`). The schema was checked column by column against this design before it was written — §7.1 tables the result. **The implementer must not write a migration**; if a column looks absent, stop and report.

**Layering.** `Domain/` references only `OrderToCash.SharedKernel` and, for payload record types on the fact builders, `OrderToCash.Contracts.Facts` — the precedent `src/Orders/Domain/Events/` already set, allowed by `tests/Architecture.Tests/OrdersDomainContractsTests.cs`'s equivalent rule. No `Domain/` namespace may reference `OrderToCash.Cqrs`, `Microsoft.EntityFrameworkCore`, `NATS.*`, `Confluent.Kafka` or `System.Text.Json`; `DomainPurityTests` and `CqrsDomainPurityTests` already scan this assembly through `DomainAssemblies.All`. `decimal` never appears — this service handles no money at all, which `DomainDecimalTests` already covers.

## 3. The domain

### 3.1 `StockItem` — the aggregate root

```csharp
public sealed class StockItem : AggregateRoot
{
    public static StockItem Reconstitute(StockItemSnapshot snapshot);   // refuses F1 violations and negatives (InvalidStockItemSnapshotError)

    public string CompanyCode { get; }
    public string ProductCode { get; }
    public int Units { get; }
    public int ReservedUnits { get; }
    public int LowStockThreshold { get; }
    public int AvailableUnits => Units - ReservedUnits;                 // derived, never stored (asyncapi StockView)
    public IReadOnlyList<ReservationView> Reservations { get; }         // the reservations LOADED with this item (§7.2 — scoped to the order being handled)

    public bool CanReserve(Quantity units);                             // pure question, no mutation, no event (R31)
    public Reservation Reserve(UniqueId reservationId, OrderNumber orderReference, string retailerCode, Quantity units);
    public IReadOnlyList<Reservation> Release(OrderNumber orderReference);
    public IReadOnlyList<Reservation> Consume(OrderNumber orderReference);
    public void Replenish(Quantity quantity);
    public void RecordOrderFact(StockDomainEvent fact);                 // refuses unless fact.AggregateId == this.Id
    public StockItemSnapshot ToSnapshot();
}
```

- **`Reserve`** creates one reservation in status `reserved` and adds `units` to `ReservedUnits`; it throws `InsufficientStockError` (`INSUFFICIENT_STOCK`, carrying `productCode`, `requested`, `available`) if it would break **F1** (`R30`). It emits nothing — the order-scoped fact is the domain service's job (§3.3).
- **`Release`** moves this item's `reserved` reservations of `orderReference` to `released` and subtracts their units, returning them; an **empty** list when none was `reserved` (**F5**, idempotent). It throws `ReservationTerminalError` if any of the order's reservations on this item is `consumed` (**F4**, `FS10`). Emits nothing.
- **`Consume`** moves this item's `reserved` reservations of `orderReference` to `consumed` and subtracts their total from **both** `Units` and `ReservedUnits` (`domain-model.md` §4.2 row 4), returning them. Emits nothing — `order.despatched.v1` is feature 18's `DespatchAdvice` fact (`FS11`).
- **`Replenish`** adds to `Units` and appends **no** domain event (`R61`).
- **`RecordOrderFact`** is the only way a fact reaches the aggregate, and it refuses (`FactAggregateMismatchError`) unless the fact's `AggregateId` equals its own — the one guard that stops this method being a generic "emit anything" hole.

**Invariant F1 lives here, not in the schema — and that was decided in phase 6.** `StockConfiguration` deliberately declares no `CHECK (reserved_units <= units)`: a check constraint would fire on legitimate intermediate states inside one transaction, and it would duplicate logic the aggregate must have anyway in order to produce a `stock.rejected.v1` **fact** rather than a raw provider error. `R30`'s test is a domain unit test for exactly that reason.

**Every counter operation is overflow-guarded (`FS20`).** `Directory.Build.props` sets no `CheckForOverflowUnderflow`, so C# `int` arithmetic wraps in silence, and `units` / `reserved_units` are `int` columns. Concretely:

- `Replenish` refuses when `quantity.Value > int.MaxValue - Units`, raising `StockUnitOverflowError` (`STOCK_UNIT_OVERFLOW`) and changing nothing.
- Availability is decided by **subtraction** — `requested <= Units - ReservedUnits` — never by `ReservedUnits + requested > Units`, so the F1 test itself cannot overflow.
- The domain service sums the units of **repeated lines naming the same product** into a `long` before comparing against `AvailableUnits`, so an order with many large lines cannot wrap its own total.
- `Reconstitute` refuses `ReservedUnits > Units`, a negative counter, or a reservation set whose reserved units do not fit an `int`.

**F2 is maintained by construction.** `ReservedUnits` is never assigned from outside: every mutation happens inside `Reserve` / `Release` / `Consume` together with the reservation it describes, and the repository writes both in one transaction (`FS12`). Reconstitution trusts the stored counter (it *is* the authoritative cache) and loads only the reservations of the order being handled — loading every historical reservation of a popular item on every command would grow with history for no invariant's benefit. So the aggregate **preserves** F2 rather than **recomputing** it; the stored equality is asserted by an integration test after every committed operation, which is where drift would actually be visible.

### 3.2 `Reservation` — the child entity and its state machine

```csharp
public enum ReservationStatus { Reserved, Released, Consumed }
public static class ReservationStatuses { public static string ToToken(...); public static ReservationStatus Parse(string? token); }

public sealed class Reservation : Entity
{
    public static Reservation Create(UniqueId id, OrderNumber orderReference, string companyCode, string retailerCode, string productCode, Quantity units);  // Reserved
    public static Reservation Reconstitute(ReservationSnapshot snapshot);
    public void Release();   // Reserved -> Released; anything else throws ReservationTerminalError and changes nothing (F4, R35)
    public void Consume();   // Reserved -> Consumed; likewise
}
```

The legal-transition table is the two edges of `domain-model.md` §4.2 and nothing else; `Released → *` and `Consumed → *` throw. A `Reservation` is reachable only through its `StockItem` (`StockItem.Reservations` exposes a frozen `ReservationView`, the discipline `Order`'s line views already use), so nobody can move a reservation without the owning item's counter moving with it. `ReservationStatuses.Parse` refuses any token outside the closed set — the persistence column is `nvarchar(20)` free text, and a typo must be a loud parse failure, not a silently unmatched status.

### 3.3 The order-scoped operation — a pure domain service, and the three facts

**Why a service and not a method.** An order's lines name different products, and a `StockItem` is one `(companyCode, productCode)`. **F3** ("either every line of the order is reserved, or none is") is therefore a rule *across* aggregates, and `R32`/`R33` ask for **exactly one** fact per order — not one per item. The rule needs a home that sees all the items at once, and that home is a pure static class in `Domain/`, not an application handler (a handler owning a domain invariant is the layering mistake repository-drains-aggregate exists to prevent).

```csharp
public static class OrderStockReservation
{
    public static ReserveOrderOutcome Reserve(
        IReadOnlyDictionary<string, StockItem> itemsByProductCode,   // case-insensitive comparer, §4.3
        ReserveOrderInput input,                                     // orderReference, companyCode, retailerCode, lines, correlationId
        StockContext context,                                        // occurredAt + causationId — time in, nothing pulled
        Func<UniqueId> newId);

    public static ReleaseOrderOutcome Release(
        IReadOnlyList<StockItem> items,
        ReleaseOrderInput input,                                     // orderReference, reason, correlationId
        StockContext context);
}
```

`Reserve` evaluates **every** line against the supplied items **before** mutating any: a product with no item is short with `available: 0` and sets the reason to `unknown_product` (`FS8`), any other shortfall sets `insufficient_stock`; only when every line is satisfiable does it call `StockItem.Reserve` per line. Exactly one fact is appended — `stock.reserved.v1` or `stock.rejected.v1` — to the **carrier**, via `carrier.RecordOrderFact`. `Release` calls `StockItem.Release` on each item and, if the union of released reservations is non-empty, appends exactly one `stock.released.v1` to the carrier; otherwise it returns `AlreadyReleased` and appends nothing (**F5**, `R34`, `FS9`). Both are pure: no I/O, no clock, no ids beyond those `newId` supplies.

**The carrier, stated once (`FS13`).** `aggregateId` must be a real aggregate id (`domain-model.md` §7.1). The fact takes the item of the **first request line that resolves to a known item** (reserve/reject) or of the **first released reservation** (release). This matches the precedent `src/Seed/Domain/Sagas/SagaFixtures.cs` already set (`firstStockItemId = StockRowId(companyCode, lines[0].ProductCode)` for the seeded `stock.*` facts), so seeded and live facts look alike to the projector. If **no** line resolves to a known item there is no carrier: the service returns `NoCarrier` and the application layer raises `NoKnownStockItemError`, which the responder maps to `RpcError` `NOT_FOUND` (§6.5, and see §4.6 for the #8-specific consequence).

The three fact records mirror `src/Orders/Domain/Events/` exactly: `StockDomainEvent` is the abstract base implementing `IDomainEventEnvelope`; `CorrelationId` is the order id from `x-correlation-id`; `AggregateId` is the carrier's id; `CausationId` and `OccurredAt` come from `StockContext`. `RetailerCode` is populated on all three payloads (optional in the schema, always known here). `stock.rejected.v1`'s `shortages[]` carries `requested` and `available` per **short** line only; satisfiable lines are not listed.

### 3.4 Domain errors

All extend `DomainError` with a stable `Code`: `InsufficientStockError` (`INSUFFICIENT_STOCK`), `ReservationTerminalError` (`RESERVATION_TERMINAL`, carrying the attempted transition), `InvalidStockItemSnapshotError` (`INVALID_STOCK_ITEM_SNAPSHOT`), `FactAggregateMismatchError` (`FACT_AGGREGATE_MISMATCH`), `StockUnitOverflowError` (`STOCK_UNIT_OVERFLOW`), `UnknownReservationStatusError` (`UNKNOWN_RESERVATION_STATUS`). §6.5 is where these codes become wire codes.

### 3.5 Invariants → where they are enforced

| Invariant | Enforced by | Proven by |
|---|---|---|
| **F1** `reservedUnits ≤ units` | `StockItem.Reserve` (throws), `Reconstitute` (refuses) | `R30` domain unit |
| **F2** counter = Σ reserved | every counter mutation co-located with its reservation mutation; one transaction | `FS12` unit + integration |
| **F3** all-or-nothing per order | `OrderStockReservation.Reserve` evaluates all lines before mutating any | `R32` / `R33` domain unit, `FS6` integration |
| **F4** terminal states | `Reservation.Release` / `Consume` throw from `Released` / `Consumed` | `R35` domain unit, `FS10` |
| **F5** release is a no-op once released | `StockItem.Release` returns `[]`; the service emits nothing | `R34` domain unit + integration, `FS9` |

## 4. The check-then-reserve race, honestly — and what MS-SQL changes

### 4.1 `stock.check` is a read that holds nothing

`CheckStockQuery` runs one `AsNoTracking` `SELECT` over `stock` for the request's company and product codes — **no** lock hint, **no** transaction, no reservation row, no counter change, no outbox row (`R31`). Its reply says *"at the moment of this read, each line was / was not satisfiable"*; `StockCheckReplyPayload.available`'s own description already says *"it is not a promise, and it is not a reservation"*. Orders calls it **before** persisting the order (`saga.md` §3.1 step 0) so an obviously unfulfillable order is refused synchronously instead of being accepted and cancelled a second later. That is its whole job. An unknown product answers `available: 0, sufficient: false` — never an error (`FS22`, §13).

### 4.2 `stock.reserve` is the authoritative claim — and F3 wins over §8 rule 6

Between the check and the reserve, any number of other orders may have reserved the same units. The reserve is therefore the **only** place availability is decided, and it decides under a lock. When it finds the units gone it emits `stock.rejected.v1` and the saga takes Path A (`saga.md` §4.1: *"The race is real and intentional … That is precisely why the saga exists"*). **This is not a bug to paper over**: no re-check-and-retry loop, no soft hold at check time, no reservation TTL. `FS7`'s integration test makes a check succeed, lets another order take the units, and asserts the later reserve is rejected cleanly.

**The tension with `domain-model.md` §8 rule 6** (*"One transaction mutates exactly one aggregate instance plus its outbox records"*) is real and is resolved in favour of **F3** — F3 is a normative invariant in the same document, rule 6 is a cross-cutting default. One `stock.reserve` transaction mutates **one `StockItem` per distinct product of the order**, plus their reservation rows, plus one outbox row. What makes that safe is the lock protocol below. #7's gate ruled the same way (its row 4); this feature inherits the ruling and carries the same promotion candidate rather than re-litigating it.

### 4.3 The lock protocol for `stock.reserve` — MS-SQL under `READ_COMMITTED_SNAPSHOT ON`

Inside one `IUnitOfWork.ExecuteAsync` (`IsolationLevel.ReadCommitted`, stated explicitly), in this order and nothing else:

```sql
-- 1. claim the stock rows — ONE STATEMENT PER ROW, issued in an order the
--    application fixes, never one multi-row statement (FS19)
SELECT id, company_code, product_code, units, reserved_units, low_stock_threshold, created_at, updated_at
FROM   dbo.stock WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
WHERE  company_code = @company AND product_code = @product;   -- repeated, in fixed order

-- 2. the order's existing reservations — a LOCKING read, AFTER the stock locks
SELECT id, stock_id, company_code, retailer_code, product_code, order_reference, units, status, created_at, updated_at
FROM   dbo.reservations WITH (UPDLOCK, HOLDLOCK)
WHERE  order_reference = @orderReference;

-- 3. domain: OrderStockReservation.Reserve(...)   -- pure, decides reserved | rejected

-- 4. reserved:  INSERT reservations (one per line); UPDATE stock SET reserved_units, updated_at (per item);
--               INSERT outbox (one row: stock.reserved.v1)
--    rejected:  no write to stock or reservations; INSERT outbox (one row: stock.rejected.v1)
-- COMMIT
```

Four things about this differ from #7's single `SELECT … FOR UPDATE … ORDER BY (company_code, product_code)`, and each is load-bearing.

- **One statement per row, in an application-fixed order.** #7 got its total order from InnoDB's scan of the unique index, so two concurrent multi-line reserves requested their overlapping locks in the same global order and the classic deadlock shape could not form. **MS-SQL offers no such guarantee**: an `ORDER BY` inside a `SELECT` constrains the *result* order, not the order in which the plan touches (and locks) rows, and a multi-value seek's access order is a planner decision that a statistics change can flip. Issuing one single-row locking statement per product, in an order fixed by the application, gives the property **by construction** and makes it independent of the plan. The cost is *n* round trips for an *n*-distinct-product order — single-digit in this domain, inside an already-open transaction.
- **The order is the invariant-uppercased ordinal order of the distinct product codes**, with distinctness itself computed case-insensitively (`StringComparer.OrdinalIgnoreCase`). Sorting in the application rather than in SQL removes the database collation from the ordering entirely, which matters because the two engines do not agree: MySQL's `utf8mb4_0900_ai_ci` and MS-SQL's server-default `SQL_Latin1_General_CP1_CI_AS` are both case-insensitive but differ on padding and on accent handling. Uppercasing before the ordinal comparison is what keeps two callers that spell a code with different letter-case — which the CI collation resolves to the **same row** — from deriving **different** lock orders and deadlocking. (The residual: an accent-insensitive collation could still map two ordinally-distinct codes to one row. `asyncapi.yaml`'s `ProductCode`/`PartyCode` alphabet and this repository's seed are ASCII, and §6.4's validator rejects anything else, so the boundary is closed at the edge rather than assumed away.)
- **`WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` on every decision-bearing read.** `READ_COMMITTED_SNAPSHOT` is **ON** for all four databases (`infra/mssql/init/01-create-databases.sql`), so an un-hinted read takes **no lock at all** and returns a row version — which would make step 2's idempotency check read a stale snapshot and let two reserves for the same order both proceed. The hints override row versioning for that reference, exactly as `EfCoreOrderNumberAllocator` already documents and as the init script's own comment already anticipates for *"the stock reservation path"*. `HOLDLOCK` additionally takes a key-range lock, so step 1's answer for a **missing** `(company, product)` is stable for the transaction, and step 2 blocks a concurrent insert of a reservation for the same order rather than racing it.
- **Why the reservations read comes second and is locking.** Two `stock.reserve` for the **same** order (the sweeper re-issuing a row whose reply was lost) must not both reserve. Both first block on the same stock rows, so the second runs step 2 only after the first has committed, and the lock hint makes that read current rather than versioned: it sees the first's committed rows and answers `already_reserved` (`FS5`). Without "stock first", the idempotency check would be racy by construction.

**Why the transaction is short.** Four statement kinds and a pure function. No NATS, no Kafka, one `IClock.UtcNow` read; the reply is sent after commit and the fact leaves through the relay.

**Deadlock is not assumed impossible.** The protocol makes the known shape unformable, but MS-SQL can still pick a victim (error 1205) for reasons outside this design's control. A deadlock victim is a **transient** failure and is mapped accordingly (`FS21`, §6.5) — never to `CONFLICT`.

### 4.4 The lock protocol for `stock.release`

```text
0. NON-LOCKING pre-read: SELECT product_code, stock_id FROM reservations WHERE order_reference = @ref
   -> empty  => reply already_released with released: [], NO transaction, no fact (FS9)
1. lock those stock rows, one statement per row, same fixed order as §4.3
2. the order's reservations, WITH (UPDLOCK, HOLDLOCK) — now authoritative
3. domain: OrderStockReservation.Release(...)
4. released:          UPDATE reservations SET status/updated_at (per row); UPDATE stock SET reserved_units (per item);
                      INSERT outbox (one row: stock.released.v1)
   already_released:  nothing written
   COMMIT
```

The pre-read decides only *whether to open a transaction*; the authoritative decision is re-made under lock in step 2. If step 2 returns a reservation whose `stock_id` was not locked in step 1 — impossible in practice, since an order's reservations are created once, under the stock locks, by `stock.reserve` — the service raises `ConcurrentReservationChangeError` and lets the orchestrator retry, rather than releasing under a lock it does not hold. That is a defensive branch with a unit test, not an expected path, and it maps to a **transient** code (`FS21`).

### 4.5 Responder idempotency — the keys, stated once

| Command | Idempotency key (`saga.md` §2) | What a repeat observes | Reply | Fact |
|---|---|---|---|---|
| `stock.reserve` | `orderReference` | **any** reservation rows for the order, in **any** status | `already_reserved` + the existing `ReservationRef`s | none |
| `stock.release` | `orderReference` | no row `reserved` (all `released`, or none at all) | `already_released` + `released: []` | none |
| `stock.release` | `orderReference` | a row `consumed` | `RpcError` `PRECONDITION_FAILED` | none |
| `stock.replenish` | — (not a saga command; `R61`) | — | applied again | none — **not idempotent by design**: a top-up is a delta, and "did I already send this" belongs to the Gateway / demo workflow (features 25, 31) |
| `stock.check`, `stock.list` | — (reads) | — | — | none |

`x-request-id` is **not** the idempotency key. `saga.md` fixes the key as `(orderReference, operation)`, and a repeat must behave identically whether the retry came from the in-line policy (same row id) or from an operator re-running the step with a new row. The header is the *causation* carrier (`FS3`), nothing more.

**The `already_reserved` short-circuit filters on nothing but `orderReference`.** This is the single line #7's review rejected the feature over: adding `&& status == "reserved"` to it re-reserves an order the saga has already unwound, and #7's entire suite stayed green under exactly that mutation. `tasks.md` names two guards for it — a `[Theory]` over `released` and `consumed` at unit level and one integration case seeding a `released` reservation before the reserve — and requires the mutation to be armed and its failure message recorded.

### 4.6 `NOT_FOUND` is terminal in #8 — a consequence worth stating

When **no** line of a `stock.reserve` resolves to a known stock item there is no carrier aggregate for a fact, so the responder replies `RpcError` `NOT_FOUND` (§3.3). In #7 the orchestrator retried every `RpcError` code and the command eventually **parked**, so it would self-heal the moment master data appeared. In #8, feature 42 classifies `NOT_FOUND` as a **terminal business rejection**: `SagaCommandDispatcher` marks the row `rejected`, a status `ClaimDueAsync`'s predicate structurally never re-claims. The order therefore stays `placed` with a `rejected` `stock.reserve` row — loud in one `SELECT`, but not self-healing.

That is accepted, not fixed here: feature 42's classification is a gate-ratified #8 ruling, an order naming a product its supplier does not stock is a contract violation that has already passed `stock.check`, and re-issuing the command is one operator action. It is recorded because the difference from #7 is real and a reviewer comparing the two runs would otherwise read it as a defect. `src/Seed`'s baseline rows make the edge nearly unreachable for demo data (`StockCatalog.Build` gives every saga-untouched company a row for every product); the residual is a saga-covered company asked for a product its fixtures never used, which §12 tells the implementer to check for before the live walkthrough.

## 5. The application layer — the existing `src/Cqrs` dispatcher

### 5.1 Messages and handlers

| Subject | Message | Handler | Transactional? |
|---|---|---|---|
| `fulfillment.stock.check` | `CheckStockQuery : IQuery<StockCheckReplyPayload>` | `CheckStockQueryHandler` → `IStockReadPort.AvailabilityAsync` | no — one `SELECT` |
| `fulfillment.stock.list` | `ListStockQuery : IQuery<StockListReplyPayload>` | `ListStockQueryHandler` → `IStockReadPort.ListAsync` | no |
| `fulfillment.stock.reserve` | `ReserveStockCommand : ICommand<StockReserveReplyPayload>` | `ReserveStockCommandHandler` → `StockReservationService.ReserveAsync` | yes — §4.3 |
| `fulfillment.stock.release` | `ReleaseStockCommand : ICommand<StockReleaseReplyPayload>` | `ReleaseStockCommandHandler` → `StockReservationService.ReleaseAsync` | yes — §4.4 |
| `fulfillment.stock.replenish` | `ReplenishStockCommand : ICommand<StockReplenishReplyPayload>` | `ReplenishStockCommandHandler` → `StockReplenishService.ReplenishAsync` | yes — lock the named items in the same fixed order, `Replenish` each, save; **no outbox row** |

The dispatcher is binding in all six services (`CLAUDE.md`, Phase 8 gate ruling) — not reopened. `AddDispatcher(Assembly.GetExecutingAssembly())` runs **after** every port is registered, so a missing or duplicated handler is a `DispatcherValidationException` at boot, not a first-dispatch surprise. `ReserveStockCommand` and `ReleaseStockCommand` carry `CorrelationId` and `RequestId` as `UniqueId`; the query messages carry neither. The command/query handler classes are thin delegations so that `StockReservationService` and `StockReplenishService` stay plain classes a unit test can `new` with fakes — the split `SagaFactHandler` already uses.

**No `IEventHandler` and no in-process fan-out here.** Fulfillment owes no post-commit in-process hop: its post-commit obligation is the relay's, and durability never depends on an in-memory bus. The command dispatch is awaited by the responder, so "reply after commit" is structural.

### 5.2 Ports

```csharp
public interface IStockItemRepository
{
    /// Locks one row per distinct product code, one statement each, in the FS19 order,
    /// then loads the order's reservations under the same lock discipline. Unknown product
    /// codes are simply absent from the returned dictionary (OrdinalIgnoreCase keys).
    Task<StockLockResult> LockForOrderAsync(string companyCode, IReadOnlyList<string> productCodes, OrderNumber orderReference, CancellationToken ct);

    /// Non-locking pre-read for release (§4.4 step 0) — the distinct product codes the order's reservations point at.
    Task<IReadOnlyList<string>> ProductCodesOfOrderAsync(OrderNumber orderReference, CancellationToken ct);

    /// Syncs each loaded item's row and its reservations, drains EVERY item's DomainEvents into outbox rows,
    /// then SaveChangesAsync — all inside the ambient transaction. Never opens its own (R13).
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IStockReadPort   // never locks, never mutates, no transaction
{
    Task<StockCheckReplyPayload> AvailabilityAsync(string companyCode, IReadOnlyList<StockCheckRequestLine> lines, CancellationToken ct);
    Task<StockListReplyPayload> ListAsync(StockListRequestPayload query, CancellationToken ct);
}
```

`StockLockResult` carries `IReadOnlyDictionary<string, StockItem> ItemsByProductCode` and `IReadOnlyList<ReservationSnapshot> ExistingReservationsOfOrder` — the second is what the `already_reserved` short-circuit reads, and it deliberately includes reservations whose product is **not** in this request, so a terminal reservation on a product the retry omitted still short-circuits (`FS5`).

There is **no `tx` parameter** anywhere. #8's unit of work opens a transaction on the scoped `DbContext` and every collaborator resolved from the same scope enlists automatically — the same shape `IOrderRepository` already has, and a real simplification over #7's Drizzle-shaped `TransactionContext`.

### 5.3 The transactional units

`StockReservationService.ReserveAsync`: `unitOfWork.ExecuteAsync(ct => LockForOrderAsync → if ExistingReservationsOfOrder is non-empty, build already_reserved from them (no domain call, nothing written) → else OrderStockReservation.Reserve(...) → repository.SaveChangesAsync → map the outcome to a reply)`. The reply is built from the domain outcome inside the delegate but returned only after `ExecuteAsync` resolves, so a rollback can never have produced a success reply. `context = new StockContext(clock.UtcNow, command.RequestId)` (`FS3`, `R12`). A **business rejection is a resolved reply, never a throw** (`saga.md` §7, `SO6`).

`StockReservationService.ReleaseAsync`: §4.4, including the no-transaction `already_released` path and the defensive `ConcurrentReservationChangeError`.

`StockReplenishService.ReplenishAsync`: lock the named items in the fixed order; if any is missing raise `UnknownStockItemError` **before** mutating anything (all-or-nothing, `FS14`); `Replenish` each (overflow-guarded, `FS20`); save; reply the affected `StockView`s. No outbox row is written, and `R61`'s "no fact" is asserted by reading the outbox after the call, not by inspection.

## 6. Presentation — the responders

### 6.1 One `BackgroundService`, five subjects

`StockRpcResponder : BackgroundService` subscribes to the five subjects of `StockSubjects` over the singleton `INatsConnection` and runs the five `await foreach` loops concurrently. One `BackgroundService` per **transport** (`CLAUDE.md`) — NATS is one transport, and five separate classes would duplicate the concurrency, scope and error-mapping logic five times. Kafka is not a transport this service consumes on (§9), so no second consumer service exists.

Per message: extract `RpcMeta` (§6.6) → deserialise with `RpcJson` → validate (§6.4) → resolve `IDispatcher` from a **fresh DI scope** → `SendAsync`/`QueryAsync` → `message.ReplyAsync(RpcJson.Serialize(reply))`. **The responder never throws and never leaves a request unanswered**: every path is wrapped, and the catch replies a mapped `RpcError` (§6.5) — the rule `OrdersCreateResponder` already follows.

The subject constants live in `Presentation/Rpc/StockSubjects.cs` and are guarded by a read-the-spec-as-text unit test asserting each equals its `asyncapi.yaml` channel `address`, the instrument `RpcSubjectsTests` and `OrdersFactTopicTests` already use.

### 6.2 Concurrency — the property that must be hand-built (`FS18`)

`src/Orders/Presentation/OrdersCreateResponder` handles requests **strictly sequentially** — `await HandleAsync(...)` inside the subscription loop — and its own remark explains why that was right there (the order-number allocator serialises every placing transaction behind a row lock anyway) and says: *"Revisit if a later feature adds a second concurrent RPC subject this responder must not block."* **This is that feature**, and copying the sequential shape would be a silent, load-bearing regression:

- Two `stock.reserve` commands could never be in flight together, so the lock protocol of §4.3 would never be exercised — and `FS6`'s race test would still pass, because two **serialised** reserves also produce exactly one `accepted` and one `rejected`. A green test proving nothing is precisely the failure class this repository keeps paying for.
- A `stock.check` — a read on the order-acceptance path, with a 5 000 ms caller budget — would queue behind a `stock.reserve` blocked on a row lock.

So the responder dispatches concurrently, with two bounds:

1. **A `SemaphoreSlim(MaxConcurrentRequests)`** acquired *before* the scope is created and released in a `finally`. Default **32**, and the number is chosen against ADO.NET's default `Max Pool Size` of 100: a request blocked on a stock row lock holds its pooled connection for the whole wait, so the concurrency bound must stay comfortably below the pool so a lock convoy degrades into *waiting* rather than into pool-exhaustion timeouts (which would surface as `INTERNAL_ERROR` replies instead of correct, slightly slower ones).
2. **One `IServiceScope` per request**, never one per responder — `FulfillmentDbContext` is scoped and is not thread-safe, and `IDispatcher` is registered scoped for the reason `OrdersCreateResponder`'s remark already gives (a singleton dispatcher captures the root provider).

In-flight tasks are tracked so `StopAsync` can await them within the host's shutdown timeout rather than tearing down a half-committed transaction.

**Guarded, not asserted.** `FS18` gets two tests: a unit test proving a distinct scope per request, and an integration test that is deterministic rather than timing-based — the test opens its own MS-SQL transaction holding `WITH (UPDLOCK, HOLDLOCK)` on product **P1**'s stock row, sends a `stock.reserve` for **P1** (which blocks inside the responder), then sends a `stock.reserve` for **P2** and asserts it is **answered while the first is still blocked**, before releasing the test's lock and letting the first complete. Under the sequential shape the second request times out; under the concurrent shape it replies. `tasks.md` arms exactly that reversion.

### 6.3 The wire — bare JSON, and why #8 has nothing to build

#7's single largest gate row on this feature (its row 1) was that `@nestjs/microservices`' NATS server treats an id-less bare request as an **event** and never replies, and wraps replies in a `{response, isDisposed, id}` packet — so it had to write a `BareJsonNatsDeserializer` / `BareJsonNatsSerializer` pair to speak the AsyncAPI wire at all.

**#8 has no such layer.** `INatsConnection.SubscribeAsync<byte[]>` yields the raw payload and `NatsMsg<byte[]>.ReplyAsync(byte[])` publishes the raw reply; `OrdersCreateResponder` already speaks exactly this shape and Orders' two outbound adapters already decode it. Request and reply pass through `RpcJson`, i.e. the one shared `JsonWire.Options` (`camelCase`, nulls omitted) that `CLAUDE.md`'s wire non-negotiable fixes. The saving is real and belongs in the effort record; the **assertion is kept anyway** (`FS4`), because "the reply body has no `response`/`isDisposed`/`id` key and deserialises directly into the AsyncAPI reply schema" is a trilogy contract, not a framework detail.

The ten request/reply records are transcribed into `Infrastructure/Messaging/Rpc/StockRpcPayloads.cs` — Fulfillment's own copy, not a reference to Orders' `SagaCommandPayloads.cs`, per the established rule that *RPC payloads live in the service that speaks them*. The two transcriptions are held to the same wire by `StockRpcPayloadTests`, the instrument `tests/Orders.UnitTests/SagaCommandPayloadTests.cs` already established: it reads `asyncapi.yaml` as text and compares each schema's declared property names against the record's `JsonWire`-serialised property names.

### 6.4 Validation

No `class-validator` equivalent is added. `StockRequestValidator` is a static class in `Presentation/Rpc/`, mirroring `OrdersCreateRequestValidator`, throwing a validation error the mapper turns into `VALIDATION_FAILED`. Rules, from `asyncapi.yaml`: `orderReference` matches `^ORD-[0-9]{6,}$`; `companyCode`/`retailerCode` 1–20 characters; `productCode` 1–30 characters; every `units`/`quantity` a strictly positive integer; `lines` at least one item; `reason ∈ {credit_rejected, order_cancelled}`; `page ≥ 1` (default 1) and `pageSize` 1–200 (default 25). The alphabet check is what closes the accent-collation residual of §4.3.

### 6.5 `StockErrorMapper` — and the one code this service may never send

A pure function, the shape of `OrdersCreateErrorMapper` but with its own cases:

| Thrown | `RpcError.code` | Class |
|---|---|---|
| validation failure | `VALIDATION_FAILED` | terminal — correct: a malformed request never becomes well-formed |
| `NoKnownStockItemError`, `UnknownStockItemError` | `NOT_FOUND` (+ `details.productCode`) | terminal — §4.6 records the consequence |
| `ReservationTerminalError` | `PRECONDITION_FAILED` | terminal — a consumed reservation never un-consumes |
| any other `DomainError` | `DOMAIN_ERROR` (+ `details.code`) | terminal |
| `SqlException` 1205 (deadlock victim), 1222 (lock request timeout), or a transient connection error; `DbUpdateConcurrencyException`; `ConcurrentReservationChangeError` | **`UNAVAILABLE`** | **transient — retried by the orchestrator** |
| the request's own deadline elapsing | `TIMEOUT` | transient |
| anything else | `INTERNAL_ERROR` | transient |

**`CONFLICT` is banned from this service's mapper**, and that is not a style preference. `src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.IsTerminalRpcErrorCode` classifies `CONFLICT` as a **terminal business rejection**: the dispatcher would mark the `saga_commands` row `rejected`, which `ClaimDueAsync` never re-claims. #7 mapped its concurrency error to `CONFLICT` and its orchestrator retried it, so the same mapping is safe there and unsafe here (`FS21`). The unit test reads the terminal set from `NatsSagaCommandsAdapter`'s own classification rather than retyping it, so a future change on the Orders side breaks this test rather than silently changing this service's meaning.

### 6.6 `RpcMeta` — the correlation and request headers

`RpcMeta.From(NatsHeaders?)` reads `x-correlation-id` and `x-request-id`, parses both as `UniqueId`, and returns a failure the responder turns into `VALIDATION_FAILED` before any dispatch when either is absent or malformed **on `stock.reserve` and `stock.release`** (`FS3`). `stock.check`, `stock.list` and `stock.replenish` require neither. The refusal is what keeps `R15` true: a fact emitted without the order id would land on an arbitrary partition and break per-order ordering for the orchestrator.

`traceparent` and `x-deadline-ms` are read by nobody yet — feature 27's.

## 7. Persistence — the EF Core adapters

### 7.1 The phase-6 schema is sufficient — checked, not assumed

| This design needs | Exists as | Where |
|---|---|---|
| one row per `(company_code, product_code)`, unique | `stock` + unique index on `(company_code, product_code)` | `StockConfiguration` |
| `units`, `reserved_units`, `low_stock_threshold` as integers | `int` columns | `Stock` entity |
| reservations by order, with a status | `reservations`, index on `(order_reference, status)` | `ReservationConfiguration` |
| reservation → stock referential integrity | FK `stock_id → stock.id`, `ON DELETE NO ACTION` | `ReservationConfiguration` |
| the outbox, with `seq IDENTITY` publication order | `outbox` | `OutboxMessageConfiguration` |
| `processed_events` | present (unused by this feature — §9) | `ProcessedEventConfiguration` |

Nothing is missing. `tests/Fulfillment.IntegrationTests`' existing phase-6 schema tests stay untouched and must stay green.

### 7.2 `EfCoreStockItemRepository`

- **`LockForOrderAsync`** issues one `FromSqlInterpolated` per distinct product code (§4.3), in the FS19 order, **tracked** (not `AsNoTracking` — `SaveChangesAsync` must issue the `UPDATE`s), then one locking read of the order's reservations. Both statements name **every mapped column literally** — `FromSqlInterpolated` requires the full projection, and an interpolation hole would become a bound parameter rather than a column list, exactly as `OutboxRelay.ClaimColumnNames` documents. Two `…ProjectionTests` compare those literal lists against the `IEntityType`'s mapped properties mechanically, the E7 instrument already in the repository. Each row is mapped through `StockRowMapper` into a `StockItem` and kept in an identity map of `(aggregate, row)` pairs, the shape `EfCoreOrderRepository` already uses.
- **`SaveChangesAsync`** syncs each aggregate's mutable fields onto its tracked row, adds new `Reservation` rows and updates changed ones, then drains **every** loaded aggregate's `DomainEvents` into outbox rows and calls `DbContext.SaveChangesAsync`, clearing the domain events only after everything above returned (`OI9`: clearing early loses the events on a rollback). Outbox rows are inserted **one awaited statement at a time**, copied verbatim from `EfCoreOrderRepository.InsertOutboxRowAsync` together with its comment — EF Core's SQL Server provider does not preserve `Add` order when assigning `IDENTITY` values, and `seq` is the entire publication-order guarantee. **No upsert is rendered anywhere**: the rows were loaded under a lock in this same transaction, so an `UPDATE` by primary key is exactly right, and check-then-act never arises (§15).
- The repository **drains**; the handler never does.

### 7.3 `EfCoreStockReadRepository`

`AvailabilityAsync` is one `AsNoTracking` query over the requested product codes plus per-line arithmetic; an unknown product yields `{ available: 0, sufficient: false }` (`FS22`). `ListAsync` applies the optional filters, expresses `belowThreshold` as `units - reserved_units < low_stock_threshold` in SQL, orders by `(company_code, product_code)`, applies `Skip`/`Take` from the page and issues a `CountAsync` for `PageInfo.total`. Two queries, no transaction, no hint — under RCSI these are versioned reads that block nobody, which is what `R31` asks for.

## 8. The outbox: writer, relay, publisher — copies, and the rule that governs them

### 8.1 What is copied, and why copying is the rule

`IUnitOfWork` + `EfCoreUnitOfWork`, `IClock` + `SystemClock`, `IFactPublisher` + `PublishableFact`, `OutboxWriter`, `OutboxEnvelopeMapper`, `OutboxRelay`, `OutboxRelayOptions`, `OutboxRelayBackgroundService`, `KafkaFactPublisher`, `KafkaOptions` — taken from `src/Orders/` with exactly these edits: `OrdersDbContext` → `FulfillmentDbContext`, `OrderDomainEvent` → `StockDomainEvent` (the writer's cast and its `FactCatalog` membership check), `OrdersFactTopic` → `FulfillmentFactTopic` (`otc.fulfillment.facts.v1`), `ClientId` default `otc-orders` → `otc-fulfillment`, and a banner on each file naming the Orders original. Same claim (`WITH (UPDLOCK, READPAST, ROWLOCK)`, `ORDER BY seq`), same stamp-after-acknowledgement, same publish timeout, same self-scheduling loop.

`CLAUDE.md`: *"The only shared runtime code is `src/SharedKernel`, `src/Contracts` and `src/Cqrs`. Nothing else is shared."* A shared outbox project would be a fourth, and it would couple three services' release cadence for ~300 lines over a table that database-per-service already duplicates. #7 ruled identically for the same reason.

### 8.2 The two services must not share a Kafka client id

`KafkaOptions.ClientId` defaults to `otc-fulfillment` here. Two services silently sharing a client id makes broker-side metrics and logs ambiguous for no benefit. `.env.example` gains `FULFILLMENT_KAFKA_CLIENT_ID` beside the existing `KAFKA_*` entries.

### 8.3 What is parity-guarded, and what is not — said out loud

- **Guarded:** the **schema** parity of the reliability tables across all four databases (`ReliabilityTableParityTests`, phase 6) and `seq` identity (`OutboxSeqIdentityTests`) — both already green for Fulfillment.
- **Not guarded by this feature:** the relay family's **code**. The reason is mechanical, not a preference: the canonical `OutboxRelay` names `OrdersDbContext` in its constructor, so a byte-identical copy is impossible without first making the canonical service-neutral — an Orders edit outside this feature's bounded §11 scope, and the natural precondition of the **third** copy. **Decision, inherited from #7's gate (its row 5): feature 19 `billing_credit` owns (a) the service-neutral refactor of the canonical and (b) extending a parity instrument to it.** Every copy made here carries the banner shape that instrument will need (`// COPY OF — src/Orders/Infrastructure/Outbox/<file>.cs`), so the guard can be armed retroactively without re-touching them. Flagged as a conscious deferral, not an oversight.

## 9. Consumers — none, and the idempotent-consumer copy is **not** made

Per `saga.md` §5, Fulfillment consumes **no** fact: `stock.reserve`, `stock.release` and `despatch.create` are all command-driven. The host therefore starts no Kafka consumer; the relay's producer is its only Kafka client, which keeps `FactConsumerConfinementTests` trivially satisfied.

#7 nonetheless copied `idempotent-consumer.ts` and `processed-events.repository.ts` into Fulfillment, to arm its OI12 parity case (vacuous with a single copy) and to give its feature 18 a guarded starting point. **#8 does not**, for two reasons that are specific to this stack and this repository:

1. **#8's guard does not ask for it.** `tests/Orders.UnitTests/IdempotentConsumerParityTests.RequiresACopyOfThePatternFromEveryWriteModelThatConsumesFacts` requires a copy only from a service that has **both** a `processed_events` configuration **and** a Kafka consumer `BackgroundService`. Fulfillment has the first and will never have the second. Case 1 (byte identity across copies) arms naturally at **feature 23 `projector_read_model`**, the first genuine second copy.
2. **C# cannot render #7's honest type.** #7 declared `CONSUMER_NAMES = [] as const`, making `ConsumerName` the bottom type `never` and the copy **uncallable** by construction. C# has no empty-enum bottom type: `enum ConsumerName { }` still has a constructible `default` value, so the copy would be live code with a ledger it must never write to — dead code that *looks* callable, which is worse than no code.

Adding an unused copy to satisfy a guard that does not ask for it is the guard-that-does-not-guard shape wearing the opposite face. **This diverges from #7 and is therefore the first gate row of `progress/spec_fulfillment_stock.md`.**

## 10. Configuration and packages

### 10.1 Settings

| Setting | Default | Note |
|---|---|---|
| `MSSQL_*` (host, port, `MSSQL_DB_FULFILLMENT`, app user/password) | as `.env` | read exactly as `Program.cs` in Orders reads its own |
| `NATS_URL` | `nats://localhost:4222` | the responder's connection |
| `KAFKA_BOOTSTRAP_SERVERS` | `localhost:9092` | the relay's producer |
| `FULFILLMENT_KAFKA_CLIENT_ID` | `otc-fulfillment` | §8.2 |
| `OUTBOX_RELAY_ENABLED`, `OUTBOX_POLL_INTERVAL_MS`, `OUTBOX_BATCH_SIZE`, `OUTBOX_PUBLISH_TIMEOUT_MS` | as Orders | same names, same semantics |
| `FULFILLMENT_MAX_CONCURRENT_REQUESTS` | `32` | §6.2 — must stay below the ADO.NET `Max Pool Size` |

### 10.2 Packages

**No new `PackageVersion`.** `src/Fulfillment/Fulfillment.csproj` adds `ProjectReference` to `src/Cqrs`, and `PackageReference` to `NATS.Net`, `Confluent.Kafka`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Options` and `Microsoft.Extensions.Logging.Abstractions` — all already pinned in `Directory.Packages.props` and already referenced by `src/Orders/Orders.csproj`, each with the same confinement comment. `tests/Fulfillment.UnitTests` is a new project (xunit, runner, coverlet, references `src/Fulfillment`); `tests/Fulfillment.IntegrationTests` gains `Testcontainers` (generic, for Kafka and NATS — `Testcontainers.Kafka` is deliberately not used, see `Directory.Packages.props`), `Confluent.Kafka` and `NATS.Net`. The commit message's package section reads **"none installed; existing pinned packages newly referenced by `src/Fulfillment` and `tests/Fulfillment.*`"**.

### 10.3 The host

`FulfillmentHost.CreateBuilder(args, configure…)` mirrors `OrdersHost.CreateBuilder` exactly, including `ValidateOnBuild = true` / `ValidateScopes = true` forced in **every** environment (review D3 of feature 15 — the defaults are Development-only, which is nowhere this repository actually runs). Registration order: persistence and clock → outbox and relay → messaging and responder options → `AddDispatcher(Assembly.GetExecutingAssembly())` **last**, so a missing port or a missing handler is a boot failure. `Program.cs` is the thin `Host.CreateApplicationBuilder` shim Orders' already is.

## 11. The Orders-side change — bounded to `FS2`

Three production files and their tests, and nothing else:

- `src/Orders/Application/Ports/ISagaCommands.cs` — each of the five methods gains a `SagaCommandMeta meta` parameter (`readonly record struct SagaCommandMeta(UniqueId CorrelationId, UniqueId RequestId)`).
- `src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs` — builds a **fresh** `NatsHeaders` per call (`NATS.Client.Core`'s own XML doc: *"Not thread-safe. Do not share a single `NatsHeaders` instance across concurrent"* calls) with `x-correlation-id` and `x-request-id`, and passes it to the `RequestAsync<TRequest,TReply>(subject, data, headers, …)` overload. The existing `RawRequester` test seam widens by one parameter so the unit test can record what was sent, and the exception taxonomy (`SagaCommandTimeoutError` / `SagaCommandTransportError` / `SagaCommandBusinessRejectionError`) is **not** touched.
- `src/Orders/Infrastructure/Saga/SagaCommandDispatcher.cs` — passes `new SagaCommandMeta(new UniqueId(claimed.OrderId), new UniqueId(claimed.Id))` on **every** attempt of every cycle. The row id is stable across in-line retries and sweeper re-issues, which is exactly `RpcHeaders`' *"a retry after a timeout reuses the same value"*.

`NatsStockAvailabilityChecker` is **untouched**: no order exists at check time, so there is no `x-correlation-id` to send (`FS2`'s last sentence). Orders' stand-in responders in `tests/Orders.IntegrationTests` ignore headers and keep working unchanged.

**Explicitly not touched:** `EfCoreSagaCommandStore` (already idempotent — `requirements.md` §1.1), `SagaFactHandler`, the step table, `PlaceOrderCommandHandler`, and anything belonging to feature 46 (§13).

## 12. First boot against the live compose stack (`FS17`)

**Pre-state, recorded by phase 8:** `otc_orders.saga_commands` holds four `parked` `stock.reserve` rows for `ORD-000007` … `ORD-000010`, each `attempts = 6`, last error *"no responder"*, on the capped park backoff (30 s → 15 min). The sweeper (`OrdersSagaSweeperOptions.IntervalMs` 30 s) re-issues due parked rows indefinitely and never gives up — that is the claim phase 8's design made and could not demonstrate, because nothing could answer.

Orders must be **rebuilt and restarted first**, with §11's headers; without them the first re-issued `stock.reserve` is refused `VALIDATION_FAILED` (`FS3`) which — being terminal (feature 42) — would mark the row `rejected`. That is a genuinely useful negative check but it is **destructive of the observation**, so `tasks.md` orders it deliberately and on a throwaway row, never on the four parked ones.

Expected sequence, **unattended**, once the Fulfillment host is up:

1. Within one sweeper interval of each row's `next_attempt_at`, Orders re-issues `stock.reserve` with `x-correlation-id = <order id>` and `x-request-id = <row id>`.
2. Fulfillment answers. For each order whose lines resolve to seeded stock: `otc_fulfillment.reservations` gains one `reserved` row per line, `stock.reserved_units` rises, `otc_fulfillment.outbox` gains one `stock.reserved.v1` which the relay stamps published within `OUTBOX_POLL_INTERVAL_MS`, and the `accepted` reply marks the `saga_commands` row `sent`. A short line instead yields `stock.rejected.v1` and a `rejected` reply outcome — still `sent` (`SO6`).
3. The orchestrator consumes the fact from `otc.fulfillment.facts.v1`: `orders.status → stock_reserved`, one new `saga_commands` row `credit.hold` → no responder → 3 attempts → **parked**. (On `stock.rejected.v1` instead: → `cancelled`, `order.cancelled.v1` in Orders' outbox, and **no** `stock.release` row — `R26`.)
4. Steady state: four `sent` `stock.reserve` rows, four `parked` `credit.hold` rows, four orders in `stock_reserved` (or `cancelled`), and the reservations visible in one `SELECT`. Billing's arrival (feature 19) repeats the same unattended story one step further.

**A precondition to check before claiming the outcome.** `src/Seed/Domain/Data/StockCatalog` gives every saga-untouched company a row for **every** product, but a saga-covered company only gets rows for the products its fixtures used. `tasks.md` therefore has the implementer first read the four parked rows' payloads and confirm each `(companyCode, productCode)` pair exists in `otc_fulfillment.stock` — and, if one does not, record the `NOT_FOUND` → `rejected` outcome of §4.6 as the observed result rather than reporting a failure.

`orders.create` also becomes genuinely end-to-end for the first time: `stock.check` has had no responder since feature 15, so every acceptance attempt has failed at the availability check. The walkthrough places one fresh order and records it reaching `stock_reserved`.

## 13. The feature 46 seam — named, not absorbed

`src/Orders/Infrastructure/Messaging/NatsStockAvailabilityChecker` deserialises every reply body directly as `StockCheckReplyPayload`, with **no** `RpcError` discriminator (unlike `NatsSagaCommandsAdapter`, which grew one in feature 42). An error reply therefore yields `Lines = null` and the very next line throws a bare `NullReferenceException` out of order acceptance. **This responder is the first thing in the repository that can send such a reply.**

Feature 46 `orders_stock_check_rpc_error_discriminator` owns the fix; this feature must not absorb it. What this feature does instead:

- **`FS22`** keeps the error reply off the ordinary path: an unknown product is `available: 0, sufficient: false`, never an error, so the only `RpcError` `stock.check` can produce is a malformed request or an internal failure.
- `tasks.md` forbids editing `NatsStockAvailabilityChecker` and requires the implementer to record, in `progress/impl_fulfillment_stock.md`, that the seam is now reachable — the hand-over feature 46 starts from.

## 14. Testing approach

| File | Level | Runner / infrastructure | Proves |
|---|---|---|---|
| `tests/Fulfillment.UnitTests/StockItemTests.cs` | domain unit | xUnit, pure | `R30`, `R61` domain half, `FS10`, `FS11`, `FS12` unit half, `FS20` |
| `…/ReservationTests.cs` | domain unit | pure | `R32`, `R33`, `R35` — the matrix's `reservation.spec` cases |
| `…/OrderStockReservationTests.cs` | domain unit | pure | `R34` domain half (the matrix's `reservation-release.spec` cases), `FS8`, `FS13`, F3 across three items, a repeated product on two lines |
| `…/StockReservationHandlerTests.cs`, `StockReplenishServiceTests.cs` | unit, fakes | pure | the `already_reserved` short-circuit (**`FS5`**), reply-after-commit, rollback propagates, `NoKnownStockItemError`, replenish all-or-nothing |
| `…/StockResponderHeaderTests.cs`, `StockRpcErrorMapperTests.cs`, `StockRequestValidatorTests.cs` | unit | pure | `FS3`, **`FS21`**, §6.4's rules |
| `…/StockResponderConcurrencyTests.cs` | unit | pure | `FS18`'s scope-per-request half |
| `…/StockLockOrderTests.cs` | unit | pure | `FS19`'s ordering half |
| `…/StockSubjectsTests.cs`, `FulfillmentFactTopicTests.cs`, `StockRpcPayloadTests.cs` | unit | read `asyncapi.yaml` as text | subjects, topic and payload shapes derived from the spec, never retyped |
| `…/StockClaimProjectionTests.cs` | unit | EF model only | the literal column lists in the locking SQL match the mapped entity types (the E7 instrument) |
| `tests/Fulfillment.IntegrationTests/StockCheckTests.cs` | integration | MsSql + NATS | `R31`, `FS22` |
| `…/StockReserveTests.cs` | integration | MsSql + NATS + Kafka | `R32`/`R33` integration halves, `FS3`, **`FS5`** |
| `…/StockReserveRaceTests.cs` | integration | MsSql + NATS | **`FS6`**, `FS7`, `FS19`'s deadlock-shape half |
| `…/StockReleaseIdempotencyTests.cs` | integration | MsSql + NATS | `R34` integration half, `FS9`, `FS10` |
| `…/StockReplenishTests.cs`, `StockListTests.cs`, `StockWireTests.cs` | integration | MsSql + NATS | `FS14`, `FS15`, `FS4` |
| `…/StockResponderConcurrencyTests.cs` | integration | MsSql + NATS | **`FS18`** — the held-lock proof of §6.2 |
| `…/StockItemRepositoryTests.cs` | integration | MsSql | `FS12` stored equality, `FS19`'s "the idempotency read blocks rather than reading a snapshot" half, rollback leaves neither rows nor outbox |
| `…/FulfillmentOutboxRelayTests.cs` | integration | MsSql + Kafka | `FS16` |
| `tests/Orders.UnitTests/NatsSagaCommandsAdapterTests.cs`, `SagaCommandDispatcherTests.cs` (extended) | unit | pure | `FS2` |

**Fixtures.** `MsSqlContainerFixture`, `NatsContainerFixture` and `KafkaContainerFixture` are copied from `tests/Orders.IntegrationTests` (same pinned images: `nats:2.14.5-alpine`, `apache/kafka:4.3.1`, the MS-SQL tag compose uses) with the same collection-definition shape. A `FulfillmentHostFixture` boots the **real** `FulfillmentHost.CreateBuilder` graph against the containers, so the integration suites exercise the same DI wiring, the same responder and the same options binding the live process uses; callers in the suites are raw `NatsConnection` clients, the production caller's shape.

**The synchronisation rule is binding** (reviewer ruling, feature 16): wait only on **terminal or monotonic** evidence — an outbox row's `published_at` (set once, never cleared), a reservation's terminal status, a count in an append-only table, the `OutboxRelayResult` of a hand-driven `RunOnceAsync()`, or a reply. Never poll `reserved_units` mid-flight. For the race test: `Task.WhenAll` two raw NATS requests for different orders against an item with exactly enough units for one; assert on the **replies** (one `accepted`, one `rejected`), on the **final** `reserved_units`, and on the outbox holding exactly one `stock.reserved.v1` and one `stock.rejected.v1` — all terminal. Repeat on **10 fresh items** so a scheduling fluke is visible rather than lucky.

**Matrix name mapping** (`specs/shared/test-matrix.md` §4's stack-neutral paths → #8):

| Matrix path | #8 file |
|---|---|
| `fulfillment/domain/stock-item.spec` | `tests/Fulfillment.UnitTests/StockItemTests.cs` |
| `fulfillment/domain/reservation.spec` | `tests/Fulfillment.UnitTests/ReservationTests.cs` |
| `fulfillment/domain/reservation-release.spec` | `tests/Fulfillment.UnitTests/OrderStockReservationTests.cs` |
| `fulfillment/domain/stock-replenishment.spec` | `tests/Fulfillment.UnitTests/StockItemTests.cs` (the `R61_…` method) |
| `fulfillment/integration/stock-check.spec` | `tests/Fulfillment.IntegrationTests/StockCheckTests.cs` |
| `fulfillment/integration/stock-release-idempotency.spec` | `tests/Fulfillment.IntegrationTests/StockReleaseIdempotencyTests.cs` |

## 15. Ported-idiom ledger

> Binding since the Phase 8 gate (`CLAUDE.md`, *"The ported-idiom ledger"*). One line per idiom this feature ports: **#7 relied on X; in #8 that property is supplied by Y.** Where the property came free from #7's engine, language or library and must be hand-built here, `tasks.md` names a guard test — the **Guard** column is the contract between the two documents.

| # | Property | #7 got it from | #8 supplies it by | Guard in `tasks.md` |
|---|---|---|---|---|
| L1 | **A blocking, current read** on the rows a decision depends on | `SELECT … FOR UPDATE` under InnoDB `REPEATABLE READ` | Explicit `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` on every decision-bearing read. `READ_COMMITTED_SNAPSHOT` is **ON** here, so an un-hinted read takes **no lock** and returns a row version — the idempotency check would read a stale snapshot and two reserves for one order would both proceed | `FS19` integration: the idempotency read blocks on a concurrent uncommitted reservation insert. Armed by removing the hint |
| L2 | **A deterministic global lock order** across a multi-line order | InnoDB acquires locks in unique-index scan order, so one statement with `ORDER BY (company_code, product_code)` sufficed | One single-row locking statement **per product**, issued in an application-fixed order. MS-SQL gives no guarantee about a multi-row seek's lock-acquisition order, and `ORDER BY` constrains the result, not the plan | `FS19` integration: `[P1,P2]` vs `[P2,P1]` concurrently, 10×, both accepted, no deadlock. Armed by reverting to one multi-row `IN` statement |
| L3 | **Agreement between the application's sort order and the database's row identity** | Both sides used MySQL's own `utf8mb4_0900_ai_ci` ordering | Sorting in the application on the **invariant-uppercased** code with `StringComparer.Ordinal`, distinctness by `OrdinalIgnoreCase`, and §6.4's alphabet validation at the edge. MS-SQL's `SQL_Latin1_General_CP1_CI_AS` resolves differently-cased codes to the same row while an ordinal sort would order them differently — two callers could derive different lock orders for the same rows | `FS19` unit: the ordering is invariant to request order and to letter case |
| L4 | **Counters that cannot wrap** | JavaScript numbers have no narrowing conversion or silent overflow | Explicit range guards in the aggregate (`StockUnitOverflowError`), availability by subtraction, repeated-line sums in `long`. C# `int` arithmetic is **unchecked by default** (`Directory.Build.props` sets no `CheckForOverflowUnderflow`) and both counters are `int` columns — the same shape as the money-truncation defect in `CLAUDE.md`'s ledger table | `FS20` domain unit, two cases. Armed by deleting the guard |
| L5 | **"Insert-or-leave-alone" as one atomic statement** | `INSERT … ON DUPLICATE KEY UPDATE`, where MySQL's statement *is* the unit of atomicity | **Not needed, and deliberately not rendered.** Every row this feature writes was loaded under an exclusive lock in the same transaction, so an `UPDATE` by primary key is exact and no upsert exists to mis-render as check-then-act. Stated because feature 45's defect was exactly a check-then-act rendering of this idiom, and the next reader will look for it here | The absence is the guard: `tasks.md` forbids `IF NOT EXISTS … INSERT` and `MERGE` in this service, and the review is told to grep for both |
| L6 | **Concurrent handling of independent requests** | `@nestjs/microservices`' NATS server ran every request on its own promise chain | A bounded `SemaphoreSlim` + a tracked task + one DI scope per request (§6.2). #8's only responder precedent is **deliberately sequential**, and copying it would make `FS6`'s race test green while proving nothing — two serialised reserves also produce one `accepted` and one `rejected` | **`FS18`** integration (answer a second request while the first is blocked on a held row lock) + unit (a distinct scope per request). Armed by reverting to `await HandleAsync(...)` in the loop |
| L7 | **A retryable failure gets retried** | #7's orchestrator retried **every** `RpcError` code, so any code was safe | A closed mapping (§6.5) in which every transient store failure produces `UNAVAILABLE`/`TIMEOUT`/`INTERNAL_ERROR` and `CONFLICT` is **banned**. #8's feature 42 made nine codes terminal; a deadlock victim answered `CONFLICT` would end the order's saga permanently, while satisfying `R32`'s text exactly | **`FS21`** unit, reading the terminal set from `NatsSagaCommandsAdapter`'s own classification rather than a retyped list. Armed by mapping the deadlock case to `CONFLICT` |
| L8 | **Publication order of facts written in one transaction** | MySQL `AUTO_INCREMENT` assigned in insert order through the ORM's batch | The per-row awaited `INSERT` copied verbatim from `EfCoreOrderRepository.InsertOutboxRowAsync` — EF Core's SQL Server provider does not preserve `Add` order when assigning `IDENTITY` values, measured in feature 14 | Inherited: `OutboxSeqIdentityTests` (phase 6) plus `FS16`. `tasks.md` forbids replacing the loop with `AddRange` |
| L9 | **A bare-JSON request/reply wire** | A hand-written `BareJsonNatsDeserializer`/`Serializer` pair, to defeat the Nest packet — #7's largest gate row on this feature | Nothing: `INatsConnection.SubscribeAsync<byte[]>` + `NatsMsg.ReplyAsync(byte[])` is bare by construction, through the one shared `JsonWire.Options`. A **saving**, recorded as such in the effort record | `FS4` still asserts it, because the wire is a trilogy contract rather than a framework artefact |
| L10 | **An uncallable copy of the consumer pattern** | `CONSUMER_NAMES = [] as const` makes `ConsumerName` the bottom type `never`, so the copy could not be called | Nothing — the copy is **not made** (§9). C# has no empty-enum bottom type, so the equivalent would be live code that looks callable. Gate row 1 | n/a — the divergence is ruled on at the gate, and `IdempotentConsumerParityTests` case 3's predicate keeps the rule honest |
| L11 | **Declarative request validation** | `class-validator` decorators on DTO classes | A hand-rolled `StockRequestValidator` mirroring `OrdersCreateRequestValidator`, with a unit test per rule. Also the place where §4.3's collation residual is closed | `StockRequestValidatorTests`, one case per rule |
| L12 | **One transaction per unit of work without a `tx` parameter** | Drizzle's explicit `TransactionContext` threaded through every repository call | The scoped `DbContext`'s ambient transaction — every collaborator in the same DI scope enlists automatically. A **simplification**, and the reason `IStockItemRepository` has no `tx` parameter | Inherited from feature 14's design; `FS12` integration asserts a forced rollback leaves neither the rows nor the outbox rows |

## 16. Out of scope — restated

- **`despatch.create`** and everything `DespatchAdvice`: feature 18. `Consume()` ships ready, unit-tested, with no caller.
- **Billing**: features 19 – 22. The live stack parks at `credit.hold` by design (§12).
- **The `stock.check` `RpcError` discriminator**: feature 46 (§13).
- **DLQ, metrics, tracing, `traceparent` / `x-deadline-ms`**: feature 27.
- **Gateway callers of `stock.list` / `stock.replenish`**, and `R61`'s API row: feature 25.
- **A code-parity guard over the relay copies**, and the canonical's service-neutral refactor: feature 19 (§8.3).
