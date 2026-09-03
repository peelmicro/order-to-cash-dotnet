# `outbox_and_idempotency` — Requirements (feature 14, phase 8, `sdd: true`)

> **This is the reuse run.** `specs/shared/requirements.md` was copied verbatim from #7 and is **read-only**. Nothing below rewords, reinterprets or extends a shared requirement; this file **cites** the shared ids this feature realises and adds only what the shared specification genuinely leaves open. The value of this feature's spec is in [`design.md`](./design.md), which is stack-specific and therefore was not inherited from anywhere.

## 1. Requirements implemented from the shared specification

This feature realises the shared EARS range **`R11` – `R18`** — [`specs/shared/requirements.md`](../shared/requirements.md) §2 `outbox_and_idempotency` — **except `R16`**, whose deferral is argued in §1.2 below and must be ratified at the human gate.

The shared wordings are **not restated here**. `specs/shared/requirements.md` is the single authority for them, is reused verbatim by #7, #8 and #9, and a copy in this file would drift the moment either document is touched. Read them there, together with the envelope contract they cite in [`specs/shared/domain-model.md`](../shared/domain-model.md) §7.1, the three idempotency layers in [`specs/shared/saga.md`](../shared/saga.md) §6, and the `Envelope` / `FactHeaders` / `DeadLetterHeaders` schemas and topic bindings in [`specs/shared/asyncapi.yaml`](../shared/asyncapi.yaml).

**Reusing an id is a claim.** Each row below asserts that the .NET realisation satisfies the same requirement #7's does. Where the mechanism differs because the engine or the language differs, the row says so and points at the section of `design.md` that argues it.

| Shared id | One-line reminder (authority is the shared file) | Realised in #8 by | `design.md` |
|---|---|---|---|
| **R11** | Complete envelope on every fact; no field absent, null or empty; `eventType` matches `<aggregate>.<fact>.v<n>` | **Not yet true in #8** — see §1.1. A pure `DomainEventEnvelope.Validate` guard lands in `src/SharedKernel`, `OrderDomainEvent` implements the interface it validates, and the outbox writer calls it before a row is inserted, so an incomplete envelope cannot reach storage, let alone the wire | §4.4, §4.7 |
| **R12** | `correlationId` = order id; `causationId` = the causing fact's `eventId` or the causing command's id | The aggregate already mints both (feature 13, `specs/orders_aggregate/design.md` §7.1) and the `causation_id` column already exists (feature 9). This feature is where the two are joined: the writer copies all five identity fields into columns and the relay reconstructs the envelope from those columns alone | §4.4, §5.5 |
| **R13** | Aggregate state and outbox records commit in **one** transaction, or neither | `IUnitOfWork` opening one `IDbContextTransaction` over the scoped `OrdersDbContext`, with `EfCoreOrderRepository.SaveChangesAsync` draining `Order.DomainEvents` into `outbox` rows inside it — the contract `specs/orders_aggregate/design.md` §7.5 already fixed for this feature | §4.1 – §4.5 |
| **R14** | Only the relay publishes; a record is stamped only after the broker acknowledges; an unstamped record is republished on a later poll | `OutboxRelay.RunOnceAsync()` — claim, publish, stamp-after-acknowledgement, one write-model transaction per cycle. "No command handler, aggregate or domain service publishes directly" additionally becomes an **architecture test** (`OI16`), not a convention | §5.1 – §5.4, §10 |
| **R15** | `correlationId` is the fact-stream partition key, giving per-order ordering | `KafkaFactPublisher` keys every record by the row's `correlation_id` rendered exactly as #7 renders it, with the idempotent producer enabled so a client-internal retry can neither reorder nor duplicate a partition (`OI7`) | §5.3 |
| **R16** | Consumer retry with backoff, then `<topic>.dlq` carrying consumer/attempts/error, then acknowledge the original | **Deferred to feature 27** `observability_reliability` — §1.2. This feature builds no fact-stream consumer, so there is nothing here for a retry wrapper to wrap | §7 |
| **R17** | (`eventId`, consumer) recorded in the **same transaction** as every state change and every outbox record the processing produced | `IdempotentConsumer.RunOnceAsync(eventId, consumer, work)` writing `processed_events` inside the same `IUnitOfWork` scope as the handler's effects | §6.1, §6.2 |
| **R18** | A redelivery is acknowledged with no mutation, no fact and no command | The duplicate branch of `RunOnceAsync`, which detects the unique-index violation on `(event_id, consumer)`, rolls back and returns `Duplicate` **without invoking the work delegate at all** | §6.1 |

