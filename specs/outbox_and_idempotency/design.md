# `outbox_and_idempotency` — Design (.NET 10 / C# 14, assessment #8)

> **Where the value of this document is.** `specs/shared/` was inherited verbatim from #7, so the requirements were not written here — the *realisation* was, and none of it exists anywhere else. Which projects, which ports, which EF Core transaction boundary, which `BackgroundService`, which table hints stand in for a clause MS-SQL does not have, which error number means "duplicate", and how a producer proves it puts #7's bytes on the wire.
>
> Authorities: [`specs/shared/requirements.md`](../shared/requirements.md) §2 (`R11` – `R18`), [`specs/shared/domain-model.md`](../shared/domain-model.md) §7.1 (the envelope) and §8 rule 5 (*aggregates emit; infrastructure publishes*), [`specs/shared/saga.md`](../shared/saga.md) §6 (the three idempotency layers) and §7 (failure handling), [`specs/shared/asyncapi.yaml`](../shared/asyncapi.yaml) (topic addresses, partition key, `Envelope`, `FactHeaders`, `DeadLetterHeaders`), the *Order To Cash — Databases* document §4.3 (`outbox` and `processed_events`, column by column), and — **binding on this feature** — [`specs/orders_aggregate/design.md`](../orders_aggregate/design.md) §7.5 (collection and drain), §8 (the persistence mapping) and §10 (the handler and port shapes).

## 0. Scope

| In scope | Out of scope (and who owns it) |
|---|---|
| `src/SharedKernel/` — the envelope interface and its pure validator (`R11`, §4.7) | Anything else in `SharedKernel`; the zero-package rule stands |
| `src/Orders/Application/Ports/` — `IClock`, `IUnitOfWork`, `IFactPublisher`, `IOrderRepository` (final shape), `ConsumerName` | Any port a NATS responder or a saga step needs — features 15, 16 |
| `src/Orders/Infrastructure/Persistence/` — `EfCoreUnitOfWork`, `EfCoreOrderRepository` and the aggregate ↔ row mapper of `orders_aggregate` §8 | The order-number allocator; `orders.create`; the availability check — feature 15 |
| `src/Orders/Infrastructure/Outbox/` — the outbox writer, the relay, the row → envelope mapper, the Kafka producer adapter, the `BackgroundService` | Consumer-side retry, backoff, `<topic>.dlq`, `x-failed-consumer` / `x-attempts` / `x-error` — **`R16`, feature 27** |
| `src/Orders/Infrastructure/Messaging/` — the **canonical** idempotent-consumer pattern and its dedup-ledger writer | The Fulfillment / Billing / Notifications / Projector copies — features 17 – 24, which copy §6 |
| One architecture rule confining the producer client (`OI16`) | Metrics, traces, health checks — feature 27 |
| The Orders DI registration extension, `AddOrdersOutbox` | `Program.cs` itself — feature 15 builds the Orders host (§2.3) |

### 0.1 What this feature designs but does not build

The precedent is `orders_aggregate` §8, which designed the persistence mapping without implementing it so that features 14 and 15 were bound by a contract rather than left to invent one, and which cites #7 drawing the identical line: *"the repository **adapter deliberately not built** — port interface only, adapter deferred"*. This feature owes its successors the same courtesy.

- **The consumer's outer shell.** §6 builds the idempotent-consumer primitive and proves it by calling it directly. The Kafka `BackgroundService` that will call it — consumer group, offset-commit policy, `IEventHandler` dispatch, the DI scope per message — is designed in §6.2 and §6.5 and **built by feature 16**.
- **The retry / dead-letter seam.** §7 states exactly where feature 27 attaches on both the consumer side and the relay side, including the one mechanism (an outbox `attempts` column) that would need a migration, so that feature does not rediscover it.
- **The Fulfillment and Billing relays.** Their `outbox` tables already exist and are already proven identical to Orders' (`OI11`). Their runtime copies of §4 – §5 land with features 17 – 22, from this feature's Orders implementation as the reference.
- **`requestId` idempotency (`R62`).** Not this feature's, and not merely by scope: on MS-SQL it needs a **filtered** unique index (`WHERE request_id IS NOT NULL`), because MS-SQL admits exactly one `NULL` in a unique index where MySQL admits many, and `R62`'s last clause requires `requestId` to stay optional. Already recorded against feature 27.

## 1. Layout

```
src/SharedKernel/
  IDomainEventEnvelope.cs              interface : IDomainEvent — the six envelope fields
  DomainEventEnvelope.cs               static Validate(...) — pure guard, R11
  Errors/ (existing DomainError)       IncompleteDomainEventEnvelopeError

src/Orders/Application/Ports/
  IClock.cs                            DateTimeOffset UtcNow  (orders_aggregate §7.3's port, first used here)
  IUnitOfWork.cs                       ExecuteAsync<T>(Func<CancellationToken, Task<T>>, CancellationToken)
  IOrderRepository.cs                  AddAsync / GetByIdAsync / GetByReferenceAsync / SaveChangesAsync
  IFactPublisher.cs                    PublishAsync(IReadOnlyList<PublishableFact>, CancellationToken)
  PublishableFact.cs                   sealed record (Key, EnvelopeJson, Headers)
  ConsumerName.cs                      the closed set: orders.saga | projector | notifications

src/Orders/Infrastructure/Persistence/
  EfCoreUnitOfWork.cs                  one IDbContextTransaction over the scoped OrdersDbContext
  EfCoreOrderRepository.cs             aggregate <-> rows, and the drain into outbox
  OrderRowMapper.cs                    business codes <-> reference-table ids (orders_aggregate §8.3)

src/Orders/Infrastructure/Outbox/
  OutboxWriter.cs                      IReadOnlyList<IDomainEvent> -> OutboxMessage rows
  OrderFactPayloadMapper.cs            OrderDomainEvent -> OrderToCash.Contracts.Facts.Payloads.*
  OutboxEnvelopeMapper.cs              OutboxMessage row -> Envelope<JsonElement> -> wire bytes
  OutboxRelay.cs                       plain class: RunOnceAsync() = claim -> publish -> stamp
  OutboxRelayBackgroundService.cs      BackgroundService: the poll loop and graceful drain
  OutboxRelayOptions.cs                Enabled, PollIntervalMs, BatchSize, PublishTimeoutMs
  KafkaFactPublisher.cs                Confluent.Kafka producer adapter
  KafkaOptions.cs                      BootstrapServers, ClientId
  OrdersFactTopic.cs                   the one topic constant, guarded by a test against asyncapi.yaml

src/Orders/Infrastructure/Messaging/
  IdempotentConsumer.cs                CANONICAL — RunOnceAsync(eventId, consumer, work)
  ProcessedEventLedger.cs              CANONICAL — the processed_events insert, duplicate detection

src/Orders/Infrastructure/OrdersOutboxServiceCollectionExtensions.cs
                                       AddOrdersOutbox(...) — explicit, one line per port
```

Namespaces mirror the folders: `OrderToCash.Orders.Application.Ports`, `.Infrastructure.Persistence`, `.Infrastructure.Outbox`, `.Infrastructure.Messaging`.

**`src/Orders/Domain/` gains nothing.** Not one file under it changes except `Events/OrderDomainEvent.cs` declaring that it implements the new `SharedKernel` interface — additive, no field, no dependency, no behaviour. That the outbox needs nothing else from the domain is the cleanest evidence that `domain-model.md` §8 rule 5 holds and that `orders_aggregate` §7.5's contract was written correctly.

## 2. Layering, and three shapes that are .NET's rather than #7's

### 2.1 Where the transaction context lives — and why no signature carries one

#7 had to invent an opaque branded `TransactionContext` and thread it through `save(order, tx)`, because Drizzle's `db.transaction(cb)` hands the callback a **different database handle**: two collaborators writing in one transaction had to be given the same handle explicitly, and the type system was the only thing that could stop one of them opening its own.

EF Core has no such problem, and pretending it does would be cargo cult. `OrdersDbContext` is registered **scoped**; `Database.BeginTransactionAsync()` associates the transaction with that context instance; every repository and every ledger resolved from the same scope shares it automatically. The transaction context *is* the `DbContext`.

So `IOrderRepository` keeps exactly the shape `orders_aggregate` §10.2 fixed — `AddAsync`, `GetByIdAsync`, `GetByReferenceAsync`, `SaveChangesAsync`, no `tx` parameter anywhere — and #7's promised revision of its own port has **no counterpart here**. What survives from #7's §4.1 is the part that was about correctness rather than about Drizzle: **the transaction boundary sits above the repository**, in an `IUnitOfWork` port declared in the application layer, because `R17` requires a dedup record, an aggregate change and outbox rows from **two** collaborators inside **one** transaction. A repository that opened its own transaction would make `R17` unsatisfiable, exactly as #7 argued.

**The price, and it is real: scoping is now load-bearing.** Because "same transaction" means "same scope", a singleton `BackgroundService` that processed two messages in one scope would silently put two messages in one transaction. §6.5 states the rule feature 16 inherits: **one DI scope per message, created explicitly, disposed before the next message**. The relay obeys the same rule for its own cycles (§5.4).

### 2.2 The relay's core is a plain class; only the loop is a hosted service

