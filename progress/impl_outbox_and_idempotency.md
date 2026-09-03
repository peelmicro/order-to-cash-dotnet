# `impl_outbox_and_idempotency.md` — feature 14 (phase 8)

## What was built

The transactional outbox writer, the MS-SQL row-claiming relay, the idempotent-consumer primitive
(canonical), and everything `tasks.md` groups A–J name, over the real Orders write model:

- **`src/SharedKernel/`** — `IDomainEventEnvelope`, `DomainEventEnvelope.Validate` (the pure R11
  guard), `IncompleteDomainEventEnvelopeError`. `OrderDomainEvent` now declares
  `: IDomainEvent, IDomainEventEnvelope` — additive, the **only** change under `src/Orders/Domain/`.
- **`src/Orders/Application/Ports/`** — `IClock`, `IUnitOfWork`, `IFactPublisher` + `PublishableFact`,
  `IOrderRepository`, `ConsumerName` + `ConsumerNames` + `UnknownConsumerNameError` (this last one
  deliberately NOT under `Domain/` — see "Deviations" below).
- **`src/Orders/Infrastructure/Persistence/`** — `EfCoreUnitOfWork`, `OrderRowMapper`,
  `EfCoreOrderRepository` (with the sequential-raw-INSERT outbox writer — see the `seq`-ordering
  finding below), `SystemClock`.
- **`src/Orders/Infrastructure/Outbox/`** — `OutboxWriter`, `OrderFactPayloadMapper`,
  `OutboxEnvelopeMapper`, `OrdersFactTopic`, `KafkaOptions`, `KafkaFactPublisher`, `OutboxRelay` +
  `IOutboxRelay` (the OI6 test seam), `OutboxRelayOptions`, `OutboxRelayBackgroundService`.
- **`src/Orders/Infrastructure/Messaging/`** — `ProcessedEventLedger` and `IdempotentConsumer`
  (CANONICAL — banner + parity-guarded).
- **`src/Orders/Infrastructure/OrdersOutboxServiceCollectionExtensions.cs`** +
  `OrdersOutboxOptions.cs` — `AddOrdersOutbox`, one explicit line per port.
- **One architecture rule** — `FactPublisherConfinementTests.cs` (OI16).
- **Task group B** — the fixture isolation-level fix, applied to Orders and, after confirming their
  suites stayed green unchanged, to Fulfillment, Billing and Notifications too.
- **~24 new/extended test files**, listed in full below.

## Requirement → test mapping

