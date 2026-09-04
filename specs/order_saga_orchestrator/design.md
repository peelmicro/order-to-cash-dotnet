# `order_saga_orchestrator` — Design (.NET 10 / C# 14 / EF Core / Confluent.Kafka / NATS.Net, assessment #8)

> **Stack-specific.** This file is where the `Confluent.Kafka`, `NATS.Net`, EF Core, `System.Threading.Channels`, `BackgroundService` and Testcontainers detail lives. Nothing here belongs in `specs/shared/`; #7 wrote its own equivalent against the same `R19` – `R29`, and #9 will write a third.
>
> **Authorities.** [`specs/shared/saga.md`](../shared/saga.md) — the whole document, and this feature is its elaboration. [`specs/shared/requirements.md`](../shared/requirements.md) §3. [`specs/shared/asyncapi.yaml`](../shared/asyncapi.yaml) — the fact payloads consumed and the five RPC request/reply payloads issued. [`specs/orders_aggregate/design.md`](../orders_aggregate/design.md) — the command methods this feature drives. [`specs/outbox_and_idempotency/design.md`](../outbox_and_idempotency/design.md) §4 (`IUnitOfWork`), §5 (the relay, whose `BackgroundService` and row-claim shapes this feature copies), §6 (the idempotent-consumer pair this feature MUST reuse unmodified).
>
> **What is already built and must not be rebuilt.** The `saga_commands` and `saga_ignored_facts` tables and their four indexes (migration `20260901100855_InitialCreate`, phase 6). `IdempotentConsumer` + `ProcessedEventLedger` (feature 14, canonical, parity-guarded). `IUnitOfWork` / `IOrderRepository` / `OutboxWriter` (feature 14). `IDispatcher` and `AddDispatcher` (feature `cqrs_dispatcher`). `OrdersHost.CreateBuilder` with `ValidateOnBuild`/`ValidateScopes` forced on in every environment (feature 15). The `Order` aggregate's eight command methods (feature 13).

---

## 1. Scope

**In scope.** The saga orchestrator inside `src/Orders`: one Kafka `BackgroundService` consuming the three fact topics, the declarative fourteen-row saga step table, fourteen fact commands and their handlers on the existing in-process dispatcher, the generic transactional unit composing the **existing, unmodified** `IdempotentConsumer` with the aggregate's command methods, the five dispatch-owed application events and the `OrderSagas` event handlers that convert them into a dispatch signal, the in-process dispatch worker, the five outbound NATS saga commands with bounded retry, the durable pending/parked command mechanism over the **existing** `saga_commands` table plus its sweeper, the durable ignored-fact record over the **existing** `saga_ignored_facts` table, two architecture-rule changes, and the unit + integration tests (stand-in NATS responders for Fulfillment and Billing).

**Out of scope, and owned elsewhere.**

| Not here | Owned by |
|---|---|
| The `stock.reserve` / `stock.release` / `despatch.create` responders | features 17–18 `fulfillment_*` (phase 9) |
| The `credit.hold` / `invoice.issue` / `payment.register` responders and the credit simulator | features 19–22 `billing_*` (phase 10) |
| Terminal-vs-retryable classification of an `RpcError` **reply body** | feature 42 `orders_saga_terminal_rejection_classification` — §6.1 leaves the seam and does not pre-empt it |
| Consumer retry-to-DLQ, `<topic>.dlq` publication, dead-letter headers, `order.saga_failed.v1` emission, metrics, OpenTelemetry | feature 27 `observability_reliability` — §6.5 records the seams |
| The projector's timeline and the notifications consumer | features 24, 23 |
| The operator cancellation flow (`orders.cancel`, unwinding credit + stock from `credit_approved`/`confirmed`) | feature 25 and a later orchestrator extension; §4.3 notes the one row it will add |

**Domain layer: untouched.** Not one file under `src/Orders/Domain/` changes. `MarkStockReserved`, `ApproveCredit`, `Confirm`, `MarkDespatched`, `MarkInvoiced`, `MarkPaid`, `Complete` and `Cancel` are exactly the surface `saga.md` §3–§4 needs — that was the point of feature 13. If a step appears to need a new aggregate method, **stop and report**: it is a finding about feature 13, not something to add in passing.

**No migration.** See §7. This is the largest single reuse dividend of the feature and it is worth stating in the scope: #7 paid for a migration here and recorded it as a gate cost; #8 pays nothing.

---

## 2. Where everything lives

```
src/Orders/
  Application/
    Sagas/
      SagaStep.cs                        the step kinds (skip | advance | cancel) — pure, framework-free
      SagaStepTable.cs                   the 14-fact table as data + MapReason + CompensationStepsFrom (§4)
      SagaFact.cs                        the Application-level DTO one consumed fact becomes (§3.5)
      SagaCommandKind.cs                 the closed set of five owed commands + wire tokens
      SagaFactHandler.cs                 the ONE generic transactional unit (§5.1), returns what it enqueued
      SagaFactResult.cs                  outcome (Processed | Duplicate | Ignored) + optional enqueued command
      OrderSagas.cs                      the five IEventHandler<T> classes — the @Saga analogue (§5.5)
      SagaDispatchEvents.cs              the five dispatch-owed application events (§5.5)
    Commands/
      SagaFactCommands.cs                the ten Handle<Fact>FactCommand records + FactCommandFor(eventType)
      SagaFactCommandHandlers.cs         ten ICommandHandler<T> wrappers -> SagaFactHandler -> post-commit publish
    Ports/
      ISagaCommands.cs                   the five typed RPC calls + SagaCommandTimeoutError/TransportError (§6.1)
      ISagaCommandStore.cs               enqueue / claim / claim-due / mark-sent / park (§6.3)
      ISagaIgnoredFactRecorder.cs        the R25 + SO8 durable record (§5.4)
      ISagaCommandSignal.cs              the in-process fast-path signal (§5.5)
      IFactStreamSubscriber.cs           the consume-with-commit-after-handler port (§3.1)
  Infrastructure/
    Messaging/
      Rpc/SagaCommandPayloads.cs         the five request/reply payload records, from asyncapi.yaml
      Rpc/RpcSubjects.cs                 EXTENDED with the five saga subjects (existing file)
      NatsSagaCommandsAdapter.cs         NATS core request-reply per subject, per-call timeout (§6.1)
      Consumers/KafkaFactStreamSubscriber.cs   the ONLY type touching Confluent.Kafka's consumer API (§3.1-§3.3)
      Consumers/SagaFactTopics.cs        the three consumed topic constants, spec-text-guarded
    Saga/
      SagaCommandDispatcher.cs           claim -> issue with SO4 policy -> mark sent | park (§6.2)
      SagaCommandDispatchWorker.cs       BackgroundService draining the in-process signal channel (§5.5)
      ChannelSagaCommandSignal.cs        the ISagaCommandSignal implementation (bounded Channel<T>)
      SagaCommandSweeper.cs              plain class: one sweep cycle (§6.4)
      SagaCommandSweeperBackgroundService.cs   PeriodicTimer loop, OutboxRelayBackgroundService's shape
      EfCoreSagaCommandStore.cs          saga_commands adapter (§6.3)
      EfCoreSagaIgnoredFactRecorder.cs   saga_ignored_facts adapter (§5.4)
    OrdersSagaOptions.cs                 the §3.2/§6.2/§6.4 settings
    OrdersSagaServiceCollectionExtensions.cs   AddOrdersSaga — one explicit line per port (§9)
  Presentation/
    SagaFactsConsumer.cs                 the ONE Kafka BackgroundService: parse, route, dispatch (§3)
  OrdersHost.cs                          EXTENDED with a third configure delegate (§9)

tests/Orders.UnitTests/
  SagaStepTableTests.cs                  every fact x every status — the exhaustive pure table test
  SagaFactHandlerTests.cs                composition with fakes (duplicate, unknown order, ignored, enqueue)
  SagaFactCommandHandlerTests.cs         delegation + publish-only-on-processed-with-enqueue
  OrderSagasTests.cs                     the five event -> signal mappings (SO3 fast path)
  SagaCommandDispatcherTests.cs          SO4 policy, SO6 no-retry, park on exhaustion (fake clock + fake port)
  SagaCommandDispatchWorkerTests.cs      SO10 — the loop hands off and returns
  SagaCommandSweeperLoopTests.cs         no-overlap, claim -> dispatch -> reschedule (OutboxRelayLoopTests shape)
  SagaFactTopicsTests.cs                 the three topics read out of asyncapi.yaml as text
  SagaRpcSubjectsTests.cs                the five subjects read out of asyncapi.yaml as text
  NatsSagaCommandsAdapterTests.cs        timeout / no-responders / RpcError-body taxonomy

tests/Orders.IntegrationTests/
  SagaHappyPathTests.cs                  R19-R24
  SagaPreconditionTests.cs               R25, SO8, the whole saga.md §6 redelivery sweep
  SagaCompensationStockRejectedTests.cs  R26
  SagaCompensationCreditRejectedTests.cs R27, R28, SO6, SO7
  SagaCommandRetryTests.cs               R29 retry clause, SO3, SO4, SO5
  SagaCommandStoreTests.cs               SO11 (the lease), enqueue-in-ambient-transaction, duplicate enqueue
  SagaConsumptionTests.cs                SO1, SO9
  StandInSagaResponders.cs               five programmable NATS responders (test support)
  OrderNumberAllocatorTests.cs           the A11 debt owed to this feature (§11.3)

tests/Architecture.Tests/
  FactPublisherConfinementTests.cs       AMENDED (§10) — producer-type confinement, not "any Confluent.Kafka"
  FactConsumerConfinementTests.cs        NEW (§10) — the consumer client is confined too
```

