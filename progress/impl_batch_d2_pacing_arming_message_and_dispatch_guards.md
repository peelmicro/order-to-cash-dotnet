# impl — batch D2: backlog ids 69, 82 and 89

Implementer record. Three re-opened phase-14 entries, worked in one dispatch: **69** (`gateway_readiness_pacing_is_unguarded`), **82** (`arming_failure_messages_that_name_nothing`), **89** (`saga_dispatch_concurrency_is_exercised_everywhere_and_asserted_nowhere`).

Contract read verbatim from `feature_list.json` before any work, as the brief required. `./init.sh` exits 0 at the start of the session and at the end.

---

## 0. Discrepancies between the brief and the entries' own acceptance bullets

The brief said *"They are now `pending`"* for all three. **Id 69's status in `feature_list.json` is `in_progress`, not `pending`**; ids 82 and 89 are `pending`. The bullets, not the brief, were treated as the contract throughout; this is recorded because the brief asked for any divergence to be reported. `feature_list.json` was **not touched** by this session — the coordinator owns its transitions.

No acceptance bullet was found to contradict the brief on substance. Two bullets the brief flagged as easy to skim were honoured explicitly:

- **Id 69 bullet 5** (the widened committed-offset half) is worked in full — §1.5.
- **Id 89 bullet 5** (the documented-decision exit) was **not** taken. It is available only *"if the conclusion is that a unit-level guard plus universal incidental exercise is sufficient"*, and it is not: the integration guard bullet 2 asks for turned out to be constructible inside an existing collection, at **10 s** of added runtime and **zero** new containers. Building it was cheaper than arguing it away. §3.
- **Id 82 bullet 5** asks for one sentence in `CLAUDE.md`. That file is the coordinator's. The sentence is **drafted in §2.6 and not applied**.

---

## 1. Id 69 — `gateway_readiness_pacing_is_unguarded`

### 1.1 What the entry is about

Bullets 1–4 concern the two Gateway readiness loops that the guard-hardening loop paced and left unproven. The brief's established fact holds: both were **already paced and correct by reading**. What was missing was a guard — the reviewer had deleted both `Task.Delay` calls, forced a rebuild, and `Gateway.IntegrationTests` stayed **48/48 green**.

Bullet 5 widens the entry to the same class in a different mechanism: a retry budget **counted in attempts** against a broker error that returns **without consuming wall-clock**.

### 1.2 What changed (bullets 1–4)

| File | Change |
|---|---|
| `tests/Gateway.IntegrationTests/StandInResponder.cs` | The private `WaitUntilSubscribedAsync` loop is extracted to `internal static WaitUntilReachableAsync(INatsConnection, string subject, byte[] probe, CancellationToken)` — the same extraction `SagaIntegrationTestSupport` and `BillingHostFixture` already carry, and for the same reason: nothing in the assembly could reach the loop while it was private, so nothing could arm it. `ReadinessAttempts` (100) and `ReadinessPacingInterval` (50 ms) are exposed so the guard states its bound in terms of the constants rather than a transcribed number. The exhaustion `TimeoutException` now names the pacing, the attempts and the elapsed wall-clock. |
| `tests/Gateway.IntegrationTests/FulfillmentStockEndToEndTests.cs` | Same: `WaitUntilReachableAsync` is now `internal static` and takes `subject`/`probe`. The one production call site passes the real `StockSubjects.StockCheck` and a real stock-check body — the identifier stays a literal at the call site, so id 82's substitution family is not opened here. Same self-describing exhaustion message. |
| `tests/Gateway.IntegrationTests/GatewayReadinessPacingRaceTests.cs` (**new**, 4 tests) | The guard. `[Collection(NatsCollection.Name)]`, which `NatsRpcClientIntegrationTests`, `StreamHttpTests` and `StreamHeartbeatHttpTests` already share — **no new container**. |

The four tests, in the three-part shape the other six id-63 sites already use:

1. `RequestSentBeforeAnythingSubscribes_ThrowsNoRespondersImmediately_EveryTime` — reproduces the sentinel *and* asserts it returns in well under the request timeout. This is the premise the whole class rests on; if `NatsNoRespondersException` ever started waiting the timeout out, an attempt-counted budget really would be a wall-clock budget and the entry's argument would dissolve. Asserted rather than assumed, 3 runs.
2. `WithoutPacing_AReplicaOfTheSameHundredAttemptLoop_BurnsItsWholeBudgetWellInsideTheSubscribersDelay_EveryTime` — the **control**. An unpaced replica of the identical loop races a subscriber delayed by a controlled 300 ms and loses **every** run, *and* is asserted to have lost in **less** than that 300 ms, so it lost for the pacing reason and not an incidental one (bullet 3).
3. `StandInResponderReadinessLoop_AbsorbsADelayedSubscription_EveryTime` — the real loop wins every run.
4. `FulfillmentStockEndToEndReadinessLoop_AbsorbsADelayedSubscription_EveryTime` — the other real loop wins every run.

Tests 3 and 4 end in one shared assertion whose message names the site, the run, the observed wall-clock, the pacing interval and the paced budget, in **both** failure directions (gave up too early / returned before the subscriber existed).

**Runtime cost: 2 s for all four**, measured, inside an already-running collection.

### 1.3 Arming — bullets 1–4

Protocol per arm: `cp` backup → mutate → `dotnet build --no-incremental` → run the ONE named test → record the verbatim failure → restore from backup → `cmp` → `touch` → forced rebuild → confirming green run.

| # | Mutation | Named test | Verbatim failure |
|---|---|---|---|
| **69-A** | `tests/Gateway.IntegrationTests/StandInResponder.cs` — `await Task.Delay(ReadinessPacingInterval, cancellationToken)` **deleted** | `GatewayReadinessPacingRaceTests.StandInResponderReadinessLoop_AbsorbsADelayedSubscription_EveryTime` | `run 1/3: StandInResponder.WaitUntilReachableAsync (tests/Gateway.IntegrationTests/StandInResponder.cs) gave up after 185 ms against a subscriber delayed by only 300 ms. Its 100-attempt budget is a budget in WALL-CLOCK only while it waits 50 ms after every failed attempt (a paced budget of 5000 ms); without that delay NatsNoRespondersException returns immediately and the whole budget is spent in about a millisecond. The loop is NOT PACING. Underlying failure: TimeoutException: Stand-in responder for 'gateway.readiness-race-repro.ed708aa37196452dbfb129cea13dd686' never became reachable: the readiness loop exhausted all 100 attempts after only 181 ms, against a PACED budget of at least 5000 ms. An elapsed time far below that budget means the loop is NOT PACING between attempts (backlog id 63/id 69): NatsNoRespondersException returns immediately, so an unpaced loop spends its whole attempt budget in about a millisecond.` |
| **69-B** | `tests/Gateway.IntegrationTests/FulfillmentStockEndToEndTests.cs` — `await Task.Delay(ReadinessPacingInterval)` **deleted** | `GatewayReadinessPacingRaceTests.FulfillmentStockEndToEndReadinessLoop_AbsorbsADelayedSubscription_EveryTime` | `run 1/3: FulfillmentStockEndToEndTests.WaitUntilReachableAsync (tests/Gateway.IntegrationTests/FulfillmentStockEndToEndTests.cs) gave up after 144 ms against a subscriber delayed by only 300 ms. Its 100-attempt budget is a budget in WALL-CLOCK only while it waits 50 ms after every failed attempt (a paced budget of 5000 ms); without that delay NatsNoRespondersException returns immediately and the whole budget is spent in about a millisecond. The loop is NOT PACING. Underlying failure: TimeoutException: 'gateway.readiness-race-repro.e7efb237621540528119d319a74b499c' never became reachable: the readiness loop exhausted all 100 attempts after only 142 ms, against a PACED budget of at least 5000 ms. An elapsed time far below that budget means the loop is NOT PACING between attempts (backlog id 63/id 69): NatsNoRespondersException returns immediately, so an unpaced loop spends its whole attempt budget in about a millisecond.` |