| Id | Test(s) |
|---|---|
| R11 | `tests/SharedKernel.UnitTests/DomainEventEnvelopeTests.cs` (9 field/pattern cases); `tests/Orders.IntegrationTests/OutboxEnvelopeTests.cs` › `R11_Outbox_RefusesToStoreAFactWhoseEventTypeIsNotInTheDeclaredFactCatalogue`; `tests/Orders.IntegrationTests/OutboxWireParityTests.cs` › `R11_PublishedEnvelope_CarriesTheSevenFieldsInTheDeclaredOrderWithNoneAbsentNullOrEmpty` |
| R12 | `OutboxEnvelopeTests.cs` › `R12_Outbox_StampsEveryFactOfOneOrderWithTheOrderIdAsCorrelationIdAndTheCausingEventIdAsCausationId` |
| R13 | `OutboxAtomicityTests.cs` › `R13_UnitOfWork_PersistsNeitherTheAggregateNorTheOutboxRecordAndPublishesNothingWhenTheTransactionFails`, `R13_UnitOfWork_RollsBackAnOutboxRowAlreadyWrittenWhenTheAggregatesOwnSaveFailsAfterwards` (the second case exists because the first alone did not discriminate arming row 2 — see below) |
| R14 | `OutboxRelayTests.cs` › `R14_Relay_StampsARecordOnlyAfterTheBrokerAcknowledgementAndRepublishesAnUnstampedRecordOnTheNextPoll` |
| R15 | `FactPartitioningTests.cs` › `R15_FactStream_DeliversAllFactsProducedByOneContextAboutOneOrderToConsumersInEmissionOrder` |
| R16 | Deferred to feature 27, per `requirements.md` §1.2. Not re-opened. |
| R17 | `IdempotentConsumerTests.cs` › `R17_IdempotentConsumer_RecordsTheEventIdAndConsumerNameInTheSameTransactionAsTheStateChangeAndTheOutboxRecords`, `R17_IdempotentConsumer_LeavesNoDedupRowWhenAFailureInsideWorkRollsBackTheWholeTransaction` |
| R18 | `IdempotentConsumerTests.cs` › `R18_IdempotentConsumer_AcknowledgesARedeliveredFactWithoutMutatingStateEmittingAFactOrIssuingACommand` |
| OI1 | `OutboxEnvelopeTests.cs` › `OI1_Relay_ReconstructsTheCompleteEnvelopeFromTheStoredRecordAloneInferringNoFieldAtPublicationTime`, `OI1_Writer_RefusesAnEventWithAnEmptyCausationIdBeforeAnyRowIsBuilt` |
| OI2 | `OutboxRelayTests.cs` › `OI2_Relay_PublishesTwoRecordsWrittenByOneTransactionInAppendOrderAlthoughBothCarryTheSameOccurredAt`, `OI2_Relay_OrdersTheClaimBySeqNeverByOccurredAtEvenWhenTheyDisagree` (added while arming row 7 — see below) |
| OI3 | `OutboxRelayTests.cs` › `OI3_Relay_PublishesALowerSequenceRecordThatCommittedAfterAHigherSequenceRecordWasAlreadyPublished` |
| OI4 | `OutboxRelayConcurrencyTests.cs` › `OI4_TwoConcurrentRelays_GrantDisjointBatchesAndPublishEveryRecordExactlyOnce` |
| OI5 | `OutboxRelayConcurrencyTests.cs` › `OI5_Relay_ReturnsRecordsClaimedByARelayThatDiedBeforeStampingToTheNextPollWithoutALeaseWait` |
| OI6 | `OutboxRelayLoopTests.cs` › `OI6_RelayLoop_NeverStartsASecondPollCycleWhileOneIsStillInProgress`, `OI6_RelayLoop_StopAsyncWaitsForTheInFlightCycleToFinish` |
| OI7 | `KafkaFactPublisherConfigTests.cs` › `OI7_Producer_IsConfiguredSoAnInternalRetryCanNeitherReorderNorDuplicateAPartitionsRecords` |
| OI8 | `OutboxRelayTests.cs` › `OI8_Relay_LeavesEveryRecordOfARejectedBatchUnstampedAndRepublishesTheSameRecordsOnTheNextPoll` |
| OI9 | `OutboxAtomicityTests.cs` › `OI9_Repository_ProducesExactlyOneOutboxRecordPerFactWhenTheOperationIsRetriedAfterARolledBackUnitOfWork` |
| OI10 | `IdempotentConsumerTests.cs` › `OI10_IdempotentConsumer_AppliesTheHandlersEffectsOnceWhenTheSameEventIsDeliveredConcurrentlyToTwoConsumers` |
| OI11 | `tests/Billing.IntegrationTests/ReliabilityTableParityTests.cs` › `Outbox_And_ProcessedEvents_Are_Defined_Identically_Across_All_Four_DbContexts` — inherited satisfied (feature 11), re-run green under this feature's fixture change |
| OI12 | `tests/Orders.UnitTests/IdempotentConsumerParityTests.cs` — 4 cases |
| OI13 | `OutboxRelayConcurrencyTests.cs` › `OI13_Claim_SkipsRowsHeldByAnotherRelayAndBehavesIdenticallyWithRowVersioningEnabledOnTheDatabase` |
| OI14 | `OutboxRelayTests.cs` › `OI14_Relay_AbandonsAPublishThatExceedsTheTimeoutRollsTheClaimBackAndRepublishesOnTheNextPoll` |
| OI15 | `OutboxWireParityTests.cs` › `OI15_Relay_PublishesBytesIdenticalToTheGoldenEnvelopeCapturedFromNumber7`, `OI15_Recorder_WritesAPayloadSemanticallyEqualToNumber7sForTheSameBusinessInputs` |
| OI16 | `tests/Architecture.Tests/FactPublisherConfinementTests.cs` › `OnlyTheOutboxAdapterMayReferenceTheFactStreamProducerClient` |

`specs/shared/test-matrix.md` column 5 flipped for R11–R15, R17, R18 (R16 stays TODO); coverage
summary counts updated to Green 16 / Scoped 1 / Not-yet-green 46 (Total 63; row 2's own
Rows/Green/Scoped/Not-yet-green is 8/7/0/1). `specs/outbox_and_idempotency/requirements.md` §3
flipped OI1–OI16 to DONE.