**Why the Kafka consumer is split port-and-adapter when the outbox relay's producer is not.** The relay owns its producer because it *is* the publication mechanism; the orchestrator's consumer is a transport detail underneath a saga. Splitting it buys three things the relay did not need: `SagaFactsConsumer` (Presentation) stays testable without a broker; the offset contract of SO9 has exactly one implementation to prove; and `Confluent.Kafka`'s consumer API stays confined to one namespace, which is what makes §10's new architecture rule expressible.

---

## 3. Consumption — how facts reach the orchestrator

### 3.1 One `BackgroundService` for the Kafka transport, over a port

`SagaFactsConsumer : BackgroundService` is the single Kafka-transport service in Orders, subscribing to all three fact topics through one consumer in group `orders.saga`. CLAUDE.md's "one `BackgroundService` per transport" is satisfied literally: the NATS responder (`OrdersCreateResponder`) and this class subscribe to different things and share nothing.

It depends on the Application port, never on `Confluent.Kafka`:

```csharp
public interface IFactStreamSubscriber
{
    /// Consumes each message once, in arrival order, invoking `handler` to
    /// completion BEFORE the message's offset becomes eligible for commit
    /// (SO9). A handler that throws propagates: the offset is not stored,
    /// the loop surfaces the failure to its caller, and the message is
    /// redelivered from the last committed offset.
    Task ConsumeAsync(
        IReadOnlyList<string> topics,
        Func<FactStreamMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken);
}

public sealed record FactStreamMessage(string Topic, int Partition, long Offset, ReadOnlyMemory<byte> Value);
```

`KafkaFactStreamSubscriber` is the one implementation and the one type in the repository that touches `IConsumer<,>`.

**The blocking-API shape, stated because getting it wrong is silent.** `Consume()` and `Commit()` are synchronous, blocking calls; `BackgroundService.ExecuteAsync` runs on a thread-pool thread and blocking it before the first `await` stalls host startup. So `ConsumeAsync` yields first (`await Task.Yield()`), then loops on `consumer.Consume(TimeSpan.FromMilliseconds(PollTimeoutMs))` — a **bounded** poll returning `null` when nothing arrived, so the cancellation token is observed every cycle — and `await`s the handler per message. `Consume(CancellationToken)` is deliberately not used: it blocks indefinitely and only unblocks on cancellation, which makes a graceful drain harder to reason about for the sake of one fewer wake-up per poll interval.

### 3.2 Consumer configuration, and the budget re-derived for this client

```csharp
new ConsumerConfig
{
    BootstrapServers = options.Kafka.BootstrapServers,
    GroupId          = "orders.saga",           // identical to the processed_events consumer token
    ClientId         = "otc-orders-saga",       // distinct from the relay producer's "otc-orders"
    AutoOffsetReset  = AutoOffsetReset.Earliest,// SO1
    EnableAutoCommit = true,                    // commits STORED offsets only — see §3.3
    EnableAutoOffsetStore = false,              // SO9 — the whole point; see §3.3
    EnablePartitionEof = false,
}
```

- **Group `orders.saga`** is deliberately the same string as `ConsumerNames.ToToken(ConsumerName.OrdersSaga)`, so the broker-side identity and the dedup-ledger identity of "the orchestrator" are one value. `ConsumerName` already exists and already carries exactly that token.
- **`AutoOffsetReset.Earliest`** is SO1. `Confluent.Kafka`'s default is `largest`, so this is a change from the default, not a restatement of it — a first boot with the default would **skip** the facts already in the topics, which is precisely the live-stack condition of §8.2.
- **Three topics, one consumer.** `consumer.Subscribe([ordersFacts, fulfillmentFacts, billingFacts])`. The topics are per-service, not per-event, so the routing to a step happens on `envelope.eventType`, never on the topic (§3.5).

**The retry budget's constraint, re-derived rather than copied.** #7 chose 3 × 5 s + 500 ms + 1 000 ms ≈ 16.5 s explicitly to stay under **kafkajs's 30 s session timeout**, because kafkajs heartbeats between messages and not during a slow handler. `Confluent.Kafka` 2.15.0 is librdkafka underneath and behaves differently — the defaults below are quoted from the package's own XML documentation on disk (`~/.nuget/packages/confluent.kafka/2.15.0/lib/net10.0/Confluent.Kafka.xml`):

| Setting | `Confluent.Kafka` 2.15.0 default | Relevance here |
|---|---|---|
| `session.timeout.ms` | **45 000** | Broker-side liveness. **Not** the binding constraint: librdkafka sends heartbeats from its own background thread, so a slow application handler does not miss them |
| `heartbeat.interval.ms` | **3 000** | Sent by librdkafka's background thread, independently of the application's `Consume()` cadence |
| `max.poll.interval.ms` | **300 000** | **This** is the binding constraint. If the application does not call `Consume()` within it, librdkafka fails the member and the group rebalances |
| `enable.auto.commit` | `true` | Kept |
| `enable.auto.offset.store` | `true` | **Overridden to `false`** — see §3.3 |
| `auto.commit.interval.ms` | 5 000 | Background commit of *stored* offsets |
| `auto.offset.reset` | `largest` | **Overridden to `Earliest`** — SO1 |
| `group.protocol` | `classic` | Kept; the KIP-848 `consumer` protocol is not adopted by this feature |

So the equivalent constraint is **300 s, not 30 s** — twenty times #7's headroom, on a per-message budget that is not even on the consume loop (SO10, §5.5). **The numbers are kept identical to #7's anyway**, and the reason is stated so nobody later mistakes it for a copy: (a) 5 000 ms is already this repository's per-call RPC budget (`NatsOptions.StockCheckTimeoutMs`, feature 15) and a second, different budget for the same broker would be arbitrary; (b) identical observable timing is what keeps `progress/history.md`'s #7-vs-#8 comparison about the language rather than about a tuning choice. What changes is the *justification*, and it is recorded in `SagaCommandDispatcher`'s header comment: the budget is bounded by nothing on the consume path, and by 300 s only in the degenerate case where SO10's decoupling is later removed.

### 3.3 Offset semantics — the #8 delta (SO9)

`Confluent.Kafka`'s defaults give **at-most-once**, not at-least-once: with `EnableAutoOffsetStore = true` the offset is stored when `Consume()` *returns* the message, and the background committer writes it every 5 s regardless of what the handler did with it. A handler that throws would therefore have its message's offset committed anyway, and the fact would be **lost**, not redelivered. `saga.md` §6's entire safety argument presumes at-least-once delivery.

The construction, in `KafkaFactStreamSubscriber`:

1. `EnableAutoOffsetStore = false` — nothing is stored implicitly.
2. `await handler(message, ct)` — the fact command handler, which is the whole transactional unit (§5.1). Resolution means the transaction committed.
3. **Only then** `consumer.StoreOffset(consumeResult)`.
4. `EnableAutoCommit = true` lets librdkafka commit stored offsets periodically, and `consumer.Close()` on shutdown commits the final stored offsets and leaves the group cleanly (the `BackgroundService`'s `StopAsync` path must reach it — a `finally` around the loop).
5. A handler that throws: nothing is stored, the exception propagates out of `ConsumeAsync`, `SagaFactsConsumer` logs it with the topic/partition/offset coordinates and **re-enters the loop after a short delay**, so the same offset is redelivered. Re-entry rather than process exit is deliberate: a permanently poisonous message would otherwise crash-loop the service, and `saga.md` §6's three layers make a redelivery harmless.

**This is the one behaviour in the feature whose failure mode is silent data loss, so it is proven twice**: an integration test that makes the handler throw and asserts the fact is redelivered (`SO9`), and an arming row that flips `EnableAutoOffsetStore` back to `true` and records which named test fails.

### 3.4 Ordering and partitions

The three topics have 6 partitions each, keyed by `correlationId = orderId` (feature 14, R15; `infra/kafka/create-topics.sh`).

- **One consume loop, one message at a time**, across all assigned partitions. Per-order ordering within one producing context is therefore preserved with no extra machinery — this is #8's equivalent of #7's `partitionsConsumedConcurrently: 1`, and it is stronger (serial across partitions, not merely within one).
- The price of serial processing is that a slow handler delays every partition. That is exactly why SO10 exists: the transactional unit is database-only work, and the RPC issue with its retries is **not** on this loop (§5.5).
- **Nothing is assumed across topics.** Every step checks its precondition (`saga.md` §6 layer 2); §4.4 argues why a fact can never be *early* on first delivery, which is what makes the R25 ignore rule lossless.

### 3.5 Envelope handling and routing

`SagaFactsConsumer` deserialises the message value as `Envelope<JsonElement>` through the shared `JsonWire.Options` — the same options the relay serialised it with, so casing and null handling cannot drift — and validates the seven envelope fields (non-empty `EventId`, `AggregateId`, `CorrelationId`, `CausationId`, non-default `OccurredAt`, non-empty `EventType`). It then binds the payload to the CLR type `FactCatalog.PayloadTypesByEventType` names for that `eventType`, producing the Application DTO:

```csharp
public sealed record SagaFact(
    Guid EventId, string EventType, Guid AggregateId, Guid CorrelationId,
    Guid CausationId, DateTimeOffset OccurredAt, object Payload);
```

`Payload` is `object`, documented as "always the `FactCatalog` CLR type for `EventType`", and the two step rows that read it use a C# type pattern (`fact.Payload is StockReleasedPayload p`). The alternative — a generic `SagaFact<TPayload>` — would make the step table itself generic and force fourteen closed types through a non-generic routing map for no behavioural gain. A unit test asserts every `FactCatalog` entry deserialises into its declared type from a real envelope.

**Three routing outcomes, in this order:**

| Case | Behaviour | Requirement |
|---|---|---|
| Malformed value — unparseable JSON, or a missing/empty required envelope field | Structured **error** log carrying topic/partition/offset and the raw value's length, then **acknowledged** (offset stored). It cannot be deduped (no trustworthy `eventId`) and cannot be parked (no `correlationId`); a producer bug is not fixable by redelivery, and feature 27's DLQ is its eventual home | recorded here, not silently chosen |
| A self-produced fact (`order.confirmed.v1`, `order.completed.v1`, `order.cancelled.v1`, `order.saga_failed.v1`) | Acknowledged with **no** dispatch, no transaction, no `processed_events` row, no aggregate load. Skipped *before* any I/O — writing dedup rows for facts that carry no handler would inflate `processed_events` by a third for no purpose | **SO2** |
| An `eventType` in neither the step table nor `FactCatalog` (a future fifteenth fact) | Structured **warning** log + acknowledge. Distinct from malformed: the envelope is well formed and a newer producer is simply ahead of this consumer | — |

A well-formed, consumed fact is routed through `FactCommandFor(eventType)` to one of the ten `Handle<Fact>FactCommand` records, which the consumer `await`s through `IDispatcher.SendAsync`. Resolution means the transaction committed; a rejection propagates into §3.3's no-store-implies-redeliver path unchanged.

---

## 4. The step table as code

### 4.1 The table

`SagaStepTable` is a static, declarative map from `eventType` to a step definition — a direct transcription of `saga.md` §3.1 and §4 plus the consumption map §5. Pure data and pure functions, in `Application/Sagas/`, with **no** `Microsoft.*`, `Confluent.*`, `NATS.*` or EF Core reference, exhaustively unit-tested over every fact × every one of the nine statuses.

```csharp
public abstract record SagaStep
{
    public sealed record Skip : SagaStep;

    public sealed record Advance(
        OrderStatus Precondition,
        Action<Order, SagaFact>? Apply,          // null => status deliberately unchanged (R19, R27)
        SagaCommandKind? CommandAfter) : SagaStep;

    public sealed record Cancel(
        OrderStatus Precondition,
        Func<SagaFact, CancellationReason> Reason,
        Func<SagaFact, IReadOnlyList<OrderCompensationStep>> CompensationSteps) : SagaStep;
}
```

| Fact consumed | Kind | Precondition | Aggregate call(s) | Command owed after commit | On precondition mismatch |
|---|---|---|---|---|---|
| `order.placed.v1` | advance | `Placed` | *(none — status unchanged, **R19**)* | `stock.reserve` | ignored (**R25**) |
| `stock.reserved.v1` | advance | `Placed` | `MarkStockReserved(fact.OccurredAt)` | `credit.hold` | ignored |
| `stock.rejected.v1` | cancel | `Placed` | `Cancel(StockRejected, [], occurredAt, causationId)` | **none — normatively none (R26)** | ignored; **critically, still no release command** |
| `credit.approved.v1` | advance | `StockReserved` | `ApproveCredit(occurredAt)` then `Confirm(occurredAt, causationId)` — one load/save, one `order.confirmed.v1` (**R21**) | `despatch.create` | ignored |
| `credit.rejected.v1` | advance | `StockReserved` | *(none — status unchanged, **R27**)* | `stock.release` (reason `credit_rejected`) | ignored |
| `stock.released.v1` | cancel | `StockReserved` | `Cancel(MapReason(fact), CompensationStepsFrom(fact), occurredAt, causationId)` (**R28**, **SO7**) | none | ignored |
| `order.despatched.v1` | advance | `Confirmed` | `MarkDespatched(occurredAt)` | `invoice.issue` | ignored |
| `invoice.issued.v1` | advance | `Despatched` | `MarkInvoiced(occurredAt)` | **none — the saga waits for the outside world (R23)** | ignored |
| `payment.received.v1` | advance | `Invoiced` | `MarkPaid(occurredAt)` | none | ignored |
| `credit.released.v1` | advance | `Paid` | `Complete(occurredAt, causationId)` — emits `order.completed.v1` (**R24**) | none | ignored |
| `order.confirmed.v1` | skip | — | — | — | — |
| `order.completed.v1` | skip | — | — | — | — |
| `order.cancelled.v1` | skip | — | — | — | — |
| `order.saga_failed.v1` | skip | — | — | — | — |

**Fourteen rows, four skips** — see `requirements.md` SO2's note on why #7's table had thirteen and three.

**`occurredAt` and `causationId` are the fact's, never the clock's.** `occurredAt` is the consumed fact's `OccurredAt` (the moment it became true in the domain, not the moment it was consumed) and `causationId` is the consumed fact's `EventId`. This is what makes every fact this feature causes the aggregate to emit chain correctly for **R12**, and it is why the aggregate's `Confirm`/`Complete`/`Cancel` take a `UniqueId causationId` parameter at all (feature 13).

### 4.2 The wrong-precondition rule (R25)

`Advance` and `Cancel` steps compare `order.Status` to `Precondition` **by equality** — no ranges, no "or later" — which is exactly the shared wording. On mismatch: no aggregate mutation, no command enqueued, no fact emitted, a `saga_ignored_facts` row with observed and expected status written **in the same transaction as the dedup record**, and the message acknowledged. Every row of `saga.md` §6's per-fact redelivery table falls out of this one rule plus the dedup layer, and `SagaPreconditionTests` sweeps that table literally: each of the ten consumed facts redelivered against its post-processing status.

### 4.3 The two compensation paths — different by design

- **Path A, `stock.rejected.v1`** (`Placed` → `Cancelled`): `Cancel(StockRejected, [], …)`. The empty compensation-steps list is **normative** (R26, `saga.md` §4.1 — reservation is all-or-nothing, so nothing was acquired), and the row has no `CommandAfter`. The test asserts not only the cancellation but the **absence** of any `stock.release` enqueue and of any request observed by the stand-in release responder, including after redelivering the fact against `cancelled`.
- **Path B, `credit.rejected.v1` → `stock.released.v1`** (release **then** cancel, R27/R28): the `credit.rejected.v1` row changes **no** status — the order stays `stock_reserved`, the safe resumable state `saga.md` §4.3 argues for — and owes `stock.release`. Cancellation happens only when `stock.released.v1` arrives, with one compensation step built from the observed fact (SO7). *"Pending compensation is a credit rejection"* (R28) is resolved by the **fact's own `reason` field**, not by any saga-instance record — there is none, because the saga state **is** the order status (`saga.md` §1). `MapReason`: `credit_rejected` → `CancellationReason.CreditRejected`, `order_cancelled` → `CancellationReason.OperatorCancelled` — both legal from `stock_reserved`, and feature 13's `Cancel` enforces the reason/source pairing itself, so an illegal pair raises `CancellationReasonNotApplicableError` rather than being silently accepted.
- **The operator flow that would *initiate* a release is feature 25's.** This table is merely already able to *finish* it: the `order_cancelled` branch of `MapReason` exists, is unit-tested, and has no live producer yet — which is precisely the "no live caller" case CLAUDE.md says to arm with double force.

### 4.4 Why ignoring an unmet-precondition fact loses nothing — stated once

R25's "ignore" would be dangerous if a fact could arrive **early** — before its precondition status is committed — because ignore + dedup would then permanently swallow it. It cannot, for two composed reasons:

1. **Commit-before-issue (SO3).** A command is enqueued in the same transaction as the status change that precedes it and issued only after commit. So `credit.hold` is never on the wire before `stock_reserved` is durable; hence `credit.approved.v1` cannot be observed before its precondition exists. The same holds for every command → fact edge.
2. **Per-partition ordering within one producing context.** The only trigger not caused by an orchestrator command is `payment.received.v1` (an operator may register a remittance the moment the invoice exists, possibly before the orchestrator has processed `invoice.issued.v1`) — but Billing emits `invoice.issued.v1` and `payment.received.v1` on the **same partition in that order**, and §3.4's loop processes serially, so `MarkInvoiced` always lands before `MarkPaid` is attempted. Likewise `credit.released.v1` follows `payment.received.v1` in one Billing transaction on the same partition (`saga.md` §6, "Ordering guarantees").

Therefore an unmet precondition on **first** delivery is impossible, and every unmet precondition in practice is a **stale** redelivery — exactly the case `saga.md` §6 declares safe to ignore. Point 1 is a shared-spec promotion candidate (`requirements.md` §4), raised by #7 and independently reached here; it is **not** applied to `specs/shared/` by this feature.

---

## 5. The transactional unit — composing what already exists

### 5.1 One generic handler, ten specific commands

`SagaFactHandler.HandleAsync(SagaFact fact, CancellationToken ct) : Task<SagaFactResult>` is the only transactional code path. The ten `ICommandHandler<Handle…FactCommand>` wrappers are one-line delegations to it — explicit commands per the dispatcher ruling, zero duplicated orchestration logic.

1. `SagaStepTable.For(fact.EventType)` — absent or `Skip` ⇒ return without any I/O (SO2; also unreachable in practice because §3.5 filters first, and the belt-and-braces is deliberate).
2. `idempotentConsumer.RunOnceAsync(fact.EventId, ConsumerName.OrdersSaga, work, ct)` — **the existing `IdempotentConsumer`, unmodified**, dedup-insert-first exactly as feature 14 built and proved it. `Duplicate` ⇒ return `SagaFactResult.Duplicate` (**R18**).
3. Inside `work(ct)`, all in the one ambient transaction that `IUnitOfWork` opened:
   - `orders.GetByIdAsync(UniqueId.From(fact.CorrelationId), ct)` — `null` ⇒ write a `saga_ignored_facts` row with marker `unknown_order` (**SO8**) and return; the dedup record stands.
   - Precondition mismatch ⇒ `saga_ignored_facts` row with observed/expected status (**R25**), return.
   - Otherwise apply the step's aggregate call(s), then `orders.SaveChangesAsync(ct)` — which drains `Order.DomainEvents` into `outbox` rows and saves, feature 14's transactional save, unchanged — and, if the step owes a command, `commandStore.EnqueueAsync(…, ct)` (**SO3**).
4. `HandleAsync` returns `{ Outcome, Enqueued }`. **Only** when the outcome is `Processed` **and** a command was enqueued — i.e. strictly after the transaction committed — does the wrapping `ICommandHandler` publish the matching dispatch-owed application event through `IDispatcher.PublishAsync` (§5.5).

One transaction therefore contains: the dedup record + the aggregate change + the outbox records + the pending-command record. That is R17 plus SO3 in one sentence, and it needs no new transaction plumbing: every collaborator resolved from the same DI scope enlists in the same `IDbContextTransaction` automatically (feature 14, `design.md` §2.1 — this is why `IOrderRepository` has no `tx` parameter).

**Scope discipline.** `SagaFactsConsumer` is a singleton `BackgroundService`; it creates **one `IServiceScope` per message** and resolves `IDispatcher` from it — the shape `OrdersCreateResponder` already established, and the reason `Dispatcher` is registered scoped rather than singleton.

### 5.2 Why the command is issued after commit, not inside the transaction

Issuing inside `work` would hold the order row's locks and the dedup index's lock across up to ~16.5 s of NATS retries, and — worse — would put the command on the wire before its causal state is durable, re-opening the early-fact race §4.4 closes. Issuing after commit opens a crash window (committed, never issued, including a crash before the in-process hop runs); the pending-command row closes it: the sweeper re-issues any `pending` row older than a grace period (§6.4). Re-issue is safe because every command is idempotent by (`orderReference`, operation) — `saga.md` §6 layer 3, and the reply contracts (`already_reserved`, `already_held`, `already_released`, `created: false`) exist for exactly this.

### 5.3 What "every step recorded" means here — the acceptance criterion, made concrete

The feature's acceptance bullet *"every step recorded"* is discharged by four durable artefacts, none of them in memory only:

| What happened | Where it is recorded | Asserted by |
|---|---|---|
| A status progression | An `outbox` row → a fact on the stream → (feature 24) the timeline | `SagaHappyPathTests`, `SagaCompensation*Tests` |
| A fact deliberately ignored | A `saga_ignored_facts` row with observed + expected status, or marker `unknown_order` | `SagaPreconditionTests` (R25, SO8) |
| A command owed, issued, parked or resumed | A `saga_commands` row: `status`, `attempts`, `last_error`, `next_attempt_at`, `sent_at` | `SagaCommandRetryTests` (SO3–SO5) |
| A redelivery absorbed | The `processed_events` row that already existed | feature 14's `IdempotentConsumerTests` + `SagaPreconditionTests` |

Every one of them is additionally accompanied by a structured log line carrying `correlationId`, per CLAUDE.md's logging rule.

### 5.4 The ignored-fact record

`EfCoreSagaIgnoredFactRecorder.RecordAsync(...)` inserts a `SagaIgnoredFact` row **in the caller's ambient transaction**: `event_id`, `event_type`, `order_id` (nullable — the unknown-order case), `correlation_id`, `observed_status` (nullable), `expected_status` (nullable), `marker` (`precondition_unmet` | `unknown_order`), `recorded_at`. A durable record and not a log line, because R25's matrix test must assert the observed and expected status were *recorded*, and because *"why did the saga ignore this?"* is an operations question the database should answer. The write only ever happens inside a first-delivery `RunOnceAsync`, so it is idempotent under the dedup layer. The entity, its configuration and its index already exist — nothing is created here but the adapter.

### 5.5 What plays the `@Saga` role in .NET — the fast path, and why it is not the guarantee

This is the largest genuine #8 delta, so it is argued rather than asserted.

**#7's shape.** Fact `CommandHandler` commits → publishes a dispatch-owed event on the in-memory `EventBus` → the `@Saga` class `OrderSagas` maps it through an RxJS `ofType` stream into an `Issue…Command` → that handler performs the NATS issue. The stream subscription is inherently **off** the consumer's await chain, which is how #7 got SO10 for free.

**Why the naive .NET translation is wrong.** `IDispatcher.PublishAsync` is a direct `await` over resolved `IEventHandler<T>` instances. Translating #7 literally would put the whole SO4 retry budget on the consume loop — and with no responder in existence until phase 9/10, "the responder is absent" is the **normal** case for this feature's entire life. Three parked orders at boot would then serialise ~50 s of consume-loop occupation before any other fact could be read, and every subsequent fact would queue behind a dead responder. It stays under `max.poll.interval.ms` (§3.2) so it would not rebalance — which is exactly what makes it dangerous: it would be slow and correct, and nothing would fail.

**The composition adopted.**

1. The fact `ICommandHandler` publishes one of five **dispatch-owed application events** (`SagaDispatchEvents.cs`: `OrderPlacedFactRecorded`, `OrderMarkedStockReserved`, `CreditRejectionRecorded`, `OrderConfirmedBySaga`, `OrderMarkedDespatched` — plain records carrying `OrderId` and `CorrelationId`), strictly after commit, through `IDispatcher.PublishAsync`.
2. `OrderSagas.cs` holds five tiny `IEventHandler<T>` classes — the direct analogue of #7's five `ofType` streams, in one file with #7's own file name so the benchmark maps one-to-one. Each does exactly one thing: `signal.Signal(new SagaCommandRef(orderId, SagaCommandKind.X))`.
3. `ChannelSagaCommandSignal` (singleton) writes the reference to a **bounded** `Channel<SagaCommandRef>` (capacity 1 024, `BoundedChannelFullMode.DropWrite`) and returns immediately. `Signal` is synchronous and non-blocking; the consume loop returns at once (SO10).
4. `SagaCommandDispatchWorker : BackgroundService` drains that channel, opening one DI scope per item and calling `SagaCommandDispatcher.DispatchAsync(orderId, command, ct)` — where the NATS issue and the SO4 retries actually happen.
5. `SagaCommandSweeper` calls the **same** `SagaCommandDispatcher`, directly, never through the channel and never through `IDispatcher`.

**Why the indirection at steps 1–2 is kept even though a direct call would do.** It is a deliberate parity cost, of exactly the kind CLAUDE.md already accepts for Notifications and Projector: #7 used its framework's `EventBus` + `@Saga` here, so a #8 that collapsed the hop into a method call would make this feature's effort number reflect a different architecture rather than a different language. It is recorded as a parity trade-off, not as a claim that the hop earns its keep on its own.

**Why dropping a signal is safe, stated as the guarantee it rests on.** `DropWrite` means a full channel silently discards the fastest path to a command whose row is already committed as `pending`. The sweeper re-issues any `pending` row older than `PendingGraceMs` (§6.4), so the observable consequence of a dropped signal is *latency*, bounded by the sweep interval, and never a lost command. This is the same property the crash window has, which is the point: **the in-process hop is only ever an optimisation over a durable queue that would deliver the same command anyway** — #7's gate constraint, carried verbatim, and the one sentence that makes an in-memory bus admissible inside a distributed saga.

**The rejected alternative, named so the gate can overrule it cheaply.** Awaiting the dispatch inline in the fact command handler (no events, no channel, no worker) is simpler and violates no written rule. It is rejected for the reason in the second paragraph above, and because SO10 would then be unprovable rather than merely unproven.

---

## 6. Command issuing — port, retry, park, sweeper

### 6.1 The port and the NATS adapter

```csharp
public interface ISagaCommands
{
    Task<StockReserveReplyPayload>   ReserveStockAsync(StockReserveRequestPayload request, CancellationToken ct);
    Task<StockReleaseReplyPayload>   ReleaseStockAsync(StockReleaseRequestPayload request, CancellationToken ct);
    Task<DespatchCreateReplyPayload> CreateDespatchAsync(DespatchCreateRequestPayload request, CancellationToken ct);
    Task<CreditHoldReplyPayload>     HoldCreditAsync(CreditHoldRequestPayload request, CancellationToken ct);
    Task<InvoiceIssueReplyPayload>   IssueInvoiceAsync(InvoiceIssueRequestPayload request, CancellationToken ct);
}
```

The five request/reply payload records are transcribed from `asyncapi.yaml` into `Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs`, beside the existing `StockCheckPayloads.cs`, and serialised through the existing `RpcJson` (which uses the one shared `JsonWire.Options`). They are **not** added to `src/Contracts`: `Contracts` carries the *fact* wire contract, and feature 15 already established that RPC payloads live in the service that speaks them. `CreditHoldRequestPayload.Amount` is the `Money` object of `asyncapi.yaml` (`{ amount, currency }`) with `amount` a `long` — never a `decimal`, never a narrowing cast.

`NatsSagaCommandsAdapter` reuses feature 15's `NatsStockAvailabilityChecker` shape verbatim in structure: the shared singleton `INatsConnection`, `RpcJson`, a per-call `NatsSubOpts { Timeout }`. The class name is fixed by feature 42's own acceptance text, which names `NatsSagaCommandsAdapter`; do not rename it.

**Error taxonomy — the pre-42 shape, deliberately.**

| Observed | Raised | Retryable? |
|---|---|---|
| `NatsNoRespondersException` | `SagaCommandTransportError` (reason: no responder subscribed) | yes |
| `NatsNoReplyException`, or a reply whose `Data` is null | `SagaCommandTimeoutError` | yes |
| A reply body that is an `RpcError` whose `code` is one of the **nine terminal business codes** | `SagaCommandRejectedError` → `rejected`, a terminal end state | **no** — feature 42 |
| A reply body that is an `RpcError` with any other `code` | `SagaCommandTransportError` | yes — feature 42 fails open to the transient side deliberately |
| A well-formed typed reply, any `outcome` including `rejected` | returned normally | n/a (SO6) |

The `UNAVAILABLE`-vs-`TIMEOUT` split is the same distinction feature 15 paid a blocking review defect to keep (`progress/history.md`, feature 15, D1) and it is preserved here by using two distinct exception types rather than one. **Feature 42 owned the next refinement and has landed it** (approved 2026-09-04) — the `RpcError`-body row above is now split on its `code` into a terminal-business set of nine and a transient remainder, with a `rejected` end state in `saga_commands`. The two rows above are the post-42 taxonomy; this feature shipped the single un-split row that the first version of this table described, and that version is superseded rather than deleted so the sequencing stays legible. What this feature was required to leave in place, and did: the two distinct exception types (the `UNAVAILABLE`-vs-`TIMEOUT` split feature 15 paid a blocking review defect to keep), the `RpcError` classification in **one** place, and `saga_commands.status` wide enough for a fourth token (`varchar(10)`; `rejected` is 8 characters).

**A business rejection is not an error (SO6).** `outcome: rejected` from `stock.reserve` or `credit.hold` resolves normally, the dispatcher marks the command `sent`, and nothing else happens — the responder has emitted (or will emit, via its own outbox) the rejection **fact**, and only that fact moves the saga (`saga.md` §2's "single most important rule"). The idempotent-repeat outcomes (`already_reserved`, `already_held`, `already_released`, `created: false`) are likewise plain successes.

### 6.2 The in-line retry policy (SO4)

| Setting (`OrdersSagaOptions.Command`) | Default | Meaning |
|---|---|---|
| `TimeoutMs` | `5000` | Per-attempt NATS request budget — the same value `NatsOptions.StockCheckTimeoutMs` already uses |
| `MaxAttempts` | `3` | In-line attempts before parking |
| `BackoffMs` | `500` | Base delay, doubling between attempts (500 ms, then 1 000 ms) |
| `LeaseMs` | `60000` | How long a claimed row is invisible to a concurrent claim (SO11, §6.3) — comfortably above the ~16.5 s worst case |

Worst-case in-line occupation: 3 × 5 000 + 500 + 1 000 = **16 500 ms**. Since §5.5 this runs on the dispatch worker, it bounds the dispatch/sweep cycle, never the consume loop or the consumer group (§3.2). Delays go through the existing `IClock`-style abstraction so unit tests run instantly with a fake. **The order status is never touched while retrying (R29)**: the state change was committed before the first attempt and no retry path opens a transaction on the aggregate.

The dispatcher is invoked from exactly two places — the dispatch worker (fast path) and the sweeper (guarantee) — and its header comment states the §3.2 budget derivation, so any future tuning re-checks it against `max.poll.interval.ms` rather than against a number nobody can source.

### 6.3 The `saga_commands` store — pending, sent, parked, and the lease

The table exists (§7). The adapter is new. Columns used, exactly as configured:

| Column | Use here |
|---|---|
| `id` | domain-generated `UniqueId` |
| `order_id`, `order_reference` | correlation, and one half of the RPC idempotency key |
| `command` | `stock.reserve` \| `stock.release` \| `despatch.create` \| `credit.hold` \| `invoice.issue` |
| `payload` | the full typed request, serialised through `RpcJson` at enqueue time from the loaded aggregate. `nvarchar(max)` — MS-SQL has no `json` type, and insertion order is preserved, which is why the payload parity claim is semantic rather than byte-exact (CLAUDE.md) |
| `triggering_event_id` | the fact that owed this command — the causal link and feature 27's join key |
| `status` | `pending` → `sent` \| `parked` (→ `sent`); a fourth `rejected` token is feature 42's |
| `attempts`, `last_error`, `next_attempt_at` | park bookkeeping **and** the SO11 lease |
| `created_at`, `updated_at`, `sent_at` | audit |
| unique `(order_id, command)` | mirrors the RPC key (`orderReference`, operation): a step can never owe the same command twice |

**Enqueue** adds the row through the ambient `DbContext` inside the fact's transaction. A duplicate-key violation (MS-SQL 2601/2627) on `(order_id, command)` means the command is **already owed or already sent**, which is not an error: the adapter catches it exactly as `ProcessedEventLedger` catches its own, detaches the entry, and returns `EnqueueOutcome.AlreadyEnqueued`; the fact handler then returns `Processed` **without** an enqueued command, so no dispatch signal is published. It is logged at warning with the `correlationId`, because reaching it means a dedup record was lost.

**Claim (SO11).** Both callers claim through one conditional update, and proceed only if it affected exactly one row:

```sql
UPDATE dbo.saga_commands
   SET next_attempt_at = @leaseUntil, updated_at = @now
 WHERE id = @id
   AND status IN ('pending', 'parked')
   AND (next_attempt_at IS NULL OR next_attempt_at <= @now)
```

`next_attempt_at` therefore carries two meanings that never collide: *when a parked row is next due* and *when a claimed row's lease expires*. A claimed row is excluded from every subsequent claim until the lease elapses, and a process that dies mid-attempt leaves a row that becomes claimable again automatically. **No new column, and therefore no migration** — the existing `(status, next_attempt_at)` index serves the predicate.

**Claim-due (the sweeper's batch)** selects with the hint triple this repository already measured — `WITH (UPDLOCK, READPAST, ROWLOCK)`, which on this MS-SQL image under `READ_COMMITTED_SNAPSHOT ON` **skips rather than blocks** (measured in feature 14, `progress/history.md`) — over:

```sql
(status = 'pending' AND created_at <= @pendingCutoff AND (next_attempt_at IS NULL OR next_attempt_at <= @now))
OR (status = 'parked' AND next_attempt_at <= @now)
```

The `pending` branch's extra lease condition is what makes the lease apply to both statuses; the existing `(status, created_at)` index serves its first two predicates.

**Outcomes.** `MarkSentAsync` sets `status='sent'`, `sent_at`, `next_attempt_at = NULL`. `ParkAsync(attemptsMade, error)` sets `status='parked'`, accumulates `attempts`, records `last_error` (truncated to a sane length) and sets `next_attempt_at = now + min(30 s × 2^parkCycles, ParkRetryCapMs)`. `sent` means "a reply was delivered" — **never** "the saga advanced"; advancement is the fact's job alone.

### 6.4 Parked is not dead — the sweeper (SO5), and what it deliberately is not

**The problem.** R29 says exhausted retries dead-letter the triggering fact — but the DLQ machinery is feature 27's, and this feature runs for two whole phases against responders that do not exist (§8). Silently dropping an exhausted command would strand every order at its first step; blocking the consume loop forever would strand every *other* order too. The design must make "the responder is absent" a **visible, self-recovering** state.

**The mechanism.** `SagaCommandSweeperBackgroundService` is structurally identical to the existing `OutboxRelayBackgroundService`: a `PeriodicTimer` loop (which does not queue missed ticks, so a slow cycle delays the next rather than stacking), one DI scope per cycle, an `Enabled` flag, failures logged and retried on the next tick, and `SagaCommandSweeper` itself a plain class exposed through `ISagaCommandSweeper` so the loop can be unit-tested against a fake with no database — feature 14's `IOutboxRelay`/`OutboxRelayLoopTests` shape, copied because it is already reviewed.

Each cycle: claim the due batch (§6.3) in one short transaction, then dispatch each row **outside** that transaction through `SagaCommandDispatcher` — called directly, never through `IDispatcher` and never through the channel, because the sweeper is the durability backstop and must not depend on the layer it exists to back up.

| Setting (`OrdersSagaOptions.Sweeper`) | Default |
|---|---|
| `Enabled` | `true` |
| `IntervalMs` | `30000` |
| `PendingGraceMs` | `10000` (the SO3 crash window, and the drop-tolerance of §5.5) |
| `ParkRetryCapMs` | `900000` (15 min) |
| `BatchSize` | `20` |

Park backoff is capped and **indefinite** — the same no-give-up stance the outbox relay takes for publication, for the same reason: giving up requires somewhere to give up *to*, and that place is feature 27's. Every park, every failed sweep attempt and every resumption logs structured JSON with `correlationId`, `command`, `attempts` and `last_error`, so the stall is loud and `SELECT * FROM saga_commands WHERE status = 'parked'` is the operator's whole view.

**Why not the alternatives.** (a) *Not acknowledging the fact and letting Kafka redeliver*: blocks the consume loop for every order, fights `max.poll.interval.ms`, and turns "Billing is down" into consumer-group churn. (b) *Publishing to `<topic>.dlq` now*: the topics exist (phase 5) but the headers, redrive tooling and consumer-retry semantics are feature 27's entire acceptance surface, and doing half of it here would pre-empt that spec with an undesigned fragment. (c) *A status-derived nudge with no durable row*: recoverable but not observable — no attempt count, no last error, nothing to `SELECT`.

**The honest divergence, restated for the gate.** Until feature 27, an exhausted command **parks and keeps retrying on a capped schedule** instead of dead-lettering the triggering fact. The fact was acknowledged and deduped; recovery does not need it again, because the `saga_commands` row carries everything (SO3). The shared matrix already splits R29 accordingly, inherited from #7 (`requirements.md` §1.1).

### 6.5 Seam summary for feature 27

1. Wrap the fact handler's rejection path with attempts/backoff/DLQ publication — the `IdempotentConsumer` caller seam recorded in `specs/outbox_and_idempotency/design.md` §7, unchanged here, and now reachable because this feature builds the first consumer.
2. Attach DLQ publication **and** the `order.saga_failed.v1` timeline entry to the **park transition** (§6.3's `ParkAsync`), on first park only. `OrderSagaFailedPayload` already exists in `src/Contracts`; the aggregate method that raises it does not, and feature 27 adds it.
3. Metrics: parked-command count and oldest-parked age join outbox lag as R59's gauges — both one `SELECT` over `saga_commands`.
4. `traceparent` propagation: `KafkaFactPublisher` deliberately writes no `traceparent` header today (feature 14's documented gap); the consumer must read one when it appears, so §3.5's envelope parse keeps the header dictionary rather than discarding it.

---

## 7. No migration — the reuse dividend, verified rather than assumed

**#7 wrote migration `0003` for `saga_commands` and `saga_ignored_facts` and recorded it as a cost at its gate (its open point 4). #8 writes nothing.** Both tables, all their columns and all four indexes were created in phase 6 by `src/Orders/Infrastructure/Persistence/Migrations/20260901100855_InitialCreate.cs`, with `SagaCommandConfiguration` and `SagaIgnoredFactConfiguration` already mapping them.

Checked column by column against what this design needs, and the answer is that nothing is missing:

| Needed by | Column / index | Present |
|---|---|---|
| SO3 enqueue | `id`, `order_id`, `order_reference`, `command`, `payload`, `triggering_event_id`, `status` default `pending`, `created_at`, `updated_at` | ✅ |
| SO4/SO5 park bookkeeping | `attempts` (default 0), `last_error` (`nvarchar(max)`), `next_attempt_at` (`datetime2(3)`), `sent_at` | ✅ |
| SO11 lease | reuses `next_attempt_at` — **no new column** (§6.3) | ✅ |
| "a step can never owe the same command twice" | unique `IX_saga_commands_order_id_command` | ✅ |
| sweeper pending-grace predicate | `IX_saga_commands_status_created_at` | ✅ |
| sweeper parked-due predicate + lease | `IX_saga_commands_status_next_attempt_at` | ✅ |
| feature 42's fourth status token | `status varchar(10)` — `rejected` is 8 characters | ✅ |
| R25 / SO8 record | `saga_ignored_facts` with `observed_status`, `expected_status`, nullable `order_id`, `marker`, and `IX_saga_ignored_facts_correlation_id` | ✅ |

**Therefore: this feature adds no migration, and must not.** If the implementer believes a column is missing, that is a finding to report, not a migration to write.

---

## 8. Living and testing without Fulfillment and Billing

### 8.1 Integration tests: stand-in NATS responders, feature 15's precedent extended

`tests/Orders.IntegrationTests/StandInSagaResponders.cs` follows `StandInFulfillmentStockCheckResponder` exactly — real `NatsConnection` subscriptions on the five subjects, started per test with programmable answers and request recording, a real subscribe-probe before returning (never a fixed delay), and the same disposal discipline (review D2's `finally` shape). It is imported only by integration tests; **no "temporary" responder may appear under `src/`**.

Crucially the stand-ins must also stand in for the responders' *outbox* side: in the real system `stock.reserved.v1` and its siblings arrive because Fulfillment or Billing committed and relayed them. The harness therefore publishes the corresponding fact envelopes **directly to the real Kafka topics**, keyed by `correlationId`, after the stand-in replies — so the tests exercise the true loop: command out over real NATS, fact in over real Kafka, aggregate advanced through real MS-SQL.

Infrastructure: the existing `KafkaContainerFixture` (`apache/kafka:4.3.1`, 6 partitions, `KAFKA_AUTO_CREATE_TOPICS_ENABLE=false`) **extended to create all three fact topics**, the existing `NatsContainerFixture` (`nats:2.14.5-alpine`) and the existing `MsSqlContainerFixture` (with `READ_COMMITTED_SNAPSHOT ON`, feature 14's fix). A new xUnit collection joins all three fixtures — the shape `KafkaCollection` already established for two.

### 8.2 The live compose stack, meanwhile — designed, not discovered

With phases 9 and 10 unbuilt, the deployed stack has **no responder on any of the five subjects**, and `otc.orders.facts.v1` already holds `order.placed.v1` for **`ORD-000007`, `ORD-000008` and `ORD-000009`** — placed during feature 15's wrap-up verification against the real stack. #7 hit exactly this, with the same order numbers, and designed the steady state rather than treating it as a bug. #8 adopts that, and states it here so a live boot surprises nobody:

1. First boot (`AutoOffsetReset.Earliest`, no committed offsets for `orders.saga`) consumes those three `order.placed.v1` facts.
2. Each is processed normally: a `processed_events` row, **no** status change (R19), a `saga_commands` row for `stock.reserve`, a dispatch signal, then `NatsNoRespondersException` on every attempt → 3 in-line attempts ≈ 16.5 s → **parked**, loudly logged.
3. Steady state: **three `parked` rows**, re-attempted on capped backoff up to every 15 min, each sweep logging its failure. `SELECT * FROM saga_commands WHERE status = 'parked'` is the operator's view. Orders placed live from now on behave identically — accepted at `placed`, then parked at `stock.reserve`.
4. When feature 17's responder first comes up, the next sweep succeeds unattended and the stranded sagas resume. **The recovery story and the crash story are the same mechanism**, which is the point of §6.4.

Because the dispatch is off the consume loop (§5.5), the three parked orders delay nothing else — a boot with the naive inline composition would have taken ~50 s to reach the newest fact.

If a clean baseline is preferred to watching the parked rows resume, the established recreate procedure (`docker compose -f docker-compose.infra.yml down -v` → up → `dotnet run --project src/Seed` → re-place) empties the topics and the tables. **Either outcome is correct**; whichever is observed must be recorded in `progress/impl_order_saga_orchestrator.md`, not left to surprise.

---

## 9. Configuration, DI registration and boot validation

`OrdersSagaOptions` carries three nested groups — `Kafka` (bootstrap servers, poll timeout), `Command` (§6.2) and `Sweeper` (§6.4) — populated by an `Action<OrdersSagaOptions>` from `Program.cs`, exactly as `OrdersOutboxOptions` and `OrdersAcceptanceOptions` already are. Environment variables reuse the existing names (`KAFKA_BOOTSTRAP_SERVERS`, `NATS_URL`) and add only the tuning knobs, each with a comment.

`AddOrdersSaga(this IServiceCollection, Action<OrdersSagaOptions>)` registers **one explicit line per port** — no assembly scan, per CLAUDE.md:

- `IFactStreamSubscriber` → `KafkaFactStreamSubscriber` (scoped; the consumer client itself is created per `ConsumeAsync` call and disposed with it)
- `ISagaCommands` → `NatsSagaCommandsAdapter` (scoped, over the **existing singleton** `INatsConnection` registered by `AddOrdersAcceptance` — no second connection)
- `ISagaCommandStore` → `EfCoreSagaCommandStore` (scoped, over the same scoped `DbContext` the repository uses, which is what puts the enqueue in the ambient transaction)
- `ISagaIgnoredFactRecorder` → `EfCoreSagaIgnoredFactRecorder` (scoped)
- `ISagaCommandSignal` → `ChannelSagaCommandSignal` (**singleton** — it owns the channel)
- `SagaFactHandler`, `SagaCommandDispatcher`, `SagaCommandSweeper` + `ISagaCommandSweeper` (scoped)
- `AddHostedService<SagaFactsConsumer>()`, `AddHostedService<SagaCommandDispatchWorker>()`, `AddHostedService<SagaCommandSweeperBackgroundService>()`

`OrdersHost.CreateBuilder` gains a **third** delegate, `configureSaga`, and calls `AddOrdersSaga` **after** `AddOrdersOutbox`/`AddOrdersAcceptance` and **before** `AddDispatcher` — the ordering rule that file's own comment already states: every port a handler needs must be registered before the dispatcher's validation pass runs. `ValidateOnBuild`/`ValidateScopes` stay forced on, so a port omitted from the list above is a **boot failure**, and the fourteen new `ICommandHandler`/`IEventHandler` implementations are discovered by the existing assembly scan with the existing "exactly one handler per command" validation. `Program.cs` and `OrdersDispatcherRegistrationTests` are updated for the new parameter and nothing else.

**Zero new packages.** `Confluent.Kafka` 2.15.0, `NATS.Net` 3.2.0, `Microsoft.Extensions.Hosting`, `Options`, `Logging.Abstractions` and EF Core are all already pinned in `Directory.Packages.props` and already referenced by `src/Orders/Orders.csproj`. `System.Threading.Channels` is part of the BCL in `net10.0`. The phase commit message's package section will read *"none"*.

---

## 10. Architecture rules — one amendment, one addition

**`FactPublisherConfinementTests` as written forbids this feature outright**, and that has to be dealt with explicitly rather than by moving code into a namespace where it does not belong. Today's rule is:

```csharp
Types.InAssemblies(DomainAssemblies.All)
     .That().DoNotResideInNamespaceMatching(@"\.Infrastructure\.Outbox(\.|$)")
     .ShouldNot().HaveDependencyOn("Confluent.Kafka")
```

Its stated intent (OI16) is R14's last sentence: *"No command handler, aggregate or domain service publishes directly."* That is a **producer** confinement. A fact-stream **consumer** is not the hazard it names, but the rule as written matches the whole `Confluent.Kafka` namespace and so would fail on `KafkaFactStreamSubscriber` regardless of intent.

**Proposed change — narrower on the producer, plus a new rule that did not exist:**

1. `FactPublisherConfinementTests` forbids, outside `*.Infrastructure.Outbox`, dependency on the **producer types only**: `Confluent.Kafka.IProducer`, `Confluent.Kafka.Producer`, `Confluent.Kafka.ProducerBuilder`, `Confluent.Kafka.ProducerConfig` (NetArchTest matches dependency names by prefix, so these four cover the producer surface). The test's message and doc-comment say why, and cite this section.
2. **New** `FactConsumerConfinementTests` forbids, outside `*.Infrastructure.Messaging.Consumers`, dependency on `Confluent.Kafka.IConsumer`, `Confluent.Kafka.Consumer`, `Confluent.Kafka.ConsumerBuilder`, `Confluent.Kafka.ConsumerConfig`. Namespace-scoped, so features 23 and 24 inherit it the moment they add their own consumers.

Net effect: the repository gains a rule it did not have and keeps the one it did, with the producer half stated in terms of what it actually protects. **Both must be armed** — the amended producer rule against a `ProducerBuilder` reference added to `Application/`, and the new consumer rule against a `ConsumerBuilder` reference added to `Presentation/`.

This is a change to a **test** file, not to `specs/shared/`, and it is raised at the gate (`progress/spec_order_saga_orchestrator.md`, row 5) because it narrows the letter of an existing guard — the one class of change this repository has repeatedly paid for getting wrong quietly.

No other architecture rule changes. `DomainPurityTests`, `CqrsDomainPurityTests`, `OrdersDomainMustNotDependOnContracts`, `DomainDecimalTests` and `SharedKernelHasNoPackagesTests` all stay green untouched, because nothing in this feature is added under any `Domain/`.

---

## 11. Testing approach

### 11.1 Levels and files

| File | Level | Proves |
|---|---|---|
| `SagaStepTableTests` | domain-pure unit | The full table: **every fact × every one of the nine statuses** — aggregate call(s), owed command, ignore, skip (SO2), reason mapping, compensation-step construction. `Order` instances built directly; no store, no broker, no framework |
| `SagaFactHandlerTests` | unit (fakes) | The §5.1 composition: duplicate ⇒ nothing; unknown order ⇒ SO8 record; precondition unmet ⇒ R25 record; enqueue happens inside the transaction; the returned `SagaFactResult` |
| `SagaFactCommandHandlerTests` | unit | Delegation, and that the dispatch-owed event is published **only** on `Processed`-with-enqueue — never on duplicate, ignored, or processed-without-enqueue |
| `OrderSagasTests` | unit | Each of the five events in ⇒ its own `SagaCommandRef` signalled, and nothing else (SO3 fast-path row) |
| `SagaCommandDispatcherTests` | unit (fake port + fake delay) | SO4 attempt count and backoff schedule; SO6 business rejection ⇒ `sent`, never retried; park on exhaustion with attempts and error |
| `SagaCommandDispatchWorkerTests` | unit | SO10 — signalling returns before the RPC issue completes; one scope per item; a failing dispatch does not kill the worker |
| `SagaCommandSweeperLoopTests` | unit | No-overlap self-scheduling, claim → dispatch → reschedule (`OutboxRelayLoopTests` shape, fake `ISagaCommandSweeper`) |
| `SagaFactTopicsTests`, `SagaRpcSubjectsTests` | unit | The three topic addresses and five subjects read out of `asyncapi.yaml` **as text** — the `OrdersFactTopicTests`/`RpcSubjectsTests` discipline, never retyped |
| `NatsSagaCommandsAdapterTests` | unit | Timeout / no-responders / `RpcError`-body taxonomy per subject, against the narrow request-client seam feature 15 established |
| `SagaHappyPathTests` | integration | **R19 – R24**, matrix case names verbatim |
| `SagaPreconditionTests` | integration | **R25**, **SO8**, and the whole `saga.md` §6 redelivery table swept literally |
| `SagaCompensationStockRejectedTests` | integration | **R26**, including zero requests observed on the release subject |
| `SagaCompensationCreditRejectedTests` | integration | **R27**, **R28**, **SO6**, **SO7** |
| `SagaCommandRetryTests` | integration | **R29** retry clause, **SO3** (crash-window: a `pending` row committed with *no* signal is still issued by a sweep), **SO4**, **SO5** |
| `SagaCommandStoreTests` | integration | **SO11** lease exclusion and expiry; enqueue enlisting in the ambient transaction; duplicate enqueue ⇒ `AlreadyEnqueued`, not a failed transaction |
| `SagaConsumptionTests` | integration | **SO1** (a fact published before the group ever subscribed is consumed) and **SO9** (a throwing handler leaves the committed offset unchanged and the fact is redelivered) |

Shared-matrix case names are used **verbatim** where the matrix names them; local `SO` rows use the names fixed in `requirements.md` §3.

### 11.2 The fact-emission arming obligation, per branch

CLAUDE.md: *"Every branch that emits — or deliberately suppresses — a domain fact must be guarded by a test that fails when the emission is deleted"*, **with double force where the branch has no live caller yet**. This feature has eight such branches and **five of them have no live caller**, because no Fulfillment or Billing responder exists. They are enumerated in `tasks.md` group J as individual, tickable arming rows, each requiring the named failing test and its verbatim message in `progress/impl_order_saga_orchestrator.md`:

| # | Branch | Emits / suppresses | Live caller today? |
|---|---|---|---|
| 1 | `credit.approved.v1` step | emits exactly one `order.confirmed.v1` (via `Confirm`) | no |
| 2 | `credit.released.v1` step | emits exactly one `order.completed.v1` (via `Complete`) | no |
| 3 | `stock.rejected.v1` step | emits `order.cancelled.v1` with reason `stock_rejected` **and empty** compensation steps | no |
| 4 | `stock.released.v1` step, `credit_rejected` branch | emits `order.cancelled.v1` with one `stock_released` compensation step | no |
| 5 | `stock.released.v1` step, `order_cancelled` branch | emits `order.cancelled.v1` with reason `operator_cancelled` | **no — and no producer at all until feature 25** |
| 6 | `stock.rejected.v1` step | **suppresses** any `stock.release` command (R26) | no |
| 7 | `invoice.issued.v1` step | **suppresses** any further command (R23) | no |
| 8 | `order.placed.v1` / `credit.rejected.v1` steps | **suppress** any status change (R19, R27) | yes (`order.placed.v1`) |

Arming follows CLAUDE.md's protocol exactly: back up the file by copy (**never** `git checkout --` on an untracked file), introduce the deletion, run the **named** test, record that it FAILS and the message verbatim, restore from the backup, **force the rebuild** (`touch` the restored file or `dotnet build --no-incremental`), re-read the changed line, then run green.

### 11.3 The A11 debt, owed to this feature by name

`progress/review_orders_acceptance.md` closes with advisory **A11**: `EfCoreOrderNumberAllocator` has **no direct test** — neither its `WITH (UPDLOCK, ROWLOCK)` concurrency claim nor its self-seeding branch over a **non-empty** `orders` table — and it names **feature 16** as the slot, because this feature owns the saga's transactions and inherits the serialised-placement ceiling that allocator creates. #7's equivalent assurance came from its reviewer's live probes (24 concurrent allocators; a counter continuing from `ORD-900000`), which #8 has not reproduced.

So `tasks.md` group I adds `tests/Orders.IntegrationTests/OrderNumberAllocatorTests.cs` with two cases: N concurrent allocations against real MS-SQL yielding a gap-free, duplicate-free sequence, and a first allocation against a table already holding `ORD-000009` continuing at `ORD-000010`. **Test-only; `EfCoreOrderNumberAllocator` itself is not touched.** If a case fails, that is a finding about feature 15 to report, not something to fix here.

---

## 12. Out of scope — restated, because each has a tempting adjacent edge

- **DLQ, consumer retry-to-dead-letter, `order.saga_failed.v1`, metrics, tracing**: feature 27, attaching at §6.5's seams. Do not publish to a `.dlq` topic here, and do not add an aggregate method to raise the fourteenth fact.
- **Terminal-vs-retryable `RpcError` classification**: feature 42. Keep the classification in one place, comment it, and do not implement the split.
- **The responders themselves**: features 17–22. Stand-ins live in `tests/`, never in `src/`.
- **Projector timeline and notifications**: features 24 and 23 — the consumption-map columns that are not the orchestrator's.
- **Operator cancellation initiation** (`orders.cancel`, unwinding from `credit_approved`/`confirmed`): feature 25+. §4.3's `order_cancelled` branch can already *finish* it and is armed accordingly.
- **`payment.register`**: the Gateway/Billing pair. The orchestrator only ever sees its consequences as facts.
