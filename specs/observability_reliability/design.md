# `observability_reliability` — Design (assessment #8, .NET 10 / EF Core / MS-SQL / Kafka / NATS)

> Realises [`requirements.md`](./requirements.md)'s shared `R56`–`R60`, `R62`, `R16` and `R29`'s dead-letter clause, plus local `OR1`–`OR7` and `RI1`–`RI5`, in this assessment's stack.
>
> **§10 is the ported-idiom ledger and is the section a reviewer should read first.** It is the only place that answers, boundary by boundary, *what made this correct in #7, and does that thing exist here?* **§11 is the ported-guard enumeration** — #7 wrote roughly 145 assertions for these mechanisms and every one is classified there as ported, deliberately not ported, or not applicable. Both are binding, per `CLAUDE.md`.
>
> #7's counterpart of this feature is the largest in its project: one spec pass, one gate, **six implementer passes**, one review. §1.3 sequences #8's equivalent into five passes and says what each may touch.

---

## 1. The two halves, the seam, and the sequencing

### 1.1 What each half touches

| | Half B — `requestId` replay (`RI1`–`RI5`, `R62`) | Half A — reliability and observability (`OR1`–`OR7`, `R16`, `R29`'s DLQ clause, `R56`–`R60`) |
|---|---|---|
| Services | `Orders` only | all six |
| Source files | `PlaceOrderCommandHandler`, `PlaceOrderCommand`, `IOrderRepository` + its EF adapter, `Order` entity + `OrderConfiguration`, one migration | the three fact consumers, `SagaCommandDispatcher`/`EfCoreSagaCommandStore`, every `*Host`, `Order` (one new method), `OrderFactPayloadMapper`, three `OutboxRelay` copies, three RPC clients, three RPC responders, six new health surfaces |
| Shares with the other half | **nothing** — no file, no call path, no table other than `orders`/`saga_commands` being in the same migration | |

They are one `feature_list.json` entry because a human folded them together in #7 on 2026-08-26 and **#8 inherits that decision**. Mechanically they are independent and could be implemented in either order by different passes without a merge conflict.

### 1.2 Do dead-lettering and request-id dedup share a mechanism? No

| | Dead-lettering (`OR1`–`OR3`) | Request-id replay (`RI1`–`RI5`) |
|---|---|---|
| What is retried | the **same** message, by the transport or by this feature's own in-line loop | a **client's** retry of a call whose outcome it could not observe |
| What proves "already done" | `processed_events` (`R17`/`R18`, unchanged) | a **new** column — `processed_events` keys on `eventId`, which a fresh client retry never repeats |
| What a repeat produces | acknowledge and do nothing (`R18`) | the **original reply** (`R62`) — a caller is waiting synchronously; "do nothing" is not an answer |
| What is being guarded | a **poison** message that can never succeed; the guard bounds retry | a **healthy** request repeated after an ambiguous outcome; the guard deduplicates and retry is unbounded and client-driven |

`IdempotentConsumer` is therefore **not** reused for `RI1`–`RI3`. They are sequenced together only because of the human decision above.

### 1.3 Sequencing — the recommendation, and the reason (#7's open point 7, genuinely #8's to choose)

**Recommendation: one `tasks.md`, five groups, in this order — B, then A1 (dead-letter), A2 (first-park hook), A3 (telemetry: trace + logs + metrics), A4 (health).** Each group is a coherent stopping point with its own green suite; a pass may end at any group boundary.

The reason the order is *this* order rather than #7's plain "B then A":

1. **B first**, exactly as #7 chose: it is the smallest, most self-contained half, it is the one with a hard concurrency claim, and finishing it de-risks the larger half. It also unblocks nothing in A, so a failure there costs nothing.
2. **A1 before A2.** `OR3`'s first-park hook publishes to a `.dlq` topic through the *same* `IDeadLetterPublisher` port `OR1` defines (§3.3). Building A2 first would mean either inventing a second publisher or building A1's port with no consumer of it. #7 hit the same ordering and recorded it (its `A3c` had to reference "the `DlqPublisher` A4 builds", i.e. a later task).
3. **A3 before A4.** Health is the only group that adds a new *host surface* to five services (§8.1). Doing it last means the telemetry wiring — which touches the same `*Host.CreateBuilder` methods — has already settled, so the health work merges into one composition change per service rather than two.
4. **A3 is one group, not three**, even though it closes `R57`, `R58` and `R59`. All three read from the same `Activity`/`ActivitySource` plumbing; splitting them produces two passes that each half-wire the SDK. #7 split them (A5/A6/A7) and paid for it: its `A6a`/`A6c` were blocked on A5 and left `TODO` across two passes.

---

## 2. Half B — `orders.create` `requestId` idempotent replay

### 2.1 Schema, and the one place MS-SQL is not MySQL

`src/Orders/Infrastructure/Persistence/Entities/Order.cs` gains one property; `OrderConfiguration` gains the column and the index:

```csharp
public Guid? RequestId { get; set; }          // request_id uniqueidentifier NULL
```

```csharp
builder.Property(o => o.RequestId).HasColumnName("request_id");
builder.HasIndex(o => o.RequestId)
       .IsUnique()
       .HasDatabaseName("uq_orders_request_id");
```

**#7 relied on a property of MySQL that MS-SQL does not have.** `apps/orders/src/infrastructure/persistence/schema/orders.schema.ts:31-37` says so in its own comment — *"MySQL's UNIQUE on a nullable column admits any number of NULLs (the 'requestId omitted' case, RI4) while still admitting at most one row per non-null value"* — and `apps/orders/drizzle/0005_sticky_goblin_queen.sql:5` renders it as a plain `ADD CONSTRAINT ... UNIQUE(request_id)`. **In MS-SQL a `UNIQUE` constraint or index treats two `NULL`s as equal and admits exactly one of them**, so the literal translation of #7's line makes the *second* order that omits `requestId` fail — a total break of `RI4` and of every existing acceptance path, since almost no order supplies one.

The mechanism that restores the property is a **filtered** unique index, `CREATE UNIQUE INDEX ... WHERE [request_id] IS NOT NULL`. EF Core's SQL Server provider generates that filter **by convention** for a unique index over a nullable column, so the code above is expected to be sufficient — **and that is a claim about the provider, not a fact this design asserts.** `tasks.md` B1 requires the generated migration to be **read** and the `filter:` argument quoted into `progress/impl_observability_reliability.md`; if it is absent, `.HasFilter("[request_id] IS NOT NULL")` is added explicitly. Ledger row **L1**, guard `RI4`'s integration case, which places **two** orders with no `requestId` and requires both to commit.

### 2.2 The port

`IOrderRepository` gains one member:

```csharp
Task<Order?> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);
```

The EF adapter implements it as a **no-tracking** read that does **not** enter `EfCoreOrderRepository`'s `_tracked` dictionary. That is deliberate and load-bearing: on the `RI3` path the same scoped `OrdersDbContext` has just failed a `SaveChangesAsync`, and a re-read that registered the winner as a tracked aggregate would make any later save in that scope try to write it again. Ledger row **L4**.

### 2.3 The handler

```csharp
// RI2 — the fast path, BEFORE reference-data resolution and the stock check.
if (command.RequestId is { } requestId)
{
    var existing = await orders.FindByRequestIdAsync(requestId, cancellationToken);
    if (existing is not null) { return ToResult(existing); }
}

// ... reference data, stock check, unchanged ...

try
{
    return await unitOfWork.ExecuteAsync(async ct => { /* allocate, Place, Add, SaveChanges */ }, cancellationToken);
}
catch (DbUpdateException ex) when (command.RequestId is not null && RequestIdCollision.Matches(ex))
{
    // RI3 — the transaction has ALREADY rolled back (see §2.4). Re-read the winner.
    db.ChangeTracker.Clear();
    var winner = await orders.FindByRequestIdAsync(command.RequestId.Value, cancellationToken);
    return winner is not null ? ToResult(winner) : throw ex;   // never a silent null
}
```

### 2.4 The race, resolved for MS-SQL — and the trap #7 did not have

**What actually happens when two requests share a not-yet-committed `requestId`.** Both pass `RI2` (neither is committed). Both open a transaction. Both call `EfCoreOrderNumberAllocator.AllocateNextAsync` **first**, which takes a row lock on the single `order_number_sequences` counter — so they serialise there. The winner inserts and commits, releasing both the counter lock and the index lock. The loser then allocates, inserts, and SQL Server raises **error 2601** (duplicate key on a unique *index*; 2627 is the unique-*constraint* form, and both are handled because a future `HasFilter`/constraint change would swap which one is raised). The loser's transaction is **not** doomed by that error under the default `XACT_ABORT OFF`, but this design does not rely on that: the exception propagates out of `unitOfWork.ExecuteAsync`, which rolls back and disposes the transaction, and only then is it caught.

**The trap is why the catch must be OUTSIDE the unit of work, and it is #8-specific.** `EfCoreOrderRepository.SaveChangesAsync` writes the `order.placed.v1` **outbox row first**, with its own awaited raw `INSERT`, and only then calls `db.SaveChangesAsync()` for the aggregate rows (`src/Orders/Infrastructure/Persistence/EfCoreOrderRepository.cs:64-112`, and the comment there explains why). So at the moment the duplicate-key error is raised, an outbox row for an order that will never exist **is already in the transaction**. #7 caught the collision *inside* its transaction and committed (`apps/orders/src/application/place-order.handler.ts:159-163`) — harmless there, because its outbox write happened after the failing insert. Doing the same here would commit a fact for a non-existent order and the relay would publish it. Ledger row **L2**; the guard is `RI3`'s integration case asserting the `outbox` table holds **exactly one** `order.placed.v1` after the race, not merely that one order exists.

**Distinguishing which index collided.** `Microsoft.Data.SqlClient` exposes no structured index name; the name appears only in `SqlException.Message` (*"Cannot insert duplicate key row in object 'dbo.orders' with unique index 'uq_orders_request_id'"*). `RequestIdCollision.Matches` therefore checks `ex.InnerException is SqlException { Number: 2601 or 2627 }` **and** that the message contains the literal `uq_orders_request_id`. A collision on `order_reference`'s own unique index must propagate unchanged. Ledger row **L3**; because the message shape is an engine claim, `tasks.md` B4 requires the message to be captured from a **real** `mssql` container and quoted in the implementation record — a hand-built `SqlException` in a unit test proves nothing about it, and `SqlException` cannot be constructed by hand anyway.

**The counter lock is not the mechanism, and must not be mistaken for one.** The serialisation described above is incidental: it exists only because both requests allocate a number inside their transaction. The unique index is the correctness mechanism. This is stated because #7 stated it (its design.md §3.3's "Why the counter-row lock does not already solve this for free"), and because `RI2`'s fast path runs entirely *before* any lock is taken, so two requests can and do both pass it.

### 2.5 `RI5` — `causationId`

#7 seeds `causationId` from `requestId` when it parses (`apps/orders/src/application/place-order.handler.ts:143` calling `:205-211`). #8 currently mints a fresh id unconditionally (`src/Orders/Application/Commands/PlaceOrderCommandHandler.cs:88`). This design **adopts #7's behaviour**, because the `Envelope.causationId` contract in `asyncapi.yaml` is *"the eventId of the fact — or the id of the command — that caused this one"*, and when a client supplies an idempotency key that key **is** the command's id. It is a behaviour change to already-shipped code, so it carries its own local requirement (`RI5`) and its own task rather than riding along inside `RI1`.

---

## 3. Half A, group A1 — the retry-then-dead-letter wrapper (`OR1`, `OR2`, `R16`)

### 3.1 The seam: exactly one call site per consumer, and what it must not swallow

All three consumers have the identical shape (`SagaFactsConsumer`, `ProjectorFactsConsumer`, `NotificationFactsConsumer`): parse and validate the envelope inside a `try` that logs-and-acknowledges; then route; then deserialise the payload; then dispatch. The wrapper goes around **the payload deserialisation and everything after it** — i.e. the tail of `HandleMessageAsync` — and around nothing before it:

| Branch | Inside the wrapper? | Why |
|---|---|---|
| Envelope deserialisation / seven-field validation failure | **No** | already log-and-acknowledge; a producer bug is not fixable by redelivery, and there is no trustworthy `eventId` to dead-letter under |
| `eventType` not in `FactCatalog` (unrouted) | **No** | already acknowledged at WARNING; a future fact is not a failure |
| Orders' `_selfProducedFacts` skip (`SO2`) | **No** | not processing at all |
| Projector's `UnknownFactTypeError` (`PR4`) | **Caught *inside* the wrapped delegate** | it is a deliberate acknowledge, not a failure; letting it reach the retry loop would dead-letter a fact the projector is *specified* to ignore. #7 hit this exactly and fixed it the same way (`apps/projector/src/presentation/projector-facts.controller.spec.ts`, *"PR4's UnknownFactTypeError is swallowed INSIDE process, never reaching the retry dispatcher's error path"*) |
| Payload deserialisation, dispatch, handler, aggregate, store | **Yes** | this is `R16`'s population |

### 3.2 The canonical file

`src/Orders/Infrastructure/Messaging/FactRetryDispatcher.cs` — canonical, copied **verbatim after its banner** into `src/Projector/Infrastructure/Messaging/` and `src/Notifications/Infrastructure/Messaging/`, exactly as `IdempotentConsumer.cs` already is.

```csharp
public sealed class FactRetryDispatcher(
    IClock clock,
    IFactRetryDelay delay,                 // a port, so unit tests run instantly — the ISagaRetryDelay shape
    IDeadLetterPublisher deadLetters,
    IOptions<FactRetryOptions> options,
    ILogger<FactRetryDispatcher> logger)
{
    public async Task DispatchAsync(
        string sourceTopic,
        FactStreamMessage message,          // the raw bytes, unmodified — never a re-serialised envelope
        Guid eventId,
        string eventType,
        Guid correlationId,
        ConsumerName consumer,
        Func<CancellationToken, Task> process,
        CancellationToken cancellationToken)
}
```

The loop: `for attempt = 1..MaxAttempts`, `await process(ct)` and **return** on success; on exception record it and, if another attempt remains, `await delay.DelayAsync(BackoffMs << (attempt - 1), ct)`. After exhaustion, publish the dead letter and **return normally** — never rethrow, because the caller returning normally is exactly what lets `KafkaFactStreamSubscriber` reach `consumer.StoreOffset(...)`. That non-rethrow is the whole of `R16`'s "acknowledge the original fact so the partition is not blocked" in #8, and it is a countable claim: ledger row **L12**, guarded by the per-service integration case that asserts the **committed offset read back from the broker** has advanced, and that the *next, distinct* fact on the same partition processes.

`OperationCanceledException` on `cancellationToken` is **rethrown unconditionally** and never retried or dead-lettered: a host shutdown is not a poison message. Ledger row **L13**.

### 3.3 `IDeadLetterPublisher`, and where the Kafka producer is allowed to live

Port in each service's `Application/Ports/`; one implementation per service in **`Infrastructure/Outbox/KafkaDeadLetterPublisher.cs`**, publishing `message.Value` **byte-for-byte unmodified** to `$"{sourceTopic}.dlq"` with the `DeadLetterHeaders` set: `x-failed-consumer` (`ConsumerNames.ToToken(consumer)`), `x-attempts`, `x-error`, `x-original-topic`, `x-first-failed-at`, `x-failed-at`, `x-event-type`, and `traceparent` injected from the ambient context (§5).

`tests/Architecture.Tests/FactPublisherConfinementTests.cs` confines `Confluent.Kafka`'s four producer types to `*.Infrastructure.Outbox`. `Projector` and `Notifications` own no outbox. Two options were considered: put the file in an `Infrastructure/Outbox/` folder in services that have no outbox, or widen the guard's namespace pattern. **This design widens the pattern** to `\.Infrastructure\.(Outbox|Messaging\.DeadLetter)(\.|$)` and puts the three copies in `Infrastructure/Messaging/DeadLetter/`, because a folder named `Outbox` in a service with no outbox is a lie that the next reader has to disprove. The widening is narrow, is justified by what `R14` actually protects (*"no command handler, aggregate or domain service **publishes** directly"* — a dead-letter republication is neither), and `tasks.md` A1e requires the widened rule to be **re-armed**: a `ProducerBuilder<string, byte[]>` reference added under `Application/` must still fail it. An unarmed widening of a confinement rule is how a guard stops guarding.

### 3.4 `OR2` — the parity guard, and the filter it must not use

`tests/Orders.UnitTests/FactRetryDispatcherParityTests.cs`, modelled on `IdempotentConsumerParityTests`: byte-identity after the banner and the single `namespace` line; the canonical names no service and imports nothing service-specific; **and the copy is required from every service that owns a fact consumer.**

The discovery predicate for "owns a fact consumer" must **not** be "has a `FactRetryDispatcher.cs`" — that is the self-selecting filter `CLAUDE.md` names, and a service that dropped the file would drop out of the population instead of failing. It is *"has a `Presentation/*FactsConsumer.cs`"*, and the test additionally asserts the discovered set is **exactly** `["Notifications", "Orders", "Projector"]`, sorted — a literal expected set, with the rest derived by subtraction. Ledger row **L14**.

### 3.5 Configuration

`FACT_RETRY_MAX_ATTEMPTS` (default `3`) and `FACT_RETRY_BACKOFF_MS` (default `500`), read in each service's `*ProgramConfiguration` alongside the existing reads, per `composition_root_env_reads_are_unguarded`'s convention that the delegate a test calls is the delegate `Program.cs` calls.

---

## 4. Half A, group A2 — the first-park hook (`OR3`, `R29`'s dead-letter clause)

### 4.1 `saga_commands` gains three columns

| Column | Type | Why it cannot be derived later |
|---|---|---|
| `triggering_event_envelope` | `nvarchar(max)` NULL | the **unmodified envelope bytes** of the fact that owed the command, captured at enqueue time. Re-reading it from Kafka later is impossible — the offset is long committed and the retention window is not a contract |
| `triggering_event_topic` | `nvarchar(64)` NULL | the source topic, so the `.dlq` name is `topic + ".dlq"` and never guessed from the `eventType` |
| `dead_lettered_at` | `datetime2(3)` NULL | `OR3`'s at-most-once marker |

Nullable, because every row already in the table predates them. `SagaCommandRecord` gains the first two so a claim hands the dispatcher everything it needs with no second read.

### 4.2 Threading the envelope

`SagaFactsConsumer` already holds the raw `FactStreamMessage` and the topic; `SagaFactHandler` already builds the `SagaFact`. Both are extended to carry the raw bytes and the topic through to `ISagaCommandStore.EnqueueAsync`, **captured verbatim** — no re-serialisation anywhere on the path, because a re-serialised envelope is not what `asyncapi.yaml`'s DLQ channel promises (*"the payload is the unmodified original envelope … a redrive is a byte-for-byte republish"*). Ledger row **L15**, guarded by an integration assertion comparing the `.dlq` message bytes to the bytes originally produced, with `Assert.Equal` over the arrays.

### 4.3 The at-most-once claim — one statement, not check-then-act

```csharp
// ISagaCommandStore.TryClaimDeadLetterAsync — returns true iff THIS caller won.
var affected = await db.SagaCommands
    .Where(c => c.Id == commandId && c.DeadLetteredAt == null)
    .ExecuteUpdateAsync(s => s.SetProperty(c => c.DeadLetteredAt, now), ct);
return affected == 1;
```

**One `UPDATE ... WHERE dead_lettered_at IS NULL`, whose affected-row count is the answer** — never `SELECT` then `INSERT`/`UPDATE`. `CLAUDE.md`'s ledger section records a defect of exactly the check-then-act shape shipped earlier in this repository; this is the same hazard and the same fix. Ledger row **L16**, guarded by a unit case over a store fake *and* by the integration case that forces a **second** park of the same row and asserts nothing further is emitted.

### 4.4 What is transactional, and what is not

`SagaCommandDispatcher`'s exhaustion path becomes:

1. `store.ParkAsync(...)` — unchanged (`SO5`, including the capped-backoff `next_attempt_at`).
2. **In one transaction:** `TryClaimDeadLetterAsync`; if it won, load the `Order`, call `order.RecordSagaFailure(...)`, `SaveChangesAsync` — which writes the `order.saga_failed.v1` outbox row through the existing writer. Claim and fact commit together or not at all.
3. **After that commit,** publish the triggering envelope to `<topic>.dlq` through the same `IDeadLetterPublisher` §3.3 defines.

Step 3 is deliberately outside the transaction and is the one non-atomic step: Kafka cannot enlist in an MS-SQL transaction. If the process dies between 2 and 3 the `.dlq` copy is lost while the timeline entry survives — the reverse of #7's window, and the better direction, because the timeline entry is what `R29` actually requires and the `.dlq` copy is diagnostic (`asyncapi.yaml` calls the fact *"purely diagnostic"* in the same words). Ledger row **L17**; stated, not hidden.

`Order.RecordSagaFailure(SagaCommandKind command, int attempts, string lastError, DateTimeOffset occurredAt, UniqueId causationId)` raises **one** `OrderSagaFailed` domain event and mutates **no** other field — no status, no lines, no totals, no `UpdatedAt`. It does not go through `TransitionTo`. `OrderFactPayloadMapper` gains the arm mapping it to the `OrderSagaFailedPayload` already present in `src/Contracts`. `FactCatalog` already carries `order.saga_failed.v1`, the projector already renders it (`Summaries.OrderSagaFailed`), the saga already skips it (`SagaStepTable`), and `NotificationFactsConsumer` already ignores it — **so no "thirteen → fourteen" sweep is owed in #8.** #7's `A1a`/`A1b` were the cost of minting the fact; #8 inherited it already registered.

---

## 5. Half A, group A3, part 1 — trace propagation (`OR4`, `R57`, `R56`'s mechanism half)

### 5.1 The SDK, per service

One `Infrastructure/Observability/Telemetry.cs` per service, registered from `*Host.CreateBuilder` **before** anything else:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName: "orders"))   // OTel service.name — OR5's only per-service discriminator
    .WithTracing(t => t.AddSource(OtcActivity.SourceName)
                       .AddAspNetCoreInstrumentation()             // Gateway only
                       .AddOtlpExporter())
    .WithMetrics(m => m.AddMeter(OtcMetrics.MeterName)
                       .AddOtlpExporter());