### 1.1 `R11` is unowned in #8, and this feature closes it — a #8 gap, not a spec change

#7 could record `R11` as *"already DONE at kernel level"*: its `packages/shared-kernel` shipped `assertValidDomainEventEnvelope` in its feature 7, so its feature 14 inherited a green row. **#8's feature 7 (`shared_kernel`) shipped no equivalent** — `src/SharedKernel/IDomainEvent.cs` is a bare marker interface with no envelope fields and no validation, and `specs/shared/test-matrix.md`'s `R11` row is still `TODO` in this repository.

`R11` therefore has **no other owner**: `shared_kernel` is `done`, and `R11` sits inside the `R11` – `R18` block this feature owns. Matrix rule 3 (*"a feature cannot be marked `done` until every one of its rows is green"*) would otherwise be discharged here by a scoped row with nobody to hand it to, which is precisely the abuse that rule names. So this feature closes it, at the level the matrix's `R11` row demands (**domain unit**), by adding a pure guard to `SharedKernel` — no package reference, no framework, no store — and calling it from the outbox writer so the guard is live rather than ornamental.

Nothing in `specs/shared/` changes. The requirement is #7's, the id is #7's, the level is #7's, and the only difference is which #8 feature pays for it.

### 1.2 The `R16` deferral — argued, and needing ratification at the gate

**The leader's brief for this feature lists `R16` among the requirements it owns. This spec proposes to defer it, and flags the disagreement rather than resolving it silently.**

The reasons, in order of weight:

1. **#7 faced exactly this and deferred it, at its own gate.** `R16` sits inside the shared `R11` – `R18` block, but its behaviour — retry with backoff, dead-letter after N attempts, then acknowledge — is **consumer-side** reliability. #7's `specs/outbox_and_idempotency/requirements.md` §1 carries a titled subsection, *"The `R16` deferral — ratified, not overlooked"*, and #7's `progress/history.md` entry for feature 14 records the outcome: *"`R16` deliberately left `TODO` for feature 27, a deferral ratified at the gate"*. The governing test for this repository is *"did #7 face this, and what did it do?"* — it did, and this is its answer, in its committed artefacts.
2. **#8's own backlog already assigns it.** `feature_list.json` id 27 (`observability_reliability`) carries the acceptance bullet *"failed processing lands on `<topic>.dlq` after N attempts"*. No other feature claims it.
3. **There is nothing here to attach it to.** `R16`'s trigger is *"the processing of a fact by a consumer"*. This feature builds **no fact-stream consumer**: the first Kafka `BackgroundService` in this repository arrives with feature 16 (saga), 23 (notifications) and 24 (projector). A retry-and-dead-letter wrapper written now would wrap a delegate no message can reach, and `specs/shared/test-matrix.md`'s own `R16` row names `orders/integration/fact-retry-dispatcher.spec` and `orders/integration/saga-dead-letter.spec` — a *saga* file, in a feature this one explicitly does not build.
4. **The seam is written down, not left to discovery.** `design.md` §7 states exactly what feature 27 attaches to on both the consumer side and the relay side, so the deferral costs a later feature no re-derivation.

**What this means for closing the feature.** `R16`'s matrix row stays `TODO`. Matrix rule 3 is read here exactly as #7 read it — *every row this feature owns* — and this feature is complete when **`R11`, `R12`, `R13`, `R14`, `R15`, `R17`, `R18`** are green. If the gate rules the other way, the consequence is not a small addition: it pulls a Kafka consumer, a consumer group, an offset-commit policy and a dead-letter producer into a feature whose four backlog acceptance bullets mention none of them, and it overlaps feature 16's `BackgroundService` before feature 16's design exists.

## 2. Feature-local requirements

These state behaviour the shared specification genuinely leaves open. They are **not** shared requirements and never become shared requirements by being written here; `specs/shared/` is untouched by this feature.

