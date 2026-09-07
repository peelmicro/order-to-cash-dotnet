# `projector_read_model` — Design (.NET 10 / C# 14 / MongoDB, assessment #8)

> Stack-specific. Everything here is #8's realisation of `R50` – `R55` and of the local `PR1` – `PR44`; **none of it belongs in `specs/shared/`**. Read with [`requirements.md`](./requirements.md) open — that file is the rule, this file is the mechanism, and nothing here may add to a rule.
>
> #7's own `specs/projector_read_model/design.md` (NestJS / TypeScript / MongoDB, as amended by A1) is the source for §3 – §7's *shape*; every §5.5 algorithm clause is `PR10`'s and is reimplemented here from `PR10`, not transliterated from #7's JavaScript. **§10 is the ported-idiom ledger and is the section a reviewer should read first**: it is the only place that answers, boundary by boundary, *what made this correct over there, and does that thing exist here?*

---

## 1. Scope, and what already exists

`src/Projector` today is a four-file scaffold — `Projector.csproj` (references `SharedKernel` + `Contracts`, no packages, no `OutputType`) and one `README_PLACEHOLDER.cs` per layer folder, of which `Domain/README_PLACEHOLDER.cs` declares `ProjectorDomainPlaceholder`. That type is **load-bearing**: `tests/Architecture.Tests/DomainAssemblies.cs` names it to obtain the Projector assembly, so every domain-purity, no-`decimal`, Cqrs-purity and Kafka/NATS-confinement rule in the repository already ranges over this service and becomes non-vacuous the moment real types land. Do not delete it.

