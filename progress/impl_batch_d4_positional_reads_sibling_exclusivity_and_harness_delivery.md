# Batch D4 — backlog id 74: positional reads, sibling exclusivity, structural group clearance, and `test.runsettings` delivery

**Status: PASS.** All seven acceptance bullets are met. **41 armings** — 6 for the reads, 13 for sibling exclusivity, 9 for the clearance, 6 for the census, 7 for the runsettings delivery — every one red under its mutation with a message that names the claim, restored (`cmp` identical), rebuilt `--no-incremental`, and confirmed green.

I did **not** touch `feature_list.json` — the brief forbids it and says the coordinator owns its transitions. That is the one place where the brief and my standing instruction ("set the feature's status to `in_review`") differ; I followed the brief and record the discrepancy here so the coordinator can make the transition.

---

## 1. The contract, read from `feature_list.json` id 74, and where each bullet is met

| Bullet | Substance | Where |
|---|---|---|
| 1 | the positional-read class enumerated repository-wide FIRST, as a search result, one classification line per **call site** | §2 |
| 2 | each positional read selects by content the test controls | §5.1 |
| 3 | armed by change of kind, per site — decoy first, real second | §7, arms P1–P6 |
| 4 | joins the guard-hardening loop with ids 67–70 | §9 |
| 5 | the sibling-exclusivity class (A13) enumerated and closed | §3, §5.2, arms X1–X13 |
| 6 | the consumer-group clearance made STRUCTURAL; teardown must not mask a test's own failure | §4, §5.3, arms G1–G9 and C1–C6 |
| 7 | `test.runsettings` delivery guarded against a PER-PROJECT escape | §5.4, arms R1–R7 |

**Where the brief and a bullet differ.** Two places, both recorded rather than resolved silently:

- The brief calls bullet 6 *"making the consumer-group clearance structural rather than per-site, across the teardown sites feature 27 applied it to."* The **bullet** asks for more than that: the clearance must live *"where a host is built or disposed (a test-host wrapper or fixture)"*, **plus** a guard, **plus** the non-masking property. I implemented all three. The bullet's own count correction is also honoured: I re-derived the population rather than reusing the filed "12/25/4".
- The brief describes bullet 4 as *"not a fourth piece of work"* but tells me to decide myself what it obliges. §9 states what I concluded and the evidence.

---

## 2. Enumeration 1 — the positional-read class (**unit: a read call site**, not a file)

`consumer.Consume(...)` is the only way a message is read from Kafka anywhere under `tests/`. Two commands, both excluding `bin`/`obj` **by path**:

```
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "consumer\.Consume(" | sort
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "ConsumerBuilder<"  | sort
```

The second command exists so that a read through a differently-named variable cannot hide from the first: every Kafka consumer that exists in the tree is listed, and each is then classified as a *message* reader or not. Nothing is filtered by the property under test (positional vs content) — membership is decided by *"is this a Kafka consumer"*, so a positional read shows up in the list rather than removing itself from it.

**Before this entry the population was 20 message-read call sites; two of them were positional. It is now 18, because the two positional helpers were deleted rather than left unused.** The post-change output is reproduced verbatim in `scratchpad/enum_e1.txt`. One classification line per hit:

### Message reads (18 sites)

| # | Site | Selects by | Verdict |
|---|---|---|---|
| 1 | `tests/Billing.IntegrationTests/BillingOutboxRelayTests.cs:142` (`ConsumeMatchingEventId`) | the outbox row's own `eventId` | **FIXED by this entry** — was `consumer.Consume(20s)`, i.e. the first message on a shared topic |
| 2 | `tests/Fulfillment.IntegrationTests/FulfillmentOutboxRelayTests.cs:129` (`ConsumeMatchingEventId`) | the outbox row's own `eventId` | **FIXED by this entry** — same pre-fix shape |
| 3 | `tests/Notifications.IntegrationTests/NotificationDeadLetterTests.cs:408` (`ConsumeMatchingAsync`) | the envelope's own `correlationId` | **FIXED by this entry** — `OR1_R16` called a positional `ConsumeOneAsync(topic, timeout)`, now deleted; both cases in the class use this one |
| 4 | `tests/Notifications.IntegrationTests/NotificationDegradesOnPermanentFailureTests.cs:336` (`TryConsumeMatchingAsync`) | the envelope's own `correlationId` | already correct |
| 5 | `tests/Orders.IntegrationTests/FactPartitioningTests.cs:68` | the message key, against this test's own two order ids | already correct |
| 6 | `tests/Orders.IntegrationTests/LogCorrelationTests.cs:246` (`WaitForDlqMessageAsync`) | the envelope's own `correlationId` | already correct |
| 7 | `tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs:644` (`ConsumeMatchingAsync`) | the envelope's own `correlationId` | already correct |
| 8 | `tests/Orders.IntegrationTests/OutboxRelayTests.cs:94` | the message key = this test's order id | already correct |
| 9 | `tests/Orders.IntegrationTests/OutboxWireParityTests.cs:86` | the message key = `row.CorrelationId` | already correct |
| 10 | `tests/Orders.IntegrationTests/OutboxWireParityTests.cs:168` | the message key = this test's order id | already correct |
| 11 | `tests/Orders.IntegrationTests/OutboxWireParityTests.cs:246` | key **and** `eventType == "order.cancelled.v1"` | already correct |
| 12 | `tests/Orders.IntegrationTests/OutboxWireParityTests.cs:303` | key **and** `eventType == "order.cancelled.v1"` | already correct |
| 13 | `tests/Orders.IntegrationTests/SagaCommandDeadLetterTests.cs:246` (`ConsumeMatchingAsync`) | the envelope's `correlationId`, optionally `eventType` | already correct |
| 14 | `tests/Orders.IntegrationTests/SagaCommandDeadLetterTests.cs:281` (`ConsumeAllRemaining`) | the envelope's `correlationId`; an ABSENCE probe | already correct |
| 15 | `tests/Orders.IntegrationTests/SagaDeadLetterTests.cs:262` (`ConsumeOneAsync(topic, correlationId, timeout)`) | the envelope's own `correlationId` | already correct — this is the reference shape the two fixes copy |
| 16 | `tests/Orders.IntegrationTests/TraceContextPropagationTests.cs:209` | the message key = this test's order id | already correct |
| 17 | `tests/Orders.IntegrationTests/TraceContextPropagationTests.cs:281` | the message key = this test's order id | already correct |
| 18 | `tests/Projector.IntegrationTests/ProjectorDeadLetterTests.cs:303` (`ConsumeMatchingAsync`) | the envelope's own `correlationId` | **FIXED by this entry** — `OR1_R16` called a positional `ConsumeOneAsync(topic, timeout)`, now deleted |

### Hits in the same output that are NOT reads (left visible, not filtered away)