`OutboxRelay` takes its collaborators through its constructor and derives from nothing. `OutboxRelayBackgroundService : BackgroundService` is the thin wrapper that owns the interval, the scope per cycle and the graceful drain. Three reasons, all of them #7's and all of them still true here: `RunOnceAsync()` is directly callable from a test with no host; the seed's integration test can point it at each seeded database to prove there is nothing to publish; and the loop's timing concerns stay out of the code that does the work.

This does not contradict `CLAUDE.md`'s *"one `BackgroundService` per transport"*. That rule exists because #7's hybrid NestJS apps registered a bare `@MessagePattern` on every connected transport at once. The relay is not a transport listener at all — it has no inbound subscription — and it is the only thing this hosted service does.

### 2.3 There is no `Program.cs` yet, and this feature does not write one

Every service in `src/` is a **library**: `Orders.csproj` has no `OutputType`, and no `Program.cs` exists anywhere outside `src/Seed`. #7 did not face this — its Nest application and `app.module.ts` existed from its scaffold feature — so this is a #8 decision, and it is not a close one.

This feature ships `OrdersOutboxServiceCollectionExtensions.AddOrdersOutbox(IServiceCollection, Action<OrdersOutboxOptions>)`: **one explicit registration line per port**, no assembly scan, exactly the shape `CLAUDE.md`'s *"every port is registered explicitly"* rule asks for, with the call site provided by feature 15 when it builds the Orders host for the `orders.create` responder. Building a host here — to run a relay whose only rows would be the seed's already-published ones — would be speculative work in the feature that has the most genuinely new mechanism in it, and it would pre-empt feature 15's startup-validation pass.

Consequence, stated so feature 15 inherits it rather than discovers it: **until feature 15 calls `AddOrdersOutbox`, the relay runs only in tests.** Every requirement below is proven by tests that construct the relay or start the `BackgroundService` directly, which is exactly the *"double force where the branch has no live caller yet"* case `CLAUDE.md`'s arming rule names — §9.4 arms all of them.

## 3. The schema — already landed, and that is this feature's largest single saving

#7's feature 14 opened with a **coordinated three-database migration** (`causation_id`, `seq`, `trace_parent`, `occurred_at` widened to millisecond precision, the poll index added) plus the collateral it broke: three migration round-trip specs, three seed writers, the seed's causal-chain fixture, a database recreation procedure, and a hand-written parity test to keep the three bodies identical. Its own task list gives that work two whole groups (A and B, 13 tasks) and its history entry records `drizzle-kit generate --custom` being forced by MySQL's `ER_WRONG_AUTO_KEY`.

**#8 does none of it.** Every column and index the relay needs already exists, was migrated in phase 6 and seeded in phase 7:

| What #7's feature 14 had to add | State in #8 before this feature starts | Evidence |
|---|---|---|
| `causation_id char(36) NOT NULL` | `causation_id uniqueidentifier NOT NULL` present in all three write models | `src/Orders/Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs` |
| `seq` tie-free insertion sequence | `seq bigint IDENTITY(1,1)` with a unique index, **and proven to increment on a real database** | `tests/Orders.IntegrationTests/OutboxSeqIdentityTests.cs` |
| `trace_parent varchar(64)` NULL | `trace_parent nvarchar(64)` NULL, reserved for feature 27 | same configuration file |
| `occurred_at` at millisecond precision | `datetime2(3)` from the first migration | same configuration file |
| The poll index `(published_at, seq)` | present, **and asserted from `sys.indexes`** | `tests/Orders.IntegrationTests/IndexTests.cs` |
| The lag index `(published_at, occurred_at)` | present, asserted, reserved for feature 27's `R59` | same |
| A hand-written three-way parity test (`OI11`) | already green across **four** contexts, read from `INFORMATION_SCHEMA` on real databases | `tests/Billing.IntegrationTests/ReliabilityTableParityTests.cs` |
| A seeded causal chain | already fabricated, deterministic, and identical to #7's | `src/Seed/Domain/Sagas/SagaFixtures.cs:254-282` |

**No migration is written by this feature, and none may be.** If implementation appears to need a column, that is a design error to bring back here, not a migration to add — the four databases are seeded and the parity test compares them.

**One thing the earlier phases did not carry over, and it matters here.** `infra/mssql/init/01-create-databases.sql` sets `READ_COMMITTED_SNAPSHOT ON` on all four deployed databases; `tests/Orders.IntegrationTests/MsSqlContainerFixture.cs`'s `CreateFreshDatabaseAsync` issues a bare `CREATE DATABASE` and does not. Every concurrency claim this feature makes would otherwise be proven under an isolation configuration the running stack does not use. §9.2 closes it.

## 4. The outbox writer — `R13`, `R12`, `OI1`, `OI9`

### 4.1 `IUnitOfWork`

```csharp
namespace OrderToCash.Orders.Application.Ports;

public interface IUnitOfWork
{
    /// Runs <paramref name="work"/> inside ONE write-model transaction.
    /// Commits if it completes, rolls back if it throws, and never swallows
    /// the exception. The delegate MUST be safe to execute more than once.
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
    Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}
```

`EfCoreUnitOfWork` implements it over the scoped `OrdersDbContext`:

```
strategy = db.Database.CreateExecutionStrategy()
strategy.ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
    var result = await work(ct);
    await tx.CommitAsync(ct);
    return result;
})
```

Three decisions inside those five lines:

1. **Always through `CreateExecutionStrategy()`, even though retries are off.** EF Core throws *"The configured execution strategy does not support user-initiated transactions"* the moment someone enables `EnableRetryOnFailure` on a `DbContext` whose code calls `BeginTransactionAsync` directly. Routing through the strategy now costs nothing (with retries off it is a single pass) and means enabling retries later is a configuration change rather than a rewrite of every transactional path in the service.
2. **Retries are deliberately NOT enabled by this feature.** A retrying strategy re-executes the delegate, and a delegate that mutated an aggregate whose events were already drained would commit aggregate rows with no outbox rows — `OI9`'s hazard, in .NET clothing. The delegate contract above ("safe to execute more than once") is the rule; §4.5 states how the repository makes it true rather than hoping for it. Enabling retries is a later decision that must re-read §4.5 first.
3. **The isolation level is stated, not inherited.** `IsolationLevel.ReadCommitted` explicitly, because §5.2's claim depends on it and because an ambient `TransactionScope` opened by a future caller could otherwise change the level under the relay's feet.

### 4.2 `IOrderRepository` — unchanged from `orders_aggregate` §10.2

```csharp
Task AddAsync(Order order, CancellationToken cancellationToken);
Task<Order?> GetByIdAsync(UniqueId id, CancellationToken cancellationToken);
Task<Order?> GetByReferenceAsync(OrderNumber reference, CancellationToken cancellationToken);
Task SaveChangesAsync(CancellationToken cancellationToken);   // drains and clears
```

No `tx` parameter (§2.1). `AddAsync` registers the aggregate with the repository's identity map and stages its rows; `SaveChangesAsync` drains every registered aggregate's `DomainEvents` into `outbox` rows, calls `DbContext.SaveChangesAsync` once, and only then calls `ClearDomainEvents()` — the order `orders_aggregate` §7.5 point 3 fixes, and it is the order that matters: clearing earlier loses the events if the transaction rolls back.

`orders_aggregate` §10.1's handler sketch annotates `AddAsync` with *"drains DomainEvents into outbox, one transaction"*. That is shorthand for *"the repository, not the handler, does it"* — §10.2 is the precise statement and it names `SaveChangesAsync`. Handler code therefore reads `AddAsync(...)` then `SaveChangesAsync(...)` inside `IUnitOfWork.ExecuteAsync`, and §10.1's fourth property — *"the handler never touches `DomainEvents`"* — holds unchanged.

### 4.3 The adapter lands here — the inherited reversal, argued again rather than assumed

`orders_aggregate` §0 assigns *"the EF Core repository and the code-to-id resolution (§8 is its binding contract)"* to **"features 14 / 15"** and §8 opens *"nothing in this section is implemented by this feature"*. Both leave the choice of which. It is 14, for the three reasons #7 gave when it made the same call at the same point, re-checked against #8:

- Written here, `SaveChangesAsync` is written **once**, with its transaction and its outbox drain, and is testable against `R13` immediately. Deferring it to 15 would create an intermediate version with no outbox that `R13` could not be asserted against.
- The blocking column gap #7 cited was closed in its feature 13; #8's equivalent — `order_items.description`, `orders.cancellation_reason`, the `bigint` money columns — was closed in phases 6 and 44 and is asserted by `SchemaColumnTypeTests` and `NoMoneyColumnIsIntTests`.
- The "it would need Testcontainers in a pure-domain feature" objection was a statement about feature 13's acceptance. This feature is a Testcontainers feature by nature: `R13` cannot be proven without a real transaction against a real store, and `CLAUDE.md` forbids proving it against a mock.

**Bounded.** The adapter implements the four port methods and the row ↔ aggregate mapping of `orders_aggregate` §8, resolving `retailerCode` / `companyCode` / `currency` / `productCode` against the four reference tables **inside the adapter** so the domain never sees a reference-table id (§8.3), reading lines ascending by `id` (§8.4), converting instants per §8.2, and handing `Rehydrate` no totals (§8.3). It does **not** allocate order numbers, does not know what NATS is and does not know what a command is: all of that is feature 15's.

