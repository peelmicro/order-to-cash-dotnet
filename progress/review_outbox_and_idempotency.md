# `review_outbox_and_idempotency.md` — feature 14 (phase 8), review pass 1

**Verdict: APPROVED**, with one artefact that must be deleted before the commit (D1) and seven non-blocking advisories.

The substance holds under independent mutation. Five mutations of my own — none of them a repeat of the implementer's nine — were introduced, built non-incrementally and run against real MS-SQL and real Kafka; every one produced a red named test, and the restore was verified byte-for-byte and re-run green. The two questions the brief singled out as unsettleable from the report both resolve in the implementation's favour: the wire-parity check proves the **producer**, not the serialiser, and the `OI13` rewrite genuinely fixed the guard-that-did-not-guard rather than merely reporting it.

---

## 1. What I ran, and what I deliberately did not

| Ran | Why |
|---|---|
| `./quality.sh`, in full, once | The claim under test *is* about the full suite (format + build + every project + coverage), and the brief asks for the wall-clock. **Green. 268 tests, 0 failures. Wall-clock 287.9 s (4 m 48 s)** — up from ~4 min before this feature, the delta being `Orders.IntegrationTests` at 2 m 34 s (39 tests, now container-backed for Kafka as well as MS-SQL). `dotnet format --verify-no-changes` clean; `dotnet build` clean; **`Architecture.Tests` 15/15 green** (the twelve inherited rules, the two `DomainAssemblies`/`Cqrs` rules, and the new `OI16`). |
| `./init.sh` | **Exit 0.** All `[OK]` including section 4's superseded-rule sweep (`no superseded rule text outside progress/`) and SDD coherence. The only `[WARN]`s are the two expected ones (53 uncommitted changes mid-session; "run quality.sh yourself"). |
| Five mutation probes + the confirming green run (§4) | This is the part of a review that cannot be delegated or inherited. |
| `dotnet build OrderToCash.sln --no-incremental` then `dotnet test tests/Orders.IntegrationTests` after restoring | The stale-binary hazard `CLAUDE.md` names. **39/39 green.** |
| A programmatic column-by-column diff of `specs/shared/test-matrix.md` against the #7 checkout | §7. |

| Did **not** run | Why |
|---|---|
| The implementer's nine arming rows, re-armed | Duplicated cost. I armed five **different** mutations instead, three against the same branches by a different route (so a test that only survives one particular mutation is exposed) and two against branches its table does not cover at all. |
| The `READPAST` deletion / the 117 ms `OI13` measurement | Established independently by the leader and explicitly out of scope for this pass. I verified the *test's shape* instead (§3), which is the part the leader's arming could not settle. |
| `Fulfillment` / `Billing` / `Notifications` / `Seed` integration suites as separate invocations | They ran inside `quality.sh` (19/19, 23/23, 7/7, 6/6). Re-invoking them would have been the same containers twice. |
| Retry-and-dead-lettering (`R16`) | Ratified out of scope at the human gate; feature 27's acceptance carries it. Not raised. |

`CLAUDE.md` was `grep`ped on disk before any rule below was enforced. It has gained a sixth amendment this session (*"Never hand the human an open question you could have closed"*, lines 63–71), which does not bear on this feature's code; `.superseded-rules` carries four phrasings and `init.sh` reports none of them present outside `progress/`.

---

## 2. Requirement → test traceability, walked

Every name below was resolved to a file on disk (not read off the report) and every cited test ran green in the `quality.sh` pass.

### Shared requirements this feature owns