| Site | Why it is not a positional read |
|---|---|
| `tests/Billing.IntegrationTests/BillingOutboxRelayTests.cs:95` | a COMMENT containing the text `consumer.Consume(...)` — it is the comment explaining the arming, not code |
| `tests/Fulfillment.IntegrationTests/FulfillmentOutboxRelayTests.cs:95` | the same comment |
| `tests/Notifications.IntegrationTests/NotificationDeadLetterTests.cs:441` | `ConsumerBuilder<Ignore, byte[]>` used only for `consumer.Committed(...)` — never calls `Consume()` |
| `tests/Orders.IntegrationTests/MetricsExposureTests.cs:258` | `ConsumerBuilder<Ignore, Ignore>` used only for `QueryWatermarkOffsets` |
| `tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs:380` | `ConsumerBuilder<Ignore, byte[]>` used only for `consumer.Committed(...)` |
| `tests/Projector.IntegrationTests/OffsetContractTests.cs:50` | `ConsumerBuilder<Ignore, byte[]>` used only for `consumer.Committed(...)` |
| `tests/Projector.IntegrationTests/ProjectorDeadLetterTests.cs:336` | `ConsumerBuilder<Ignore, byte[]>` used only for `consumer.Committed(...)` |
| `tests/Billing.UnitTests/BillingConsumesNoFactsTests.cs:92` | a string literal written to a scratch file by that guard's own arming probe |

**The three dead-letter tests the entry names were indeed not where the class ends.** It also covered Billing's `BC16` and Fulfillment's `FS16`, whose reads took the first message on a topic the whole Kafka collection shares and then asserted only the message **key** — an assertion a decoy under the same key satisfies. Those two are the sites that would have been missed by treating the filed list as the population.

---

## 3. Enumeration 2 — the sibling-exclusivity class (advisory A13) (**unit: an exactly-one assertion site**)

A13's own command re-run (line numbers have moved since the review):

```
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "Measurements"
```

That command only finds the *metric* capture. The bullet's class is wider — *"a MeterListener/ActivityListener collection, a shared topic, a process-wide sink"* — so the population was derived from the **capture mechanism**, not from the assertion, and then every exactly-one assertion over each mechanism was classified. Deriving membership from the capture is what keeps the sweep honest: an unscoped assertion shows up as an unscoped line, it does not remove itself.

The capture mechanisms that exist in this repository, enumerated:

```
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -ln "MeterListener"                       → 3 files (the three MetricCapture copies)
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n  "Exported"                             → 4 RecordingActivityExporter copies + 9 use sites
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -ln "CapturedConsole"                      → 6 copies + 8 use files
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n  "AddActivityListener"                  → 8 sites
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n  "static.*List<\|static.*Concurrent"     → no mutable static test capture exists
```

### (a) `MetricCapture` — a `MeterListener` on the PROCESS-WIDE `OtcMetrics` meter. 9 exactly-one sites; A13 counted 9.

| # | Site (current line) | Instrument | Was | Now |
|---|---|---|---|---|
| X1 | `tests/Gateway.UnitTests/RequestLatencyMiddlewareTests.cs:24` | `otc_request_latency_ms` | `Assert.Single(capture.Measurements)` | `capture.SingleOwnMeasurement()` + interloper |
| X2 | `tests/Gateway.UnitTests/RequestLatencyMiddlewareTests.cs:40` | `otc_request_latency_ms` | same | same |
| X3 | `tests/Orders.UnitTests/FactRetryDispatcherTests.cs:111` | `otc_fact_processing_latency_ms` | `Assert.Single(meterListener.Measurements)` | `meterListener.SingleOwnMeasurement()` + interloper |
| X4 | `tests/Orders.UnitTests/FactRetryDispatcherTests.cs:149` | `otc_fact_processing_latency_ms` | same | same |
| X5 | `tests/Orders.IntegrationTests/MetricsExposureTests.cs:65` | `otc_outbox_lag_ms` | `Assert.Single(capture.Measurements)` | `capture.SingleOwnMeasurement()` + interloper |
| X6 | `tests/Orders.IntegrationTests/MetricsExposureTests.cs:77` | `otc_outbox_lag_ms` | same | same |
| X7 | `tests/Orders.IntegrationTests/MetricsExposureTests.cs:118` | `otc_dlq_depth` | `Assert.Single(capture.LongMeasurements)` | `capture.SingleOwnLongMeasurement()` + interloper |
| X8 | `tests/Orders.IntegrationTests/MetricsExposureTests.cs:138` | `otc_dlq_depth` | same | same |
| X9 | `tests/Orders.IntegrationTests/MetricsExposureTests.cs:186` | `otc_dlq_depth` | same | same |

Non-exactly-one reads of the same capture, left as they are and classified: `RealInfraMetricsProvenanceTests.cs:86,102,202,209,212,222` are `Assert.Contains` / `Assert.NotEmpty` / bounded polls over `capture.Measurements` — they do not claim exclusivity, so foreign measurements cannot make them wrong.

### (b) `RecordingActivityExporter` — a `BaseExporter<Activity>` on a `TracerProvider` over the PROCESS-WIDE `OtcActivity.Source`. 9 use sites, 8 exactly-one.

| # | Site | Scoped by | Verdict |
|---|---|---|---|
| X10 | `tests/Orders.IntegrationTests/TraceContextPropagationTests.cs:149` | `DisplayName` ONLY | **FIXED** — now also `a.TraceId == writeActivity!.TraceId`, armed by a foreign span |
| X11 | `tests/Orders.IntegrationTests/TraceContextPropagationTests.cs:169` | `DisplayName` ONLY | **FIXED** — same |
| X12 | `tests/Orders.IntegrationTests/TraceContextPropagationTests.cs:325` | `DisplayName` ONLY | **FIXED** — now also `a.TraceId == rpcSpan!.TraceId` |
| X13 | `tests/Orders.IntegrationTests/TraceContextPropagationTests.cs:326` | `DisplayName` ONLY | **FIXED** — same |
| — | `tests/Orders.IntegrationTests/TraceContextPropagationTests.cs:109` | `DisplayName` **and** `a.TraceId == callerActivity.TraceId` | already correct |
| — | `tests/Billing.IntegrationTests/BillingRpcResponderTraceContinuationTests.cs:54` | `DisplayName` **and** `TraceId` | already correct |
| — | `tests/Fulfillment.IntegrationTests/StockRpcResponderTraceContinuationTests.cs:59` | `DisplayName` **and** `TraceId` | already correct |
| — | `tests/Orders.IntegrationTests/OrdersCreateResponderTraceContinuationTests.cs:75` | `DisplayName` **and** `TraceId` | already correct |
| — | `tests/Gateway.IntegrationTests/LogCorrelationTests.cs:112` | `Assert.NotEmpty`, not exactly-one | not in class |

**A13 counted nine sites and stopped at the metric capture. The four unscoped exporter sites were a second instance of the same class that the review did not reach** — the entry's own words, *"the review counted nine such sites beyond the one feature 27 fixed"*, were where it was noticed, not where it ends.

### (c) `CapturedConsole` — a process-wide `Console.Out` redirect. 4 exactly-one sites, all already scoped.

| Site | Scoped by | Verdict |
|---|---|---|
| `tests/Gateway.IntegrationTests/LogCorrelationTests.cs:155` | a marker literal unique to the test (`Row46_no_span_marker`) | already correct |
| `tests/Orders.IntegrationTests/LogCorrelationTests.cs:198` | a marker literal unique to the test (`R58_OR7_no_span_marker`) | already correct |
| `tests/Orders.IntegrationTests/LogCorrelationTests.cs:99`, `tests/Notifications.IntegrationTests/LogCorrelationTests.cs:106`, `tests/Projector.IntegrationTests/LogCorrelationTests.cs:84` | `Assert.Single(traceIds.Distinct(...))` over records already filtered to this test's own `correlationId` | already correct |