```

`OtcActivity.SourceName` is one constant in each service (`"OrderToCash.<Service>"`). `AddSource` is **exact-match**: a source whose name is not registered produces `Activity` objects that are never sampled and never exported, silently. Ledger row **L20**, guarded by an assertion that every `ActivitySource` constructed under `src/` appears in the `AddSource` list of its own host.

`OTEL_EXPORTER_OTLP_ENDPOINT` defaults to `http://localhost:4317` (services run on the host against `docker-compose.infra.yml`'s mapped ports, as every other default in `.env.example` already assumes).

### 5.2 NATS — inject on request, extract on reply-side

Three outbound sites (`Gateway/Infrastructure/Messaging/NatsRpcClient.cs`, `Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs`, `Orders/Infrastructure/Messaging/NatsStockAvailabilityChecker.cs`) each already build a **fresh** `NatsHeaders` per call; each gains `traceparent`/`tracestate` beside the existing `x-correlation-id`/`x-request-id`. **A fresh instance per call is not optional** — `NatsHeaders` is documented not thread-safe and `projector_read_model`'s ledger row `L39` already records it. Ledger row **L21**: the trace fields must be added to the *existing per-call construction*, never hoisted into a shared field as a "cheap" optimisation.

Three responder classes (`Orders/Presentation/OrdersCreateResponder.cs`, `Fulfillment/Presentation/StockRpcResponder.cs`, `Billing/Presentation/BillingRpcResponder.cs`) serve **fifteen** subjects between them — 3 + 6 + 6, enumerated in `RpcSubjects`, `StockSubjects`, `CreditSubjects`/`InvoiceSubjects`. Each responder has exactly **one** per-message body (`SubscribeLoopAsync`), so extraction is added at three sites and covers all fifteen. `tasks.md` A3c makes both the 3 and the 15 countable claims and requires the subject list to be read from the constants, never re-typed.