| Id | Test(s) — verified present and green | Non-vacuity evidence |
|---|---|---|
| **R11** | `tests/SharedKernel.UnitTests/DomainEventEnvelopeTests.cs` › `R11_DomainEventEnvelope_RefusesAnEnvelopeWithAnAbsentNullOrEmptyFieldAndAnEventTypeThatDoesNotMatchThePattern_{EventIdEmpty,AggregateIdEmpty,CorrelationIdEmpty,CausationIdEmpty,OccurredAtDefault,EventTypeNull,EventTypeEmpty,BadEventType(×4)}` + `_AcceptsACompleteEnvelope` + `_AcceptsEveryEventTypeMatchingThePattern(×3)`; `tests/Orders.IntegrationTests/OutboxEnvelopeTests.cs` › `R11_Outbox_RefusesToStoreAFactWhoseEventTypeIsNotInTheDeclaredFactCatalogue`; `tests/Orders.IntegrationTests/OutboxWireParityTests.cs` › `R11_PublishedEnvelope_CarriesTheSevenFieldsInTheDeclaredOrderWithNoneAbsentNullOrEmpty` | Pure — `SharedKernel.UnitTests` references no framework and `SharedKernelHasNoPackagesTests` is green. The wire case asserts the field **order** (`OutboxWireParityTests.cs:181`) against a fact the aggregate really placed, off a real broker, plus both headers and the deliberate absence of `traceparent` (`:194-196`). |
| **R12** | `OutboxEnvelopeTests.cs` › `R12_Outbox_StampsEveryFactOfOneOrderWithTheOrderIdAsCorrelationIdAndTheCausingEventIdAsCausationId` | Two transactions, second driven through a **reloaded** aggregate; asserts both rows carry the order id and the confirm row's `causation_id` is the id handed to `Confirm` (`:73-79`). |
| **R13** | `OutboxAtomicityTests.cs` › `R13_UnitOfWork_PersistsNeitherTheAggregateNorTheOutboxRecordAndPublishesNothingWhenTheTransactionFails` **and** `R13_UnitOfWork_RollsBackAnOutboxRowAlreadyWrittenWhenTheAggregatesOwnSaveFailsAfterwards` | My probe **P3** kills the second case and, crucially, **not** the first — independently reproducing the implementer's own finding that the matrix-named case alone cannot discriminate a transaction escape. The second case is what makes R13 true. |
| **R14** | `OutboxRelayTests.cs` › `R14_Relay_StampsARecordOnlyAfterTheBrokerAcknowledgementAndRepublishesAnUnstampedRecordOnTheNextPoll` | First cycle uses a **real** `KafkaFactPublisher` against a deliberately unreachable bootstrap (`:42-53`) — a real failed publish, not a stub — then asserts `PublishedAt` null, then republishes against the real broker and consumes it. Killed by **P2**. |
| **R15** | `FactPartitioningTests.cs` › `R15_FactStream_DeliversAllFactsProducedByOneContextAboutOneOrderToConsumersInEmissionOrder` | Real Kafka, two orders interleaved 5 facts, asserts one partition per order **and** per-order emission order (`:85-94`). |
| **R16** | Deferred to feature 27, ratified at the gate. Matrix row correctly left `TODO`. | — |
| **R17** | `IdempotentConsumerTests.cs` › `R17_IdempotentConsumer_RecordsTheEventIdAndConsumerNameInTheSameTransactionAsTheStateChangeAndTheOutboxRecords` **and** `R17_IdempotentConsumer_LeavesNoDedupRowWhenAFailureInsideWorkRollsBackTheWholeTransaction` | The second case is the one #7's D10 needed. Killed by **P3** (`Expected: 0, Actual: 1`). |
| **R18** | `IdempotentConsumerTests.cs` › `R18_IdempotentConsumer_AcknowledgesARedeliveredFactWithoutMutatingStateEmittingAFactOrIssuingACommand` | Killed by **P1**. Two of the three negatives are asserted; the third is not — see **D2/D3**. |

### Feature-local `OI1` – `OI16`

All sixteen resolve to a real file and a real case; all ran green.

`OI1` `OutboxEnvelopeTests` › `OI1_Relay_ReconstructsTheCompleteEnvelopeFromTheStoredRecordAloneInferringNoFieldAtPublicationTime`, `OI1_Writer_RefusesAnEventWithAnEmptyCausationIdBeforeAnyRowIsBuilt` · `OI2` `OutboxRelayTests` › `OI2_Relay_PublishesTwoRecordsWrittenByOneTransactionInAppendOrderAlthoughBothCarryTheSameOccurredAt`, `OI2_Relay_OrdersTheClaimBySeqNeverByOccurredAtEvenWhenTheyDisagree` · `OI3` › `OI3_Relay_PublishesALowerSequenceRecordThatCommittedAfterAHigherSequenceRecordWasAlreadyPublished` · `OI4` `OutboxRelayConcurrencyTests` › `OI4_TwoConcurrentRelays_GrantDisjointBatchesAndPublishEveryRecordExactlyOnce` · `OI5` › `OI5_Relay_ReturnsRecordsClaimedByARelayThatDiedBeforeStampingToTheNextPollWithoutALeaseWait` · `OI6` `tests/Orders.UnitTests/OutboxRelayLoopTests.cs` › `OI6_RelayLoop_NeverStartsASecondPollCycleWhileOneIsStillInProgress`, `OI6_RelayLoop_StopAsyncWaitsForTheInFlightCycleToFinish` · `OI7` `KafkaFactPublisherConfigTests` › `OI7_Producer_IsConfiguredSoAnInternalRetryCanNeitherReorderNorDuplicateAPartitionsRecords` · `OI8` `OutboxRelayTests` › `OI8_Relay_LeavesEveryRecordOfARejectedBatchUnstampedAndRepublishesTheSameRecordsOnTheNextPoll` · `OI9` `OutboxAtomicityTests` › `OI9_Repository_ProducesExactlyOneOutboxRecordPerFactWhenTheOperationIsRetriedAfterARolledBackUnitOfWork` · `OI10` `IdempotentConsumerTests` › `OI10_IdempotentConsumer_AppliesTheHandlersEffectsOnceWhenTheSameEventIsDeliveredConcurrentlyToTwoConsumers` · `OI11` `tests/Billing.IntegrationTests/ReliabilityTableParityTests.cs` › `Outbox_And_ProcessedEvents_Are_Defined_Identically_Across_All_Four_DbContexts` (inherited satisfied, re-run green **under the new fixture setting** — which is the only thing this feature owed it) · `OI12` `tests/Orders.UnitTests/IdempotentConsumerParityTests.cs`, four cases (see **D4**) · `OI13` `OutboxRelayConcurrencyTests` › `OI13_Claim_SkipsRowsHeldByAnotherRelayAndBehavesIdenticallyWithRowVersioningEnabledOnTheDatabase` · `OI14` `OutboxRelayTests` › `OI14_Relay_AbandonsAPublishThatExceedsTheTimeoutRollsTheClaimBackAndRepublishesOnTheNextPoll` · `OI15` `OutboxWireParityTests` › `OI15_Relay_PublishesBytesIdenticalToTheGoldenEnvelopeCapturedFromNumber7`, `OI15_Recorder_WritesAPayloadSemanticallyEqualToNumber7sForTheSameBusinessInputs` · `OI16` `tests/Architecture.Tests/FactPublisherConfinementTests.cs` › `OnlyTheOutboxAdapterMayReferenceTheFactStreamProducerClient`.