### (d) `ActivityListener` constructions — 8 sites, none in class.

All eight (`Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs:151,239`; `Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs:108`; `Orders.UnitTests/KafkaDeadLetterPublisherTests.cs:34`, `NatsSagaCommandsAdapterTests.cs:237`, `TraceContextCarrierTests.cs:29`; `Notifications.UnitTests/KafkaDeadLetterPublisherTests.cs:35`; `Projector.UnitTests/KafkaDeadLetterPublisherTests.cs:35`) exist only to force sampling; none of them collects into a list that an exactly-one assertion then reads. The `Assert.Single(producer.Produced)` assertions nearby are over a `RecordingKafkaProducer` **instance the test owns**, which nothing else can write to.

### (e) A shared TOPIC is the fourth capture, and it is enumeration 1.

---

## 4. Enumeration 3 — the consumer-group clearance (**unit stated explicitly: a call site, and separately a host BUILD site**)

### 4a. The filed figure, re-derived

```
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "StopHostAndWaitForGroupToClearAsync" | sort | nl
```

**55 occurrences = 3 definitions + 1 doc-comment mention (`NotificationConsumptionTests.cs:113`) + 51 call sites.** That reconciles exactly with the leader's correction of 51 and refutes the filed "12 Notifications + 25 Orders + 4 Projector = 41". By project the 51 call sites are **Notifications 11, Orders 36, Projector 4**.

### 4b. The unit the *fix* is about is a host BUILD site, not a teardown site

A16's failure mode is a teardown that forgets the helper. Guarding teardown sites one by one reproduces the per-site discipline the bullet asks me to retire. The population that actually determines the hazard is *"which hosts join a shared Kafka consumer group"*, and that is determined by content: the three composition roots `OrdersHost.CreateBuilder`, `NotificationsHost.CreateBuilder` and `ProjectorHost.CreateBuilder` **unconditionally** register their service's `KafkaFactStreamSubscriber` and its consumer —

```
src/Orders/Infrastructure/OrdersSagaServiceCollectionExtensions.cs:44,97
src/Notifications/Infrastructure/NotificationsServiceCollectionExtensions.cs:67,105
src/Projector/Infrastructure/ProjectorServiceCollectionExtensions.cs:63,66
```

— with the group ids `"orders.saga"`, `"notifications"` and `"projector"` hard-coded at `KafkaFactStreamSubscriber.cs:130 / :115 / :111`. A host built any other way (a bare `Host.CreateApplicationBuilder()` plus `AddOrdersOutbox`/`AddOrdersAcceptance`) joins no group at all — `SagaConsumptionTests.cs:66-70` already documents exactly that for its own first host.

```
for p in Orders Notifications Projector; do find tests/$p.IntegrationTests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "Host.CreateBuilder(\|Host.CreateApplicationBuilder()" | sort; done
```

**16 group-joining build sites.** One classification line each:

| # | Build site | Before | Now |
|---|---|---|---|
| 1 | `Orders.IntegrationTests/SagaIntegrationTestSupport.cs:50` | bare `IHost` | returns `KafkaGroupTestHost` |
| 2 | `Orders.IntegrationTests/SagaConsumptionTests.cs:86` (`secondBuilder`) | bare `IHost` | wrapped |
| 3 | `Orders.IntegrationTests/SagaConsumptionTests.cs:126` | bare `IHost` | wrapped |
| 4 | `Orders.IntegrationTests/HealthProbesTests.cs:135` | bare `IHost` | wrapped |
| 5 | `Orders.IntegrationTests/SagaCommandRetryTests.cs:225` | bare `IHost` | wrapped |
| 6 | `Notifications.IntegrationTests/NotificationConsumptionTestSupport.cs:52` | bare `IHost` | returns `KafkaGroupTestHost` |
| 7 | `Notifications.IntegrationTests/NotificationConsumptionTests.cs:120` (`builder2`) | bare `IHost` | wrapped |
| 8 | `Notifications.IntegrationTests/NotificationDeadLetterTests.cs:70` | bare `IHost` | wrapped |
| 9 | `Notifications.IntegrationTests/NotificationDeadLetterTests.cs:235` | bare `IHost` | wrapped |
| 10 | `Notifications.IntegrationTests/NotificationDegradesOnPermanentFailureTests.cs:53` | bare `IHost` | wrapped |
| 11 | `Notifications.IntegrationTests/LogCorrelationTests.cs:57` | bare `IHost` | wrapped |
| 12 | `Notifications.IntegrationTests/HealthProbesTests.cs:138` | bare `IHost` | wrapped |
| 13 | `Projector.IntegrationTests/TestSupport/ProjectorTestHost.cs:33` | bare `IHost` | returns `KafkaGroupTestHost` |
| 14 | `Projector.IntegrationTests/HealthProbesTests.cs:135` | bare `IHost` | wrapped |
| 15 | `Projector.IntegrationTests/OffsetContractTests.cs:73` | bare `IHost` | wrapped |
| 16 | `Projector.IntegrationTests/ProjectorBootTests.cs:18` | bare `IHost` | **EXEMPT, literally and with a reason**: its `StartAsync` is asserted to throw (unreachable NATS), so the consumer never subscribes and no membership is created |

Non-group host builds in the same output, classified and excluded on a **content** ground (they register no consumer), not on the property under test: `Orders.IntegrationTests/CatalogReferenceListAcceptanceTests.cs:372`, `OrdersCreateAcceptanceTests.cs:683`, `OrdersCreateIdempotentReplayTests.cs:453`, `OrdersCreateResponderTraceContinuationTests.cs:88`, `SagaConsumptionTests.cs:44`, plus the two `Host.CreateApplicationBuilder()` probes inside each new `KafkaGroupTeardownGuardTests`.

### 4c. The bare-`StopAsync` population the entry warned about