**Id namespace and provenance.** Local ids are prefixed `OI`, for the reason `orders_aggregate` established: the shared range `R1` – `R63` is frozen, ids are never renumbered, and all three assessments cite the same numbers, so `OI<n>` cannot collide with `R<n>`.

**`OI1` – `OI12` are #7's local ids, reused deliberately.** #7 discovered them while designing the same feature against the same shared spec, and they are recorded in `order-to-cash-nestjs/specs/outbox_and_idempotency/requirements.md` §2. Keeping the numbering makes the two feature specs comparable line by line, which is what the benchmark is for. Reusing one is the same kind of claim as reusing an `R<n>`: it asserts #8 owes the same obligation. Where the .NET realisation makes an id **already satisfied by earlier work**, or **differently shaped**, the entry says so in its own words rather than quietly restating #7's.

**`OI13` – `OI16` are new to #8**, and each exists because of something #7 could not have faced: an engine that has no `SKIP LOCKED`, a defect #7 shipped and recorded so its successors need not, a wire-parity claim that only a second implementation can make, and a compiler that can enforce "only the relay publishes" where a linter could not.

### 2.1 Inherited from #7 (`OI1` – `OI12`)

**OI1.** THE SYSTEM SHALL persist, for every outbox record, every field of the `R11` envelope — `eventId`, `eventType`, `aggregateId`, `correlationId`, `causationId`, `occurredAt` and `payload` — such that the relay reconstructs the published envelope from the stored record alone, inferring, defaulting or regenerating no field at publication time; and IF any envelope field is absent from a record about to be written, THEN THE SYSTEM SHALL refuse the write rather than store an incomplete envelope.

> *#8 note.* The column half is already true — `otc_orders.outbox` carries all seven, `causation_id` included, since feature 9 (`db_orders`). What this feature adds is the **refusal** half at the writer and the **no inference** half at the relay: the row-to-`Envelope` mapper reads stored columns only and has no access to a clock, a `Guid.NewGuid()` or a default.

**OI2.** THE SYSTEM SHALL order the relay's publication of unpublished records by a **strictly increasing, tie-free sequence assigned at insertion**, so that two records written by one transaction — which necessarily share a `correlationId` and may share an identical `occurredAt` — are always published in the order the aggregate appended them, and so that the publication order of any two records of one write model is the same on every poll and on every relay instance.

> *#8 note.* The mechanism already exists: `outbox.seq` is `bigint IDENTITY(1,1)` with a unique index, and `tests/Orders.IntegrationTests/OutboxSeqIdentityTests.cs` already proves it is an identity and really increments. This feature is where the **relay is obliged to order by it** — `ORDER BY seq`, never `occurred_at`, never `id`.

**OI3.** THE SYSTEM SHALL select the records to publish by the **absence of a publication stamp** and never by a stored high-water mark of the ordering sequence, so that a record whose transaction committed after a higher-sequence record had already been published is still found and published on a later poll.

**OI4.** WHILE two or more relay instances poll one write model concurrently, THE SYSTEM SHALL grant each unpublished record to at most one instance per poll cycle, so that no record is published twice as a result of concurrent claims and no record is skipped because another instance holds it.

**OI5.** IF a relay instance terminates, loses its database connection or fails after claiming records and before stamping them published, THEN THE SYSTEM SHALL make those records claimable again without operator action, without a lease-expiry wait and without a compensating sweep, and SHALL publish them on a later poll — accepting the resulting duplicate publication as the at-least-once contract of `R14`.

**OI6.** WHILE a poll cycle of one relay instance is in progress, THE SYSTEM SHALL NOT begin a further poll cycle in that instance, so that the configured poll interval can never cause overlapping cycles to compete for the same records.

**OI7.** THE SYSTEM SHALL configure the fact-stream producer so that a retry performed inside the producer client can neither reorder the records of one partition nor create a broker-side duplicate of a record the broker already accepted.

**OI8.** IF the broker rejects or fails to acknowledge a claimed batch, THEN THE SYSTEM SHALL leave every record of that batch unpublished, SHALL log the failure with the `correlationId` and `eventId` of the affected records, SHALL retry the same records on the next poll, and SHALL NOT skip them, drop them, reorder them or publish a later record ahead of them.