### 5.3 Kafka — the outbox is the carrier

- **Write time.** `OutboxWriter.BuildRows` currently sets `TraceParent = null` with a comment naming this feature. It now sets `Activity.Current` rendered as a W3C `traceparent`, or **`null` when there is no active span** — never a fabricated one.
- **Publish time.** `OutboxRelay` restores the stored parent, starts its **own child publish span** under it, and injects **that span's** context as the `traceparent` header (`OutboxRelay.cs:149-158`, whose comment already says *"No `traceparent` — feature 27's gap"*). The consumed message therefore extracts to the publish span, whose trace id equals the writing command's. This is exactly #7's shape (`apps/orders/src/infrastructure/observability/trace-context.spec.ts`, *"a manual 'publish' child span started under a restored context keeps the SAME traceId but mints a FRESH spanId"*).
- **Consume time.** Each consumer extracts from the inbound Kafka headers and wraps **the whole `FactRetryDispatcher.DispatchAsync` call** — so every retry attempt and the eventual dead-letter publication stay on the same trace. Ledger row **L22**; guarded by the case that asserts all N attempts and the DLQ publish observe one trace id.

All three `OutboxRelay.cs` copies (`Orders`, `Fulfillment`, `Billing`) carry the identical change; they are `// COPY OF —` files and drifting one is how a copy stops being a copy.

### 5.4 The write-database hop, and why no instrumentation package

Acceptance bullet 1 requires the trace to span *HTTP → NATS → the write database → Kafka → consumers*. `OpenTelemetry.Instrumentation.EntityFrameworkCore` is **1.15.0-beta.1**; a prerelease dependency for one span is a poor trade. Instead `EfCoreUnitOfWork.ExecuteAsync` starts one `Activity` named `writemodel.transaction` with `db.system=mssql` and `db.name` attributes, in `Orders`, `Fulfillment` and `Billing` — the three services on the saga's write path. That is a *better* hop than an auto-instrumented statement span for this purpose: it is the **transaction**, which is the unit `R56` actually names (*"every write-model transaction"*). Ledger row **L23**.

### 5.5 What "continue" means, and the only guard shape that proves it

A test that asserts "a `traceparent` header is present" proves nothing — a fresh span produces one too. Every continuation assertion in this feature **extracts the header back and compares the trace id to the real id of the originating span**, and asserts the span id is *different* (a continuation, not a copy). #7 learnt this and says so in its own case titles (*"not merely 'a header is present'"*). Ledger row **L24**.

---

## 6. Half A, group A3, part 2 — structured logging (`OR7`, `R58`)

Three settings that must all agree, in every service's host:

1. `builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole(o => { o.IncludeScopes = true; o.UseUtcTimestamp = true; });`
2. `builder.Logging.Configure(o => o.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);`
3. a `correlationId` **logging scope** pushed at each unit-of-work entry point.

**Any one of the three missing silently removes a field from every line** — the console formatter drops scopes when `IncludeScopes` is false, and activity tracking is off by default. That is the ledger's own failure shape (present and correct on the path a deletion probe takes), so **L25** names it and the guard captures **real emitted JSON** from a configured host and asserts the fields on **every** record of one flow, never on a formatter configuration object.