The entry says a bare `.StopAsync(` appears 128 times across `tests/` and is **not** a population of escapes. Confirmed and not used as one. Within the three projects it is 26 occurrences, of which the ones on group-joining hosts are now routed through the wrapper by construction. The three-project scope was re-verified: the only other group ids in `tests/` are `tests/Billing.IntegrationTests/BillingOutboxRelayTests.cs:86` and `tests/Fulfillment.IntegrationTests/FulfillmentOutboxRelayTests.cs:86`, both `GroupId = $"test-{Guid.NewGuid():N}"` — a fresh per-run group that cannot join the shared-group race. (Those two line numbers moved by this entry's own edits; the *shape* is unchanged.)

---

## 5. What changed, and why

### 5.1 Positional reads → content selection (bullets 1–3)

- `tests/Notifications.IntegrationTests/NotificationDeadLetterTests.cs` — `OR1_R16` now reads through `ConsumeMatchingAsync(DlqTopic, correlationId, …)`; the positional `ConsumeOneAsync(topic, timeout)` overload is **deleted**, not left unused. An unused positional reader is a loaded gun for the next test that needs a `.dlq` read.
- `tests/Projector.IntegrationTests/ProjectorDeadLetterTests.cs` — the same change, keyed on the poison envelope's `orderId` (which is also its `correlationId`).
- `tests/Billing.IntegrationTests/BillingOutboxRelayTests.cs`, `tests/Fulfillment.IntegrationTests/FulfillmentOutboxRelayTests.cs` — `consumer.Consume(20s)` replaced by `ConsumeMatchingEventId(consumer, publishedRow.EventId, …)`, plus an explicit `AssertIsThisTestsOwnRecord` that names the record actually read. The pre-existing message-key assertion **cannot** do that job: the decoy shares the key by design, which is precisely why the old shape was dangerous rather than merely untidy.
- Four stale comments that described the retired positional overload were corrected: the two `LogCorrelationTests` class comments in Notifications and Projector, and the two `OR4_R57` case comments.

### 5.2 Sibling exclusivity (bullet 5)

`MetricCapture` (all three copies) gains a **provenance scope**. `otc_outbox_lag_ms` and `otc_dlq_depth` carry no tags at all, so there is no tag to scope by; what every measurement does carry is the `ExecutionContext` of the code that recorded it. `MeterListener` invokes its callback synchronously on the recording thread, so an `AsyncLocal<Guid>` set when the capture is created flows into everything the test awaits and into nothing it does not. `OwnMeasurements` / `OwnLongMeasurements` are the measurements this test's own flow produced; `SingleOwnMeasurement()` / `SingleOwnLongMeasurement()` are the exactly-one assertions, with messages that name the instrument and count the foreign measurements they excluded.

`MetricCapture.RecordFromAConcurrentWriter(Action)` emits one matching-shaped measurement from a genuinely foreign context — `ExecutionContext.SuppressFlow()` plus a fresh `Thread`, because without the suppression the thread would inherit the test's own `AsyncLocal` value and the scope could not tell them apart. **That interloper lives in the test, not in the mutation**, so each of the nine sites is deterministic: read unscoped, every one of them sees two.

The four exporter sites gain `&& a.TraceId == <this test's own activity>.TraceId`, matching the shape the other four exporter sites in the repository already use, plus `StartAForeignSpan(displayName)` — a root activity (`parentContext: default`) with the same `DisplayName` on a different trace.

**One honest caveat.** At X10–X13 the trace id is now part of the *selection*, and the assertion it replaced (`Assert.Equal(writeActivity.TraceId, span.TraceId)`) was removed rather than kept, because keeping it would have been an assertion that can no longer fail. The claim those cases are really about — the `ParentSpanId` chain — is untouched and still asserted, and a span that landed on the wrong trace now fails the `Assert.Single` with its full collection printed.

### 5.3 The clearance, made structural (bullet 6)

Three new pairs of files, one per project:

- `KafkaGroupTestHost : IHost, IAsyncDisposable` — decorates the real host. `StopAsync`, `Dispose` and `DisposeAsync` all route through one idempotent clearance. **The helper's declared return type is this wrapper**, so the compiler carries the property to all 51 existing call sites with no call-site churn: `host.Services`, `PlaceOrderAsync(host)` and `StopHostAndWaitForGroupToClearAsync(host, kafka)` all still compile unchanged.
- `KafkaGroupClearance` — the wait loop, plus the two behavioural changes:
  - **it never throws.** Every C# `finally` that throws *replaces* the exception already in flight, so the pre-fix `throw new TimeoutException(...)` destroyed the message of any test that failed and whose teardown then timed out. A failure to stop, a failure to dispose and a failure to clear are now all **recorded**.
  - **the enforcement moved to the SETUP path.** `EnsureGroupIsClearBeforeStartAsync` is a no-op unless a previous teardown recorded a leak for that group; if one did, it waits and throws — naming the recorded leak — only if the group never clears. It is never inside a `finally`, so it cannot mask anything. The last test in a run can still leak without anything noticing, and that is deliberate: at that point there is no next test to harm.

A performance note found by measurement, not by reasoning: the first version of the guard tests took **2 m 34 s per project**, because a bare `StopAsync()` against an unreachable broker spent the whole 150 s default budget, and because the librdkafka admin client was being constructed even when the budget was already zero. The wrapper now takes an optional budget, and `WaitForGroupToClearAsync` returns before constructing a client if the deadline has already passed. The same three tests now run in **60 ms**.

`OffsetContractTests`' two "Mechanism-2 classification" comments were updated rather than left contradicting the code: those hosts now run the clearance anyway, which is the point — the teardown no longer depends on that classification being remembered.

### 5.4 `test.runsettings` delivery (bullet 7)

`tests/Architecture.Tests/TestRunSettingsDeliveryTests.cs`, seven cases over a **literal** list of the 18 test projects. The list is reconciled against the tree **in both directions**, so a nineteenth project fails the guard until the list names it — the expected set is the literal and the rest is derived by subtraction, never a predicate a violating project could escape. The four escape shapes A1 named, plus the delivery mechanism and its payload:

1. the literal list matches `tests/` both ways;
2. root `Directory.Build.props` declares exactly one `RunSettingsFilePath`, ending in `test.runsettings`;
3. root `test.runsettings` carries exactly one `DOTNET_hostBuilder__reloadConfigOnChange`, `false`;
4. no project overrides `RunSettingsFilePath`;
5. no project sets `ImportDirectoryBuildProps` to anything but `true`;
6. no `Directory.Build.*` under `tests/` shadows the root one;
7. no project directory carries its own `*.runsettings`.

Project files are read as **parsed XML**, not as text, so a commented-out property cannot shadow a real one and a real one inside a conditioned `PropertyGroup` cannot hide. `bin`/`obj`/`publish` are excluded by **path segment**.

### 5.5 The host-wrapping census (bullet 6's guard)

`tests/Architecture.Tests/KafkaGroupHostWrappingTests.cs` — a **Roslyn** census over the three integration-test projects. It finds every invocation of `OrdersHost.CreateBuilder` / `NotificationsHost.CreateBuilder` / `ProjectorHost.CreateBuilder` and requires a `new KafkaGroupTestHost(...)` **in the same enclosing method body**, or a literal exemption with a reason. It also fails if the exemption set names a site that no longer exists, and if the census finds nothing at all.

**The instrument's premises, stated because swapping an instrument swaps its premises** (id 68's lesson): it parses with `DEBUG` defined, so the parser and the compiler agree about which region is live; and it looks for the wrapper inside the *enclosing method*, not anywhere in the file, so a wrapper constructed in some other method cannot vouch for this one. Both premises are armed below (C3 and, by construction of the per-method scope, C1).

---

## 6. Arming table — 41 arms (6 P + 13 X + 9 G + 6 C + 7 R), every one red under mutation, restored `cmp`-identical, forced rebuild, confirming green

Protocol on every one: `cp -p` backup → scripted mutation asserting exactly one match → `dotnet build <project> --no-incremental` → the named test → verbatim failure → `cp` restore → `cmp` → `touch` + rebuild → confirming run. Never two builds at once; `pgrep -fl "dotnet (build|test|format)"` was clear before each script started, and each script is strictly sequential.

### Bullets 1–3 — the positional reads (6 arms)