Both restores `cmp`-identical, both `touch`ed, both rebuilt `--no-incremental`, confirming run **4/4 green** after each. When 69-B was armed the other three cases were green in the same run, which is the confirming evidence for 69-A's restore.

**Change of KIND, not of probability (bullet 2).** The delay is a controlled 300 ms; the unpaced replica loses 3/3 and loses in 144–185 ms; the paced loops win 3/3 each. Nothing here observes that a flake stopped.

### 1.4 Bullet 5 — the enumeration

Bullet 5 prescribes the enumerating command. It was run as prescribed, path-excluded:

```
find ./tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n '\.Committed('
```

Complete output, one classification line per hit:

| Hit | Classification |
|---|---|
| `tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs:429` | **IN** — `ReadCommittedOffsetsAsync`, the 5-attempt/300 ms/catch-every-`KafkaException` shape. Fixed. |
| `tests/Notifications.IntegrationTests/NotificationDeadLetterTests.cs:100` | **NOT A SITE** — a prose comment (`// interval, so \`consumer.Committed()\` can lag the true stored`), not a call. |
| `tests/Notifications.IntegrationTests/NotificationDeadLetterTests.cs:423` | **IN** — `NotificationOffsetSupport.ReadCommittedOffsetTotalAsync`, same shape. Fixed. |
| `tests/Projector.IntegrationTests/OffsetContractTests.cs:58` | **IN** — the site that went red in the SA-3 verification run. Same shape. Fixed. |
| `tests/Projector.IntegrationTests/ProjectorDeadLetterTests.cs:333` | **IN** — `ProjectorOffsetSupport.ReadCommittedOffsetTotalAsync`, same shape. Fixed. |

Four call sites, exactly the four the entry names. Nothing unclassified.

### 1.5 Bullet 5 — what changed

`KafkaCommittedOffsetRetry` (new) now owns the policy, in three copies — `tests/Orders.IntegrationTests/`, `tests/Notifications.IntegrationTests/`, `tests/Projector.IntegrationTests/` — one per project that has a site, namespace apart. Duplication rather than a new shared test project is this repository's established stance for test support (`NotificationOffsetSupport`/`ProjectorOffsetSupport` each say so in their own doc comments: *"duplicated here rather than cross-project-referenced"*), and **each copy carries its own copy of the deterministic proof**, so no copy is guarded only by a sibling's test. That is deliberate: `CLAUDE.md`'s own lesson is that a class fixed at the sites where it was noticed survives in the files nobody enumerated, under the same test name.

Two changes, both of which the entry asks for:

1. **The budget is wall-clock, not attempts.** A 60 s deadline paced at 300 ms — #7's own `waitForConsumerGroupReady` shape (`apps/projector/src/test-support/kafka-test-fixture.ts:93-126`: a 60 000 ms deadline, a 300 ms interval, a loading coordinator treated as not-ready). The retired shape spent its whole budget in ~1.2 s against an error the broker returns immediately.
2. **Only the three group-coordinator codes are retried** — `ErrorCode.NotCoordinatorForGroup`, `ErrorCode.GroupCoordinatorNotAvailable`, `ErrorCode.GroupLoadInProgress` (verified present in `Confluent.Kafka` 2.15.0). Everything else propagates on the first attempt, untouched, so a genuine failure is no longer retried four times and then reported as the last attempt's exception. On exhaustion a `TimeoutException` names the read, the budget, the attempts and the last broker error, with the `KafkaException` as its inner exception.

Twelve new tests (4 × 3 copies), all pure — **no container, no collection**, 10 s per copy.

### 1.6 Arming — bullet 5

The proof is a change of KIND against a coordinator **withheld for a controlled interval** (2 s) longer than the retired 1.2 s budget. One test replicates the retired shape in-test as the control and loses 3/3; the real policy wins 3/3. A third test proves a non-coordinator error is not retried; a fourth proves the exhaustion message names the budget, the attempts and the broker error.

| # | Mutation | Copies | Named test | Verbatim failure |
|---|---|---|---|---|
| **69-C** | `KafkaCommittedOffsetRetry.Read` — the wall-clock check `if (startedAt.Elapsed + effectiveInterval >= effectiveBudget)` reverted to the attempt-counted `if (attempts >= 5)` | **all three** | `KafkaCommittedOffsetRetryTests.TheWallClockDeadline_AbsorbsTheSameWithheldCoordinator_EveryTime` | Orders: `run 1/3: KafkaCommittedOffsetRetry.Read gave up on a coordinator withheld for only 2.0s, after 1.2s and 5 attempt(s), against its own 60s WALL-CLOCK budget. A budget counted in attempts expires in about a second against an error the broker returns immediately; this one must not. Failure: System.TimeoutException: the committed offset for group 'orders.saga': the group coordinator was still not ready after 1.2s and 5 attempt(s) paced 300 ms apart, against a wall-clock budget of 60.0s. Last broker error: NotCoordinatorForGroup — Broker: Not coordinator.` — Notifications and Projector identical but for `group 'notifications'`/`group 'projector'` |
| **69-D** | the retry predicate widened back to every `KafkaException` (`catch (KafkaException ex) when (IsCoordinatorNotReady(ex))` → `catch (KafkaException ex)`) | **all three** | `KafkaCommittedOffsetRetryTests.AGenuineFailure_IsNotRetried_AndSurfacesOnTheFirstAttempt` | `Assert.Throws() Failure: Exception type was not an exact match / Expected: typeof(Confluent.Kafka.KafkaException) / Actual: typeof(System.TimeoutException) / ---- System.TimeoutException : the committed offset for group 'orders.saga': the group coordinator was still not ready after 59.7s and 200 attempt(s) paced 300 ms apart, against a wall-clock budget of 60.0s. Last broker error: UnknownTopicOrPart — Broker: Unknown topic or partition. / -------- Confluent.Kafka.KafkaException : Broker: Unknown topic or partition` |

Six armings (2 mutations × 3 copies), each with its own forced `--no-incremental` rebuild, each restored from a `cp` backup, each `cmp`-identical, each confirmed **4/4 green** after restore.

69-D is worth reading twice: under the widened predicate an unknown-topic error — a genuine, permanent failure — was retried for **59.7 s across 200 attempts** before surfacing, wearing the disguise of a transient. That is the second half of the entry's bullet 5, measured.

### 1.7 What could not be done for bullet 5

The withholding coordinator is a **delegate**, not a real broker: reproducing a Kafka group coordinator that stays unloaded for a controlled interval is not something a Testcontainers broker can be asked for. The delegate throws the exact `ErrorCode.NotCoordinatorForGroup` / `"Broker: Not coordinator"` the SA-3 verification run observed, and the wall-clock is real (`Stopwatch`, real `Thread.Sleep`), so the *policy* is exercised for real and only the *broker* is substituted. That substitution is stated in the test class's own doc comment rather than hidden, as the bullet's *"or state why that proof cannot be constructed here and what evidence replaces it"* allows.

---

## 2. Id 82 — `arming_failure_messages_that_name_nothing`

### 2.1 Bullet 1 — the enumeration, and how the population was derived

Bullet 1 defines the class as *"every assertion used as the failing assertion of a **recorded arm** whose message cannot name the claim"*. That is a conjunction, and the population must be derived from the half that is enumerable without begging the question.

The brief's 16-hit `Assert.False(string.IsNullOrEmpty(...))` sweep is explicitly **not** the population, and a sweep over the *code* is the wrong instrument for a second reason: `Assert.True(`/`Assert.False(` occurs **562** times under `tests/`, and the overwhelming majority are not the failing assertion of any recorded arm. Selecting the code hits that "look nameless" would be selecting by the property under test.

So the population is derived from the **records** — the artefacts that define what a "recorded arm" is — and every member is then classified.

**Command A — the population of recorded arming messages, ungated by the property under test:**

```
find ./progress -name '*.md' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -o 'Assert\.[A-Za-z]*() Failure' | sed 's/^.*:Assert/Assert/' | sort | uniq -c | sort -rn
```

Complete output — 545 quoted failure messages, classified by whether the assertion **can** name the claim:

| count | assertion | classification |
|---:|---|---|
| 278 | `Assert.Equal() Failure` | **OUT** — prints expected and actual |
| 53 | `Assert.Single() Failure` | **OUT** — prints the collection/count |
| 42 | `Assert.Throws() Failure` | **OUT** — prints expected and actual type |
| 30 | `Assert.NotNull() Failure` | **OUT, with a stated reason** — the offending value *is* null; there is no value to print, and bullet 1's class is *"`Assert.NotNull` on a bare bool-like check"*, which none of these are |
| 20 | `Assert.Contains() Failure` | **OUT** — prints the item and the collection |
| 19 | `Assert.Empty() Failure` | **OUT** — prints the non-empty collection |
| **17** | **`Assert.False() Failure`** | **IN** — prints only `Expected: False / Actual: True` |
| **15** | **`Assert.True() Failure`** | **IN** — prints only `Expected: True / Actual: False`, except where a user message was supplied |
| 15 | `Assert.DoesNotContain() Failure` | **OUT** — prints the item |
| 14 | `Assert.Null() Failure` | **OUT** — prints the type of the non-null value |
| 11 | `Assert.NotEqual() Failure` | **OUT** — prints the equal values |
| 8 | `Assert.NotEmpty() Failure` | **OUT, with a stated reason** — the offending value is an empty collection; there is nothing to print |
| 8 | `Assert.IsType() Failure` | **OUT** — prints both types |
| 3 | `Assert.Matches() Failure` | **OUT** — prints the regex and the value |
| 3 | `Assert.All() Failure` | **OUT for the wrapper** — but the inner assertion is classified on its own merits; two of these wrap an `Assert.False`, which is IN |
| 2 | `Assert.ThrowsAny() Failure` | **OUT** |
| 2 | `Assert.Same() Failure` | **OUT** |
| 2 | `Assert.NotSame() Failure` | **OUT** |
| 2 | `Assert.InRange() Failure` | **OUT** |
| 1 | `Assert.Collection() Failure` | **OUT** |

**Command B — the IN subset, located by record:**

```
find ./progress -name '*.md' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -c 'Assert\.\(True\|False\)() Failure' | grep -v ':0' | sort
```

Complete output — 32 occurrences across 29 lines in 18 files:

```
./progress/history.md:1
./progress/impl_gateway_rest_auth.md:1
./progress/impl_gateway_sse_push.md:1
./progress/impl_notification_send_degrades_on_permanent_failure.md:1
./progress/impl_observability_reliability.md:9
./progress/impl_operator_cancel_races_saga_forward_progress.md:1
./progress/impl_order_saga_orchestrator.md:3
./progress/impl_outbox_and_idempotency.md:1
./progress/impl_outbox_relay_deadlock_victim_escapes_run_once.md:1
./progress/review_fulfillment_despatch.md:1
./progress/review_notification_send_degrades_on_permanent_failure.md:2
./progress/review_observability_reliability.md:3
./progress/review_operator_cancel_races_saga_forward_progress.md:1
./progress/review_operator_note_reaches_the_timeline.md:1
./progress/review_order_saga_orchestrator.md:1
./progress/review_orders_catalog_responder.md:1
./progress/review_orders_stock_check_rpc_error_discriminator.md:1
./progress/review_seed_job.md:1
```

### 2.2 The population, one classification line per hit

Each of the 32 occurrences resolved to the assertion in `tests/` it names. The distinct assertion sites, with the arming row that depends on each:

| # | Assertion site (post-fix line) | Arming row(s) that depend on it | Disposition |
|---|---|---|---|
| 1 | `tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:438` `Assert.True(accepted)` | `review_operator_cancel_races_saga_forward_progress.md` M4 | **Replaced** (message) + re-armed |
| 2 | `tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:410` `Assert.False(accepted)` | `impl_operator_cancel_races_saga_forward_progress.md` A3 | **Replaced** + re-armed |
| 3 | `tests/Seed.IntegrationTests/SeedIntegrationTests.cs:193` `Assert.True(completed.HeaderComplete)` | `review_seed_job.md` row C | **Replaced** + re-armed. **The row's line number (`:179`) was stale and pointed at the wrong assertion** — see §2.5. |
| 4 | `tests/Fulfillment.IntegrationTests/StockCheckTests.cs:31` `Assert.True(payload.Available)` | `review_orders_stock_check_rpc_error_discriminator.md` P10 | **Replaced** + re-armed |
| 5 | `tests/Projector.UnitTests/SummariesTests.cs:153` `Assert.False(result.Detail.ContainsKey("note"), …)` | `review_operator_note_reaches_the_timeline.md` row D | **Replaced** + re-armed |
| 6 | `tests/Orders.IntegrationTests/CatalogReferenceListAcceptanceTests.cs:220` `Assert.False(disabledRow.Enabled)` | `review_orders_catalog_responder.md` Q4 | **Replaced** + re-armed |
| 7 | `tests/Gateway.UnitTests/PlaceOrderCommandHandlerTests.cs:51-52` `Assert.True(window.IsRecentlyIssued(replyOrderId))` / `Assert.False(...)` | `impl_gateway_rest_auth.md` row 6 | **Replaced** + re-armed |
| 8 | `tests/Fulfillment.UnitTests/DespatchCreationServiceTests.cs:130` `Assert.False(reply.Created)` | `review_fulfillment_despatch.md` P4 | **Replaced** + re-armed |
| 9 | `tests/Orders.IntegrationTests/SagaCommandRetryTests.cs:138` `Assert.True(sentCount > 0)` (and `:125` `Assert.True(row.Status is …)`) | `review_order_saga_orchestrator.md` sweeper row; `impl_order_saga_orchestrator.md` H8 row 3 | **Replaced** + re-armed |
| 10 | `tests/Orders.UnitTests/KafkaFactPublisherConfigTests.cs:17` `Assert.True(config.EnableIdempotence)` | `impl_outbox_and_idempotency.md` row 9 | **Replaced** + re-armed |
| 11 | `tests/Notifications.IntegrationTests/NotificationDegradesOnPermanentFailureTests.cs` `Assert.False(string.IsNullOrEmpty(consoleLine.Value.TraceId))` and its `degraded` sibling | `impl_notification_send_degrades_on_permanent_failure.md` row 3; `review_notification_send_degrades_on_permanent_failure.md` M7 and advisory A6 | **DELETED** — this is the id 73 sighting bullet 2 names, and bullet 2 prescribes the remedy: the `Assert.Matches("^[0-9a-f]{32}$", …)` one line later already subsumes it for both null and empty. Re-armed. |
| 12 | `tests/Billing.IntegrationTests/LogCorrelationTests.cs:65` `Assert.False(string.IsNullOrEmpty(ScopeValue(record, "TraceId")))` | `review_observability_reliability.md` row 3 | **Replaced** + re-armed |
| 13 | `tests/Projector.IntegrationTests/ProjectorDeadLetterTests.cs:80` `Assert.True(DateTimeOffset.TryParse(headers["x-first-failed-at"], out _))` | `review_observability_reliability.md` row 9; `impl_observability_reliability.md` P7 row | **Replaced** + re-armed |
| 14 | `tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs:154` `Assert.False(string.IsNullOrEmpty(traceparent))` | `review_observability_reliability.md` C1; `impl_observability_reliability.md` L21 | **DELETED** — the `Assert.Equal(callActivity.Id, traceparent)` on the next line subsumes it and prints both values (the review's own row already said so). Re-armed. |
| 15 | `tests/Gateway.UnitTests/ReplayBufferTests.cs:59, 73` `Assert.False(…Resumed)` | `impl_gateway_sse_push.md` row 3 | **Replaced** + re-armed |
| 16 | `tests/Orders.UnitTests/DeadlockRetryExecutionStrategyTests.cs:36, 51, 59, 68, 74` | `impl_outbox_relay_deadlock_victim_escapes_run_once.md` (both mutation directions) | **Replaced** + re-armed, both directions |
| 17 | `tests/Orders.IntegrationTests/LogCorrelationTests.cs:202` `Assert.True(probe.RootElement.TryGetProperty("Scopes", …))` and `:208, :213` `Assert.False(hasTraceId / hasSpanId)` | `impl_observability_reliability.md` `IncludeScopes = false` arm | **Replaced** + re-armed |
| 18 | `tests/Orders.IntegrationTests/LogCorrelationTests.cs:98` `Assert.All(traceIds, t => Assert.False(string.IsNullOrEmpty(t)))` | `impl_observability_reliability.md` `ActivityTrackingOptions.None` arm | **Replaced** + re-armed |
| 19 | `tests/Orders.IntegrationTests/SagaConsumptionTests.cs:183, 195` `Assert.True(gate.Attempts >= 1 / >= 2, "…")` | `impl_order_saga_orchestrator.md` F6 rows 1 and 2 | **NOT in the class** — these assertions **already** carry a claim-naming message; the record's quoted blocks are stale text, not nameless assertions. Re-running the arm to refresh them produced a **much worse finding** — see §5, Disclosure D1. |
| 20 | `progress/history.md:2417` | — | **NOT an arm** — prose quoting advisory A6's own example. Left as history. |

**Sites changed for consistency that are NOT in the population** (no recorded arm names them; stated here so they are visible rather than silently folded in): `tests/Fulfillment.IntegrationTests/LogCorrelationTests.cs:65`, `tests/Notifications.IntegrationTests/LogCorrelationTests.cs:105`, `tests/Projector.IntegrationTests/LogCorrelationTests.cs:83`, `tests/Orders.UnitTests/NatsSagaCommandsAdapterTests.cs:260`, `tests/Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs:140`, `tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs:227`, `tests/Orders.IntegrationTests/OutboxEnvelopeTests.cs:122`, `tests/Orders.IntegrationTests/SagaCommandDeadLetterTests.cs:92`, `tests/Projector.IntegrationTests/TimelineProjectionTests.cs:39`, `tests/Gateway.IntegrationTests/AuthAndRateLimitHttpTests.cs:29`, `tests/Gateway.IntegrationTests/ProblemJsonCorrelationTests.cs:62`, `tests/Seed.IntegrationTests/SeedIntegrationTests.cs:184`, `tests/Gateway.UnitTests/ReplayBufferTests.cs:43`, `tests/Fulfillment.IntegrationTests/StockCheckTests.cs:34`, `tests/Orders.IntegrationTests/SagaCommandRetryTests.cs:125`. Together with the population these close all 16 hits of the brief's `IsNullOrEmpty` sweep, and the closure reconciles exactly: **3** of the 16 were **deleted** (both Notifications degrade sites and the Gateway `traceparent` site, each subsumed by the value-printing assertion on the next line); the other **13** now carry a claim-naming message. Re-running the sweep afterwards returns 9 single-line hits, which is not 13 — the difference is fully accounted for and is a property of the *sweep*, not of the code: 6 of the 13 were re-formatted across three lines and no longer match a single-line pattern, and 2 of the 9 are new **comments** describing the deletions. The content-based command `… | xargs -0 grep -n 'string\.IsNullOrEmpty'` returns all 13, and every one of them carries a message. A count that does not reconcile is a finding; this one reconciles.

**Sites deliberately left alone**: `tests/Gateway.UnitTests/StreamHubTests.cs:48, 71` and `tests/Gateway.UnitTests/IssuedOrderWindowTests.cs:30, 56, 77, 78` — bare booleans with no recorded arm and no sibling in a file this batch touched. Listed rather than omitted.

### 2.3 Arming — bullet 4 and bullet 3

Bullet 4 asks for **one** replaced assertion re-introduced beside its replacement, both messages legible. The id 73 sighting is used, because it is the one the entry was filed from.

**Same mutation for both halves**: `src/Notifications/NotificationsHost.cs` — `ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId` → `ActivityTrackingOptions.None`. Same test: `NotificationDegradesOnPermanentFailureTests.APermanentSmtpFailure_DegradesToConsole_KeepsTheLedgerRow_AndNeverDeadLetters_WithCorrelationIdAndTraceIdOnTheLogLine`. Forced `--no-incremental` rebuild for each.

| | failing assertion | verbatim message |
|---|---|---|
| **OLD** (re-introduced for this arm) | `Assert.False(string.IsNullOrEmpty(consoleLine.Value.TraceId));` | `Assert.False() Failure`<br>`Expected: False`<br>`Actual:   True` |
| **NEW** (what stands in the tree) | `Assert.Matches("^[0-9a-f]{32}$", consoleLine.Value.TraceId!);` | `Assert.Matches() Failure: Pattern not found in value`<br>`Regex: "^[0-9a-f]{32}$"`<br>`Value: null` |

The old message names nothing — not the field, not the line, not the service. The new one names the shape that was expected and the value that was found. Both files restored from `cp` backups, both `cmp`-identical, forced rebuild, confirming run **1/1 green**.

Bullet 3 then requires the affected rows to be **re-run** and their verbatim messages updated. Eighteen arms were re-run, every one on its own forced `--no-incremental` rebuild, every one restored from a `cp` backup and `cmp`-verified:

| # | Mutation | Named test | New verbatim failure |
|---|---|---|---|
| 82-1 | `EfCoreSagaCommandStore.cs` `StockReleaseToken = "stock.release"` → `"credit.release"` (sibling substitution) | `SagaCommandStoreTests.HasAcceptedOperatorCancelAsync_TheSyntheticOperatorEnvelopeOnStockRelease_ReturnsTrue` | `HasAcceptedOperatorCancelAsync returned false for an order whose stock.release row carries the SYNTHETIC orders.cancel.requested envelope CancelOrderCommandHandler enqueues. That row IS an accepted operator cancel and must count.` |
| 82-2 | `HasAcceptedOperatorCancelAsync` reduced to a plain `AnyAsync` over the command token, no envelope-content filter | `…_ARealFactEnvelopeOnStockRelease_ReturnsFalse` | `HasAcceptedOperatorCancelAsync returned true for an order whose only stock.release row carries a REAL credit.rejected.v1 fact envelope — R27's own compensation path, never the operator's cancel. It is matching on the command token alone instead of on the synthetic orders.cancel.requested envelope's content.` |
| 82-3 | `MongoSeedWriter.cs` `HeaderComplete = true` → `false` | `SeedIntegrationTests.Order_Timeline_Documents_Carry_Every_Field_With_The_Right_Types` | `the seeded timeline document for the completed order ORD-000001 has headerComplete=false. Every header field above was present, so the writer is not stamping the flag the read model uses to tell a complete document from a placeholder.` |
| 82-4 | the review's own P10: `R31`'s request replaced with `new StockCheckRequestPayload("", [])` | `StockCheckTests.R31_AnswersPerLineWithoutMutatingAStockItemAndWithoutEmittingAFact` | `the stock.check reply reports available=false for a request the fixture seeded with enough stock. Reply: {"Available":false,"Lines":null}.` |
| 82-5 | `Summaries.cs` — `detail["note"]` written unconditionally | `SummariesTests.PR16_OrderCancelled` | `Summaries.OrderCancelled wrote a 'note' detail key (value: ) for a fact that carries no note. SA-2 requires the key to be absent, not present-and-null.` |
| 82-6 | `OrdersCreateResponder.cs` — `p.Enabled` → `true` on the wire | `CatalogReferenceListAcceptanceTests.CatalogReferenceList_IncludeDisabledTrueVersusOmitted_TheDisabledProductAppearsOnlyWhenRequested` | `the catalog reply reports PROD-DISABLED as enabled=true. The responder is hard-coding the wire flag instead of carrying the row's own enabled column.` |
| 82-7 | `PlaceOrderCommand.cs` — `issuedOrders.Record(reply.OrderId)` → `Record(Guid.NewGuid())` | `PlaceOrderCommandHandlerTests.HandleAsync_RecordsTheReplysOrderId_InTheIssuedOrderWindow` | `the issued-order window does not hold the REPLY's own orderId c848a50c-513e-4fde-97e5-cc4dd551fffc. The handler recorded some other id, so GET /orders/c848a50c-513e-4fde-97e5-cc4dd551fffc would answer a false 404 right after placement.` |
| 82-8 | `DespatchCreationService.cs` — the in-flight-race branch replies `created: true` | `DespatchCreationServiceTests.F8_InFlightRace_…` | `the in-flight race branch replied created=true for despatch DES-000002, which a concurrent committer had ALREADY created. Only the winner may report created=true.` |
| 82-9 | `SagaCommandSweeper.cs` — `DispatchClaimedAsync(row, …)` reverted to the claim-then-issue `DispatchAsync(row.OrderId, row.Command, …)` | `SagaCommandRetryTests.SO3_APendingRowCommittedWithNoInProcessSignal_…` | `the stock.release row never reached 'sent' within 20s after a responder appeared. It was committed PENDING with no in-process signal, so only SagaCommandSweeper can issue it — SO3's crash-window recovery path is not working.` |
| 82-10 | `KafkaFactPublisher.cs` — `EnableIdempotence = true` → `false` | `KafkaFactPublisherConfigTests.OI7_Producer_…` | `the fact producer is built with EnableIdempotence = False. Without it an internal librdkafka retry can reorder or duplicate a partition's records (OI7).` |
| 82-11 | `NotificationsHost.cs` — `ActivityTrackingOptions.None` | `NotificationDegradesOnPermanentFailureTests.APermanentSmtpFailure_…` | `Assert.Matches() Failure: Pattern not found in value / Regex: "^[0-9a-f]{32}$" / Value: null` |
| 82-12 | `BillingHost.cs` — `ActivityTrackingOptions.None` | `Billing.IntegrationTests/LogCorrelationTests.R58_OR7_ARealRpcFailureLogLineCarriesTheRequestsCorrelationIdAndATraceId` | `a log record carrying correlationId c59503ae-370b-495b-8828-e1dfbef17a2b has no TraceId scope entry (observed value: '').` |
| 82-13 | Projector `KafkaDeadLetterPublisher.cs` — `x-first-failed-at` → the literal `"corrupted-by-review-probe"` | `ProjectorDeadLetterTests.OR1_R16_…` | `the .dlq message's x-first-failed-at header is 'corrupted-by-review-probe', which is not a parseable timestamp.` |
| 82-14 | `NatsRpcClient.cs` — `TraceContext.InjectNats(headers);` deleted | `NatsRpcClientIntegrationTests.D5_Row34_InjectsTheActiveTraceIdIntoTheOutboundRequestHeaders` | `Assert.Equal() Failure: Strings differ / ↓ (pos 0) / Expected: "00-fb67759278495ed08c412463023d7caf-9e8cd"··· / Actual: ""` |
| 82-15 | `ReplayBuffer.cs` — `ReplayAfter` always returns `Resumed: true` | `ReplayBufferTests` (both named cases) | `an unknown cursor that was never issued was reported resumed=true with 0 missed item(s). It must be treated exactly like one that aged out.` and `cursor 'c1' has been evicted from a 2-slot buffer, yet ReplayAfter reported resumed=true with 0 missed item(s). A cursor the buffer no longer holds must resolve resumed:false, never a partial replay presented as a resumption.` |
| 82-16a | `DeadlockRetryExecutionStrategy.ShouldRetryOn` — `&& false` (never retries) | `DeadlockRetryExecutionStrategyTests` (the two "should retry" cases) | `ShouldRetryOn refused a RAW SqlException with error number 1205 (the deadlock victim). The outbox relay would surface it instead of retrying.` and `ShouldRetryOn refused a deadlock-victim SqlException (error 1205) WRAPPED in InvalidOperationException — the exact shape ExecuteUpdateAsync's own internal strategy produces, one level deeper than CallOnWrappedException unwraps.` |
| 82-16b | `ShouldRetryOn` — `|| true` (retries everything) | the three "should not retry" cases | `ShouldRetryOn retried a RAW SqlException with error number 1222 (lock request timeout), which is NOT a deadlock. Retrying it would hide a genuine failure.`, `ShouldRetryOn retried a WRAPPED SqlException with error number 1222 (lock request timeout), which is NOT a deadlock.`, `ShouldRetryOn retried a plain TimeoutException carrying no SqlException at all — the predicate is not inspecting the error number.` |
| 82-17 | `OrdersHost.cs` — `o.IncludeScopes = true` → `false` | `Orders.IntegrationTests/LogCorrelationTests.R58_OR7_OmitsTheTraceFieldsEntirelyRatherThanRenderingThemEmpty_WhenNoSpanIsActive` | `the no-span probe log line has no Scopes array at all, so the claim that it omits TraceId/SpanId cannot be tested. Line: {"EventId":0,"LogLevel":"Information","Category":"OrderToCash.Orders.IntegrationTests.NoSpanProbe","Message":"no-span-probe R58_OR7_no_span_marker","State":{"Marker":"R58_OR7_no_span_marker","{OriginalFormat}":"no-span-probe {Marker}"}}.` |
| 82-18 | `OrdersHost.cs` — `ActivityTrackingOptions.None` | `Orders.IntegrationTests/LogCorrelationTests.R58_OR7_EveryRecordProducedWhileHandlingAFactCarriesTheSameCorrelationIdAndTheSameTraceId` | `Assert.All() Failure: 3 out of 3 items in the collection did not pass. / [0]: Item: null / Error: one of the 3 log records sharing this correlationId carries an empty TraceId scope entry; the values observed were [, , ]. (backlog id 82 — the bare Assert.False named nothing)` (×3) |
| 82-19 | `KafkaFactStreamSubscriber.cs` (Orders) — `EnableAutoOffsetStore = false` → `true` | `SagaConsumptionTests.SO9_AHandlerThatThrows_…` | **SURVIVED — Passed 1/1.** See §5, Disclosure D1. |

### 2.4 Bullet 3 — the records themselves

Every affected arming row in `progress/` was edited in place: the nameless message is **retained** (it is history, and the point of the entry is that the difference be legible), and an inline `**[message updated by backlog id 82, re-run 2026-09-13]**` annotation carries the new verbatim message beside it. Records touched: `review_operator_cancel_races_saga_forward_progress.md`, `impl_operator_cancel_races_saga_forward_progress.md`, `review_seed_job.md`, `review_orders_stock_check_rpc_error_discriminator.md`, `review_operator_note_reaches_the_timeline.md`, `review_orders_catalog_responder.md`, `impl_gateway_rest_auth.md`, `review_fulfillment_despatch.md`, `review_order_saga_orchestrator.md`, `impl_outbox_and_idempotency.md`, `impl_notification_send_degrades_on_permanent_failure.md`, `review_notification_send_degrades_on_permanent_failure.md`, `review_observability_reliability.md`, `impl_observability_reliability.md`, `impl_gateway_sse_push.md`, `impl_outbox_relay_deadlock_victim_escapes_run_once.md`, `impl_order_saga_orchestrator.md`.

### 2.5 A stale line number, caught by re-running rather than by reading

`review_seed_job.md`'s row C said the arm failed at `SeedIntegrationTests.cs:179`. Reading the current file at that offset lands three lines above the real assertion, and the nearest `Assert.True` there is `completed.Totals!.TotalAmount > 0`. That is **not** what `HeaderComplete = false` kills. Re-running the arm before believing the number put the failure at `:193`, `Assert.True(completed.HeaderComplete)` — a different assertion, which was then the one fixed and re-armed. The brief's warning that line numbers in these entries may be stale was justified; the fix for it is to re-locate by content and, where a row claims an arm, to *run* it.

### 2.6 Bullet 5 — the `CLAUDE.md` sentence, DRAFTED AND NOT APPLIED

`CLAUDE.md` was **not** edited. Proposed addition to the arming-protocol bullet in *Testing conventions*, for the coordinator to apply or reject:

> **A failure counts as evidence only if its message names the claim — so an assertion whose message cannot name the claim is not an acceptable failing assertion for an arm.** `Assert.False(string.IsNullOrEmpty(x))` prints `Expected: False / Actual: True` and nothing else, so the arm is evidence only to a reader who opens the stack line; `Assert.True(x != null)`, `Assert.True(collection.Any())` and a bare `Assert.True(flag)` are the same shape. Use a form that prints the offending value (`Assert.Equal`, `Assert.Matches`, `Assert.Contains` with the actual collection), or supply the user-message overload. Found as advisory A6 of feature 73's review, where the reviewer's own M7 probe and the implementer's arm 3 both ended in `Assert.False() Failure / Expected: False / Actual: True` and were distinguishable only by their stack line — and closed as backlog id 82, which replaced that exact assertion with `Assert.Matches("^[0-9a-f]{32}$", …)`, whose failure reads `Pattern not found in value / Regex: "^[0-9a-f]{32}$" / Value: null`.

---

## 3. Id 89 — `saga_dispatch_concurrency_is_exercised_everywhere_and_asserted_nowhere`

### 3.1 Bullet 1 — the enumeration

Bullet 1 asks for *"every `Orders.IntegrationTests` fixture that **boots a host containing** `SagaCommandDispatchWorker`"*. That is a claim about what a fixture **constructs**, not about which files mention the worker — the brief is explicit that the single filename-level hit is a starting point and not the population. The chain was derived from the source:

```
find ./src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n 'SagaCommandDispatchWorker'
  → src/Orders/Infrastructure/OrdersSagaServiceCollectionExtensions.cs:98:  services.AddHostedService<SagaCommandDispatchWorker>();   (the ONLY registration)
find ./src ./tests -name '*.cs' … | xargs -0 grep -n 'AddOrdersSaga'
  → src/Orders/OrdersHost.cs:93 is the ONLY caller; the two hits under tests/ are a doc comment and a comment
```

So the population is: every site in `tests/Orders.IntegrationTests` that reaches `OrdersHost.CreateBuilder`, directly or through `SagaIntegrationTestSupport.StartHostAsync`.

```
find ./tests/Orders.IntegrationTests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 grep -n 'OrdersHost\.CreateBuilder\|Host\.CreateApplicationBuilder\|StartHostAsync'
```

Complete output classified (comment-only lines — `SagaConsumptionTests.cs:67`, `OrdersCancelResponderReadinessRaceTests.cs:73/94/115`, `SagaIntegrationTestSupport.cs:27/86` — are prose, not sites):

**A. Boots a host CONTAINING the worker**

| Site | Overrides `Dispatch`? |
|---|---|
| `SagaIntegrationTestSupport.cs:41` — the shared harness, reached from **36** call sites in 13 files (`OrdersCancelAcceptanceTests` ×9, `OperatorCancelRacesSagaForwardProgressTests` ×6, `SagaCommandRetryTests` ×3, `LogCorrelationTests` ×3, `SagaDeadLetterTests` ×2, `RealInfraMetricsProvenanceTests` ×2, `SagaPreconditionTests` ×2, `SagaHappyPathTests`, `SagaCompensationStockRejectedTests`, `SagaCompensationCreditRejectedTests`, `SagaCommandDeadLetterTests`, and this batch's own new test) | **No** |
| `SagaConsumptionTests.cs:86` — direct `OrdersHost.CreateBuilder` | **No** |
| `SagaConsumptionTests.cs:122` — direct | **No** |
| `HealthProbesTests.cs:135` — direct | **No** |
| `SagaCommandRetryTests.cs:225` — direct | **No** |

**B. Boots a host that does NOT contain the worker** — a bare `Host.CreateApplicationBuilder()` with `AddOrdersOutbox` + `AddOrdersAcceptance` + `AddDispatcher` and **no** `AddOrdersSaga`: `OrdersCreateAcceptanceTests.cs:683`, `SagaConsumptionTests.cs:44`, `OrdersCreateResponderTraceContinuationTests.cs:88`, `OrdersCreateIdempotentReplayTests.cs:453`, `CatalogReferenceListAcceptanceTests.cs:372`.

**The `Dispatch` column was derived by subtraction, not by searching for overrides** — searching for an override would have been a sweep filtered by the property under test. The whole-project sweep

```
find ./tests/Orders.IntegrationTests -name '*.cs' … | xargs -0 grep -n 'Dispatch'
```

returns 20 hits, every one of which is `AddDispatcher` (the unrelated CQRS registration), a doc comment or a prose comment. **There is no `options.Dispatch…` assignment anywhere in the project**, so every site in group A runs the production default `DegreeOfParallelism = 8`. Id 80's review's mitigant is **verified still true**, not inherited.

### 3.2 Bullet 2 — the integration guard

`tests/Orders.IntegrationTests/SagaCommandDispatchConcurrencyIntegrationTests.cs` (new, 1 test), `[Collection(SagaCollection.Name)]` — an existing collection, so **no new container**.

`TheFastPathDispatchesTwoOrdersConcurrently_TheSecondOrdersStockReserveReachesSentWhileTheFirstsIsStillGated`: two orders through the real host, real NATS, real Kafka and the real `saga_commands` table. Order A's `stock.reserve` RPC is held open by a gated responder; only once the responder has **provably received** A's request (never "by now it must have") is order B placed; B's row is then observed reaching `sent` while A's row is asserted still not `sent`.

**Two things are deliberately taken out of the way, and both are stated in the test's own doc comment, because leaving them in would make the guard unable to fail:**

- **`Sweeper.Enabled = false`.** The sweeper is the durability backstop: it re-claims any row left `pending` past its grace window and dispatches it itself. With it running, a **sequential** worker still gets B sent — eventually, by the other mechanism — and the test would pass against exactly the regression it names. This is also why the pre-existing `OperatorCancelRacesSagaForwardProgressTests.Confirmed_ReleaseWins` is **not** this guard: its own comment records that before id 80 the sweeper was what delivered `stock.release` there, so its wait never distinguished the two mechanisms.
- **`Command.TimeoutMs = 60_000`** against a 20 s observation bound, so a sequential worker cannot be freed by the gated RPC timing out inside the window.

The gated responder answers **each request on its own task**, and holding is the **default** with release explicit by order reference. Both matter: a sequential responder would stall B's request at the responder and the test would fail against a *correct* worker, and a gate armed after placement would be a timing race.

### 3.3 Bullet 3 — armed by change of KIND

| Mutation | Named test | Verbatim failure |
|---|---|---|
| `src/Orders/Infrastructure/Saga/SagaCommandDispatchWorker.cs` — `var degreeOfParallelism = Math.Max(1, options.Value.Dispatch.DegreeOfParallelism);` reverted to a single sequential loop (`_ = options.Value.Dispatch.DegreeOfParallelism; var degreeOfParallelism = 1;` — the discard keeps `options` read so the mutation is a behaviour change and not a compile error) | `SagaCommandDispatchConcurrencyIntegrationTests.TheFastPathDispatchesTwoOrdersConcurrently_…` | `order ORD-000002's stock.reserve never reached 'sent' within 20s while order ORD-000001's stock.reserve RPC was held open by the gated responder (its own per-attempt budget is 60s, and the sweeper backstop is disabled for this test). SagaCommandDispatchWorker is therefore dispatching the fast path SEQUENTIALLY: one blocked responder is stalling every other order's saga command behind it — backlog id 80's defect. Order ORD-000001's own row is 'pending'; requests observed by the responder: [ORD-000001].` |

The message names the blocked order, the stalled order and the bound, as bullet 3 requires. Restored from backup, `cmp`-identical, `touch`ed, rebuilt `--no-incremental`, then run **three times: 1/1 green each time** (10 s, 10 s, 10 s). Not "the flake stopped" — the mutated worker fails at 30 s every time it was run, the fixed worker passes every time.

### 3.4 Bullet 4 — measured runtime cost, and where it belongs

**10 s**, measured, three consecutive runs. It joins `SagaCollection`, which `SagaHappyPathTests`, `OrdersCancelAcceptanceTests`, `SagaDeadLetterTests` and nine other classes already share, so it starts **no** container — container startup dominates, and a new fixture would have cost a fresh MS-SQL (~20–30 s), Kafka and NATS. Against `Orders.IntegrationTests`' total it is a rounding error.

Cost accounting for the whole batch: **+17 tests** — 4 (id 69 Gateway) + 12 (id 69 Kafka policy, 4 × 3 copies) + 1 (id 89). Added wall-clock: 2 s + 3 × 10 s + 10 s ≈ **42 s**, no new containers anywhere.

### 3.5 Bullet 5 — not taken, and why

Bullet 5 permits closing the entry as a documented decision *if* a unit-level guard plus universal incidental exercise is sufficient. The evidence says it is not, and the cost of proving it properly was small:

- id 80's three guards are all against a **fake** `ISagaCommandDispatcher`; the enumeration in §3.1 confirms the real worker runs in every host-booting integration test but **nothing asserts** its concurrency there;
- the guard was constructible **inside an existing collection** at 10 s and zero new containers;
- and it kills the regression at the level closest to production, with a message naming the orders and the bound.

Closing it as a decision would have been the cheaper sentence and the worse outcome.

---

## 4. Defeat list — which of `CLAUDE.md`'s ten attacks were run against the new guards

The guards written here are `GatewayReadinessPacingRaceTests` (4), `KafkaCommittedOffsetRetryTests` (4 × 3), `SagaCommandDispatchConcurrencyIntegrationTests` (1), and the ~34 replaced assertions of id 82.

| # | Attack | Run? |
|---|---|---|
| 1 | Delete the behaviour | **Yes** — 69-A/69-B delete the pacing; 82-14 deletes `InjectNats`; 82-5 deletes the conditional; 89's mutation deletes the parallelism. All killed. |
| 2 | Corrupt a payload field the test supplied | **Yes** — 82-13 corrupts `x-first-failed-at` to a literal; 82-4 corrupts the request on the wire; 82-6 hard-codes `enabled`. All killed. Also structural: every id 82 replacement was chosen so the *value* is printed, which is what makes a corruption probe legible. |
| 3 | Substitute a valid sibling identifier | **Yes** — 82-1 substitutes `"stock.release"` → `"credit.release"` (a real sibling command token, and the failure names the claim rather than the default). For id 69's two Gateway loops the `subject` became a **parameter**, which is exactly the shape `CLAUDE.md` warns about — so it is stated here that the production call sites pass the literals `StockSubjects.StockCheck` and `[ProbeByte]`, and the race test passes a `Guid`-suffixed subject that no responder in the repository subscribes to; a swap between them cannot pass. For `KafkaCommittedOffsetRetry` the group id is the caller's own `groupId` argument, unchanged from the four pre-existing sites. |
| 4 | Shadow the pattern from a comment or string literal | **Not applicable** — none of these guards is a text scanner. Every one of them **executes** the code it is about, so text that reads like code cannot satisfy it. |
| 5 | Hide the real thing in a dead region (`#if false`) | **Not applicable** — same reason. A `#if false` region does not run, and these guards assert on runtime behaviour, so hiding the behaviour there makes them fail rather than pass. |
| 6 | Hide it in a raw or verbatim string | **Not applicable** — same reason. |
| 7 | Drop an OPTIONAL element entirely, where the guard only compares presence | **Yes, and it is the theme of id 82.** The replacements move exactly this way: `Assert.False(string.IsNullOrEmpty(x))` compares presence, `Assert.Matches`/`Assert.Equal` compares the value. 82-11's own new failure (`Value: null`) is the absence case caught by a value-comparing assertion. |
| 8 | Compare a literal to a literal — a "check" that never reads the tree | **Yes, considered and avoided.** Every new bound is read from the constant it is about (`StandInResponder.ReadinessPacingInterval`, `ReadinessAttempts`, `KafkaCommittedOffsetRetry.CoordinatorReadyBudget`) rather than transcribed, so a change to the production constant moves the assertion with it. The id 89 test reads the actual `saga_commands` rows and the actual responder's observed references. |
| 9 | Satisfy the closer half of a two-part claim and leave the premise half stale | **Yes — and it caught two things.** The premise of id 69's whole class is *"`NatsNoRespondersException` returns immediately"*: it is asserted, not assumed (test 1). The premise of the id 89 test is *"A is still gated when B is observed"*: it is asserted (`firstStatus != "sent"`), not assumed. And re-running rather than re-reading the id 82 rows caught a stale line number (§2.5) and a mutation that no longer kills (§5, D1). |
| 10 | Let a build-output copy join the population | **Yes** — every enumerating command in this record excludes `bin/`/`obj/` **by path** (`find … -not -path '*/bin/*' -not -path '*/obj/*' -print0 \| xargs -0 grep`), never by post-filtering `grep -rn` output, which would filter on the matched line's content and silently drop any line quoting the command itself. |

---

## 5. Disclosures — things found that are NOT in these three entries' scope

### D1 (substantive, recommended as a new backlog entry) — `SagaConsumptionTests.SO9`'s two F6 mutations no longer kill it

**Measured, not suspected.** `src/Orders/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs:134`, `EnableAutoOffsetStore = false` → `true` — the exact mutation `impl_order_saga_orchestrator.md`'s F6 row 1 records as killing the test. Forced `--no-incremental` rebuild; `SagaConsumptionTests.SO9_AHandlerThatThrows_LeavesTheCommittedOffsetUnchangedAndTheFactIsRedelivered` ran **Passed! Failed: 0, Passed: 1 — 11 s**. Restored, `cmp`-identical, rebuilt, 1/1 green again.

The mechanism is named by the test's own comment, which has gone stale: it relies on *"KafkaFactStreamSubscriber's `finally` calls `consumer.Close()` on the dying consumer the instant the FIRST handler's exception propagated out of `ConsumeAsync`"*, and `Close()` committing any prematurely stored offset. Since `observability_reliability` added OR1, `SagaFactsConsumer` wraps every dispatch in `FactRetryDispatcher`, which retries **in-process** and dead-letters — so the handler's exception no longer escapes `ConsumeAsync`, the consumer never dies, `Close()` is never reached, and the prematurely stored offset is never committed before the test reads it. `gate.Attempts >= 2` is then satisfied by OR1's **in-process retry**, not by a Kafka redelivery at all — while the test's name still says *"AndTheFactIsRedelivered"*.

This is `CLAUDE.md`'s guard-that-does-not-guard in its purest form: a correct claim, a passing test, and a mutation of the exact behaviour the claim is about leaving the suite green. It is **out of id 82's scope** (that entry is about arming *messages*) and is routed here rather than fixed, per the rule that a disclosure becomes a numbered backlog entry. `feature_list.json` was not touched — **the coordinator is asked to file it.** The annotation is also written into `progress/impl_order_saga_orchestrator.md` beside the F6 rows, so a reader of that record cannot inherit the superseded claim.

### D2 (procedural) — one M7 re-run is still owed

`review_notification_send_degrades_on_permanent_failure.md`'s M7 row quoted a message from an assertion that this batch **deleted**. The same assertion's replacement was re-armed under the sibling A1 mutation (`ActivityTrackingOptions.None`), which exercises the same field by the same route, and the row now says so. M7's **own** mutation (rendering the fallback outside the ambient activity) was not re-run, because locating and re-creating it was disproportionate to refreshing one quoted string. Stated rather than papered over.

### D3 (informational) — a `pgrep` that could count itself

The brief warns that `pgrep -f "dotnet (build|test|format)"` can match the waiting shell's own command line. It did, on the first check of this session: two `bash` PIDs were returned, both of them this session's own shells. Every subsequent idle check used `pgrep -a -x dotnet | grep -v nodemode`, which matches the executable name and treats idle MSBuild reuse nodes (`MSBuild.dll /nodemode:1`) as not-a-build. No two builds or test runs overlapped at any point in this session.

---

## 6. Files touched

**New**
- `tests/Gateway.IntegrationTests/GatewayReadinessPacingRaceTests.cs`
- `tests/Orders.IntegrationTests/KafkaCommittedOffsetRetry.cs`, `tests/Orders.IntegrationTests/KafkaCommittedOffsetRetryTests.cs`
- `tests/Notifications.IntegrationTests/KafkaCommittedOffsetRetry.cs`, `tests/Notifications.IntegrationTests/KafkaCommittedOffsetRetryTests.cs`
- `tests/Projector.IntegrationTests/KafkaCommittedOffsetRetry.cs`, `tests/Projector.IntegrationTests/KafkaCommittedOffsetRetryTests.cs`
- `tests/Orders.IntegrationTests/SagaCommandDispatchConcurrencyIntegrationTests.cs`
- `progress/impl_batch_d2_pacing_arming_message_and_dispatch_guards.md` (this file)

**Modified — `tests/` only** (no `src/` file is left changed by this batch; every `src/` mutation was an arm, restored and `cmp`-verified)
- id 69: `tests/Gateway.IntegrationTests/StandInResponder.cs`, `tests/Gateway.IntegrationTests/FulfillmentStockEndToEndTests.cs`, `tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs`, `tests/Notifications.IntegrationTests/NotificationDeadLetterTests.cs`, `tests/Projector.IntegrationTests/ProjectorDeadLetterTests.cs`, `tests/Projector.IntegrationTests/OffsetContractTests.cs`
- id 82: `tests/Billing.IntegrationTests/LogCorrelationTests.cs`, `tests/Fulfillment.IntegrationTests/LogCorrelationTests.cs`, `tests/Fulfillment.IntegrationTests/StockCheckTests.cs`, `tests/Fulfillment.UnitTests/DespatchCreationServiceTests.cs`, `tests/Gateway.IntegrationTests/AuthAndRateLimitHttpTests.cs`, `tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs`, `tests/Gateway.IntegrationTests/ProblemJsonCorrelationTests.cs`, `tests/Gateway.UnitTests/PlaceOrderCommandHandlerTests.cs`, `tests/Gateway.UnitTests/ReplayBufferTests.cs`, `tests/Notifications.IntegrationTests/LogCorrelationTests.cs`, `tests/Notifications.IntegrationTests/NotificationDegradesOnPermanentFailureTests.cs`, `tests/Orders.IntegrationTests/CatalogReferenceListAcceptanceTests.cs`, `tests/Orders.IntegrationTests/LogCorrelationTests.cs`, `tests/Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs`, `tests/Orders.IntegrationTests/OutboxEnvelopeTests.cs`, `tests/Orders.IntegrationTests/SagaCommandDeadLetterTests.cs`, `tests/Orders.IntegrationTests/SagaCommandRetryTests.cs`, `tests/Orders.IntegrationTests/SagaCommandStoreTests.cs`, `tests/Orders.UnitTests/DeadlockRetryExecutionStrategyTests.cs`, `tests/Orders.UnitTests/KafkaFactPublisherConfigTests.cs`, `tests/Orders.UnitTests/NatsSagaCommandsAdapterTests.cs`, `tests/Projector.IntegrationTests/TimelineProjectionTests.cs`, `tests/Projector.UnitTests/SummariesTests.cs`, `tests/Seed.IntegrationTests/SeedIntegrationTests.cs`

**Modified — `progress/`**: the 17 records listed in §2.4.

**Not touched**, as the brief required: `feature_list.json`, `CLAUDE.md`, `specs/shared/`, the #7 repository, `apps/web/`, and every service not named above. No `git` command that writes the index or the working tree was run; no `git commit`, no `git push`.

---

## 7. Verification

### 7.1 `./quality.sh`, one clean run at the end of the batch

```
── 1. Format check    [OK] dotnet format --verify-no-changes: clean
── 2. Build           [OK] dotnet build: succeeded  (0 warnings, 0 errors)
── 3. Test + coverage [OK] dotnet test: all tests passed
```

Per-project, all 18 projects, read off that run:

| project | failed | passed | skipped | total | duration |
|---|---:|---:|---:|---:|---|
| SharedKernel.UnitTests | 0 | 50 | 0 | **50** | 252 ms |
| Cqrs.UnitTests | 0 | 23 | 0 | **23** | 188 ms |
| Contracts.UnitTests | 0 | 24 | 0 | **24** | 322 ms |
| Notifications.UnitTests | 0 | 111 | 0 | **111** | 2 s |
| Gateway.UnitTests | 0 | 237 | 0 | **237** | 929 ms |
| Fulfillment.UnitTests | 0 | 146 | 0 | **146** | 1 s |
| Orders.UnitTests | 0 | 491 | 0 | **491** | 11 s |
| Billing.UnitTests | 0 | 262 | 0 | **262** | 3 s |
| Seed.UnitTests | 0 | 44 | 0 | **44** | 105 ms |
| Projector.UnitTests | 0 | 120 | 0 | **120** | 7 s |
| Architecture.Tests | 0 | 36 | 0 | **36** | 19 s |
| Seed.IntegrationTests | 0 | 6 | 0 | **6** | 27 s |
| Projector.IntegrationTests | 0 | 63 | 0 | **63** | 1 m 25 s |
| Notifications.IntegrationTests | 0 | 26 | 0 | **26** | 3 m 1 s |
| Fulfillment.IntegrationTests | 0 | 64 | 0 | **64** | 3 m 32 s |
| Billing.IntegrationTests | 0 | 90 | 0 | **90** | 4 m 54 s |
| Gateway.IntegrationTests | 0 | 65 | 0 | **65** | 6 m 26 s |
| Orders.IntegrationTests | 0 | 152 | 0 | **152** | 11 m 43 s |
| **solution** | **0** | **2010** | **0** | **2010** | |

The per-project lines sum to **2010**, which is the figure reported; it was summed from the lines rather than read off a summary.

### 7.2 Reconciliation

**The four baselines the brief supplied are unit-test projects, and all four are unchanged**: Gateway.UnitTests **237** (brief: 237), Billing.UnitTests **262** (262), Orders.UnitTests **491** (491), Fulfillment.UnitTests **146** (146). This batch added no unit test — id 82 replaced assertions inside existing cases and added none — so those four matching exactly is the strongest available evidence that nothing outside the intended change moved.

**This batch's own delta is +17, all in integration projects**, counted from the new files (`grep -c '^    \[Fact\]'`):

| new class | tests |
|---|---:|
| `Gateway.IntegrationTests/GatewayReadinessPacingRaceTests` | 4 |
| `Orders.IntegrationTests/KafkaCommittedOffsetRetryTests` | 4 |
| `Notifications.IntegrationTests/KafkaCommittedOffsetRetryTests` | 4 |
| `Projector.IntegrationTests/KafkaCommittedOffsetRetryTests` | 4 |
| `Orders.IntegrationTests/SagaCommandDispatchConcurrencyIntegrationTests` | 1 |
| | **17** |

So the pre-batch solution total was **1993 = 2010 − 17**. That figure is a **subtraction, not a measurement**: no full-solution run was taken before this batch started, so it is stated as a derivation.

**The gap to the last full-solution figure on record is stated rather than glossed.** `progress/current.md` records **1833** at the SA-3 verification run. 1993 − 1833 = **+160**, and none of it is this batch's: between that run and this one, commits `b453931`, `1affd4a` and `55b21f7` landed the guard-hardening audit, SA-4 and the dead-letter arming fix, and the working tree also carries batch D1's uncommitted additions (`progress/impl_batch_d1_gateway_payload_dedup_and_key_set_guards.md`) and the SA-4/id-62 rework. Those +160 are other features' and are not reconciled line-by-line here; what is reconciled is that **every number this batch is responsible for adds up exactly, and the four independent baselines the brief measured are unmoved**.

### 7.3 State

- `./init.sh` → **exit 0** at the start and at the end of the session.
- No `ARMED` marker survives anywhere: `find ./src ./tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n 'ARMED (id 8\|ARMED (backlog id 89'` → no output.
- `git status --porcelain src/` shows **only** the nine Gateway files that were already modified by batch D1 before this session began — **no `src/` file carries a change from this batch**, which is the expected end state: every `src/` edit here was an arm, restored from a `cp` backup and `cmp`-verified.
- No two `dotnet build`/`test`/`format` processes ever overlapped; the one long `./quality.sh` run was backgrounded and waited on by **PID** (`while kill -0 3821257; do sleep 30; done`), never by a `pgrep -f` pattern that could match the waiting shell itself.