Plus the two tasks that carry no requirement id: `tests/Orders.IntegrationTests/MsSqlContainerFixtureTests.cs` › `Fixture_CreatesDatabasesWithRowVersioningEnabledExactlyAsTheDeployedStackDoes` (B2) and `tests/Seed.IntegrationTests/SeedIntegrationTests.cs` › `TheRelayFindsNoUnpublishedRecordInAnySeededWriteModel` (H6).

### Tasks

Every box in `tasks.md` A1 – J8 is ticked, and I checked the artefact behind each rather than the tick: the three package entries **plus one the task did not name** (D6), the four fixtures (B1/B4), all four ports and the two SharedKernel files, the writer/mapper/repository/unit-of-work, the relay and its `IOutboxRelay` seam, the two canonical `Messaging/` files with their banners, `AddOrdersOutbox` with one explicit line per port and its resolution test, and the wire-parity trio. **Exactly one file under any `Domain/` changed** — `git diff --stat -- src/*/Domain` returns `src/Orders/Domain/Events/OrderDomainEvent.cs | 11 ++++++++++-`, and the change is the interface declaration and a `<remarks>` block, nothing else (J4 satisfied).

---

## 3. The five things the brief said I had to settle

**(1) Does the wire-parity check prove the PRODUCER, or re-prove the serialiser? — It proves the producer.** `OI15_Relay_PublishesBytesIdenticalToTheGoldenEnvelopeCapturedFromNumber7` reads the golden bytes from disk (`OutboxWireParityTests.cs:43`), writes an `outbox` **row** whose payload text is the golden file's own bytes verbatim via `GetRawText()` (`:61`), commits it, and then runs the production `OutboxRelay`, which re-reads that row **out of MS-SQL** through the `FromSqlInterpolated` claim before serialising it. The final assertion (`:95`) is `Assert.Equal(goldenBytes, consumed!.Message.Value)` — golden file bytes against bytes **consumed from a real Kafka broker**. Nothing in the chain compares a serialiser to itself. `OI15_Recorder_WritesAPayloadSemanticallyEqualToNumber7sForTheSameBusinessInputs` likewise reads `db.OutboxMessages.Select(o => o.Payload).SingleAsync()` (`:129`) — the stored column — and compares it to the golden payload by key set, kind, value and casing, with order asserted nowhere.

That it is *live* rather than merely non-circular is established by probes **P4** and **P5**: a mutation in `OutboxEnvelopeMapper` (a producer-side class Phase 5 never touched) and a mutation in `OutboxWriter`'s serialiser options each kill exactly one of the two cases and neither kills the Phase 5 golden tests' own project. P4 is #7's shipped D4 defect class reproduced deliberately — a local-offset `occurredAt` — and OI15 catches it.

**(2) Is idempotency proven by a real redelivery? — Yes, and by a real concurrent one; but only two of the three negatives are asserted.** The redelivery in `R18` is a genuine second `RunOnceAsync` on a **fresh `DbContext`** against the same database (`IdempotentConsumerTests.cs:100-105`), detected by the unique-index violation rather than a `SELECT` — there is no `SELECT` anywhere in the dedup path (`IdempotentConsumer.cs`, `ProcessedEventLedger.cs:60-74`). `OI10` runs two deliveries genuinely concurrently on separate connections and asserts one `Processed`, one `Duplicate`, one effect. Under probe **P1** (dedup check made to pass through) both `R18` and `OI10` fail. The three negatives: **no fact emitted** is asserted (`:113`, exactly one `order.confirmed.v1` row); **no mutation** is asserted only indirectly, via `workCallCount == 1` (`:108`) — the order row is never re-read after the redelivery (**D3**); **no command issued** is asserted nowhere — `OrdersDbContext` exposes `SagaCommands` and `SagaIgnoredFacts`, and neither is queried (**D2**). Under P1 the failure that surfaces is `IllegalOrderTransitionError : Cannot transition an order from 'confirmed' to 'confirmed'` — the aggregate's own state machine, upstream of the row's three named negatives. The guard guards; the discriminator is not the one the name advertises.