## The two-relay measurement — OI13, the "genuine unknown"

Measured on this stack, `READ_COMMITTED_SNAPSHOT ON` (per task group B's fixture fix), against a
real MS-SQL container:

- **The claim genuinely SKIPS, it does not block.** `OI13_Claim_SkipsRowsHeldByAnotherRelayAndBehavesIdenticallyWithRowVersioningEnabledOnTheDatabase`
  holds one row's claim open in a real `OutboxRelay` (a fake publisher that blocks forever), and a
  second real `OutboxRelay` claims the *other* row. **Measured: 117ms** for the second relay's whole
  `RunOnceAsync()` to return (claim + stamp + commit), against a 3-second assertion bound. This is
  the item the stack-comparison document has carried open since Phase 1: **`WITH (UPDLOCK, READPAST, ROWLOCK)` on this MS-SQL image, under RCSI, behaves like `SKIP LOCKED` — it skips, it does not block.**
- **Confirmed by the reverse experiment.** Arming row 6 (deleting `READPAST` from the claim SQL)
  turned the SAME test into an indefinite hang: `timeout 25 dotnet test ... ` exited with code 124
  (killed by the timeout) rather than completing, because the second relay's claim genuinely
  **blocked** waiting for the first relay's `UPDLOCK` to release — which never happens during the
  test (the holder is only released at the very end, via cancellation). This is the "different
  diagnosis" `tasks.md` J1 row 6 asks for: **the un-`READPAST`ed claim fails by blocking, not by
  duplicate publication** — no lock-wait-timeout error surfaced because MS-SQL's default
  `lock_timeout` is `-1` (infinite); the process hangs rather than erroring.
- **The §5.2 fallback (claim `id`/`seq` through the covering index, then read by `id` without
  `READPAST`) was NOT needed.** Confirmed both by the timing measurement above and by capturing the
  real query plan (task F4, below): it is exactly the two-lock shape design.md §5.2 named as the
  *expected* good case (index seek + key lookup, no lock escalation, no page-level lock).
- **`OI4`'s two-concurrent-relay test also holds**: disjoint batches, union = every record,
  intersection = empty, over 20 rows.
- **`OI5`'s crash-recovery test holds**: a claim abandoned by dropping the connection (transaction
  disposed without commit) is claimable again on the very next poll, no lease, no sweep, measured at
  well under a second.

**Surprise relative to the design's own framing.** The design correctly predicted the *outcome*
(skip, not block) but its own two exemplar tests as first-drafted (`OI4`, an early `OI13` draft) did
NOT actually exercise the production `OutboxRelay` class for the side being measured — an early
`OI13` draft reimplemented the claim as inline SQL for BOTH transactions, which meant deleting
`READPAST` from `OutboxRelay.cs` would not have failed the test at all (a genuine "guard that does
not guard," caught by literally arming it and watching it stay green). `OI13` was rewritten so BOTH
transactions run through the real `OutboxRelay.RunOnceAsync()` — see "Deviations" below.

## The claim SQL — as EF emitted it, and its query plan (task F4)

Captured via `Microsoft.EntityFrameworkCore.Database.Command` logging at `Information`, against a
real (fresh, migrated) MS-SQL container:

```sql

            SELECT TOP (100)
                   id, event_id, event_type, aggregate_id, correlation_id, causation_id,
                   payload, occurred_at, published_at, created_at, seq, trace_parent
            FROM   dbo.outbox WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE  published_at IS NULL
            ORDER  BY seq ASC
```

`SET SHOWPLAN_TEXT ON` against the identical statement, same container, produced:

```
  |--Top(TOP EXPRESSION:((100)))
       |--Nested Loops(Inner Join, OUTER REFERENCES:([...].[dbo].[outbox].[id]))
            |--Index Seek(OBJECT:([...].[dbo].[outbox].[IX_outbox_published_at_seq]), SEEK:([...].[dbo].[outbox].[published_at]=NULL) ORDERED FORWARD)
            |--Clustered Index Seek(OBJECT:([...].[dbo].[outbox].[PK_outbox]), SEEK:([...].[dbo].[outbox].[id]=[...].[dbo].[outbox].[id]) LOOKUP ORDERED FORWARD)
```

Exactly the plan design.md §5.2 predicted: an **index seek** on the poll index `(published_at, seq)`
plus a **key lookup** (clustered index seek by `id`) — no table/page scan, no lock escalation
signal. **The §5.2 fallback (claim `id`/`seq` only through the covering index, then a second,
`READPAST`-free read by `id`) was not needed**, confirmed both by this plan and by the 117ms OI13
measurement.

## Wire parity — against the golden envelopes

- **`OI15`, byte-exact case**: an `outbox` row built from `tests/Contracts.UnitTests/GoldenEnvelopes/order_placed_v1.json`'s own columns (payload text copied verbatim via `JsonElement.GetRawText()`), republished through the real relay and a real broker, consumed, and compared **byte for byte** against the golden file's raw bytes. **Passed** — the relay's `JsonElement`-based pass-through (never re-parsed into a typed payload) reproduces #7's exact bytes, including its MySQL-`json`-column key ordering, because it never touches that ordering — it republishes whatever `nvarchar(max)` held.
- **`OI15`, semantic case**: a real `Order.Place(...)` carrying the golden file's own business values (`ORD-000011`, `LeroyMerlinEs`/`PORTOTOOLS`, GLNs `5400000000034`/`5400000000386` — both independently verified against the GS1 mod-10 check digit before use), through the real repository; the stored `payload` column compared **semantically** (own duplicate of `JsonEquivalence`'s rule — see "Deviations") against the golden payload. **Passed.**
- **`R11` at the producer**: a fact the aggregate really placed, published through a real broker, consumed, and asserted for all seven fields, order, and the two headers (`x-event-type`, `content-type`) with **no `traceparent`** present. **Passed.**