| Arm | Mutation | Named test | Verbatim failure |
|---|---|---|---|
| **P1** | `ConsumeMatchingEventId(...)` → `consumer.Consume(TimeSpan.FromSeconds(20))!` | `BillingOutboxRelayTests.BC16_PublishesTheFactsOfACreditHoldTransactionToTheBillingTopicKeyedByCorrelationId_AndStampsPublishedAtOnlyAfterAcknowledgement` | `the read from 'otc.billing.facts.v1' returned the envelope with eventId 7b42cc18-7405-476b-a6fb-cb48379d1b24, not the fact this test's own relay cycle published (6157fcb8-8edc-4bd1-8fa7-e60eb35fa027). A decoy with eventId 7b42cc18-… was deliberately published to that topic first, under the SAME Kafka key, so a read that selects by POSITION returns the decoy — and the message-key assertion that follows cannot detect it, because the decoy shares the key.` |
| **P2** | same | `FulfillmentOutboxRelayTests.FS16_PublishesTheFactsOfAReserveTransactionToTheFulfillmentTopicKeyedByCorrelationId_AndStampsPublishedAtOnlyAfterAcknowledgement` | `the read from 'otc.fulfillment.facts.v1' returned the envelope with eventId ca407ded-e113-434b-adc5-d6e85cd08085, not the fact this test's own relay cycle published (d9471e6b-6b14-4f46-9976-77e99473b208). …` |
| **P3** | `ConsumeMatchingAsync`'s predicate reverted to the retired positional one — `if (result is not null && !result.IsPartitionEOF)` | `NotificationDeadLetterTests.OR1_R16_…` | `the read from 'otc.orders.facts.v1.dlq' returned the envelope with eventId 1c59f31e-7064-47c1-a6f7-eddaed9aad1d, not this test's own poison fact 82b8cfa9-75df-4249-a3d7-e788c7b80c3b. A decoy with eventId 1c59f31e-… was deliberately published to that topic first, so a read that selects by POSITION ('the first non-EOF message') returns the decoy instead of the record this test produced.` |
| **P4** | same mutation, same run | `NotificationDeadLetterTests.OR4_R57_…` | `the read from 'otc.fulfillment.facts.v1.dlq' returned the envelope with eventId c5ffc35b-62b6-4e29-a1e0-16c5d03fbd81, not this test's own poison fact dbc70f2f-eca9-4f4f-a980-68ae535e9951. …` |
| **P5** | same mutation in the Projector copy | `ProjectorDeadLetterTests.OR1_R16_…` | `the read from 'otc.orders.facts.v1.dlq' returned the envelope with eventId 63a597f9-1428-4206-8d27-c14479fd64e6, not this test's own poison fact 9a3822f8-264d-4d29-bafe-57df0e92ac15. …` |
| **P6** | same mutation, same run | `ProjectorDeadLetterTests.OR4_R57_…` | `the read from 'otc.billing.facts.v1.dlq' returned the envelope with eventId 469a1ee1-7e5c-43ee-9d50-b47ffc107b7e, not this test's own poison fact 7f741af3-54c0-493e-a348-bd80dfb740f4. …` |

Confirming green after restore: Billing 1/1, Fulfillment 1/1, Notifications 2/2, Projector 2/2.

### Bullet 5 — sibling exclusivity (13 arms, one per site)

Every arm mutates exactly ONE site, so the evidence is per-site rather than per-file — two of these sites live in the same test method and two more in another, which a whole-file mutation could not have distinguished.

| Arm | Site | Mutation | Named test | Failure |
|---|---|---|---|---|
| X1 | Gateway `RequestLatencyMiddlewareTests` success | `capture.SingleOwnMeasurement()` → `Assert.Single(capture.Measurements)` | `RecordsOnSuccess_TaggedByTheRequestPath` | `Assert.Single() Failure: The collection contained 2 items` |
| X2 | Gateway, error path | same | `RecordsOnTheErrorPathToo_BeforeRethrowing` | same shape |
| X3 | Orders.UnitTests dispatcher, success | `meterListener.SingleOwnMeasurement()` → `Assert.Single(meterListener.Measurements)` | `OtcFactProcessingLatencyMs_RecordedOnSuccess_TaggedByConsumer` | same shape |
| X4 | Orders.UnitTests dispatcher, DLQ path | same | `OtcFactProcessingLatencyMs_RecordedOnTheExhaustedRetryDlqPathToo` | same shape |
| X5 | `MetricsExposureTests` `otc_outbox_lag_ms`, first cycle | same | `OtcOutboxLagMs_TracksAGenuinelyAgedRealRow_ThenDropsTo0AfterTheRelayDrains` | same shape |
| X6 | the SAME test's second cycle | same, occurrence 2 only | same test | same shape |
| X7 | `otc_dlq_depth`, watermark case | `capture.SingleOwnLongMeasurement()` → `Assert.Single(capture.LongMeasurements)` | `OtcDlqDepth_TheRealKafkaBackedGauge_…` | same shape |
| X8 | `otc_dlq_depth`, missing-topic case | same | `OtcDlqDepth_ANonExistentTopic_Reports0AndNeverThrows` | same shape |
| X9 | `otc_dlq_depth`, multi-topic case | same | `OtcDlqDepth_SumsAcrossMultipleTopicsIndependently_…` | same shape |
| X10 | exporter, `writemodel.transaction`, Kafka-facts case | drop ` && a.TraceId == writeActivity!.TraceId` | `R57_OR4_KafkaFacts_…` | `Assert.Single() Failure: The collection contained 2 matching items … Match indices: 0, 2` |
| X11 | exporter, `outbox.publish`, same case | drop the same clause, occurrence 2 | `R57_OR4_KafkaFacts_…` | `… Match indices: 3, 4` |
| X12 | exporter, `writemodel.transaction`, DB-hop case | drop ` && a.TraceId == rpcSpan!.TraceId` | `R56_OR4_TheWriteDatabaseHop_…` | `Assert.Single() Failure: The collection contained 2 matching items` |
| X13 | exporter, `outbox.publish`, DB-hop case | drop the same clause, occurrence 2 | `R56_OR4_TheWriteDatabaseHop_…` | same shape |

Confirming green after restore: Gateway.UnitTests `RequestLatencyMiddlewareTests` 2/2, Orders.UnitTests `FactRetryDispatcherTests` 9/9, Orders.IntegrationTests `MetricsExposureTests` + `TraceContextPropagationTests` 8/8.

### Bullet 6 — the structural clearance (9 arms, three per project)

| Arm | Mutation | Named test | Verbatim failure (Notifications copy shown; Orders and Projector identical but for the type names) |
|---|---|---|---|
| G1/G4/G7 | the start helper declares a bare `IHost` again | `KafkaGroupTeardownGuardTests.TheHostHelper_DeclaresAGroupClearingWrapperAsItsReturnType_NotABareIHost` | `NotificationConsumptionTestSupport.StartHostAsync declares its host as 'Microsoft.Extensions.Hosting.IHost'. It must declare 'OrderToCash.Notifications.IntegrationTests.KafkaGroupTestHost', because that type is what routes a bare host.StopAsync()/Dispose() through the consumer-group clearance. …` |
| G2/G5/G8 | `KafkaGroupTestHost.StopAsync` becomes `_inner.StopAsync(cancellationToken)` | `…ABareStopAsync_OnAHostTheHelperHandsOut_StillRunsTheGroupClearance` | `a bare host.StopAsync() ran the consumer-group clearance 0 time(s); it must run it exactly once. That is the whole point of the wrapper — the clearance belongs where the host is torn down, not in 51 per-site finally blocks a future teardown can forget.` |
| G3/G6/G9 | the clearance throws a `TimeoutException` from the `finally` block again | `…ATestThatFailsAndWhoseTeardownAlsoFails_StillReportsItsOwnFailure` | `no leak was recorded for the probe group after a zero-budget clearance against an unreachable broker, so the teardown either succeeded (this case would then prove nothing) or THREW instead of recording — and throwing from a finally block is exactly the masking defect this case exists to catch. The exception that escaped was: TimeoutException: Consumer group 'otc-teardown-guard-probe' still reported members 0s after a test's own host was stopped.` |