**(3) The single-transaction guarantee — the failing half is genuinely guarded.** Probe **P3** made `EfCoreUnitOfWork` commit on throw instead of rolling back. `R13_UnitOfWork_RollsBackAnOutboxRowAlreadyWrittenWhenTheAggregatesOwnSaveFailsAfterwards` failed with `Expected: 1, Actual: 2` (the escaped outbox row survived) and `R17_IdempotentConsumer_LeavesNoDedupRowWhenAFailureInsideWorkRollsBackTheWholeTransaction` failed with `Expected: 0, Actual: 1`. The matrix-named first `R13` case stayed **green** under the same mutation — which is precisely why the second case had to exist, and confirms the implementer's account of why it added one.

**(4) `published_at` stamped only after broker acknowledgement — guarded, and at-least-once is what is asserted.** Probe **P2** moved the `ExecuteUpdateAsync` stamp above `PublishAsync` and made the failure branch commit instead of roll back. `R14`, `OI8` and `OI14` all failed with `Assert.Null() Failure: Value of type 'Nullable<DateTime>' has a value`. The re-claim half is asserted in the same three cases (`OutboxRelayTests.cs:265-277`, `:318-331`), and `OI5`/`OI14` assert it happens **immediately**, with no lease wait. Nothing anywhere asserts exactly-once; `OI5`'s own remarks name the duplicate publication as the accepted `R14` contract. Correct.

**(5) The self-caught defect — the rewrite is real, and complete.** `OI13` (`OutboxRelayConcurrencyTests.cs:140-207`) builds **both** transactions from the production class: the holder is `holderRelay.RunOnceAsync(holderCts.Token)` at `:166`, `holderRelay` coming from `BuildRelay` → `new OutboxRelay(...)` at `:209-210`, blocked inside a publisher whose `PublishAsync` is `Task.Delay(Timeout.InfiniteTimeSpan, ct)`; the measured side is `secondRelay.RunOnceAsync` at `:185`. There is no hand-written claim SQL in that test at all. I checked whether the rewrite was partial: the **only** inline claim SQL left in the project is in `OI5` (`:86-94`), where it is deliberate and harmless — it plays the role of the relay that *died*, and `OI5`'s measured side is a real `OutboxRelay`. So the class of defect the implementer caught in its own work is closed rather than displaced.

**(6) Task group B — every fixture, not just the one this feature needed.** `git diff tests/*/MsSqlContainerFixture.cs` shows the identical `ALTER DATABASE [...] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;` block added to **all four** — Orders, Fulfillment, Billing, Notifications — each pointing at `infra/mssql/init/01-create-databases.sql`. There is no fifth fixture (Projector has no MS-SQL fixture yet). All four suites ran green under it in `quality.sh`. The assertion, however, exists only for Orders — see **D8**.

---

## 4. My own arming table

Protocol: `cp` backup → mutate → run the specific named tests against real containers → record the message verbatim → `cp` restore → `touch` → `dotnet build --no-incremental` → confirming green run. Backups in the session scratchpad under `revarm/`.