**OI9.** WHEN a unit of work that persisted an aggregate and its outbox records is rolled back, THE SYSTEM SHALL leave no outbox record and no aggregate change behind, and a retry of the same operation SHALL produce exactly one outbox record per emitted fact — never zero, which is what a retry driven from an aggregate instance whose events were already cleared would produce.

**OI10.** IF the same (`eventId`, consumer) pair is delivered concurrently to two consumer instances of one write model, THEN THE SYSTEM SHALL apply the handler's effects exactly once — one delivery committing its effects together with its dedup record, the other observing the dedup record and reporting a duplicate without applying any effect.

**OI11.** THE SYSTEM SHALL keep the `outbox` and `processed_events` definitions — every column, type, nullability, key and index — identical in every write model that carries them, and SHALL prove that identity mechanically rather than by inspection.

> *#8 note — already green, and this feature must keep it green.* `tests/Billing.IntegrationTests/ReliabilityTableParityTests.cs` (feature 11, `db_billing`) already compares `INFORMATION_SCHEMA.COLUMNS` and the `sys.indexes` catalogue across all four real migrated databases. This feature changes no schema (`design.md` §3), so `OI11` is inherited **satisfied**; the obligation here is that it stays that way, which the existing test enforces without a line being added.

**OI12.** THE SYSTEM SHALL keep every per-service copy of the idempotent-consumer pattern in textual agreement with one designated canonical copy, normalising only the per-copy banner and the single namespace declaration in which a copy declares where it lives, and SHALL prove that agreement mechanically rather than by inspection; and IF a write model that consumes facts carries no copy of the pattern, or a copy diverges from the canonical outside those two regions, or the canonical acquires a service-specific name or reference another service could not adopt verbatim, THEN THE SYSTEM SHALL fail the check. WHERE a component's dedup ledger is not a relational `processed_events` table and the pattern therefore cannot be copied verbatim, THE SYSTEM SHALL require that component's variant to name the canonical copy and state its divergence in prose, and SHALL exclude it from the textual comparison.

> *#8 note — same obligation, one extra normalisation, and #7 was wrong about #8.* #7's `requirements.md` §5 predicted that `OI12` would not apply to #8 because *"#8 (.NET) can put the pattern in one shared project"*. It cannot: `CLAUDE.md`'s non-negotiable admits exactly three shared runtime projects — `src/SharedKernel` (zero package references, and this pattern talks to a store), `src/Contracts` (the wire contract, and an in-process dedup ledger is not a wire concern) and `src/Cqrs` (an Application-layer dispatcher that must not reference EF Core). A fourth would be a new gate ruling, and the dispatcher precedent does not transfer: `src/Cqrs` exists because #7 got that capability from a *package*, whereas #7 got this one from its own hand-copied files. So #8 inherits the duplication **and** the guard. The one addition is that C# puts the service's name in a `namespace` declaration, so byte-identity after the banner alone is unsatisfiable; `design.md` §6.4 argues why exactly two normalised regions is still the strictest satisfiable rule.

### 2.2 New in #8 (`OI13` – `OI16`)

**OI13.** THE SYSTEM SHALL claim unpublished outbox records under an **explicit update lock that skips records another transaction already holds**, taken at row granularity and at an isolation level under which such a skipping read is legal, so that concurrent relay instances take disjoint batches; and THE SYSTEM SHALL make the claim's behaviour independent of the write model's row-versioning configuration, so that a database created with statement-level row versioning enabled neither weakens the claim into a versioned read that sees stale rows nor causes the claim to fail.

> *Why this is new.* MS-SQL has no `SKIP LOCKED`. #7's claim is one clause of ANSI-adjacent MySQL syntax; #8's is a table hint triple whose parts interact with the isolation level, with lock escalation and with a database option (`READ_COMMITTED_SNAPSHOT ON`) that `infra/mssql/init/01-create-databases.sql` sets on all four databases for parity with #7's non-blocking MySQL reads. This is the single item the stack-comparison document has carried open since Phase 1, and `design.md` §5.2 is where it is closed.