The five scope-push sites, one per kind of unit of work:

| Site | Scope value |
|---|---|
| `Gateway` — a middleware immediately after `CorrelationIdMiddleware` | the request-scoped id already in `HttpContext.Items` |
| each RPC responder's per-message body | `x-correlation-id` from the request headers |
| each fact consumer's per-message body | `envelope.correlationId` |
| `SagaCommandDispatcher` / `SagaCommandSweeper` | the row's `order_id` |
| `OutboxRelay` per claimed row | the row's `correlation_id` |

**Where no id exists, no field is invented.** The malformed-envelope branch has no trustworthy `correlationId` and logs the trace id alone; the sweeper's pre-claim failure log has no row yet. Both are #7's own documented exceptions (`review_observability_reliability.md`, Probe 5) and both are ported as exceptions, named in `tasks.md`.

**`#7`'s open point 6 does not exist in #8 — verified, and the real invariant is the ordering.** `ProblemJsonMiddleware.cs:37-39` reads the request-scoped id out of `HttpContext.Items` and mints a Guid only as a fallback, and `GatewayHost.Configure` registers `CorrelationIdMiddleware` **before** it (`GatewayHost.cs:87-88`), so on a real request the fallback is unreachable. What is unguarded is the **order**: swapping those two lines compiles, passes every existing test, and silently makes every error-path line carry a different id from the rest of its request. `tasks.md` A3f adds that guard, armed by swapping the two `UseMiddleware` lines.

---

## 7. Half A, group A3, part 3 — metrics (`OR5`, `R59`)

One `Meter` per service (`System.Diagnostics.Metrics`, in the framework — no package). Instrument names are **#7's, verbatim**, so one Grafana dashboard reads either assessment:

| Instrument | Kind | Where recorded | The observation that makes the test real |
|---|---|---|---|
| `otc_request_latency_ms` | histogram | Gateway, one middleware, tagged by endpoint | measured on the **error** path too, not only on `200` |
| `otc_fact_processing_latency_ms` | histogram | `FactRetryDispatcher.DispatchAsync` entry→exit, tagged by consumer | recorded on the **exhausted-retry/DLQ** path as well as on success |
| `otc_saga_completion_ms` | histogram | `SagaFactHandler`, when a transition lands the order on `completed`/`cancelled`, measured from the order's own `order_date` | an **exact, independently computable** value, tagged `outcome=completed|cancelled`, one instrument not two |
| `otc_outbox_lag_ms` | observable gauge | `OutboxRelay`, age of the oldest row with `published_at IS NULL`, via the existing `(published_at, seq)` index | asserted at an exact value against a **clock-aged** real row, then at `0` after the relay drains |
| `otc_dlq_depth` | observable gauge | `OutboxRelay`'s own cycle, one `AdminClient`/watermark query per `.dlq` topic | asserted against the **broker's** reported count, never a locally kept tally; a non-existent topic reports `0` and never throws |