| # | Branch under test | Armed by | Named test(s) that failed | Verbatim failure |
|---|---|---|---|---|
| **P1** | a redelivery runs no work | `src/Orders/Infrastructure/Messaging/IdempotentConsumer.cs:57-66` — the duplicate short-circuit removed, so the dedup check passes through and `work` runs on the duplicate path | `IdempotentConsumerTests` › `R18_IdempotentConsumer_AcknowledgesARedeliveredFactWithoutMutatingStateEmittingAFactOrIssuingACommand`; `OI10_IdempotentConsumer_AppliesTheHandlersEffectsOnceWhenTheSameEventIsDeliveredConcurrentlyToTwoConsumers` | `OrderToCash.Orders.Domain.Errors.IllegalOrderTransitionError : Cannot transition an order from 'confirmed' to 'confirmed': no such edge exists in Table T-1.` (both cases; 2 failed / 3 passed) |
| **P2** | the stamp follows the acknowledgement | `src/Orders/Infrastructure/Outbox/OutboxRelay.cs` — the `ExecuteUpdateAsync` stamp (`:137-139`) moved above `PublishAsync` (`:110`), and the failure branch's `RollbackAsync` (`:127`) changed to `CommitAsync` | `OutboxRelayTests` › `R14_Relay_StampsARecordOnlyAfterTheBrokerAcknowledgement...`, `OI8_Relay_LeavesEveryRecordOfARejectedBatchUnstamped...`, `OI14_Relay_AbandonsAPublishThatExceedsTheTimeout...` | `Assert.Null() Failure: Value of type 'Nullable<DateTime>' has a value` (all three; 3 failed / 3 passed) |
| **P3** | the failing half of the unit of work rolls back | `src/Orders/Infrastructure/Persistence/EfCoreUnitOfWork.cs:41-44` — a `catch` that commits the transaction before rethrowing | `OutboxAtomicityTests` › `R13_UnitOfWork_RollsBackAnOutboxRowAlreadyWrittenWhenTheAggregatesOwnSaveFailsAfterwards`; `IdempotentConsumerTests` › `R17_IdempotentConsumer_LeavesNoDedupRowWhenAFailureInsideWorkRollsBackTheWholeTransaction` | `Assert.Equal() Failure: Values differ — Expected: 1, Actual: 2` and `Assert.Equal() Failure: Values differ — Expected: 0, Actual: 1` (2 failed / 6 passed). **`R13_UnitOfWork_PersistsNeitherTheAggregate...` stayed GREEN** — the finding the implementer reported, reproduced independently. |
| **P4** | the published `occurredAt` carries no machine offset | `src/Orders/Infrastructure/Outbox/OutboxEnvelopeMapper.cs:40` — `new DateTimeOffset(row.OccurredAt, TimeSpan.Zero)` replaced by the implicit conversion (host TZ `CEST +0200`) | `OutboxWireParityTests` › `OI15_Relay_PublishesBytesIdenticalToTheGoldenEnvelopeCapturedFromNumber7` | `Assert.Equal() Failure: Collections differ` (1 failed / 2 passed). This is #7's shipped D4 defect class; `OI15` catches it. |
| **P5** | the stored payload uses the shared wire options | `src/Orders/Infrastructure/Outbox/OutboxWriter.cs:61` — `JsonSerializer.Serialize(payload, JsonWire.Options)` → `JsonSerializer.Serialize(payload)` | `OutboxWireParityTests` › `OI15_Recorder_WritesAPayloadSemanticallyEqualToNumber7sForTheSameBusinessInputs` | `$: key set differs — expected {lines,buyerGln,currency,orderDate,companyCode,supplierGln,totalAmount,retailerCode,initialAmount,orderReference,initialDiscount}, found {OrderReference,RetailerCode,CompanyCode,BuyerGln,SupplierGln,Currency,OrderDate,Lines,InitialAmount,InitialDiscount,TotalAmount,Notes}` (1 failed / 2 passed) — it discriminates casing, key set **and** null-omission at once. |

**Restore verified.** `cmp` byte-identical against the backup for all five files; `grep -rn "REVIEWER ARM" src/ tests/` returns nothing; `dotnet build OrderToCash.sln --no-incremental` exit 0; `dotnet test tests/Orders.IntegrationTests` → **39 passed, 0 failed**. No residue.

---

## 5. Defects

### D1 — blocks the commit, not the feature. A zero-byte stray source file inside `src/`

`src/Orders/Infrastructure/Persistence/OutboxRelay.cs` — **0 bytes, mode `600`**, untracked. It sits at a path that shadows the real relay (`src/Orders/Infrastructure/Outbox/OutboxRelay.cs`, 159 lines): anyone opening the obvious-looking path finds an empty file. It compiles harmlessly, which is why nothing goes red.

It is **not the implementer's artefact**: its mtime is `19:11:17`, after `progress/impl_outbox_and_idempotency.md` was written at `19:09:40`, and its mode is `600` where every implementer-created file in this feature is `664` — the signature of an output redirection from a different process, most plausibly the independent `READPAST` arming run. Recorded here rather than fixed, because I do not edit. **`rm src/Orders/Infrastructure/Persistence/OutboxRelay.cs` before `git add`.** CHECKPOINTS C5's first box is not tickable until it is gone.

### D2 — advisory. `R18`'s third negative — "issuing a command" — is asserted nowhere

`tests/Orders.IntegrationTests/IdempotentConsumerTests.cs:110-113`. The case name claims three negatives; the assertions cover two. `OrdersDbContext` already exposes `SagaCommands` (`OrdersDbContext.cs:37`) and `SagaIgnoredFacts` (`:39`), so the missing line is `Assert.Equal(0, await assertDb.SagaCommands.CountAsync());`. Why it matters: `work` not being invoked is a strong proof today only because nothing else in the process can issue a command — feature 16 is the first that can, and by then the row will read as already covered. Not live now; cheapest to close now.

### D3 — advisory. `R18` never re-reads the aggregate after the redelivery