This feature turns the scaffold into the **fourth Kafka fact consumer** in the system (after Orders' saga orchestrator and Notifications) and the **only runtime writer** of `otc_read_model.order_timeline`. It consumes **all fourteen** facts — the first consumer that must (`domain-model.md` §7.3: the orchestrator takes ten, Notifications seven). It maintains one denormalised document per order. It publishes an update signal on NATS. It answers nothing, exposes no HTTP surface, and owns no aggregate.

Three existing pieces of #8 are the reference implementations, and this feature copies their shape rather than inventing:

| Concern | Copy from | Why |
|---|---|---|
| Consume-only service: host, options, DI extension, `BackgroundService`, routing dictionary | `src/Notifications` (feature 23, phase 11) | Landed immediately before this; same "no outbox, no responder, no aggregate" shape. Its review's D1 (one routing table, not a filter beside a switch) and D3 (`ConsumerConfig` asserted by reflection, not argued in prose) are inherited here as `PR36` and `PR38`. |
| Kafka offset contract | `src/Orders/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs` + `tests/Orders.IntegrationTests/SagaConsumptionTests.cs` | `AutoOffsetReset.Earliest`, `EnableAutoOffsetStore = false`, `StoreOffset` after the handler, and the only precedent in this repository for **reading the committed offset back from the broker** (`SagaIntegrationTestSupport.ReadCommittedOffsetsAsync`, `consumer.Committed(...)`). |
| The MongoDB document, its indexes and its wire rendering | `src/Seed/Infrastructure/Mongo/{OrderTimelineDocument,MongoSeedWriter,SeedMongoConfig}.cs` (feature `seed_job`, phase 5) | **The seed already writes this collection**, already at `timelineOrderVersion = 2`, already with `statusRank`, `processedEventKeys`, `events[].causationId` and the **partial** `uq_order_reference` index. That is the document shape; this feature does not invent a second one. |

**What #8 does not have to do that #7 did.** #7 needed `PR29` — a boot backfill deriving `statusRank`/`processedEventKeys` from documents its own seed had written without them — and needed a one-line edit to `apps/seed` to make `uq_order_reference` partial. Neither applies here: `src/Seed/Infrastructure/Mongo/` has exactly one commit (`181060f`) and that revision already writes all four fields and the partial index. `requirements.md` §2.10 records why `PR29`'s id is deliberately not reused.

---

## 2. Where everything lives

```
src/Projector/
├── Projector.csproj                                     ← + OutputType Exe, + 6 PackageReferences, + Cqrs
├── Program.cs                                           ← env → ProjectorOptions → ProjectorHost.CreateBuilder → RunAsync
├── ProjectorHost.cs                                     ← the NotificationsHost shape (ValidateOnBuild/ValidateScopes forced on)
├── Domain/                                              ← ZERO driver/framework/serialiser types (PR28)
│   ├── README_PLACEHOLDER.cs                            ← KEEP: ProjectorDomainPlaceholder, named by DomainAssemblies
│   ├── FactEnvelope.cs                                  ← the domain's own input record (PR28)
│   ├── ProjectionDelta.cs                               ← + TimelineEntryDelta, OrderHeaderDelta, ReferenceFill
│   ├── OrderStatusRank.cs                               ← PR12's table; ImpliedStatusOf / RankOf
│   ├── MoneyFormat.cs                                   ← long minor units + currency → text. No decimal, no float.
│   ├── Summaries.cs                                     ← the fourteen summary/detail builders (PR16)
│   ├── SummaryResult.cs
│   ├── FactProjection.cs                                ← Project(FactEnvelope) → ProjectionDelta; fourteen arms
│   └── UnknownFactTypeError.cs                          ← : DomainError, stable Code
├── Application/
│   ├── Ports/
│   │   ├── ConsumerName.cs                              ← COPY OF src/Orders/Application/Ports/ConsumerName.cs
│   │   ├── IFactStreamSubscriber.cs                     ← COPY OF src/Orders/Application/Ports/IFactStreamSubscriber.cs
│   │   ├── IReadModelWriter.cs                          ← the ONLY write surface the application sees
│   │   └── IUpdateSignalPublisher.cs
│   ├── Signals/
│   │   ├── OrderStreamUpdate.cs                         ← openapi.yaml's shape, projector-owned (§7.3)
│   │   └── TimelineStreamEntry.cs                       ← …with causationId (PR33)
│   ├── Commands/
│   │   ├── ProjectFactCommand.cs                        ← ONE command (PR27)
│   │   └── ProjectFactCommandHandler.cs                 ← ONE handler, delegation only
│   └── ProjectionApplyService.cs                        ← delta → writer → signal; owns PR18/PR19's ordering
├── Infrastructure/
│   ├── ProjectorOptions.cs                              ← + ProjectorKafkaOptions, ProjectorMongoOptions, ProjectorNatsOptions
│   ├── ProjectorServiceCollectionExtensions.cs          ← AddProjector: one explicit line per port
│   ├── Messaging/
│   │   ├── IdempotentConsumer.cs                        ← THE VARIANT (PR23/PR24). The banner is load-bearing.
│   │   └── Consumers/
│   │       ├── KafkaFactStreamSubscriber.cs             ← the ONE type touching Confluent.Kafka's consumer API
│   │       └── ProjectorFactTopics.cs                   ← the three topic literals
│   ├── Persistence/
│   │   ├── ReadModelCollection.cs                       ← IMongoCollection<BsonDocument> factory + element-name constants
│   │   ├── PlaceholderDocument.cs                       ← PR8's skeleton, in the seed's element order
│   │   ├── DeltaToPipeline.cs                           ← ProjectionDelta → the ONE $set stage (§5.3, §5.4)
│   │   ├── TimelineOrder.cs                             ← the causal-order expression, shared by apply and migration
│   │   ├── MongoReadModelWriter.cs                      ← IReadModelWriter over the variant (§5.1, §5.2)
│   │   ├── ReadModelIndexes.cs                          ← PR22, PR39
│   │   └── TimelineOrderMigration.cs                    ← PR32, PR35
│   └── Signal/
│       └── NatsUpdateSignalPublisher.cs                 ← PR17, over the shared singleton INatsConnection
└── Presentation/
    └── ProjectorFactsConsumer.cs                        ← the ONE Kafka BackgroundService; PR3/PR4/PR36

tests/Projector.UnitTests/            ← no container, ever
tests/Projector.IntegrationTests/     ← Testcontainers: mongo:8.3.8, apache/kafka:4.3.1, nats:2.14.5-alpine
```

`ReadModelBootstrap.cs` lives in `Infrastructure/Persistence/` and is the `IHostedService` of `PR43`.

**Both test projects are added to `OrderToCash.sln`.** `quality.sh` runs `dotnet test "$SLN"`, so solution membership is what puts them in the coverage pass; nothing else needs editing. `tests/Architecture.Tests/Architecture.Tests.csproj` **already** references `src/Projector/Projector.csproj` — no edit there.

### 2.1 Packages

`Projector.csproj` gains, all already pinned in `Directory.Packages.props` — **no new `PackageVersion`, no version bump**:

| Package | Version (already pinned) | Confined to |
|---|---|---|
| `MongoDB.Driver` | 3.11.1 | `Infrastructure/Persistence/` |
| `Confluent.Kafka` | 2.15.0 | `Infrastructure/Messaging/Consumers/` (enforced by `FactConsumerConfinementTests`) |
| `NATS.Net` | 3.2.0 | `Infrastructure/Signal/` + the DI extension |
| `Microsoft.Extensions.Hosting` | 10.0.11 | `Program.cs`, `ProjectorHost`, the hosted services |
| `Microsoft.Extensions.Options` | 10.0.11 | every `IOptions<T>` site |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.11 | every `ILogger<T>` site |

plus `ProjectReference` to `src/Cqrs/Cqrs.csproj` (`PR27`). **No `Microsoft.EntityFrameworkCore.*`, no `Microsoft.Data.SqlClient`** (`PR21`), and **no `ProjectReference` to `src/Seed`** — `Seed.csproj` references Orders, Fulfillment and Billing and would drag EF Core in transitively. The *test* projects may and do reference `src/Seed` (`PR40`, `PR44`); `requirements.md` §5 already scopes that.

`tests/Projector.UnitTests` takes the `Notifications.UnitTests` package set (`Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging.Abstractions`) plus `Confluent.Kafka` (for `PR38`'s reflection read of `ConsumerConfig`) and `MongoDB.Driver` (for `PR41`/`PR10`'s assertions on the emitted `BsonDocument` stage tree — a pure unit concern, no container). `tests/Projector.IntegrationTests` takes `Testcontainers`, `Testcontainers.MongoDb`, `Confluent.Kafka`, `NATS.Net`, `MongoDB.Driver`.

### 2.2 Configuration

**No `.env.example` change.** #7 added `PROJECTOR_KAFKA_CLIENT_ID` and `PROJECTOR_CONSUMER_GROUP` because NestJS read them from the environment; #8's convention (established by `NotificationsOptions`, `OrdersSagaOptions`, `BillingOptions`) puts service-fixed values in the options class as defaults and reads only the **shared infrastructure** coordinates from the environment. `Program.cs` therefore reads exactly what every other #8 host reads and nothing new:

- `KAFKA_BOOTSTRAP_SERVERS`, falling back to `localhost:${KAFKA_HOST_PORT:-9092}` — `Notifications/Program.cs`'s expression verbatim.
- `NATS_URL`, falling back to `nats://localhost:${NATS_CLIENT_HOST_PORT:-4222}` — `Orders`/`Billing`/`Fulfillment` `Program.cs`'s expression verbatim.
- `MONGO_HOST`, `MONGO_HOST_PORT`, `MONGO_INITDB_ROOT_USERNAME`, `MONGO_INITDB_ROOT_PASSWORD`, `MONGO_DB_READMODEL` — **through the same construction `SeedMongoConfig.Load()` uses**, re-declared in `ProjectorOptions` (the projector may not reference `src/Seed`) with the identical `Uri.EscapeDataString` handling of the credential, because a password containing `@` or `/` silently produces a malformed URI otherwise.

`GroupId`, `ClientId`, poll timeout, the three topics, the two NATS subjects and the database/collection names are **constants in code**, asserted by `PR38`/`PR1`/`PR17`, not environment variables.

---

## 3. The document shape — the seed's, not a second one

`src/Seed/Infrastructure/Mongo/OrderTimelineDocument.cs` is the shape. The projector **does not re-declare it as a C# class**: it works throughout in `IMongoCollection<BsonDocument>` with camelCase **string-literal** element names collected in `ReadModelCollection` (`PR41`), and the seed's class is used as the **oracle** from the test projects instead of as a twin that nothing compares. This is a deliberate departure from #7, which re-declared the interface in `apps/projector` and explicitly declined a text-parity guard between the two: a TypeScript `interface` is erased at run time and could not have been the thing that wrote the bytes, so #7 had no cheaper option. #8 does — `tests/Projector.IntegrationTests/ProjectionWriteTests.cs` › `PR41_AStoredDocumentDeserialisesThroughTheSeedsOrderTimelineDocumentClassMapWithNoLoss` deserialises what the projector actually wrote through `OrderTimelineDocument`'s real BSON class map and asserts no loss, which is strictly stronger than two declarations agreeing by intent.

### 3.1 Element names, types and order

| Path | BSON type | Source |
|---|---|---|
| `_id`, `orderId` | String — `Guid.ToString("D")`, lowercase hyphenated | the fact's `correlationId` (`R12`) |
| `orderReference` | String / **explicit `null`** | `order.placed.v1` only |
| `orderDate` | String, `yyyy-MM-dd'T'HH:mm:ss.fff'Z'` | `order.placed.v1` only |
| `retailer.code`, `retailer.gln`, `company.code`, `company.gln` | String | `order.placed.v1` only |
| `retailer.name`, `company.name`, `items[].name` | **always `null` in a projected document** | master data, on no fact — `R54` forbids the lookup (`PR9`) |
| `status` | String | `PR12` |
| `cancellationReason` | String / null | `order.cancelled.v1`, `$ifNull` |
| `currency` | String / null | `order.placed.v1` |
| `totals.initialAmount`, `.initialDiscount`, `.totalAmount` | **Int64** | `order.placed.v1` |
| `items[].productCode` | String | `order.placed.v1` |
| `items[].quantity` | **Int32** | `order.placed.v1` |
| `items[].unitPrice`, `.lineDiscount` | **Int64** | `order.placed.v1` |
| `references.despatchReference` / `.invoiceReference` / `.paymentReference` | String / null | `order.despatched.v1` / `invoice.issued.v1` / `payment.received.v1`, `$ifNull` |
| `events[]` | Array of documents | §3.2 |
| `headerComplete` | Boolean | `false` in the placeholder, `true` only from `order.placed.v1` |
| `updatedAt` | String, same instant format | `$max` of `occurredAt` |
| `statusRank` | Int32 | `PR12` |
| `timelineOrderVersion` | Int32, `2` | `PR40` |
| `processedEventKeys` | Array of String, sorted | `PR23` |

**Element order is part of the shape.** `BsonDocument` equality is element-order sensitive, `PR15` says "BSON-identical" and `PR44` compares against the seed's own serialisation, so the order is fixed here rather than left to whichever pipeline stage happens to append a field. The top-level order is the seed class's declaration order:

`_id, orderId, orderReference, orderDate, retailer, company, status, cancellationReason, currency, totals, items, references, events, headerComplete, updatedAt, statusRank, timelineOrderVersion, processedEventKeys`

and it is achieved for free: `PlaceholderDocument` writes **every one** of these keys in that order via `$setOnInsert`, and the apply's `$set` only ever *updates* keys that already exist, which leaves their position untouched. A field appended by `$set` because the placeholder forgot it would land at the end and silently break `PR44` — which is why the placeholder is total over the shape rather than minimal.

### 3.2 The timeline entry

`{ eventId, eventType, occurredAt, summary, detail?, causationId }` — the seed's `TimelineEvent` declaration order, with `detail` **absent** (not null) when the fact has none, matching the seed's `[BsonIgnoreIfNull]`. `causationId` is last because that is where the seed declares it; it is nonetheless part of the public read contract (`PR33`, `openapi.yaml` `TimelineEntry.causationId`).

`detail` is built by serialising the domain's `IReadOnlyDictionary<string, object>` through `JsonWire.Options` and parsing the result with `BsonDocument.Parse` — one conversion, camelCase keys, and integers land as Int32 or Int64 by magnitude with no truncation. `PR44` deliberately excludes `events[].detail` from the seed-oracle comparison: the seed's fixtures carry a hand-written subset of the projector's `detail`, and `PR16`'s builders — not the seed — are `detail`'s oracle.

### 3.3 `_id` and the identity chain

`_id = orderId = correlationId`. `R12` makes `correlationId` always the order id, `asyncapi.yaml` makes it the partition key of all three fact topics, and `domain-model.md` §7.1 calls it "the read-model document key" outright. **Nothing is generated in this service** — `UniqueId.New` and `Guid.NewGuid` appear nowhere under `src/Projector/`, which is one of the three legs of `PR15`.

---

## 4. The domain layer — a projection, not an aggregate

The projector owns no aggregate and enforces no invariant. Its domain is one pure, total function from an envelope to a **store-agnostic description of the change**.

```csharp
// Domain/FactEnvelope.cs — the domain's own input. NOT Contracts.Envelopes.Envelope<T>:
// that is a wire type parameterised by payload, and the domain must switch on the
// payload's CLR type without knowing how it arrived (PR28, PR36).
public sealed record FactEnvelope(
    Guid EventId,
    string EventType,
    Guid CorrelationId,
    Guid CausationId,
    DateTimeOffset OccurredAt,
    object Payload);

// Domain/ProjectionDelta.cs
public sealed record TimelineEntryDelta(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string Summary,
    Guid CausationId,                                   // A1/PR30 — verbatim from the envelope
    IReadOnlyDictionary<string, object>? Detail);

public sealed record OrderHeaderDelta(
    string OrderReference, DateTimeOffset OrderDate,
    string RetailerCode, string BuyerGln,
    string CompanyCode, string SupplierGln,
    string Currency,
    long InitialAmount, long InitialDiscount, long TotalAmount,
    IReadOnlyList<OrderItemDelta> Items);

public sealed record ProjectionDelta(
    Guid OrderId,                                        // = CorrelationId
    TimelineEntryDelta Entry,
    string? ImpliedStatus,                               // null for the five status-less facts
    int StatusRank,                                      // 0 when ImpliedStatus is null
    string? CancellationReasonIfAbsent,
    string? DespatchReferenceIfAbsent,
    string? InvoiceReferenceIfAbsent,
    string? PaymentReferenceIfAbsent,
    OrderHeaderDelta? Header);                           // present ONLY for order.placed.v1
```

`FactProjection.Project(FactEnvelope) : ProjectionDelta` is a **switch on `envelope.Payload`'s CLR type**, fourteen arms, `_ => throw new UnknownFactTypeError(envelope.EventType)`. Switching on the type rather than on the `eventType` string is what makes `PR36` structural: the fourteen `eventType` literals live in exactly one table in the whole repository, `FactCatalog.PayloadTypesByEventType`, and `src/Projector/` contains none of them in a routing position (§9).

`Summaries.cs` holds the fourteen builders. Every expected string is `#7`'s `apps/projector/src/domain/summaries.ts` byte for byte:

| `eventType` | `summary` | `detail` |
|---|---|---|
| `order.placed.v1` | `Order {orderReference} placed for {retailerCode}` | — |
| `stock.reserved.v1` | `Stock reserved for {reservations.Count} line(s)` | — |
| `stock.rejected.v1` | `Stock rejected: {Σ(requested−available)} unit(s) short on {shortages[0].productCode}` | `shortages`, `reason` |
| `stock.released.v1` | `{Σ units} unit(s) released back to stock` + `" (compensation)"` iff `reason == "credit_rejected"` | `released`, `reason` |
| `credit.approved.v1` | `Credit hold of {MoneyFormat(heldAmount, currency)} approved` | — |
| `credit.rejected.v1` | `Credit hold of {MoneyFormat(requestedAmount, currency)} rejected ({reason})` | `reason`, `requestedAmount` |
| `credit.released.v1` | `Credit exposure released — ` + (`invoice paid` iff `reason == "invoice_paid"`, else `order cancelled`) | — |
| `order.confirmed.v1` | `Order confirmed (ORDRSP)` | — |
| `order.despatched.v1` | `Despatch {despatchReference} created` | — |
| `invoice.issued.v1` | `Invoice {invoiceReference} issued` | — |
| `payment.received.v1` | `Payment {paymentReference} received` | — |
| `order.completed.v1` | `Order {orderReference} completed` | — |
| `order.cancelled.v1` | `Order {orderReference} cancelled ({cancellationReason})` | `cancellationReason`, `compensationSteps` |
| `order.saga_failed.v1` | `Saga command "{command}" dead-lettered after {attempts} attempt(s)` | `command`, `attempts`, `lastError` |

`MoneyFormat.Of(long minorUnits, string currency)` renders the **integer** with a single ASCII space as the thousands separator and the currency code appended — `16130, "EUR"` → `"16 130 EUR"`. Integer arithmetic only: no `/ 100`, no `decimal`, no `ToString("N0")` with a culture (which would give `16,130` under `en-US` and `16.130` under `de-DE`). `DomainDecimalTests` already bans `decimal` from this namespace and becomes non-vacuous for it here.

**The two known seed/projector voice differences are recorded, not repaired** (`PR16`, `PR44`): the seed writes `"Credit hold of 16130 EUR approved"` (no grouping) and carries a hand-written `detail` subset. Both are inherited #7 differences.

---

## 5. The write — exactly two operations, and why

This is the heart of the feature, and its shape is a requirement (`PR6`) rather than a style note because the read-then-write version passes every single-threaded test. It is also the local instance of the class `CLAUDE.md` records from feature 45: **`IF NOT EXISTS (SELECT …) INSERT` is check-then-act, and so is `FindAsync(...)` then `UpdateOneAsync(...)`.** The filter *is* the check, here as there.

### 5.1 Phase 1 — bring the document into existence

```csharp
await collection.UpdateOneAsync(
    Builders<BsonDocument>.Filter.Eq("_id", orderId),
    Builders<BsonDocument>.Update.SetOnInsert(PlaceholderDocument.For(orderId, occurredAtWire)),
    new UpdateOptions { IsUpsert = true },               // NOT the default — UpdateOptions.IsUpsert is false
    cancellationToken);
```

`$setOnInsert` applies **only** on insert, so this is a no-op by construction against an existing document — idempotent without inspection. It never touches `events`, `status`, `statusRank`, `references` or `processedEventKeys` of a document that already exists.

Its one race is the classic upsert race: two concurrent deliveries for an **absent** order both fail to match and both attempt an insert; one wins, the other raises `E11000` on `_id`. Handled explicitly and **only here** (`PR7`): catch `MongoWriteException` whose `WriteError.Category == ServerErrorCategory.DuplicateKey`, retry this one operation **exactly once**, after which it can only match. Every other error propagates so the fact is redelivered.

> **Do not delegate this to the driver's retryable writes.** `retryWrites` defaults to `true`, but retryable writes require a replica set or a sharded cluster; the compose stack and `MongoDbBuilder("mongo:8.3.8")` are a **standalone**, where the driver does not retry. #7's node driver behaved identically for the same reason, which is why #7 also hand-wrote this retry. Ledger row **L22**.

### 5.2 Phase 2 — the atomic apply

```csharp
var applied = await collection.FindOneAndUpdateAsync(
    Builders<BsonDocument>.Filter.And(
        Builders<BsonDocument>.Filter.Eq("_id", orderId),
        Builders<BsonDocument>.Filter.Ne("processedEventKeys", dedupKey)),   // ← THE IDEMPOTENCY CHECK
    Builders<BsonDocument>.Update.Pipeline(stages),                          // ONE $set stage, §5.3
    new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After },  // ← NOT the default
    cancellationToken);
// applied is null  ⇒ Duplicate: nothing written, no signal (R51, PR18)
// applied non-null ⇒ Processed: exactly this fact applied, exactly once
```

Three things about this call are hand-supplied in #8 where they were free or explicit in #7, and each is a ledger row with a guard:

1. **`IsUpsert` on Phase 1** defaults to `false` on `UpdateOptions` (L10).
2. **`ReturnDocument` defaults to `Before`** on `FindOneAndUpdateOptions<T>` (L9, `PR42`). With the default, duplicate detection still works — a non-match is still `null` — so the deletion-family mutation cannot see it, and every signal would silently carry the *previous* status. This is the ledger's own failure shape and is why `PR42` exists as a requirement.
3. **`{ processedEventKeys: { $ne: key } }` against a document where the field is absent** matches (`$ne` matches missing), which is correct and is what makes the pipeline total over an odd document.

**Under two concurrent deliveries of the same `eventId`:** MongoDB takes the document-level write lock and, on a WiredTiger write conflict, retries the whole operation **re-evaluating the query predicate**. The loser therefore re-runs its filter after the winner's `$setUnion` landed, finds its own key present, matches nothing and returns `null` — `Duplicate`, which is the correct answer, not an error. This is a **server** property of `mongo:8.3.8`, identical on both sides of the trilogy (L2); what is *not* identical is the client-side concurrency shape, which is why `PR7`'s test drives it from **separate `MongoClient` instances** rather than one pool (L24).

**Under out-of-order delivery:** every field's new value is a monotone function of its old value and the incoming fact — `$max` for the rank and `updatedAt`, `$ifNull` for the references and the cancellation reason, a full re-sort for the array. Monotone means commutative here, which is what makes `PR15` true and `R52` automatic rather than case-analysed.

### 5.3 The pipeline — one `$set` stage

`DeltaToPipeline.For(ProjectionDelta, dedupKey) : BsonDocument[]` is a **pure function returning plain `BsonDocument`s**, unit-testable with no container, which is where `$ifNull`/`$cond`/`$sortArray` correctness is cheapest to pin.

```js
[{ $set: {
  processedEventKeys: { $sortArray: {
      input: { $setUnion: [ { $ifNull: ["$processedEventKeys", []] }, ["<dedupKey>"] ] },
      sortBy: 1 } },                                                  // PR15 — $setUnion's order is unspecified

  events: <§5.4's causal-order expression>,                           // PR10

  statusRank: { $max: [ { $ifNull: ["$statusRank", 0] }, <rank> ] },  // PR12
  status:     { $cond: [ { $gt: [ <rank>, { $ifNull: ["$statusRank", 0] } ] }, "<impliedStatus>", "$status" ] },

  "references.despatchReference": { $ifNull: ["$references.despatchReference", <despatchOrNull>] },   // PR11
  "references.invoiceReference":  { $ifNull: ["$references.invoiceReference",  <invoiceOrNull>]  },
  "references.paymentReference":  { $ifNull: ["$references.paymentReference",  <paymentOrNull>]  },
  cancellationReason:            { $ifNull: ["$cancellationReason",            <reasonOrNull>]   },   // PR13

  updatedAt: { $max: ["$updatedAt", "<occurredAtWire>"] },            // PR14 — never a clock

  timelineOrderVersion: 2,                                            // PR32/PR40 — every write stamps the version

  // present ONLY when delta.Header is (order.placed.v1) — PR9, unconditional, no $ifNull
  orderReference, orderDate, "retailer.code", "retailer.gln", "company.code", "company.gln",
  currency, "totals.initialAmount", "totals.initialDiscount", "totals.totalAmount", items,
  headerComplete: true
}}]
```

Notes that matter:

- **Every array and rank operand is `$ifNull`-guarded.** `$setUnion` and `$concatArrays` *error* on a missing field, and a missing `$statusRank` compares as **null**, which BSON orders below every number — so an unguarded `$gt: [1, "$statusRank"]` is `true` and would regress a document that has a status but no rank. No writer in #8 has ever produced such a document (`requirements.md` §2.10), but the guard costs nothing and makes the pipeline total, which is what `PR31` asks of the ordering and what a hand-inserted document deserves.
- **`status` uses `$cond` on a strict `$gt`; `statusRank` uses `$max`.** Both are evaluated against the *pre-update* document within one `$set` stage, so the two agree without ordering games. For a status-less fact `rank = 0`, `$max` is a no-op and `$gt` is false, so `status` rewrites its own value — total, with no conditional pipeline construction, which keeps `DeltaToPipeline` one shape.
- **The header fields are written unconditionally** when `delta.Header` is present. `order.placed.v1` occurs once per order and the dedup filter stops its redelivery; a conditional write would hide two different `order.placed.v1` facts for one order id behind a silent no-op (`PR9`).
- **Dotted output paths** (`"retailer.code"`, `"totals.totalAmount"`, `"references.invoiceReference"`) are supported by pipeline `$set` and create/overwrite only the named leaf, which is exactly what keeps `retailer.name` at its placeholder `null` (`PR9`).
- The C# literals matter: `2` written as an `int` is BSON Int32 (`timelineOrderVersion`, `statusRank`, `items[].quantity`); every money value is written as a C# `long` and is therefore BSON Int64 (L17, `PR41`). A narrowing cast anywhere on this path is a defect, not something to make loud (`CLAUDE.md` § Money).

### 5.4 The causal timeline order (`PR10`, `PR30`, `PR31`)

> **`requirements.md` `PR10` is the rule; nothing here may add to it.** This section is `PR10`'s realisation in the MongoDB aggregation language available on `mongo:8.3.8`.

#### 5.4.1 The rule, restated as an algorithm

```
sort E by (occurredAt asc, depth(e) asc, eventId asc)

where for the tie group G(e) = { x ∈ E : x.occurredAt == e.occurredAt }:
  cause(e) = the x ∈ G(e) with x.eventId == e.causationId and x ≠ e, or none
  depth(e) = 0 if cause(e) is none, else 1 + depth(cause(e))
  evaluated as a fixpoint over at most |E| relaxation rounds, capped at |E|
```

Four properties, each a requirement clause rather than an implementation nicety: it is a **topological order of the causal forest** (`depth(c) > depth(cause(c))`, so a fact is always stored after its cause — `R28`); it is **total** (`eventId` is unique); it is a **pure function of the stored entry set** (no arrival index, no clock, no generated id — `PR15`); and it **terminates on any input including a cycle**, because the round count is bounded a priori rather than by convergence (`PR31`).

**This is not decorative in #8.** #8's live facts produce three real tie groups per saga, all with real edges, because a derived fact reuses its triggering fact's `occurredAt` verbatim:

| Tie group | Where the shared `occurredAt` comes from |
|---|---|
| `credit.approved.v1` → `order.confirmed.v1` | `src/Orders/Application/Sagas/SagaStepTable.cs`: `order.Confirm(fact.OccurredAt, UniqueId.From(fact.EventId))` |
| `stock.released.v1` → `order.cancelled.v1` (`R28`) | `SagaFactHandler.cs`: `order.Cancel(..., fact.OccurredAt, UniqueId.From(fact.EventId))` |
| `payment.received.v1` → `credit.released.v1` → `order.completed.v1` (`R24`) | Billing reuses `ctx.OccurredAt` for both facts of the pair (`progress/impl_completion_pair_has_no_causal_edge.md`), and Orders then reuses `credit.released.v1`'s |

The **seed** fixtures, by contrast, place every fact at a distinct instant (`SagaFixtures.Build*`: `t0`, `+1m`, `+2m`, `+2m30s`, `+3m`, `+4m`, `+1d`, `+1d5s`, `+1d10s`), so **`PR44`'s seeded oracle does not exercise a single tie group**. That is why `PR10`/`PR31`'s proof is `TimelineCausalOrderTests` with adversarially chosen `eventId`s and not the oracle.

#### 5.4.2 Why the whole array is re-sorted on every apply

`depth` is a property of the **group**, not of the entry, and a fact can arrive before the fact that caused it — the three topics are partitioned by `correlationId` but carry **no ordering guarantee across topics**, and `stock.released.v1` (`otc.fulfillment.facts.v1`) and `order.cancelled.v1` (`otc.orders.facts.v1`) are on different ones. Placing a child at its arrival-time depth would fix it at `0` forever and make the final array a function of arrival order — a straight `PR15` violation a single-topic test could never catch. The array is bounded by fourteen entries; the cost is irrelevant.

#### 5.4.3 The expression

Still **one** `$set` stage inside the same single `FindOneAndUpdateAsync` — `PR6` is untouched. `TimelineOrder.Expression(BsonValue entriesInput, int cap)` returns it, and **both** the live apply and the migration (`PR32`) call that one method, differing only in the input (`$concatArrays[$ifNull[$events,[]], [entry]]` versus `$ifNull[$events,[]]`).

```js
{ $let: {
  vars: { all: <entriesInput> },
  in: { $let: {
    vars: { ranked: { $reduce: {
      input: "$$all",                                  // |all| rounds — ≥ |G| for every group
      initialValue: { $map: { input: "$$all", as: "e",
                              in: { $mergeObjects: ["$$e", { __depth: 0 }] } } },
      in: { $let: { vars: { prev: "$$value" }, in: { $map: {
        input: "$$prev", as: "e",
        in: { $let: {
          vars: { cause: { $arrayElemAt: [ { $filter: { input: "$$prev", as: "p", cond: { $and: [
                    { $eq: ["$$p.occurredAt", "$$e.occurredAt"] },              // same tie group
                    { $ne: ["$$p.eventId",   "$$e.eventId"] },                  // never itself
                    { $eq: ["$$p.eventId",   { $ifNull: ["$$e.causationId", null] }] }   // PR31: no causationId ⇒ no cause
                  ] } } }, 0 ] } },
          in: { $mergeObjects: [ "$$e", { __depth:
                 { $min: [ <cap>, { $add: [ { $ifNull: ["$$cause.__depth", -1] }, 1 ] } ] } } ] }
        } }
      } } } }
    } } },
    in: { $map: {
      input: { $sortArray: { input: "$$ranked",
                             sortBy: { occurredAt: 1, __depth: 1, eventId: 1 } } },
      as: "e",
      in: { $unsetField: { field: "__depth", input: "$$e" } }         // computed and DISCARDED
    } }
  } }
} }
```

- `$sortArray` needs server **5.2+**, `$unsetField`/`$setField` **5.0+**; `$reduce`, `$map`, `$filter`, `$let`, `$mergeObjects` are 3.4+. The stack is `mongo:8.3.8` and `MongoDbBuilder("mongo:8.3.8")` drives the same tag (L5, L6). **No `$function`, no server-side JavaScript** — that needs scripting enabled and is not available in the compose stack.
- `$$cause` missing ⇒ `$$cause.__depth` missing ⇒ `$ifNull → -1` ⇒ `depth 0`. That single expression covers all four of `PR31`'s cases: an edge outside the tie group, an edge naming a fact never received, a self-edge, and an entry with **no** `causationId` at all.
- The `$min` cap makes a **cycle** terminate with a deterministic (meaningless) order rather than an unbounded depth; `$map` preserves cardinality, so every entry is present exactly once whatever the input.
- `__depth` is **computed and discarded inside the stage**. Storing it would be the retired `events[].statusRank` mistake in a new costume: a second derived field that goes stale the moment a cause arrives late. `$unsetField` is used in preference to a `$map` that re-projects the six known fields, because `$unsetField` preserves any field a future writer added and cannot silently drop `detail`.
- Depth is a **BFS-level** order, not a DFS pre-order: two independent chains in one tie group interleave by level. Both are valid topological orders, `PR31`'s `eventId` fallback makes either deterministic, and no tie group this domain produces contains two independent chains. The simpler key is chosen deliberately, exactly as in #7.

### 5.5 What is *not* used, and why

| Rejected | Why |
|---|---|
| `ReplaceOneAsync` of a document rebuilt in C# | Requires a read; lost-update under concurrency; and it is what `MongoSeedWriter.SeedTimelinesAsync` does — correctly, because the seed is single-threaded, offline and authoritative over the whole document. |
| A multi-document transaction across a `processed_events` collection and `order_timeline` | The compose stack runs a **standalone** `mongo:8.3.8`, so transactions are unavailable — and even with them, one document is strictly better than two (§6.3). |
| A fifth MS-SQL database holding the canonical `processed_events` table | §6.3. Rejected because it is **weaker**, not because it is dearer. |
| An optimistic-concurrency `version` field with a compare-and-set retry loop | Correct, but a read-then-write with extra steps, and `PR15`'s byte comparison would then have to exclude it. |
| `$addToSet` + `$push` with `$each`/`$sort` (classic operators) | Cannot express `R52`'s conditional `status` in the same operation, forcing a second write or a read. |
| `IMongoCollection<OrderTimelineDocument>` with a re-declared twin class | §3 — the element names would then be produced by a class map nothing compares against the seed's, replacing an explicit constant with an inferred one. Literal names plus the seed's class map as the oracle is the stronger pair. |

---

## 6. Idempotency — the documented variant (`PR23` – `PR26`)

### 6.1 The class

`src/Projector/Infrastructure/Messaging/IdempotentConsumer.cs`. The type name is `IdempotentConsumer` because `.editorconfig` requires the file to match the type, and because `IdempotentConsumerParityTests` case 4 resolves `src/<Service>/Infrastructure/Messaging/IdempotentConsumer.cs` by path.

```csharp
public enum ConsumptionOutcome { Processed, Duplicate }

public sealed class IdempotentConsumer(IMongoCollection<BsonDocument> collection)
{
    /// scopeId      the document the dedup key lives in (= orderId)
    /// eventId      the fact's eventId
    /// consumer     the consumer name — the key is the PAIR (R17)
    /// stages       the projection, merged into the SAME atomic write
    /// afterApplied runs exactly once, AFTER the write, only when it matched
    public Task<ConsumptionOutcome> RunOnceAsync(
        Guid scopeId,
        Guid eventId,
        ConsumerName consumer,
        Func<string, BsonDocument[]> stages,
        Func<BsonDocument, CancellationToken, Task> afterApplied,
        CancellationToken cancellationToken);
}
```

The banner is load-bearing. `IdempotentConsumerParityTests` case 4 (`RequiresADocumentedDivergenceBannerFromACopyThatCannotShareTheCanonicalsTransaction`) has had **no subject since it was written** — it skips every service that either has no `IdempotentConsumer.cs` or *has* a relational `ProcessedEventConfiguration.cs`, and today that is every service. The projector is its first subject, and the case checks two things: the banner cites the canonical path literal `src/Orders/Infrastructure/Messaging/IdempotentConsumer.cs`, and it carries a line beginning `Divergence:`. The **third** line — `Behavioural conformance:` naming a file that exists — is not checked by that case; `PR24` therefore requires the projector's own `VariantBannerTests.cs` to check it, including `File.Exists` on the named path.

```
// VARIANT OF — src/Orders/Infrastructure/Messaging/IdempotentConsumer.cs
//
// Divergence: the ledger is not a `processed_events` row inside a SQL
// transaction; it is the `processedEventKeys` array of the read-model
// document itself, written by the SAME single findAndModify that applies the
// projection. There is no transaction to share, because there is nothing to
// keep consistent — the mark and the effect are the same bytes in the same
// write (design.md §6.3).
//
// Behavioural conformance: tests/Projector.IntegrationTests/IdempotentConsumerConformanceTests.cs
```

**`src/Orders` and `tests/Orders.*` are not edited by this feature** (`PR24`). The projector owns **no** `Infrastructure/Persistence/Configurations/ProcessedEventConfiguration.cs`, so `DiscoverCopyServices` never selects it and case 1's byte-identity set stays `{Orders, Notifications}`; and it owns no `ProcessedEventLedger.cs`, no `DbContext` and no `IUnitOfWork` (`PR21`).

### 6.2 Why `stages` is a parameter and the work is a post-apply callback

The canonical's `RunOnceAsync(eventId, consumer, work, ct)` runs `work` **inside** the transaction that inserts the dedup row. There is no transaction here, and the effect *is* the same write, so `work` cannot be "run inside" anything. Two shapes were considered:

- **`work` returns the stages** → `work` must then be invoked *before* the write, **including on duplicates**, and conformance case 2 (*the work does not run on a redelivery*) fails. That is not a test-fitting problem: the shape genuinely does not preserve the pattern's observable contract.
- **the stages are their own parameter and the work becomes the post-apply callback** → invoked exactly once on `Processed`, never on `Duplicate`, which *is* the canonical's observable contract. And it has a real job: it is where `PR18`'s signal publication lives, which makes "no signal for a suppressed redelivery" **structural** rather than a second `if`.

The second is adopted, exactly as in #7.

### 6.3 Why not a fifth MS-SQL database

The alternative — register the projector as a byte-identical `copy`, add `otc_projector` with the canonical `processed_events` table, adopt `IdempotentConsumer.cs` verbatim — is rejected, and not for cost.

The canonical's whole guarantee is *"the mark and the effect commit together"* (`R17`). Here the effect lives in **MongoDB** and the mark would live in **MS-SQL**. No transaction spans them, so a crash between commit and apply leaves the event permanently marked and never projected — a silently missing timeline entry that no redelivery can repair, because the mark suppresses it. Notifications hit the same wall and answered with an insert-first-then-compensating-delete, defensible for one lost email and **not** for a read model. The variant has no such window: there is nothing to keep consistent, because the mark and the effect are the same bytes in the same write. It is the *stronger* option here, which is why it is a `documented-variant` rather than an apology.

Cost accepted: `src/Projector` gains no MS-SQL and can therefore never adopt the canonical pair, so case 1's byte-identity branch will never cover it. That is exactly the gap the behavioural conformance suite closes — `PR25`, `PR26`.

### 6.4 The five conformance cases

#8 has **no generic copyable conformance suite** — `outbox_and_idempotency` design.md §6 defines the five cases in prose and Orders/Notifications each assert them in their own integration tests. So the five are written directly in `tests/Projector.IntegrationTests/IdempotentConsumerConformanceTests.cs`, against a **real** `mongo:8.3.8`, with the **real** `IdempotentConsumer`, one fixed scope document created per class, a dedup-only `$set` of the key as `stages`, and a counter as the post-apply callback:

| Case | Why it genuinely bites here |
|---|---|
| 1 first call `Processed`, callback once | the filter matches; `FindOneAndUpdateAsync` returns a document |
| 2 second call `Duplicate`, callback still once | the `$ne` no longer matches; `null`; callback skipped |
| **3 fresh instance over the SAME store → `Duplicate`** | the state is in MongoDB, not in a field of the class — the property an in-memory ledger fails |
| 4 two distinct `eventId`s both run | two distinct keys |
| **5 same `eventId`, different consumer name → both run** | **this is why the key is the pair and not the bare `eventId` already sitting in `events[]`** |

Case 5 needs a second `ConsumerName` member; `ConsumerName` already carries the full closed set `{OrdersSaga, Projector, Notifications}` in every copy, so the test uses `ConsumerName.Notifications` as the second name without inventing anything.

**`N10`'s rule binds every one of these:** an assertion that an absent side effect did not happen proves nothing about whether it was *attempted*. Each case asserts on `FindOneAndUpdateAsync`'s returned `null` **and** the callback counter, not only on the document's residue.

---

## 7. The update signal (`R55`, `PR17` – `PR19`, `PR42`)

### 7.1 The transport — inherited, not re-decided

**NATS core publish**, subjects `readmodel.order.updated.<orderId>` and `readmodel.timeline.appended.<orderId>`. This is #7's gate row 2, approved there and inherited verbatim; §11 row 2 of the gate record carries the citation and the three reasons (its delivery semantics are NATS's and `openapi.yaml` `/orders/stream` already accepts them in writing; it must fan out to **every** Gateway replica, which a shared Kafka consumer group cannot do; the subject hierarchy makes feature 25's `?orderId=` a subscription rather than client-side filtering). It is a deliberate widening of `CLAUDE.md`'s *"Kafka carries facts, NATS carries RPC"* from request-reply to *"…and publish-only read-model signals"*, mitigated by the signal having no responder, no reply subject, nothing awaited and no branch anywhere reading it.

### 7.2 Shape and ordering

Two publishes per **applied** fact, both built from the **post-apply document** (`ReturnDocument.After`, `PR42`), so a signal can never describe a state the store never held:

- `readmodel.order.updated.<orderId>` → `OrderStreamUpdate` — `eventId`, `orderId`, `orderReference`, `status`, `cancellationReason`, `references`, `totals`, `occurredAt`. `openapi.yaml` requires `[eventId, orderId, status, occurredAt]`.
- `readmodel.timeline.appended.<orderId>` → `TimelineStreamEntry` — `eventId`, `causationId` (`PR33`), `orderId`, `orderReference`, `eventType`, `occurredAt`, `summary`. `openapi.yaml` requires `[eventId, orderId, eventType, occurredAt, summary]`.

Both are serialised with `JsonWire.Options` (camelCase, nulls omitted, `InstantJsonConverter`'s three-fraction-digit `Z` format). Both records live in `src/Projector/Application/Signals/` and **not** in `src/Contracts`: `Contracts` is the wire contract versioned by `asyncapi.yaml`, these two subjects are deliberately absent from `asyncapi.yaml` (#7's gate row 2, inherited), and adding them to `Contracts` would assert a fact-stream status they do not have. Feature 25 may forward the bytes verbatim or declare its own reader; the seam is stated in §7.4.

Each publish builds a **fresh `NatsHeaders`** carrying `x-correlation-id = <orderId>`. `NATS.Client.Core`'s own XML documentation says `NatsHeaders` is not thread-safe and must not be shared across concurrent calls — `src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs` records the same finding as feature 17's `FS2`. Reusing one instance across two publishes is the defect this sentence exists to prevent (L36).

### 7.3 Failure handling, and the connection

`PR19`: **log and swallow**. Counter-intuitive, so the argument is restated: the projection has already applied, so a rethrow produces a redelivery that `PR6`'s filter suppresses — the signal could **never** be emitted on a later attempt, and the partition would block while failing forever. Losing a frame is already covered by `openapi.yaml` `/orders/stream`'s stated limitations (bounded buffer, client re-fetches, the read model is the source of truth).

**That argument has a precondition #7 got for free and #8 does not.** #7's `main.ts` did `await connect(...)` at boot, so a wrong `NATS_URL` failed the boot loudly. `NATS.Net`'s `NatsConnection` connects **lazily** on first use; #8's existing services never noticed because they are responders whose `SubscribeAsync` materialises the connection during startup. A publish-only projector with a wrong URL would project perfectly and signal nothing, forever, with `PR19` dutifully swallowing every failure into a log line — a silent, total loss of `R55`'s producer half that no test on a good URL can see. **Ledger row L38, and the subject of the one GATE row in `progress/spec_projector_read_model.md`** (row 1: connect eagerly in `ReadModelBootstrap`, making a misconfiguration a boot failure and leaving `PR19` for what it is for — a *transient* publish failure).

### 7.4 The seam with feature 25

`requirements.md` §4 states it line by line. Recorded here so feature 25 does not rediscover it: the Gateway subscribes to `readmodel.order.updated.*` / `readmodel.timeline.appended.*` (or the single-order subject when `?orderId=` is given), and excludes `{ statusRank: 0, processedEventKeys: 0, timelineOrderVersion: 0 }` from every read of `order_timeline` — `events[].causationId` is **public** (`PR33`) and is not excluded.

---

## 8. Wiring

### 8.1 `ProjectorHost` and boot ordering (`PR43`)

`ProjectorHost.CreateBuilder(string[] args, Action<ProjectorOptions> configure)` is `NotificationsHost.CreateBuilder`'s shape verbatim: `Host.CreateApplicationBuilder`, `ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }))` — **forced on in every environment**, because `Host.CreateApplicationBuilder` enables them only under `Development`, which is nowhere this repository runs — then `AddProjector(configure)`, then `AddDispatcher(Assembly.GetExecutingAssembly())` **last**, so a missing or duplicated `ICommandHandler<ProjectFactCommand>` is a boot failure rather than a first-dispatch surprise.

Registration order is the boot contract:

```csharp
services.AddHostedService<ReadModelBootstrap>();          // 1 — indexes (PR22) then migration (PR32)
services.AddHostedService<ProjectorFactsConsumer>();      // 2 — the only Kafka BackgroundService
```

`HostOptions.ServicesStartConcurrently` stays at its default `false`, so the host awaits `ReadModelBootstrap.StartAsync` to completion before calling the consumer's. `ReadModelBootstrap` implements `IHostedService` **directly, not `BackgroundService`** — a `BackgroundService`'s `StartAsync` returns at the first `await` inside `ExecuteAsync`, so making the bootstrap one would give the appearance of ordering with none of the substance. If the bootstrap throws, the host fails to start. `PR43` guards both halves: a unit assertion on the registration order and on `ServicesStartConcurrently`, and an integration case that the indexes and the migration are complete when `StartAsync` returns.

### 8.2 `AddProjector` — one explicit line per port

The `AddNotifications` shape: no assembly scan, every port bound explicitly (`CLAUDE.md` § Explicit DI registration).

```csharp
services.AddSingleton<IOptions<ProjectorKafkaOptions>>(Options.Create(options.Kafka));
services.AddSingleton<IMongoClient>(_ => new MongoClient(options.Mongo.ConnectionUri));
services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>()
    .GetDatabase(options.Mongo.Database)
    .GetCollection<BsonDocument>(ReadModelCollection.Name));      // one IMongoCollection<BsonDocument>
services.AddSingleton<INatsConnection>(_ => new NatsConnection(new NatsOpts { Url = options.Nats.Url }));
services.AddSingleton<IUpdateSignalPublisher, NatsUpdateSignalPublisher>();
services.AddSingleton<IdempotentConsumer>();
services.AddSingleton<IReadModelWriter, MongoReadModelWriter>();
services.AddSingleton<ProjectionApplyService>();
services.AddSingleton<IFactStreamSubscriber, KafkaFactStreamSubscriber>();
services.AddHostedService<ReadModelBootstrap>();
services.AddHostedService<ProjectorFactsConsumer>();
```

**Everything is a singleton, and that is a real difference from Notifications**, which registers most of its chain `Scoped` because it holds an EF Core `DbContext`. The projector holds none: `IMongoClient` is thread-safe and pools connections internally (one client per process — L24), `IdempotentConsumer` and `MongoReadModelWriter` hold no per-request state, and the whole point of `PR6` is that no state is carried between the two operations. `ValidateScopes` would reject a singleton `BackgroundService` consuming a scoped service, so there is no `IServiceScopeFactory` hop at all here — `ProjectorFactsConsumer` resolves `IDispatcher` once. `AddDispatcher`'s own registration lifetime is whatever `src/Cqrs` chooses; if it registers handlers as scoped, `ProjectorFactsConsumer` takes `IServiceScopeFactory` and creates a scope per message, exactly as `NotificationFactsConsumer` does. **The implementer follows whichever `src/Cqrs` actually does and does not change `src/Cqrs`.**

### 8.3 `Program.cs`

Reads the environment per §2.2 and calls `ProjectorHost.CreateBuilder(...).Build().RunAsync()`. `MONGO_INITDB_ROOT_PASSWORD` absent throws with the same `export $(grep -E ... .env | xargs)` hint `SeedMongoConfig` and `Notifications/Program.cs` both give — a missing credential must be a loud, actionable boot failure, never a default.

---

## 9. The consumer — routing with one source of truth (`PR1` – `PR4`, `PR36`, `PR37`, `PR38`)

`ProjectorFactsConsumer : BackgroundService` is the **only** `BackgroundService` in the service that touches a transport, and it subscribes to all three topics through `IFactStreamSubscriber` in one call, exactly as `NotificationFactsConsumer` does — the `ExecuteAsync` loop, the `OperationCanceledException` handling, the log-and-re-enter backoff and the malformed-envelope branch are copied from it.

**What is deliberately *not* copied is the routing shape.** Notifications shipped a `HashSet` filter beside a routing `switch`; deleting a filter entry silently stopped a fact being notified with both suites green (review round 1, D1), and its fix collapsed both into one `Dictionary` whose `Keys` **are** the filter. The projector consumes **every** fact, so even that dictionary would be a second list of fourteen strings that must agree with `asyncapi.yaml`. It has none:

```csharp
if (!FactCatalog.PayloadTypesByEventType.TryGetValue(envelope.EventType, out var payloadType))
{
    logger.LogError("Unknown eventType '{EventType}' on {Topic}[{Partition}]@{Offset} — acknowledged, not projected.", ...);
    return;                                                     // PR4: logged, never a bare return
}

var payload = JsonSerializer.Deserialize(envelope.Payload, payloadType, JsonWire.Options)
    ?? throw new JsonException($"Fact payload for '{envelope.EventType}' deserialised to null.");

await dispatcher.SendAsync(new ProjectFactCommand(new FactEnvelope(
    envelope.EventId, envelope.EventType, envelope.CorrelationId,
    envelope.CausationId, envelope.OccurredAt, payload)), cancellationToken);
```

`FactCatalog.PayloadTypesByEventType` is the one table, it already has a completeness test comparing it against `asyncapi.yaml`, and the domain then switches on the payload's **CLR type**. `PR2` closes the loop in both directions by comparing the catalogue's value types against `FactProjection`'s handled types; `PR36`'s structural test greps `src/Projector/` for the fourteen literals and permits hits only in log messages and test-facing constants, never in a routing structure.

`ProjectorFactTopics.All` is the three topic literals, `NotificationFactTopics`'s shape verbatim, with the same text-scan guard reading them out of `asyncapi.yaml` — three topic names, not fourteen event types, and they are a *subscription* list, not a routing table.

**Envelope validation** is `NotificationFactsConsumer.ValidateEnvelope`'s seven checks verbatim (`PR3`). **Envelope copying** (`PR37`) is the shape backlog id 58 was filed against: a hand-written field-by-field copy whose `correlationId` could be corrupted on a green suite. Every copied field is therefore guarded with a **sentinel value the test supplied**, at all three hops — wire → `FactEnvelope`, `FactEnvelope` → stored entry, post-apply document → both signal payloads. `Assert.NotEqual` on two ids proves non-collision and can never prove provenance (`CLAUDE.md`, feature 18); every one of these assertions compares against a value the test chose.

**The consumer configuration** (`PR38`) is `Orders`' `BuildConsumerConfig` with the projector's identity:

```csharp
BootstrapServers = options.BootstrapServers,
GroupId = "projector",                    // == ConsumerNames.ToToken(ConsumerName.Projector)
ClientId = "otc-projector",
AutoOffsetReset = AutoOffsetReset.Earliest,
EnableAutoCommit = true,                  // commits STORED offsets only
EnableAutoOffsetStore = false,
EnablePartitionEof = false,
```

`AutoOffsetReset.Earliest` is **deliberately the opposite of Notifications' `Latest`**, and the reason is this service's own obligation, not a preference. Notifications owns no aggregate and produces an **external** side effect: a fact its group never saw is an email nobody gets — an omission. The projector's store is **derived**, and its defining property, written into `feature_list.json`'s own acceptance bullet 2, is that replaying the topics reconstructs it. A projector starting at the log head produces a permanently and silently incomplete read model — a correctness violation, and one no test on a fresh topic can see, because on a fresh topic `Earliest` and `Latest` agree. `PR15` is what makes replay safe: a replayed fact either applies once or is suppressed, and either way the document converges to the same bytes.

Notifications' equivalent shipped **argued in prose and asserted by nothing**, which was review defect D3. So this one is asserted three ways: a reflection test on the private `BuildConsumerConfig` the runtime actually calls (`KafkaFactStreamSubscriberConfigTests`, the D3 shape); an integration case producing a fact **before** the group first subscribes and asserting it is still consumed; and `OffsetContractTests`, which asserts a throwing handler leaves the committed offset unchanged, **read from the broker** with `consumer.Committed(...)` over the full partition set — `tests/Orders.IntegrationTests/SagaIntegrationTestSupport.ReadCommittedOffsetsAsync` is the working precedent, copied. Never inferred from the fact that a redelivery happened: that is the exact defect `CLAUDE.md` records against the saga orchestrator's own offset task.

---

## 10. The ported-idiom ledger

> **How this table was produced.** The boundaries were **enumerated first**, from the artefacts rather than from intuition: every distinct place where #7's engine, language, driver or framework supplied a property that #8 must now obtain from somewhere. **54 boundaries were listed; 54 rows follow.** A boundary considered and dismissed is a row saying why. A boundary never listed is the failure mode this table exists to prevent.
>
> **Guard column.** Where the property came free in #7 and is hand-built here, the Guard column names a test, and `tasks.md` carries that test with the arming flag. A Guard entry is itself a countable claim: naming it here creates the obligation to see it fail, and does not discharge it. `—` means the property is supplied by the same mechanism on both sides and there is nothing to hand-build.

### 10.1 The document store — MongoDB server (identical `mongo:8.3.8` on both sides)

| # | Boundary | #7 relied on | #8 supplies it by | Guard |
|---|---|---|---|---|
| **L1** | Single-document write atomicity of `findAndModify` | the MongoDB server | the **same server**, `mongo:8.3.8` in compose and in `MongoDbBuilder("mongo:8.3.8")` | — |
| **L2** | A write conflict re-evaluates the query predicate, so the loser of two same-`eventId` deliveries matches nothing | the MongoDB server / WiredTiger | the same server | `ProjectionConcurrencyTests › PR7_TwoConcurrentDeliveriesOfOneEventIdFromSeparateClients_…` — same server, but **the client-side race shape differs** (L24), so it is proven, not assumed |
| **L3** | Two concurrent upserts on an absent `_id` — one wins, one raises `E11000` | the server's unique `_id` index | the same server; the **retry is hand-written**, exactly once, and only on Phase 1 | `ProjectionConcurrencyTests › PR7_TwoConcurrentDeliveriesOfDifferentEventIdsForAnAbsentOrder_…` |
| **L4** | `$setOnInsert` is a no-op on match | the server | the same server, via `Builders.Update.SetOnInsert` | `ProjectionWriteTests › PR6_…` (command monitoring shows one `update`, one `findAndModify`, no `find`) |
| **L5** | Update-with-aggregation-pipeline (server 4.2+) | the server | `Builders<BsonDocument>.Update.Pipeline(...)` against the same server | — |
| **L6** | `$sortArray` (5.2+), `$unsetField` (5.0+) | the server | the same server; recorded because a downgrade would silently turn "sorted in the document" into "sorted at read time" | `DeltaToPipelineTests › PR10_…` asserts the emitted stage tree |
| **L7** | `$setUnion`'s result order is **unspecified** | #7 wrapped it in `$sortArray` | the same wrapping — `$sortArray { sortBy: 1 }` over the union | `ReplayDeterminismTests › PR15_…` (BSON-identical document across a shuffled replay) |
| **L8** | A missing `$statusRank` compares as **null**, which BSON orders below every number | #7 hit this live and guarded every operand with `$ifNull` | the same `$ifNull` on every array and rank operand — total over any document shape | `DeltaToPipelineTests` asserts `$ifNull` on each operand |
| **L9** | `findOneAndUpdate` returns the **post**-update document | #7 passed `returnDocument: 'after'` **explicitly** | `FindOneAndUpdateOptions<T>.ReturnDocument` **defaults to `Before`** in MongoDB.Driver 3.x — set explicitly. With the default, deletion probes still pass and every signal carries the previous status | `UpdateSignalTests › PR42_TheOrderUpdatedSignalCarriesThePostApplyStatusAndReferences_NotThePreApplyOnes` |
| **L10** | Upsert on Phase 1 | #7 passed `{ upsert: true }` explicitly | `UpdateOptions.IsUpsert` **defaults to `false`** — set explicitly | `PlaceholderDocumentTests › R53_…` |
| **L11** | `createIndex` is idempotent when options match, and raises `IndexOptionsConflict` (85) when they do not | the server | the same server; the projector **fails to start** on code 85 with the offending index name and the `dropIndex` one-liner, rather than continuing | `ReadModelIndexesTests › PR22_RefusesToStartAgainstANonPartialIndexOfTheSameName_NamingTheIndex` |
| **L12** | The partial filter expression's stored form | #7 wrote the JS literal `{ orderReference: { $type: 'string' } }`, and its seed was **edited** to match | #8's seed landed first, and writes it through `Builders<OrderTimelineDocument>.Filter.Type(d => d.OrderReference, BsonType.String)` — which renders `$type` with BSON's **numeric** code, not the string alias. The projector must build it with the **identical** `Builders<…>.Filter.Type(field, BsonType.String)` call, not a hand-written `BsonDocument`, or the two definitions differ and neither service can start after the other | `ReadModelIndexesTests › PR39_TheSeedsIndexThenTheProjectors_AndTheProjectorsThenTheSeeds_RaiseNoIndexOptionsConflict` — **the row most likely to bite** |
| **L13** | A unique index treats indexed nulls as equal, so two placeholders collide | #7 discovered it at spec time and made the index **partial** | already partial in `MongoSeedWriter.EnsureIndexesAsync` (phase 5, commit `181060f`) — inherited, not built | `PlaceholderDocumentTests › PR8_TwoPlaceholdersWithNullOrderReferenceCoexistUnderThePartialIndex` |
| **L14** | Multi-document transactions | unavailable on a standalone; #7 designed around it | the same standalone; the variant needs none (§6.3) | `IdempotentConsumerConformanceTests › PR23_…` |
| **L15** | Document 16 MB limit / unbounded array growth | bounded by fourteen facts per order | identical; **dismissed** — no mechanism needed | — |
| **L16** | The distinction between an explicit `null` and an absent field | #7's `placeholderSkeleton` wrote explicit nulls | `PlaceholderDocument.For` writes explicit `BsonNull.Value` for every unknown scalar, and `detail` is **absent** (never null) when the fact has none, matching the seed's `[BsonIgnoreIfNull]` | `ProjectionWriteTests › PR41_AStoredDocumentDeserialisesThroughTheSeedsOrderTimelineDocumentClassMapWithNoLoss` |
| **L17** | Top-level and entry **element order** in the stored document | JavaScript object literals preserve insertion order, and #7 built the whole document in one literal | `BsonDocument` also preserves insertion order — but here the document is assembled by `$setOnInsert` plus repeated `$set`, and `$set` **appends** a key the placeholder omitted. The placeholder is therefore **total over the shape**, in the seed class's declaration order | `ProjectionWriteTests › PR41_…` asserts the element name sequence, top level and entry |
| **L18** | Read concern / read preference | standalone: primary, local | identical; **dismissed** — the projector performs no read on the write path at all (`PR6`), and the migration and bootstrap run against the same standalone | — |

### 10.2 The document store — MongoDB.Driver 3.11.1 versus the node driver

| # | Boundary | #7 relied on | #8 supplies it by | Guard |
|---|---|---|---|---|
| **L19** | BSON **element names** | JavaScript object keys are the names; `orderReference` *is* `orderReference` | the C# driver's default element name is the **PascalCase property name**. #8 writes camelCase **string literals** collected in `ReadModelCollection`, equal to `OrderTimelineDocument`'s `[BsonElement]` names, and never lets a class map infer one on the write path | `ProjectionWriteTests › PR41_…`; `SeededOracleParityTests › PR44_…` |
| **L20** | Identifier rendering | `crypto.randomUUID()` strings; ids were always strings | the C# driver's default for `Guid` is the **BSON UUID binary subtype**. #8 writes `Guid.ToString("D")` (lowercase, hyphenated) at every id site, matching the seed and #7's bytes | `DeltaToPipelineTests › PR41_ElementNamesAre…_IdsAreLowercaseHyphenatedStrings_InstantsAreTheWireFormat` |
| **L21** | Instant rendering | `.toISOString()` — always three fraction digits and a literal `Z` | the C# driver's default for `DateTime`/`DateTimeOffset` is a **BSON date**. #8 writes the `InstantJsonConverter` format `yyyy-MM-dd'T'HH:mm:ss.fff'Z'` as a **string**. `"O"` would give seven fraction digits and `+00:00`, breaking `PR14`'s `$max` and `PR10`'s primary sort key on the *same* document | `DeltaToPipelineTests › PR41_…`; `TimelineProjectionTests › PR14_…` |
| **L22** | Retryable writes | `retryWrites` defaults true in both drivers, and **neither** retries on a standalone topology | the hand-written retry-exactly-once for `E11000` on Phase 1 — not a driver feature on either side | `ProjectionConcurrencyTests › PR7_…DifferentEventIds…_NoDuplicateKeyErrorEscaping` |
| **L23** | Write concern | node driver default acknowledged | `MongoClientSettings` default is `WriteConcern.Acknowledged` (w:1) on a standalone — the same durability the loser-detection in L2 depends on. **Not overridden**; recorded because setting `w:0` anywhere would silently break duplicate detection by making every write "succeed" | — (no override exists; `PR6`'s command-monitoring test would show the write) |
| **L24** | Connection pooling and what "concurrent" means in a test | node's driver serialises through one pool per `MongoClient`; #7's own spec drove concurrency through **one** client, and its reviewer had to open twelve to attack it properly | one `IMongoClient` per process (thread-safe, internally pooled). **`PR7`'s test drives separate `MongoClient` instances**, adopting the reviewer's harder shape from the start rather than the implementer's | `ProjectionConcurrencyTests › PR7_…FromSeparateClients_…` |
| **L25** | Proving the **absence** of a read | #7 used the node driver's `monitorCommands: true` | `MongoClientSettings.ClusterConfigurator` + `cb.Subscribe<CommandStartedEvent>(...)`, recording the command names issued per apply. This is the only way to prove a negative here, and it is what makes `PR6` checkable rather than aspirational | `ProjectionWriteTests › PR6_AppliesOneFactInExactlyOneUpsertAndOneFilteredFindAndModify_IssuingNoReadOfOrderTimeline` |
| **L26** | Numeric width on the wire into BSON | JavaScript numbers are doubles; the node driver wrote them as such and money never truncated | C# is explicit: a `long` literal becomes **Int64**, an `int` becomes **Int32**. Every money value stays `long` end to end. **A narrowing cast on a money value is a defect** (`CLAUDE.md` § Money; feature `money_column_width` is the standing precedent) | `DeltaToPipelineTests › PR41_MoneyIsInt64_QuantityIsInt32_AndADetailAmountAboveInt32MaxSurvivesAsInt64` |
| **L27** | `detail`'s nested payload → BSON | a plain JS object went straight in | serialise the domain's `IReadOnlyDictionary<string, object>` through `JsonWire.Options`, then `BsonDocument.Parse` — one conversion, camelCase keys, integers Int32 or Int64 **by magnitude**, never truncated | `DeltaToPipelineTests › PR41_…AboveInt32Max…` |
| **L28** | String comparison inside the server (`$max` on `updatedAt`, `$sortArray` on `eventId`/`occurredAt`) | MongoDB's **simple binary** collation, case-sensitive | the same server default; no collation is set anywhere. It orders chronologically **only because** every instant is rendered in one fixed-width format (L21) — the two rows are one property | `TimelineProjectionTests › PR14_UpdatedAtIsTheGreatestOccurredAtApplied_NotTheLatestArrival` |
| **L29** | `processedEventKeys`'s stored order | `$sortArray { sortBy: 1 }` — binary | the same `$sortArray`; the **seed** sorts its copy with `StringComparer.Ordinal`, which agrees with binary order for ASCII `projector:<guid>` keys. Two different sorters producing one array is the risk; the oracle test is what settles it | `SeededOracleParityTests › PR44_…` compares `processedEventKeys` element by element |
| **L30** | Which collection object the whole service uses | `db.collection('order_timeline')` | one registered `IMongoCollection<BsonDocument>`; `MongoSeedWriter.CollectionName` is the literal `"order_timeline"`, re-declared (not referenced — no `ProjectReference` to `Seed`) and asserted equal from the test project | `ReadModelIndexesTests › PR39_…` operates on both writers' handles |

### 10.3 The Kafka consumer — Confluent.Kafka 2.15.0 versus kafkajs

| # | Boundary | #7 relied on | #8 supplies it by | Guard |
|---|---|---|---|---|
| **L31** | Consume from the beginning of the retained log | kafkajs' `subscribe({ fromBeginning: true })` — an explicit **subscribe option**, asserted by `main-kafka-options.spec.ts` | `AutoOffsetReset.Earliest`, a **config token whose default is `Latest`**. Notifications' equivalent shipped argued in prose and asserted by nothing (review D3) | `KafkaFactStreamSubscriberConfigTests › PR38_BuildConsumerConfig_SetsEarliestNotLatest_BecauseTheReadModelIsRebuiltByReplay` **and** an integration case producing a fact before the group first subscribes |
| **L32** | Offset committed only after the handler completed | kafkajs' `eachMessage` resolving before the offset advances | `EnableAutoCommit = true` **with** `EnableAutoOffsetStore = false` and an explicit `StoreOffset` after `await handler(...)` — three settings that must agree, copied from Orders | `OffsetContractTests › PR38_AThrowingHandlerLeavesTheCommittedOffsetUnchanged_ReadFromTheBroker` — **read from the broker with `consumer.Committed(...)`, never inferred from a redelivery** |
| **L33** | Broker-side and ledger-side identity being one value | one `groupId` string | `GroupId = "projector"` asserted **equal to** `ConsumerNames.ToToken(ConsumerName.Projector)`, so a typo cannot open a second silently-empty dedup namespace | `KafkaFactStreamSubscriberConfigTests › PR38_UsesTheProjectorGroupAndClientIdentity_…` |
| **L34** | Per-partition ordering, and **no** ordering across topics | the broker | the same broker. This is precisely why `PR10` re-sorts the whole array on every apply (§5.4.2) — the compensation pair lives on two different topics | `ReplayDeterminismTests › PR15_AFactDeliveredBeforeTheFactThatCausedItProducesTheIdenticalFinalArrayAsTheReverseArrival` |
| **L35** | Not blocking the host thread on a synchronous `Consume()` | Node is single-threaded and `eachMessage` is async by construction | `Confluent.Kafka`'s `Consume()`/`Commit()` are **blocking**; `BackgroundService.ExecuteAsync` runs on a thread-pool thread and blocking before the first `await` stalls host startup. `await Task.Yield()` before `Subscribe`, copied verbatim from Orders and Notifications | the host boots at all — `ReadModelBootstrapTests › PR43_…` and every integration case |
| **L36** | A malformed message must not block a partition | #7's tolerant `parseFactEnvelope` + log-and-acknowledge | the same shape: `NotificationFactsConsumer`'s seven-field `ValidateEnvelope` verbatim, then log-with-topic/partition/offset and return | `ProjectorFactsConsumerTests › PR3_…`, `› PR4_…` (both assert on a recording fake that the writer was **never called** — the attempt, not the residue) |

### 10.4 NATS — NATS.Net 3.2.0 versus the `nats` JS client

| # | Boundary | #7 relied on | #8 supplies it by | Guard |
|---|---|---|---|---|
| **L37** | Fire-and-forget core publish with no reply subject | `nc.publish(subject, codec.encode(x))` | `connection.PublishAsync(subject, bytes, headers: ...)` — no `RequestAsync`, no reply subject, nothing awaited beyond the publish | `ProjectorConsumesOnlyTests › PR1_…AndNoNatsSubscriptionOfAnyKind` |
| **L38** | A wrong broker URL fails the **boot** | `await connect(...)` in `main.ts` | **`NatsConnection` connects lazily on first use.** #8's other services never noticed because their responders subscribe at startup; a publish-only projector would project perfectly and signal nothing forever, with `PR19` swallowing every failure. → **GATE row 1**: connect eagerly in `ReadModelBootstrap` | conditional on the gate — `ReadModelBootstrapTests › PR45_…` |
| **L39** | Headers are safe to build once and reuse | JS object literals | `NatsHeaders` is documented **not thread-safe**; a **fresh instance per publish** (feature 17's `FS2`, recorded in `NatsSagaCommandsAdapter`'s own header) | `NatsUpdateSignalPublisherTests › PR37_EveryEnvelopeFieldReachesBothSignalPayloadsVerbatim_SentinelPerField` drives two publishes and reads both headers |
| **L40** | Subject-hierarchy fan-out (`readmodel.order.updated.*`) | the NATS server | the same server, `nats:2.14.5-alpine` | `UpdateSignalTests › PR17_…ReceivedByBothAWildcardAndASingleOrderSubscriber` |
| **L41** | One connection per process, multiplexed | one `connect()` | `AddSingleton<INatsConnection>` — the `Orders`/`Billing`/`Fulfillment` registration verbatim | `ProjectorDispatcherRegistrationTests` resolves the real host |
| **L42** | A test observing a publish must not race the connection | #7 subscribed before producing | the same discipline: subscribe **first**, assert on messages received (terminal evidence), never a bare sleep and never an intermediate status | every case in `UpdateSignalTests` |

### 10.5 Serialisation, language and host

| # | Boundary | #7 relied on | #8 supplies it by | Guard |
|---|---|---|---|---|
| **L43** | Signal payload JSON shape (camelCase, nulls omitted) | `JSONCodec` over plain objects whose keys were already camelCase | `JsonWire.Options` — the one shared `JsonSerializerOptions` (`CLAUDE.md` wire non-negotiable). Default `System.Text.Json` would emit PascalCase and write nulls | `UpdateSignalTests › PR17_BothPayloadsCarryOpenApisRequiredFields_ReadFromTheContractAsText` |
| **L44** | Instant on the signal wire | `.toISOString()` | `InstantJsonConverter`, registered in `JsonWire.Options` | same as L43 |
| **L45** | Fourteen-way exhaustiveness with no silent default | #7's `switch` threw on an unknown type; coverage came from a text-scan spec against `domain-model.md` | a `switch` on the payload **CLR type** with `_ => throw new UnknownFactTypeError(...)`, plus `PR2`'s **two-directional** comparison against `FactCatalog` — the registry that already has its own completeness test against `asyncapi.yaml` | `FactProjectionTests › PR2_…FailsInBothDirectionsWhenTheCatalogueAndTheSwitchDisagree` |
| **L46** | One list of handled fact types, not two | #7 had `HANDLED_EVENT_TYPES` **beside** its switch — the same two-list shape Notifications was rejected for, unbitten only by luck | **no list at all** under `src/Projector/`: `FactCatalog` is the table (§9) | `FactProjectionTests › PR36_TheOnlyEventTypeTableUnderSrcProjectorIsTheFactCatalogue_ProvedByEnumeratingTheFourteenLiterals` |
| **L47** | Case-sensitive dictionary lookup of `eventType` | JS object property lookup is exact | `FactCatalog` is built with `StringComparer.Ordinal`; `JsonWire.Options` sets `PropertyNameCaseInsensitive = true` for **deserialisation only** and does not affect this lookup | `ProjectorFactsConsumerTests › PR36_EachOfTheFourteenFactsReachesTheHandlerThroughExecuteAsyncItself_TypedByTheCatalogue` |
| **L48** | Money never truncates | JavaScript numbers have no narrowing conversion | `long` end to end, `Money` where a value object is wanted, **no `decimal` in the domain** (`DomainDecimalTests`), no cast at the BSON boundary (L26) | `DomainDecimalTests` (becomes non-vacuous for this namespace) + `DeltaToPipelineTests › PR41_MoneyIsInt64…` |
| **L49** | Bootstrap completes before the first poll | `main.ts` awaited both in one function before `startAllMicroservices()` | **registration order** + `HostOptions.ServicesStartConcurrently = false` + `IHostedService` (not `BackgroundService`) for the bootstrap — three properties, any of which can be changed without a compile error | `ProjectorHostTests › PR43_ReadModelBootstrapIsRegisteredBeforeTheConsumer_AndServicesStartSequentially`; `ReadModelBootstrapTests › PR43_StartAsyncReturnsOnlyAfterTheIndexesAndTheMigrationExist_AndAThrowingBootstrapFailsTheHost` |
| **L50** | A missing handler is loud | `@nestjs/cqrs` resolved at module init | `AddDispatcher` last, `ValidateOnBuild`/`ValidateScopes` forced on in **every** environment (the framework enables them only under `Development`) | `ProjectorDispatcherRegistrationTests › PR27_TheRealHostResolvesExactlyOneHandlerForProjectFactCommand` |
| **L51** | The domain stays free of the store | #7's ESLint `no-restricted-imports` over `apps/*/src/domain/**` | NetArchTest's `DomainPurityTests`/`CqrsDomainPurityTests`/`DomainDecimalTests`, which **already enumerate** `OrderToCash.Projector.Domain` via `ProjectorDomainPlaceholder` and become non-vacuous for it here | those tests, plus `FactProjectionTests › PR28_…` |
| **L52** | The Kafka consumer client stays in one namespace | #7 had no equivalent rule | `FactConsumerConfinementTests` is **namespace-scoped, not service-scoped**, and already ranges over this assembly — inherited free, no edit | that test |
| **L53** | The projector is the only writer | #7's text-scan spec over `apps/*` with `apps/seed` allow-listed by name | the same shape over `src/*`, listing MongoDB.Driver's **write-shaped method names** (`PR20`) rather than the package import — because `R54`'s own second half obliges the Gateway to *read* this collection, so an import-level guard would have to be relaxed at feature 25 and would then guard nothing | `ReadModelSoleWriterTests › PR20_FiresWhenAThirdServiceAcquiresAWriteShapedCall_ProvedOnAScratchTree` |
| **L54** | The seed's own documents as a parity oracle | #7 had none at implementation time — it wrote the projector first | `tests/Seed.IntegrationTests/OracleFixtures/order_timeline_from_number7.json` proves #8's seed byte-identical to #7's, so the seed is the **cheapest available oracle** for the projector — an advantage #7 never had | `SeededOracleParityTests › PR44_…` (six `[InlineData]` rows) |

### 10.6 What the ledger says about where to spend review time

Three rows are the ones a reviewer should attack first, because each is a property that is **present and correct on the path a deletion probe takes** and only wrong under a condition the happy path never creates: **L9** (`ReturnDocument` default — every signal carries the previous status, and the count of signals is still right), **L12** (`$type` rendering — both services work in isolation and neither can start after the other), and **L31** (`AutoOffsetReset` — identical behaviour on a fresh topic, permanently incomplete on a populated one). None of the three is visible to `R<n>` traceability, and none is visible to emission-deletion arming. They are visible only to the question this table asks.

---

## 11. Testing approach

Levels, per `CLAUDE.md` and `test-matrix.md`:

- **Pure unit, no container** (`tests/Projector.UnitTests/`): the whole `Domain/` folder (`FactProjection`, ranks, summaries, money formatting), `DeltaToPipeline` and `TimelineOrder` (the emitted stage tree is a plain `BsonDocument` — this is where `$sortArray`/`$ifNull`/`$cond`/`$unsetField` correctness is cheapest to pin), the consumer's parse/route/validate branches over a recording fake subscriber, and every structural text-scan guard. Coverage gate ≥80% on the domain layer.
- **Integration, Testcontainers, real infrastructure** (`tests/Projector.IntegrationTests/`): everything else. `mongo:8.3.8`, `apache/kafka:4.3.1` via the generic `ContainerBuilder` (`Testcontainers.Kafka` cannot drive this image — `Directory.Packages.props` carries the probe), `nats:2.14.5-alpine` via `ContainerBuilder`. **No mocked broker, no mocked driver, ever.** The three fixtures are per-service copies of `Notifications.IntegrationTests/KafkaContainerFixture.cs`, `Billing.IntegrationTests/NatsContainerFixture.cs` and `Seed.IntegrationTests`' `MongoDbBuilder` usage — the established convention, not a shared test project.

Two rules bind every integration case here:

1. **Synchronise only on terminal or monotonic evidence.** A projector is a race by construction: the document passes through many intermediate states a correct system is *allowed* to be in. Poll for `events.Count == N` (monotone), `status == "completed"` (terminal), or `processedEventKeys` containing a specific key (monotone) — never for `status == "confirmed"` on an order that will continue, and never a bare sleep.
2. **`N10`.** An assertion that an absent side effect did not happen proves nothing about whether it was *attempted*. "No duplicate timeline entry after a redelivery" is satisfied by a projector that applies twice and happens to overwrite. The **attempt** must be observed: `FindOneAndUpdateAsync`'s returned `null`, and a counter on the post-apply callback — not only the document's final contents.

### 11.1 The fact-emission rule, and its analogue here

The projector emits no domain fact, so the rule lands on its only outward emission — the update signal — and on the two deliberate **suppressions**. `tasks.md` group **K** is where each of these is armed by the implementer, with the verbatim failure recorded in `progress/impl_projector_read_model.md`.

But **deletion is one mutation family, not the whole of arming.** Every one of these branches carries a payload, and a guard can count the frames perfectly while never opening one — which is the defect feature 17 shipped and feature 18 shipped again. So each row below is armed twice: once by deleting the emission, once by **corrupting a field the test itself supplied**. For fields the test does not control — ids, the clock, generated references — the source is injected or bracketed, because `Assert.NotEqual` proves non-collision and can never prove provenance.

### 11.2 The replay-determinism case (`PR15`) — how it is made to bite

Not "run it twice and compare":

1. Produce the full fact set of one seeded saga to the three **real** topics, consume, snapshot the **whole** document (`statusRank`, `processedEventKeys`, `timelineOrderVersion` included).
2. Drop the collection.
3. Produce the **same set**, shuffled by a seeded PRNG, each fact duplicated a deterministic number of times (0, 1, 2 extra), across the three topics; consume; snapshot again.
4. Assert the two `BsonDocument`s equal **in full**, element order included.

The shuffle is what makes it a determinism test rather than a repeatability test; the duplication is what makes it also an `R51` test. A comparison weakened to a projection of the document has stopped proving `feature_list.json`'s acceptance bullet 2.

### 11.3 The seeded oracle (`PR44`)

For each of the six sagas, take that saga's own outbox fixtures — `OrderSagaFixture.OrdersOutbox`, `.FulfillmentOutbox` and `.BillingOutbox`, whose `Payload` is already a typed `Contracts.Facts.Payloads.*` record — build an envelope per row, project all of them through the real writer into an empty collection, and compare the stored document field by field (**and by BSON type**) against `MongoSeedWriter.ToTimelineDocument(saga)`. Nine facts for a completed saga, five for the cancelled one; the counts match the fixture timelines exactly. The excluded set is `requirements.md` `PR44`'s enumerated one and nothing else: `retailer.name`, `company.name`, `items[].name`, the two `credit.*` summaries and every `events[].detail`.

This is the cheapest strong oracle available and #7 never had it. Its one blind spot is stated so nobody mistakes it for coverage: the fixtures place every fact at a **distinct** instant, so `PR10`'s causal tiebreak is never exercised by it (§5.4.1).

---

## 12. Live boot expectation, stated in advance

Against the running compose stack with `src/Seed` already applied, starting the projector with `AutoOffsetReset.Earliest` on a fresh `projector` consumer group:

- It replays every fact the previous phases relayed to the three topics, from offset 0.
- **The six seeded documents are not disturbed, and no backfill is needed.** The seed writes its outbox rows **already published** (`OutboxFixture.PublishedAt` is set, and the seed writers persist it), so the relay never publishes them and the seeded facts' `eventId`s are **not on Kafka at all**. There is therefore no replay of a seeded fact to suppress. Even if there were, every seeded document already carries `processedEventKeys` and a non-null `statusRank`, so `PR6`'s filter would suppress it and `PR12`'s `$max` could not regress it. Both halves of #7's open point 9 are closed by inheritance. **Verify this live rather than assume it** — `tasks.md` M2.
- Every live order placed since phase 8 gets a complete document; every fact on the topics for that order appears exactly once in `events[]`.
- **Placeholders will exist**, and that is `R53` behaving as specified: any order whose `order.placed.v1` is not on a topic (a probe fact injected in an earlier phase, a partial saga) yields a `headerComplete: false` document with `orderReference: null`. They coexist only because `uq_order_reference` is partial. Feature 25's list-query fixtures should expect them rather than be surprised — #7's review N7 recorded exactly this and it is inherited.
- **`Earliest` makes every synthetic probe fact ever injected into a dev fact topic permanently materialised** into the read model, on every fresh consumer group, forever. Correct, and worth knowing before the boot.
- Expected **absence**: no new order, no new fact on any topic, no `outbox` row, no `saga_commands` row. The projector produces nothing but signals — verified with a `nats sub 'readmodel.>'` while one order is placed end to end.

---

## 13. Out of scope — restated

`requirements.md` §5 in full. In particular: no HTTP surface and no health endpoint (feature 27's), no SSE and no replay buffer (feature 25/26's), no DLQ beyond `PR3`/`PR4`'s log-and-acknowledge (feature 27's, including #7's N8 poison-pill note carried verbatim in `requirements.md` §5), no OpenTelemetry, no change to any fact payload/topic/envelope, no edit under `src/Orders`, `tests/Orders.*`, `src/Seed`, `tests/Seed.*`, `src/Notifications`, `tests/Notifications.*`, `src/Gateway` or `apps/web`, and no `specs/shared/` edit other than this feature's own Status cells in `test-matrix.md` §7 — which that file's own reuse recipe makes each assessment's per-assessment record, not shared content.

Backlog ids **58** and **59** do not ride this feature. `PR37` applies id 58's *lesson* to the projector's own envelope copy from the start; it does not fix `NotificationFactsConsumer.ToEnvelope`, and claiming it did would be a scope lie.