*Insert-or-update semantics.* `SaveChangesAsync` must be safe for both a newly placed order and a reloaded one. EF's change tracker distinguishes them for free when the aggregate was loaded through `GetByIdAsync`; for `AddAsync` the rows are new. The adapter therefore keeps, per aggregate, whether it was added or loaded, rather than probing the database — a probe is a round trip and a race, and the repository already knows.

### 4.4 From domain events to outbox rows

The mapping is `Databases` §4.3's, column by column, and nothing in it is inferred at publication time (`OI1`):

| `outbox` column | Source | Note |
|---|---|---|
| `id` | `Guid.NewGuid()` | The **row** id, deliberately distinct from `event_id`: a row is a publication-attempt record, an event is a domain fact |
| `event_id` | `domainEvent.EventId` | Minted in the domain when the fact became true (`orders_aggregate` §7.1); the column is `UNIQUE`, so a double drain fails loudly instead of duplicating a fact |
| `event_type` | `domainEvent.EventType` | Validated against `^[a-z]+\.[a-z_]+\.v[0-9]+$` **and** against `FactCatalog.PayloadTypesByEventType`'s keys before the row is built (§4.7) |
| `aggregate_id`, `correlation_id`, `causation_id` | the event, verbatim | No transformation, no defaulting. `correlation_id` is the order id by construction (`orders_aggregate` §7.1) |
| `occurred_at` | `domainEvent.OccurredAt.UtcDateTime` | `datetime2(3)`; `orders_aggregate` §8.2's conversion, in the same direction |
| `payload` | `JsonSerializer.Serialize(payload, JsonWire.Options)` | The fact **body** only, from the typed `OrderToCash.Contracts.Facts.Payloads.*` record built by `OrderFactPayloadMapper` |
| `published_at` | `null` | The relay's job (`R14`) |
| `trace_parent` | `null` | Feature 27; the column exists so no fourth coordinated migration is needed |
| `created_at` | `IClock.UtcNow` | Through the port, so tests control time |
| `seq` | *not assigned* | `IDENTITY(1,1)`; EF's `ValueGeneratedOnAdd().UseIdentityColumn(1, 1)` already forbids assigning it |

`OrderFactPayloadMapper` is the **one** place a domain event becomes a `Contracts` payload. `orders_aggregate` §7.2 requires exactly this: the domain events carry domain types (`Money`, `OrderNumber`, `GLN`, `Quantity`) and must never reference `Contracts`, which the `OrdersDomainMustNotDependOnContracts` architecture rule already enforces. The mapper lives in `Infrastructure/Outbox/` and is where `Money.MinorUnits` becomes `long`, `OrderNumber` becomes `string`, `GLN` becomes `string` and `Quantity` becomes `int`. **No `decimal` appears anywhere on this path**, in either direction.

*Ordering inside one transaction.* `DomainEvents` is `IReadOnlyList<IDomainEvent>` in raise order (`orders_aggregate` §7.5 point 2); the writer inserts in that order, so `seq` reflects emission order. That is what will make `asyncapi.yaml`'s promise about `payment.received.v1` preceding `credit.released.v1` true in feature 22 with no extra mechanism.

### 4.5 `OI9` — the drained-events hazard in EF Core

`ClearDomainEvents()` is destructive. If it ran before the commit and the transaction rolled back, the in-memory aggregate would have lost its events and a naive retry on the same instance would commit aggregate rows with **no** outbox rows — a dual write in the other direction, and silent.

Two defences, both cheap:

1. **Order.** `SaveChangesAsync` drains into rows, calls `DbContext.SaveChangesAsync`, and calls `ClearDomainEvents()` only after it returns — `orders_aggregate` §7.5 point 3, followed literally. A rollback therefore leaves the aggregate's events intact.
2. **Rule.** A failed unit of work invalidates the aggregate instances it touched **and the `DbContext` scope that held them**: EF's change tracker retains `Added` entries after a failed `SaveChangesAsync`, and reusing that context would re-attempt the same inserts. A retry re-creates the scope and re-loads or re-creates the aggregate. This is the delegate contract of §4.1 point 1, and #7's review recorded that its equivalent rule was *demonstrated, not guarded* (defect D9). Here it gets a named test (`OI9`) that fails if the clear moves above the save.

The alternative — a non-destructive `UncommittedEvents` getter on `AggregateRoot` with a commit-aware clear — is rejected for #7's reason and one of #8's: `AggregateRoot` is `SharedKernel` code whose semantics feature 7 froze and feature 13 built on, and the fix would push commit-awareness into a base class that must know nothing about stores.

### 4.6 `IClock` lands here

`orders_aggregate` §7.3 says the clock port *"is to be declared in `Orders/Application/Ports/` by feature 15"*. This feature is 14 and needs it first — `created_at`, `published_at` and `processed_at` all come from it — so it lands here, in the location §7.3 specified, with the shape it specified. This is exactly what #7 did (`tasks.md` C1: *"This is the clock port `orders_aggregate` said would live in the application layer; it lands here because the recorder and the relay are its first users"*), and it is an ordering correction, not a design change: the port, its layer and its signature are all unchanged.

```csharp
public interface IClock { DateTimeOffset UtcNow { get; } }
```

The default implementation returns `DateTimeOffset.UtcNow`. `TimeProvider` is deliberately not used as the port type: the domain takes instants as parameters and never reaches for a clock at all (`orders_aggregate` §7.3), so the only consumers are three infrastructure classes, and a one-property interface is testable without a `FakeTimeProvider` package.

### 4.7 `R11` — the envelope guard, and where it lives

`SharedKernel` gains two new files and one new error, and nothing else changes there:

```csharp
public interface IDomainEventEnvelope : IDomainEvent
{
    UniqueId EventId { get; }
    string EventType { get; }
    UniqueId AggregateId { get; }
    UniqueId CorrelationId { get; }
    UniqueId CausationId { get; }
    DateTimeOffset OccurredAt { get; }
}

public static class DomainEventEnvelope
{
    public static void Validate(IDomainEventEnvelope envelope);   // throws IncompleteDomainEventEnvelopeError
}
```

What `Validate` checks, and why each check is not already given by the type system:

- **The four identifiers are non-empty.** `UniqueId` is a value type wrapping a `Guid`; `default` is `Guid.Empty`, which is a perfectly constructible value and would satisfy `R11`'s *"no field absent"* while violating its *"or empty"*.
- **`occurredAt` is not `default`.** Same reason: `default(DateTimeOffset)` is year 1, not "absent".
- **`eventType` is non-empty and matches `^[a-z]+\.[a-z_]+\.v[0-9]+$`** — `R11`'s own pattern, transcribed from `asyncapi.yaml`'s `Envelope.eventType`, not paraphrased. The regex is a compile-time-generated matcher (`[GeneratedRegex]`) so no allocation happens per event.

`payload` is deliberately **not** checked here: `SharedKernel` has zero package references and must not know what JSON is. Its non-emptiness is checked at the writer (§4.4), which owns serialisation, together with the membership check against `FactCatalog.PayloadTypesByEventType` — a catalogued `eventType` with no payload type is a fifteenth fact nobody declared.

`OrderDomainEvent` (feature 13) already declares all six members with exactly these names and types; it gains `: IDomainEventEnvelope` and nothing else. Every existing `tests/Orders.UnitTests` case must stay green, which is the check that the change really was additive.

**The guard is live, not ornamental.** `OutboxWriter` calls `Validate` on every event before it builds a row, so `OI1`'s refusal clause is satisfied at the writer rather than at the relay: an incomplete envelope never reaches storage. That is stricter than `R11`, which speaks about publication — and it is the direction `R11` would want, since a stored incomplete envelope is unrecoverable after the fact.

## 5. The relay — `R14`, `R15`, `OI2` – `OI8`, `OI13` – `OI15`

### 5.1 Shape

```csharp
public sealed record OutboxRelayResult(int Claimed, int Published);

public sealed class OutboxRelay
{
    public OutboxRelay(OrdersDbContext db, IFactPublisher publisher, IClock clock,
                       IOptions<OutboxRelayOptions> options, ILogger<OutboxRelay> logger);

    /// One complete cycle: claim -> publish -> stamp, in one write-model transaction.
    public Task<OutboxRelayResult> RunOnceAsync(CancellationToken cancellationToken);
}
```

One transaction per cycle, three steps inside it, and the transaction is the relay's own (`Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)`), not an `IUnitOfWork` — the relay writes no aggregate and enlists no other collaborator, and §5.2's claim needs the isolation level pinned at the point the hints are taken.

### 5.2 The claim — the one place MS-SQL has no equivalent of #7's clause

#7 claims with `SELECT … FOR UPDATE SKIP LOCKED`. **MS-SQL has no `SKIP LOCKED`.** The translation is a table-hint triple, and it is the item the stack-comparison document has carried open since Phase 1:

```sql
SELECT TOP (@batchSize)
       id, event_id, event_type, aggregate_id, correlation_id, causation_id,
       payload, occurred_at, published_at, created_at, seq, trace_parent
FROM   dbo.outbox WITH (UPDLOCK, READPAST, ROWLOCK)
WHERE  published_at IS NULL
ORDER  BY seq ASC;

-- publish to the fact stream, await acknowledgement  (no SQL, bounded by OI14)

UPDATE dbo.outbox SET published_at = @now WHERE id IN (@ids);
COMMIT;
```

| Part | What it buys | What breaks without it |
|---|---|---|
| `UPDLOCK` | Takes the **update** lock the claim needs and holds it to the end of the transaction, so no second relay can read the same row for update | Two relays both read the same rows and both publish them: a duplicate per row per relay, every cycle |
| `READPAST` | **Skips** rows another transaction already holds instead of waiting for them — the `SKIP LOCKED` behaviour | The second relay *blocks* on the first until it commits, so the "concurrent instances" acceptance bullet becomes serialisation and, under a slow broker, a lock-wait timeout. #7's reviewer mutated its `SKIP LOCKED` away and got exactly that: `ER_LOCK_WAIT_TIMEOUT` |
| `ROWLOCK` | Asks the engine for row granularity, so a claim over a hundred rows does not start life as a page lock | A page-granularity lock makes `READPAST` skip **every** row on a locked page, including rows nobody claimed, so a relay silently defers work it could have done, and — worse — defers it *out of `seq` order relative to another relay's batch* |

Everything below is the part a naive translation gets wrong.

**Row versioning is ON, and it does not weaken this claim — but the reason matters.** `infra/mssql/init/01-create-databases.sql` sets `READ_COMMITTED_SNAPSHOT ON` on all four databases, deliberately, *"the closest MS-SQL gets to the semantics the shared spec was written against"* — MySQL's InnoDB serves consistent reads from MVCC and never blocks a reader on a writer, and without RCSI the same code would block where #7's did not. That file's own comment already anticipates this feature: *"It does NOT weaken the two places that matter, because both take explicit locks rather than relying on the ambient level: the outbox relay claims rows `WITH (UPDLOCK, READPAST, ROWLOCK)`"*. Spelled out:

1. **RCSI is not SNAPSHOT isolation.** RCSI is a database option that changes how the `READ COMMITTED` level behaves — statement-level row versioning instead of shared locks. `SNAPSHOT` is a *session* level a caller must ask for explicitly, and nothing here does.
2. **A statement that takes locks opts out of versioning for the table it hints.** `UPDLOCK` makes this read a locking read: it sees the latest committed row rather than a version snapshot, and it blocks — or, with `READPAST`, skips — where a versioned read would have sailed past. That is precisely what a claim must do: claiming from a snapshot would let a relay claim a row another relay had already stamped in a transaction that committed after the snapshot was taken.
3. **`READPAST` stays legal.** `READPAST` may be specified only in transactions at `READ COMMITTED` or `REPEATABLE READ`. RCSI leaves the level `READ COMMITTED`, so the hint is legal; **`SNAPSHOT` isolation is where it would not be**, which is the second reason §4.1 and §5.1 pin `IsolationLevel.ReadCommitted` explicitly instead of inheriting whatever an ambient `TransactionScope` supplies. A future caller wrapping a relay cycle in a snapshot-isolation scope would turn a working claim into a runtime failure, and pinning the level is what stops that being possible.
4. **Writers are unaffected.** Inserts into `outbox` take their own locks on new rows and never collide with a claim's `UPDLOCK` on existing ones, so the placing transaction of feature 15 never waits behind the relay. This is the property that makes the claim's open transaction (below) tolerable.

**Lock escalation is the failure mode `SKIP LOCKED` does not have.** MySQL has no lock escalation; MS-SQL escalates row locks to a table lock at roughly five thousand locks per statement per object. Three consequences:

- The **claim itself** stays far below the threshold: `BatchSize` defaults to 100, and each claimed row costs a lock on the index row and one on the clustered row — a few hundred, not a few thousand. `ROWLOCK` additionally discourages the engine from starting at page granularity.
- `ROWLOCK` is a **hint, not a guarantee**. The hard switch is `ALTER TABLE dbo.outbox SET (LOCK_ESCALATION = DISABLE)`, which is a **schema change and therefore out of scope** (§3): the four databases are migrated and the parity test compares them. It is recorded here as the escape hatch if load ever demands it, with its cost — a coordinated migration across three write models — so feature 27 does not rediscover it.
- The realistic escalation source is **someone else**: a retention job deleting published rows, or an index rebuild. A statement that escalates to a table lock cannot be skipped by `READPAST` — `READPAST` skips *rows*, and there is no row to skip past when the whole object is locked — so the relay blocks until that statement finishes. Any future retention of the published tail must therefore delete in bounded batches. Recorded now; nothing in this feature deletes anything.

**Ordering, claimed honestly.** `ORDER BY seq ASC` gives one relay a total, tie-free order (`OI2`), and `seq` is never used as a **cursor** (`OI3`): the predicate is `published_at IS NULL`, a nullability test, so a row whose transaction committed late is simply found on the next poll. The self-healing property of the whole design is that sentence. What is **not** claimed, in either assessment: with two relay instances running, `READPAST` (like `SKIP LOCKED`) gives disjoint batches but no relative order **between** them, so two facts about one order could be published out of `seq` order if two relays claim them separately. #7 has the identical exposure and answers it the identical way — `OutboxRelayOptions.Enabled` exists so a scaled-out deployment runs exactly one relay per write model, and `R15`'s per-order ordering is a single-relay guarantee. Saying it here means feature 27 does not have to discover it under load.

**How it is expressed in EF Core.** Table hints are not expressible in LINQ, so the claim is `FromSqlInterpolated` on `DbSet<OutboxMessage>` with `AsNoTracking()`:

- `FromSql` requires **every** mapped column of the entity type in the projection — all twelve are listed above, in the configuration's order, and a missing one is a runtime error, not a compile error. The completeness of that list is asserted by a test that compares it against the `IEntityType`'s property list, so adding a column later cannot silently break the relay.
- `AsNoTracking()` because the relay mutates no entity: the stamp is a set-based `ExecuteUpdateAsync`, which does not go through the change tracker, and mixing the two would leave tracked entities stale.
- `@batchSize` and `@now` are **parameters**, never interpolated text. `FromSqlInterpolated` parameterises them automatically; `FromSqlRaw` with string concatenation would not, and is banned here for that reason.
- The stamp is `db.Outbox.Where(o => ids.Contains(o.Id)).ExecuteUpdateAsync(s => s.SetProperty(o => o.PublishedAt, now), ct)` — one round trip, inside the same transaction, no entities materialised twice.

**The fallback, specified so it is not invented.** The claim needs columns the poll index does not cover, so the plan is an index seek on `(published_at, seq)` plus a key lookup on the clustered index. If the two-lock path is ever observed to block rather than skip — the honest risk of combining `READPAST` with a lookup — the specified remedy is: claim `id` and `seq` only, through the covering index, under the same three hints; then read the twelve columns by `id` **in the same transaction** under `WITH (UPDLOCK, ROWLOCK)` without `READPAST`, which cannot block because every claimer reaches the clustered rows through the index rows this transaction already holds. It is not the default because it is two round trips for a hazard that the `OI4` / `OI13` tests will show or not show. If it is taken, the reason and the observation go in `progress/impl_outbox_and_idempotency.md`.

**And what is not used, deliberately.** No `NOLOCK` anywhere, ever — it would let the relay claim uncommitted rows. No claim columns (`claimed_by` / `claimed_at`): a lease is two more columns across three databases, a tunable that causes **double publication by design** when set shorter than a slow publish, and a stale-lease sweeper; whereas a dropped connection releases MS-SQL's locks immediately and the rows are claimable on the very next poll with no operator action and no wait (`OI5`).

### 5.3 Publishing — `R15`, `OI7`, `OI8`, `OI14`

```csharp
public sealed record PublishableFact(string Key, ReadOnlyMemory<byte> EnvelopeJson,
                                     IReadOnlyDictionary<string, string> Headers);

public interface IFactPublisher
{
    /// Completes only when the broker has acknowledged EVERY fact; throws otherwise.
    /// Never reports partial success.
    Task PublishAsync(IReadOnlyList<PublishableFact> facts, CancellationToken cancellationToken);
}
```