Same file, same lines. "Without mutating state" is carried entirely by `workCallCount == 1`. One assertion on the order row's `Status`/`UpdatedAt` after the second delivery would make the negative direct rather than inferential.

### D4 — advisory. Three of `OI12`'s four cases cannot fail today

`tests/Orders.UnitTests/IdempotentConsumerParityTests.cs`. Case 1 (`HoldsEveryWriteModelsCopyByteIdenticalToTheCanonicalAfterTheBannerAndTheNamespaceLine`) ranges over `DiscoverCopyServices`, which today returns only `Orders` — it compares the canonical against itself. Cases 3 and 4 range over empty sets. This is exactly the four-case shape `design.md` §6.4 prescribed and `requirements.md` §2.1 disclosed ("the non-vacuous case at n = 1" is case 2), so it is honest rather than hidden — but `OI12`'s coverage today rests on **case 2 alone**, and that should be stated plainly wherever `OI12` is cited as green.

### D5 — advisory. `progress/current.md` is out of lockstep with `feature_list.json`

`progress/current.md:4` reads `**Status:** in_progress` while `feature_list.json` had the feature `in_review`. One word. #7 recorded this identical drift three separate times (its feature-15 D4); it is the cheapest recurring defect in the trilogy.

### D6 — advisory, and it must reach the commit message. An installed package the task list did not name

`Directory.Packages.props` adds `<PackageVersion Include="Testcontainers" Version="4.14.0" />` alongside the three `Microsoft.Extensions.*` entries task A1 named. The addition is right and its comment is exemplary (it records the `Testcontainers.Kafka` probe and why the generic builder was taken), and I confirmed `Testcontainers.Kafka` appears in **no** `PackageVersion` and no `.csproj` — the impl report's claim holds. But `progress/impl_outbox_and_idempotency.md` never states that a fourth package was installed, and `CLAUDE.md` is explicit: *"Never install a package without it appearing in that phase's commit message."* The commit message must list **`Testcontainers` 4.14.0**, `Microsoft.Extensions.Hosting.Abstractions` 10.0.11, `Microsoft.Extensions.Options` 10.0.11, `Microsoft.Extensions.Logging.Abstractions` 10.0.11 (and note the new `Confluent.Kafka` / `Microsoft.Extensions.DependencyInjection` *references* against already-pinned versions).

### D7 — advisory. `R11`'s matrix cell cites a pattern, not a case name

`specs/shared/test-matrix.md`, `R11` row: *"nine cases, one per field/pattern check, named `R11_..._*`"*. The document's own rule 4 makes a case name the contract, resolvable literally. The prefix is unambiguous and every case does exist, so this is cosmetic — but it is the one cell in this feature's seven that a mechanical citation check could not resolve.

### D8 — advisory. Three of the four fixture changes are unguarded

`tests/{Fulfillment,Billing,Notifications}.IntegrationTests/MsSqlContainerFixture.cs` now carry the `READ_COMMITTED_SNAPSHOT ON` statement, but only Orders has `Fixture_CreatesDatabasesWithRowVersioningEnabledExactlyAsTheDeployedStackDoes` pinned on it. Deleting the line from any of the other three leaves every suite green — the same class of invisible drift this task group existed to close, one layer down. Tasks B2/B4 only asked for the Orders assertion, so this is not a gap against the spec; it is a gap against the lesson. Cheapest close: the same one-`[Fact]` file per project, or one shared assertion when feature 17 touches those suites.

**Not defects, checked and dismissed:** the raw per-row outbox `INSERT` (deviation 1) is a genuine, reproduced EF Core ordering defect with a correct fix, and it is guarded by `OI2`'s second case, which I confirmed is the discriminating one; `UnknownConsumerNameError` living under `Application/Ports/` is required by J4's own one-Domain-file check; `JsonEquivalence` being duplicated follows the existing `RepositoryPaths` precedent; `sys.databases` replacing `DATABASEPROPERTYEX` was probed rather than guessed and asserts the same fact; H6's Fulfillment/Billing halves by direct query are within `design.md` §11's non-goals. Retry/dead-lettering not raised — ratified out of scope.

---

## 6. CHECKPOINTS walk

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (six definitions).
- [x] Every agent definition declares its model — `init.sh` reports each explicitly.
- [x] `./init.sh` exits 0.

### C2 — state is coherent
- [x] At most one feature `in_progress` — `init.sh`: `no feature in_progress`; this one was `in_review`.
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests — the whole suite is green.
- [x] `progress/current.md` describes the active session (not leftovers) — though see **D5** on its status word.
- [x] No `blocked` features.

