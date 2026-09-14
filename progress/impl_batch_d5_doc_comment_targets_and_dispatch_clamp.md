# Batch D5 — backlog ids 78 and 90

Implementer record. Both entries were `RE-OPENED 2026-09-13 by maintainer ruling`; the `ACCEPTED, NOT FIXED` prose in their `notes` is history and was treated as such. The contract worked from is the `acceptance` array of each entry in `feature_list.json`, read verbatim before anything else.

**Where the brief and a bullet differ.** One place, and it is a widening rather than a narrowing. The brief calls id 78's bullets 5–7 "three specific prose corrections". Bullets 6 and 7 each additionally *mandate an enumeration* of the retired wording across `src/` and `tests/` with one classification line per hit, **before any edit** — so they are two obligations each, not one. Both enumerations were run first and are reproduced below, and each turned up one further live instance of the same defect class beyond the site the bullet names. Nothing else in the brief conflicted with a bullet.

---

## Part 1 — id 78, `doc_comment_crefs_are_never_compiler_checked`

### Bullet 1 — the class, enumerated FIRST, as a search result

**The command** (documentation generation forced on, `CS1591` suppressed, and `TreatWarningsAsErrors` forced **off for the measurement only**, so the whole class is visible in one pass instead of the first failing project stopping its dependents):

```
dotnet build OrderToCash.sln --no-incremental --nologo \
  -p:GenerateDocumentationFile=true -p:NoWarn=CS1591 -p:TreatWarningsAsErrors=false -v:n
```

Exit code 0. **28 of 28 projects built** (30 `Project(` lines in `OrderToCash.sln`, two of which are solution folders). Raw warning lines: **214**; those are MSBuild node-prefixed (`10>…`), and after stripping the node prefix and de-duplicating, **107 distinct sites**.

**The count before any fix, by code:**

| code | meaning | sites |
|---|---|---|
| CS1574 | cref target could not be resolved | 46 |
| CS1573 | parameter with no `<param>` tag, where others have one | 26 |
| CS1734 | `<paramref>` naming a parameter not in scope | 22 |
| CS0419 | ambiguous cref across an overload set | 7 |
| CS1584 | syntactically invalid cref signature | 2 |
| CS1658 | parse error knocked on from the CS1584 cref | 2 |
| CS1570 | malformed XML in a doc comment | 2 |
| | **total** | **107** |

64 in `src/`, 43 in `tests/`. **All three sites bullet 1 names are in the list and are numbered below**: `ISagaCommandStore.cs` `FindOperatorCancelNoteAsync` (id 71's, row 30 — the entry's `:26` is still exact), `SagaFactHandler.cs` `commandStore` (id 71's, row 47 — the entry says `:183`, the real line is **`:268`**; re-located by content as the brief instructed), and `CancelOrderCommandHandler.cs` `OrderNotCancellableError` (feature 41's, row 25 — the entry says `:34`, the real line is **`:38`**).

Two codes the entry does not name turned up and are reported rather than dropped: **CS0419** (7) and **CS1570** (2). CS0419 is the same defect class — a cref that does not identify one target — and CS1570 is a doc comment the XML parser cannot read at all. Both are fixed and both are errors under the standing check.

**Reconciliation with id 71's review round 3**, which "counted 12 in the Orders build closure alone": the Orders closure (`src/Orders` + `src/Contracts` + `src/Cqrs` + `src/SharedKernel`) here holds **38** of the 107 (`grep -cE "^src/(Orders|Contracts|Cqrs|SharedKernel)/"` over the de-duplicated site list). The figures do not contradict — that review ran a doc-enabled build of one project and counted the cref family; this run covers all four documentation codes across the same closure and includes CS1573/CS1734, which that count did not.

#### One classification line per site

