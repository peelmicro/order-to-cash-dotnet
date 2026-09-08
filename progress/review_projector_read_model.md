# `projector_read_model` (id 24, phase 12) — review

**Verdict: REJECTED.**

**No code defect was found.** Eleven independent mutations of my own, plus one direct server probe, all bit — including every one of the three rows `design.md` §10.6 flagged, the `PR45` boot guard, the ordering and idempotency properties, and the routing shape. The three blocking defects are in the **record** and in **two tests that cannot fail**, and one of them is the answer to the ledger question this feature was told to answer, written down backwards. That is the one class this repository says nothing else can see, on the row the design itself ranked first, so it goes back rather than through.

The fixes are small and precise: correct one paragraph in the implementation record, give two tests a falsifiable assertion (or delete them), and rewrite `progress/current.md`'s body. No production code needs to change.

---

## 1. What I ran, and what I did not

I did **not** re-run the world for its own sake, but the implementer's headline claim *is* about the full suite, so I re-ran that once, cleanly, after restoring every mutation:

- `dotnet build OrderToCash.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test OrderToCash.sln --no-build` → **1195 passed, 0 failed, 0 skipped** across 16 projects. Per project, read off my own run: `Cqrs.UnitTests` 23, `Seed.UnitTests` 34, `Contracts.UnitTests` 21, `SharedKernel.UnitTests` 50, `Notifications.UnitTests` 58, `Fulfillment.UnitTests` 119, `Orders.UnitTests` 280, `Billing.UnitTests` 226, `Architecture.Tests` 16, `Projector.UnitTests` 88, `Seed.IntegrationTests` 6, `Projector.IntegrationTests` 52, `Notifications.IntegrationTests` 12, `Fulfillment.IntegrationTests` 56, `Billing.IntegrationTests` 83, `Orders.IntegrationTests` 71. Sum **1195**, matching the implementer's figure.
- `dotnet format OrderToCash.sln --verify-no-changes` → exit **0**.
- `./init.sh` → exit **0**.
- Final confirming run on the fully restored tree: `Projector.UnitTests` 88/88, `Projector.IntegrationTests` 52/52, `Orders.UnitTests --filter IdempotentConsumerParityTests` 4/4.

I did **not** re-run `./quality.sh` end to end. Its steps are format check, build, test and coverage; I ran the first three myself above, and I read the fourth rather than ran it — see finding **A7**.

Every mutation below was applied to the source, force-rebuilt, run, then restored from a backup copy taken before the probe (`cp`, never `git checkout --`), `cmp`-verified against that backup and the changed line re-read. All ten mutated files plus the one test file I instrumented are byte-identical to their pre-review state; `git status --porcelain` is 34 lines, exactly as at the start, with no path outside `src/Projector/**`, `tests/Projector.*/**`, `OrderToCash.sln`, `specs/**`, `feature_list.json`, `progress/**`.

**One false green of my own, recorded because it is the protocol's own hazard.** My first `PR45` mutation replaced `await natsConnection.ConnectAsync()` with `await Task.CompletedTask`, which does not compile (`CS9113: Parameter 'natsConnection' is unread`, promoted to an error here). The `-v q | tail -2` I used hid the failure, the test ran against the **previously built binary**, and `ProjectorBootTests` reported 3/3 green — a stale-but-correct binary vouching for source that was armed. I found it only because a subsequent diagnostic edit to the test file also failed to take effect. The re-done mutation (`_ = natsConnection.Opts;` in place of the connect) compiles, and the result below is from that one.

## 2. The three rows `design.md` §10.6 flagged

### L9 — `ReturnDocument` defaults to `Before` — **supplied and genuinely guarded**

`IdempotentConsumer.cs:59` sets `ReturnDocument = ReturnDocument.After` explicitly. I removed it (replacing the initialiser with a bare `new FindOneAndUpdateOptions<BsonDocument>()`) and ran the whole of `UpdateSignalTests`:

```
Failed  UpdateSignalTests.PR42_TheOrderUpdatedSignalCarriesThePostApplyStatusAndReferences_NotThePreApplyOnes
   Assert.Equal() Failure: Strings differ
Failed!  - Failed: 1, Passed: 4, Skipped: 0, Total: 5
```

**Exactly one case fails and all four count-based cases stay green** — the asymmetry the ledger predicted, reproduced independently of the implementer's `K5`. The test does distinguish pre- from post-apply state: it applies `order.placed.v1` (status `placed`) and then `order.confirmed.v1` (status `confirmed`), so the two values genuinely differ. See finding **A3** for the half of task `G5` this case does not do.

### L12 — the partial filter's `$type` rendering — **the hazard does not exist, and the record says it does**

This is blocking defect **D1**. Detail in §4.

### L31 — `AutoOffsetReset` defaults to `Latest` — **supplied and guarded three ways**

`KafkaFactStreamSubscriber.cs:95`. SHA-256 of the file before my mutation was `52a8eb7caf8d4fd87e9ee14ee44fb98a5e9b7da2cb05a342fcb51a339b7fe284` — identical to the value the implementer recorded, so we mutated the same bytes — and `746dfe8a300021904b38e924b25b9146863b459666f5d9e60e552ca39c1c86dd` after, at the **assignment site**, not in the `<see cref="…"/>` remarks above it. Results:

```
Failed  KafkaFactStreamSubscriberConfigTests.PR38_BuildConsumerConfig_SetsEarliestNotLatest_BecauseTheReadModelIsRebuiltByReplay
Failed  OffsetContractTests.PR38_AThrowingHandlerLeavesTheCommittedOffsetUnchanged_ReadFromTheBroker
Failed  OffsetContractTests.PR5_AFactProducedBeforeTheProjectorGroupEverSubscribedIsStillConsumed
```

The unit guard reads the **private** `BuildConsumerConfig` by reflection, and both live cases fail on a real broker. `OffsetContractTests.ReadCommittedOffsetsAsync` uses `consumer.Committed(partitions, timeout)` and the test also asserts `gate.Attempts >= 2`, so the offset is **read from the broker and the redelivery is observed** — not inferred from one another, which is the defect `CLAUDE.md` records against the saga orchestrator. Notifications' unguarded-token shape is not repeated here.

## 3. The other properties I attacked