### C3 — architecture is respected
- [x] No EF Core / Kafka / NATS / Mongo / ASP.NET reference in any `Domain/` — `DomainPurityTests` and the rest of `Architecture.Tests` green, 15/15, run not eyeballed. The one `Domain/` change is a declaration of a `SharedKernel` interface.
- [x] No cross-service DB access; no FK crosses a boundary. This feature adds no migration and no schema change at all.
- [x] No shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs` — the two new files land in `SharedKernel`, and `SharedKernelHasNoPackagesTests` is green (zero `PackageReference`). Verified against `CLAUDE.md` **on disk**, which carries the `src/Cqrs` amendment.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` — `CqrsDomainPurityTests` green.
- [x] `src/SharedKernel` still has zero `PackageReference` entries.
- [x] No `decimal` in domain arithmetic — `DomainDecimalTests` green; `OrderFactPayloadMapper` maps `Money.MinorUnits` to `long`.
- [x] Every interaction classifiable as Kafka-fact or NATS-RPC — this feature adds exactly one interaction, the relay publishing facts to `otc.orders.facts.v1`, keyed by `correlationId`. No NATS. `OI16` now makes "only the outbox adapter touches the producer" a red test rather than a convention.
- [x] No stray debug logging, no context-free TODOs — `grep` over every new file returns none. (`quality.sh`'s own coverage-gate TODO is dated and owned by feature 34.)

### C4 — verification is real
- [x] `./quality.sh` passes — format, build, 268 tests, coverage collected. 287.9 s.
- [x] Domain tests are pure — `SharedKernel.UnitTests` and `Orders.UnitTests`' domain cases reference no framework, no DB, no broker.
- [x] Integration tests use Testcontainers against real MS-SQL / Kafka / MongoDB — never a mocked broker. `R14`'s first cycle even uses a real producer against an unreachable broker rather than a stub.
- [ ] Coverage thresholds met (≥80% domain, ≥60% overall) — **not assertable here, and deliberately so.** `quality.sh` reports per-project line rates and does not gate; its own comment and `CLAUDE.md` assign the enforcing gate to feature 34, with the standing instruction not to fake a gate that does not gate. `SharedKernel` reports 90.2%. This box stays open project-wide until feature 34, exactly as it did for features 7–13.
- [x] No Jest anywhere — xUnit throughout; no Node dependency in the backend.

### C5 — the session closed cleanly
- [ ] No suspicious untracked files — **fails on D1**, `src/Orders/Infrastructure/Persistence/OutboxRelay.cs` (0 bytes). Delete before the commit and this box closes.
- [x] `progress/history.md` has an entry for the feature **including its effort record** — appended by this review (§8).
- [x] `feature_list.json` reflects the true state — set to `done` by this review.
- [x] The human has been told what was done and how to test it manually — `progress/impl_outbox_and_idempotency.md` §"Manual verification for the human", four steps, including the `SYSUTCDATETIME()` caution that closes #7's D4 by hand as well as by test.
- [x] Claude did not commit. No `git commit`, no `git push` in this session.

### C6 — spec-driven development (`sdd: true`, applies in full)
- [x] `specs/outbox_and_idempotency/` holds all three of `requirements.md`, `design.md`, `tasks.md` (163 / 636 / 105 lines).
- [x] `requirements.md` uses strict EARS, every requirement carrying an id — `OI1` – `OI16` in THE SYSTEM SHALL / IF-THEN / WHILE / WHEN form, and the shared `R11` – `R18` are **cited, not restated**, which is the correct reuse discipline.
- [x] Every task ticked `[x]` in `tasks.md`, and each verified against its artefact rather than read as ticked.
- [x] Every `R<n>` covered by a concrete named non-vacuous test, recorded in `specs/shared/test-matrix.md` — §2 above, and the non-vacuity of the four load-bearing ones is my own arming table, not the implementer's.
- [~] The spec commit precedes the implementation commit — **cannot be satisfied by any agent here.** Nothing is committed; the spec (`specs/outbox_and_idempotency/`) and the implementation are in one uncommitted working tree. The human must make **two commits, spec first**, to close this box. Carried, as in every prior `sdd` feature.

### C7 — spec-reuse fidelity (assessment #8)
Only the boxes this feature can touch:
- [x] `specs/shared/` still byte-identical to #7's except `test-matrix.md`'s Status column — verified with a real `diff` against `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs`: `test-matrix.md` is the **only** differing file, and a programmatic column-by-column comparison of all 109 table rows shows **no difference in columns 1–4 of any requirement row**. The only col-1-4 differences are the numeric Green/Scoped/Not-yet-green cells of the summary table, which are per-assessment counts by construction.
- [x] The `R<n>` ids are #7's, and the .NET realisation genuinely satisfies the same requirement — walked one row at a time in §2. `OI1` – `OI12` reuse #7's local ids as an explicit claim of the same obligation; `OI13` – `OI16` are new and each names why #7 could not have faced it.
- [x] `progress/history.md` effort record complete and honest, **including that this one was not faster** — §8.
- n/a `n8n/workflows`, the black-box API script, README benchmark section — no Gateway exists yet (feature 15+).

**Matrix discipline.** `git diff specs/shared/test-matrix.md` touches exactly: the Status cell of R11, R12, R13, R14, R15, R17, R18; row 2 of the coverage summary (`8 | 0 | 0 | 8` → `8 | 7 | 0 | 1`); and the Total row (`9 | 1 | 53` → `16 | 1 | 46`). Nothing else — no other requirement row, no column 1–4, no prose. I recomputed the counts from column 5 independently: 63 rows, 16 `DONE`, 1 scoped (`R1`, domain half), 46 `TODO`. The published numbers are correct.

---

## 7. Verdict

**APPROVED.** Feature 14 set `done` in `feature_list.json`; the effort record is appended to `progress/history.md`.

Two conditions on the commit, neither requiring re-review:

1. **Delete `src/Orders/Infrastructure/Persistence/OutboxRelay.cs`** (0 bytes) before staging — D1.
2. **Name every installed package in the commit message**, `Testcontainers` 4.14.0 included — D6.

And, per C6's last box, **commit the spec first and the implementation second**.

---

## 8. Benchmark — what the reuse saved here, and what it could not

**#7's baseline** (`order-to-cash-nestjs/progress/history.md`, feature 14): 1 session, ~2.4 h wall-clock — spec ~30 min, implementation ~60 min, review ~50 min.

**#8**: 1 session, ~3.4 h wall-clock (16:10 → ~19:35 local, bounded by the previous commit `a170a6b` at 16:10 and this review) — spec pass ~16:27–16:52, human gate and the `CLAUDE.md` amendment it produced ~16:52–17:14, implementation 17:19–18:57 (~1 h 40 m, of which the nine-row arming pass was 18:22–18:46), impl report to 19:09, review 19:12–19:35.

**#8 was about 40% slower than #7 on the same feature, and the reasons are specific rather than general.**

*What the reuse saved.* The requirement semantics cost nothing: `specs/shared/requirements.md` R11–R18 was read, not written, and `requirements.md` cites rather than restates it — a discipline that also removed the drift risk. `OI1` – `OI12` were inherited wholesale as ids *and* as obligations, so the design started from twelve settled questions instead of twelve open ones. Most valuably, three of #7's shipped defects were converted into requirements before a line was written: D2 and D7 became `OI14` (publish timeout actually enforced; failure rolls back rather than committing empty) and D10 became `R17`'s mandatory second case — each of which #7 paid a review cycle to discover and #8 got for the price of reading. #7 also correctly predicted `OI12` would be needed and was wrong only about *why*; the four-case parity shape transferred verbatim.

*What the reuse could not touch, and where the time went.* Three things had no counterpart to copy. **The row-claiming statement** — #7's `FOR UPDATE SKIP LOCKED` is one clause; #8's `WITH (UPDLOCK, READPAST, ROWLOCK)` is a hint triple interacting with isolation level, lock escalation and a database option, and the only way to know whether it skips or blocks was to build a two-relay test that measures it. **The `seq`-ordering defect** — `AddRange` + one `SaveChangesAsync` does not assign `IDENTITY` values in Add-call order on this provider, `MaxBatchSize(1)` does not fix it, and since `seq` is this feature's entire publication-order guarantee the writer had to be rewritten to one awaited raw `INSERT` per row. That is a genuine EF Core/SQL Server finding, reproduced in isolation, that #7's Drizzle/MySQL stack never posed. **The fixture isolation gap** was #8's own: six features had been proving concurrency behaviour under an isolation configuration the deployed stack does not use, and no amount of #7 to read would have surfaced it. Add to that the arming protocol itself — two of nine rows needed a *strengthened test* before they armed, both found by watching an "armed" run stay green, which is 25 minutes of work that produces no feature and is the single most valuable thing in the session.

*The honest summary.* Reuse compresses the part of a feature that is **specification and hindsight**, and compresses it a lot — the spec pass was 30 min in #7 and ~25 min here for a strictly better document, because most of it was citation. It does nothing for the part that is **engine-specific behaviour you must measure**, and on this feature that part dominated. The 1 h difference is almost exactly the three items above. A benchmark that showed #8 faster on this feature would have meant the row-claiming question had been assumed rather than answered — and answering it is what closed the trilogy's oldest open technical item.

*Also worth recording for #9:* the two instruments #7 named as portable both transferred and both paid — the pure-text parity guard over committed artefacts, and the four-case shape that lets such a guard exist honestly at n = 1. And #8 adds a third: **the byte-exact/semantic split on wire parity**, where the byte claim is scoped to the relay's *pass-through* (the producer changed nothing) and the payload is compared semantically, because #7's key order is MySQL's `json`-column normalisation and neither #8 nor #9 can or should reproduce it.