**OI14.** IF the broker has not acknowledged a claimed batch within the configured publication timeout, THEN THE SYSTEM SHALL abandon the wait, SHALL **roll the claim transaction back** rather than commit it, SHALL leave every record of that batch unstamped, and SHALL retry the same records on the next poll; and THE SYSTEM SHALL hold no claim transaction open for longer than that timeout plus the time the claim and the stamp themselves take.

> *Why this is new.* It closes two defects #7 shipped and recorded, before #8 can repeat them. #7's review found **D2** — `OUTBOX_PUBLISH_TIMEOUT_MS` was parsed and never enforced, so its design's claim that the open claim transaction is bounded *"is false as shipped"* — and **D7** — a publish failure committed an empty transaction rather than rolling back, *"contrary to §5.3's wording"*. Both are silent: the suite is green either way. Stating them as a requirement with its own named test is the cheapest way to inherit the lesson rather than the bug. Under MS-SQL the first defect is worse than under MySQL, because the claim transaction holds update locks that `OI13`'s hint makes other relays skip but that a retention delete or a schema operation would block on.

**OI15.** THE SYSTEM SHALL publish, for a stored outbox record, an envelope whose seven fields appear in the order `asyncapi.yaml` declares them and whose six scalar fields reproduce byte for byte the bytes assessment #7 published for the same record, and SHALL republish the stored payload text **unchanged**, so that what a consumer receives is what the producing transaction committed; and THE SYSTEM SHALL produce, for the same business inputs, a payload **semantically equal** to #7's — same keys, same values, same types, same casing — with key order asserted nowhere.

> *Why this is new, and exactly what it does and does not claim.* Phase 5 proved the **serialiser** against twelve real #7 envelopes captured from its retained Kafka topics (`tests/Contracts.UnitTests/GoldenEnvelopes/`). This feature proves the **producer**: the path from an aggregate's domain event, through an `outbox` row, through the relay, onto a real broker. The split between byte-exact and semantic is `CLAUDE.md`'s and its reason is evidence: #7's payload key order is MySQL's `json`-column normalisation leaking onto its wire through its own relay, `#8` stores payloads in `nvarchar(max)` which preserves insertion order, and #9 on PostgreSQL could reproduce neither. `design.md` §5.5 states the one place this feature *can* honestly assert payload bytes — the relay's pass-through, where the assertion is that the relay changed nothing, not that #8 independently reproduced another engine's storage artefact.

**OI16.** THE SYSTEM SHALL confine the fact-stream producer client to the outbox relay's adapter, such that no type in a domain, application or presentation namespace of any service can reference it, and SHALL prove that confinement with a check that fails the build rather than with a convention.

> *Why this is new.* `R14`'s last sentence — *"No command handler, aggregate or domain service publishes directly"* — is an architectural prohibition, and #7 could only approximate it with an ESLint rule scoped to `domain/`. `CLAUDE.md` makes architecture tests first-class in #8 (*"Architecture tests are tests"*, NetArchTest runs in the normal `dotnet test` pass), and `tests/Architecture.Tests/` already holds twelve armed rules over the same namespaces. Making `R14`'s prohibition one of them costs one rule and converts the most load-bearing sentence of the outbox pattern from a review question into a red test.

## 3. Local traceability

Shared `R11` – `R15`, `R17` and `R18` are traced in [`specs/shared/test-matrix.md`](../shared/test-matrix.md) §2, and the implementer flips those seven rows to `DONE` with the **real #8 file and the real xUnit method name**, in the style feature 13 established for `R1` – `R10`. Columns 1 – 4 of that file are the trilogy contract and are **not** edited; column 5 is #8's realisation record. `R16` stays `TODO` for feature 27 (§1.2).

The local requirements are traced here, and every one starts `TODO`.