- **One topic per service.** Orders publishes to `otc.orders.facts.v1` — every row in this outbox belongs to it by construction. The constant lives in `OrdersFactTopic` and is guarded by a unit test that reads `specs/shared/asyncapi.yaml` **as text**, extracts the `ordersFacts` channel's `bindings.kafka.topic`, and asserts equality: the same "derive the topic from the spec, never retype it" discipline `infra/kafka/create-topics.sh` already follows, with no YAML parser added to a service.
- **Key = `correlationId`** (`R15`), rendered `Guid.ToString()` — the default `"D"` format, lowercase and hyphenated, which is exactly the shape every golden envelope carries. Kafka's default partitioner hashes the key, so all facts about one order land on one partition and are read in publication order whatever the partition count (the topic is created with six).
- **Idempotent producer** (`OI7`). In `Confluent.Kafka` this is `EnableIdempotence = true`, which makes librdkafka pin `Acks = All`, keep retries effectively unbounded and cap in-flight requests at five **while preserving per-partition order** — that last property is what makes five safe here, and it is the substantive difference from #7, whose kafkajs client pins `maxInFlightRequests = 1` to get the same guarantee. The `OI7` test therefore asserts on the constructed `ProducerConfig` — `EnableIdempotence`, `Acks`, `MessageSendMaxRetries`, `MaxInFlight` — and **not** by mocking a broker. Without idempotence a client-internal retry can both duplicate a record the broker already accepted and reorder a partition, silently breaking `R15` in a way no test of our own code would catch.
- **Headers:** `x-event-type` (mirroring the envelope's `eventType`, so a consumer can filter without deserialising — the cost of topic-per-service) and `content-type: application/json`. **No `traceparent`**: feature 27 owns `R56`/`R57`, the `trace_parent` column is provisioned and written `NULL`, and until then #8 publishes facts without the header `FactHeaders` marks required — a documented, dated gap, exactly as #7 shipped it, never a fabricated header.
- **The acknowledgement point is the stamp point** (`R14`). `ProduceAsync` completes when the broker has acknowledged; the batch awaits all of them; only then does the `UPDATE` run and the transaction commit. If any publish fails, **the transaction is rolled back** — not committed empty — nothing is stamped, and the identical batch is retried, in the same order, on the next poll (`OI8`). #7 shipped the "commit an empty transaction" variant and recorded it as defect D7; `OI14` makes the rollback a named requirement here.
- **The publish is bounded** (`OI14`). A linked `CancellationTokenSource` cancelling after `PublishTimeoutMs` wraps the produce, so the claim transaction — which holds `UPDLOCK`s — cannot stay open indefinitely behind an unreachable broker. #7 parsed this setting and never enforced it (defect D2), which made its own design's boundedness claim false as shipped; here the enforcement has a test, and the timeout failure takes the same path as any other publish failure.
- **Partial success inside a failed batch is possible and is accepted.** This is at-least-once, and de-duplication is the consumer's job (§6). Saying it out loud is the point: `saga.md` §6's three layers exist precisely because this system does not pretend to exactly-once delivery.
- **`Confluent.Kafka` directly, not a wrapper.** The relay needs explicit control of the key, the idempotence flags and the acknowledgement point, and a `Producer` is disposable, so `KafkaFactPublisher` is `IDisposable` with the producer as a field (`CA2213` is an **error** in this repository, so a forgotten dispose fails the build).

### 5.4 The loop — `OI6`, and shutdown

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    if (!options.Enabled) { return; }
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.PollIntervalMs));
    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
        using var scope = scopeFactory.CreateScope();
        var relay = scope.ServiceProvider.GetRequiredService<OutboxRelay>();
        try { await relay.RunOnceAsync(stoppingToken); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* structured log, next tick */ }
    }
}
```

- **`OI6` is satisfied by construction.** One loop, one `await` per cycle: a second cycle cannot begin while the first is in flight, because there is no second caller. This is structurally stronger than #7's self-scheduling `setTimeout` chain, which achieves the same end by discipline; `PeriodicTimer` does not queue missed ticks, so a slow cycle delays the next one rather than stacking. It still gets a named test — a guarantee that costs nothing to assert is not a guarantee that should be argued in prose.
- **A cycle never escapes its scope.** One DI scope per cycle, disposed at the end, so the `OrdersDbContext` and its change tracker are fresh (§2.1's price, paid here).
- **A failed cycle is logged and the loop continues.** The failure is already durable — nothing was stamped — so stopping the relay would only delay recovery. The log line carries the `correlationId` and `eventId` of every affected record (`OI8`), which is the shape `R58` will formalise.
- **Shutdown** is the `stoppingToken`: `WaitForNextTickAsync` returns false and the in-flight cycle observes cancellation at the next await. The host's shutdown timeout bounds it. #7 learned in feature 15 that a missing `enableShutdownHooks()` silently disarmed exactly this drain; in .NET the equivalent risk is a host that does not await `StopAsync`, so the graceful-drain test starts and stops the `BackgroundService` directly rather than trusting a host it does not own.

### 5.5 Wire parity — `OI15`, and exactly what is claimed

Phase 5 proved the **serialiser** against twelve real #7 envelopes. This feature proves the **producer**: aggregate → payload → `outbox` row → relay → real broker.

**The row → envelope mapping.** `OutboxEnvelopeMapper` builds `Envelope<JsonElement>`, where the payload is `JsonDocument.Parse(row.Payload).RootElement` and the six scalar fields come from the row's columns:

- The generic `Envelope<TPayload>` already declares the seven fields in `asyncapi.yaml`'s order and `System.Text.Json` emits properties in declaration order, which is what makes envelope byte-exactness assertable at all. The record's own remarks forbid reordering them; nothing here does.
- `JsonElement` rather than a typed payload, **deliberately**. Serialising a `JsonElement` writes the stored text through unchanged, so what a consumer receives is byte-for-byte what the producing transaction committed. Round-tripping through `FactCatalog`'s typed payload would silently drop any field the C# record does not declare — additive optional fields are explicitly non-breaking in `asyncapi.yaml` — and would make the relay's output depend on a type version rather than on the row.
- `JsonDocument` is `IDisposable` and `CA2213` is an error here: the document is disposed within the mapping, after the bytes are written.
- `occurred_at` is `DateTime` in the row and `DateTimeOffset` on the envelope: `new DateTimeOffset(row.OccurredAt, TimeSpan.Zero)`, never the implicit conversion, which would apply the machine's local offset to a `DateTimeKind.Unspecified` value read back from `datetime2(3)` and publish an instant hours away from the truth. #7 shipped the mirror image of this defect in its own manual-verification steps (D4, a local-time `NOW(3)` publishing two hours into the future) — cheap to inherit as a rule.
- Serialisation is `JsonWire.Options` and nothing else. There is no second options instance in this repository and none may be created.

**Three named assertions, and the boundary between them is the whole point.**

1. **The relay changed nothing** (`OI15`, first case). Insert an `outbox` row whose columns are taken from a committed golden envelope — including its `payload` **text** verbatim — run the relay against a real broker, consume the record, and assert the delivered bytes equal the golden file **byte for byte, payload included**. This is a claim about *pass-through*: the relay neither reorders nor reformats what was stored. It is **not** a claim that #8 independently reproduces MySQL's key ordering, and it must not be written up as one.
2. **The payload is semantically #7's** (`OI15`, second case). Place a real `Order` with the golden `order.placed.v1`'s business values — same `orderReference`, `retailerCode`, `companyCode`, GLNs, currency, `orderDate`, lines, amounts — through the real repository, read the `payload` column back, and assert **semantic** equality with the golden payload: same keys, same values, same types, same casing, key order asserted nowhere. This is achievable exactly because `OrderPlacedPayload` carries no identifiers; every field in it is business data the test can supply. `tests/Contracts.UnitTests/JsonEquivalence.cs` already implements this comparison and is the tool to reuse.
3. **Every published envelope is complete** (`R11`, at the producer). For a fact placed by the aggregate rather than transcribed from a golden file: seven fields, in the declared order, none absent, null or empty, `eventType` matching the pattern, `correlationId` equal to the order id, `causationId` equal to the id supplied to the aggregate method.

The reason the first two are separate assertions rather than one is `CLAUDE.md`'s recorded finding: #7's payload key order is **MySQL's `json`-column normalisation** — key length, then alphabetical — leaking onto its wire because its relay reads the payload back out of that column and republishes it. #8 stores payloads in `nvarchar(max)`, which preserves insertion order; matching #7's bytes there would mean emulating another engine's storage quirk forever, and #9 on PostgreSQL could not do it at all. Key order carries no meaning and nothing downstream reads it.

## 6. The idempotent-consumer pattern — `R17`, `R18`, `OI10`, `OI12`

### 6.1 The shape

```csharp
public enum ConsumptionOutcome { Processed, Duplicate }