## The `seq`-ordering finding — a real EF Core / SQL Server defect, found and fixed within this feature

While driving `OI2`'s two-rows-one-transaction case, **`db.OutboxMessages.AddRange(rows)` followed by
one `SaveChangesAsync()` did NOT preserve C# list order in the assigned `seq` (`IDENTITY(1,1)`)
values.** Reproduced in isolation (a five-row `AddRange`, `Guid` client-generated PK + `IDENTITY`
secondary column): the identity values came back scrambled relative to insertion order — `Seq=3,2,5,1,4`
for `item-1..5`. **`MaxBatchSize(1)` did NOT fix it** (still scrambled — `Seq=9,6,10,8,7` for a fresh
five-row `AddRange`), which rules out multi-row-INSERT/`OUTPUT`-clause reordering as the sole cause;
only issuing each row as its own tracked `Add()` + its own `SaveChangesAsync()` call, sequentially
and awaited, preserved order.

Since `seq` is this feature's **entire** publication-order guarantee (R12, OI2, and the
`payment.received.v1`/`credit.released.v1` ordering promise design.md §4.4 explicitly hands to
feature 22), this was not acceptable to document and move past — it was fixed in the actual writer:
**`EfCoreOrderRepository.InsertOutboxRowAsync` now issues one raw, parameterised, awaited
`INSERT ... VALUES (...)` per outbox row** (never tracked, never batched with any other row), inside
the same ambient transaction `IUnitOfWork` opened. This makes SQL Server's own IDENTITY counter — which
does increment in statement-execution order — the only thing `seq` depends on. Verified fixed via the
same reproduction harness (five sequential single-row inserts through the fix's shape: `Seq=11..15`
in order, every time).

**Consequence for arming row 2 and for the R13 test suite**: since a duplicate-key conflict on the
outbox row's raw `INSERT` now surfaces as `Microsoft.Data.SqlClient.SqlException` (not
`DbUpdateException`, which is specific to EF's *tracked* `SaveChanges`), the original `R13` test was
updated to expect `SqlException`, and a **second** `R13` case was added
(`R13_UnitOfWork_RollsBackAnOutboxRowAlreadyWrittenWhenTheAggregatesOwnSaveFailsAfterwards`) because
the first case's conflict always occurs on the very FIRST write attempted, which cannot distinguish
"rolled back correctly" from "wrote then leaked outside the transaction" — both look like zero rows.
The second case forces the conflict on the *aggregate's own* `SaveChangesAsync()`, which runs *after*
the outbox row's own (successful, no-conflict) insert, so a transaction-escape defect would leave the
outbox row behind while the order never lands. This is exactly the discriminator arming row 2 needed,
and armed correctly the first time only after this rewrite (see the arming table).

## The nine-row arming table (design.md §9.4 / task J1)