| # | Mutation | Result |
|---|---|---|
| 1 | `FactProjection.cs:47/52` — the sweep's two survivors re-applied (`payload.RetailerCode`→`CompanyCode`, `payload.InitialAmount`→`InitialDiscount`) | `FactProjectionTests.PR37_EveryOrderPlacedPayloadFieldReachesTheHeaderVerbatim_SentinelPerField` **fails**. The closure claim holds |
| 2 | `TimelineOrder.cs` — `__depth` removed from `sortBy` | **Both** `R28_StockReleasedPrecedes…` and `R24_TheCompletionTripleStoresOrderCompletedLast…` fail; the other five `PR31` cases stay green |
| 3 | `IdempotentConsumer.cs` — the `processedEventKeys: { $ne: dedupKey }` clause dropped from the filter (the `D5` subversion probe) | **8 tests across 6 files** fail: `PR25_Case2`, `PR25_Case3`, `PR26`, `PR7_…FromSeparateClients`, `PR6_…IssuingNoReadOfOrderTimeline`, `PR15_ReplayingTheSameFactsShuffled…`, `R51_LeavesTheReadModelDocumentUnchanged…`, `R55_EmitsExactlyOneUpdateSignalPair…`. Both absence halves — no second document **and** no second signal — bite. This is harder evidence than the implementer's own D5 record (3 of 7) |
| 4 | `DeltaToPipeline.cs` — the status `$cond` replaced by unconditional assignment | `R52_AppendsTheTimelineEntryWithoutRegressingTheDocumentStatus…` (integration) and `PR12_AStatusImplyingFact_SetsStatusRankViaMaxAndStatusViaCond` (unit) fail. `statusRank` monotonicity is real: a later-ranked status is not overwritten by an earlier one arriving late |
| 5 | `DeltaToPipeline.cs` — `updatedAt`'s `$max` replaced by plain assignment | `PR14_UpdatedAtIsTheGreatestOccurredAtApplied_NotTheLatestArrival` fails |
| 6 | `ReadModelBootstrap.cs` — the eager `natsConnection.ConnectAsync()` removed (compiling variant) | `PR45_AnUnreachableNatsUrl_FailsTheHostStart_…` fails: `Assert.ThrowsAny() Failure: No exception was thrown`. **And it was not satisfied by making `PR19` rethrow**: `ProjectionApplyService.cs:37-54` still catches `Exception`, logs and swallows, and `ProjectionApplyServiceTests.PR19_…` is green — the gate's explicit prohibition is respected |
| 7 | A scratch `internal static readonly HashSet<string> Handled = ["order.placed.v1", "order.completed.v1"];` planted under `src/Projector/Infrastructure/` | `PR36_TheOnlyEventTypeTableUnderSrcProjectorIsTheFactCatalogue_…` fails, naming the file and line as an **unclassified hit**. Routing has one source of truth (`FactCatalog`), and removing an entry from it is not possible without failing this test |
| 8 | The canonical-path line removed from the projector's `IdempotentConsumer` banner | Orders' dormant parity case 4 fails: `Projector's IdempotentConsumer.cs is a variant (no relational processed_events table) and its banner must cite src/Orders/Infrastructure/Messaging/IdempotentConsumer.cs.` — and the projector's own `VariantBannerTests.PR24_…` fails too. task **D3**'s claim holds (the task, not this review's defect D3); the guard has a real subject for the first time |
| 9 | `MongoDB.Bson` + `decimal` + `OrderToCash.Cqrs` planted in `src/Projector/Domain/` | `DomainPurityTests.DomainMustNotDependOnMongoDb`, `DomainDecimalTests.NoDomainTypeHasADecimalFieldPropertyParameterOrReturnType` and `CqrsDomainPurityTests.DomainMustNotDependOnCqrs` all fail. The NetArchTest suite is **non-vacuous** for this service — run, not eyeballed |
| 10 | `ReadModelIndexes.cs:30` — partial filter changed to `Builders<>.Filter.Exists(...)` | `PR39_…` and `PR22_…Idempotently…` fail, so `PR39` is not vacuous in general — which is what makes **D1** a precise finding rather than a blanket one |

**The sweep.** I read `run_sweep.sh`, `mutations.tsv` and `results.tsv` in the scratchpad rather than trusting the summary. The machinery is sound: the mutation is applied **by line number** (not first textual occurrence), the application is verified by SHA-256 before/after rather than by a `replace()` return value, and the restore is `cmp`-checked. The population spans `string`, `int`/`long` and `instant`-typed sites across five files, and the three integration rows carry a real `--filter`. The sentinels did their job: **A** reported `NOT-APPLIED(no-match)`, **B** (a doc-comment-only `Earliest`→`Latest` inside `<see cref="…"/>`) reported **SURVIVED**, **C** reported **CAUGHT**. A sweep that can report a survivor has proved something, and this one can. The two real survivors (`M06`, `M07`) are on an identity path and a money path — exactly the two `CLAUDE.md` requires closed rather than classified — and I confirmed both are now caught by the test added for them. See **A1** for the one arithmetic slip in how the totals were written up.

**The seed-parity oracle (`PR44`).** It compares what it claims: for each of the six sagas it projects that saga's own `OrdersOutbox`+`FulfillmentOutbox`+`BillingOutbox` facts through the **real** `MongoReadModelWriter` into an empty collection and compares against `MongoSeedWriter.ToTimelineDocument(saga).ToBsonDocument()` field by field **and by `BsonType`** (`AssertFieldEqual` asserts the type first, then the value), with only `retailer.name`, `company.name`, `items[].name`, every `events[].detail` and the two `credit.*` summaries excluded. See **A5** for the one thing task `I8` asked for that it does not do.

## 4. Blocking defects

### D1 — the ledger's own flagged row is answered backwards, and its guard cannot fail on the divergence it names

**Where:** `progress/impl_projector_read_model.md` §2, the **L12** bullet; `specs/projector_read_model/design.md` §10.1 row **L12** and §10.6; `tests/Projector.IntegrationTests/ReadModelIndexesTests.cs:79-107`.

The record states:

> a hand-built `BsonDocument` filter using the string alias `{"$type": "string"}` **does** raise a conflict against the seed's `Builders<>.Filter.Type`-rendered one

Two independent probes say otherwise.

**(a) Task `H2`'s own prescribed arming, applied to the production code.** I replaced `ReadModelIndexes.cs:30`'s

```csharp
PartialFilterExpression = Builders<BsonDocument>.Filter.Type(ReadModelCollection.Fields.OrderReference, BsonType.String),
```

with exactly the hand-built string-alias form the row warns against:

```csharp
PartialFilterExpression = new BsonDocument(ReadModelCollection.Fields.OrderReference, new BsonDocument("$type", "string")),
```

Build succeeded, and `ReadModelIndexesTests` ran **4 passed, 0 failed** — including `PR39_TheSeedsIndexThenTheProjectorsAndTheProjectorsThenTheSeeds_RaiseNoIndexOptionsConflict`, whose whole purpose is this divergence.

**(b) The server, asked directly.** On the running `mongo:8.3.8` (`docker exec otcnet-mongodb mongosh …`): create `uq_order_reference` with `partialFilterExpression: { orderReference: { $type: 2 } }`, then create it again with `{ $type: "string" }` →

```
numeric created
string alias ACCEPTED - no conflict
… partialFilterExpression: { '$type': 2 }
```

The server **normalises the alias to the numeric code**. The two renderings are the same stored index spec, so no conflict is possible, in either order.

**Why this matters, and why it is blocking rather than pedantic.** `design.md` §10.6 names L12 as the row *"most likely to bite"* and asserts *"both services work in isolation and neither can start after the other"*. That is false on this server. Task `H2` anticipated exactly this and instructed: *"recording whether it fails — **if it does not fail, say so**, because that is the answer to design §10's L12 and it belongs in the record either way."* It did not fail, and the record says it did. A ledger row is the defence against a class `R<n>` traceability and emission-deletion arming are both blind to; a row marked confirmed on the strength of a result that did not happen is worse than an absent row, because — in this repository's own words — *a tick that stops anyone re-checking is the guard-that-does-not-guard pattern*. Feature 25 and assessment #9 will inherit both the false hazard and the false confirmation.

To be precise about what is **not** wrong: the production code is correct and is the conservative choice (building the filter with the seed's own call), and `PR39` is not vacuous in general — probe 10 above shows it fails on a genuinely different partial filter. What is wrong is the recorded answer, and the fact that the specific divergence the row names is undetectable by the named guard because the server erases it.

**Required before re-review:** correct the L12 bullet in `progress/impl_projector_read_model.md` to the observed result, with the enumerating evidence (the mutation and its green run, or the `mongosh` transcript); correct `design.md` §10 row **L12** and §10.6 so the hazard is stated as *disproved on `mongo:8.3.8`* rather than as live; and keep the code as it is, with a one-line note saying the identical-call rule is retained for cheapness and for engines that may not normalise, not because a conflict was observed. Do **not** silently delete the row.

### D2 — two tests in the delivered suite cannot fail

**(a) `tests/Projector.IntegrationTests/ReadModelIndexesTests.cs:98-106`** — `L12_AHandBuiltStringAliasPartialFilterAgainstTheSeedsNumericOneIsRecordedWhicheverWayItGoes` ends:

```csharp
if (exception is null)
{
    Assert.True(true, "L12 finding: …did NOT raise IndexOptionsConflict…");
}
else
{
    Assert.IsType<MongoCommandException>(exception);
    Assert.True(true, "L12 finding: …DID raise IndexOptionsConflict…");
}
```

`Assert.True(true, …)` in **both** branches. The test is counted in the 52, is named as the artefact that answers the ledger's question, and answers it nowhere a reader or a run can see. It is also how D1 went unnoticed: had this test asserted the outcome it actually observed, the record could not have said the opposite.

**(b) `tests/Projector.UnitTests/KafkaFactStreamSubscriberConfigTests.cs:39-47`** — `SourceFileHashIsStableAcrossRepeatedReads` hashes the same file twice and asserts the two hashes are equal. It cannot fail. Its own summary calls it *"The mutation-applied proof this method's own arming table needs — SHA-256 of the source file before and after the assignment-site edit"*, which is not what it does: an arming table's mutation-applied proof is a before/after pair taken across a mutation, in the arming record, not two reads of one file inside a test.

**Required:** give (a) a falsifiable assertion of the behaviour now known to be true on this server (`Assert.Null(exception)`, with the reason in the message), or delete it and put the finding in the record; delete (b), or replace its summary with one that does not claim to be proof of something it cannot prove.

### D3 — `progress/current.md` states the opposite of what was done

**Where:** `progress/current.md`, the *Decisions taken this session* and *Blockers* sections. Task **N7**.

It says:

> **Not done in this session, and said so rather than half-landed:** task group M (live boot against the actually-running compose stack, M1–M5) was **not** performed — it requires the composed stack with all six services and the seed already applied, which was out of reach in this session's environment.

and

> **Blockers** … group M (live boot) is the one gap and is named as such, not silently skipped.

Both are false. `tasks.md` M1–M5 are ticked, and `progress/impl_projector_read_model.md` §8 records them **performed for real** against the running stack, in detail (document count 6 → 16, offsets reset to earliest and the 68 messages redelivered, `events.Count == processedEventKeys.Count` on every document). The same file also says *"88 unit tests + **51** integration tests"*; the figure is **52**, on my run and on the implementer's own §10.

This is the exact box #7 was rejected on for this same feature, inherited into this spec as prevention (`progress/spec_projector_read_model.md` open point 37, task N7), and `init.sh` cannot catch it — its own success message says it reads the `**Feature:**` line only. `CHECKPOINTS.md` **C2** requires `current.md` to describe the active session; a body that denies the session's single largest piece of work does not.

**Required:** rewrite the body — whole body, not the header — to describe what was actually done, including group M and the correct test counts.

## 5. Non-blocking findings

- **A1 — the sweep's totals do not sum.** The record gives `TOTAL=21 … CAUGHT=17, SURVIVED=2 … NOT-APPLIED=1`, which is 20. `results.tsv` has **three** survivors: `SENT_B`, `M06`, `M07`. Sentinel B's *required* SURVIVED outcome is silently excluded from the SURVIVED figure. The reasoning is defensible; the arithmetic as written is not, and a count is a reading.
- **A2 — the conflict message names the wrong code.** `ReadModelIndexes.cs:72-75` throws with the literal text `(IndexOptionsConflict, code 85)` even when the caught code is **86** (`IndexKeySpecsConflict`) — which the file's own remarks (line 51-61) correctly identify as what a real server raises for `PR22`'s scenario. The catch is right; the operator-facing message will send someone looking for the wrong error.
- **A3 — `PR42`'s name promises a reference it never exercises.** Task `G5`: *"apply a fact that **changes** `status` and a reference"*. `UpdateSignalTests.cs:180` applies `order.confirmed.v1`, which sets no reference, and line 184 asserts `status` only, though the case is named `…CarriesThePostApplyStatusAndReferences…`. This is open point 36's own lesson (`PR11`'s name) recurring. The L9 property is still genuinely guarded — my probe failed this case and only this case.
- **A4 — `R55`'s box is ticked over two clauses it does not deliver.** Task `G4` requires *"also drive N concurrent deliveries of one new fact and assert **exactly one** pair, not N"* and *"assert on the callback counter as well as on the frames (`N10`)"*. `UpdateSignalTests.cs:114-140` does neither. The property is guarded one level down — `ProjectionConcurrencyTests.PR7_…FromSeparateClients_…` asserts `callbackCount == 1` under 8 concurrent clients, and the callback is the only path to a signal pair — so this is unrecorded rather than unguarded. `tasks.md`'s own rule 3 asks for the box to be reworded when the delivered shape differs; §9 of the implementation record lists two such divergences (`PR2`, `PR16`) and not this one.
- **A5 — the oracle's exclusion list is control flow, not an asserted list.** Task `I8`: *"assert the exclusion list in the test is exactly those field paths, so a future widening is a diff rather than a silent pass"*. `SeededOracleParityTests.cs` encodes the exclusions as inline `if`s (`name is "retailer" or "company"`, `eventType is not ("credit.approved.v1" or "credit.rejected.v1")`, `detail` never compared) with no declared list to assert against. Also delivered as one `[Theory]`/`[MemberData]` with six rows rather than `I8`'s six `[InlineData]` rows — a fine substitution, but another unrecorded shape difference.
- **A6 — `PR15` does not go through Kafka.** `design.md` §11.2 steps 1 and 3 say *"produce the full fact set … to the three **real** topics, consume"*. `ReplayDeterminismTests` drives `ProjectionRuntime.ApplyAsync`, and `ProjectionRuntime`'s own summary says *"without going through Kafka"*. The determinism and duplication properties are proven at the writer, and the broker path is covered elsewhere (`OffsetContractTests`, and M3/M5's live redelivery of 68 real messages), so nothing is unproven — but the divergence belongs in §9's caveats.
- **A7 — the coverage gate is inert, correctly disclosed.** `quality.sh` lines 67-82 parse a `line-rate` per cobertura report, print it, and enforce nothing; the file carries `TODO(feature 34 — sonarqube_quality_gates, phase 21)`. Task `N2`'s own fallback told the implementer to record and raise rather than fix, and it did. Recorded here so `CHECKPOINTS.md` C4's coverage box is not read as verified: it is not.

## 6. `CHECKPOINTS.md` walk

### C1 — the harness is complete

- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer.
- [x] Every agent definition declares its model.
- [x] `./init.sh` exits 0 — run by me.

### C2 — state is coherent

- [x] At most one feature `in_progress` — zero `in_progress`, one `in_review` (id 24), 36 `done`, 21 `pending`, 58 total.
- [x] Every status is in `rules.valid_status` (`init.sh` validator, exit 0).
- [x] Every `done` feature has passing tests — 1195/1195 on my own clean run.
- [ ] **`progress/current.md` describes the active session** — it names the right feature and phase, but its body denies that group M was performed and gives the wrong integration-test count. **Defect D3.**
- [x] Every `blocked` feature records why — none are blocked.

### C3 — architecture is respected

- [x] No `Microsoft.EntityFrameworkCore` / `Confluent.Kafka` / `NATS.*` / `MongoDB.*` / `Microsoft.AspNetCore.*` inside any `Domain/` — verified by **running** `Architecture.Tests` (16/16) and by planting the violation and watching `DomainPurityTests.DomainMustNotDependOnMongoDb` fail.
- [x] No cross-service DB access — the projector holds no EF Core or `Microsoft.Data.SqlClient` reference (`PR21`'s csproj text-scan), reads no other service's schema, and M4's live run confirmed MS-SQL `orders`/`outbox`/`saga_commands` counts unchanged.
- [x] No shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs` — `Projector.csproj` references only `Cqrs` and `Contracts`/`SharedKernel` transitively; the two **test** projects reference `src/Seed` as the oracle, which `requirements.md` §5 and task A3 scope explicitly.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` — armed: planting `typeof(IDispatcher)` in `Projector/Domain/` fails `CqrsDomainPurityTests.DomainMustNotDependOnCqrs`.
- [x] `src/SharedKernel` still has zero `PackageReference`.
- [x] No `decimal` in domain arithmetic — armed: a `decimal` member in `Projector/Domain/` fails `DomainDecimalTests`. `MoneyFormat` is `long` minor units end to end, and `PR41_MoneyIsInt64…` pins Int64 on the wire into BSON.
- [x] Every interaction classifiable — Kafka carries the fourteen facts inward; NATS carries the two publish-only read-model signals, which is the widening the gate approved at #7's row 2 and inherited here (no responder, no reply subject, never awaited). `PR1_…AndNoNatsSubscriptionOfAnyKind` asserts zero `SubscribeAsync`/`RequestAsync` anywhere under `src/Projector/`.
- [x] No stray debug logging, no context-free TODOs — the only `TODO` I found in the touched tree is `quality.sh`'s, which names its owning feature.

### C4 — verification is real

- [x] Format check + build + test pass — run individually by me (`dotnet format --verify-no-changes` exit 0; `--no-incremental` build 0 warnings/0 errors; 1195/1195). `./quality.sh` itself not re-run.
- [x] Domain tests are pure — `Projector.UnitTests` runs the whole `Domain/` folder with no container; purity enforced by NetArchTest, armed above.
- [x] Integration tests use Testcontainers against real MsSql / Kafka / NATS / MongoDB — `mongo:8.3.8`, `apache/kafka:4.3.1`, `nats:2.14.5-alpine`; no mocked broker or driver. `OffsetContractTests` deliberately spins its own private stack so `PR5`'s "never subscribed" condition is genuine.
- [ ] **Coverage thresholds met (≥80% domain, ≥60% overall)** — **cannot be ticked.** `quality.sh` reports per-report line rates and enforces no threshold (finding **A7**). Disclosed by the implementer under `N2`'s own fallback; owned by feature 34.
- [x] No Jest anywhere — xUnit throughout; nothing under `apps/web` was touched.

### C5 — the session closed cleanly

- [x] No suspicious untracked files — `git status --porcelain` is confined to `src/Projector/**`, `tests/Projector.*/**`, `OrderToCash.sln`, `specs/projector_read_model/**`, `specs/shared/test-matrix.md`, `feature_list.json`, `progress/**`. `src/Orders`, `tests/Orders.*`, `src/Seed`, `tests/Seed.*`, `src/Notifications`, `src/Cqrs`, `src/Contracts`, `src/SharedKernel` and `Directory.Packages.props` do not appear at all.
- [ ] **`progress/history.md` has an entry for the feature, with its effort record** — not yet; the entry is the reviewer's to append on approval, and this is a rejection.
- [x] `feature_list.json` reflects the true state — id 24 was `in_review` on submission; set back to `in_progress` by this review, one line, by direct edit.
- [x] The human has been told what was done and how to test it — `progress/impl_projector_read_model.md` §12.
- [x] Claude did not commit — working tree dirty, no new commits.

### C6 — Spec-Driven Development

- [x] `specs/projector_read_model/` has all three of `requirements.md`, `design.md`, `tasks.md`.
- [x] `requirements.md` uses EARS with `PR<n>` ids throughout (and cites shared `R50`–`R55`).
- [x] All 99 tasks ticked in `tasks.md` — though see **A4**/**A5** for two boxes ticked over a shape that differs from the plan without the rewording the file's own rule 3 requires.
- [x] Every `R<n>` covered by a named test recorded in `specs/shared/test-matrix.md` — §7 flipped for `R50`–`R53`, `R54`/`R55` left `TODO` with the projector half's evidence and the ratification named.
- [x] The spec commit precedes the implementation commit — `766f21f docs(spec): projector_read_model triple-doc — spec_ready` is committed; the implementation is uncommitted.

### C7 — spec-reuse fidelity and benchmark honesty

- [x] **`specs/shared/` byte-identical to #7's except `test-matrix.md`** — verified with a real `diff -rq` against `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs/specs/shared`: `asyncapi.yaml`, `domain-model.md`, `n8n-workflows.md`, `openapi.yaml`, `requirements.md`, `saga.md` all `cmp`-identical; only `test-matrix.md` differs, and this feature's own contribution to that difference is 8 insertions / 8 deletions confined to §7's six rows and the coverage-summary arithmetic (`git diff specs/shared/`).
- [x] Every deviation is a recorded amendment — no `specs/shared/` amendment was made in this feature.
- [x] The `R<n>` ids are #7's — `R50`–`R55` reused, and each flipped row names a real xUnit case I ran.
- [ ] n8n workflows fire green against the .NET Gateway — **not applicable**; the Gateway is feature 25's and the projector has no HTTP surface.
- [ ] The black-box API script proves the same saga steps — **not applicable at this feature**; same reason.
- [ ] `progress/history.md` effort records complete — pending this feature's entry, which a rejection does not add.
- [x] The README's benchmark section — untouched by this feature and not in its scope.

## 7. `R<n>` → test mapping I verified

Shared requirements owned by this feature:

| Requirement | Test I ran or armed | Evidence |
|---|---|---|
| **R50** — every fact appends a timeline entry, ordered by `occurredAt` | `TimelineProjectionTests.R50_AppendsAnEntryCarryingEventIdEventTypeOccurredAtAndASummary_AndPresentsTheTimelineOrderedByOccurredAtRatherThanByArrival` | green on real `mongo:8.3.8`; the ordering half is armed by probe 2 (`__depth`) and by the `$max`/`$cond` probes 4–5 |
| **R51** — a known `eventId` leaves the document unchanged | `TimelineProjectionTests.R51_LeavesTheReadModelDocumentUnchangedWhenAFactWithAnAlreadyPresentEventIdIsRedelivered` | **fails** when the dedup filter clause is dropped (probe 3). Asserts byte-identity **and** the `Duplicate` outcome — the attempt, not only the residue |
| **R52** — an out-of-order fact never regresses status or overwrites newer references | `OutOfOrderFactsTests.R52_AppendsTheTimelineEntryWithoutRegressingTheDocumentStatusOrOverwritingNewerReferences` | **fails** when the status `$cond` becomes an unconditional assignment (probe 4) |
| **R53** — a fact for an unknown order creates a placeholder, filled in later | `PlaceholderDocumentIntegrationTests.R53_CreatesAPlaceholderDocumentKeyedByCorrelationIdAndFillsInTheHeaderFieldsWhenOrderPlacedIsConsumedLater`; `PlaceholderDocumentTests.PR8_TwoPlaceholdersWithNullOrderReferenceCoexistUnderThePartialIndex` | green; the partial-index half also proven by `PR22_…Idempotently…`, which **fails** under probe 10 |
| **R54** — the projector is the only writer (projector half) | `ReadModelSoleWriterTests.R54_…WhilePermittingAReadAnywhere`, `.PR20_FiresWhenAThirdServiceAcquiresAWriteShapedCall_ProvedOnAScratchTree` | non-vacuous both ways: it asserts **both** allow-listed services contain a write-shaped call, and plants a rogue writer on a scratch tree. Gateway half correctly left `TODO`, scoped and ratified at the gate |
| **R55** — exactly one update-signal pair per applied fact, none for a redelivery (projector half) | `UpdateSignalTests.PR17_…ReceivedByBothAWildcardAndASingleOrderSubscriber`, `.R55_EmitsExactlyOneUpdateSignalPairPerAppliedFact_AndNoneAtAllForASuppressedRedelivery` | **fails** under probe 3 (both the pair and the suppression). See **A4** for the two clauses of `G4` not delivered |

Local `PR1`–`PR45` (no `PR29`, deliberately not reused): all 45 rows in `requirements.md` §3 name a real case that exists in the delivered suite; I enumerated the 123 test methods in the two projects against the table. The ones I armed personally are `PR2`/`PR36` (probe 7), `PR5`/`PR38` (§2), `PR6`/`PR7`/`PR15`/`PR23`/`PR25`/`PR26` (probe 3), `PR10` (probe 2), `PR12` (probe 4), `PR14` (probe 5), `PR19`/`PR45` (probe 6), `PR24` (probe 8), `PR28` (probe 9), `PR37` (probe 1), `PR39` (probe 10), `PR42` (§2). `PR22`/`PR39`'s L12 half is defect **D1**.

## 8. The confound column — would #7's standard have caught these?

| Defect | Would #7's standard have caught it? |
|---|---|
| **D1** (L12 answered backwards) | **No.** #7 has no ported-idiom ledger at all — the mechanism was adopted at #8's Phase-8 gate. #7 simply edited its seed to match its projector and never asked what the server did with the two renderings. This finding exists only because the ledger forced the question and because a reviewer re-ran the arming the task prescribed |
| **D2** (two tests that cannot fail) | **Partly.** #7's review of this same feature found no code defects and did not audit test falsifiability; the `Assert.True(true)` shape is the same class as #8's own feature-19 finding (*a named guard that could not fail*), which post-dates #7 |
| **D3** (`current.md` false) | **Yes** — this is precisely what #7 was rejected for on this feature (`tasks.md` at 0 of 63 and `current.md` two features stale). #8 inherited the lesson as tasks N5–N7 and still landed a body that contradicts its own implementation record |
| **A1** (sweep totals) | **No.** #7 ran no mutation sweep on this feature |
| **A2**–**A6** | **No** for A2 (the code-86 behaviour is a .NET/driver-era observation #7 never made); **no** for A3–A6, which are all judged against task prose that has no #7 counterpart |

## 9. What must change before re-review

1. **D1** — correct the L12 answer in `progress/impl_projector_read_model.md` §2 and in `design.md` §10 row L12 / §10.6, with the enumerating evidence for the corrected claim. Keep the production code; state why the identical-call rule is retained anyway.
2. **D2** — make `L12_AHandBuiltStringAlias…` assert the outcome it observes (or delete it and move the finding to the record), and delete `SourceFileHashIsStableAcrossRepeatedReads` or strip its false "mutation-applied proof" claim.
3. **D3** — rewrite `progress/current.md`'s body so it describes what was actually done, group M included, with the correct counts.
4. **A1–A6** — address or explicitly accept, in the record. At minimum: fix the sweep totals sentence (A1), fix the code-85 literal in the thrown message (A2), and reword the `G4`/`G5`/`I8`/`PR15` boxes in `tasks.md` to the shapes actually delivered, per that file's own rule 3, or deliver the missing halves.

Nothing here requires touching a production behaviour, and none of the ten properties I attacked failed. This is a strong implementation with a record that misreports the one thing it was specifically told to report.

## 10. Note on phase closure

Id 24 is **not** the last feature of phase 12: ids **58** (`notification_envelope_copy_is_unguarded`) and **59** (`date_typed_payload_sites_survive_mutation`) are also filed against phase 12 and remain `pending`. No phase-12 closing assessment is due at this feature's close, whatever its verdict.

---

**Status set:** `in_review` → `in_progress`.

---

# Round 2 — review of the fix round (`progress/impl_projector_read_model.md` §13)

> **This section is additive. Nothing in round 1 above is amended, retracted or reopened.** Round 1's verdict and its evidence stand as written; what follows is a second, independent pass over what the fix round changed.

**Verdict: REJECTED.**

**D1's operational answer is now correct and I reproduced it independently. D2 and D3 are closed, A3 is genuinely closed rather than reworded, and the suite arithmetic is exactly what was claimed.** One new blocking defect, **D4**, is the *mechanism* the corrected L12 row gives for that correct answer: it says the server **normalises** the `$type` alias to the numeric code **when the index specification is stored**, "whichever alias created it". That is false on `mongo:8.3.8`, in one command and again through the real driver. The stored spec keeps whichever rendering created it; what the server does is compare the two renderings **as equivalent**. Same row, same class as round 1 — an engine claim generalised from a probe run in one direction only — now additionally baked into a **test's name and its doc-comment**, where the next reader (feature 25, and #9 on PyMongo, whose idiomatic filter is exactly the hand-built `{"$type": "string"}` dict) will take it as confirmed.

The fix is smaller than round 1's: one clause in four documents, one test name, and — recommended, and cheap — the reverse creation order added to the L12 case, which would make the row self-checking for the first time.

## R2.1 What I ran, and what I did not

Re-run in full, because the claim under test *is* about the full suite and about a suite that **shrank**:

- `dotnet build OrderToCash.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test OrderToCash.sln --no-build` → **1194 passed, 0 failed, 0 skipped**, 16 projects. Read off my own run: `Seed.UnitTests` 34, `Cqrs.UnitTests` 23, `Contracts.UnitTests` 21, `SharedKernel.UnitTests` 50, `Notifications.UnitTests` 58, `Fulfillment.UnitTests` 119, `Billing.UnitTests` 226, `Orders.UnitTests` 280, `Architecture.Tests` 16, `Projector.UnitTests` **87**, `Seed.IntegrationTests` 6, `Projector.IntegrationTests` 52, `Notifications.IntegrationTests` 12, `Fulfillment.IntegrationTests` 56, `Billing.IntegrationTests` 83, `Orders.IntegrationTests` 71. Sum **1194**.
- **The arithmetic of the drop, checked project by project against round 1's own run rather than against the implementer's summary:** every one of the sixteen projects has the identical count to round 1 except `Projector.UnitTests`, 88 → 87. `Skipped: 0` on all sixteen lines. The suite shrank by exactly the one test D2(b) deleted, and nothing is hiding behind a skip.
- `dotnet format OrderToCash.sln --verify-no-changes` → exit **0**. `./init.sh` → exit **0** (58 features, id 24 `in_review` on entry, one `in_review`, zero `in_progress`).
- `git status --porcelain` → **36 lines**, every one under `src/Projector/**`, `tests/Projector.*/**`, `OrderToCash.sln`, `specs/**`, `feature_list.json`, `progress/**` — enumerated with an inverted-grep allow-list that would have printed any line outside it, and it printed none.
- I did **not** re-run `./quality.sh` end to end; as in round 1 I ran its format, build and test steps individually and read its coverage step (finding **A7**, unchanged and correctly owned by feature 34).

Five mutation probes of my own, below. Every one was applied by an asserted single-occurrence replacement, force-rebuilt (`--no-incremental` on the owning project), run, then restored from a `cp` backup taken first, `cmp`-verified byte-identical against that backup, the changed line re-read, the solution force-rebuilt again and the two projector suites re-run green (**87/87** and **52/52**) on the restored tree.

## R2.2 D1 — the corrected answer is right; the corrected mechanism is wrong (new defect **D4**)

**What is now true and independently confirmed.** My own `mongosh` against the live `otcnet-mongodb` (`mongo:8.3.8`), both creation orders plus a control:

```
numeric created
A) numeric-then-string-alias: ACCEPTED, no conflict
stored: [... "name":"uq_order_reference","unique":true,"partialFilterExpression":{"orderReference":{"$type":2}}]
string alias created first
B) string-alias-then-numeric: ACCEPTED, no conflict
stored: [... "name":"uq_order_reference","unique":true,"partialFilterExpression":{"orderReference":{"$type":"string"}}]
C) CONTROL different filter CONFLICT: IndexKeySpecsConflict / 86
```

So the hazard row L12 originally named does **not** exist, in **either** order — the fix round's headline correction is right, and the control shows the server does still refuse a genuinely different filter, so this is not a server that accepts anything.

**What is false.** Row **B** disproves the corrected mechanism. `specs/projector_read_model/design.md:617` (row **L12**) says *"A real server **normalises** the `$type` alias to the numeric BSON code when the index specification is stored … `getIndexes()` shows the stored `partialFilterExpression` is `{'$type': 2}` whichever alias created it"*, and `design.md` §10.6 repeats it (*"which normalises the string alias to the numeric code before storing the index"*, *"the server erases it"*). The stored spec is **not** normalised: it is whatever created the index. The equivalence lives in the server's **comparison** of index specifications, not in storage. The same sentence appears in `progress/impl_projector_read_model.md` §2 (L12 bullet) and §13, in `specs/projector_read_model/requirements.md`'s `PR22`/`PR39` row, and in the test's own name and XML doc at `tests/Projector.IntegrationTests/ReadModelIndexesTests.cs:77-97`.

**Probe P2 — the same falsification through the real driver, not just the shell.** I reversed the creation order inside the delivered L12 case: hand-built string-alias index first, then `MongoSeedWriter.EnsureIndexesAsync` (the seed's numeric rendering), assertions otherwise untouched.

```
Failed  ReadModelIndexesTests.L12_AHandBuiltStringAliasPartialFilterAgainstTheSeedsNumericOneRaisesNoConflict_TheServerNormalisesTheAliasToTheNumericCode
   System.InvalidCastException : Unable to cast object of type 'MongoDB.Bson.BsonString' to type 'MongoDB.Bson.BsonInt32'.
   at …ReadModelIndexesTests.cs:line 121
```

Note **where** it failed: `Assert.Null(exception)` on line 114 **passed** — the seed's numeric `EnsureIndexesAsync` raised nothing against an alias-created index, confirming "no conflict, either order" a third time — and the failure is on line 121, the stored-type assertion, because the stored `$type` is the **string** `"string"`. The delivered test only ever creates numeric-first, so it cannot see this, and its name asserts it anyway.

**Why this is blocking rather than an advisory.** It is the same row, the same failure shape and the same cost as round 1: an engine property recorded as confirmed on the strength of a one-directional probe (the implementer's own transcript in §13 runs numeric-then-string only, and `M1`'s live index was likewise created by the seed). The difference is that the false clause is now inside a **test name** — `…_TheServerNormalisesTheAliasToTheNumericCode` — which is precisely the *tick that stops anyone re-checking*, and inside the artefact `CLAUDE.md` says nothing else in this harness can see. For #9 it inverts in practice: PyMongo's idiomatic filter is a hand-built dict, so the alias-created index is the **likely** stored form there, and a reader who trusts this row will write a check that cannot hold.

**To be precise about what is not wrong:** the production code is unchanged and correct; `PR39` is non-vacuous (round 1's probe 10); `Assert.Null(exception)` and the stored-type assertion are both falsifiable (probes **P1** and **P2**); and the operational conclusion in the record — no conflict, either order, keep the identical `Builders<>.Filter.Type` call — is true.

## R2.3 D2 — both closed

**(a) The `Assert.True(true, …)` pair is gone and the replacement is armed. I ran the implementer's own swap.** Probe **P1**: `Assert.Equal(2, storedIndex…)` → `Assert.Equal(3, …)`, force-rebuilt, run:

```
Failed  ReadModelIndexesTests.L12_AHandBuiltStringAliasPartialFilterAgainstTheSeedsNumericOneRaisesNoConflict_…
   Assert.Equal() Failure: Values differ
Failed!  - Failed: 1, Passed: 3, Skipped: 0, Total: 4
```

The reported arming is real. Probe **P2** additionally shows the `Assert.Null(exception)` half is exercised rather than decorative — it is evaluated, and passes, on a case the delivered file never runs.

**(b) Deleting `SourceFileHashIsStableAcrossRepeatedReads` was the right call, and I checked that the property it gestured at still has a guard.** Probe **P5**: `AutoOffsetReset.Earliest` → `Latest` at the **assignment site** (`KafkaFactStreamSubscriber.cs:95`, not the `<see cref="…"/>` remarks):

```
Failed  KafkaFactStreamSubscriberConfigTests.PR38_BuildConsumerConfig_SetsEarliestNotLatest_BecauseTheReadModelIsRebuiltByReplay
   Assert.Equal() Failure: Values differ
Failed!  - Failed: 1, Passed: 86, Skipped: 0, Total: 87
```

`PR38` reads the real private `BuildConsumerConfig` by reflection and dies on the real mutation, and `OffsetContractTests`' two live cases (round 1, §2) do too. The deleted test guarded nothing that these do not; nothing lost.

## R2.4 A3 — genuinely closed, and armed in **both** mutation families

`UpdateSignalTests.cs:174-210` now applies `order.despatched.v1` after `order.confirmed.v1` and asserts, on a second frame, both `status == "despatched"` and `references.despatchReference == "DES-POSTAPPLY"` — a value the test itself supplies — having first asserted the reference is **absent** on the earlier frame. Two probes:

- **P3, payload corruption** (the family round 1 warned is the one that gets skipped): `NatsUpdateSignalPublisher.cs:36`, `new OrderStreamReferences(document.DespatchReference, …)` → `(document.InvoiceReference, …)`. → **only** `PR42` fails, `Failed: 1, Passed: 4`. The reference half is not decorative.
- **P4, the original K5 deletion** re-run against the strengthened case: `ReturnDocument = ReturnDocument.After` removed from `IdempotentConsumer.cs:59`. → **only** `PR42` fails, `Assert.Equal() Failure: Strings differ / Expected: "confirmed" / Actual: "placed"`, `Failed: 1, Passed: 4` — the ledger's L9 asymmetry preserved, verbatim as recorded.

## R2.5 The declined advisories, judged one by one

| Advisory | Response | My judgement |
|---|---|---|
| **A1** sweep totals | Fixed: `17+3+1=21` pre-fix, `19+1+1=21` post-fix, Sentinel B's required survival now counted | **Accepted.** Both sum, and the classification matches what I read in `results.tsv` in round 1 (`SENT_B`, `M06`, `M07`) |
| **A2** wrong code in the thrown message | Fixed in code: `ReadModelIndexes.cs:74` now interpolates `ex.CodeName`/`ex.Code` | **Accepted**, read on disk. Small carry-forward: no test asserts the message reports the *caught* code, so a regression to a literal would pass — `PR22` asserts only the index name and `dropIndex`. Advisory **A8** below |
| **A4** `G4`'s two undelivered clauses | Reworded per the file's own rule 3, naming `ProjectionConcurrencyTests.PR7_…` (8 concurrent clients, `callbackCount == 1`) as where the property actually lives | **Accepted.** I armed `PR7` in round 1 (probe 3); the box is now honest about where the guard is, which was the whole of the finding |
| **A5** `I8`'s exclusion list is control flow | Reworded, refactor declined as out of proportion to a record-only fix round | **Accepted, with the gap left standing:** what `I8` asked for — a declared list a future widening would diff against — still does not exist, so widening the exclusions remains a silent code change. Reasonable to decline inside this round; it should not be forgotten if the oracle is ever reused |
| **A6** `PR15` does not go through Kafka | Recorded in §9's caveats; restructuring declined | **Accepted.** The divergence is now disclosed where a reader will find it, and the broker path is covered by `OffsetContractTests` and M3/M5 |
| **A7** coverage gate inert | Left untouched as feature 34's | **Correct.** Not this feature's to fix; `CHECKPOINTS.md` C4's coverage box stays unticked, as in round 1 |

## R2.6 What the fix round disturbed, checked

- **`tasks.md`** — 99 boxes, **99 ticked, 0 unticked**, and `git diff -U0` shows **every** changed line is a checkbox line (0 non-checkbox lines changed): the rewordings sit inside the task lines they belong to, and no task text was quietly relocated or dropped.
- **`specs/shared/test-matrix.md`** — the summary was **recomputed, not hand-adjusted**. I enumerated all **63** requirement rows from the Status column mechanically: **49 DONE, 4 scoped, 10 TODO**, summing to 63, exactly the `| **Total** | **R1 – R63** | **63** | **49** | **4** | **10** |` row; feature 7's row (`R50 – R55`: 6/4/0/2) matches R50–R53 DONE and R54/R55 TODO one for one.
- **`requirements.md`** — the `PR22`/`PR39` row cites the **renamed** L12 case; a repository-wide `grep` for the old name and for `SourceFileHashIsStable` returns hits **only** inside round 1 of this review file and inside §13's own narration of the change. No stale citation anywhere in `specs/`, `src/` or `tests/`.
- **`progress/current.md`** — the leader's correction is intact; the implementer did not write to it again; the offending phrase survives only as a quotation inside the note explaining it. Round 1's **D3** is closed. One advisory: its **Status** line still reads `in_progress — … fix round in flight` while the backlog says `in_review`, and the file carries two `## Notes` headings — leader's file, leader's to refresh (advisory **A9**).
- **`feature_list.json`** — the fix round's only change is id 24's status line; `git diff` shows that one line and nothing else.

## R2.7 New advisories from this round

- **A8 — the A2 message fix has no guard.** `ReadModelIndexes.cs:70-77` now reports `ex.CodeName`/`ex.Code`, but `PR22_RefusesToStartAgainstANonPartialIndexOfTheSameName_NamingTheIndex` asserts only `Contains(indexName)` and `Contains("dropIndex")`. A regression to a hard-coded "code 85" would leave the suite green. One `Assert.Contains("IndexKeySpecsConflict", …)` would close it; not blocking, because the operator-facing text is correct today and the catch clause itself is armed.
- **A9 — `progress/current.md`'s status line is one transition stale** (`in_progress` while the backlog says `in_review`) and the file has two `## Notes` sections. It is the leader's file and I have not touched it; noted so the next transition rewrites the whole body, which is the rule the file itself states.

## R2.8 `CHECKPOINTS.md` walk — round 2

Only boxes whose evidence changed since round 1 are re-argued; the rest are re-affirmed on round 1's evidence plus this round's clean full run.

### C1 — the harness is complete

- [x] All harness files present; `./init.sh` exit **0**, run by me this round.

### C2 — state is coherent

- [x] At most one feature `in_progress` — zero `in_progress`, one `in_review` (id 24), 36 `done`, 21 `pending`, 58 total.
- [x] Every status is in `rules.valid_status` — `init.sh` validator, exit 0.
- [x] Every `done` feature has passing tests — 1194/1194, 0 skipped, my own clean run.
- [x] **`progress/current.md` describes the active session** — round 1's **D3** is closed; the body now describes what was actually done and why. Advisory **A9** on the stale status line only.
- [x] Every `blocked` feature records why — none are blocked.

### C3 — architecture is respected

- [x] All boxes as round 1, on round 1's armed evidence (domain purity, no cross-service DB access, shared-code list, no `Cqrs` in `Domain/`, `SharedKernel` zero packages, no `decimal` in domain arithmetic, Kafka-fact/NATS-RPC classification, no stray TODOs). Nothing the fix round touched moves any of them: the only production edit was `ReadModelIndexes.cs`'s exception message.

### C4 — verification is real

- [x] Format check + build + test pass — run individually by me (`dotnet format --verify-no-changes` exit 0; `--no-incremental` build 0/0; 1194/1194, 0 skipped).
- [x] Domain tests are pure; integration tests hit real containers (`mongo:8.3.8`, `apache/kafka:4.3.1`, `nats:2.14.5-alpine`).
- [ ] **Coverage thresholds met (≥80% domain, ≥60% overall)** — still **cannot be ticked**; `quality.sh` enforces no threshold (**A7**), owned by feature 34.
- [x] No Jest anywhere.

### C5 — the session closed cleanly

- [x] No suspicious untracked files — 36 lines, all inside the allow-list, enumerated by inverted grep.
- [ ] **`progress/history.md` has an entry for the feature, with its effort record** — not appended; this is a rejection.
- [x] `feature_list.json` reflects the true state — set back to `in_progress` by this review, one line, by direct edit, no `git checkout`.
- [x] The human has been told what was done and how to test it — `progress/impl_projector_read_model.md` §12 and §13.
- [x] Claude did not commit — no new commits; working tree dirty.

### C6 — Spec-Driven Development

- [x] `specs/projector_read_model/` has all three documents.
- [x] EARS `PR<n>` ids throughout.
- [x] All 99 tasks ticked, and the `G4`/`G5`/`I8` boxes now carry the rule-3 rewording round 1 asked for.
- [x] Every `R<n>` covered by a named test recorded in `specs/shared/test-matrix.md` — summary re-enumerated by me, 49/4/10 of 63.
- [ ] **`design.md` is true where it is most load-bearing** — row **L12** and §10.6 state a mechanism that is false on this engine. **Defect D4.**
- [x] The spec commit precedes the implementation commit.

### C7 — spec-reuse fidelity and benchmark honesty

- [x] `specs/shared/` byte-identical to #7's except `test-matrix.md` — round 1's `diff -rq`; this round changed nothing under `specs/shared/` beyond that file.
- [x] Every deviation is a recorded amendment — none made.
- [x] The `R<n>` ids are #7's.
- [ ] n8n workflows / black-box API script — not applicable at this feature (Gateway is feature 25's).
- [ ] `progress/history.md` effort records complete — pending, a rejection adds none.
- [x] README benchmark section — untouched, out of scope.

## R2.9 `R<n>` → test mapping — re-verified

`R50`–`R53` DONE, `R54`/`R55` projector-half DONE with the ratified gateway/web halves outstanding: unchanged from round 1's table and re-confirmed against a green `Projector.IntegrationTests` 52/52 and `Projector.UnitTests` 87/87 on the restored tree. The only mapping the fix round altered is the `PR22`/`PR39` row's citation of the renamed L12 case, which now matches the file on disk. **D4 is not a traceability failure** — every requirement still names a real, non-vacuous test — which is exactly why it needed a ledger to be visible at all.

## R2.10 What must change before re-review

1. **D4 — state the mechanism the server actually implements**, in all five places that currently state the wrong one: `specs/projector_read_model/design.md` row **L12** (line 617) and **§10.6**; `progress/impl_projector_read_model.md` §2's L12 bullet and §13's D1 paragraph; `specs/projector_read_model/requirements.md`'s `PR22`/`PR39` row. The observed behaviour, in one sentence: *`mongo:8.3.8` treats `{$type: "string"}` and `{$type: 2}` as the **same** partial filter when it compares an existing index against a requested one — so neither creation order conflicts — while **storing** whichever rendering created the index; `getIndexes()` returns the alias if the alias created it.* Include the reverse-order evidence (probe **P2** above, or your own re-run of it), because the claim that was wrong is precisely the one nobody ran in that direction.
2. **D4 — rename the L12 test and rewrite its doc-comment** so neither asserts normalisation. Its subject is *no conflict against the seed's rendering*; its stored-`$type` assertion holds because the **seed** created the index, not because the server canonicalises anything.
3. **Recommended, and cheap: make the row self-checking.** Add the reverse order to the same case — hand-built alias index first, then `MongoSeedWriter.EnsureIndexesAsync`, `Assert.Null(exception)`, and assert the stored `$type` equals the rendering that created it in each direction. That is roughly the ten lines of probe P2 with a corrected expectation, it runs in under 100 ms, and it would turn L12 from a row that has now been wrong twice into the one row in the ledger that cannot be wrong again.
4. **A8** — optional: assert the caught code name appears in the thrown message, so A2's fix has a guard.
5. Nothing else. **No production behaviour needs to change**, and no test needs to be added or deleted beyond point 3.

## R2.11 Carried forward for the effort record (to be written when this feature is approved)

Not appended now — `progress/history.md` gets no entry for a rejected feature. When id 24 is approved, its entry must carry:

- **#7's baseline for this feature:** 1 spec session + 1 gate + 1 implementation session + 1 bookkeeping-fix pass + 2 review passes, rejected once **on bookkeeping only**; and, separately, an entire **second spec pass eight phases later** (Amendment A1) for timeline ordering. **#8 folded A1 into its first draft** because backlog id 57 closed the underlying defect the day before — a dividend that shows up as work that did not happen, and it should be named as such.
- **The confound column**, per defect: **D1** — #7 could not have caught it (it has no ported-idiom ledger; the mechanism was adopted at #8's Phase-8 gate). **D2** — partly; #7 did not audit test falsifiability, and the `Assert.True(true)` shape is the same class as #8's own feature-19 finding, which post-dates #7. **D3** — **yes**, and this is the sharpest line in the record: it is the same box #7 was rejected on for this same feature, inherited into this task list as prevention (task `N7`), and it happened anyway. **An inherited prevention task that did not prevent** — state it plainly, because it is the most useful datum this feature produced about how much of #7's experience actually transfers. **D4** — no; same reasoning as D1.
- **Round 1's process note, and its second occurrence.** The reviewer's own first `PR45` mutation did not compile, the quiet build output hid it, the test ran against the **previously built binary** and reported a false green. That is the arming protocol's own hazard biting the person applying the protocol, and this build has now met the stale-binary/quiet-output family more than once (feature 7, and round 1 here). Both rounds of this review therefore forced `--no-incremental` on the owning project before every probe and re-ran the two projector suites green on the restored tree.
- **Two review rounds, both rejecting on records rather than code**, against a production implementation in which **sixteen independent mutations across the two rounds** all bit. That asymmetry is itself the finding worth measuring.

## R2.12 Phase closure — nothing is due

Id 24 is **not** the last feature of phase 12. Ids **58** (`notification_envelope_copy_is_unguarded`) and **59** (`date_typed_payload_sites_survive_mutation`) are filed against phase 12 and are both still `pending`, confirmed by reading `feature_list.json` this round. **No phase-12 closing assessment is due at this feature's close, whatever its verdict** — the next session should not go looking for one.

---

**Status set (round 2):** `in_review` → `in_progress`.

---

# Round 3 — review of the second fix round (`progress/impl_projector_read_model.md` §14)

> **This section is additive. Nothing in rounds 1 and 2 above is amended, retracted or reopened.** Their verdicts and evidence stand as written; what follows is a third, independent pass over what the second fix round changed.

**Verdict: REJECTED.**

**The corrected mechanism is right, and I confirmed it myself in both directions plus two controls neither prior round ran. The reverse-order extension is real and genuinely falsifiable in both orders — I armed it three ways, including a value-level probe stronger than the one the implementer recorded. The five places the round-2 list named are all corrected, and nothing else drifted across three passes.** One blocking defect, **D5**: the fix reached the five places **the round-2 list named** and missed **two more that state the original, disproved hazard as live fact** — one of them in **production source** (`src/Projector/Infrastructure/Persistence/ReadModelIndexes.cs:11-13`), one four lines above the corrected case in the test file itself (`tests/Projector.IntegrationTests/ReadModelIndexesTests.cs:13-15`) — plus the ticked task box `H1` that says the same thing. All three assert *"neither service c(oul)d then start after the other"*, which is exactly what round 1 disproved, what round 2 reconfirmed, and what I have now measured a third time in both creation orders.

The root cause is one this repository already names, applied to a fix rather than to a test: **round 2's own list of five places was prose, not a search result**, and the fix round took the list as the enumeration. Everyone — the implementer and me, twice — grepped for the *mechanism* word (`normalis`). The residue carries the *hazard* wording instead, and one `grep -rn "start after the other"` prints it in under a second. That command is in **R3.4** with its complete output.

The fix is one comment block in two files and one task-box parenthetical. **No production behaviour, no test, no assertion changes**, and the arming I did this round does not need repeating.

## R3.1 What I ran, and what I did not

The claim under test *is* about the full suite, and the tree changed in a compiled test project, so I re-ran it once cleanly on the restored tree:

- `dotnet build OrderToCash.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test OrderToCash.sln --no-build` → **1194 passed, 0 failed, 0 skipped**, exit 0, 16 projects. Read off my own run: `Cqrs.UnitTests` 23, `SharedKernel.UnitTests` 50, `Seed.UnitTests` 34, `Contracts.UnitTests` 21, `Notifications.UnitTests` 58, `Fulfillment.UnitTests` 119, `Orders.UnitTests` 280, `Billing.UnitTests` 226, `Architecture.Tests` 16, `Projector.UnitTests` **87**, `Seed.IntegrationTests` 6, `Projector.IntegrationTests` **52**, `Notifications.IntegrationTests` 12, `Fulfillment.IntegrationTests` 56, `Billing.IntegrationTests` 83, `Orders.IntegrationTests` 71. Summed mechanically from the sixteen result lines: **1194 passed, 0 failed, 0 skipped**. Identical to round 2's per-project figures on every one of the sixteen, which is the check that the rewritten L12 case replaced its predecessor rather than being added alongside it — `Projector.IntegrationTests` is still **52**, as §14 claims.
- `dotnet format OrderToCash.sln --verify-no-changes` → exit **0**. `./init.sh` → exit **0** (58 features, one `in_review`, zero `in_progress`, 37 uncommitted changes warned as expected mid-session).
- I did **not** re-run `./quality.sh` end to end. Its steps are format, build, test and coverage; I ran the first three individually above and read the fourth — finding **A7**, unchanged, inert by disclosure and owned by feature 34.
- Seven probes of my own: four directly against the live `mongo:8.3.8` server (**R3.2**) and three mutations of the delivered test through the real driver (**R3.3**). Every mutation was applied by an asserted single-occurrence replacement, the owning project force-rebuilt with `--no-incremental`, the test run, then the file restored from a `cp` backup taken first, `cmp`-verified byte-identical, the changed lines re-read, and the solution force-rebuilt before the confirming full run above. SHA-256 of `ReadModelIndexesTests.cs` before the first probe and after the last restore: `411c4188f760d2a8e521b5579f29b3e12a460bacc1eb50d707791dfccf51b0cb` both times.

## R3.2 The corrected mechanism — run both ways myself, plus two controls nobody had run

This is the third statement of this claim, so I did not read it — I measured it. Directly against the running `otcnet-mongodb` (`mongo:8.3.8`), on four fresh databases:

```
A) numeric created
A) string-alias-second: ACCEPTED, no conflict
   stored: partialFilterExpression: { orderReference: { '$type': 2 } }
B) string alias created
B) numeric-second: ACCEPTED, no conflict
   stored: partialFilterExpression: { orderReference: { '$type': 'string' } }
C) CONTROL different filter ($type:2 then $exists:true) CONFLICT: IndexKeySpecsConflict / 86
D) CONTROL string-vs-int alias ($type:"string" then $type:"int") CONFLICT: IndexKeySpecsConflict / 86
```

**A and B confirm the corrected row exactly:** neither creation order conflicts, and the stored spec is whichever rendering created the index — not a normalised form. **C and D are new**, and they are what makes the corrected wording *precise* rather than merely *not-wrong*. Round 2 ran only control C (a structurally different operator), which leaves open the reading that the server ignores the partial filter's contents when an index of that name already exists. **D closes that**: two *string aliases* that name different BSON types (`"string"` vs `"int"`) **do** conflict. So the server is genuinely comparing the filter semantically and treating `"string"` and `2` as the same value of the same predicate — which is the mechanism the row now states, established rather than inferred.

**The server said so itself, in my probe P3c below**, in the words of its own error: `Requested index: { … partialFilterExpression: { orderReference: { $type: 2 } } }, existing index: { … partialFilterExpression: { orderReference: { $type: "int" } } }`. The requested spec reaches the comparison **unnormalised**, as `2`, and is compared against the stored alias. That is direct evidence for comparison-time equivalence and against storage-time normalisation, from the engine rather than from a reading of it.

**Verdict on the mechanism: correct as stated, in `design.md:617`, `design.md:683`, `requirements.md:198`, `impl` §2, `impl` §14 and the test's own doc-comment.** I accept it, on my own measurements, in both directions.

## R3.3 The reverse-order extension and its arming — three probes, and one caveat on the implementer's own

The case now runs both creation orders on two fresh databases and makes four assertions. The question the brief asks is not whether it fails, but whether **both orders assert something that can fail**, and whether it is asserting more than "an exception was thrown". Three probes:

| Probe | Mutation | Result |
|---|---|---|
| **P3a** | Direction **B**'s stored-type assertion `Assert.Equal("string", …AsString)` → `Assert.Equal("int", …)` — the type preserved, only the **value** wrong | **Fails**: `Assert.Equal() Failure: Strings differ / Expected: "int" / Actual: "string"`, `Failed: 1, Passed: 3, Total: 4`. The stored value is genuinely read out of the server and compared |
| **P3b** | Direction **A**'s `Assert.Equal(2, …AsInt32)` → `Assert.Equal(4, …)` | **Fails**: `Assert.Equal() Failure: Values differ / Expected: 4 / Actual: 2` at line 131. Direction A is not carried by direction B |
| **P3c** | Direction **B**'s *pre-created* index given a genuinely divergent filter (`{"$type": "int"}` instead of `"string"`), so the seed's numeric `EnsureIndexesAsync` should now conflict | **Fails**: `Assert.Null() Failure: Value is not null / Actual: MongoCommandException: Command createIndexes failed: An existing index has the same name as the requested index…` at line 147. Direction B's **no-conflict** half is live, falsifiable **by the server**, and not decorative |

So: both orders assert a value and both orders assert an outcome, and each half fails independently. **The extension is real.**

**One caveat on the arming recorded in §14, not blocking.** The implementer armed direction B by mutating `Assert.Equal("string", …AsString)` to `Assert.Equal(2, …AsInt32)`, which fails with `InvalidCastException`. That is a **type** mismatch, and a cast exception would fire equally if the assertion were reading the wrong field, a missing key, or any other `BsonString` — it proves the stored `$type` is not an `Int32`, which is weaker than proving the assertion compares the value it names. **P3a supplies the missing half**: with the type held constant, a wrong expected value still fails. This is the corruption-family point `CLAUDE.md` makes about probes that only bite on shape; recorded so the arming table is read for what it proves.

## R3.4 D5 (blocking) — the fix reached the five places the list named, and the claim lives in two more

**Where:**

- `src/Projector/Infrastructure/Persistence/ReadModelIndexes.cs:8-13` — **production source**, the class summary of the file the L12 row is *about*:

```
/// the IDENTICAL Builders<BsonDocument>.Filter.Type(field, BsonType.String)
/// call MongoSeedWriter.EnsureIndexesAsync uses (ledger L12) —
/// a hand-written { $type: "string" } BsonDocument renders
/// differently (BSON's numeric type code versus the string alias) and
/// neither service could then start after the other.
```

- `tests/Projector.IntegrationTests/ReadModelIndexesTests.cs:10-16` — the class summary of the file containing the corrected case, four lines above `PR39` and sixty above `L12_…InBothCreationOrders`: *"…the partial filter's stored form must be built with the IDENTICAL … call the seed uses, **or the two definitions differ and neither service can start after the other**"*, attributed to *"the row design.md §10.6 names as 'most likely to bite'"* — a §10.6 that now says, at line 683, that L12 **did not** bite.
- `specs/projector_read_model/tasks.md:91`, task **H1**, ticked: *"(ledger **L12** — a hand-written `{ $type: "string" }` `BsonDocument` renders differently and neither service could then start after the other) … **fails loudly** on `IndexOptionsConflict` (code 85)"*. Both halves are now known false: the hazard does not exist in either order, and the code a real server raises for `PR22`'s own scenario is **86**, which `ReadModelIndexes.cs:44-62`'s own remarks state correctly. `tasks.md`'s rule 3 rewording was applied to `G4`, `G5` and `I8` in the first fix round; this box carries a stronger claim than any of those and was not touched.

**The enumerating command and its complete output**, so that a missed hit would be an unclassified line rather than an unmentioned one:

```
$ grep -rn "start after the other" --include='*.cs' --include='*.md' . | grep -v '/bin/\|/obj/'
progress/spec_projector_read_model.md:43   → spec-phase session record (open point 23), written before implementation — history, unmarked (advisory A11)
progress/spec_projector_read_model.md:70   → same file, same status — history, unmarked (advisory A11)
specs/projector_read_model/tasks.md:91     → LIVE, ticked task H1 — defect D5
specs/projector_read_model/design.md:683   → narrated history, both wrong versions named as superseded — correct
progress/review_projector_read_model.md:112 → round 1 of this file, quoting the claim it disproved — correct
src/Projector/Infrastructure/Persistence/ReadModelIndexes.cs:13 → LIVE, production source — defect D5
tests/Projector.IntegrationTests/ReadModelIndexesTests.cs:15    → LIVE, test class summary — defect D5
```

Two supporting sweeps, both complete: `grep -rn "renders differently\|string alias\|numeric type code\|numeric code\|numeric BSON" src/ tests/` returns four hits — `ReadModelIndexes.cs:12` (the defect) and three lines inside the corrected L12 case that are true as written; `grep -rn "L12" src/ tests/` returns six, of which three are Billing's unrelated `L12` rows, one is `ReadModelIndexes.cs:10` (the defect's opening line) and two are the corrected test.

**Why this is blocking rather than an advisory.** Three reasons, in order of weight.

First, **it is in production source.** Every previous instance of this claim lived in a document or a test; this one is the class summary of `ReadModelIndexes.cs`, the file whose one design decision the whole L12 row exists to justify. A reader of that file — feature 25's implementer, or #9's — is told the hazard is real, and told it in the same breath as the rule it is offered as the reason for. The rule survives the correction; **the reason given for it does not**, and the corrected record says so explicitly (*"not because a conflict was observed … but because it is the cheaper, self-documenting form"*).

Second, **it is the artefact class this repository says nothing else can see.** Round 2 rejected on precisely this ground — a false engine claim in a test's name and doc-comment — and approving the identical claim in a production doc-comment one round later would apply a lower standard to the same defect at the moment it ships. Traceability is green, the suite is green, every mutation bites; none of that can see a false sentence, which is the entire reason the ledger convention exists.

Third, **the file now contradicts itself.** `ReadModelIndexesTests.cs` asserts at line 110 that neither order conflicts, and states at line 15 that neither service can start after the other. `ReadModelIndexes.cs` says at line 13 that the two renderings differ, and at lines 49-56 gives an accurate account of the two conflict codes that only makes sense if the server compares specs semantically. Whichever a reader trusts, one of them has taught them something false.

**Where this came from, and the transferable lesson — which is partly this reviewer's.** Round 2's `R2.10` point 1 enumerated *"all five places that currently state the wrong one"*. That list was a **prose sweep**, produced by searching for the mechanism sentence, and the fix round reasonably treated it as the enumeration — §14 says *"No file outside those five … was touched"*, which is a true statement about **scope** offered where a statement about **completeness** was needed. `CLAUDE.md`'s rule — *a negative claim about the repository is a search result, not a reading* — applies to *"the wrong claim now appears nowhere"* exactly as it applies to *"no test does X"*. The specific trap here is worth naming for #9: **when a claim is corrected, the search that proves the correction landed must be keyed to the OLD claim's wording, not the new one.** A grep for `normalis` finds every place the *second* wrong version was fixed and cannot, by construction, find the places carrying the *first*.

## R3.5 What the three passes disturbed — checked, and nothing did

- **`specs/projector_read_model/design.md`** — `git diff --stat` shows **3 insertions, 3 deletions**, and `git diff -U0` shows the changed lines are exactly **616** (L11's code-85/86 correction, round 1), **617** (row L12) and **683** (§10.6). No other row of the 40+-row ledger moved, and the L12 row's Guard column names the renamed test.
- **`specs/projector_read_model/tasks.md`** — **99 boxes, 99 ticked, 0 unticked**, and of the 99 changed lines in `git diff -U0`, **99 are checkbox lines and 0 are anything else**: no task text was relocated, dropped or added outside a box. (This is also how D5's third site is visible: H1's text was reworded by nobody, because rule-3 rewording was applied only where round 1 asked for it.)
- **`specs/shared/test-matrix.md`** — untouched this round; re-enumerated mechanically from the Status column all the same: **63 rows → 49 DONE, 10 TODO, 4 partial/scoped**, matching the `| **Total** | **R1 – R63** | **63** | **49** | **4** | **10** |` row. The only hunks against HEAD are the two summary lines (78, 81) and §7's six rows (174-179), as in round 2.
- **`specs/projector_read_model/requirements.md`** — the diff is the traceability table being filled in (rounds 1-2) plus four lines at 162; the `PR22`/`PR39` row cites the renamed case, and a repository-wide grep for the two deleted test names (`…TheServerNormalisesTheAliasToTheNumericCode`, `SourceFileHashIsStable`) returns hits **only** inside rounds 1-2 of this review file and inside §13/§14's own narration of the renames — no stale citation in `specs/`, `src/` or `tests/`.
- **The working tree** — `git status --porcelain` is **37 lines**. Round 2 saw 36; the difference is `M CLAUDE.md`, the leader's own addition of the both-directions ledger convention, which I read on disk (lines 92 and 94) and which this round's D5 is an application of. Every other line is inside the same allow-list, checked by inverted grep that would have printed anything outside it and printed nothing.
- **Production code** — unchanged this round, as §14 states; the only production edit in any round is `ReadModelIndexes.cs`'s exception message (round 1's A2).

## R3.6 Advisories — carried forward and new

- **A7** (coverage gate inert, feature 34's) — unchanged, correctly disclosed, still blocks `CHECKPOINTS.md` C4's coverage box.
- **A8** (the A2 message fix has no guard: `PR22` asserts only the index name and `dropIndex`, so a regression to a hard-coded "code 85" stays green) — still open, still not blocking.
- **A9** (`progress/current.md`'s **Status** line reads `in_progress — … fix round in flight` while the backlog says `in_review`, and the file carries two `## Notes` headings) — still open. Leader's file; noted, not touched.
- **A10 — new.** `specs/projector_read_model/requirements.md:114` (`PR22`'s EARS text) still names only `IndexOptionsConflict`, code 85, as the trigger; the code's own remarks (`ReadModelIndexes.cs:49-61`) and the traceability row both record that the real server raises **86** for that scenario. The requirement text and the implementation now disagree on the observed fact, with the correction living only in the row that cites the test. Worth folding into D5's pass, since H1 carries the same "code 85" clause.
- **A11 — new, and deliberately not blocking.** `progress/spec_projector_read_model.md:43` and `:70` still state the hazard as fact. That file is a dated spec-phase session record, and rewriting it would falsify the history of what was believed at the gate — the same argument that correctly kept `impl` §13 as written with a superseded-callout. If it is touched at all, the right shape is `impl` §13's: a one-line marker pointing at `design.md` §10.6, not an edit.

## R3.7 `CHECKPOINTS.md` walk — round 3

Only boxes whose evidence changed are re-argued; the rest are re-affirmed on rounds 1-2's armed evidence plus this round's clean full run.

### C1 — the harness is complete

- [x] All harness files present; `./init.sh` exit **0**, run by me this round.

### C2 — state is coherent

- [x] At most one feature `in_progress` — zero `in_progress`, one `in_review` (id 24), 36 `done`, 21 `pending`, 58 total, read out of `feature_list.json` this round.
- [x] Every status is in `rules.valid_status` — `init.sh` validator, exit 0.
- [x] Every `done` feature has passing tests — **1194/1194, 0 skipped**, my own run.
- [x] `progress/current.md` describes the active session — body correct since round 2; advisory **A9** on the stale status line only.
- [x] Every `blocked` feature records why — none are blocked.

### C3 — architecture is respected

- [x] All boxes as rounds 1-2, on their armed evidence. Nothing this round touched production code, and `Architecture.Tests` is 16/16 on my run. The one production file carrying **D5** carries it in a comment; the code it describes is correct and unchanged.

### C4 — verification is real

- [x] Format check + build + test pass — run individually by me (`dotnet format --verify-no-changes` exit 0; `--no-incremental` build 0/0; 1194/1194, 0 skipped). `./quality.sh` not re-run end to end.
- [x] Domain tests are pure; integration tests hit real containers (`mongo:8.3.8`, `apache/kafka:4.3.1`, `nats:2.14.5-alpine`) — and this round's three probes went through the real driver against a real server, not a fake.
- [ ] **Coverage thresholds met (≥80% domain, ≥60% overall)** — still **cannot be ticked** (**A7**), owned by feature 34.
- [x] No Jest anywhere.

### C5 — the session closed cleanly

- [x] No suspicious untracked files — 37 lines, all inside the allow-list, enumerated by inverted grep; the one line new since round 2 is the leader's `CLAUDE.md` amendment.
- [ ] **`progress/history.md` has an entry for the feature, with its effort record** — not appended; this is a rejection. The full text to append on approval is carried in **R3.10**.
- [x] `feature_list.json` reflects the true state — id 24 was `in_review` on entry; set back to `in_progress` by this review, one line, by direct edit, **no `git checkout`**.
- [x] The human has been told what was done and how to test it — `impl` §12, §13, §14.
- [x] Claude did not commit — no new commits; working tree dirty.

### C6 — Spec-Driven Development

- [x] `specs/projector_read_model/` has all three documents.
- [x] EARS `PR<n>` ids throughout.
- [ ] **All 99 tasks ticked over text that is true** — 99/99 ticked, but task **H1**'s own parenthetical states the disproved hazard and the superseded error code, where `G4`/`G5`/`I8` were reworded under the same rule. **Defect D5.**
- [x] Every `R<n>` covered by a named test recorded in `specs/shared/test-matrix.md` — re-enumerated by me, 49/4/10 of 63.
- [ ] **`design.md` and the code it describes are true where most load-bearing** — the ledger row and §10.6 are now correct and independently reconfirmed; `ReadModelIndexes.cs`'s own class summary is not. **Defect D5.**
- [x] The spec commit precedes the implementation commit.

### C7 — spec-reuse fidelity and benchmark honesty

- [x] `specs/shared/` byte-identical to #7's except `test-matrix.md` — round 1's `diff -rq`; nothing under `specs/shared/` changed this round.
- [x] Every deviation is a recorded amendment — none made.
- [x] The `R<n>` ids are #7's.
- [ ] n8n workflows / black-box API script — not applicable at this feature (Gateway is feature 25's).
- [ ] `progress/history.md` effort records complete — pending; a rejection adds none.
- [x] README benchmark section — untouched, out of scope.

## R3.8 `R<n>` → test mapping — re-verified

Unchanged from rounds 1-2 and re-confirmed against my own green run (`Projector.IntegrationTests` 52/52, `Projector.UnitTests` 87/87, `Orders.UnitTests` 280/280):

| Requirement | Test | Status this round |
|---|---|---|
| **R50** | `TimelineProjectionTests.R50_AppendsAnEntryCarryingEventIdEventTypeOccurredAtAndASummary_AndPresentsTheTimelineOrderedByOccurredAtRatherThanByArrival` | green; ordering half armed in round 1 (probes 2, 4, 5) |
| **R51** | `TimelineProjectionTests.R51_LeavesTheReadModelDocumentUnchangedWhenAFactWithAnAlreadyPresentEventIdIsRedelivered` | green; armed in round 1 (probe 3) |
| **R52** | `OutOfOrderFactsTests.R52_AppendsTheTimelineEntryWithoutRegressingTheDocumentStatusOrOverwritingNewerReferences` | green; armed in round 1 (probe 4) |
| **R53** | `PlaceholderDocumentIntegrationTests.R53_…`, `PlaceholderDocumentTests.PR8_…` | green; partial-index half armed in round 1 (probe 10) |
| **R54** (projector half) | `ReadModelSoleWriterTests.R54_…`, `.PR20_…ProvedOnAScratchTree` | green; non-vacuous both ways |
| **R55** (projector half) | `UpdateSignalTests.R55_…`, `.PR17_…`, `.PR42_…` | green; armed in rounds 1-2 (probes 3, P3, P4) |

`PR22`/`PR39`'s L12 half — the row that has now cost three rounds — is, for the first time, **guarded in both creation orders by a test I have personally falsified three ways**. **D5 is not a traceability failure**: every requirement still names a real, non-vacuous test, which is once again exactly why it took a ledger to be visible at all.

## R3.9 What must change before re-review

1. **D5 — correct the two live doc-comments and the one live task box**, and nothing else: `src/Projector/Infrastructure/Persistence/ReadModelIndexes.cs:8-13` (drop the "renders differently … neither service could then start after the other" reason, keep the rule, give the corrected reason the record already states — identical call for cheapness and self-documentation, not because a conflict was observed); `tests/Projector.IntegrationTests/ReadModelIndexesTests.cs:10-16` (same, and stop citing §10.6's "most likely to bite" framing without the outcome §10.6 now records); `specs/projector_read_model/tasks.md:91` task **H1** (rule-3 rewording, as `G4`/`G5`/`I8` already received — both the hazard clause and the "code 85" clause).
2. **A10 — while in `tasks.md`'s neighbourhood**, reconcile `requirements.md:114`'s `PR22` text with the observed error code, or state there that 85 is the requirement's language and 86 is what this engine raises, as `ReadModelIndexes.cs:49-61` already does correctly.
3. **Prove it with the command, not the list.** The fix is complete when `grep -rn "start after the other" --include='*.cs' --include='*.md' . | grep -v '/bin/\|/obj/'` returns only lines that are history (`design.md:683`, this review file, and — if left as advisory **A11** — `progress/spec_projector_read_model.md`), with one classification line per hit. Paste the command and its full output into the record.
4. **Nothing else.** No production behaviour, no test, no assertion. **Do not re-arm the L12 case** — R3.3's three probes stand as this review's evidence that both directions are falsifiable, and re-running them would cost containers to re-prove what is already recorded here.

## R3.10 The effort record, carried forward again (to be appended to `progress/history.md` on approval)

Not appended now — a rejected feature gets no `history.md` entry. When id 24 is approved, its entry must carry all of the following.

**#7's baseline for this feature**, read off `order-to-cash-nestjs/progress/history.md` §`projector_read_model (id 24, phase 12) — 2026-08-24` rather than recalled: **1 spec session + 1 human gate + 1 implementation session + 1 bookkeeping-fix pass + 2 review passes, REJECTED once — on bookkeeping only, the first rejection in that project containing zero code defects — total ≈3 h 41 min** wall-clock from first spec file to final verdict (spec ≈17:16→17:24, implementation ≈17:24→19:49, review 1 ≈23 min, fix ≈25 min, review 2 ≈14 min). **And, separately, an entire second spec pass eight phases later**: amendment **A1**, which replaced the timeline's `occurredAt` ordering with a recorded-causal-edge ordering (`causationId` → `eventId`, bounded `$reduce` depth, a version-stamped migration, and a one-line Billing change making `credit.released.v1` follow `payment.received.v1` instead of being its sibling) — a full re-specification of this feature's central invariant, taken twice and rejected twice before it landed. **#8 folded A1 into its first draft**: `TimelineOrder.cs`'s `__depth`, `timelineOrderVersion`, `TimelineOrderMigrationTests` and `TimelineCausalOrderTests` (`PR31`/`PR32`/`PR35`) are in the delivered feature, because backlog id 57 closed the underlying Billing causal edge the day before. **That dividend is work that did not happen and must be named as such** — it is the clearest instance in this build of #7's experience transferring as *avoided rework* rather than as *faster typing*, and it does not appear anywhere in a session count.

**The confound column, per defect** — would #7's standard have caught it?

| Defect | Answer |
|---|---|
| **D1** (L12 answered backwards) | **No.** #7 has no ported-idiom ledger; the mechanism was adopted at #8's own Phase-8 gate. #7 edited its seed to match its projector and never asked what the server did with the two renderings |
| **D2** (two tests that cannot fail) | **Partly.** #7's review of this feature audited no test for falsifiability; the `Assert.True(true)` shape is the same class as #8's feature-19 finding, which post-dates #7 |
| **D3** (`current.md` false) | **Yes — and this is the sharpest line in the record.** It is the same box #7 was rejected on for this same feature, inherited into this task list as prevention (task `N7`), and it happened anyway. **An inherited prevention task that did not prevent.** State it plainly: it is the most useful datum this feature produced about how much of #7's experience actually transfers, and it is evidence that inheriting a lesson as a *task* is weaker than inheriting it as a *check* |
| **D4** (the corrected mechanism also wrong) | **No.** Same reasoning as D1; additionally it produced a new binding convention in `CLAUDE.md` (an engine claim probed in one direction is a claim about that direction only) |
| **D5** (the claim survives in two more places, one in production source) | **No**, and for a reason worth recording: it is a defect *in a fix*, in a repository whose fix-completeness claims are prose lists. #7 never made the claim, so it never had to unmake it |

**The stale-binary process note, from round 1, and its family.** Round 1's own first `PR45` mutation did not compile, the quiet build output hid it, the test ran against the **previously built binary**, and `ProjectorBootTests` reported a false green — the arming protocol's own hazard biting the reviewer applying the protocol, the second occurrence in this build (feature 7 was the first). All three rounds therefore forced `--no-incremental` on the owning project before every probe and re-ran the projector suites green on the restored tree; round 3 additionally recorded the file's SHA-256 before the first probe and after the last restore and found them equal.

**And the honest line the leader asked for: L12 cost three review rounds on a row whose production code was correct throughout — is that the ledger working expensively, or failing cheaply?**

**Working, expensively — and the expense is not the ledger's, it is the absence of an enumeration step around it.** Each of the three rounds found a claim that was actually false, about an actual engine, in an artefact that would have been inherited: nothing else in this harness saw any of them, because traceability was green all three times (the requirement was met), arming was green all three times (the behaviour was correct on the tested path), and the suite was green all three times (1195, then 1194, then 1194). A mechanism that finds three real defects nothing else can see is not failing. But the *cost profile* is the interesting datum for #9, and it is the inverse of a code defect's: **each round's fix was one or two sentences, and each round's detection was a full review pass.** Cheap to fix, expensive to find — which means the leverage is entirely in detection, and detection failed the same way three times running. Round 1 corrected a claim by reasoning about a probe; round 2 corrected the correction by running the probe the other way; round 3 found the claim still standing in two files because rounds 1 and 2 had each closed with a **prose list of places** rather than a command. The generalisable rule, and the one thing from this feature #9 should take before it writes its first ledger row: **a ledger row's claim is a countable claim, so correcting it is a search and not an edit — grep the OLD wording, classify every hit, and paste the output.** Had that been done at the end of round 1, this feature would have closed in two rounds and probably in one; the row's *content* was never hard, and no round spent its time on analysis.

**The asymmetry that frames the whole entry:** three review rounds, **all three rejecting on records rather than on code**, against a production implementation in which **nineteen independent mutations across the three rounds every one bit** (eleven in round 1, five in round 2, three in round 3, plus four direct server probes and one planted-violation architecture check). The code was right on the first submission and is unchanged except one exception message. For a benchmark whose question is *how much does the stack cost*, this feature's answer is that in #8 the expensive artefact was not the C#.

## R3.11 Phase closure — nothing is due, again

Id 24 is **not** the last feature of phase 12. Read out of `feature_list.json` this round: ids **58** (`notification_envelope_copy_is_unguarded`) and **59** (`date_typed_payload_sites_survive_mutation`) are both filed against phase 12 and both still `pending`. **No phase-12 closing assessment is due at this feature's close, whatever its verdict** — the next session should not go looking for one.

---

**Status set (round 3):** `in_review` → `in_progress`.

# Round 4 — `projector_read_model` (id 24) — **APPROVED**

**Verdict: APPROVED.**

**Round 3's §15 fix is complete, and the leader's §15a is right where §15 is wrong.** I reproduced the enumeration question myself from scratch: this environment's `ugrep 7.8.4` is **not** nondeterministic — both forms are stable across eight consecutive runs — and the two forms disagree because the post-filter `| grep -v '/bin/\|/obj/'` is applied to `grep -rn`'s `path:lineno:content` output and therefore matches **content**. **The fix's completeness claim survives the corrected method**: I ran the path-excluding form for both retired claims, got **21** and **35** hits, and classified **every one**. No hit in `src/` or `tests/` asserts either retired claim; both live sites are clean; `H1` and the two rewritten class summaries state the corrected mechanism in the same terms my own `R3.2` measurements established, with no third variant. One new non-blocking advisory (**A12**), on the *form* of §15's pasted evidence rather than its conclusion.

Rounds 1-3 above are closed and unamended.

## R4.1 What I ran, and what I did not

The claim under test this round is a **documentation-correctness** claim plus a **build** claim (two XML doc comments live in compiled files), so I sized the run to that and say so rather than implying a full re-verification:

- **Ran:** `grep --version` (→ `ugrep 7.8.4 x86_64-pc-linux-gnu`); both enumeration forms × 8 runs × 2 patterns; the set-difference between the two forms; full classification of all 21 + 35 hits; `dotnet build OrderToCash.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**; `dotnet format OrderToCash.sln --verify-no-changes` → exit **0**; `dotnet test tests/Projector.UnitTests --no-build` → **87 passed, 0 failed, 0 skipped**; `dotnet test tests/Projector.IntegrationTests --no-build --filter FullyQualifiedName~ReadModelIndexesTests` → **4 passed, 0 failed, 0 skipped** against the live `otcnet-mongodb` (`mongo:8.3.8`, up 15 h, healthy); `./init.sh` → exit **0**; `git diff feature_list.json`; `git diff --stat`; `git diff -U0` on all four changed spec/progress files; an mtime reconstruction of every non-`bin`/`obj` file touched since 05:00 today.
- **Did not run, deliberately:** the full 1194-test suite, and **any re-arming of the `L12` case**. Per the brief and per `CLAUDE.md`'s own economy rule, `R3.3`'s three probes (`P3a`/`P3b`/`P3c`, run through the real driver against the real server) are this review's standing evidence that both creation orders are falsifiable, and re-running them would spend containers to re-prove a record I wrote myself. The tree changed **only** in two XML doc-comment blocks and four markdown files, `--no-incremental` build is 0/0, the two affected suites are green, and **no test method was added or removed** (`ReadModelIndexesTests` carries 4 `[Fact]`s, as in round 1). That is the scope of the claim and the scope of my run.
- **Did not run:** `./quality.sh` end to end. Its four steps are format, build, test and coverage; I ran format and build solution-wide, tests on the two projects the change can reach, and coverage remains **A7** — feature 34's, unchanged, still inert by disclosure.

## R4.2 The adjudication the brief asked for: §15 vs §15a

**§15a's account is right. §15's is wrong. I have this from my own runs, not from reading either record.**

```
$ grep --version | head -1
ugrep 7.8.4 x86_64-pc-linux-gnu +sse2; -P:pcre2jit; -z:zlib,bzip2,zstd,brotli,7z,tar/pax/cpio/zip

$ for i in $(seq 8); do grep -rn "start after the other" --include='*.cs' --include='*.md' . 2>/dev/null | grep -v '/bin/\|/obj/' | wc -l; done
16 16 16 16 16 16 16 16

$ for i in $(seq 8); do find . \( -name '*.cs' -o -name '*.md' \) -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/node_modules/*' -print0 | xargs -0 grep -n "start after the other" 2>/dev/null | wc -l; done
21 21 21 21 21 21 21 21

$ for i in $(seq 8); do grep -rn "start after the other" --include='*.cs' --include='*.md' . 2>/dev/null | wc -l; done     # same recursive grep, NO post-filter
21 21 21 21 21 21 21 21
```

**Three findings, in order of what they settle.**

1. **No nondeterminism exists.** Every form is stable at its own value across eight runs. §15's claim that `grep -rn --include` "returned different counts on successive identical invocations (14, then 23, then 33)" is not reproducible here on either pattern; pattern 2 returns **35** under both forms, eight runs each. §15's switch to `find | xargs` was nonetheless the right move — it just fixed the problem for a reason its author had not identified.
2. **The third run above is the decisive control, and neither §15 nor §15a ran it.** The *same* `grep -rn --include` invocation, with the post-filter removed, returns **21** — identical to the `find | xargs` form. So the discrepancy is **entirely** the post-filter's; the recursive descent and the `--include` handling are exonerated, not merely suspected. That is a stronger disproof of §15's diagnosis than §15a's own two-form comparison, which could still have been read as two tools disagreeing.
3. **The suppressed lines are exactly as §15a describes, and there are now five.** §15a found three; the tree has since grown by §15a itself, which quotes the command twice more:

```
$ comm -23 <path-excluding, sorted> <post-filtered, sorted>
progress/impl_projector_read_model.md:339   → §15's own enumeration command
progress/impl_projector_read_model.md:405   → §15a's first probe command
progress/impl_projector_read_model.md:407   → §15a's second probe command
progress/review_projector_read_model.md:544 → round 3's pasted enumeration command (R3.4)
progress/review_projector_read_model.md:655 → R3.9's instruction to run that command
```

**All five contain the literal text `/bin/` or `/obj/` because all five quote a command containing `grep -v '/bin/\|/obj/'`.** The mechanism §15a names is confirmed exactly: the filter is written as a path exclusion, is applied to `path:lineno:content`, and therefore deletes hits by **content**. And §15a's sharpest point survives its own extension — the population it preferentially deletes is *the records of the sweep itself*, so the filter grows more blind the more carefully the sweep is documented. Five suppressed lines, five command quotations, zero coincidences.

**I checked the convention landed and is stated correctly**: `CLAUDE.md:201` on disk carries it, with the `find … -not -path` form and the anchored-`init.sh:191` alternative. Its cited figures (16 vs 19) were true when written; they are 16 vs 21 today because §15a added two more self-referential lines. That is the rule demonstrating itself, not an error in it.

## R4.3 Does the completeness claim survive the corrected method? — pattern 1, all 21 hits classified

`find . \( -name '*.cs' -o -name '*.md' \) -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/node_modules/*' -print0 | xargs -0 grep -n "start after the other"` → **21 hits, stable over 8 runs.** (§15's own command, without the `node_modules` clause, returns the same 21 — there is no `node_modules` in this tree.)

| # | Hit | Classification |
|---|---|---|
| 1 | `progress/impl_projector_read_model.md:323` | §15's opening narration, quoting the clause it removed — record of the fix |
| 2 | `progress/impl_projector_read_model.md:327` | §15 edit-1's description, quoting the dropped text — record of the fix |
| 3 | `progress/impl_projector_read_model.md:337` | §15's self-referentiality caveat, quoting the search phrase — record of the fix |
| 4 | `progress/impl_projector_read_model.md:339` | §15's pattern-1 command line — record of the fix (**suppressed by the old form**) |
| 5 | `progress/impl_projector_read_model.md:405` | §15a probe command — leader's addendum (**suppressed by the old form**) |
| 6 | `progress/impl_projector_read_model.md:407` | §15a probe command — leader's addendum (**suppressed by the old form**) |
| 7 | `progress/review_projector_read_model.md:112` | round 1, quoting the claim it disproved — history |
| 8 | `progress/review_projector_read_model.md:473` | round 3 verdict paragraph — history |
| 9 | `progress/review_projector_read_model.md:475` | round 3 root-cause paragraph — history |
| 10 | `progress/review_projector_read_model.md:535` | round 3's quotation of the production-source defect — history |
| 11 | `progress/review_projector_read_model.md:538` | round 3's quotation of the test-file defect — history |
| 12 | `progress/review_projector_read_model.md:539` | round 3's quotation of the `tasks.md` defect — history |
| 13 | `progress/review_projector_read_model.md:544` | round 3's pasted command (R3.4) — history (**suppressed by the old form**) |
| 14 | `progress/review_projector_read_model.md:562` | round 3's self-contradiction argument — history |
| 15 | `progress/review_projector_read_model.md:653` | R3.9 required-fix item 1 — history |
| 16 | `progress/review_projector_read_model.md:655` | R3.9 required-fix item 3 — history (**suppressed by the old form**) |
| 17 | `progress/spec_projector_read_model.md:43` | spec-phase row 23, left as written — **advisory A11, correctly handled**: superseded by the appended note at :94-100, not retconned |
| 18 | `progress/spec_projector_read_model.md:70` | spec-phase gate note, same status — **A11** |
| 19 | `progress/spec_projector_read_model.md:96` | the appended superseded note, quoting the hazard **to say it does not hold** — corrected framing |
| 20 | `specs/projector_read_model/design.md:683` | §10.6, narrating both wrong versions as superseded — corrected framing, untouched this round |
| 21 | `specs/projector_read_model/tasks.md:91` | `H1`, stating the hazard as L12's *original* claim that "does **not exist**" — corrected framing |

**Zero unclassified lines. Zero hits under `src/` or `tests/`.** The two live sites round 3 rejected on — `src/Projector/Infrastructure/Persistence/ReadModelIndexes.cs` and `tests/Projector.IntegrationTests/ReadModelIndexesTests.cs` — are absent from the enumeration, which is the form of proof R3.9 item 3 asked for and is stronger than re-reading the two files. **The leader's reading is confirmed: all remaining hits are history, narration, or corrected framing.**

## R4.4 Pattern 2 — `code 85` / `IndexOptionsConflict`, all 35 hits classified

`find … -print0 | xargs -0 grep -n "code 85\|IndexOptionsConflict"` → **35 hits, stable over 8 runs; the post-filtered form also returns 35**, because no matched line here quotes a build directory. §15's figure of 35 is correct.

| # | Hit | Classification |
|---|---|---|
| 1-11 | `progress/impl_projector_read_model.md:26, 123, 181, 204, 323, 329, 330, 337, 365, 371, 378` | this record's own narration across §2/§9/§13/§15 — every one either discusses the 85→86 correction or quotes the search pattern. Non-blocking line-number note in **A12** below |
| 12 | `progress/review_projector_read_model.md:100` | round 1's own test-run output — history |
| 13 | `progress/review_projector_read_model.md:125` | round 1 quoting the `Assert.True(true, "…IndexOptionsConflict…")` defect D2 — history |
| 14 | `progress/review_projector_read_model.md:130` | round 1, same defect's second case — history |
| 15 | `progress/review_projector_read_model.md:161` | round 1 advisory A2 (wrong code in the thrown message) — history, and the fix it asked for is in place |
| 16 | `progress/review_projector_read_model.md:380` | round 2 advisory A8 — history, still open as an advisory |
| 17 | `progress/review_projector_read_model.md:539` | round 3's quotation of `H1`'s old text — history |
| 18 | `progress/review_projector_read_model.md:578` | round 3 restating A8 — history |
| 19 | `progress/review_projector_read_model.md:580` | round 3 raising A10 — history; A10 is now closed (see #27) |
| 20 | `progress/review_projector_read_model.md:653` | R3.9 item 1 — history |
| 21 | `progress/spec_projector_read_model.md:43` | spec-phase row 23's original text — **A11**, superseded in place by the appended note |
| 22 | `progress/spec_projector_read_model.md:96` | appended superseded note, quoting 85 to say it does not apply — corrected framing |
| 23 | `progress/spec_projector_read_model.md:98` | appended note's explicit 85→86 correction — corrected framing |
| 24 | `specs/projector_read_model/design.md:616` | ledger **L11**, correctly stating 86 is what a real server raises for `PR22`'s scenario and that the catch covers both — correct |
| 25 | `specs/projector_read_model/design.md:617` | ledger **L12**, correctly stating comparison-time equivalence and creation-order storage — correct |
| 26 | `specs/projector_read_model/requirements.md:114` | `PR22`'s EARS obligation retains `IndexOptionsConflict`, code 85 (the requirement's own reused #7 language) **plus** the appended observed-reality sentence naming 86 and the both-codes catch — **advisory A10 closed** |
| 27 | `specs/projector_read_model/requirements.md:150` | `PR39`'s absence claim (*"raises no `IndexOptionsConflict`"*) — true regardless of which code the server would use, since neither order raises anything |
| 28 | `specs/projector_read_model/requirements.md:198` | traceability row, narrating the L11 and both L12 corrections and citing the renamed case — correct |
| 29 | `specs/projector_read_model/tasks.md:91` | `H1`, rule-3 reworded this round — corrected framing (checked in R4.5) |
| 30 | `specs/projector_read_model/tasks.md:92` | `H2`'s untouched arming instruction, naming the error class it told the implementer to probe for — correct as an instruction |
| 31 | `src/Projector/Infrastructure/Persistence/ReadModelIndexes.cs:54` | the `CreateIndexAsync` remarks block, naming **85 for options differences and 86 for spec differences** and which one this scenario raises — correct, untouched, and D5 never named it |
| 32 | `src/Projector/Infrastructure/Persistence/ReadModelIndexes.cs:73` | the live `catch` clause: `ex.CodeName is "IndexOptionsConflict" or "IndexKeySpecsConflict" \|\| ex.Code is 85 or 86` — the code itself, correct |
| 33 | `tests/Projector.IntegrationTests/ReadModelIndexesTests.cs:66` | `PR39`'s doc comment — an absence claim |
| 34 | `tests/Projector.IntegrationTests/ReadModelIndexesTests.cs:68` | `PR39`'s method name — an absence claim |
| 35 | `tests/Projector.IntegrationTests/ReadModelIndexesTests.cs:92` | the L12 case's doc comment, *"never raise `IndexOptionsConflict`, in EITHER creation order"* — an absence claim, and true on my own R3.2 measurements |

**Zero unclassified lines.** Every occurrence is either the *name of a real MongoDB error code* used in a context that also names the code the server actually raises, an *absence* claim (true under either code), the requirement's own reused language now annotated with observed reality, or history. **No artefact anywhere asserts unqualified that a real server raises 85 for `PR22`'s scenario.** The leader's reading holds on this pattern too.

## R4.5 New prose is a new claim — `H1` and the two class summaries, checked against my own R3.2 measurements

My `R3.2` measured, directly against `otcnet-mongodb` (`mongo:8.3.8`) on four fresh databases: **(i)** neither creation order conflicts; **(ii)** `getIndexes()` returns whichever rendering created the index (numeric-first stores `2`, alias-first stores `"string"`); **(iii)** control C — a structurally different filter conflicts (86); **(iv)** control D — two *aliases naming different types* conflict (86), which is what makes it genuine semantic comparison rather than name-matching. The question is whether the three rewritten passages say **that**, and not a third thing.

| Artefact | What it now says | Verdict |
|---|---|---|
| `specs/projector_read_model/tasks.md:91` (**`H1`**) | L12's original hazard *"does **not exist** on a real `mongo:8.3.8`, in either creation order"*; the identical-call rule is kept *"for cheapness and self-documentation, not because a conflict was observed"*; and *"the code a real server raises for this box's own `PR22` scenario is **86** (`IndexKeySpecsConflict`), not 'code 85'"*, citing L11 and `ReadModelIndexes.cs:44-62`. Box stays ticked; work was done | **Correct.** It asserts the **outcome** (no conflict, either order) and defers the mechanism to `design.md` §10 row L12/§10.6 rather than restating it — so it cannot introduce a variant. Both halves R3.4 called false are now stated as false. Matches (i) exactly, and matches L11 on the code |
| `src/Projector/Infrastructure/Persistence/ReadModelIndexes.cs:6-18` (class summary) | *"not because a hand-written `{ $type: "string" }` `BsonDocument` has been observed to conflict with it (it does not: a real `mongo:8.3.8` **compares** the string alias and the numeric BSON type code as equivalent, in either creation order), but because the identical call is the cheaper, self-documenting form, and because depending on comparison-time equivalence beyond what is tested here is exactly the kind of engine behaviour a future MongoDB version could change"* | **Correct, and precise.** It uses **compares**, scopes the equivalence to comparison, states both creation orders, and gives the honest reason for the rule. It makes **no** storage claim at all — which is the right restraint for a production comment. Matches (i); consistent with (ii) by omission rather than contradiction |
| `tests/Projector.IntegrationTests/ReadModelIndexesTests.cs:10-22` (class summary) | *"design.md §10.6 flagged … as the row 'most likely to bite', and it **did not**"*; the server *"compares … as the SAME partial filter, in either creation order, so neither raises a conflict against the other"*; points at the L12 case **by name**; and repeats the honest reason for the rule | **Correct.** It fixes exactly what R3.9 asked: it no longer cites §10.6's "most likely to bite" framing without §10.6's own recorded outcome. Matches (i) |
| `tests/Projector.IntegrationTests/ReadModelIndexesTests.cs:88-114` (the L12 case's own doc comment, **untouched this round**) | equivalence *"when the server COMPARES an existing index's specification against a requested one"*; *"The server does NOT normalise the alias when STORING the index: `getIndexes()` returns whichever rendering actually created it"*; directions A and B spelled out with their stored renderings | **Correct** — and it is the one place that states **both** halves, (i) and (ii), which is where a reader should find them |

**No third variant.** Read together the four passages say one thing: *comparison-time equivalence, in both orders; stored rendering follows creation order*. That is what I measured. The distribution is also sensible — the production comment makes the weakest claim it needs, the test doc-comment makes the full claim next to the assertions that prove it.

**And the test logic is untouched.** I read the whole `L12_TheServerComparesTheTypeAliasesAsEquivalentButStoresWhicheverRenderingCreatedTheIndex_InBothCreationOrders` body: both directions still build the hand-built `BsonDocument` filter, still call `MongoSeedWriter.EnsureIndexesAsync` for the seed half (not a re-typed equivalent), still `Assert.Null` each exception and still assert the stored value — `Assert.Equal(2, …AsInt32)` for direction A and `Assert.Equal("string", …AsString)` for direction B. These are the exact lines `R3.3`'s `P3a`, `P3b` and `P3c` falsified; the only change is that their line numbers moved down with the class summary above them. **`R3.3` therefore still applies to the code on disk, which is why re-arming was neither required nor performed.**

## R4.6 What four passes over the same files disturbed — checked, and nothing did

- **`feature_list.json`** — `git diff` is **exactly one line**, `spec_ready` → `in_review` on id 24. My own round-3 `in_progress` write and the implementer's `in_review` write are both post-HEAD, so the net diff against HEAD is the single line; **no write was lost**, confirmed against the file rather than assumed.
- **`specs/projector_read_model/design.md`** — `git diff --stat` **6 changed lines (3+/3-)** in **2 hunks**, at `616`, `617` and `683`. Identical to `R3.5`. Not touched this round.
- **`specs/projector_read_model/tasks.md`** — **99 boxes, 99 ticked, 0 unticked**. `git diff -U0` changes **99 lines, all of them checkbox lines**: `git diff -U0 … | grep '^+' | grep -v '^+- \['` prints only the `+++ b/…` header, i.e. **zero non-checkbox additions**. No task text was relocated, dropped or added outside a box across four passes.
- **`specs/projector_read_model/requirements.md`** — three hunks: `114` (PR22, this round's **A10** fold), `162` (+4, round 1-2), `178-215` (the traceability table). No fourth hunk appeared.
- **`progress/spec_projector_read_model.md`** — a **single appended hunk** at `@@ -72,0 +73,28 @@`. Rows 43 and 70 are byte-unchanged; the superseded note is **appended, not rewritten**, which is precisely the shape `R3.6`'s **A11** prescribed. **A11 correctly discharged.**
- **`specs/shared/test-matrix.md`** — untouched this round; re-enumerated mechanically anyway: **49 `DONE`, 10 `TODO`, 4 partial/scoped = 63**, matching the `| **Total** | **R1 – R63** | **63** | **49** | **4** | **10** |` row at line 81. The only hunks against HEAD remain lines 78, 81 and §7's rows 174-179.
- **`src/` and `tests/`** — untracked, so I verified by mtime instead of diff. Of the **86** projector source and test files, the two most recently modified are `ReadModelIndexes.cs` (**10:01:35**) and `ReadModelIndexesTests.cs` (**10:01:49**); the next most recent is 50 minutes earlier (09:11:13). **Exactly the two files §15 names, and nothing else in `src/Projector`, `tests/Projector.UnitTests` or `tests/Projector.IntegrationTests`, was touched in the third fix round.**
- **Build and shape** — `dotnet build --no-incremental` **0 warnings / 0 errors** (the rewritten XML doc comments, including the `<see cref="MongoSeedWriter.EnsureIndexesAsync"/>`, resolve cleanly under `TreatWarningsAsErrors`); `dotnet format --verify-no-changes` exit **0**; `ReadModelIndexesTests` still carries **4** `[Fact]`s and runs **4/4** green against the real container; `Projector.UnitTests` **87/87**.
- **Working tree** — `git status --porcelain` **37 lines**, unchanged from round 3. `./init.sh` exit **0**: 58 features, backlog tripwire clean, commit-msg hook installed and matching, 37 uncommitted changes warned as expected.
- **`CLAUDE.md`** — `git diff` shows **12 added lines** in two blocks: the two ledger clauses (both-directions probing) recorded at round 3, and the three enumeration clauses added after round 3 (retire-the-old-wording, exclude-by-path, and the suppressed-evidence argument). All leader-owned files, all outside `src/`/`tests/`/`apps/web/`, all accurate as read on disk.

## R4.7 Defects and advisories

**Blocking defects this round: none.**

**A12 — new, non-blocking, and worth recording because #9 inherits this file.** §15's pasted enumeration output is **not** the command's output: it has been transcribed, and its line numbers for its own file have drifted. Pattern 1's block lists `progress/impl_projector_read_model.md:335` and `:340`; the actual hits in that file are at `:337` and `:339`, and neither 335 nor 340 matches. Pattern 2's block lists `impl:…360, 363, 373, 375`; the actual hits are at `…337, 365, 371, 378` — and the deltas are non-monotonic (+2, −2, +3), so this is not a uniform shift from a later insertion but partly hand-written figures. §15 explicitly asserts *"the figures below were captured **after** finishing all prose in this section … and are not re-chased after this point"*; that assertion does not hold. **Why it is only an advisory:** the *file set*, the *counts* (19 then, 21 now for the same command; 35 and 35) and every *classification* are correct, and I have independently reproduced the whole enumeration and found nothing missed. **Why it is worth writing down:** pattern 2's block is grouped as one sentence per file rather than one line per hit, which is exactly the form `CLAUDE.md` forbids — *"a missed hit must be visible as an unclassified line, not invisible as a sentence"*. A retyped output is a reading wearing the clothes of a search result. The one-line rule for #9: **paste the output, do not retype it, and if the file is self-referential, re-run and re-paste after the last prose edit rather than asserting that you did.**

**Carried forward, unchanged, none blocking:**

- **A7** — the coverage gate is inert (per-report `line-rate`, no aggregate threshold enforced). Feature 34's, correctly disclosed, still the reason `CHECKPOINTS.md` C4's coverage box cannot be ticked.
- **A8** — the A2 message fix has no guard: `PR22_RefusesToStartAgainstANonPartialIndexOfTheSameName_NamingTheIndex` asserts the index name and `dropIndex` only, so a regression to a hard-coded *"code 85"* in the thrown message stays green. Cheap to close whenever `ReadModelIndexes.cs` is next opened.
- **A9** — `progress/current.md:4` still reads `**Status:** in_progress — rejected on three defects … fix round in flight`, while the backlog says `in_review` and the fix round is finished; the file also still carries **two** `## Notes` headings. Leader-owned, outside implementer scope, and the reason `CHECKPOINTS.md` C5's session-close step is called out below. **This must be corrected to reflect `done` at session close** — it is bookkeeping, not a defect in the feature.
- **A10 — closed.** `requirements.md:114` now carries the observed-reality sentence naming code 86 and the both-codes catch, without rewriting `PR22`'s reused #7 obligation. Exactly the shape R3.9 item 2 offered as an alternative.
- **A11 — closed.** `progress/spec_projector_read_model.md`'s rows 43 and 70 are byte-unchanged and a dated superseded note is **appended** at lines 94-100. The prescribed shape was followed.
- **Cosmetic, sub-advisory:** `progress/spec_projector_read_model.md:96` says the hazard sentence *"survived unedited in two more places"* and then lists **three** (that file, `ReadModelIndexes.cs`, `ReadModelIndexesTests.cs`). The third is that file itself, which was A11 rather than D5. Prose slip in a superseded note; not worth an advisory number.

## R4.8 `CHECKPOINTS.md` walk — round 4

Boxes re-argued only where evidence changed this round; the rest stand on rounds 1-3's armed evidence, which this round did not disturb.

### C1 — the harness is complete

- [x] All harness files present; `./init.sh` exit **0**, run by me this round; commit-msg hook installed and matching the tracked copy.

### C2 — state is coherent

- [x] At most one feature `in_progress` — **zero** `in_progress`, one `in_review` (id 24) on entry, read out of `feature_list.json` this round; set `done` by this verdict.
- [x] Every status is in `rules.valid_status` — `init.sh` validator, exit 0.
- [x] Every `done` feature has passing tests — solution builds 0/0 `--no-incremental`; the two suites this change can reach are **87/87** and **4/4**; round 3's own full run of **1194/1194, 0 skipped** stands unchallenged, since no test logic changed since.
- [x] `progress/current.md` describes the active session — body correct; **advisory A9** on the stale status line, to be corrected at session close.
- [x] Every `blocked` feature records why — none are blocked.

### C3 — architecture is respected

- [x] Clean-Architecture layering, domain purity, no cross-service DB access, shared runtime code confined to `SharedKernel`/`Contracts`/`Cqrs`, Kafka-facts vs NATS-RPC classification — all as rounds 1-3, on their armed evidence and the planted-violation `Architecture.Tests` check. **No production behaviour changed in this round**: the only `src/` edit in the whole fix round is one XML doc-comment block, and `ReadModelIndexes.EnsureAsync`/`CreateIndexAsync` are byte-identical to round 2's delivery.

### C4 — verification is real

- [x] Format check + build + test pass — `dotnet format --verify-no-changes` exit **0**, `--no-incremental` build **0 warnings / 0 errors**, `Projector.UnitTests` 87/87, `ReadModelIndexesTests` 4/4, all run by me this round. `./quality.sh` not re-run end to end (steps run individually, coverage read).
- [x] Domain tests are pure; integration tests hit real containers — `ReadModelIndexesTests` ran against the live `otcnet-mongodb` (`mongo:8.3.8`), not a fake; the container list is `mongo:8.3.8`, `apache/kafka:4.3.1`, `nats:2.14.5-alpine`, `mcr.microsoft.com/mssql/server:2022-CU26`, `axllent/mailpit:v1.27.5`.
- [x] Guards have been seen to fail — **nineteen independent mutations across rounds 1-3, every one of which bit**, including `R3.3`'s `P3a`/`P3b`/`P3c` on the `L12` case in both creation orders and both mutation families. Deliberately not repeated this round; the code they attacked is unchanged, verified by mtime and by reading the case body.
- [ ] **Coverage thresholds met (≥80% domain, ≥60% overall)** — **cannot be ticked (A7)**, owned by feature 34, disclosed not hidden.
- [x] No Jest anywhere.

### C5 — the session closed cleanly

- [x] No suspicious untracked files — **37** `git status --porcelain` lines, all inside the allow-list.
- [x] **`progress/history.md` has an entry for the feature, with its effort record** — appended by this verdict, text in **R4.9**.
- [x] `feature_list.json` reflects the true state — id 24 set `spec_ready`→`done` by a **single-line edit**, `git diff` read afterwards to confirm exactly one changed line. **No `git checkout` was run on this file at any point in any round.**
- [x] The human has been told what was done and how to test it — `impl` §12, §13, §14, §15, plus the leader's §15a.
- [x] Claude did not commit — no new commits; working tree dirty and left for the human.
- [ ] **`progress/current.md` reset for the next session** — **A9**; leader-owned, one line, to be done at close.

### C6 — Spec-Driven Development

- [x] `specs/projector_read_model/` has all three documents.
- [x] EARS `PR<n>` ids throughout.
- [x] **All 99 tasks ticked over text that is true** — 99/99 ticked; **`H1` is now reworded under rule 3** and states both retired claims as retired, in terms my own measurements confirm (R4.5). **D5's third site closed.**
- [x] Every `R<n>` covered by a named test recorded in `specs/shared/test-matrix.md` — 49 DONE / 4 partial / 10 TODO of 63, re-enumerated mechanically this round.
- [x] **`design.md` and the code it describes are true where most load-bearing** — the ledger rows L11 and L12, §10.6, `PR22`, `PR39`, the traceability row, `H1`, both class summaries and the L12 case's doc comment now all say the same, correct thing. **D5's other two sites closed**, proved by their absence from a path-excluding enumeration rather than by re-reading.
- [x] The spec commit precedes the implementation commit — spec committed `766f21f`, 2026-09-07 11:28:42 +0200; no implementation commit yet exists (the human commits).

### C7 — spec-reuse fidelity and benchmark honesty

- [x] `specs/shared/` byte-identical to #7's except `test-matrix.md` — round 1's `diff -rq`; nothing under `specs/shared/` changed this round.
- [x] Every deviation is a recorded amendment — none made.
- [x] The `R<n>` ids are #7's.
- [ ] n8n workflows / black-box API script — **not applicable at this feature**; the Gateway is feature 25's.
- [x] `progress/history.md` effort records complete — this feature's is appended by this verdict.
- [x] README benchmark section — untouched, out of scope for this feature.

## R4.9 `R<n>` → test mapping — verified once more, against files on disk

Every name below was confirmed to exist by `grep -rl` this round, and the two suites they live in were run green by me (87/87 and, for the index file, 4/4):

| Requirement | Test | Status |
|---|---|---|
| **R50** | `TimelineProjectionTests.R50_AppendsAnEntryCarryingEventIdEventTypeOccurredAtAndASummary_AndPresentsTheTimelineOrderedByOccurredAtRatherThanByArrival` | present, green; ordering half armed in round 1 (probes 2, 4, 5) |
| **R51** | `TimelineProjectionTests.R51_LeavesTheReadModelDocumentUnchangedWhenAFactWithAnAlreadyPresentEventIdIsRedelivered` | present, green; armed in round 1 (probe 3) |
| **R52** | `OutOfOrderFactsTests.R52_AppendsTheTimelineEntryWithoutRegressingTheDocumentStatusOrOverwritingNewerReferences` | present, green; armed in round 1 (probe 4) |
| **R53** | `PlaceholderDocumentIntegrationTests.R53_…` + `PlaceholderDocumentTests.PR8_…` | both present, green; partial-index half armed in round 1 (probe 10) |
| **R54** (projector half) | `ReadModelSoleWriterTests.R54_…`, `.PR20_…ProvedOnAScratchTree` | present, green; non-vacuous in both directions |
| **R55** (projector half) | `UpdateSignalTests.R55_…`, `.PR17_…`, `.PR42_…` | present, green; armed in rounds 1-2 (probes 3, P3, P4) |
| **PR22 / PR39** (the ledger row that cost four rounds) | `ReadModelIndexesTests.PR22_Creates…IdempotentlyOnASecondRun`, `.PR22_RefusesToStartAgainstANonPartialIndexOfTheSameName_NamingTheIndex`, `.PR39_TheSeedsIndexThenTheProjectors_AndTheProjectorsThenTheSeeds_RaiseNoIndexOptionsConflict`, `.L12_TheServerComparesTheTypeAliasesAsEquivalentButStoresWhicheverRenderingCreatedTheIndex_InBothCreationOrders` | all four present, **4/4 green against a real `mongo:8.3.8`**; the L12 case falsified three ways by me in round 3, in both creation orders |

**Traceability is complete and was never the thing at issue** — which is, for the fourth round running, exactly the point the ledger convention exists to make.

## R4.10 Phase closure — nothing is due, stated explicitly

**No phase-12 closing assessment is due at this feature's close.** Read out of `feature_list.json` this round: ids **58** (`notification_envelope_copy_is_unguarded`) and **59** (`date_typed_payload_sites_survive_mutation`) are both filed against **phase 12** and both still **`pending`**. Id 24 is therefore not the last feature of its phase. The next session should not go looking for a closing assessment, and should not write one.

## R4.11 The effort record and its closing judgement — carried forward from R3.10, with round 4 added

Appended to `progress/history.md` by this verdict. The wall-clock is reconstructed from artefact mtimes (`find . -newermt '2026-09-08 05:00' -not -path './.git/*' -not -path '*/bin/*' -not -path '*/obj/*' -printf '%T+ %p\n' | sort`), read off my own run this round, and is labelled as a proxy rather than a stopwatch.

**The judgement R3.10 reached — "the ledger working, expensively, with the cost sitting entirely in detection, which failed the same way three times because each round closed with a list instead of a command" — needs one correction and one addition, and they point in opposite directions.**

**The correction: it is not the same failure a fourth time.** Round 3 *did* close with a command, and R3.9 item 3 required the fix round to do the same. The fix round obeyed: it ran an enumeration, pasted output, and classified every hit — and its conclusion was **correct**, which four independent re-runs by me confirm. So the discipline R3.10 prescribed was adopted and it worked. What round 4 found is a **different and more interesting failure**: the command itself had a hole. `grep -rn … | grep -v '/bin/\|/obj/'` looks like a path exclusion, is not one, and drops hits whose *content* mentions a build directory — a population that consists almost entirely of **quotations of the enumeration command**, i.e. the records of the sweep. **The guard-that-does-not-guard has now appeared inside the enumeration rule that exists to defeat it**, which is its fifth disguise in this repository and the first one that hides specifically from the person checking.

**The addition, and the honest cost line: round 4 was cheap, and it was cheap for a reason worth naming.** Rounds 1-3 each cost a full review pass — arming, container probes, suite runs. Round 4 cost none of that, because round 3 closed with two things instead of one: a required fix **and** a standing evidentiary record (`R3.3`'s three probes) that explicitly discharged the need to re-arm. A brief that says *"do not re-run this, my own record already establishes it"* is what turned a fourth round from ≈40 minutes into ≈10. **That is the transferable process win from this feature, and it belongs next to the ledger lesson rather than under it.**

**So the revised closing judgement, for #9:** the ledger found four real, inherited-if-shipped falsehoods that nothing else in this harness could see — traceability was green all four times, arming was green all four times, the suite was green all four times. It is working. Its cost profile is inverted relative to a code defect (each fix was one or two sentences; each detection was a review pass), so **all the leverage is in detection** — and detection failed three times on prose and once on a *malformed* command. The two rules that come out of it are now both in `CLAUDE.md`, and #9 should have them before it writes its first ledger row: **enumerate on the wording of the claim being retired, not the claim being written**; and **exclude by path at the source, never by post-filtering `grep -rn`'s output**. Had both been in force at the end of round 1, this feature closes in two rounds.

**And the asymmetry that frames the whole entry is unchanged and now stronger:** four review rounds, **all four rejecting or querying records rather than code**, against a production implementation in which **nineteen independent mutations across three rounds every one bit**, and whose executable code has changed exactly once in four rounds — one exception message, round 1's A2. For a benchmark asking *how much does the stack cost*, this feature's answer is that in #8 the expensive artefact was not the C#.

---

**Verdict: APPROVED.** Id 24 set `done`; effort record appended to `progress/history.md`. Ids 58 and 59 remain `pending` under phase 12, so **no phase-12 closing assessment is due**.

## R4.12 Closing note — one harness failure the approval creates, and it is the leader's to clear

**The verdict above stands.** This note records the state of the tree *after* the transition, measured rather than assumed.

Setting id 24 to `done` turns advisory **A9** from cosmetic into a hard harness failure, because the two are the same fact seen from either side:

```
$ ./init.sh
[OK]    no feature in_progress
[OK]    SDD coherence: 7 sdd feature(s) past pending have their triple-doc
[FAIL]  progress/current.md claims a feature while none is active: "**Feature:** `projector_read_model` (id 24, phase 12)"
[OK]    backlog tripwire: no feature lost, no done reverted
══ init.sh: FAILURES above — do not advance the session ══
exit 1
```

`progress/current.md:3-4` still reads `**Feature:** projector_read_model (id 24, phase 12)` / `**Status:** in_progress — rejected on three defects … fix round in flight`. That was accurate when written at **08:16:09**, three rounds ago; it is now wrong in three ways at once (the feature is `done`, no fix round is in flight, and the file's own template says to reset on close). The file also still carries **two** `## Notes` headings.

**Consequences for my own boxes, stated plainly rather than left to be discovered:**

- **C1** — the `./init.sh` exit-0 box was ticked in R4.8 on a run made **before** the transition, and it was true then. **Post-transition it is `[ ]`** until `progress/current.md` is reset. I am not restating the walk; I am naming the one box the approval moves.
- **C5** — the *"`progress/current.md` reset for the next session"* box was already `[ ]` in R4.8 for exactly this reason. It is now the **only** thing standing between this tree and a clean `init.sh`.

**Why this does not change the verdict.** `init.sh`'s check is about *session bookkeeping*, not about the feature: it fires because the leader's session file has not been closed, and it would fire identically after any correct approval made while that file is stale. Every artefact this feature owns — its three spec documents, its 99 ticked tasks, its tests, its ledger rows, its impl report, its history entry — is complete and verified above. **Rejecting a feature because the coordinator has not yet written its own closing line would be a guard firing on the wrong artefact**, which is a failure mode this repository has already paid for twice.

**Required at session close, by the leader, before anything is committed** (one file, outside `src/`/`tests/`/`apps/web/`, squarely in the leader's own remit):

1. Reset `progress/current.md` from its own template, or set `**Status:**` to reflect that id 24 is `done` and phase 12 continues with ids 58 and 59.
2. Remove the duplicate `## Notes` heading.
3. Re-run `./init.sh` and confirm exit **0**.

`git status --porcelain` is **38** lines at the time of this note — 37 as measured in R4.6, plus `progress/history.md`, now modified by this verdict's effort record. `feature_list.json`'s diff against HEAD is the **single** line `"status": "spec_ready"` → `"status": "done"` on id 24, read back after the edit; 58 features, 37 `done`, 21 `pending`, JSON re-parsed to confirm it is still valid. **No `git checkout` was run on that file, in this round or any other.**