Confirming green after restore: 3/3 in each of the three projects.

### Bullet 6's census (6 arms — the defeat list run against the guard itself)

| Arm | Attack | Verbatim failure |
|---|---|---|
| C1 | #1, delete the behaviour: unwrap one real site | `1 of 16 test host(s) built from a composition root that JOINS a shared Kafka consumer group are not wrapped in a KafkaGroupTestHost: Orders.IntegrationTests/HealthProbesTests.cs::R60_OR6_ReportsReadyWhile… Wrap it, or add it to this guard's literal exemption set with the reason its host never joins the group.` |
| C2 | #4, shadow the pattern from a **comment** | identical failure — Roslyn sees trivia, not an `ObjectCreationExpression` |
| C3 | #5, hide the wrapper in a **`#if false` region** | identical failure — the parser is given `DEBUG`, and `#if false` is dead in both the parser and the compiler |
| C4 | #6, hide the wrapper in a **raw string literal** | identical failure |
| C5 | #9/#7, a **stale exemption** naming a site that no longer exists | `1 exemption(s) in this guard name a build site that no longer exists: Orders.IntegrationTests/ThisFileWasDeletedLongAgo.cs::SomeMethod. Remove them — a stale exemption is an unreviewed hole.` |
| C6 | #8, point the sweep at a population it cannot find anything in | `the census found NO host built from a group-joining composition root in any of the three integration-test projects. That cannot be right and means the census stopped looking at what it is about — a sweep that finds nothing cannot fail.` |

Confirming green after restore: 1/1.

### Bullet 7 — `test.runsettings` delivery (7 arms, each escape shape applied to one project in turn)

| Arm | Mutation | Named test | Verbatim failure |
|---|---|---|---|
| R1 | `<RunSettingsFilePath>` added to `Seed.UnitTests.csproj` | `NoTestProject_OverridesRunSettingsFilePath` | `1 test project(s) override root Directory.Build.props' RunSettingsFilePath and therefore no longer receive DOTNET_hostBuilder__reloadConfigOnChange=false: Seed.UnitTests (declares RunSettingsFilePath = [$(MSBuildThisFileDirectory)own.runsettings]). HostInotifyReloadGuardTests lives in Orders.UnitTests only and would stay green while this project's hosts each opened an inotify instance.` |
| R2 | `<ImportDirectoryBuildProps>false` added to `Cqrs.UnitTests.csproj` | `NoTestProject_OptsOutOfImportingDirectoryBuildProps` | `1 test project(s) opt out of importing root Directory.Build.props and therefore never receive RunSettingsFilePath at all: Cqrs.UnitTests (ImportDirectoryBuildProps = [false]).` |
| R3 | a **buildable** `tests/Directory.Build.props` that forgets `RunSettingsFilePath` | `NoDirectoryBuildPropsShadowsTheRootOne_UnderTests` | `1 Directory.Build.* file(s) exist under tests/ and shadow the root one MSBuild would otherwise reach: [tests/Directory.Build.props]. …` |
| R4 | `tests/Gateway.UnitTests/own.runsettings` | `NoTestProjectDirectory_CarriesItsOwnRunsettingsFile` | `1 test project(s) carry their own *.runsettings file, which \`dotnet test --settings\` or an IDE will prefer over the root one: Gateway.UnitTests ([tests/Gateway.UnitTests/own.runsettings]). …` |
| R5 | a nineteenth project the literal list does not name | `TheLiteralProjectList_MatchesTheTreeInBothDirections` | `the literal test-project list … no longer matches tests/. Named in the list but absent from the tree: []. Present in the tree but absent from the list: [Escape.UnitTests]. …` |
| R6 | delete `RunSettingsFilePath` from root `Directory.Build.props` | `RootDirectoryBuildProps_PointsRunSettingsFilePathAtRootTestRunsettings` | `root Directory.Build.props must declare exactly ONE RunSettingsFilePath; it declares 0 ([]). Without it no test project receives test.runsettings at all.` |
| R7 | flip `DOTNET_hostBuilder__reloadConfigOnChange` to `true` | `RootTestRunsettings_DisablesHostConfigurationReloadOnChange` | `test.runsettings must carry exactly one DOTNET_hostBuilder__reloadConfigOnChange entry set to "false"; it carries 1 ([true]). …` |

**R3's first attempt is worth recording because it did not arm anything.** An empty `<Project></Project>` shadow removed `TargetFramework` too, so the *build* failed (`NETSDK1013`) and the guard never ran. A mutation that stops the build is not evidence about the guard. The rerun used a shadow that carries the minimum to build and only forgets `RunSettingsFilePath` — which is also the realistic shape of the escape.

Confirming green: 7/7.

---

## 7. Proof by change of KIND, not of probability

Bullet 3 is the only bullet that asks for this explicitly, and the same discipline is applied to bullet 5.

**The determinism is in the test, never in the mutation.** Each of the six read sites now publishes a **decoy** — a valid envelope with a different `eventId` — to the same topic **before** the real record exists, under the **same Kafka key**, so it occupies the same partition at a strictly earlier offset. Same-partition ordering is a Kafka guarantee, not a timing hope: the decoy is therefore first on **every** run, not on most of them. For the `.dlq` sites the key is the `eventType`, because that is what `KafkaDeadLetterPublisher` keys the dead-letter copy by (`…/DeadLetter/KafkaDeadLetterPublisher.cs:58` in each of the three services); for the two relay sites it is the `correlationId`, the key the relay itself uses.

The mutation is therefore only the **selection** — content matching back to "the first non-EOF message" — and it is red 1/1 at each site with a message naming the decoy's own `eventId`. No delay, no sleep and no repetition was needed anywhere.

For bullet 5 the same structure: the interloper measurement and the foreign span are emitted **by the test**, from a genuinely foreign execution context, so the unscoped read sees exactly two on every run. The mutation is only the removal of the scope.

---

## 8. Ported-idiom ledger

`sdd: false`, so the ledger lives here. **Rows are owed**, and finding that out took reading #7's *tests*, not only its source — which is what the *"port its guards"* rule asks for and is where three of these four rows came from.