Protocol: back up by `cp` (never `git checkout --`, these files are untracked mid-flight), mutate,
`touch` the file, `dotnet build --no-incremental`, run the SPECIFIC named test, record the message
verbatim, restore from the backup, `touch` + rebuild again, confirm green.

| # | Branch | Armed by | Named test | Verbatim failure (armed) |
|---|---|---|---|---|
| 1 | drain writes one row per event | `OutboxWriter.BuildRows`'s loop iterates `Array.Empty<IDomainEvent>()` instead of `domainEvents` | `OutboxAtomicityTests` › `OI9_Repository_...` | `Assert.Throws() Failure: No exception was thrown` (`Expected: typeof(Microsoft.Data.SqlClient.SqlException)`) — the pre-seeded conflicting row never collides because zero outbox rows are ever built |
| 2 | outbox insert stays inside the transaction | `EfCoreOrderRepository.InsertOutboxRowAsync` rewritten to open a BRAND-NEW `SqlConnection` and INSERT on it directly (bypassing the ambient transaction entirely) | `OutboxAtomicityTests` › `R13_UnitOfWork_RollsBackAnOutboxRowAlreadyWrittenWhenTheAggregatesOwnSaveFailsAfterwards` | `Assert.Equal() Failure: Values differ` — `Expected: 1`, `Actual: 2` (the escaped outbox row survived the rollback) — **the first R13 case was tried first and did NOT fire** (see the finding above); this second case is what actually discriminates it |
| 3 | `ClearDomainEvents()` runs after the save | the clear loop moved to the very top of `EfCoreOrderRepository.SaveChangesAsync`, before the outbox insert loop | `OutboxAtomicityTests` › `OI9_Repository_...` | `Assert.Throws() Failure: No exception was thrown` (`Expected: typeof(Microsoft.Data.SqlClient.SqlException)`) — clearing early empties `DomainEvents` before `BuildRows` ever runs, so the pre-seeded conflict is never reached and the FIRST (meant-to-fail) attempt silently "succeeds" with zero outbox rows |
| 4 | the envelope guard runs before a row is built | `DomainEventEnvelope.Validate(orderEvent);` commented out in `OutboxWriter` | `OutboxEnvelopeTests` › `OI1_Writer_RefusesAnEventWithAnEmptyCausationIdBeforeAnyRowIsBuilt` | `Assert.Throws() Failure: No exception was thrown` (`Expected: typeof(OrderToCash.SharedKernel.Errors.IncompleteDomainEventEnvelopeError)`) |
| 5 | stamp happens after acknowledgement | `OutboxRelay`: the `ExecuteUpdateAsync` stamp moved above `PublishAsync`, and the failure branch changed to `CommitAsync` instead of `RollbackAsync` | `OutboxRelayTests` › `OI8_Relay_...`, `OI14_Relay_...`, `R14_Relay_...` (all three fired) | `Assert.Null() Failure: Value of type 'Nullable<DateTime>' has a value` (in all three cases — a row got stamped despite the publish never (successfully) acknowledging) |
| 6 | claim skips rows another relay holds | `WITH (UPDLOCK, READPAST, ROWLOCK)` → `WITH (UPDLOCK, ROWLOCK)` in `OutboxRelay.cs` | `OutboxRelayConcurrencyTests` › `OI13_Claim_...` | **Blocked, did not fail with a message.** `timeout 25 dotnet test ... --filter OI13_Claim` exited with process-killed code `124` — the second relay's `RunOnceAsync()` never returned within 25s because its claim was genuinely waiting on the first relay's `UPDLOCK` (which is held for the whole test). This is the "different diagnosis" the design asked to record: **blocking, not duplicate publication**, and no SQL lock-wait-timeout error surfaced because MS-SQL's `lock_timeout` defaults to `-1` (infinite) |
| 7 | claim orders by `seq` | `ORDER BY seq ASC` → `ORDER BY occurred_at ASC` in `OutboxRelay.cs` | `OutboxRelayTests` › `OI2_Relay_OrdersTheClaimBySeqNeverByOccurredAtEvenWhenTheyDisagree` (added — see below) | `Assert.Equal() Failure: Collections differ` — `Expected: [bbe5..., 3a85...]`, `Actual: [3a85..., bbe5...]` — **the original `OI2` case (tied `occurredAt`) did NOT fire**, because SQL Server has no obligation to break an `ORDER BY occurred_at` tie any particular way, and it happened to preserve physical/insertion order anyway on this run; a second case with genuinely conflicting `seq`-vs-`occurredAt` order was written and IS the one that discriminates |
| 8 | dedup insert precedes `work`, same transaction | `IdempotentConsumer.RunOnceAsync` rewritten to commit the dedup insert in its OWN `unitOfWork.ExecuteAsync` call, then run `work` in a SECOND, separate call | `IdempotentConsumerTests` › `R17_IdempotentConsumer_LeavesNoDedupRowWhenAFailureInsideWorkRollsBackTheWholeTransaction` | `Assert.Equal() Failure: Values differ` — `Expected: 0`, `Actual: 1` — the dedup row survived a failure inside `work`, exactly the #7 D10-class defect this case exists to catch |
| 9 | producer is idempotent | `EnableIdempotence = true` → `false` in `KafkaFactPublisher.BuildProducerConfig` | `KafkaFactPublisherConfigTests` › `OI7_Producer_...` | `Assert.True() Failure` — `Expected: True`, `Actual: False` |