public sealed class IdempotentConsumer
{
    /// Runs `work` at most once for (eventId, consumer):
    ///   BEGIN
    ///     INSERT INTO processed_events (id, event_id, consumer, processed_at, created_at)   -- FIRST
    ///     -> unique-index violation ? ROLLBACK and return Duplicate WITHOUT calling `work`  (R18)
    ///     await work(cancellationToken)                                                     -- effects + outbox rows
    ///   COMMIT                                                                              (R17)
    public Task<ConsumptionOutcome> RunOnceAsync(
        Guid eventId, ConsumerName consumer,
        Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}
```

**Insert the dedup record first, then do the work.** Both orders are transactionally equivalent for the sequential case; insert-first is better twice over. It fails fast on the common redelivery path without touching any aggregate, and it takes the unique-index lock **before** the effects, so two concurrent deliveries of the same event serialise: the second blocks on the index until the first commits, then gets the violation and reports `Duplicate` (`OI10`). A read-then-write check (`SELECT`, then `INSERT` if absent) has no such property and would let both deliveries through under `READ COMMITTED` — and under RCSI it is worse, because the `SELECT` reads a version that predates the other transaction's insert. **There is no `SELECT` anywhere in the dedup path.** The unique index is the guarantee.

**Detecting the duplicate, in MS-SQL terms.** `DbContext.SaveChangesAsync` surfaces it as a `DbUpdateException` whose inner exception is a `SqlException`. The number is **2601** — *"cannot insert duplicate key row in object with unique index"* — because `ProcessedEventConfiguration` declares `HasIndex(p => new { p.EventId, p.Consumer }).IsUnique()`, an index rather than a constraint; **2627** is its constraint-shaped sibling and is matched too, so a future change from `HasIndex` to `HasAlternateKey` cannot silently turn a duplicate into an unhandled exception. Anything else propagates unchanged — swallowing a foreign-key or timeout error as "duplicate" would turn a failure into a silent acknowledgement, which is the one outcome `R18` must never produce by accident. This is #7's `ER_DUP_ENTRY` (1062) branch, in MS-SQL's numbers.

**The change tracker is poisoned by the failure, and the code must say so.** After a failed `SaveChangesAsync` the `ProcessedEvent` entry is still `Added`; reusing that context would re-attempt the insert on the next save. The duplicate branch therefore rolls back the transaction, detaches the entry and returns — and, because §2.1 makes the scope the transaction, the caller disposes the scope before the next message anyway. Both defences are stated because either alone would be silent when it failed.

**`ConsumerName` is a closed set** — `orders.saga`, `projector`, `notifications`, the three names `specs/shared/requirements.md`'s Vocabulary section fixes — realised as an enum plus wire tokens plus a `Parse`, the convention `OrderStatuses` and `CancellationReasons` already established in this repository. A typo cannot create a second, silently-empty dedup namespace. The column is `nvarchar(50)`; the longest token is 13 characters.

**What the caller does with the outcome.** `Duplicate` → acknowledge the message, mutate nothing, emit nothing, issue nothing (`R18`). `Processed` → acknowledge. An exception → do **not** acknowledge; this is where feature 27's retry/dead-letter wrapper attaches (§7). This feature returns an outcome and lets the caller decide, because the caller — a Kafka consumer — does not exist yet.

### 6.2 How it composes with a handler's transaction

The handler never opens a transaction; it receives one. A saga step in feature 16 will read:

```csharp
var outcome = await idempotency.RunOnceAsync(envelope.EventId, ConsumerName.OrdersSaga, async ct =>
{
    var order = await orders.GetByIdAsync(orderId, ct);
    order.MarkStockReserved(clock.UtcNow);
    await orders.SaveChangesAsync(ct);          // aggregate rows + outbox rows, same transaction as the dedup row
}, cancellationToken);
```

One transaction contains the dedup record, the aggregate change and the outbox records. That is `R17` literally, and it is why the boundary had to sit above the repository (§2.1). `IdempotentConsumer` opens the transaction through `IUnitOfWork`, so the delegate is inside `ExecuteAsync` and every collaborator resolved from the same scope enlists automatically.

### 6.3 Where this code lives, given that shared runtime code is capped at three projects

`CLAUDE.md` admits `src/SharedKernel`, `src/Contracts` and `src/Cqrs` and nothing else. Five components will eventually need this pattern (Orders, Fulfillment, Billing, Projector, Notifications). The options:

| Option | Verdict |
|---|---|
| Put it in `src/SharedKernel` | **Rejected.** The kernel is dependency-free *domain* code and an architecture test asserts it carries **zero** package references. This class talks to EF Core. |
| Put it in `src/Contracts` | **Rejected.** `Contracts` is the wire contract, versioned by `asyncapi.yaml`. An in-process dedup ledger is not a wire concern, and adding EF Core to it would make every service that reads the wire depend on a store. |
| Put it in `src/Cqrs` | **Rejected.** `Cqrs` is an Application-layer dispatcher and must not reference EF Core; an architecture test already forbids `Domain` reaching it, and widening it into infrastructure would undo the argument that got it accepted. |
| Add a fourth shared project | **Rejected, and this is the one worth arguing.** The `src/Cqrs` precedent does not transfer: that project exists because #7 obtained the capability from a *package* (`@nestjs/cqrs`) and #8 had to hand-roll it, so a home was needed for something #7 never had to home. Here #7 obtained the capability from **its own hand-copied files**, so a shared project would be #8 doing something structurally different from #7 in a feature whose effort is being measured against #7's — the same reason `CLAUDE.md` gives for using the dispatcher in all six services even where it does not earn its keep. |
| **Per-service copies of a small pattern, guarded by a test** | **Chosen.** Two files, against a table the database-per-service rule already duplicates. The duplication is deliberate, bounded and honest: each service owns its own dedup ledger, and nothing links their deployments. |

Only the **Orders** copy is written in this feature, as the canonical reference, with the tests that prove it. Features 17 – 24 copy it. Two seams are recorded now so those features do not re-decide:

- **The Projector (feature 24) has no MS-SQL.** Its dedup ledger is a MongoDB collection with a unique index on `(eventId, consumer)` and a single-document upsert; the *pattern* — record first, effects second, duplicate ⇒ no-op — transfers unchanged, the transaction does not. `R51` already anticipates this.
- **Notifications (feature 23) has a database but nothing transactional to bind to.** `otc_notifications` exists and carries `processed_events` (and no `outbox` — it produces no facts), but sending an email is not transactional. Feature 23 must choose between recording before the send and recording after it, and argue the failure mode it accepts; `R17`'s letter cannot be met by any code that calls an SMTP server. Flagged here, decided there. The *Databases* document already records why that ledger is durable at all: a consumer-group replay once caused a real rate-limit storm in #7.

### 6.4 The parity guard for the copies — `OI12`, and the one thing C# adds

#7's rule was **byte-identity after the banner**, with the banner defined as the file's leading run of contiguous comment lines. C# forces exactly one more normalisation: a copy in another service must declare `namespace OrderToCash.Fulfillment.Infrastructure.Messaging;`, and a namespace declaration names the service by construction. So:

**Two normalised regions, and no third.** The **banner** (the leading run of `///` and `//` lines, up to the first line that is neither) and the **single `namespace` declaration line**. Everything else is compared byte for byte, including whitespace, comments and blank lines.

| Option | Verdict |
|---|---|
| Whole-file byte identity | **Impossible** in C#: the namespace line cannot be identical across services without putting all copies in one namespace, which would defeat the point. |
| Normalise the banner only | **Impossible**, same reason. |
| A forgiving normalisation — strip all comments, rename service tokens, compare syntax trees | **Rejected.** Every normalisation rule is a licence to drift, and the drift it forgives first is the worst kind: a comment that still states a rule the code no longer follows. |
| **Banner + the one namespace line** | **Chosen.** The strictest rule that is satisfiable, and it keeps #7's best property: byte-identity is only achievable if the pattern is genuinely service-agnostic, which turns a property of the test into a constraint on the code. |

**The constraint that places on the two files.** Outside the banner and the namespace line, neither file may contain the tokens `Orders`, `Fulfillment`, `Billing`, `Projector` or `Notifications` in any casing — which means, concretely: `ProcessedEventLedger` may not name `OrdersDbContext`. It takes `DbContext` (the base type) and the `ProcessedEvent` entity type, both of which every write model has under identical names because `OI11` keeps the tables identical. `IdempotentConsumer` takes `IUnitOfWork` and `IClock`, which are per-service files at identical paths. Every `using` must resolve to the same namespace suffix in every service tree: the whitelist is `Microsoft.EntityFrameworkCore`, `Microsoft.Data.SqlClient`, `OrderToCash.SharedKernel`, and the service's own `.Application.Ports` / `.Infrastructure.Persistence.Entities` namespaces matched by suffix rather than by literal text. Prose that must name a service belongs in the banner.

*One consequence worth stating.* `ProcessedEventLedger` taking `DbContext` rather than `OrdersDbContext` means it cannot use the typed `DbSet` property; it uses `db.Set<ProcessedEvent>()`. That is a small cost and it is exactly the cost that makes the file adoptable verbatim.

**Where the test lives: beside the canonical copy.** `tests/Orders.UnitTests/IdempotentConsumerParityTests.cs`, not in a project that spans services. #7 argued this and the argument holds unchanged: the pattern spans five components, two of which no cross-cutting project has any relationship with, and the honest home for a guard is next to the thing it certifies, so the developer editing the canonical file gets the red test in the project they are editing. It reads the other copies as **text** through `System.IO`, resolving the repository root the way `tests/Contracts.UnitTests/RepositoryPaths.cs` and `tests/Architecture.Tests/RepositoryPaths.cs` already do, so it creates no build dependency, adds no package, boots no container and runs in the ordinary `dotnet test` pass.

**Four cases, of which two are meaningful at n = 1.** A parity test over a set of one is vacuous, so the shape is #7's — and this is the portable instrument its history entry names as the thing worth copying:

| # | Case | Set it ranges over | State today (one copy) |
|---|---|---|---|
| 1 | *holds every write model's copy byte-identical to the canonical, after the banner and the namespace line* | every `src/*` that has **both** a `processed_events` EF configuration and an `Infrastructure/Messaging/IdempotentConsumer.cs` | one member; asserts the canonical equals itself and **says so in its own failure message**. Arms at the second copy (feature 17) |
| 2 | *keeps the canonical adoptable verbatim, naming no service and referencing nothing service-specific* | the canonical pair alone | **fully meaningful now.** The anti-vacuity case: it fails the day someone writes `OrdersDbContext` into the pattern |
| 3 | *requires a copy of the pattern from every write model that consumes facts* | every `src/*` carrying a `processed_events` configuration | **fully meaningful now**, and self-arming: a service with the ledger **and** a Kafka consumer `BackgroundService` must carry the copy. Today no service has a consumer, so the case asserts a computed empty set; it turns red the moment feature 16 or 17 adds one without the pattern |
| 4 | *requires a documented divergence banner from a copy that cannot share the canonical's transaction* | any `IdempotentConsumer.cs` in a service **without** a relational `processed_events` configuration | dormant. Arms at features 23/24. A variant is **never** compared to the canonical — it cannot be — but its banner must cite the canonical path and carry a line beginning `Divergence:` |

The discriminator between "copy" and "variant" is *"does this service own a relational `processed_events` table"*, read from the filesystem — never a hand-maintained list of service names, because a registry someone must remember to edit is a registry someone forgets, and the drift then hides in the very file meant to reveal it.

### 6.5 The consumer shell — designed here, built by feature 16

Stated so feature 16 inherits it:

- **One DI scope per message.** A `BackgroundService` is a singleton; the `OrdersDbContext` is scoped. Creating one scope per received message and disposing it before the next is what makes "same transaction" mean what §2.1 says it means. Two messages in one scope would silently share a transaction and a change tracker.
- **Acknowledge after the transaction commits, never before.** With `Confluent.Kafka` that means manual offset handling (`EnableAutoCommit = false`, `StoreOffset` / `Commit` after the outcome), because an auto-committed offset can advance past a message whose transaction later failed.
- **The outcome, not an exception, is the normal signal.** `Duplicate` and `Processed` both acknowledge; an exception does not, and feature 27 wraps that point (§7).

## 7. Retry and DLQ — precisely what this feature does and does not do

**Does:**

- Retries **publication** indefinitely by construction: an unstamped record is retried on every poll until the broker accepts it (`R14`, `OI8`). There is no attempt counter and no give-up on the relay side.
- Bounds each publish attempt (`OI14`) so an unreachable broker cannot hold a claim transaction — and its update locks — open.
- Logs every publication failure as a structured line carrying `correlationId` and `eventId`, the shape `R58` will formalise.
- Returns `Processed` / `Duplicate` from the consumer primitive and lets an exception propagate, so the caller decides whether to acknowledge.

**Does not** — all of it `R16` and `R56` – `R59`, owned by feature 27:

- No consumer-side retry, no backoff, no attempt counting, no `<topic>.dlq` publication, no `x-failed-consumer` / `x-attempts` / `x-error` headers.
- No metrics. Outbox lag and dead-letter depth are `R59`; the `(published_at, occurred_at)` index that lag query needs already exists and this feature leaves it untouched.
- No trace propagation. The `trace_parent` column is provisioned and written `NULL`.

**The seam feature 27 attaches to**, so it is not re-derived:

1. **Consumer side:** wrap `IdempotentConsumer.RunOnceAsync(...)`. An exception is already the failure signal; feature 27 adds attempts, backoff and the dead-letter publication around it and only then acknowledges. Nothing in §6 changes.
2. **Relay side:** a permanently unpublishable record — a payload the broker rejects outright — blocks its batch and therefore its write model's outbox forever, because `seq` ordering retries the head first every time. That is the **correct** default (a fact must not be skipped or reordered, `OI8`) and it is loud (lag climbs, the error log repeats). If feature 27 chooses to bound it, the mechanism is an `attempts` column and an outbox dead-letter path — **a coordinated migration across three write models plus a re-run of `OI11`**, which is why it is written down here rather than discovered then.
3. **Headers:** the publisher already builds a header dictionary; feature 27 injects `traceparent` / `tracestate` into it and populates `trace_parent` at record time.

## 8. Configuration and packages

| Setting | Default | Bound from |
|---|---|---|
| `Outbox:RelayEnabled` | `true` | `OutboxRelayOptions`; env `Outbox__RelayEnabled`. Exists so a scaled-out deployment runs exactly one relay per write model (§5.2) |
| `Outbox:PollIntervalMs` | `250` | Small enough that the demo feels immediate, large enough not to hammer the database when idle — #7's number, kept so the benchmark compares like with like |
| `Outbox:BatchSize` | `100` | Bounds how long the claim transaction stays open and how many locks it holds (§5.2) |
| `Outbox:PublishTimeoutMs` | `5000` | The acknowledgement budget, enforced (`OI14`) |
| `Kafka:BootstrapServers` | `localhost:9092` | `kafka:29092` inside compose; `KAFKA_INTERNAL_HOST` / `KAFKA_HOST_PORT` in `.env` stay the source of truth for the broker itself |
| `Kafka:ClientId` | `otc-orders` | |

`specs/shared/requirements.md` §10 leaves these numbers explicitly to each assessment; this table is #8's answer and it is #7's numbers.

`AddOrdersOutbox` takes an `Action<OrdersOutboxOptions>` rather than an `IConfiguration`, so this feature adds no configuration-binding package and feature 15's `Program.cs` does the binding where the configuration root actually exists.

**New packages** — every one appears in this feature's commit message, per `CLAUDE.md`:

| Package | Where | Purpose |
|---|---|---|
| `Confluent.Kafka` | `Directory.Packages.props` **already pins 2.15.0**; add the `PackageReference` to `src/Orders/Orders.csproj` | The relay's producer |
| `Microsoft.Extensions.Hosting.Abstractions` | new `PackageVersion` (10.0.11, the band every other Microsoft.Extensions package here uses) + reference in Orders | `BackgroundService`, `IHostedService` |
| `Microsoft.Extensions.Options` | new `PackageVersion` (10.0.11) + reference in Orders | `IOptions<T>` and `services.Configure<T>` — referenced explicitly rather than relied on transitively through EF Core |
| `Microsoft.Extensions.Logging.Abstractions` | new `PackageVersion` (10.0.11) + reference in Orders | `ILogger<T>` on the relay and the publisher, same reason |
| `Testcontainers.Kafka` | new `PackageVersion` (4.14.0, the band `Testcontainers.MsSql`/`MongoDb` already use) + reference in `tests/Orders.IntegrationTests` | Real Kafka in the relay tests — **only if it can drive `apache/kafka:4.3.1`** (§9.3). If the generic fallback is taken, this package is **not** installed: #7 shipped it unused and recorded that as defect D3 |

## 9. Testing approach

### 9.1 Levels and files

`quality.sh` runs `dotnet test` over the whole solution, so integration tests run in the ordinary pass; there is no separate Docker-only lane to opt out of.

| File | Level | Infrastructure | Proves |
|---|---|---|---|
| `tests/SharedKernel.UnitTests/DomainEventEnvelopeTests.cs` | domain unit | none | **R11** (the matrix's `shared-kernel/domain/event-envelope.spec` row) |
| `tests/Orders.UnitTests/OutboxRelayLoopTests.cs` | unit | none | **OI6** |
| `tests/Orders.UnitTests/KafkaFactPublisherConfigTests.cs` | unit | none | **OI7** — asserted on the constructed `ProducerConfig`, never by mocking a broker |
| `tests/Orders.UnitTests/OrdersFactTopicTests.cs` | unit | none | the topic constant equals the `ordersFacts` channel binding in `asyncapi.yaml` |
| `tests/Orders.UnitTests/IdempotentConsumerParityTests.cs` | unit | none | **OI12**, four cases (§6.4) |
| `tests/Orders.IntegrationTests/OutboxAtomicityTests.cs` | integration | MS-SQL | **R13**, **OI9** |
| `tests/Orders.IntegrationTests/OutboxEnvelopeTests.cs` | integration | MS-SQL | **R12**, **OI1** |
| `tests/Orders.IntegrationTests/OutboxRelayTests.cs` | integration | MS-SQL + Kafka | **R14**, **OI2**, **OI3**, **OI8**, **OI14** |
| `tests/Orders.IntegrationTests/FactPartitioningTests.cs` | integration | MS-SQL + Kafka | **R15** |
| `tests/Orders.IntegrationTests/OutboxWireParityTests.cs` | integration | MS-SQL + Kafka | **OI15** |
| `tests/Orders.IntegrationTests/OutboxRelayConcurrencyTests.cs` | integration | MS-SQL (fake publisher — the point is the claim, not the broker) | **OI4**, **OI5**, **OI13** |
| `tests/Orders.IntegrationTests/IdempotentConsumerTests.cs` | integration | MS-SQL | **R17**, **R18**, **OI10** |
| `tests/Architecture.Tests/FactPublisherConfinementTests.cs` | architecture | none | **OI16** |
| `tests/Seed.IntegrationTests/SeedIntegrationTests.cs` (extended) | integration | the existing MS-SQL + Mongo fixture | the relay finds **zero** unpublished records in the seeded databases |

The matrix's stack-neutral paths map as follows, and the implementer writes these into column 5 of `specs/shared/test-matrix.md`:

| `test-matrix.md` path | #8 file |
|---|---|
| `shared-kernel/domain/event-envelope.spec` | `tests/SharedKernel.UnitTests/DomainEventEnvelopeTests.cs` |
| `orders/integration/outbox-envelope.spec` | `tests/Orders.IntegrationTests/OutboxEnvelopeTests.cs` |
| `orders/integration/outbox-atomicity.spec` | `tests/Orders.IntegrationTests/OutboxAtomicityTests.cs` |
| `orders/integration/outbox-relay.spec` | `tests/Orders.IntegrationTests/OutboxRelayTests.cs` |
| `orders/integration/fact-partitioning.spec` | `tests/Orders.IntegrationTests/FactPartitioningTests.cs` |
| `orders/integration/idempotent-consumer.spec` | `tests/Orders.IntegrationTests/IdempotentConsumerTests.cs` |

Method names follow the convention feature 13 established and column 5 already records: `R<n>_<Subject>_<PascalCaseDescription>` for shared rows, `OI<n>_<Subject>_<PascalCaseDescription>` for local ones. The names in `requirements.md` §3 are the contract.

### 9.2 The isolation-level gap in the existing fixture, and why it is a blocker for this feature

`infra/mssql/init/01-create-databases.sql` sets `READ_COMMITTED_SNAPSHOT ON` on all four deployed databases. `tests/Orders.IntegrationTests/MsSqlContainerFixture.cs`'s `CreateFreshDatabaseAsync` issues a bare `CREATE DATABASE` and leaves it **off**.

For every earlier feature that was harmless — they asserted schema, not concurrency. For this one it is not: `OI4`, `OI5`, `OI10` and `OI13` are all statements about locking behaviour, and proving them on a database whose isolation configuration differs from production is proving them about a system nobody runs. So the fixture sets it, in the same statement shape the init script uses, and a named case asserts `DATABASEPROPERTYEX(DB_NAME(), 'IsReadCommittedSnapshotOn') = 1` on a fixture-created database — because a fixture change with no assertion is exactly the guard-that-does-not-guard this repository keeps finding.

The same change is applied to the three sibling fixtures (`Fulfillment`, `Billing`, `Notifications`) only if it can be done without touching their assertions; if it cannot, the Orders fixture changes alone and the others are recorded as a follow-up, not silently diverged.

### 9.3 Real Kafka, for the first time in this repository

The image is `apache/kafka:4.3.1` — **the same pinned tag as `docker-compose.infra.yml`**, following the convention `db_orders` set for MS-SQL. Rules for the implementer:

- Never a different image and never a floating tag. Try `Testcontainers.Kafka`'s `KafkaBuilder` with that tag first; if it cannot drive it (it targets the Confluent image), fall back to the generic `ContainerBuilder` with **that same tag** and explicit KRaft environment mirroring the compose service — never `confluentinc/cp-kafka`, never `latest`. #7 hit exactly this and took the fallback; its own design named the fallback in advance, which is why it was a deviation and not a surprise.
- **If the fallback is taken, do not install `Testcontainers.Kafka`.** #7 installed it, never imported it, and its reviewer recorded that as a defect.
- Create the topic **explicitly** through `IAdminClient.CreateTopicsAsync` with **6 partitions, replication factor 1** — the numbers `infra/kafka/create-topics.sh` uses. The broker is configured with `KAFKA_AUTO_CREATE_TOPICS_ENABLE: "false"`, and relying on auto-creation would in any case yield one partition and make the `R15` partitioning test vacuous.
- A cold image pull is slow. Raise the xUnit timeout for the Kafka collection with a comment saying why; never `Skip`.

### 9.4 Arming — this feature is the hard case, twice over

`CLAUDE.md`: *"Every branch that emits — or deliberately suppresses — a domain fact must be guarded by a test that fails when the emission is deleted ... with double force where the branch has no live caller yet."*

There is no host, no responder and no consumer (§2.3), so the only thing that executes any of this before feature 15 is the test written beside it. **Nine branches must be armed**, and the protocol is `CLAUDE.md`'s in full: back up the file **by copy** (never `git checkout --`, these files are untracked while the feature is in flight), introduce the violation, run the specific named test, record the failure message **verbatim**, restore from the backup, **force the rebuild** (`touch` the restored file or `dotnet build --no-incremental`), re-read the changed line to confirm, then run green. An arming table produced without the forced rebuild proves nothing about the code on disk.

| # | Branch | Arm by | Must fail |
|---|---|---|---|
| 1 | the drain writes one outbox row per domain event | delete the loop body in `OutboxWriter` | `OutboxAtomicityTests` (R13) |
| 2 | the drain happens **inside** the transaction | move `SaveChangesAsync` for outbox rows after `CommitAsync` | `OutboxAtomicityTests` (R13) |
| 3 | `ClearDomainEvents()` is called **after** the save | move it above `DbContext.SaveChangesAsync` | `OutboxAtomicityTests` (OI9) |
| 4 | the envelope guard runs before a row is built | delete the `Validate` call | `OutboxEnvelopeTests` (R11/OI1) |
| 5 | the stamp happens **after** the acknowledgement | move the `ExecuteUpdateAsync` above the publish | `OutboxRelayTests` (R14, OI8) |
| 6 | the claim skips rows another relay holds | delete `READPAST` from the hint | `OutboxRelayConcurrencyTests` (OI13) — and record whether it fails by **blocking to a lock timeout** or by duplicate publication, because those are different diagnoses |
| 7 | the claim orders by `seq` | change `ORDER BY seq` to `ORDER BY occurred_at` | `OutboxRelayTests` (OI2) |
| 8 | the dedup insert precedes the work and shares its transaction | commit the dedup record in its own transaction ahead of the work | `IdempotentConsumerTests` (R17) |
| 9 | the producer is idempotent | set `EnableIdempotence = false` | `KafkaFactPublisherConfigTests` (OI7) |

Row 8 deserves its own note: #7's reviewer found that **its `R17` matrix-named case survived exactly this mutation** and only a sibling case caught it (defect D10). So the `R17` case here must assert the *joint* outcome — that a failure inside `work` leaves **no** dedup row — and not merely that a dedup row exists after a success.

All nine rows, with verbatim failure messages, go in `progress/impl_outbox_and_idempotency.md`.

### 9.5 Two mutations the reviewer should expect to be asked for

Recorded so the implementer builds tests that survive them: **stamp before acknowledgement** (row 5) and **claim without `READPAST`** (row 6) are the two mutations #7's reviewer used to kill this feature's equivalent, and the second is the one whose .NET behaviour is genuinely unknown until it is run.

## 10. Architecture rules added

One new rule in `tests/Architecture.Tests/`, joining the twelve already armed there:

**`OnlyTheOutboxAdapterMayReferenceTheFactStreamProducerClient`** (`OI16`) — no type outside the `OrderToCash.*.Infrastructure.Outbox` namespace may depend on `Confluent.Kafka`. That is `R14`'s last sentence — *"No command handler, aggregate or domain service publishes directly"* — expressed as something that fails the build. It is scoped by namespace rather than by service so that features 17 – 22 inherit it the moment they add their own relays, and it is armed like any other rule: add a `using Confluent.Kafka;` to an Orders application type, watch it fail, record the message, restore, force the rebuild, re-run.

The existing rules constrain this feature and none of them may be relaxed: `Domain` may not reference EF Core, Kafka, NATS, MongoDB, ASP.NET Core or `System.Text.Json`; `OrdersDomainMustNotDependOnContracts` keeps the payload mapper in infrastructure where §4.4 puts it; `SharedKernelHasNoPackagesTests` means §4.7's guard must be pure C# with no package; and no `decimal` may appear in a domain namespace, which the payload mapper respects by construction (`Money.MinorUnits` is `long` and every money column is `bigint`).

## 11. Explicit non-goals

- **No migration and no schema change of any kind** (§3). If one seems necessary, that is a design error to bring back here.
- **No `Program.cs`, no host, no startup validation pass** — feature 15 (§2.3).
- **No NATS**: no client, no subject, no responder — feature 15.
- **No saga orchestrator and no Kafka consumer** — feature 16. This feature builds the *primitive* a consumer uses and proves it by calling it directly.
- **No `R16`**: no retry, no backoff, no `<topic>.dlq`, no dead-letter headers — feature 27, deferral argued in `requirements.md` §1.2 and pending ratification at the gate.
- **No metrics, no traces, no health checks** — feature 27. `trace_parent` is written `NULL` and `traceparent` is not emitted; a fabricated header would be worse than a documented gap.
- **No `requestId` idempotency (`R62`)** — feature 27, and it needs a **filtered** unique index on MS-SQL because MS-SQL admits one `NULL` in a unique index where MySQL admits many.
- **No Fulfillment, Billing, Notifications or Projector code.** Their copies of §4 – §6 land with features 17 – 24.
- **No change to the `Order` aggregate's behaviour.** The only edit under `src/Orders/Domain/` is `OrderDomainEvent` declaring an interface it already satisfies member for member.
- **No amendment, rewording or reinterpretation of anything under `specs/shared/`**, and no edit to `specs/orders_aggregate/`, which is gate-approved and binding on this feature.