| # | Property | #7 relied on X — with a file and line | In #8 that property is supplied by Y | Guard |
|---|---|---|---|---|
| **L74-1** | A `.dlq` read returns the record THIS test produced | `apps/notifications/src/notification-dead-letter.integration.spec.ts:253-255` collects every message on the topic and then `dlqMessages.find((m) => m.value.eventId === poisonEnvelope.eventId)`. `apps/projector/src/projector-dead-letter.integration.spec.ts:159-161` is identical. `apps/orders/src/saga-command-dead-letter.integration.spec.ts:98-100` goes further and asserts `filter(...).toHaveLength(1)`. **All three of #7's dead-letter specs select by content; none of them is positional.** | #8's Orders copy kept it (`SagaDeadLetterTests.ConsumeOneAsync(topic, correlationId, timeout)`); **the Notifications and Projector copies dropped it in translation** and read "the first non-EOF message". This entry restores it and deletes the positional overloads. | `NotificationDeadLetterTests.OR1_R16_…`, `ProjectorDeadLetterTests.OR1_R16_…` — armed as P3 and P5 |
| **L74-2** | An outbox-relay read returns the record THIS test's relay published | `apps/billing/src/infrastructure/outbox/outbox-relay.integration.spec.ts:49-73` — `consumeForKey(key, groupId)` with `if (messageKey !== key) { return; }`. `apps/fulfillment/src/infrastructure/outbox/outbox-relay.integration.spec.ts:65-89` is the same helper. **#7 filtered; it never took the first message.** | #8's ported copies took the first message and then asserted the key — an assertion a same-key decoy satisfies. Now selected by the outbox row's own `eventId`, with an explicit identity assertion the key assertion cannot substitute for. | `BillingOutboxRelayTests.BC16_…`, `FulfillmentOutboxRelayTests.FS16_…` — armed as P1 and P2 |
| **L74-3** | An exactly-one metric assertion is not polluted by a sibling test | `apps/orders/src/test-support/metrics-test-provider.ts:34` calls `metrics.setGlobalMeterProvider(provider)` — the harness is **process-global**, and `apps/orders/src/infrastructure/messaging/fact-retry-dispatcher-metrics.spec.ts:59-62` really does assert `histogram.count).toBe(1)` on it. It survives because **Vitest isolates every spec file in its own worker process**, so "process-wide" and "this test" are the same thing there. | xUnit runs every collection in ONE process with class-level parallelism, so process-wide is emphatically not per-test. The property is hand-built: an `AsyncLocal<Guid>` provenance token in `MetricCapture`, read inside the `MeterListener` callback, and `SingleOwnMeasurement()` / `SingleOwnLongMeasurement()`. | the nine metric cases, armed as X1–X9, each against a real concurrent writer |
| **L74-4** | A teardown does not leave a stale member in a shared consumer group | #7 **has no teardown clearance at all.** What it has is the mirror image: `apps/notifications/src/test-support/kafka-test-fixture.ts:93-126` — `waitForConsumerGroupReady`, which waits for the group to reach `Stable` **before** the test runs. The same file exists in all six apps' test-support. It works there because Vitest's per-file process isolation kills any stale member with its worker. | In #8 the members outlive the test: feature 27 added the teardown clearance, and this entry moves it into `KafkaGroupTestHost` so it cannot be forgotten, makes it non-throwing so it cannot mask, and moves the *enforcement* to the setup path — which is, interestingly, exactly where #7 put its own version of the check. | `KafkaGroupTeardownGuardTests` ×3 projects, armed as G1–G9; the census `KafkaGroupHostWrappingTests`, armed as C1–C6 |

**A fifth row is owed for bullet 7, and my first draft of this section got it wrong.** I had written "none owed — #8-only harness work" on the assumption that Vitest has no per-project runner-settings mechanism. Then I ran the search, which is the whole point of the rule:

```
find . -path ./node_modules -prune -o \( -name '*.runsettings' -o -name 'vitest.config*' \) -print   → 10 per-app/package vitest.config.mts, plus 7 vitest.integration.config.mts
grep -rn "import" apps/*/vitest.config.mts packages/*/vitest.config.mts                              → every one imports only `defineConfig` — there is NO shared base
grep -rn "inotify\|reloadConfigOnChange" --include='*.ts' --include='*.json' --include='*.md' .      → no hits outside node_modules
```

| # | Property | #7 relied on X — with a file and line | In #8 that property is supplied by Y | Guard |
|---|---|---|---|---|
| **L74-5** | One runner-level setting genuinely reaches every test project | #7 has **no inheritance at all**: ten `vitest.config.mts` files, each a standalone `defineConfig` with no shared base, so "every project has the setting" is maintained by copy. It guards that by a **hand-maintained LITERAL list** — `apps/seed/src/domain-threshold-guard.spec.ts:41` (`NESTJS_SERVICES`) with a literal exemption set at `:57` (`EXEMPT_VACUOUS_DOMAIN`), written for exactly the reason given in its own header comment: *"a `vitest.config.mts` that declares a `src/domain/**` coverage.thresholds group is only a meaningful gate if `src/domain/` actually contains source matched by that glob"*. And it has no inotify pressure to relieve — the grep above returns nothing. | #8 inherits instead: one root `RunSettingsFilePath` that **any** project can override, opt out of, shadow or pre-empt. The literal-population shape is the same (I reached it independently, and finding #7's version afterwards is a good sign for both); what is genuinely new are the **four inheritance escapes**, which do not exist in a no-inheritance model and therefore have no #7 counterpart to port. | `TestRunSettingsDeliveryTests` — seven cases, armed as R1–R7 |

The lesson, recorded because it cost me a wrong sentence: *"none owed"* felt obviously true and was not. What made it wrong was not the conclusion about #8 but the **premise about #7** — "Vitest has no per-project config" — which one `find` disproves in under a second.

---

## 9. Bullet 4 — what "joins the guard-hardening loop with ids 67–70" obliges

Ids 67, 68, 69 and 70 are all `done` and all `sdd: false`; `ls progress/` shows their records folded into two batch files (`impl_composition_root_delegation_and_design_time_factories.md`, `impl_composition_root_env_reads.md`) and none of them is a shared, loop-level enumeration artefact that this entry could extend. What the loop's first task establishes, and what id 68's acceptance bullet 1 states in so many words, is that **the population is re-derived by the loop's own enumeration before any test is written, as a search result, with `bin` and `obj` excluded by path.** That is the obligation, and it is discharged four times over in §2, §3, §4 and §5.4 — each one a command, its complete output, and one classification line per hit, with every hit I could not classify left visible rather than dropped.

It also obliges the *shape* of the fix: a fix prescribed at a class closes a class. That is why bullet 1's fix reached Billing and Fulfillment (which the entry did not name), why bullet 5's reached the four exporter sites (which A13 did not name), and why bullet 6's is a wrapper plus a census rather than 51 corrected `finally` blocks.

---

## 10. Defeat list — which of `CLAUDE.md`'s ten attacks I ran against my own guards, and why the rest cannot bite