All nine rows restored from their `cp` backups, forced-rebuilt, and re-confirmed green (verified via
`diff` against the backup showing byte-identical restoration in every case, followed by
`dotnet build --no-incremental` and a green run of the affected test file).

**Two rows (2 and 7) needed a strengthened or additional test before they armed correctly** — found
by literally watching the "armed" run stay green, exactly the failure mode CLAUDE.md's arming
protocol exists to catch. Both are recorded above with what the *original* case's blind spot was, not
just the eventual passing shape.

## `init.sh` / `quality.sh`

- `./init.sh` exits 0 (environment, harness files, backlog coherence, superseded-rules sweep,
  repository state all `[OK]`; the only `[WARN]`s are the expected "uncommitted mid-session" and
  "run quality.sh yourself" notices).
- `./quality.sh` — **full green**: `dotnet format --verify-no-changes` clean; `dotnet build` (whole
  solution) succeeded; `dotnet test` (whole solution, all 10 test projects, real MS-SQL/Kafka/Mongo
  containers throughout) — **all tests passed**, 0 failures. Coverage is collected and reported per
  test project (feature 34 has not wired the enforcing gate yet, per `quality.sh`'s own `TODO` and
  CLAUDE.md's "do not fake a gate that does not gate"). Selected numbers from this run:
  `OrderToCash.SharedKernel` package line-rate 90.2% (covers the new `DomainEventEnvelope` guard);
  `OrderToCash.Orders` package line-rate ranged 23.2% (an isolated unit-test-only report) to 85.9%
  (a report combining unit + integration coverage of the Orders assembly) across the several coverlet
  reports that reference it (multiple test projects reference `Orders.csproj` transitively, e.g. via
  `Seed.csproj`, so more than one report carries an `OrderToCash.Orders` package entry).

## Deviations from `design.md`, each with its reason

1. **`EfCoreOrderRepository.InsertOutboxRowAsync` uses raw, sequential, per-row `INSERT` statements
   instead of `db.OutboxMessages.AddRange(...)` + one shared `SaveChangesAsync()`.** Design's own
   prose says "calls `DbContext.SaveChangesAsync` once" for the aggregate's rows — that half is
   unchanged (still exactly one call, for `Order`/`OrderItem`). The outbox rows are the part that
   changed, and the reason is the `seq`-ordering defect documented above: it is real, reproduced in
   isolation, not fixable by `MaxBatchSize`, and directly threatens R12/OI2's core guarantee. This is
   the single largest deviation in this feature and the one most worth a reviewer's attention.
2. **`OI13`'s test was rewritten to drive BOTH sides through the real `OutboxRelay` class** rather
   than reimplementing the claim as inline SQL for the "holder" transaction, once the inline-SQL
   version was shown (by arming row 6) not to discriminate the very defect it exists to catch. The
   holder transaction is now a real `OutboxRelay.RunOnceAsync()` call with a publisher whose
   `PublishAsync` blocks forever, released via cancellation at the end of the test — exercising
   design.md §5.4's "genuine outer cancellation propagates, disposing the transaction without a
   commit" path as a side effect.
3. **`OI2` gained a second case** (`OI2_Relay_OrdersTheClaimBySeqNeverByOccurredAtEvenWhenTheyDisagree`)
   for the same reason: the original case's tied `occurredAt` values gave SQL Server no obligation to
   break the tie any particular way, so it did not reliably discriminate `ORDER BY seq` from
   `ORDER BY occurred_at`. The original case is kept (it is still a real, valid property — "order
   survives even with tied timestamps") and the new one adds the genuinely-conflicting-order case.
4. **`R13` gained a second case** for the same class of reason — see the `seq`-ordering finding above.
5. **`UnknownConsumerNameError` lives in `Application/Ports/`, not `Domain/Errors/`.** `ConsumerName`
   is an application-layer concept this feature introduces (design.md §6.1's own placement,
   `Orders/Application/Ports/ConsumerName.cs`), not part of the `Order` aggregate's vocabulary, and
   design.md §1 fixes that `src/Orders/Domain/` gains nothing beyond `OrderDomainEvent`'s one line —
   task J4's own check (`git diff --stat` showing exactly one changed `Domain/` file) would have
   failed had this error lived under `Domain/Errors/`.
6. **Task H6 (`TheRelayFindsNoUnpublishedRecordInAnySeededWriteModel`) proves the ORDERS half through
   the real `OutboxRelay` class, and the Fulfillment/Billing halves by direct query** (`COUNT(*) WHERE
   published_at IS NULL = 0` against their own `OutboxMessages` DbSets), not by constructing
   Fulfillment/Billing relay instances. `OutboxRelay` is typed to `OrdersDbContext` specifically
   (design.md §5.1's own signature), and building per-service relay adapters is explicitly features
   17–22's job (design.md §11's non-goals: "No Fulfillment, Billing, Notifications or Projector
   code"). The invariant this task cares about — the seed's pre-published rows agree with what a
   relay would find — is still proven for all three write models; only the MECHANISM differs for two
   of them, and the reason is recorded in the test's own remarks.
7. **`JsonEquivalence`'s semantic-comparison rule is duplicated into `Orders.IntegrationTests`**
   rather than referenced from `tests/Contracts.UnitTests/JsonEquivalence.cs`, because that class is
   `internal` to its own project and neither adding `InternalsVisibleTo` nor making it `public` is a
   change `tasks.md` names as in-scope for this feature (`Contracts.UnitTests` is not listed).
   Precedent: `RepositoryPaths` is already duplicated per test project in this repository rather than
   shared, for the identical reason (no shared test-helper project exists).
8. **Task B2's assertion uses `sys.databases.is_read_committed_snapshot_on` instead of
   `DATABASEPROPERTYEX(DB_NAME(), 'IsReadCommittedSnapshotOn')`**, which `tasks.md`'s own wording
   names. Probed directly: `DATABASEPROPERTYEX` returned `DBNull` on this MS-SQL image both from
   `master` (by literal database name) and from a connection scoped to the database itself
   (`DB_NAME()`) — reproduced in an isolated probe before changing the test, not guessed at.
   `sys.databases` returns the real value (`True`) reliably. Recorded here as the honest reason for
   the substitution, not silently swapped.
9. **`OutboxRelayLoopTests`/`OrdersOutboxRegistrationTests` (H2/H4) needed `IOutboxRelay`**, a small
   interface `OutboxRelay` implements, added to `OutboxRelay.cs` — not named explicitly in
   design.md's §5.1 shape, but required to satisfy design.md's OWN testing table's claim that
   `OutboxRelayLoopTests.cs` is `unit | none` (no database, no host): `OutboxRelayBackgroundService`
   resolves `IOutboxRelay` from its DI scope rather than the concrete class, letting a fake with
   controllable timing prove the loop's own re-entry/drain behaviour with zero infrastructure.

## Testcontainers / Kafka — task 9.3's decision, evidenced

`Testcontainers.Kafka`'s `KafkaBuilder` was tried first, exactly as design.md §9.3 instructs, against
`apache/kafka:4.3.1` (the SAME pinned tag `docker-compose.infra.yml` uses). **It failed**: the
container exited with `Exception in thread "main" org.apache.kafka.common.config.ConfigException:
Configuration 'advertised.listeners' values must not be empty` — `Testcontainers.Kafka` targets the
Confluent image family's own configurator, which never populated `KAFKA_ADVERTISED_LISTENERS` for
this non-Confluent image. The generic `Testcontainers.ContainerBuilder`, configured with the SAME
KRaft environment shape `docker-compose.infra.yml`'s `kafka` service uses (plus a dynamically-picked,
fixed host-port binding so `KAFKA_ADVERTISED_LISTENERS` can be computed before `Build()`), drove it
correctly — verified with a real produce/consume round trip before writing `KafkaContainerFixture.cs`.
**`Testcontainers.Kafka` is not referenced anywhere in this solution** (`Directory.Packages.props` has
no `PackageVersion` for it; only comments mention its name, explaining why it was tried and rejected)
— confirmed by `grep` across `Directory.Packages.props` and every `.csproj` in the solution.

## Manual verification for the human

1. Bring the stack up: `docker compose -f docker-compose.infra.yml up -d` (or however the project's
   own compose invocation is scripted), and confirm `otc-kafka`, `otc-mssql` (or the compose service
   names in use) and Redpanda Console are healthy.
2. There is no host yet (feature 15 builds `Program.cs`), so there is nothing today that runs
   `OutboxRelayBackgroundService` against the seeded `otc_orders` database automatically. The
   equivalent check this feature proves in-process is `tests/Seed.IntegrationTests/SeedIntegrationTests.cs`
   › `TheRelayFindsNoUnpublishedRecordInAnySeededWriteModel` — run it
   (`dotnet test tests/Seed.IntegrationTests --filter TheRelayFindsNoUnpublishedRecordInAnySeededWriteModel`)
   and confirm it reports `Claimed = 0, Published = 0` and the fake publisher was never called,
   against a freshly seeded database.
3. To see a real end-to-end publish by hand: connect to the compose stack's MS-SQL instance, `otc_orders`
   database, and insert one row directly:

   ```sql
   INSERT INTO dbo.outbox (id, event_id, event_type, aggregate_id, correlation_id, causation_id,
                            payload, occurred_at, published_at, created_at, trace_parent)
   VALUES (NEWID(), NEWID(), 'order.placed.v1', NEWID(), NEWID(), NEWID(),
           N'{"orderReference":"ORD-999999","retailerCode":"TEST","companyCode":"TEST","currency":"EUR","lines":[],"initialAmount":0,"initialDiscount":0,"totalAmount":0}',
           SYSUTCDATETIME(), NULL, SYSUTCDATETIME(), NULL);
   ```

   **`SYSUTCDATETIME()`, never a local-time function** — #7 shipped a two-hour-future `occurredAt`
   this exact way, and it is worth repeating the caution rather than repeating the mistake.
4. Since no host exists yet, run `OutboxRelay.RunOnceAsync()` once against that database from a
   throwaway console app or the debugger, wired via `AddOrdersOutbox` (or directly, as the tests do),
   pointed at the real Kafka bootstrap servers. Watch Redpanda Console's `otc.orders.facts.v1` topic:
   the message should appear keyed by the row's `correlation_id`, with headers `x-event-type:
   order.placed.v1` and `content-type: application/json`, and **no `traceparent` header** — confirm
   that gap is visibly absent rather than silently missing.

## Every task confirmed genuinely done

Every checkbox in `specs/outbox_and_idempotency/tasks.md` is ticked. None was ticked without the file
existing, building, and (for test tasks) the named test running green in this session — the whole
solution's `dotnet test` pass (via `quality.sh`) is the final cross-check, run after every arming
restore, and it is green.

## What surprised me

- **The `seq`-ordering defect** (above) — genuinely unexpected, reproduced independently of any
  design.md hint, and directly threatened a requirement this feature exists to prove.
- **The row-claiming statement behaved EXACTLY as design.md hoped**, which is its own kind of
  surprise given how much of §5.2's prose is hedged ("if the two-lock path is ever observed to
  block..."). It did not block; the fallback was not needed; the plan matched the prediction exactly.
- **`DATABASEPROPERTYEX` returning `DBNull`** on this specific image/setup was unexpected and easy to
  miss without probing it directly first (the task's own named assertion would have quietly never
  proven anything — an `InvalidCastException` on `(int)` would have failed loudly at first run, which
  is what actually caught it here, but a `(int?)` cast would have silently passed a null-vs-1
  comparison wrong).
- **Two arming rows (2 and 7) needed strengthening before they fired** — the single most valuable
  thing the protocol did this session, since both defects were real (the transaction-escape shape row
  2 catches, and the tie-breaking non-guarantee row 7 catches) and both were completely invisible
  without literally watching the "armed" run stay green.