| Id | Level | Test file › case | Status |
|---|---|---|---|
| **OI1** | integration | `tests/Orders.IntegrationTests/OutboxEnvelopeTests.cs` › `OI1_Relay_ReconstructsTheCompleteEnvelopeFromTheStoredRecordAloneInferringNoFieldAtPublicationTime` | DONE |
| **OI2** | integration | `tests/Orders.IntegrationTests/OutboxRelayTests.cs` › `OI2_Relay_PublishesTwoRecordsWrittenByOneTransactionInAppendOrderAlthoughBothCarryTheSameOccurredAt`, `OI2_Relay_OrdersTheClaimBySeqNeverByOccurredAtEvenWhenTheyDisagree` | DONE |
| **OI3** | integration | `tests/Orders.IntegrationTests/OutboxRelayTests.cs` › `OI3_Relay_PublishesALowerSequenceRecordThatCommittedAfterAHigherSequenceRecordWasAlreadyPublished` | DONE |
| **OI4** | integration | `tests/Orders.IntegrationTests/OutboxRelayConcurrencyTests.cs` › `OI4_TwoConcurrentRelays_GrantDisjointBatchesAndPublishEveryRecordExactlyOnce` | DONE |
| **OI5** | integration | `tests/Orders.IntegrationTests/OutboxRelayConcurrencyTests.cs` › `OI5_Relay_ReturnsRecordsClaimedByARelayThatDiedBeforeStampingToTheNextPollWithoutALeaseWait` | DONE |
| **OI6** | unit | `tests/Orders.UnitTests/OutboxRelayLoopTests.cs` › `OI6_RelayLoop_NeverStartsASecondPollCycleWhileOneIsStillInProgress`, `OI6_RelayLoop_StopAsyncWaitsForTheInFlightCycleToFinish` | DONE |
| **OI7** | unit | `tests/Orders.UnitTests/KafkaFactPublisherConfigTests.cs` › `OI7_Producer_IsConfiguredSoAnInternalRetryCanNeitherReorderNorDuplicateAPartitionsRecords` | DONE |
| **OI8** | integration | `tests/Orders.IntegrationTests/OutboxRelayTests.cs` › `OI8_Relay_LeavesEveryRecordOfARejectedBatchUnstampedAndRepublishesTheSameRecordsOnTheNextPoll` | DONE |
| **OI9** | integration | `tests/Orders.IntegrationTests/OutboxAtomicityTests.cs` › `OI9_Repository_ProducesExactlyOneOutboxRecordPerFactWhenTheOperationIsRetriedAfterARolledBackUnitOfWork` | DONE |
| **OI10** | integration | `tests/Orders.IntegrationTests/IdempotentConsumerTests.cs` › `OI10_IdempotentConsumer_AppliesTheHandlersEffectsOnceWhenTheSameEventIsDeliveredConcurrentlyToTwoConsumers` | DONE |
| **OI11** | integration | `tests/Billing.IntegrationTests/ReliabilityTableParityTests.cs` › `Outbox_And_ProcessedEvents_Are_Defined_Identically_Across_All_Four_DbContexts` — **inherited satisfied** (feature 11); re-run green under this feature's changes | DONE |
| **OI12** | unit | `tests/Orders.UnitTests/IdempotentConsumerParityTests.cs` › `HoldsEveryWriteModelsCopyByteIdenticalToTheCanonicalAfterTheBannerAndTheNamespaceLine`, `KeepsTheCanonicalAdoptableVerbatimNamingNoServiceAndReferencingNothingServiceSpecific`, `RequiresACopyOfThePatternFromEveryWriteModelThatConsumesFacts`, `RequiresADocumentedDivergenceBannerFromACopyThatCannotShareTheCanonicalsTransaction` — the four cases exactly as `design.md` §6.4's table prescribes | DONE |
| **OI13** | integration | `tests/Orders.IntegrationTests/OutboxRelayConcurrencyTests.cs` › `OI13_Claim_SkipsRowsHeldByAnotherRelayAndBehavesIdenticallyWithRowVersioningEnabledOnTheDatabase` | DONE |
| **OI14** | integration | `tests/Orders.IntegrationTests/OutboxRelayTests.cs` › `OI14_Relay_AbandonsAPublishThatExceedsTheTimeoutRollsTheClaimBackAndRepublishesOnTheNextPoll` | DONE |
| **OI15** | integration | `tests/Orders.IntegrationTests/OutboxWireParityTests.cs` › `OI15_Relay_PublishesBytesIdenticalToTheGoldenEnvelopeCapturedFromNumber7` and › `OI15_Recorder_WritesAPayloadSemanticallyEqualToNumber7sForTheSameBusinessInputs` | DONE |
| **OI16** | architecture | `tests/Architecture.Tests/FactPublisherConfinementTests.cs` › `OnlyTheOutboxAdapterMayReferenceTheFactStreamProducerClient` | DONE |