| # | site | code | target | what it was | disposition |
|---|---|---|---|---|---|
| 1 | `src/Billing/Application/Ports/IInvoiceRepository.cs:14` | CS1574 | `DomainEvents` | unresolved cref | FIXED — → `AggregateRoot.DomainEvents`. Inherited from the base, so `Invoice.DomainEvents` does not bind. |
| 2 | `src/Billing/Application/Ports/IInvoiceRepository.cs:35` | CS1574 | `DomainEvents` | unresolved cref | FIXED — → `AggregateRoot.DomainEvents`. Inherited from the base, so `Invoice.DomainEvents` does not bind. |
| 3 | `src/Billing/Application/Ports/IUnitOfWork.cs:18` | CS1574 | `EfCoreUnitOfWork` | unresolved cref | FIXED — → `<c>EfCoreUnitOfWork</c>`. The type is in `Infrastructure/`; an Application-layer file must not name it as a symbol even in a comment. |
| 4 | `src/Billing/Domain/Errors/CreditReleaseUnderflowError.cs:8` | CS1574 | `Release` | unresolved cref | FIXED — → `BuyerCredit.Release`. The bare name does not bind from the error type's own doc comment. |
| 5 | `src/Billing/Domain/Events/InvoiceIssued.cs:7` | CS1574 | `AggregateId` | unresolved cref | FIXED — → `FactEvent.AggregateId`. A positional parameter of a record whose BASE already declares the property generates no new property, so the derived name does not bind. |
| 6 | `src/Billing/Infrastructure/Persistence/BillingDbContext.cs:14` | CS1574 | `DomainPurityTests` | unresolved cref | FIXED — → `<c>…</c>`. A test type; no `src/` assembly references the test assembly. |
| 7 | `src/Contracts/Envelopes/Envelope.cs:24` | CS1574 | `OrderPlacedPayload` | unresolved cref | FIXED — → `OrderToCash.Contracts.Facts.Payloads.OrderPlacedPayload`; the cref named the wrong namespace (`…Facts.` not `…Facts.Payloads.`). |
| 8 | `src/Cqrs/IDispatcher.cs:52` | CS1584 | `System.Reflection.MethodInfo.Invoke(object?,object?[]?)` | invalid cref signature | FIXED — → `System.Reflection.MethodBase.Invoke(object, object[])`. Nullable annotations are not legal in a cref signature (CS1584), and `Invoke` is declared on `MethodBase`, not `MethodInfo`. |
| 9 | `src/Cqrs/IDispatcher.cs:52` | CS1658 | `(see the CS1584 above)` | knock-on parse error from the CS1584 cref | FIXED — resolved by the CS1584 cref fix in the same line; not a second site. |
| 10 | `src/Fulfillment/Domain/OrderDespatch.cs:41` | CS1734 | `newId` | paramref naming a parameter not in scope | FIXED — → `<c>newId</c>`; a parameter of a factory method, referenced from the TYPE's summary. |
| 11 | `src/Fulfillment/Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs:14` | CS1574 | `ValueGeneratedOnAdd` | unresolved cref | FIXED — → `<c>ValueGeneratedOnAdd()</c>`; an extension method on `PropertyBuilder`, never a member of the configuration class. |
| 12 | `src/Fulfillment/Infrastructure/Persistence/FulfillmentDbContext.cs:13` | CS1574 | `DomainPurityTests` | unresolved cref | FIXED — → `<c>…</c>`. A test type; no `src/` assembly references the test assembly. |
| 13 | `src/Gateway/Infrastructure/Messaging/NatsStreamSignalSubscriber.cs:43` | CS1574 | `Parse(ReadOnlySpan{byte})` | unresolved cref | FIXED — → `<c>JsonDocument.Parse(ReadOnlySpan&lt;byte&gt;)</c>`; `JsonDocument.Parse` has no `ReadOnlySpan<byte>` overload, so the cref named an overload that does not exist. |
| 14 | `src/Gateway/Presentation/RequestLatencyMiddleware.cs:10` | CS1574 | `GetEndpoint` | unresolved cref | FIXED — → `<c>HttpContext.GetEndpoint()</c>`; an extension method, not a member of `HttpContext`. |
| 15 | `src/Notifications/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs:9` | CS1574 | `FactConsumerConfinementTests` | unresolved cref | FIXED — → `<c>…</c>`. Test type, not referenceable from `src/`. |
| 16 | `src/Notifications/Infrastructure/Messaging/FactRetryDispatcher.cs:37` | CS1734 | `cancellationToken` | paramref naming a parameter not in scope | FIXED — → `<c>cancellationToken</c>`; referenced from a type summary / a parameterless test method. |
| 17 | `src/Notifications/Infrastructure/Messaging/FactRetryDispatcher.cs:37` | CS1734 | `cancellationToken` | paramref naming a parameter not in scope | FIXED — → `<c>cancellationToken</c>`; referenced from a type summary / a parameterless test method. |
| 18 | `src/Notifications/Infrastructure/Messaging/IdempotentConsumer.cs:31` | CS1734 | `work` | paramref naming a parameter not in scope | FIXED — → `<c>work</c>`; `RunOnceAsync`'s parameter, referenced from the type summary. |
| 19 | `src/Notifications/Infrastructure/Messaging/IdempotentConsumer.cs:31` | CS1734 | `work` | paramref naming a parameter not in scope | FIXED — → `<c>work</c>`; `RunOnceAsync`'s parameter, referenced from the type summary. |
| 20 | `src/Notifications/Infrastructure/Messaging/IdempotentConsumer.cs:34` | CS1734 | `work` | paramref naming a parameter not in scope | FIXED — → `<c>work</c>`; `RunOnceAsync`'s parameter, referenced from the type summary. |
| 21 | `src/Notifications/Infrastructure/Messaging/IdempotentConsumer.cs:34` | CS1734 | `work` | paramref naming a parameter not in scope | FIXED — → `<c>work</c>`; `RunOnceAsync`'s parameter, referenced from the type summary. |
| 22 | `src/Notifications/Infrastructure/Notification/SendFailureClassifier.cs:7` | CS1574 | `SendAsync` | unresolved cref | FIXED — → fully qualified `OrderToCash.Notifications.Application.Ports.INotificationSender.SendAsync`; that namespace is not imported here. |
| 23 | `src/Notifications/Infrastructure/Persistence/NotificationsDbContext.cs:15` | CS1574 | `DomainPurityTests` | unresolved cref | FIXED — → `<c>…</c>`. A test type; no `src/` assembly references the test assembly. |
| 24 | `src/Notifications/Presentation/NotificationFactsConsumer.cs:50` | CS1574 | `NotificationFactsConsumerTests` | unresolved cref | FIXED — → `<c>…</c>`. Test type, not referenceable from `src/`. |
| 25 | `src/Orders/Application/Commands/CancelOrderCommandHandler.cs:38` | CS1574 | `OrderNotCancellableError` | unresolved cref | FIXED — → fully qualified `OrderToCash.Orders.Domain.Errors.OrderNotCancellableError`; the relative `Errors.` prefix did not bind from `Application.Commands`. |
| 26 | `src/Orders/Application/Commands/RequestIdCollision.cs:15` | CS1574 | `Message` | unresolved cref | FIXED — → `<c>SqlException.Message</c>`; inherited from `Exception`, so the derived-type cref does not bind. |
| 27 | `src/Orders/Application/Ports/IIdempotentSagaRunner.cs:3` | CS1574 | `SagaFactHandler` | unresolved cref | FIXED — → fully qualified `OrderToCash.Orders.Application.Sagas.SagaFactHandler`; not imported by `Application.Ports`. |
| 28 | `src/Orders/Application/Ports/IOrderRepository.cs:22` | CS1573 | `order` | parameter with no `<param>` tag (others documented) | `<param name="order">` written (only `requestId` was documented). |
| 29 | `src/Orders/Application/Ports/IOrderRepository.cs:22` | CS1573 | `cancellationToken` | parameter with no `<param>` tag (others documented) | FIXED — → `<c>cancellationToken</c>`; referenced from a type summary / a parameterless test method. |
| 30 | `src/Orders/Application/Ports/ISagaCommandStore.cs:26` | CS1574 | `FindOperatorCancelNoteAsync` | unresolved cref | FIXED — → `ISagaCommandStore.FindOperatorCancelNoteAsync`. The comment is on `SagaCommandRecord`, NOT on the interface — the bare member name never bound. Named by bullet 1 as id 71's. |
| 31 | `src/Orders/Application/Ports/ISagaCommandStore.cs:81` | CS1573 | `orderId` | parameter with no `<param>` tag (others documented) | FIXED — → `<c>orderId</c>`; a method parameter referenced from a PROPERTY's summary. |
| 32 | `src/Orders/Application/Ports/ISagaCommandStore.cs:82` | CS1573 | `orderReference` | parameter with no `<param>` tag (others documented) | FIXED — `<param name="orderReference">` written. |
| 33 | `src/Orders/Application/Ports/ISagaCommandStore.cs:83` | CS1573 | `command` | parameter with no `<param>` tag (others documented) | FIXED — `<param name="command">` written. |
| 34 | `src/Orders/Application/Ports/ISagaCommandStore.cs:84` | CS1573 | `payload` | parameter with no `<param>` tag (others documented) | FIXED — `<param name="payload">` written. |
| 35 | `src/Orders/Application/Ports/ISagaCommandStore.cs:85` | CS1573 | `triggeringEventId` | parameter with no `<param>` tag (others documented) | FIXED — `<param name="triggeringEventId">` written. |
| 36 | `src/Orders/Application/Ports/ISagaCommandStore.cs:88` | CS1573 | `cancellationToken` | parameter with no `<param>` tag (others documented) | FIXED — → `<c>cancellationToken</c>`; referenced from a type summary / a parameterless test method. |
| 37 | `src/Orders/Application/Ports/ISagaFirstParkDeadLetterHandler.cs:22` | CS1573 | `cancellationToken` | parameter with no `<param>` tag (others documented) | FIXED — → `<c>cancellationToken</c>`; referenced from a type summary / a parameterless test method. |
| 38 | `src/Orders/Application/Ports/ISagaIgnoredFactRecorder.cs:5` | CS1574 | `SagaFactHandler` | unresolved cref | FIXED — → fully qualified `OrderToCash.Orders.Application.Sagas.SagaFactHandler`; not imported by `Application.Ports`. |
| 39 | `src/Orders/Application/Ports/IUnitOfWork.cs:19` | CS1574 | `EfCoreUnitOfWork` | unresolved cref | FIXED — → `<c>EfCoreUnitOfWork</c>`. The type is in `Infrastructure/`; an Application-layer file must not name it as a symbol even in a comment. |
| 40 | `src/Orders/Application/Sagas/SagaFact.cs:36` | CS1573 | `EventId` | parameter with no `<param>` tag (others documented) | FIXED — → `FactEvent.EventId`, same mechanism as `AggregateId` above. |
| 41 | `src/Orders/Application/Sagas/SagaFact.cs:37` | CS1573 | `EventType` | parameter with no `<param>` tag (others documented) | `<param name="EventType">` written (the record documented only its two trailing parameters). |
| 42 | `src/Orders/Application/Sagas/SagaFact.cs:38` | CS1573 | `AggregateId` | parameter with no `<param>` tag (others documented) | FIXED — → `FactEvent.AggregateId`. A positional parameter of a record whose BASE already declares the property generates no new property, so the derived name does not bind. |
| 43 | `src/Orders/Application/Sagas/SagaFact.cs:39` | CS1573 | `CorrelationId` | parameter with no `<param>` tag (others documented) | FIXED — `<param name="CorrelationId">` written. |
| 44 | `src/Orders/Application/Sagas/SagaFact.cs:40` | CS1573 | `CausationId` | parameter with no `<param>` tag (others documented) | FIXED — `<param name="CausationId">` written. |
| 45 | `src/Orders/Application/Sagas/SagaFact.cs:41` | CS1573 | `OccurredAt` | parameter with no `<param>` tag (others documented) | FIXED — `<param name="OccurredAt">` written. |
| 46 | `src/Orders/Application/Sagas/SagaFact.cs:42` | CS1573 | `Payload` | parameter with no `<param>` tag (others documented) | FIXED — `<param name="Payload">` written. |
| 47 | `src/Orders/Application/Sagas/SagaFactHandler.cs:268` | CS1574 | `commandStore` | unresolved cref | FIXED — → `<c>commandStore</c>`. A primary-constructor parameter is not a cref target. Named by bullet 1 as id 71's. |
| 48 | `src/Orders/Application/Sagas/SagaFactResult.cs:14` | CS1574 | `Enqueued` | unresolved cref | FIXED — → `SagaFactResult.Enqueued`; the comment is on the sibling `SagaFactOutcome` enum. |
| 49 | `src/Orders/Infrastructure/Messaging/FactRetryDispatcher.cs:37` | CS1734 | `cancellationToken` | paramref naming a parameter not in scope | FIXED — → `<c>cancellationToken</c>`; referenced from a type summary / a parameterless test method. |
| 50 | `src/Orders/Infrastructure/Messaging/FactRetryDispatcher.cs:37` | CS1734 | `cancellationToken` | paramref naming a parameter not in scope | FIXED — → `<c>cancellationToken</c>`; referenced from a type summary / a parameterless test method. |
| 51 | `src/Orders/Infrastructure/Messaging/IdempotentConsumer.cs:31` | CS1734 | `work` | paramref naming a parameter not in scope | FIXED — → `<c>work</c>`; `RunOnceAsync`'s parameter, referenced from the type summary. |
| 52 | `src/Orders/Infrastructure/Messaging/IdempotentConsumer.cs:31` | CS1734 | `work` | paramref naming a parameter not in scope | FIXED — → `<c>work</c>`; `RunOnceAsync`'s parameter, referenced from the type summary. |
| 53 | `src/Orders/Infrastructure/Messaging/IdempotentConsumer.cs:34` | CS1734 | `work` | paramref naming a parameter not in scope | FIXED — → `<c>work</c>`; `RunOnceAsync`'s parameter, referenced from the type summary. |
| 54 | `src/Orders/Infrastructure/Messaging/IdempotentConsumer.cs:34` | CS1734 | `work` | paramref naming a parameter not in scope | FIXED — → `<c>work</c>`; `RunOnceAsync`'s parameter, referenced from the type summary. |
| 55 | `src/Orders/Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs:13` | CS1574 | `ValueGeneratedOnAdd` | unresolved cref | FIXED — → `<c>ValueGeneratedOnAdd()</c>`; an extension method on `PropertyBuilder`, never a member of the configuration class. |
| 56 | `src/Orders/Infrastructure/Persistence/EfCoreUnitOfWork.cs:10` | CS1574 | `IDbContextTransaction` | unresolved cref | FIXED — → fully qualified `Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction`; that namespace is not imported. |
| 57 | `src/Orders/Infrastructure/Persistence/OrdersDbContext.cs:14` | CS1574 | `DomainPurityTests` | unresolved cref | FIXED — → `<c>…</c>`. A test type; no `src/` assembly references the test assembly. |
| 58 | `src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:290` | CS1574 | `SagaCommandStoreTests` | unresolved cref | FIXED — → `<c>…</c>`. Test type, not referenceable from `src/`. |
| 59 | `src/Orders/Infrastructure/Saga/SagaCommandDispatcher.cs:43` | CS1574 | `ISagaCommandSignal` | unresolved cref | FIXED — → fully qualified `OrderToCash.Orders.Application.Ports.ISagaCommandSignal`; the relative `Ports.` prefix did not bind from `Infrastructure.Saga`. |
| 60 | `src/Projector/Application/Ports/IReadModelWriter.cs:16` | CS1734 | `afterApplied` | paramref naming a parameter not in scope | FIXED — → `<c>afterApplied</c>`; a parameter of the interface's method, referenced from the interface summary. |
| 61 | `src/Projector/Infrastructure/Messaging/FactRetryDispatcher.cs:37` | CS1734 | `cancellationToken` | paramref naming a parameter not in scope | FIXED — → `<c>cancellationToken</c>`; referenced from a type summary / a parameterless test method. |
| 62 | `src/Projector/Infrastructure/Messaging/FactRetryDispatcher.cs:37` | CS1734 | `cancellationToken` | paramref naming a parameter not in scope | FIXED — → `<c>cancellationToken</c>`; referenced from a type summary / a parameterless test method. |
| 63 | `src/Projector/Infrastructure/Messaging/IdempotentConsumer.cs:27` | CS1734 | `scopeId` | paramref naming a parameter not in scope | FIXED — → `<c>scopeId</c>`; same shape as `afterApplied`. |
| 64 | `src/Projector/Infrastructure/Messaging/IdempotentConsumer.cs:27` | CS1734 | `scopeId` | paramref naming a parameter not in scope | FIXED — → `<c>scopeId</c>`; same shape as `afterApplied`. |
| 65 | `tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs:167` | CS1574 | `GetSymbolInfo(SyntaxNode, System.Threading.CancellationToken)` | unresolved cref | FIXED — → `<c>…</c>`; the real overload set is on `CSharpExtensions`/`SemanticModel` with different parameter types and the cref matched none of them. |
| 66 | `tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs:375` | CS1574 | `GetSymbolInfo(SyntaxNode, System.Threading.CancellationToken)` | unresolved cref | FIXED — → `<c>…</c>`; the real overload set is on `CSharpExtensions`/`SemanticModel` with different parameter types and the cref matched none of them. |
| 67 | `tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs:64` | CS1574 | `GetSymbolInfo(SyntaxNode, System.Threading.CancellationToken)` | unresolved cref | FIXED — → `<c>…</c>`; the real overload set is on `CSharpExtensions`/`SemanticModel` with different parameter types and the cref matched none of them. |
| 68 | `tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs:154` | CS1570 | ``<b>` / `</summary>` mismatch` | malformed XML | FIXED — an unclosed `<b>` in the class summary (line 86's heading) balanced; both CS1570s are the one unclosed tag. |
| 69 | `tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs:155` | CS1570 | ``<b>` / `</summary>` mismatch` | malformed XML | FIXED — an unclosed `<b>` in the class summary (line 86's heading) balanced; both CS1570s are the one unclosed tag. |
| 70 | `tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs:562` | CS0419 | `CSharpSyntaxTree.ParseText` | ambiguous cref (overload set) | FIXED — → `<c>CSharpSyntaxTree.ParseText</c>`; ambiguous across overloads and the comment is not about a particular one. |
| 71 | `tests/Architecture.Tests/KafkaGroupHostWrappingTests.cs:13` | CS1574 | `KafkaGroupTestHost` | unresolved cref | FIXED — → `<c>…</c>`. Three per-project copies exist and `Architecture.Tests` references none of them. |
| 72 | `tests/Billing.IntegrationTests/BillingRpcResponderTraceContinuationTests.cs:19` | CS1574 | `BillingRpcResponder` | unresolved cref | FIXED — → `<c>…</c>`. `internal` to `Billing`; visible to that project's tests only via `InternalsVisibleTo`, which does not make a cref bind. |
| 73 | `tests/Cqrs.UnitTests/DispatcherTests.cs:144` | CS1584 | `System.Reflection.MethodInfo.Invoke(object?,object?[]?)` | invalid cref signature | FIXED — → `System.Reflection.MethodBase.Invoke(object, object[])`. Nullable annotations are not legal in a cref signature (CS1584), and `Invoke` is declared on `MethodBase`, not `MethodInfo`. |
| 74 | `tests/Cqrs.UnitTests/DispatcherTests.cs:144` | CS1658 | `(see the CS1584 above)` | knock-on parse error from the CS1584 cref | FIXED — resolved by the CS1584 cref fix in the same line; not a second site. |
| 75 | `tests/Fulfillment.IntegrationTests/RoundTripTests.cs:10` | CS1574 | `FulfillmentDbContext` | unresolved cref | FIXED — → fully qualified `OrderToCash.Fulfillment.Infrastructure.Persistence.FulfillmentDbContext`; only `…Persistence.Entities` was imported. |
| 76 | `tests/Fulfillment.IntegrationTests/RoundTripTests.cs:11` | CS1574 | `FulfillmentDbContext` | unresolved cref | FIXED — → fully qualified `OrderToCash.Fulfillment.Infrastructure.Persistence.FulfillmentDbContext`; only `…Persistence.Entities` was imported. |
| 77 | `tests/Fulfillment.IntegrationTests/StockRpcResponderTraceContinuationTests.cs:19` | CS1574 | `StockRpcResponder` | unresolved cref | FIXED — → `<c>…</c>`, same reason as `BillingRpcResponder`. |
| 78 | `tests/Fulfillment.UnitTests/OrderStockReservationTests.cs:64` | CS1574 | `EventId` | unresolved cref | FIXED — → `FactEvent.EventId`, same mechanism as `AggregateId` above. |
| 79 | `tests/Gateway.IntegrationTests/GatewayTestHost.cs:13` | CS1734 | `overrideServices` | paramref naming a parameter not in scope | FIXED — → `<c>overrideServices</c>`; a factory-method parameter referenced from the type summary. |
| 80 | `tests/Gateway.IntegrationTests/GatewayTestHost.cs:49` | CS1574 | `AssemblyBehavior` | unresolved cref | FIXED — → `<c>AssemblyBehavior.cs</c>`; the file holds only assembly-level attributes, there is no such type. |
| 81 | `tests/Gateway.IntegrationTests/MongoOrderReadModelIntegrationTests.cs:8` | CS1574 | `MongoOrderReadModelMappingTests` | unresolved cref | FIXED — → `<c>…</c>`. Lives in `Gateway.UnitTests`, not referenced by `Gateway.IntegrationTests`. |
| 82 | `tests/Notifications.IntegrationTests/KafkaGroupTestHost.cs:32` | CS1573 | `inner` | parameter with no `<param>` tag (others documented) | `<param name="inner">` written (only `budget` was documented). |
| 83 | `tests/Notifications.IntegrationTests/KafkaGroupTestHost.cs:32` | CS1573 | `bootstrapServers` | parameter with no `<param>` tag (others documented) | FIXED — `<param name="bootstrapServers">` written. |
| 84 | `tests/Notifications.IntegrationTests/KafkaGroupTestHost.cs:32` | CS1573 | `groupId` | parameter with no `<param>` tag (others documented) | FIXED — `<param name="groupId">` written. |
| 85 | `tests/Notifications.IntegrationTests/NotificationDeadLetterTests.cs:36` | CS1574 | `WarmUpAsync` | unresolved cref | FIXED — → `<c>NotificationConsumptionTestSupport.WarmUpAsync</c>`; there is no `WarmUpAsync` on this class, which is what the sentence was actually about. |
| 86 | `tests/Notifications.UnitTests/NotificationDispatchServiceTests.cs:37` | CS1734 | `buildMessage` | paramref naming a parameter not in scope | FIXED — → `<c>buildMessage</c>`; a parameter of the code under test, referenced from a parameterless test method. |
| 87 | `tests/Notifications.UnitTests/RecordingKafkaProducer.cs:8` | CS0419 | `ProduceAsync` | ambiguous cref (overload set) | FIXED — → `ProduceAsync(string, Message{string, byte[]}, CancellationToken)`; the exact overload `KafkaDeadLetterPublisher` calls. |
| 88 | `tests/Orders.IntegrationTests/FakeDlqDepthGauge.cs:6` | CS1574 | `OutboxRelay` | unresolved cref | FIXED — → `<c>OutboxRelay</c>`; `internal` to `Orders`. |
| 89 | `tests/Orders.IntegrationTests/KafkaGroupTestHost.cs:32` | CS1573 | `inner` | parameter with no `<param>` tag (others documented) | `<param name="inner">` written (only `budget` was documented). |
| 90 | `tests/Orders.IntegrationTests/KafkaGroupTestHost.cs:32` | CS1573 | `bootstrapServers` | parameter with no `<param>` tag (others documented) | FIXED — `<param name="bootstrapServers">` written. |
| 91 | `tests/Orders.IntegrationTests/KafkaGroupTestHost.cs:32` | CS1573 | `groupId` | parameter with no `<param>` tag (others documented) | FIXED — `<param name="groupId">` written. |
| 92 | `tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs:541` | CS1574 | `SagaFirstParkDeadLetterHandler` | unresolved cref | FIXED — → `<c>…</c>`; `internal` to `Orders`. |
| 93 | `tests/Orders.IntegrationTests/SagaDeadLetterTests.cs:218` | CS0419 | `IConsumer{TKey,TValue}.Assign` | ambiguous cref (overload set) | FIXED — → `IConsumer{TKey, TValue}.Assign(TopicPartition)`; the overload this fixture actually calls. |
| 94 | `tests/Orders.IntegrationTests/SagaFirstParkDeadLetterTests.cs:16` | CS0419 | `Task.WhenAll` | ambiguous cref (overload set) | FIXED — → `Task.WhenAll(IEnumerable{Task})`; the overload this test uses. |
| 95 | `tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs:119` | CS1574 | `OrdersCreateResponder` | unresolved cref | FIXED — → `<c>…</c>`; `internal` to `Orders`. |
| 96 | `tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs:348` | CS0419 | `IConsumer{TKey,TValue}.Committed` | ambiguous cref (overload set) | FIXED — → `IConsumer{TKey, TValue}.Committed(IEnumerable{TopicPartition}, TimeSpan)`; the overload this helper calls. |
| 97 | `tests/Orders.IntegrationTests/StandInFulfillmentStockCheckResponder.cs:33` | CS1573 | `connection` | parameter with no `<param>` tag (others documented) | `<param name="connection">` written (only `rawAnswer` was documented). |
| 98 | `tests/Orders.IntegrationTests/StandInSagaResponders.cs:26` | CS1574 | `PublishFactAsync{TPayload}` | unresolved cref | FIXED — → `<c>PublishFactAsync&lt;TPayload&gt;</c>`; the generic method is on a different class in the file. |
| 99 | `tests/Orders.UnitTests/CancelOrderCommandHandlerTests.cs:58` | CS1574 | `OrderCancelled` | unresolved cref | FIXED — → fully qualified `OrderToCash.Orders.Domain.Events.OrderCancelled`; `…Domain` is imported but not `…Domain.Events`. |
| 100 | `tests/Orders.UnitTests/FactRetryDispatcherTests.cs:229` | CS1734 | `cancellationToken` | paramref naming a parameter not in scope | FIXED — → `<c>cancellationToken</c>`; referenced from a type summary / a parameterless test method. |
| 101 | `tests/Orders.UnitTests/RecordingKafkaProducer.cs:8` | CS0419 | `ProduceAsync` | ambiguous cref (overload set) | FIXED — → `ProduceAsync(string, Message{string, byte[]}, CancellationToken)`; the exact overload `KafkaDeadLetterPublisher` calls. |
| 102 | `tests/Orders.UnitTests/SagaFactHandlerTests.cs:768` | CS1734 | `orderId` | paramref naming a parameter not in scope | FIXED — → `<c>orderId</c>`; a method parameter referenced from a PROPERTY's summary. |
| 103 | `tests/Projector.IntegrationTests/TestSupport/KafkaGroupTestHost.cs:32` | CS1573 | `inner` | parameter with no `<param>` tag (others documented) | `<param name="inner">` written (only `budget` was documented). |
| 104 | `tests/Projector.IntegrationTests/TestSupport/KafkaGroupTestHost.cs:32` | CS1573 | `bootstrapServers` | parameter with no `<param>` tag (others documented) | FIXED — `<param name="bootstrapServers">` written. |
| 105 | `tests/Projector.IntegrationTests/TestSupport/KafkaGroupTestHost.cs:32` | CS1573 | `groupId` | parameter with no `<param>` tag (others documented) | FIXED — `<param name="groupId">` written. |
| 106 | `tests/Projector.UnitTests/RecordingKafkaProducer.cs:8` | CS0419 | `ProduceAsync` | ambiguous cref (overload set) | FIXED — → `ProduceAsync(string, Message{string, byte[]}, CancellationToken)`; the exact overload `KafkaDeadLetterPublisher` calls. |
| 107 | `tests/Seed.IntegrationTests/SeedIntegrationTests.cs:227` | CS1574 | `DeterministicParityTests` | unresolved cref | FIXED — → `<c>…</c>`. Names a #7-side oracle, not a type in this solution. |

### Bullets 2 and 3 — every site fixed, and the check made STANDING

**Bullet 2.** All 107 are fixed at the site. The per-site dispositions are in the table above; the four shapes are (a) qualify the cref to the type or namespace that actually declares the member, (b) rewrite as `<c>…</c>` where the target is genuinely not referenceable from that assembly (a test type named from `src/`, an `internal` type, a #7-side artefact), (c) name the exact overload for an ambiguous cref, (d) write the missing `<param>` tag. **Nothing was suppressed to make a site go away.**

The bullet's clause "a cref that pointed at a non-existent `<remarks>` is corrected to what the target actually carries" is satisfied at row 30: `SagaCommandRecord`'s summary said *"see `FindOperatorCancelNoteAsync`'s own remarks for the write"*, and the member it meant is on `ISagaCommandStore`, not on the record the comment is attached to — now `<see cref="ISagaCommandStore.FindOperatorCancelNoteAsync"/>`, which binds and which does carry those remarks (`ISagaCommandStore.cs:149-178`).

**Bullet 3 — the choice, and its build-time cost.** `Directory.Build.props` now sets `<GenerateDocumentationFile>true</GenerateDocumentationFile>` and `<NoWarn>$(NoWarn);CS1591</NoWarn>`, under the existing `TreatWarningsAsErrors`. `TreatWarningsAsErrors` was **not** weakened, and `CS1591` ("missing XML comment for a publicly visible member") is the only code added to `NoWarn` — documenting every public member is not a convention here and leaving it on would bury the codes that matter.

This was preferred to a `quality.sh` step because it fails **every** build, including a bare `dotnet build` in one project and an IDE build, not only the scripted quality run — and because a `quality.sh` step would have to re-derive what the compiler already knows.

**Cost, measured, same machine, back to back, nothing else running:**

| configuration | full `--no-incremental` solution build |
|---|---|
| `-p:GenerateDocumentationFile=false` | **30.83 s** |
| the committed configuration (docs on, warnings as errors) | **32.07 s** |

**+1.24 s, +4.0%.** Both runs: `0 Warning(s)`, `0 Error(s)`.

One thing worth recording because it cost a cycle and will cost the next person one: **`--` is illegal inside an XML comment**, and a `Directory.Build.props` comment that quoted `dotnet build --no-incremental` made MSBuild fail the *solution restore* with `error : Invalid framework identifier ''` — a message that names nothing to do with the real cause. The comment now says "a full, non-incremental build".

### Bullet 4 — armed

See the arming table (arms **A1** and **A2**).

### Bullet 5 — R3-A1, the triggering-event-topic docs

**Enumeration** (the claim is about the phrase, so it is swept over content, path-excluded at the source):

```
find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "consumed from"
```

Four hits, all classified:

| hit | classification |
|---|---|
| `src/Orders/Application/Ports/ISagaCommandStore.cs:79` (entry says `:77`) | **UNTRUE, FIXED.** The `triggeringEventTopic` param doc. Rewritten to say what the column is — the topic the row's `.dlq` republish is routed to — and to separate the two cases explicitly: fact-triggered rows carry the topic the envelope *was* consumed from; the RPC-triggered operator-cancel row stores the orders facts topic (`OperatorCancelRequestedEnvelope.Topic`, `= "otc.orders.facts.v1"`) purely so the synthetic envelope has a dead-letter destination, and **nothing was ever consumed from it**. |
| `src/Orders/Infrastructure/Persistence/Entities/SagaCommand.cs:55` (line exact) | **UNTRUE, FIXED.** The `TriggeringEventTopic` column's own summary, same correction. |
| `src/Orders/Application/Sagas/SagaFact.cs:34` | **TRUE, NOT CHANGED.** `SagaFact` is built only by `SagaFactsConsumer` from a genuinely consumed Kafka message; the operator-cancel envelope never becomes a `SagaFact` — `CancelOrderCommandHandler` calls `ISagaCommandStore.EnqueueAsync` directly (`CancelOrderCommandHandler.cs:213-221`). The wording is accurate here and changing it would make it less so. |
| `tests/Orders.UnitTests/SagaFactsConsumerTests.cs:103` | **TRUE, NOT CHANGED.** A comment in the consumer's own test, about a real consumed message. |

The bullet names two sites; the sweep found four and the third had to be *decided* rather than assumed, which is why it is listed with its evidence.

### Bullet 6 — `PlaceOrderCommand.cs`'s stale `<remarks>`

**Enumeration, on the retired wording, before the edit:**

```
find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "IGNORED"
find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "never looks at it again"
find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "out of scope here by the orders_acceptance brief"
find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "orders_acceptance"
```

| phrase | hits | classification |
|---|---|---|
| `IGNORED` | 2 | `src/Orders/Application/Commands/PlaceOrderCommand.cs:19` — **STALE, FIXED.** `tests/Orders.UnitTests/SagaFactHandlerTests.cs:171` — **unrelated**, the saga's `Ignored` outcome, correct as written. |
| `never looks at it again` | 1 | `PlaceOrderCommand.cs:26` — **STALE, FIXED** (same remark). |
| `out of scope here by the orders_acceptance brief` | **0** | The bullet quotes it as one string; in the file it is **wrapped across lines 22–23**, so the literal phrase does not exist and a verbatim sweep returns nothing. Widening to `orders_acceptance` (4th command) found it. **This is the sweep that would have reported "clear" and been wrong**, and it is exactly the shape `CLAUDE.md` warns about — a negative claim that is only as good as its candidate set. |
| `orders_acceptance` | 17 | 1 stale (`PlaceOrderCommand.cs:23`, the wrapped phrase, **FIXED**); the other 16 are ordinary references to the feature by name (`Program.cs:6`, `OrdersCreateResponder.cs:21`, `OrderStatus.cs:53`, `PlaceOrderCommandHandler.cs:10`, `InvalidOrderSnapshotError.cs:12`, `CancellationReasonRequiredError.cs:15`, `CancellationReasonNotApplicableError.cs:16`, `OrdersAcceptanceServiceCollectionExtensions.cs:14`, `OrderRehydrationTests.cs:59/:128/:163`, `PlaceOrderCommandHandlerTests.cs:10`, `OrdersCreateAcceptanceTests.cs:19`, `StandInFulfillmentStockCheckResponder.cs:10`, `EfCoreOrderReferenceCatalogListTests.cs:12`, `OrderNumberAllocatorTests.cs:11`) — **all correct, none changed.** |

The remark now says what is true: `RequestId` is the `orders.create` idempotency key (`RI1`–`RI5`); `orders_acceptance` deliberately carried it without acting on it and **feature 27 ended that**; `PlaceOrderCommandHandler.HandleAsync` opens with the `FindByRequestIdAsync` fast path (`RI2`, `PlaceOrderCommandHandler.cs:39-46`) which returns the original order's reply before any reference-data lookup or stock check; and `null` means "no key supplied" (`RI4`), never "ignore this field".

### Bullet 7 — `Invoice.cs`'s stale remark, enumerated as one CLASS

**Enumeration, on the six retired wordings, before the edit** (`grep -in`, path-excluded at the source, `src/` and `tests/`):

| phrase | hits | classification |
|---|---|---|
| `uncalled until` | 1 | `src/Billing/Domain/Invoice.cs:24` — **STALE, FIXED** (the site the bullet names; line number exact). |
| `no live caller` | 0 | — |
| `ships uncalled` | 0 | — |
| `delivered uncalled` | 1 | `src/Billing/Application/PaymentRegisterService.cs:13` — **TRUE, NOT CHANGED.** *"the sole LIVE caller of `Invoice.MarkPaid` and `BuyerCredit.Release`, both delivered uncalled by features 21/19"* is a statement about what features 21/19 shipped, and it is correct; it is also the evidence that `Invoice.cs:24` is stale. |
| `has no caller until` | 0 | — |
| `seam` | 58 | See below. (Counted before the edits: the sweep run today over the edited tree returns **56**, and the two rewritten lines — `Invoice.cs:24` and `BillingFactPayloadMapperTests.cs:12` — no longer contain the word, `grep -c seam` on both returning `0`.) |

`seam` is a real design term here and most hits are `test seam` / `fakeable seam` in the ordinary sense. Classified against the class the bullet defines — *a stated absence outliving the feature that ended it*:

- **STALE, FIXED — `src/Billing/Domain/Invoice.cs:24`.** Said `MarkPaidInput` is *"feature 22's seam. Delivered and unit-tested here; uncalled until `billing.payment.register` exists."* False since feature 22 shipped in phase 10: `PaymentRegisterService.cs:12` calls itself the sole live caller, calling `Invoice.MarkPaid` at `:131` and `MarkPaidAsync` at `:144`, wired at `BillingRpcResponder.cs:68/:214/:326`, proven against real infrastructure by `tests/Billing.IntegrationTests/PaymentRegisterTests.cs`. Now states what feature 21 shipped, what feature 22 supplied, and names the live caller and the integration test.
- **STALE, FIXED — `tests/Billing.UnitTests/BillingFactPayloadMapperTests.cs:11`.** Said *"`PaymentReceived` has NO live caller in this feature (feature 22's seam), so `BillingFactPayloadMapper`'s arm for it is otherwise unreachable from any integration test."* The second half has been false since feature 22: `PaymentRegisterTests.cs:93/:100/:108/:212` assert `payment.received.v1` rows written through `EfCoreInvoiceRepository.MarkPaidAsync`'s outbox drain, which is that arm. **The src/ half of this class is what id 72's own enumeration never swept, and this is the second instance in it.** The class is kept — it asserts the mapped payload field by field, which the integration test does not — and now says so.
- **STALE, FIXED — `src/Billing/Application/Ports/ICreditDecisionPort.cs:61`.** Said *"Bound today by `AlwaysApproveCreditDecision`; feature 20 replaces the DI registration only."* `BillingServiceCollectionExtensions.cs:64` binds `SimulatorCreditDecision`; feature 20 landed. Same class — a stated state of the world outliving the feature that changed it — found by the bullet's own mandated sweep, so it is fixed here rather than left for a later sighting.
- **The remaining 55 `seam` hits are the ordinary design term and assert no absence.** Arithmetic: 58 pre-edit − the 3 stale ones above (`Invoice.cs:24`, `BillingFactPayloadMapperTests.cs:12`, `ICreditDecisionPort.cs:61`) = 55. Cross-check on the edited tree: today's sweep returns 56, of which exactly one — `ICreditDecisionPort.cs:61` — is a corrected line that still uses the word legitimately; 56 − 1 = 55. They name a test seam, a fakeable port, an `InternalsVisibleTo` seam or a DI seam, and are correct as written: `DispatcherServiceCollectionExtensions.cs:46`, `Billing/InternalsVisibleTo.cs:7`, `Gateway/InternalsVisibleTo.cs:6`, `GatewayHost.cs:27`, `Fulfillment/InternalsVisibleTo.cs:7`, `NotificationDispatchService.cs:11`, `SagaConsumptionTests.cs:279/:289`, `StandInFulfillmentStockCheckResponder.cs:71`, `SagaCommandRetryTests.cs:297`, `RecordingFulfillmentStandIn.cs:210`, `NatsStockAvailabilityCheckerTests.cs:56`, `KafkaDeadLetterPublisherTests.cs:17` (Notifications), `StockResponderShutdownTests.cs:15`, `OpenApiContractTests.cs:99`, `RegisterPaymentCommandHandlerTests.cs:23`, `MoneyRepresentationHttpTests.cs:27/:168`, `GatewayTestHost.cs:15`, `StreamProjectorEndToEndTests.cs:18`, `OrdersHttpTests.cs:204`, `InvoicesHttpTests.cs:22`, `CreditResponderShutdownTests.cs:16`, `CreditHoldTests.cs:216`, `KafkaDeadLetterPublisherTests.cs:16` (Orders), `NatsSagaCommandsAdapterTests.cs:21`, `KafkaDeadLetterPublisherTests.cs:17` (Projector), `OrdersOutboxServiceCollectionExtensions.cs:63`, `OrdersSagaServiceCollectionExtensions.cs:72`, `ValidationProbes.cs:25`, `INotificationIdempotency.cs:11`, `NotificationMessage.cs:25/:29`, `SimulatorCreditDecision.cs:15`, `KafkaFactPublisher.cs:25` (Billing), `IFactPayloadMapper.cs:6` (Billing), `IClock.cs:3`, `StreamHub.cs:102`, `ProblemJsonMiddleware.cs:79`, `ISmtpTransport.cs:6`, `NatsStreamSignalSubscriber.cs:109`, `KafkaFactPublisher.cs:25` (Fulfillment), `IFactPayloadMapper.cs:6` (Fulfillment), `NatsStockAvailabilityChecker.cs:100`, `NatsSagaCommandsAdapter.cs:34/:42/:55`, `KafkaFactPublisher.cs:24` (Orders), `IIdempotentSagaRunner.cs:11`, `IRpcRequestSerializer.cs:4`, `IFactPayloadMapper.cs:6` (Orders), `SagaFactHandler.cs:12`, `KafkaDeadLetterPublisher.cs:24` (Notifications), `KafkaDeadLetterPublisher.cs:24` (Projector), `KafkaDeadLetterPublisher.cs:33` (Orders). (`PaymentRegisterService.cs:13` does not contain the word and is not in this set; it is the `delivered uncalled` hit above.)

---

## Part 2 — id 90, `degree_of_parallelism_clamp_is_unguarded_and_fails_silently`

### Bullet 4 first, because it frames the rest — is it reachable?

**Enumeration** (path-excluded at the source, whole repository, then the non-`.cs` half separately):

```
find . -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "DegreeOfParallelism"
grep -rn "DEGREE_OF_PARALLELISM\|DegreeOfParallelism" --exclude-dir=bin --exclude-dir=obj --exclude-dir=.git --exclude='*.cs' .
```

11 `.cs` hits, all classified: the declaration (`OrdersSagaOptions.cs:74`), the read (`SagaCommandDispatchWorker.cs:40`), two doc-comment mentions (`SagaCommandDispatchWorker.cs:10`, `ChannelSagaCommandSignal.cs:17`), one comment in an integration test (`OperatorCancelRacesSagaForwardProgressTests.cs:96`), and six in `SagaCommandDispatchWorkerTests.cs`. **No composition root, and no environment read.** `OrdersProgramConfiguration.ConfigureSaga` (`:31-49`) reads `KAFKA_BOOTSTRAP_SERVERS`, `KAFKA_HOST_PORT`, `FACT_RETRY_MAX_ATTEMPTS`, `FACT_RETRY_BACKOFF_MS` and the DLQ broker, and touches `options.Dispatch` nowhere. The non-`.cs` sweep returns only `CLAUDE.md`, `progress/*` and `feature_list.json` prose — **no `.env`, no compose file, no chart**. Advisory A5 is confirmed independently, not inherited.

**Decision: it is deliberately NOT configurable, and the decision is now written where it is read** — `OrdersSagaDispatchOptions.DegreeOfParallelism`'s own `<remarks>` (`src/Orders/Infrastructure/OrdersSagaOptions.cs`). The reason is that the number bounds outbound NATS RPC concurrency, whose safe range depends on the responder side rather than the deployment, and the durable `saga_commands` row plus `SagaCommandSweeper` — not this worker — is the guarantee, so there is nothing an operator would tune under incident conditions. The remark also names, in advance, what becomes mandatory the day it *is* wired: id 56/67's env-read guard convention including sibling-substitution arming, and the composition-time validation below.

### Bullet 3 — clamp silently, or fail fast? **FAIL FAST, and keep the clamp.** Here is the reason.

`CLAUDE.md`'s standing rule is that DI and configuration failures must be **loud at boot**, and this value has the worst quiet failure in the codebase. A silent clamp to 1 would hide an operator's mistake behind degraded-but-working behaviour — the fast path running at 1/8 of its designed concurrency, with id 80's head-of-line stall quietly back, and nothing anywhere saying so.

So `OrdersSagaServiceCollectionExtensions.AddOrdersSaga` now **throws** immediately after `configure(options)` when `Dispatch.DegreeOfParallelism < 1`, naming the option, the observed value and the consequence. That is composition time: the host never starts, so it can never report healthy.

**And the `Math.Max(1, …)` floor in `ExecuteAsync` is deliberately KEPT alongside it, not replaced by it.** They guard different populations. The throw covers every value that arrives through the composition root; the floor covers every construction that bypasses it — the unit tests today, and any future in-code wiring. Bullet 1 requires the floor to be guarded, which presupposes it still exists; belt and braces, both armed separately (arms A3/A4 and A5/A6).

The predicate is `< 1` rather than `== 0` on purpose: 0 is the value with the silent failure, but a negative is just as wrong and must be rejected **by name** rather than absorbed by the floor (`Math.Max(1, -3) == 1`) and never mentioned again.

### Bullets 1 and 2 — the guards, and a measurement that changed one of them

Three new tests in `tests/Orders.UnitTests/SagaCommandDispatchWorkerTests.cs` and three in the new `tests/Orders.UnitTests/OrdersSagaDispatchValidationTests.cs` (7 test cases in total).

- **Bullet 1 — `DegreeOfParallelismBelowOne_IsClampedToOneRunningConsumerLoop`** (`[Theory]`, `0` and `-3`). Observes a **count**, not a presence: the existing `ConcurrencyTrackingFakeDispatcher` records the maximum dispatches ever in flight, so the assertion reports *the degree of parallelism actually achieved* and the failure message names it. Asserted `== 1`, not `>= 1`, because `>= 1` would also pass if the floor were mutated into a constant `1` — which would cap production's default of 8 at one loop and silently reintroduce id 80's stall. The other side is pinned by the two existing tests (at 2, and at the production default).
- **Bullet 2 — `DegreeOfParallelismZero_MustNotLeaveExecuteAsyncCompletedWhileTheHostStaysUpAndHealthy`.** Asserts the property the host actually observes: that `ExecuteAsync`'s task is still **running**.
- **The premise, demonstrated rather than assumed — `ACompletedBackgroundService_LeavesTheGenericHostRunningAndTheReadinessSurfaceUp`.** Static reading settles only the Orders half of bullet 2's premise (three `IHealthCheck`s are registered — MS-SQL, Kafka, NATS — and none references the worker). The framework half is exercised here against a **real `IHost`**, with the defect's own shape (`Task.WhenAll` over `Enumerable.Range(0, 0)`) rather than a bare `Task.CompletedTask`: after `host.StartAsync()` the service's `ExecuteTask` is `RanToCompletion`, `IHostApplicationLifetime.ApplicationStopping` is **not** signalled, and the production `HealthCheckAggregator.ReadyAsync` still answers `200`/`"up"`. The assertion is written so it fails if the framework ever stops behaving this way, with a message saying the entry's premise must then be re-argued.
- **Bullet 3 — `AddOrdersSaga_DegreeOfParallelismBelowOne_ThrowsAtCompositionTime_NamingTheOptionAndTheValue`** (`[Theory]`, `0` and `-1`), plus `AddOrdersSaga_AtTheProductionDefault_DoesNotThrow` as the control, so replacing the predicate with an unconditional `throw` cannot leave the theory green.

#### The finding that came out of arming, and it is the important one

**The first draft of bullet 2's guard read `ExecuteTask.IsCompleted` immediately after `StartAsync`, and it PASSED under the very mutation it exists to catch.** Measured, not suspected — the clamp was deleted and the run reported `Failed: 2, Passed: 1`, the pass being this test.

A throwaway diagnostic produced the mechanism:

```
DIAG degree=0:  statusImmediately=WaitingForActivation, statusAfter300ms=RanToCompletion, EnumerableRangeThrows=no
DIAG degree=-3: statusImmediately=WaitingForActivation, statusAfter300ms=Faulted,         EnumerableRangeThrows=YES
```

In .NET 10 `Task.WhenAll(IEnumerable<Task>)` **enumerates asynchronously**, so the returned task is `WaitingForActivation` for a moment *whatever* the degree is, and an immediate `IsCompleted` read can never be `true` — a guard that cannot fail, inside the arming of an entry whose whole subject is a guard that cannot fail. It is now a bounded wait via `Task.WhenAny(executeTask, Task.Delay(1s))`, which is a change of **kind**: with the floor the task never completes at all; without it, it settles in milliseconds. `WhenAny` rather than a plain `await` deliberately, so a **faulted** `ExecuteTask` also counts as finished instead of rethrowing.

The second line is a correction to the entry's own text, and it is now recorded in the source. The entry says a value `<= 0` produces the silent-healthy shape. **Only `0` does.** A negative makes `Enumerable.Range`'s `ArgumentOutOfRangeException` land *inside* the returned task (for the same asynchronous-enumeration reason), so `ExecuteTask` **faults** — which a real host's default `BackgroundServiceExceptionBehavior.StopHost` does notice. The distinction is now in `SagaCommandDispatchWorker`'s remarks and in `AddOrdersSaga`'s comment, because it is precisely the kind of "what made this correct" detail a ledger exists to carry.

---

## Arming table

Protocol followed for every arm: `cp` backup → mutate → forced rebuild → run the ONE named artefact → record the message verbatim → restore **from the backup copy** (never `git checkout --`) → `cmp` against the backup → re-read the changed line → force the rebuild → confirming green run.

| # | entry / bullet | mutation | artefact | verbatim failure | restored |
|---|---|---|---|---|---|
| **A1** | 78 b4 | `src/Orders/Application/Sagas/SagaFactResult.cs:11` — one `<see cref="NoSuchTypeAnywhereInThisRepository"/>` added to an existing summary | the standing check itself: `dotnet build OrderToCash.sln --no-incremental` | `…/src/Orders/Application/Sagas/SagaFactResult.cs(11,41): error CS1574: XML comment has cref attribute 'NoSuchTypeAnywhereInThisRepository' that could not be resolved [.../src/Orders/Orders.csproj]` → `Build FAILED.` **An `error`, not a warning — `TreatWarningsAsErrors` is doing the work, and the message names both the cref and the file.** | `cmp` identical; line 11 re-read; `--no-incremental` rebuild → `Build succeeded.` |
| **A2** | 78 b3 | `Directory.Build.props:41` — `GenerateDocumentationFile` `true` → `false` | `DocumentationGenerationTests.EveryCoveredAssembly_HasACompilerEmittedXmlDocumentationFile_SoBrokenSeeCrefTargetsCannotSurviveTheBuild` | `GenerateDocumentationFile must stay ON for every project (Directory.Build.props, backlog id 78): it is the ONLY thing that makes a broken <see cref> target fail the build, and TreatWarningsAsErrors is what turns the resulting warning into an error. 11 of 11 covered assemblies failed:` followed by one line per assembly, e.g. `OrderToCash.SharedKernel: GenerateDocumentationFile is OFF for this assembly — the compiler emitted no XML documentation file at '…/OrderToCash.SharedKernel.xml'. …` **Also measured here: all 11 `*.xml` files disappeared from `bin/` on the `--no-incremental` rebuild** (`ls …/OrderToCash.*.xml \| wc -l` → `0`), so MSBuild's incremental clean removes them and the guard cannot be fooled by a stale artefact. | `cmp` identical; line 41 re-read (`true`); `--no-incremental` rebuild; 11 xml files back; test green |
| **A3** | 90 b1 | `SagaCommandDispatchWorker.cs:40` — `Math.Max(1, options.Value.Dispatch.DegreeOfParallelism)` → `options.Value.Dispatch.DegreeOfParallelism` | `SagaCommandDispatchWorkerTests.DegreeOfParallelismBelowOne_IsClampedToOneRunningConsumerLoop` (both cases) | `OrdersSagaOptions.Dispatch.DegreeOfParallelism was configured as 0 and must be clamped to exactly ONE running consumer loop by SagaCommandDispatchWorker.ExecuteAsync's Math.Max(1, ...) floor. Observed degree of parallelism: 0 (maximum dispatches in flight at once, after 3 saga commands were signalled and a 500 ms settle). 0 means Enumerable.Range(0, 0) produced NO consumer loops: the fast path dispatches nothing, for any order, forever — backlog id 90.` and the same with `configured as -3` / `Enumerable.Range(0, -3)`. | one restore covers A3+A4: `cmp` identical; line 40 re-read; rebuild; green |
| **A4** | 90 b2 | the SAME mutation as A3 | `SagaCommandDispatchWorkerTests.DegreeOfParallelismZero_MustNotLeaveExecuteAsyncCompletedWhileTheHostStaysUpAndHealthy` | `SagaCommandDispatchWorker.ExecuteAsync FINISHED within 1 s with OrdersSagaOptions.Dispatch.DegreeOfParallelism = 0 (task status RanToCompletion). Enumerable.Range(0, 0) produced no consumer loops, so Task.WhenAll had nothing to wait for and this BackgroundService finished — which does not stop the Generic Host and does not make it unhealthy (no health check references this worker; see ACompletedBackgroundService_LeavesTheGenericHostRunningAndTheReadinessSurfaceUp). The host therefore reports HEALTHY while the saga fast path dispatches nothing for any order, forever, and every command falls back to the 30 s sweeper. Backlog id 90, bullet 2 — the Math.Max(1, ...) floor in ExecuteAsync is what prevents this.` **This is the arm that first passed under the mutation and was rewritten — see the finding above.** | as A3 |
| **A5** | 90 b3 | `OrdersSagaServiceCollectionExtensions.cs:60` — `if (options.Dispatch.DegreeOfParallelism < 1)` → `if (false)` | `OrdersSagaDispatchValidationTests.AddOrdersSaga_DegreeOfParallelismBelowOne_ThrowsAtCompositionTime_NamingTheOptionAndTheValue` (both cases) | `AddOrdersSaga ACCEPTED OrdersSagaOptions.Dispatch.DegreeOfParallelism = -1 without throwing. A value below 1 must fail fast at composition time (CLAUDE.md: DI and configuration failures are loud at boot). At 0, SagaCommandDispatchWorker.ExecuteAsync gets no consumer loops, finishes successfully, and leaves the host running and reporting healthy while the saga fast path dispatches nothing for any order, forever. Backlog id 90, bullet 3.` (and the same for `= 0`) | `cmp` identical; line 60 re-read; green |
| **A6** | 90 b3, the MESSAGE | production exception message only: `it was configured as {…}` → `it was out of range`, and `reports healthy` removed. The predicate left intact. | same test | `the composition-time failure must name the OBSERVED value (-1), not merely that some value was wrong. Message was: OrdersSagaOptions.Dispatch.DegreeOfParallelism must be at least 1; it was out of range. …` — so the message assertions are load-bearing, not decoration. | `cmp` against backup + the one intentional wording polish re-applied and re-read; green |

**The first version of A5 is itself recorded as a defect found and fixed.** It used `Assert.Throws<InvalidOperationException>`, whose failure reads `Assert.Throws() Failure: No exception was thrown / Expected: typeof(System.InvalidOperationException)` — a message that names nothing, which `CLAUDE.md` (adopted as id 82) rules is not acceptable arming evidence. Rewritten to an explicit `try`/`catch` plus `Assert.True` with a naming message, and the reason is a comment in the test.

**Not armable, and stated rather than glossed:** id 78's bullets 5, 6 and 7 are prose corrections. There is no executable claim to mutate. What *can* be shown is that each enumeration was run before the edit and that each fix is checkable against cited source lines, which is what the tables above do.

## Ported-idiom ledger

| property | #7 relied on | in #8 that property is supplied by | guard |
|---|---|---|---|
| **A broken doc-comment link is caught mechanically** | **Nothing — verified, not assumed.** `grep -rn "typedoc\|tsdoc\|jsdoc" package.json eslint.config.mjs tsconfig.base.json` in `order-to-cash-nestjs` returns **no output**, and `package.json`'s `devDependencies`/`dependencies` contain no package with `doc` in the name (`quality` = `lint && typecheck && test:coverage`). TypeScript has no `<see cref>` construct at all — a stale `@link` or a prose reference to a renamed symbol compiles and lints clean there, exactly as it did here while `GenerateDocumentationFile` was `false`. So this is a **strengthening**, not a port: #8 now has a build-failing check for a class #7 cannot express and does not check. | `Directory.Build.props`: `GenerateDocumentationFile=true` + `TreatWarningsAsErrors` + `NoWarn=CS1591` | **A1** (the build itself fails, naming the cref and file) and **A2** (`DocumentationGenerationTests…`, which fails if the property is ever turned off) |
| **Fast-path dispatch concurrency** | **The Node event loop, with no number to get wrong.** #7 has no dispatch worker and no parallelism setting: each saga command is dispatched by its own `@CommandHandler` (`apps/orders/src/application/commands/saga-dispatch.handlers.ts:18-60`, six handlers each `await this.dispatcher.dispatch(...)`), invoked per command by `@nestjs/cqrs`. `grep -rniE "concurrency\|parallel\|mergemap" apps/orders/src --include='*.ts'` returns 13 hits and **not one is about dispatch** — they are the order-number allocator's concurrency safety (5), a Jest parallel-worker timeout comment (3), an outbox-relay concurrency integration spec (1), a test-matrix-guard comment (1), and three more allocator lines. | #8's `OrdersSagaDispatchOptions.DegreeOfParallelism` (default 8) driving `Enumerable.Range(0, n)` consumer loops — a **configured integer**, which is a failure surface #7 structurally does not have. A value of 0 has no #7 analogue at all. | **A3/A4** (the floor) and **A5/A6** (composition-time rejection). Both are new obligations created by the translation, which is exactly what this row exists to record. |

**Enumerating #7's tests for the ported mechanism**, per `CLAUDE.md`: `find apps/orders/src -name '*.spec.ts' -print0 | xargs -0 grep -lin "dispatchworker\|degreeofparallelism\|clamp"` returns **no output**. There is no #7 guard to port, dropped or otherwise, because there is no #7 mechanism. Both rows are strengthenings; neither is a guard lost in translation.

## Defeat list — which of `CLAUDE.md`'s ten attacks were run against the new guards

| # | attack | run against `DocumentationGenerationTests` | run against id 90's four guards |
|---|---|---|---|
| 1 | Delete the behaviour | **Yes — A2** (property flipped off) and **A1** (a real broken cref introduced). | **Yes — A3/A4** (floor deleted), **A5** (predicate defeated). |
| 2 | Corrupt a payload field the test supplied | **Yes, structurally**: the test does not merely check the file exists, it requires `<doc><assembly><name>` to equal the assembly name, so a wrong-assembly `.xml` fails. | **Yes — A6** (the exception message corrupted with the predicate intact; two of three `Contains` assertions fire). |
| 3 | Substitute a valid sibling identifier | **N/A — no identifier is a parameter here.** The assembly list is a literal of `typeof(...)` references; substituting one would be a compile-level change that still names a real assembly and would fail on the `<name>` comparison. No `MSSQL_DB_*`-style family is involved. | **Partly N/A, partly done.** Id 80's review already armed the sibling substitution (`Dispatch.DegreeOfParallelism` → `Command.MaxAttempts`) and it is RED. There is no environment key to substitute — bullet 4 establishes there is none. |
| 4 | Shadow the pattern from a comment or string literal | **N/A.** The guard is not a text scanner; it reads a compiler-emitted artefact off `Assembly.Location`. A comment cannot produce an `.xml` file. | **N/A.** These guards execute code. |
| 5 | Hide the real thing in a dead region (`#if false`) | **N/A** for the same reason — and note the standing build check *is* affected by preprocessor regions in the way any compile is, which is correct rather than a hole: the compiler and the check are the same instrument. | **N/A.** |
| 6 | Hide it in a raw or verbatim string | **N/A**, same reason. | **N/A.** |
| 7 | Drop an OPTIONAL element entirely | **Yes:** the absent-`.xml` branch is the primary failure path, and A2 exercised it for all 11 assemblies at once. | **Yes:** A5 is precisely "the element (the throw) is absent"; A3/A4 are "the loop is absent". |
| 8 | Compare a literal to a literal | **The attack this guard was designed against.** It never reads `Directory.Build.props`. A props-file string assertion would pass on a file MSBuild never evaluated, on a `.csproj`-level override and on a command-line override; reading the emitted `.xml` cannot. Stated in the test's own remarks. | **N/A** — no population claim is made. |
| 9 | Satisfy the closer half of a two-part claim, leaving the premise stale | **Run, and it bit.** Bullet 2's guard had a premise ("a completed `BackgroundService` leaves the host healthy") asserted nowhere; it is now `ACompletedBackgroundService_LeavesTheGenericHostRunningAndTheReadinessSurfaceUp`, written to fail if the framework changes. | Same row — that test is id 90's. |
| 10 | Let a build-output copy join the population | **Run.** Every enumerating command in this record excludes `bin/`/`obj/` **by path** at the `find` level, never by post-filtering `grep` output. The guard itself reads *from* `bin/` on purpose — that is the artefact under test — and pins the identity via `<assembly><name>`, so a foreign copy fails rather than satisfies. | **Run** — same path-exclusion discipline in the `DegreeOfParallelism` sweep. |

## Suite counts, reconciled against 2 034

`./quality.sh`, one clean run, after every restore and a forced rebuild:

```
[OK]    dotnet format --verify-no-changes: clean
[OK]    dotnet build: succeeded          (0 Warning(s), 0 Error(s) — with documentation generation ON)
[OK]    dotnet test: all tests passed
[OK]    quality.sh finished
```

**2 042 passed, 0 failed, 0 skipped, across 18 projects.**

| project | before | after | delta |
|---|---|---|---|
| Orders.UnitTests | 491 | **498** | **+7** — `DegreeOfParallelismBelowOne_IsClampedToOneRunningConsumerLoop` ×2 cases, `DegreeOfParallelismZero_MustNotLeaveExecuteAsyncCompletedWhileTheHostStaysUpAndHealthy`, `ACompletedBackgroundService_LeavesTheGenericHostRunningAndTheReadinessSurfaceUp`, `AddOrdersSaga_DegreeOfParallelismBelowOne_ThrowsAtCompositionTime_NamingTheOptionAndTheValue` ×2 cases, `AddOrdersSaga_AtTheProductionDefault_DoesNotThrow` |
| Architecture.Tests | 49 | **50** | **+1** — `EveryCoveredAssembly_HasACompilerEmittedXmlDocumentationFile_SoBrokenSeeCrefTargetsCannotSurviveTheBuild` |
| Gateway.UnitTests | 237 | 237 | — |
| Billing.UnitTests | 262 | 262 | — |
| Fulfillment.UnitTests | 146 | 146 | — |
| every other project | unchanged | unchanged | — |
| **total** | **2 034** | **2 042** | **+8** |

**2 034 + 7 + 1 = 2 042.** Exact; nothing else moved. The 107 doc-comment fixes are comment-only and changed no test count, which is itself the expected result.

Full per-project after: SharedKernel.UnitTests 50, Cqrs.UnitTests 23, Contracts.UnitTests 24, Notifications.UnitTests 111, Gateway.UnitTests 237, Fulfillment.UnitTests 146, Billing.UnitTests 262, Orders.UnitTests 498, Seed.UnitTests 44, Projector.UnitTests 120, Architecture.Tests 50, Seed.IntegrationTests 6, Projector.IntegrationTests 68, Notifications.IntegrationTests 29, Fulfillment.IntegrationTests 64, Billing.IntegrationTests 90, Gateway.IntegrationTests 65, Orders.IntegrationTests 155.

`./init.sh` exits 0.

## Files touched

**Build configuration (1)** — `Directory.Build.props` (`GenerateDocumentationFile` → `true`, `NoWarn` → `$(NoWarn);CS1591`, with the reasoning and the "do not defang this" note in a comment).

**New tests (2)** — `tests/Architecture.Tests/DocumentationGenerationTests.cs`, `tests/Orders.UnitTests/OrdersSagaDispatchValidationTests.cs`.

**Behaviour (2)** — `src/Orders/Infrastructure/OrdersSagaServiceCollectionExtensions.cs` (the composition-time throw), `src/Orders/Infrastructure/Saga/SagaCommandDispatchWorker.cs` (doc only; the clamp itself is unchanged).

**Tests changed (2)** — `tests/Orders.UnitTests/SagaCommandDispatchWorkerTests.cs` (three tests and two fixtures added; no existing test's assertion altered), `tests/Billing.UnitTests/BillingFactPayloadMapperTests.cs` (bullet 7's second instance — summary only).

**Comment-only, 74 files** across `src/` and `tests/`, counted rather than estimated — `awk '{print $1}' sites_fileline.txt | sort -u | wc -l` gives **68** distinct files holding the 107 doc-comment sites, plus **6** files touched only by bullets 5/6/7 and their sweeps' findings (`SagaCommand.cs`, `PlaceOrderCommand.cs`, `Invoice.cs`, `ICreditDecisionPort.cs`, `BillingFactPayloadMapperTests.cs`, and `OrdersSagaOptions.cs`'s "deliberately not configurable" remark), with **no overlap** between the two sets (`comm -12` returns nothing). No executable line was changed in any of the 74.

## What I could not do, and one thing I did not do

- **`feature_list.json` is untouched.** The brief forbids it explicitly and says the coordinator owns the transitions. Both entries therefore still read `"status": "in_progress"` (78) and `"status": "pending"` (90); **the coordinator needs to move both to `in_review`.** Flagging it because the standing implementer instruction says to make that transition and the brief overrides it — this is a deliberate omission, not an oversight.
- **Id 78's bullets 5–7 have no arming.** Prose has no executable claim to mutate. Each enumeration was run before the corresponding edit and each replacement text cites the source lines that make it true, which is the strongest available substitute and is what the tables above provide.
- **The doc-comment class is fixed and now standing, but the check is scoped to what the compiler binds.** It catches a `<see cref>` that cannot resolve; it cannot catch a cref that resolves to the *wrong but existing* member, nor prose that is simply out of date — which is precisely the defect bullets 5, 6 and 7 are about, and why those had to be found by sweeps rather than by the build. Worth knowing before anyone concludes the class is now mechanically closed.

## Things that surprised me, in the order they cost time

1. **`--` is not allowed inside an XML comment**, and MSBuild's report of it is `error : Invalid framework identifier ''` from `NuGet.targets`, during *restore*, naming nothing related. A comment quoting `dotnet build --no-incremental` broke the whole solution.
2. **`Task.WhenAll(IEnumerable<Task>)` does not complete synchronously in .NET 10**, even over an empty sequence, and that turned a guard into one that could not fail. It was found only because the arming protocol demands the mutation actually be run — the very failure mode the entry exists to fix, reproduced inside its own fix. It also corrected the entry's premise: only `0` is silent; a negative faults the task.
3. **107 sites, not the "12 in the Orders build closure" the entry anticipated.** The Orders closure alone holds 34 once all four documentation codes are counted.
4. **The cost of turning the check on is 1.24 s** on a 31-second full rebuild. The reason it was never on is not that it is expensive.
5. **The two extra prose instances were both found by the bullets' own mandated sweeps**, not by the sites the bullets name — which is the enumeration rule earning its keep twice in one entry.