| # | Attack | Against the read guards (P1–P6) | Against the exclusivity guards (X1–X13) | Against the census (C1–C6) and the runsettings guard (R1–R7) |
|---|---|---|---|---|
| 1 | Delete the behaviour | **run** — the selection is deleted back to positional at every site | **run** — the scope is deleted at every site | **run** — C1 unwraps a real site; R1–R7 each delete or override the delivery |
| 2 | Corrupt a payload field the test supplied | **run** — the decoy IS a corrupted-identity record; the assertion is on an `eventId` the test produced | **run** — the interloper's value (`4242`, `424242`) is a real value the unscoped read then reports | **run** — R7 corrupts the runsettings value to `true` |
| 3 | Substitute a valid sibling identifier | **run** — the decoy carries a valid sibling `eventType` and, at the relay sites, the *same* key, which is the strongest form: a same-key substitute that the old key assertion could not detect | n/a — the instruments (`otc_outbox_lag_ms`, `otc_dlq_depth`) carry no identifier the test passes; the scope is provenance, not a name | **run** — R1 points one project at a *sibling* settings file; C1's exemption set is keyed by a real site path, and C5 substitutes a nonexistent one |
| 4 | Shadow the pattern from a comment or string literal | n/a — these guards execute code; a comment cannot make a Kafka read return a different record | n/a — same | **run** — C2 (comment) and C4 (raw string). For R1–R7 the project files are read as parsed XML, so a commented-out `RunSettingsFilePath` is not an element at all |
| 5 | Hide the real thing in a dead region | n/a — executing guards | n/a — same | **run** — C3, with the parser given `DEBUG` so it agrees with the compiler about which region is live |
| 6 | Hide it in a raw or verbatim string | n/a | n/a | **run** — C4 |
| 7 | Drop an OPTIONAL element entirely | n/a — nothing here is optional | n/a | **run** — R6 deletes `RunSettingsFilePath` entirely (absence, not a wrong value); C5 is the same shape applied to an exemption |
| 8 | Compare a literal to a literal | n/a | n/a | **run** — R5 proves the literal project list is reconciled against the **tree**, and C6 proves the census fails rather than passes when it can find nothing |
| 9 | Satisfy the closer half and leave the premise stale | **considered**: the premise half of each read guard is "a decoy really is first on this partition", and it is not stale prose — the decoy publish is in the test, so a run that did not produce it cannot pass the mutated version either | **considered**: the control is the interloper itself, present in the same method | **run** — C5 is exactly this: an exemption whose premise (the site exists) has gone stale |
| 10 | Let a build-output copy join the population | n/a for the executing guards | n/a | **run by construction** — every enumeration here excludes `bin`/`obj`/`publish` **by path segment**, never by post-filtering an output line's content |

---

## 11. Suite counts

- **Baseline before this entry: 2 017 passed, 0 failed, 0 skipped** (the brief's figure, `./quality.sh`).
- **Tests added: 17.** `Architecture.Tests` +8 (7 `TestRunSettingsDeliveryTests` + 1 `KafkaGroupHostWrappingTests`); `Orders.IntegrationTests` +3, `Notifications.IntegrationTests` +3, `Projector.IntegrationTests` +3 (`KafkaGroupTeardownGuardTests`).
- **Tests removed: 0.** Two *methods* were deleted (`NotificationDeadLetterTests.ConsumeOneAsync`, `ProjectorDeadLetterTests.ConsumeOneAsync`), but they were private helpers, not `[Fact]`s.
- **Expected total: 2 017 + 17 = 2 034.**
- **Observed: 2 034 passed, 0 failed, 0 skipped, 18 projects, `quality.sh finished`.** `./quality.sh` clean end to end — format check, build with zero warnings, all 18 suites. **The reconciliation is exact: 2 017 + 17 = 2 034.**

```
grep -E "^(Passed!|Failed!)" quality_74.log | grep -oE "Passed: +[0-9]+" | grep -oE "[0-9]+" | paste -sd+ | bc   → 2034
grep -c '^Failed!' quality_74.log                                                                                 → 0
```

| Project | Passed | Delta |
|---|---|---|
| `Architecture.Tests` | 49 | **+8** (7 `TestRunSettingsDeliveryTests` + 1 `KafkaGroupHostWrappingTests`) |
| `Billing.IntegrationTests` | 90 | 0 |
| `Billing.UnitTests` | 262 | 0 — matches the brief's baseline exactly |
| `Contracts.UnitTests` | 24 | 0 |
| `Cqrs.UnitTests` | 23 | 0 |
| `Fulfillment.IntegrationTests` | 64 | 0 |
| `Fulfillment.UnitTests` | 146 | 0 — matches the brief's baseline exactly |
| `Gateway.IntegrationTests` | 65 | 0 |
| `Gateway.UnitTests` | 237 | 0 — matches the brief's baseline exactly |
| `Notifications.IntegrationTests` | 29 | **+3** (`KafkaGroupTeardownGuardTests`) |
| `Notifications.UnitTests` | 111 | 0 |
| `Orders.IntegrationTests` | 155 | **+3** (`KafkaGroupTeardownGuardTests`) |
| `Orders.UnitTests` | 491 | 0 — matches the brief's baseline exactly |
| `Projector.IntegrationTests` | 68 | **+3** (`KafkaGroupTeardownGuardTests`) |
| `Projector.UnitTests` | 120 | 0 |
| `Seed.IntegrationTests` | 6 | 0 |
| `Seed.UnitTests` | 44 | 0 |
| `SharedKernel.UnitTests` | 50 | 0 |
| **Total** | **2 034** | **+17** |

All four of the brief's named unit baselines (Gateway 237, Billing 262, Orders 491, Fulfillment 146) come back unchanged to the test, which is the check that nothing was silently lost while 13 assertions and two read helpers were rewritten.

`./init.sh` exits **0** — "environment and state are coherent", with only the two expected warnings (uncommitted changes mid-session; `quality.sh` not run from inside `init.sh`).

### A note on the arming evidence and a mid-flight rename

`dotnet format --verify-no-changes` rejected the first pass: `IDE1006` on six new `private static readonly` fields, which this repository names `_camelCase` (`ApplicationInfrastructureLayeringTests.cs:162` is the precedent) and which `dotnet format` cannot auto-fix (`NamingStyleCodeFixProvider doesn't support Fix All in Solution`). Renaming them changed files that armings had already run against, so **31 of the 35 arms were re-run after the rename** — R1–R7, C1–C6, X1–X9, G1–G9 — plus P3–P6, which were re-run because bullet 6's wrapping edit later touched the same two dead-letter files. Every re-run reproduced the same failure and the same confirming green. The four not re-run (P1, P2, X10–X13) are on files that `cmp` proves byte-identical to the copies they were armed against.

A rename of a private field is provably behaviour-preserving, so re-running was not strictly forced — but "provably equivalent" is exactly the reasoning that produces a false green, and the arms are cheap.

---

## 12. What I could not do, and what surprised me

- **`feature_list.json` is untouched**, per the brief's explicit prohibition, so id 74 is still `in_progress`. My standing instruction says to set it `in_review`; the brief overrides it and I flag the difference rather than resolve it silently. The coordinator owns the transition.
- **The suite's own start-of-session `git status` was stale**, exactly as `CLAUDE.md` warns about injected caches: it listed ~40 dirty `src/` files that had since been committed (`07c7877`, `a0a67b8`, `d5c8a3a` all land after the `909394f` the snapshot named). I checked `git log`/`git reflog` rather than assuming work had been lost, and touched nothing.
- **What surprised me, and is the most useful thing here for #9.** The entry's title is about dead-letter tests. The two sites that were genuinely *dangerous* were not dead-letter tests at all: Billing's `BC16` and Fulfillment's `FS16` read the first message on a shared topic and then asserted the message **key** — and the decoy I had to write to arm them carries the same key by construction, so the pre-fix assertion **passes on the wrong record**. A positional read whose only assertion is on a field the decoy shares is strictly worse than one that fails loudly, and it is invisible to every enumeration that starts from the phrase "dead letter".
- Second surprise, measured rather than suspected: the first version of the teardown guards took **2 m 34 s per project** because a bare `StopAsync()` against an unreachable broker spends its whole 150 s budget and because a librdkafka admin client was being built to be used zero times. Both are now fixed and the same three tests run in 60 ms. A guard that is correct and slow is a guard someone will eventually delete.
- No change was required under `src/`, and none was made. Nothing in `specs/shared/` is implicated, so no `SA-n` and no shared-spec backlog entry is owed by this entry.