> **Case names are the contract.** `specs/shared/test-matrix.md` rule 4 — *"Renaming a test means editing its row"* — applies to this table too. The names above are the ones the implementer writes; if one changes, this table changes in the same commit.

## 4. Acceptance (from `feature_list.json`)

| Acceptance bullet | Requirements |
|---|---|
| aggregate row and outbox row written in one transaction | R13, OI1, OI9 |
| relay publishes to Kafka keyed by `correlationId` and stamps `publishedAt` | R11, R12, R14, R15, OI1, OI2, OI3, OI7, OI8, OI14, OI15, OI16 |
| safe under concurrent relay instances | OI4, OI5, OI6, OI13 |
| redelivery deduplicated via `processed_events` | R17, R18, OI10, OI12 |

`OI11` serves no bullet directly: it is a standing guarantee this feature must not break, already proven by feature 11's test. `OI12` serves the fourth bullet only indirectly — the Orders copy alone satisfies the bullet, but that copy is the reference four more components will duplicate, and a duplication guarded by discipline is a defect waiting for feature 17.

## 5. Promotion candidates for `specs/shared/` — restated, not re-decided

#7 recorded four of its local requirements as **promotion candidates**: `OI1` (the envelope must survive storage), `OI2` (deterministic publication order), `OI4` + `OI5` (exclusive claim and crash recovery) and `OI10` (concurrent duplicate delivery) — each of which, left local, permits three conforming implementations to behave genuinely differently. They were never promoted: `specs/shared/requirements.md` as inherited here runs `R1` – `R63` with §2 unchanged at `R11` – `R18`.

**Nothing is promoted by this feature and nothing in `specs/shared/` is edited.** The candidacy is restated for two reasons and no others: #8 hitting the same four gaps independently is corroborating evidence for them, and #9 will read only `specs/shared/` and will hit them a third time. `OI13` is **not** a candidate — it is an answer to a question one engine asks and the other two do not. `OI15` is not a candidate either: it is a parity claim between two assessments, meaningless in a document all three share. `OI16` is not a candidate: it restates `R14`'s own last sentence and adds only a mechanism.

If an amendment to `specs/shared/` is ever thought necessary, the procedure is `CLAUDE.md`'s and it is not this document's to shortcut: stop, say so explicitly, write it as its own change, flag it for back-porting to #7, and take it to the human gate.

## 6. What this feature changes outside its own directory

Recorded here so the gate sees it before approval, and so no later reviewer discovers it as drift:

| File | Change | Why |
|---|---|---|
| `src/SharedKernel/` | **New files only** — the envelope interface and its pure validator (§1.1). No existing type's behaviour changes; the zero-package-reference rule is preserved and `SharedKernelHasNoPackagesTests` must stay green | `R11` has no other owner in #8 |
| `src/Orders/Domain/Events/OrderDomainEvent.cs` | Additive: the abstract record declares that it implements the new envelope interface. No new field, no new dependency, no behaviour change | So the guard can be applied to real events without infrastructure reaching into the domain |
| `tests/Orders.IntegrationTests/MsSqlContainerFixture.cs` | `CreateFreshDatabaseAsync` sets `READ_COMMITTED_SNAPSHOT ON`, matching `infra/mssql/init/01-create-databases.sql` | Today the integration fixture creates databases **without** it, so every concurrency test in this repository has been proving behaviour under an isolation configuration the deployed stack does not use (`design.md` §9.2) |
| `Directory.Packages.props`, `src/Orders/Orders.csproj`, `tests/Orders.IntegrationTests/*.csproj` | Package versions and references (`design.md` §8) | Every one appears in the feature's commit message, per `CLAUDE.md` |
| `specs/shared/test-matrix.md` | **Column 5 only**, rows `R11` – `R15`, `R17`, `R18`, plus the derived counts | The one edit the shared spec's own recipe sanctions |