`AddOtlpExporter()` only; **no** `OpenTelemetry.Exporter.Prometheus.AspNetCore`, and no `/metrics` endpoint anywhere — `OR5`. `infra/prometheus/prometheus.yml` is already in its post-decision state (it carries #7's removal of the stale per-application block in its own header comment, lines 12-21) and **is not edited by this feature**.

---

## 8. Half A, group A4 — liveness and readiness (`OR6`, `R60`)

### 8.1 The hosting decision, and why it is not a gate question

`openapi.yaml` publishes `GET /health/live` and `GET /health/ready`, and `R60` says *"per service"*. #7 gave each of its six services an HTTP port for exactly this (`apps/orders/src/presentation/health.controller.ts`'s own banner: *"this service's HTTP port exists purely for health/metrics"*). So *that* every #8 service serves HTTP health is inherited, not decided.

What is #8's is **how**, because five of its services are console generic hosts with no ASP.NET Core at all. Two shapes were considered:

- **Convert each `*Host.CreateBuilder` to `WebApplication.CreateBuilder`.** Changes the public return type of five composition-root methods and every test that calls them, for two endpoints.
- **A `HealthProbeService : IHostedService` per service** that builds a minimal `WebApplication` inside `StartAsync`, mapping only the two paths, with the readiness checks handed to it from the outer container. Adds `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (no NuGet package, no `Directory.Packages.props` entry) and touches no existing signature.

**This design takes the second.** The Gateway, which already is a `WebApplication`, simply maps the two endpoints into its existing pipeline — deliberately **before** `BearerAuthenticationMiddleware`'s protection, matching `openapi.yaml`'s `security: []` on both paths, and covered by the existing route sweep's literal public set.

Ports, following `GATEWAY_PORT=3001`: `ORDERS_HEALTH_PORT=3002`, `FULFILLMENT_HEALTH_PORT=3003`, `BILLING_HEALTH_PORT=3004`, `NOTIFICATIONS_HEALTH_PORT=3005`, `PROJECTOR_HEALTH_PORT=3006` — the same allocation #7 used. **These five names are a sibling family**, so `tasks.md` A4b requires the *substitution* mutation: repoint one service's read at another's variable and confirm a test fails naming the wrong port, not merely that a port was read.

### 8.2 Checks per service

| Service | `writeModel` | `factStream` | `rpcTransport` | `readModel` |
|---|---|---|---|---|
| Gateway | — | — | NATS | MongoDB |
| Orders | MS-SQL | Kafka | NATS | — |
| Fulfillment | MS-SQL | — | NATS | — |
| Billing | MS-SQL | — | NATS | — |
| Notifications | MS-SQL | Kafka | — | — |
| Projector | — | Kafka | NATS (it publishes update signals) | MongoDB |

Check names are `openapi.yaml`'s `HealthResponse.checks` keys and are #7's own (`writeModel`, `factStream`, `rpcTransport`, `readModel`). `/health/live` always answers `200 {"status":"up"}` and consults **nothing**. `/health/ready` runs every check, answers `200` when all are `up` and `503` otherwise, and the body names the failing check.

### 8.3 Every probe is bounded, and this is the row #7 got wrong

Each probe is a real call — `SELECT 1`, an `AdminClient` metadata request, a NATS `PingAsync`/RTT, a Mongo `ping` command — with an **explicit** timeout, and nothing is cached.

**A stalled-but-open socket is the case that matters, and it is not the same as a closed one.** A `docker pause`d broker leaves the TCP connection open, so `IsClosed` stays false and the write succeeds while no reply ever arrives; the client's own stale-connection detection is minutes away. #7 shipped its Gateway NATS probe without a wrapper — found and disclosed by #7's own **implementer**, who hit the hang against a paused broker while building the other services' probes and widened those copies with an explicit `withTimeout(2000ms)` "the Gateway's own copy does not have" (`order-to-cash-nestjs/progress/impl_observability_reliability.md:912`, mechanism at `:927`, section A8 at `:879`); #7's reviewer then confirmed the already-disclosed gap as a minor finding (`review_observability_reliability.md:70-72`, finding 1). *(Corrected at #8's feature-27 review round 1, R2: this sentence originally said #7's reviewer found it.)* The fix comment is in `apps/gateway/src/infrastructure/health/nats-health-check.ts:1-21`. #8 builds all six with the bound from the start, plus `tests/Architecture.Tests/HealthProbeTimeoutTests.cs` enumerating every `IHealthCheck` implementation under `src/` and asserting each names a timeout — an **enumeration**, with the expected set derived by subtraction, not a prose claim. Ledger row **L26**.

The Kafka probe uses a **dedicated** short-timeout, zero-retry admin client, never the long-lived relay producer whose retry policy would make one "down" observation take many seconds. Ledger row **L27**.

---

## 9. Packages, configuration, migrations

### 9.1 New packages — every one must appear in the phase's commit message

| Package | Purpose | Version rule |
|---|---|---|
| `OpenTelemetry` | the SDK — `TracerProvider`/`MeterProvider`, `AddSource`, `AddMeter` | pin to the same version as the already-pinned `OpenTelemetry.Extensions.Hosting` (**1.18.0**) |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | spans and metrics → `otel-collector:4317` over OTLP/gRPC | same band; **if the highest available exporter version is below 1.18.0, move the whole OTel set down to that version and record the resolved version in the implementation record** — a mixed band is a runtime `MissingMethodException`, not a compile error |
| `OpenTelemetry.Instrumentation.AspNetCore` | the Gateway's inbound HTTP server span — the first hop of acceptance bullet 1 | same band |

`OpenTelemetry.Extensions.Hosting` is **already pinned** at 1.18.0 and used by nothing; this feature is its first consumer. Deliberately **not** added: `OpenTelemetry.Instrumentation.EntityFrameworkCore` (beta — §5.4), `OpenTelemetry.Exporter.Prometheus.AspNetCore` (`OR5` forbids a scrape endpoint), any health-check package (hand-rolled, as #7 refused `@nestjs/terminus` for the same reason), `System.Diagnostics.DiagnosticSource` (in the framework).

### 9.2 New environment variables

`FACT_RETRY_MAX_ATTEMPTS=3`, `FACT_RETRY_BACKOFF_MS=500`, `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317`, `OTEL_SERVICE_NAME_*` **not** used (the resource attribute is a literal per service), and the five `*_HEALTH_PORT` values of §8.1. All added to `.env.example` with the surrounding comment style already there.

### 9.3 Migrations

**One** Orders migration carrying both halves' schema changes — `orders.request_id` + its filtered unique index, and `saga_commands.triggering_event_envelope` / `triggering_event_topic` / `dead_lettered_at`. Both are Orders-only and there is no reason to force two coordinated deploys. No other service has a schema change.

---

## 10. The ported-idiom ledger

> **How this table was produced.** The boundaries were enumerated first, from the two checkouts rather than from intuition: every distinct place where #7's engine, language, driver or framework supplied a property this design must now obtain from somewhere. **28 boundaries were listed; 28 rows follow** — **29 since review round 3**, which found a boundary the enumeration missed (L29, fact-processing latency read from an injectable clock versus a `Stopwatch`) *(count corrected at #8's feature-27 review round 3, D8)*. A boundary considered and dismissed is a row saying why; a boundary never listed is the failure mode this table exists to prevent.
>
> **The *"#7 relied on"* half is a claim about #7's source and is cited by file and line.** The **Guard** half names a test that `tasks.md` carries with the arming flag; naming it here creates the obligation to see it fail and does not discharge it. Before ticking any row's guard, ask the question that is *not* about passing: **does this test execute the code the row is about?** `—` means the property is supplied by the same mechanism on both sides and there is nothing to hand-build.

### 10.1 Half B — the store

| # | Boundary | #7 relied on | #8 supplies it by | Guard |
|---|---|---|---|---|
| **L1** | A unique index over a nullable column admits **many** rows with no value | MySQL's `UNIQUE`, which treats `NULL`s as distinct — stated in `apps/orders/src/infrastructure/persistence/schema/orders.schema.ts:33-36` and rendered as a plain `UNIQUE(request_id)` in `apps/orders/drizzle/0005_sticky_goblin_queen.sql:5` | **MS-SQL treats two `NULL`s as equal** and admits exactly one. The property is restored by a **filtered** index (`WHERE [request_id] IS NOT NULL`), which EF Core's SQL Server provider is expected to emit by convention — **read the generated migration and quote the `filter:` argument**; add `.HasFilter(...)` if it is absent | `OrdersCreateIdempotentReplayTests › RI4_TwoOrdersPlacedWithNoRequestIdBothCommit_TheNullableUniqueIndexAdmittingBoth` — **two** rows with no value, which is the only shape that can fail |
| **L2** | Catching the collision without committing a partial write | #7's outbox row was written *after* the failing insert, so catching **inside** the transaction and committing was harmless (`apps/orders/src/application/place-order.handler.ts:159-163`) | `EfCoreOrderRepository.SaveChangesAsync` writes the `order.placed.v1` outbox row **first** (`src/Orders/.../EfCoreOrderRepository.cs:64-112`), so at collision time it is already in the transaction. The catch is therefore **outside** `unitOfWork.ExecuteAsync`, after rollback | `OrdersCreateIdempotentReplayTests › RI3_…` asserts the `outbox` table holds **exactly one** `order.placed.v1` after the race, not merely that one order exists |
| **L3** | Knowing **which** unique key collided | `mysql2`'s `ER_DUP_ENTRY` plus a substring match on the driver's `sqlMessage` (`apps/orders/src/application/place-order-request-id.ts:14-40`) | `SqlException.Number` `2601`/`2627` **and** a substring match on `Message`, which is the only place `Microsoft.Data.SqlClient` exposes the index name. Both numbers are handled because a constraint-vs-index change swaps which is raised | `PlaceOrderRequestIdReplayTests › RI3_ADuplicateKeyOnTheOrderReferenceIndexPropagatesUnchanged` **plus** a task obligation to capture the real message text from a real `mssql` container — `SqlException` cannot be constructed by hand, so a fake proves nothing about the format |
| **L4** | A re-read after a failed save leaving the context usable | JS had no change tracker; the re-read was a plain query | `db.ChangeTracker.Clear()` before the re-read, and `FindByRequestIdAsync` is a **no-tracking** read that never enters `EfCoreOrderRepository._tracked` | `PlaceOrderRequestIdReplayTests › RI3_ADuplicateKeyOnTheRequestIdIndexResolvesToTheWinnersReReadReply` over a repository fake, **plus** the integration case, which is the only one that exercises a real post-failure `DbContext` |
| **L5** | The transaction is not doomed by a duplicate-key error | MySQL: the statement fails, the transaction continues | MS-SQL under `XACT_ABORT OFF` behaves the same, **but this design does not depend on it** — the exception leaves `ExecuteAsync`, which rolls back and disposes. Recorded because a future `SET XACT_ABORT ON` would change it and nothing would fail | — (no dependency exists; `L2`'s guard would fail if one were introduced) |
| **L6** | Two same-`requestId` requests do not deadlock | both allocate the order number first, serialising on one counter row | identical — `EfCoreOrderNumberAllocator` is the first statement inside the transaction in both. The lock **order** is what makes it a wait rather than a deadlock, and it is a property of the handler's statement order, not of the engine | `OrdersCreateIdempotentReplayTests › RI3_…` runs the race ≥ 5 rounds; a deadlock surfaces as SQL error 1205, not as a hang |
| **L7** | Money and totals on the replayed reply | JS numbers | `long` minor units end to end; `PlaceOrderResult` already carries `Money`. The replay path re-reads through `OrderRowMapper`, the same mapper the write path uses — **no second projection is written for it** | `RI2`'s unit case asserts the replayed reply field-by-field against the original, including all three money fields |
| **L8** | `requestId` as `causationId` | `UniqueId.from(requestId)` with a try/catch fallback (`place-order.handler.ts:205-211`) | `Guid?` — already parsed by the responder's validator, so no parse can fail here and no fallback is needed | `PlaceOrderRequestIdReplayTests › RI5_…` asserts both directions (supplied → equal; omitted → fresh and non-empty) |

### 10.2 Half A — the fact consumers and Kafka

| # | Boundary | #7 relied on | #8 supplies it by | Guard |
|---|---|---|---|---|
| **L9** | The poison-message shape itself | a non-UUID `correlationId` passing `parseFactEnvelope`, whose fields are strings | **cannot occur** — `Envelope.CorrelationId` is a `Guid` and the envelope guard rejects it. #8's equivalent shape is a payload that fails `JsonSerializer.Deserialize` against its catalogued type, or any downstream throw. **Porting #7's test literally would produce a vacuous test** | each service's dead-letter integration case constructs a fact that is valid at the envelope level and fails **after** it, and asserts the retry count observed |
| **L10** | Where the retry wrapper attaches | one `@EventPattern` controller method per service, all three identical | three `*FactsConsumer.HandleMessageAsync` methods, all three identical in shape — the same seam exists, at the same granularity | `OR2`'s parity guard, plus a per-consumer unit case that the dispatch genuinely goes **through** the injected dispatcher, not around it (#7's own `A4b` case, ported) |
| **L11** | A deliberate ignore not being dead-lettered | #7 caught `UnknownFactTypeError` **inside** the wrapped delegate after finding the bug | the same placement, stated in §3.1's table rather than rediscovered | `ProjectorFactsConsumerTests › OR1_TheUnknownFactTypeIgnoreIsSwallowedInsideProcess_NeverReachingTheRetryPath` |
| **L12** | The offset advancing after a dead letter | `@nestjs/microservices` committed the offset because the handler returned normally | `KafkaFactStreamSubscriber` calls `StoreOffset` **only after** the handler returns (`src/Orders/.../KafkaFactStreamSubscriber.cs:96-100`), with `EnableAutoCommit=true` + `EnableAutoOffsetStore=false` — three settings that must agree. The dispatcher's **non-rethrow** is what reaches it | each dead-letter integration case asserts the **committed offset read from the broker** (`consumer.Committed(...)`) has advanced — read, never inferred from a non-redelivery — and that the next distinct fact on the same partition processes |
| **L13** | Shutdown not looking like a poison message | Node's cancellation was a closed consumer, not an exception through the handler | `OperationCanceledException` on the stopping token is rethrown unconditionally, never retried, never dead-lettered | `FactRetryDispatcherTests › OR1_ACancelledTokenIsRethrownImmediately_NeitherRetriedNorDeadLettered` |
| **L14** | The copy-parity population | `SERVICE_IDEMPOTENCY_MODE`/`hasEventPatternHandler` discovery, which #7 had to fix mid-feature when it matched a fixture string inside a `.spec.ts` | discovery by `Presentation/*FactsConsumer.cs`, **excluding `tests/`**, with the expected set asserted as the literal `["Notifications","Orders","Projector"]` and the rest derived by subtraction — never "has the dispatcher file", which would let a violation remove itself from the population | `FactRetryDispatcherParityTests › RequiresACopyOfTheDispatcherFromEveryServiceThatOwnsAFactConsumer_AndFromNoOther`, armed by deleting one copy **and** by adding a fourth consumer with none |
| **L15** | The dead letter being byte-for-byte the original | kafkajs handed the raw `Buffer` straight through | the raw `FactStreamMessage.Value` is carried into the dispatcher and published unmodified — **never** a re-serialised `Envelope`, which would reorder keys and drop unknown fields | the dead-letter integration cases compare the `.dlq` message bytes to the produced bytes with `Assert.Equal` over the arrays |
| **L16** | "At most once per parked row" | a `WHERE dead_lettered_at IS NULL` guarded UPDATE | the same, as a **single** `ExecuteUpdateAsync` whose affected-row count is the answer — never `SELECT`-then-`UPDATE`. `CLAUDE.md` records a shipped defect of exactly the check-then-act shape | `SagaFirstParkDeadLetterTests › OR3_ClaimsTheFirstParkExactlyOnce_AndDoesNothingOnASecondPark` (unit, concurrent callers) and the integration case's forced second park |
| **L17** | What is atomic with what | #7 sequenced the Kafka publish and the MySQL transaction and stated the window | the **claim + the `order.saga_failed.v1` outbox row** commit in one transaction; only the `.dlq` republication sits outside it. A crash loses the diagnostic copy, never the timeline entry — the reverse of #7's window, stated deliberately | the integration case asserts the outbox row and the `dead_lettered_at` stamp are both present or both absent |
| **L18** | Kafka producer confinement | #7 had no equivalent architecture rule | `FactPublisherConfinementTests` confines four producer types to `*.Infrastructure.Outbox`; widened here to admit `*.Infrastructure.Messaging.DeadLetter`, since `Projector`/`Notifications` own no outbox | `FactPublisherConfinementTests` **re-armed** after the widening: a `ProducerBuilder<string, byte[]>` under `Application/` must still fail it, with the `` `2 `` arity suffix the existing test's own remarks explain |
| **L19** | `x-attempts` and the retry count agreeing | one loop, one counter | identical, but the header is written from the **same** variable the loop increments, never from `options.MaxAttempts` re-read at the end — the two are equal only while nothing short-circuits | `FactRetryDispatcherTests › OR1_RetriesToTheConfiguredMaximum…` asserts `x-attempts` equals the observed `process` invocation count, both driven from a counting fake |

### 10.3 Half A — telemetry, logging and the host

| # | Boundary | #7 relied on | #8 supplies it by | Guard |
|---|---|---|---|---|
| **L20** | A span created is a span exported | `@opentelemetry/sdk-node` exports every span from any tracer by default | **`AddSource(name)` is exact-match opt-in.** An `ActivitySource` whose name is not registered is never sampled and never exported — silently, with the code otherwise perfect | `TelemetryWiringTests › OR4_EveryActivitySourceNameConstructedUnderSrcIsRegisteredOnItsOwnHostsTracerProvider`, armed by renaming one constant |
| **L21** | Building trace headers per call | JS object literals, safe to build anywhere | `NatsHeaders` is documented **not thread-safe**; the three outbound sites already build a fresh instance per call and the trace fields are added **there**, never hoisted (`projector_read_model/design.md` row `L39`; `NatsSagaCommandsAdapter.cs:91-97`) | one **two-concurrent-calls** case per outbound site, each asserting every request carries its own trace id: `tests/Orders.UnitTests/NatsSagaCommandsAdapterTests.cs` › `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId`, `tests/Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs` › `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId`, `tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs` › `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId` — each armed by hoisting `NatsHeaders` onto a shared field *(corrected at #8's feature-27 review round 2, R9: this cell originally cited a `NatsRpcClientTests` that never existed, and only the saga adapter's site was guarded; the other two guards were added in review-round-2 fixes)* |
| **L22** | Retries staying on the originating trace | an explicit `otelContext.with(...)` around the dispatch | `Activity.Current` flows through `async` continuations on the same execution context, **but** the extracted context must be made current with a started activity around the *whole* `DispatchAsync` call, not around each attempt | `SagaDeadLetterTests › R57_OR4_EveryRetryAttemptAndTheEventualDlqPublishObserveTheSameTraceIdAsTheInboundFact` |
| **L23** | The write-database hop appearing in the trace | nothing — #7 had no DB instrumentation; its trace jumped from the RPC span to the outbox relay span | one hand-started `writemodel.transaction` activity in `EfCoreUnitOfWork.ExecuteAsync`. A prerelease EF Core instrumentation package is deliberately refused (§5.4). **#8 is stronger than #7 here**, which is why the row exists — an inherited gap is still a gap | `TraceContextPropagationTests` asserts the exported span list for one placement contains the transaction span **between** the RPC span and the publish span, by parent-span id |
| **L24** | "Continued" meaning the same trace, not merely a header | #7's own case titles insist on it (*"not merely 'a header is present'"*) | every continuation assertion extracts the header back and compares to the **real** originating trace id, and asserts the span id differs | every `OR4` case; armed by injecting a *fresh* span's context instead of the active one, which leaves a "header present" assertion green. **For the three production RPC responders** the guards are `OrdersCreateResponderTraceContinuationTests`, `StockRpcResponderTraceContinuationTests` and `BillingRpcResponderTraceContinuationTests`, each driving the real responder over real NATS and asserting the responder's span continues the caller's trace id with the caller's span as parent *(corrected at #8's feature-27 review round 1, D1: the row's original guard was decorative for the responders — the R57 NATS case drove a stand-in that re-implemented the extraction, and a fresh trace root in `StockRpcResponder` left every suite green)* |
| **L25** | Every log line carrying trace and correlation ids | #7 hand-added `...(traceId ? { traceId } : {})` at each JSON log call site — a per-site property: **8** non-spec sites at #7's feature-27 commit `95e883a` (2026-08-27 — `apps/gateway/src/presentation/problem-json.filter.ts:57`; in `apps/orders/src/`: `infrastructure/messaging/fact-retry-dispatcher.ts:167`, `infrastructure/outbox/outbox-relay.ts:192`, `infrastructure/saga/saga-command-dispatcher.ts:158` and `:187`, `infrastructure/saga/saga-command-sweeper.service.ts:101`, `infrastructure/saga/saga-first-park-dead-letter-handler.ts:67`, `presentation/saga-facts.controller.ts:101`), and **21** at #7's HEAD after its Phase-25 `R58` closeout spread the pattern to every service — counted with `git grep -nE '\.\.\.\((traceId\|[a-zA-Z]+TraceId) \? \{ traceId' <rev> -- apps`, spec files excluded *(corrected at #8's feature-27 review round 1, R2: this cell originally said "eleven", uncited)* | **two explicit host settings plus one framework default**: `AddJsonConsole` and `IncludeScopes = true` are both required, and removing either drops the fields from every line; `ActivityTrackingOptions` is **on by default** in the generic host — deleting the explicit setting leaves `TraceId` on every line, while setting it to `None` removes it. No assertion fails anywhere unless the guard reads real output *(corrected at #8's feature-27 review round 2, R4: this cell originally said any one of the three settings missing removes the field; the default was measured by the reviewer's deletion probe, not read from framework source)* | `LogCorrelationTests › R58_OR7_…` captures **emitted JSON** from a configured host and asserts on every record of one flow; armed by turning off each of the three settings in turn |
| **L26** | A readiness probe that cannot hang | nothing — #7 shipped one probe unbounded (the Gateway's NATS `rtt()`), found and disclosed by #7's own **implementer** (`order-to-cash-nestjs/progress/impl_observability_reliability.md:912`, mechanism at `:927`) and then confirmed as an already-disclosed minor finding by #7's reviewer (`review_observability_reliability.md:70-72`, finding 1) *(corrected at #8's feature-27 review round 1, R2: this cell originally said #7's reviewer found it)* | an explicit timeout on **every** probe in all six services from the start, plus an enumeration test over every `IHealthCheck` implementation under `src/` | `HealthProbeTimeoutTests › OR6_EveryReadinessProbeInEveryServiceIsBoundedByAnExplicitTimeout`, armed by removing one timeout; and each service's probe case against a **paused** real container, which is the only shape that distinguishes stalled from closed |
| **L27** | The broker probe being fast | a dedicated `retries: 0` admin client (`apps/orders/src/infrastructure/health/kafka-health-check.ts:16-24`) | a dedicated `AdminClient` with a short `socket.timeout.ms` and no reuse of the relay's long-lived producer, whose retry policy would stretch one "down" observation over seconds | the paused-container case asserts readiness reports `down` **within the probe's own bounded window**, not merely eventually |
| **L28** | Five services gaining an HTTP surface | every #7 service was already an HTTP app | `FrameworkReference Microsoft.AspNetCore.App` + one `IHostedService` per service (§8.1) — no NuGet package, no change to any `*Host.CreateBuilder` signature | each service's `HealthCheckAggregationTests` resolves the real host and asserts both routes exist; the port env-var reads are armed by **substitution** across the five-name family |
| **L29** | Fact-processing latency being a value a test can assert | #7 read its injected `Clock` at dispatch entry (`apps/orders/src/infrastructure/messaging/fact-retry-dispatcher.ts:135`, `const enteredAt = this.clock.now()`) and again at each of the two record sites (`:142` success, `:171` exhausted retry), and its own comment says why (`:129-134`: *"so a unit test's fake clock controls the recorded value exactly"*) — read at #7 HEAD `bf45af0` | the same injected `IClock` every `FactRetryDispatcher` copy already held, read at entry (`src/Orders/Infrastructure/Messaging/FactRetryDispatcher.cs:73`) and at both record sites (`:84`, `:136`), replacing a bare `Stopwatch` that no test could drive; identical in the Notifications and Projector copies (byte-parity family) | `tests/Orders.UnitTests/FactRetryDispatcherTests.cs` › `OtcFactProcessingLatencyMs_RecordedOnSuccess_TaggedByConsumer` (exact `240`) and › `OtcFactProcessingLatencyMs_RecordedOnTheExhaustedRetryDlqPathToo` (exact `5750`), plus row 72's integration upper bound; armed by ticks-for-milliseconds, seconds-for-milliseconds and deletion of the record call *(added at #8's feature-27 review round 3, D8: the ledger had no row for this boundary, so a `Stopwatch` swap went unrecorded and the value unguarded behind `>= 0` asserts)* |

### 10.4 Where to spend review time

Three rows are believed to be **present and correct on the path a deletion probe takes**, and wrong only under a condition the happy path never creates. They are where a reviewer should attack first:

- **L1** — with an unfiltered unique index, every test that places exactly one order with no `requestId` still passes. Only a **second** such order fails. This is the single highest-consequence row in the feature: get it wrong and ordinary order placement breaks in production while the suite is green.
- **L2** — with the catch inside the transaction, the race still resolves to the winner's reply and `RI3`'s headline assertion still passes. The damage is an orphan outbox row that a later relay cycle publishes as a fact for an order that does not exist.
- **L25** — with `IncludeScopes` left at its default, every logging assertion written against the logger *abstraction* still passes; only a test reading real emitted JSON can see it.

---

## 11. Ported guards — enumerating #7's assertions for these mechanisms

**The candidate set is a search result, not a reading.** #7 built this feature in two commits; the enumeration is every `it(...)` case in every `.spec.ts` file those two commits **added**, plus every `it(...)` line they **added to an existing** spec file:

```
cd order-to-cash-nestjs
{ git show --diff-filter=A --name-only --format="" 95e883a; \
  git show --diff-filter=A --name-only --format="" 8635b66; } \
  | grep '\.spec\.ts$' | sort -u          # → 38 files, 112 it() cases
for c in 8635b66 95e883a; do git show $c -- '*.spec.ts' \
  | awk '/^\+\+\+ / {f=$2} /^\+[[:space:]]*it\(/ {print f" :: " substr($0,2)}'; done \
  | grep -vFf <the 38 file names>          # → 33 further cases in modified files
```

**145 cases; every one is classified below.** Where a case title is repeated **identically** across services the row states the multiplicity explicitly (`×5`) — the unit classified is still the assertion, and nothing is folded away silently.

| # | #7 case (abbreviated to its claim) | Classification for #8 |
|---|---|---|
| 1–3 | `fact-retry-dispatcher.spec` — retries to the maximum with exponential backoff then publishes and swallows; retries then succeeds without ever calling the DLQ; succeeds first time with no delay and no DLQ | **Ported** → `FactRetryDispatcherTests`, all three (`requirements.md` §5 `OR1`) |
| 4–5 | `fact-retry-dispatcher.spec` — env defaults `3`/`500`; reads both from the environment | **Ported** into `composition_root_env_reads_are_unguarded`'s convention, with the **substitution** family check the config rule now requires — in each fact-consuming service's `*ProgramConfigurationTests`: `tests/Orders.UnitTests/OrdersProgramConfigurationTests.cs` › `ConfigureSaga_DefaultsFactRetryPolicyToThreeAttemptsAndFiveHundredMs_WhenNoEnvVarsAreSet` and › `ConfigureSaga_ReadsFactRetryMaxAttemptsAndBackoffMsIndependently_FromTheirOwnDistinctVariableNames`; `tests/Notifications.UnitTests/NotificationsProgramConfigurationTests.cs` and `tests/Projector.UnitTests/ProjectorProgramConfigurationTests.cs` › `Configure_DefaultsFactRetryPolicyToThreeAttemptsAndFiveHundredMs_WhenNoEnvVarsAreSet` and › `Configure_ReadsFactRetryMaxAttemptsAndBackoffMsIndependently_FromTheirOwnDistinctVariableNames` *(corrected at #8's feature-27 review round 4, RC2: this cell originally named a `FactRetryOptionsTests` class that never existed)* |
| 6–7 | `kafka-dlq-publisher.spec` — injects the caller's real trace id, extractable to the same id; with no active span publishes **no** `traceparent` at all | **Ported** → `KafkaDeadLetterPublisherTests` |
| 8–9 | `saga-dead-letter.integration.spec` — the Phase-12 incident (non-UUID `correlationId`); the generic-throw variant | **Ported with a changed subject** — ledger **L9**: the non-UUID shape is unreachable in #8, so both #8 cases use a post-envelope-guard failure. The *property* (retried → dead-lettered → offset commits → next fact still processes) is ported unchanged |
| 10 | `projector-dead-letter.integration.spec` — a `stock.rejected.v1` with an empty `shortages` array | **Ported** (#8's shape: a payload that fails deserialisation against its catalogued type) |
| 11 | `notification-dead-letter.integration.spec` — an `order.placed.v1` with `retailerCode` omitted | **Ported**, same substitution |
| 12–13 | `idempotent-consumer.parity.spec` — requires the dispatcher copy from every service with a fact consumer; holds every copy byte-identical | **Ported** → `FactRetryDispatcherParityTests`, as a **separate** test class rather than widening `IdempotentConsumerParityTests`, and with the discovery predicate hardened (**L14**) |
| 14–16 | `saga-command-dispatcher.spec` — `onFirstPark` called exactly once with the accumulated attempts; not called when the row was already dead-lettered; not called when `park()` reports no transition | **Ported** → `SagaFirstParkDeadLetterTests` |
| 17 | `saga-command-dead-letter.integration.spec` — dead-letters and appends `order.saga_failed.v1` exactly once on first park, `SO5` untouched, neither repeated on a second park | **Ported** verbatim in claim |
| 18–19 | `order.spec` — `recordSagaFailure` appends exactly one event leaving status/lines/totals unchanged; `correlationId` and `aggregateId` are both the order id | **Ported** → `OrderSagaFailureTests` |
| 20 | `event-envelope.spec` — the event-type pattern admits an underscore in `saga_failed` | **Not applicable** — #8's `FactCatalog` already carries `order.saga_failed.v1` and `Contracts` already declares the payload; there is no pattern to widen |
| 21–28 | `trace-context.spec` — inject/extract on the NATS carrier; extract from headers with no context; extract from null headers; the Kafka carrier both header encodings; `activeTraceParent()` with and without a span; `contextFromTraceParent(null)`; a restored-context child keeps the trace id and mints a fresh span id; the default setter populates a plain carrier | **Ported (7 of 8)** → `TraceContextCarrierTests`. The kafkajs `Buffer`-vs-`string` half of case 24 is **not applicable**: `Confluent.Kafka` headers are `byte[]` with one encoding |
| 29–31 | `trace-context-propagation.integration.spec` — NATS RPC continuation over a real socket; the Kafka write → `outbox.trace_parent` → relay child span → consumed headers chain; a write with no active span produces no header at all | **Ported** → `TraceContextPropagationTests` |
| 32 | `saga-facts-trace-continuity.spec` — every retry attempt **and** the DLQ publish share the inbound trace id | **Ported** → into `SagaDeadLetterTests` (**L22**) |
| 33 | `nats-saga-commands.adapter.spec` — every outbound call injects the real active trace id | **Ported**, and **strengthened**: #8's case drives two concurrent calls (**L21**) |
| 34 | `nats-rpc-client.adapter.spec` — the Gateway client injects the real trace id | **Ported**, same strengthening |
| 35 | `orders-create.controller.spec` — the responder continues the inbound trace | **Ported**, and generalised to the three responder classes / fifteen subjects (§5.2) |
| 36–37 | `projector-facts.controller.spec` / `notification-facts.controller.spec` — each consumer continues the inbound Kafka trace | **Ported** |
| 38–40 | `projector-facts.controller.spec` — dispatch goes **through** the injected dispatcher; a generic failure reaches it rather than propagating raw; `UnknownFactTypeError` is swallowed inside | **Ported** (**L10**, **L11**) |
| 41–42 | `notification-facts.controller.spec` — the same two dispatcher-routing claims | **Ported** |
| 43 | `notifications-consumes-only.spec` — no RPC responder and no outbound producer except the one named DLQ adapter | **Ported by amendment** — #8's equivalent is `FactPublisherConfinementTests`' widened namespace rule (**L18**), which is stronger: it is repository-wide rather than one service's text scan |
| 44 | `http-instrumentation.spec` — a real inbound request produces a real server span and a real client span on one trace | **Ported in part** → `tests/Gateway.IntegrationTests/LogCorrelationTests.cs` › `Row44To45_ARealHttpRequestProducesARealServerSpan_AndTheFailureLogLineCarriesTheSameTraceId` drives a real HTTP request through the Gateway host and asserts the server span is exported **through the host's own tracer provider** (a recording exporter appended via `ConfigureOpenTelemetryTracerProvider`, with no test-owned `ActivityListener` on `Microsoft.AspNetCore`), so deleting the host's `.AddAspNetCoreInstrumentation()` fails it. #8 has **no outbound HTTP call**, so the client-span half is **not applicable** *(corrected at #8's feature-27 review round 3, D10: this cell originally credited `TelemetryWiringTests` with asserting the registration, which nothing did — the row-44 case then registered its own listener, which is what made ASP.NET Core create the span)* |
| 45–46 | `problem-json.filter.spec` — carries the real active trace id; omits it rather than rendering `"undefined"` | **Ported** → into the Gateway's log-capture case |
| 47 | `problem-json.filter.spec` — reuses the request-scoped `correlationId` rather than minting a fresh one | **Ported as an ordering guard** — #8 already reuses it (`ProblemJsonMiddleware.cs:37-39`); what is unguarded is the middleware **order** (§6) |
| 48–49 | `saga-facts-log-trace-id.spec` — the malformed-envelope log carries the real inbound trace id; omits it when no header is present | **Ported** |
| 50–58 | `*-log-trace-id.spec` ×5 files — the retry dispatcher (2), the saga command dispatcher (3), the sweeper (2), the first-park handler (2) each log the real trace id and omit rather than render `"undefined"` | **Ported as one property, not nine files** — #8 obtains the trace id from `ActivityTrackingOptions` rather than per call site (**L25**), so one log-capture case per service replaces the per-site files. The **omission** half is ported explicitly, since a scope that renders an empty value is the failure this family exists to catch |
| 59–60 | `log-correlation.integration.spec` — two real failure lines for the same fact carry the identical real trace id; a row written with no span carries none | **Ported** → `LogCorrelationTests` |
| 61–62 | `request-latency.interceptor.spec` — records the real duration on success and **on error** | **Ported** → the Gateway middleware's tests |
| 63–64 | `fact-retry-dispatcher-metrics.spec` — records the real duration by consumer on success and on the exhausted-retry path | **Ported** |
| 65–66 | `otel-saga-metrics.spec` — records the exact duration and outcome attribute; distinguishes completed from cancelled **by attribute**, not by a second instrument | **Ported** |
| 67–69 | `kafka-dlq-depth.spec` — sums `(high − low)` across partitions; reports `0` for a missing topic without throwing; queries each topic independently | **Ported** → `tests/Orders.IntegrationTests/MetricsExposureTests.cs` › `OtcDlqDepth_TheRealKafkaBackedGauge_SumsHighMinusLowAcrossEveryPartitionOfEveryDlqTopic_AgainstTheBrokersOwnReportedCount` (row 67), › `OtcDlqDepth_ANonExistentTopic_Reports0AndNeverThrows` (row 68), › `OtcDlqDepth_SumsAcrossMultipleTopicsIndependently_AMissingTopicNeitherStopsNorZeroesTheOthers` (row 69) *(corrected at #8's feature-27 review round 4, RC2: this cell originally named a `KafkaDlqDepthTests` class that never existed)* |
| 70–73 | `metrics-exposure.integration.spec` — outbox lag tracks a genuinely aged row then drops; DLQ depth reflects the **broker's** count; fact-processing latency from a real Kafka-delivered fact; saga completion measured between two real timestamps | **Ported** → `MetricsExposureTests`, keeping the exact-value assertions (a "greater than zero" assertion here proves nothing) |
| 74–78 | `saga-fact-handler-saga-completion-metrics.spec` — the completing fact records exactly one completion; a direct cancel records exactly one; the compensation-completing cancel records exactly one, not two; a non-closing step records nothing; an ignored fact records nothing | **Ported** — the two "records nothing" cases are the ones that matter and are easiest to lose |
| 79–93 | `health-checks.spec` ×5 services — write-model probe up / down-with-detail (×4 services); Kafka probe up / down on `describeCluster` / down on `connect`, always disconnecting (×3); NATS probe up / down-when-closed-without-calling-rtt / **down when rtt never settles within the bounded timeout** (×3); Mongo ping up / down (×1) | **Ported**, per #8's own dependency table (§8.2). The stalled-socket case is the one that must not be dropped (**L26**) |
| 94–108 | `health.controller.spec` ×5 services — `live()` always `200 up` independent of any check; `ready()` reports up with no status override when all are up; `ready()` reports `down` + `503` when **any single** check is down, naming it | **Ported** ×6 (#8 adds the Gateway's own) |
| 109–118 | `health-probes.integration.spec` ×5 services — ready when everything is reachable; pausing the **real** container makes readiness `503`/`down` for that check **only** while liveness stays `200` throughout, and recovers | **Ported** ×6, against paused real containers, never a faked failure |
| 119–121 | `orders-create-idempotent-replay.integration.spec` — `RI1` persists under the constraint; `RI3` the concurrent race; `RI4` omitted `requestId` places a normal order | **Ported**, `RI4` **strengthened** to two orders with no `requestId` (**L1**) |
| 122–126 | `place-order.handler.spec` — `RI4` no lookup; `RI2` fast path with no reference-data or stock call; `RI3` the `uq_orders_request_id` catch; `RI3` `order_reference` propagates; `requestId` passed through to `save()` | **Ported** → `PlaceOrderRequestIdReplayTests`; the pass-through case becomes `RI5`'s causation assertion plus the persistence assertion |
| 127–128 | `outbox-relay.parity.spec` — copies byte-identical except the tracing files, which must match **each other**; the canonical stays adoptable | **Ported** — #8's three `OutboxRelay.cs` copies take the identical tracing change, so **no exception is needed**: the plain byte-identity rule holds. Recorded because #7 needed the exception and #8 must not quietly acquire one |
| 129–133 | `projector` domain specs — `domain-model.md` lists fourteen facts; the handler table covers exactly fourteen; unknown type throws; rank table covers fourteen; summaries cover fourteen | **Not applicable** — already `DONE` in #8 (`projector_read_model`, `PR2`/`PR12`/`PR16`); `order.saga_failed.v1` was minted into `Contracts` and the projector before this feature |
| 134–145 | `gateway/saga-e2e-verification.integration.spec` (12 cases mentioning these ids) | **Not applicable here** — feature 28 owns the composed-stack proof; listed so the enumeration is complete rather than filtered |

**Two guards #7 had that #8 deliberately does not port, with the reason:** case 20 (the event-type pattern widening) and cases 129–133 (the thirteen→fourteen sweep) — both are already true in #8 because the fourteenth fact was minted into `Contracts`, `FactCatalog` and the projector by earlier features. **One guard #8 adds that #7 never had:** the middleware-ordering guard of §6, and `L23`'s write-model transaction span.

---

## 12. Testing approach

**Levels**, per `CLAUDE.md` and `test-matrix.md`:

- **Pure unit** — the dispatcher's attempt/backoff/publish logic over a fake clock, delay port and publisher; `RequestIdCollision.Matches`'s narrowing (over message text, with the real text captured separately); `Order.RecordSagaFailure`; readiness aggregation over faked checks; the trace-context carriers; the metric instruments over an in-memory reader; every structural/architecture guard.
- **Integration, Testcontainers, real infrastructure** — everything else. Real `mssql`, real `apache/kafka:4.3.1` through the generic `ContainerBuilder` (`Testcontainers.Kafka` cannot drive this image — `Directory.Packages.props` carries the probe), real `nats:2.14.5-alpine`, real `mongo:8.3.8`. **No mocked broker, no mocked store, ever.** Spans and metrics are asserted against OTel's **in-memory** exporter/reader, not a live collector — feature 28 is the one positioned to observe the real Jaeger.
- **The one thing that must use a real container failure**: `R60`. A paused container, never a faked "down".

**Three rules bind every case here:**

1. **Synchronise on terminal or monotonic evidence.** Poll for the `.dlq` message to arrive, for the committed offset to advance, for `events.Count == N` — never a bare sleep, never an intermediate state.
2. **A retry budget counted in attempts assumes each attempt costs time.** Every readiness/arrival loop in this feature's fixtures is **explicitly paced**, and the fast-failing error (NATS *no responders*, a missing topic) is the one that breaks an unpaced loop in about a millisecond. `CLAUDE.md` records the incident; backlog id 63 records four more unpaced fixtures.
3. **An absent side effect proves nothing about an attempt.** "No second `order.saga_failed.v1`" must be observed as *the claim returned false*, not only as *the table has one row*.

**Coverage** — `./quality.sh`'s existing gates (≥80% domain, ≥60% overall) apply unchanged; the domain addition here is one method.

---

## 13. Out of scope, restated

- The composed-stack observation of `R56` — feature 28.
- Grafana dashboards and the `kafka-exporter` scrape job — `observability_dashboards`, phase 22. `infra/` is **not edited** by this feature.
- Any change to `SO1`–`SO11`, the saga step table, the sweeper's schedule, or `IdempotentConsumer`.
- A `.dlq` redrive consumer — `asyncapi.yaml` is explicit that redriving is a human act.
- `Fulfillment` and `Billing` fact consumers — neither consumes a fact, so neither receives a `FactRetryDispatcher` copy, and `OR2`'s guard must actively assert that.
