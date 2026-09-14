# Phase 15, batch 2 — id 93 payload unification, Projector's PR38 offset-guard sibling, and Orders' config unit guard

Three pieces, one dispatch, per the leader's brief. All three PASS.

## Piece 1 — Backlog id 93: unify orders.\*/catalog.\* payloads into `src/Contracts/Rpc`

### Design decision

Per the brief and id 93's own notes ("DESIGN DECISION MADE 2026-09-15 by
leader: move to src/Contracts/Rpc, following id 84's exact precedent"),
this was not re-litigated. The ten Gateway-only records id 84's review
counted, plus their Orders-side counterparts, moved to two new files:
`src/Contracts/Rpc/OrdersRpcPayloads.cs` (orders.create/orders.cancel) and
`src/Contracts/Rpc/CatalogRpcPayloads.cs` (catalog.reference.list).

### Enumeration (acceptance bullet 1) — search result

Command run: `grep -rln --include="*.cs" -w "<TypeName>" src/ tests/` for
every `orders.*`/`catalog.*` record declared in
`src/Orders/Presentation/Rpc/*.cs` (`CatalogReferenceListPayloads.cs`,
`OrdersCancelPayloads.cs`, `OrdersCreatePayloads.cs` — confirmed as the
full set by `ls src/Orders/Presentation/Rpc/`) and cross-checked against
`src/Gateway/Application/Rpc/GatewayRpcPayloads.cs`.

**Population confirmed: 10 Gateway-only `sealed record` declarations**
(matching id 84 review's own count), plus one non-record companion type
(`CatalogReferenceKinds`, a `static class` of consts — not itself a
`record`, so not part of the "10", but merged as an 11th type because both
sides declared it and it is inseparable from the closed enum the records
reference).

| # | Record | Orders declaration | Gateway declaration | Classification |
|---|---|---|---|---|
| 1 | `OrdersCreateRequestLine` | `OrdersCreatePayloads.cs` | `GatewayRpcPayloads.cs` | **Identical** — `(string ProductCode, int Quantity, long? UnitPrice, long? LineDiscount)` |
| 2 | `OrdersCreateRequestPayload` | `OrdersCreatePayloads.cs` | `GatewayRpcPayloads.cs` | **Identical** — same 7 properties, same types |
| 3 | `OrdersCreateReplyPayload` | `OrdersCreatePayloads.cs` | `GatewayRpcPayloads.cs` | **Identical** — same 8 properties, same types |
| 4 | `OrdersCancelRequestPayload` | `OrdersCancelPayloads.cs` | `GatewayRpcPayloads.cs` | **Identical** — `(Guid? OrderId, string? OrderReference, string? Reason, string? Note)` |
| 5 | `OrdersCancelReplyPayload` | `OrdersCancelPayloads.cs` | `GatewayRpcPayloads.cs` | **Field-different, deliberate and harmless**: property set and types identical (5 properties); Orders' constructor gave `CancellationReason` a default `= null`, Gateway's did not. `tests/Orders.UnitTests/OrdersCancelPayloadTests.cs:55` relies on the default to construct a confirmed-branch reply without naming the argument. Kept the default on the unified type — harmless to the Gateway, which always passes every argument explicitly. |
| 6 | `CatalogReferenceListRequestPayload` | `CatalogReferenceListPayloads.cs` | `GatewayRpcPayloads.cs` | **Identical** |
| 7 | `ProductPayload` | `CatalogReferenceListPayloads.cs` | `GatewayRpcPayloads.cs` | **Identical** |
| 8 | `PartyPayload` | `CatalogReferenceListPayloads.cs` | `GatewayRpcPayloads.cs` | **Identical** |
| 9 | `CurrencyViewPayload` | `CatalogReferenceListPayloads.cs` | `GatewayRpcPayloads.cs` | **Identical** |
| 10 | `CatalogReferenceListReplyPayload` | `CatalogReferenceListPayloads.cs` | `GatewayRpcPayloads.cs` | **Identical** |
| 11 | `CatalogReferenceKinds` (static class, not a record) | `CatalogReferenceListPayloads.cs` | `GatewayRpcPayloads.cs` | **Field-different, deliberate**: both declare the same 4 consts (`Products`/`Retailers`/`Companies`/`Currencies`); Orders' copy additionally declares `public static readonly IReadOnlyList<string> All`, used by `CatalogReferenceListRequestValidator` and `OrdersCreateResponder` to resolve an omitted `kinds` to "all four" — the Gateway never omits `kinds` (always one at a time via `ListCatalogQuery`/`CatalogEndpoints`), so it never declared `All`. Merged as the strict superset (Orders' version). |

Out of scope for this entry (confirmed one-sided by id 84 already, unrelated
subject): `GatewayInvoiceViewPayload` / `GatewayInvoiceListReplyPayload` —
the `billing.invoice.list` pair id 84 deliberately kept separate (field
difference: the Gateway's carries an extra optional `lines`). Left
untouched in `GatewayRpcPayloads.cs`.

### What moved

- **New**: `src/Contracts/Rpc/OrdersRpcPayloads.cs` — `OrdersCreateRequestLine`, `OrdersCreateRequestPayload`, `OrdersCreateReplyPayload`, `OrdersCancelRequestPayload`, `OrdersCancelReplyPayload`.
- **New**: `src/Contracts/Rpc/CatalogRpcPayloads.cs` — `CatalogReferenceKinds`, `CatalogReferenceListRequestPayload`, `ProductPayload`, `PartyPayload`, `CurrencyViewPayload`, `CatalogReferenceListReplyPayload`.
- **Deleted**: `src/Orders/Presentation/Rpc/OrdersCreatePayloads.cs`, `src/Orders/Presentation/Rpc/OrdersCancelPayloads.cs`, `src/Orders/Presentation/Rpc/CatalogReferenceListPayloads.cs` (each contained nothing but the now-moved types — matches the precedent of feature 76/id 84 deleting `StockCheckPayloads.cs`/`SagaCommandPayloads.cs` outright rather than leaving empty stubs).
- **Edited** (added `using OrderToCash.Contracts.Rpc;`, no other change) — validators/responder still declare the client-caused-refusal error types and validation logic locally, they just read the now-shared payload shape:
  - `src/Orders/Presentation/Rpc/OrdersCreateRequestValidator.cs`
  - `src/Orders/Presentation/Rpc/OrdersCancelRequestValidator.cs`
  - `src/Orders/Presentation/Rpc/CatalogReferenceListRequestValidator.cs`
  - `src/Orders/Presentation/OrdersCreateResponder.cs`
  - `src/Gateway/Application/Commands/PlaceOrderCommand.cs`
  - `src/Gateway/Application/Commands/CancelOrderCommand.cs`
  - `src/Gateway/Application/Queries/ListCatalogQuery.cs`
  - `src/Gateway/Presentation/Endpoints/CatalogEndpoints.cs`
- **Edited** `src/Gateway/Application/Rpc/GatewayRpcPayloads.cs` — removed the 10 now-unified record declarations and their header's "group 1" exception text; kept the `GatewayInvoiceViewPayload`/`GatewayInvoiceListReplyPayload` group and updated the header to record id 93 closing id 84's own deliberate exception.
- **Test files touched** (added `using OrderToCash.Contracts.Rpc;` where the file referenced a moved type and did not already import it — several already had the using from prior features and needed no change): `tests/Orders.UnitTests/OrdersCreateRequestValidatorTests.cs`, `OrdersCancelRequestValidatorTests.cs`, `CatalogReferenceListRequestValidatorTests.cs`, `OrdersCancelPayloadTests.cs`, `CatalogReferenceListPayloadTests.cs`; `tests/Orders.IntegrationTests/OrdersCreateIdempotentReplayTests.cs`, `CatalogReferenceListAcceptanceTests.cs`, `OrdersCancelResponderReadinessRaceTests.cs`, `SagaIntegrationTestSupport.cs`, `OrdersCreateResponderTraceContinuationTests.cs`; `tests/Gateway.UnitTests/CancelOrderCommandHandlerTests.cs`, `PlaceOrderCommandHandlerTests.cs`; `tests/Gateway.IntegrationTests/OrdersHttpTests.cs`.
- Verified (read, no change needed): `tests/Orders.UnitTests/AsyncApiSchema.PayloadRecords.cs` and the Gateway equivalent only reference these type names in doc comments (reflection helpers are generic over `Type`); `tests/Orders.IntegrationTests/EfCoreOrderReferenceCatalogListTests.cs` mentions `CatalogReferenceListReplyPayload` only in a doc comment; `tests/Gateway.UnitTests/GatewayRpcPayloadTests.cs`, `ReadOnlyQueryHandlerTests.cs`, `tests/Gateway.IntegrationTests/MoneyRepresentationHttpTests.cs`, `OperatorNoteReachesTimelineEndToEndTests.cs` already had `using OrderToCash.Contracts.Rpc;`.
- Confirmed no fully-qualified (`OrderToCash.Orders.Presentation.Rpc.OrdersCreate…`/`OrderToCash.Gateway.Application.Rpc.OrdersCreate…`) references exist anywhere (`grep -rn` over `src/`, `tests/`, empty result both directions).

### Wire-integrity tests run (acceptance bullet 3)

| Suite | Command | Result |
|---|---|---|
| Contracts.UnitTests (golden envelopes) | `dotnet test tests/Contracts.UnitTests/Contracts.UnitTests.csproj --no-build` | **24/24 passed** |
| Orders.UnitTests (BC23/reflection payload-key guards) | `dotnet test tests/Orders.UnitTests/Orders.UnitTests.csproj --no-build` | **500/500 passed** (498 pre-existing + 2 new from piece 3) |
| Gateway.UnitTests (`GatewayRpcPayloadTests` BC23/name-agreement theories over the now-unified types) | `dotnet test tests/Gateway.UnitTests/Gateway.UnitTests.csproj --no-build` | **237/237 passed** |
| Gateway.IntegrationTests, `OrdersHttpTests`+`MoneyRepresentationHttpTests` (Gateway's own HTTP payload tests) | `dotnet test tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~OrdersHttpTests\|FullyQualifiedName~MoneyRepresentationHttpTests"` | **11/11 passed** (re-run twice, identical) |
| Architecture.Tests (layering/domain-purity unaffected) | `dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --no-build` | **50/50 passed** |
| Orders.IntegrationTests, `OrdersCreateAcceptanceTests`+`OrdersCancelAcceptanceTests`+`CatalogReferenceListAcceptanceTests` | `dotnet test tests/Orders.IntegrationTests/Orders.IntegrationTests.csproj --no-build --filter "…"` | **24/24 passed** |

Byte-identity of the wire is asserted by `GatewayRpcPayloadTests`' two
theories reading the SAME `OrderToCash.Contracts.Rpc` types via reflection
against the parsed spec — before the move these theories already covered
every one of the ten records (they read
`OrderToCash.Gateway.Application.Rpc`'s then-local copies); after the move
they read the identical types from `OrderToCash.Contracts.Rpc`, and the
suite stayed green with no assertion changed. Orders' own
`CatalogReferenceListPayloadTests.cs`/`OrdersCancelPayloadTests.cs` do the
same BC23 check independently from the Orders side, against the same
Contracts types — so both former "copies" now converge on testing one
canonical type from both directions.

### Arming (acceptance bullet 4)

**Arm 1 — undeclared property on a unified record.**

- Backed up `src/Contracts/Rpc/OrdersRpcPayloads.cs`.
- Mutation: added `string ArmId93UndeclaredProperty = "arm"` to `OrdersCreateReplyPayload` (default value kept so every existing call site still compiles — the claim under test is the BC23 key-set guard, not a build break).
- Forced rebuild: `dotnet build --no-incremental` → succeeded (0 errors).
- Ran `dotnet test tests/Gateway.UnitTests/Gateway.UnitTests.csproj --no-build --filter "FullyQualifiedName~GatewayPayload_CarriesExactlyThePropertyNamesAsyncApiDeclares"`.
- **Result: FAILED**, verbatim:
  ```
  record OrderToCash.Contracts.Rpc.OrdersCreateReplyPayload does not carry exactly the property names asyncapi.yaml's 'OrdersCreateReplyPayload' schema declares. Declared by the schema but MISSING from the record: []. Declared by the record but UNDECLARED by the schema: [armId93UndeclaredProperty].
  ```
  1 failed, 25 passed, 26 total.
- Restored from backup; `cmp`-equivalent `diff` against the backup printed nothing ("RESTORE CONFIRMED IDENTICAL"); `touch`ed the file; `dotnet build --no-incremental` succeeded; re-ran the same filter — **26/26 passed**.

**Arm 2 — record name substituted for a real sibling.**

- Backed up `tests/Gateway.UnitTests/GatewayRpcPayloadTests.cs`.
- Mutation: in `RequestAndReplySchemas()`, changed `{ "OrdersCreateRequestPayload", typeof(OrdersCreateRequestPayload) }` to `{ "OrdersCreateRequestPayload", typeof(OrdersCancelRequestPayload) }` — a real sibling substitution (both are declared, wire-real types in the same file).
- Forced rebuild: `dotnet build --no-incremental` → succeeded.
- Ran `dotnet test tests/Gateway.UnitTests/Gateway.UnitTests.csproj --no-build --filter "FullyQualifiedName~GatewayPayload_EveryRowsSchemaNameNamesTheRecordThatRowClaims"`.
- **Result: FAILED**, verbatim:
  ```
  this theory row claims asyncapi.yaml schema 'OrdersCreateRequestPayload' for record OrderToCash.Contracts.Rpc.OrdersCancelRequestPayload, but 'OrdersCancelRequestPayload' does not name that schema (expected the record's name to end with 'OrdersCreateRequestPayload', optionally prefixed with a service or subject qualifier). A row whose schema name is substituted for a real sibling's is invisible to the key-set assertion whenever the two schemas declare the same keys.
  ```
  1 failed, 25 passed, 26 total.
- Restored from backup; `diff` against the backup printed nothing; `touch`ed the file; `dotnet build --no-incremental` succeeded; re-ran the full `Gateway.UnitTests` suite — **237/237 passed**.

### Ported-idiom ledger row (acceptance bullet 5)

**#7 relied on**: generated contract types. `#7`'s payload records are
GENERATED into `packages/contracts/src/generated/` and drift-checked by
`packages/contracts/scripts/check.mjs` and `check.spec.ts`, consumed
directly at `apps/fulfillment/src/application/ports/stock-read.port.ts:3`
and `apps/gateway/src/application/queries/list-stock.query.ts:9`. One
generator, one drift check, structurally impossible for two copies of the
same schema to diverge.

**In #8 that property is supplied by**: nothing — #8 hand-writes its
contract types (no code generation from `asyncapi.yaml`/`openapi.yaml`
exists in this repository), so duplication across a service's
Presentation/Application layer and the Gateway's own caller-side copy was
the observable, repeated consequence (first found and partly closed by
feature 76, extended by id 84 to six more subjects, and now closed for the
remaining three by this entry). **Guard**:
`tests/Gateway.UnitTests/GatewayRpcPayloadTests.cs`'s
`GatewayPayload_CarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped`
and `GatewayPayload_EveryRowsSchemaNameNamesTheRecordThatRowClaims` theories
— armed above (Arm 1 and Arm 2), both fail naming exactly what was broken.
Recorded verbatim in `src/Contracts/Rpc/OrdersRpcPayloads.cs`'s own header
comment as well, pointing back at this record.

## Piece 2 — Projector's PR38 sibling, armed against the F6 mutation family

### Mechanism check

`diff <(tail -n +26 src/Orders/Infrastructure/Messaging/FactRetryDispatcher.cs) <(tail -n +26 src/Projector/Infrastructure/Messaging/FactRetryDispatcher.cs)`
→ **only the namespace line differs** (`OrderToCash.Orders.Infrastructure.Messaging`
vs `OrderToCash.Projector.Infrastructure.Messaging`). Projector's
`FactRetryDispatcher` is the OR2 canonical copy, byte-identical to Orders'
outside the banner/namespace regions the parity guard normalises — so it
swallows the handler's exception inside its own retry loop the same way
Orders' does, and `tests/Projector.IntegrationTests/OffsetContractTests.cs`'s
`PR38_AThrowingHandlerLeavesTheCommittedOffsetUnchanged_ReadFromTheBroker`
had exactly SO9's pre-id-94 shape: a single immediate read taken right
after `gate.Attempts` reached 2, never a polling window.

### Reproduction (before the fix)

- Backed up `src/Projector/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs`.
- Mutation: `EnableAutoOffsetStore = false` → `true` (F6).
- Forced rebuild: `dotnet build --no-incremental` → succeeded.
- Ran `dotnet test tests/Projector.IntegrationTests/Projector.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~PR38_AThrowingHandlerLeavesTheCommittedOffsetUnchanged_ReadFromTheBroker"`.
- **Result: PASSED** (1/1) — the dead arm reproduced. Confirms the mutation is invisible to the old single-read shape, exactly as SO9 was before id 94.
- Restored `KafkaFactStreamSubscriber.cs` from backup (`diff` against backup: empty, "RESTORE CONFIRMED IDENTICAL"), `touch`ed, forced rebuild.

### Fix applied

Ported SO9's repair verbatim in shape: replaced the single immediate
post-`gate.Attempts>=2` read with a polling window (7 s, comfortably past
one `auto.commit.interval.ms` tick at librdkafka's 5000 ms default) that
fails the instant any read disagrees with the baseline, naming the offset
it saw — same doc-comment reasoning, same bracket duration as
`SagaConsumptionTests.SO9`'s own `_autoCommitIntervalBracket`. Also renamed
the local `redeliveryDeadline` variable to `retryDeadline` and the
`gate.Attempts >= 2` assertion message to say "in-process retry" rather
than "redelivery", matching SO9's own vocabulary correction. **No test
method rename was needed**: unlike SO9's old name
(`…AndTheFactIsRedelivered`), PR38's existing name
(`…LeavesTheCommittedOffsetUnchanged_ReadFromTheBroker`) never claimed
Kafka redelivery specifically — its own doc comment already said "never
inferred from the fact that a redelivery happened," so it did not overclaim
and needed only the assertion strengthened, not the name changed. Extended
the test's doc comment to record the id 94/id 93-batch-2 lineage and the
reproduction result.

File touched: `tests/Projector.IntegrationTests/OffsetContractTests.cs`.

### Post-fix verification

- `dotnet build --no-incremental` → succeeded.
- Ran the same filter against the FIXED test (no mutation, baseline code): **1/1 passed** (16 s).
- Re-applied the F6 mutation (`EnableAutoOffsetStore = true`) to `KafkaFactStreamSubscriber.cs`, forced rebuild, re-ran the same filter.
- **Result: FAILED**, verbatim:
  ```
  the committed offset advanced to [p0=unset, p1=1, p2=unset, p3=unset, p4=unset, p5=unset] (baseline was [p0=unset, p1=unset, p2=unset, p3=unset, p4=unset, p5=unset]) WHILE the retry was still deterministically blocked on the gate — a StoreOffset call reached the broker before the handler completed (F6: EnableAutoOffsetStore = true, or StoreOffset moved before the handler's await).
  ```
  1 failed, 0 passed (3 m 11 s — this run's private-container startup was slower than the others; still deterministic, not a timeout-shaped failure).
- Restored `KafkaFactStreamSubscriber.cs` from backup (`diff`: empty, "RESTORE CONFIRMED IDENTICAL"), `touch`ed, forced rebuild.
- Final confirming green run: `PR38…` **1/1 passed** (16 s); full `OffsetContractTests` class (`PR38`+`PR5`) **2/2 passed** (33 s).

### Enumeration of the class (acceptance requirement carried over from id 94's own bullet 4 discipline)

Command: `grep -rln "EnableAutoOffsetStore\|StoreOffset\b" --include="*.cs" src/ tests/ | grep -v '/bin/\|/obj/'`. Every fact-consuming
service's `KafkaFactStreamSubscriber` (Orders, Projector, Notifications) —
all three follow the identical offset-commit-after-handler contract. Of the
three, only Orders (`SagaConsumptionTests.SO9`, fixed by id 94) and
Projector (`OffsetContractTests.PR38`, fixed here) have a committed-offset
integration guard against this mutation family at all — Notifications has
no such integration test (its `KafkaFactStreamSubscriberConfigTests.cs` is
a unit-level config guard only, the same shape piece 3 below ports to
Orders; it does not read the broker's committed offset). Disclosed here,
not fixed: **Notifications has no integration-level guard against F6 to
arm in the first place** — out of this dispatch's three named pieces, and
not filed as a new backlog entry per the brief's scope (which named
Projector's PR38 specifically).

## Piece 3 — Orders' reflection-based Kafka config unit guard

### Pattern located

`tests/Notifications.UnitTests/KafkaFactStreamSubscriberConfigTests.cs` and
`tests/Projector.UnitTests/KafkaFactStreamSubscriberConfigTests.cs` both
read their service's private `KafkaFactStreamSubscriber.BuildConsumerConfig`
by reflection (never a re-declared copy) and assert
`GroupId`/`ClientId`/`AutoOffsetReset`/`EnableAutoCommit`/
`EnableAutoOffsetStore`/`EnablePartitionEof`. Orders — the ORIGINAL of the
three services (design.md §3.1-§3.3 is where these settings were first
decided) — had no such unit-level guard; only the container-level
`SagaConsumptionTests.SO9` exercised the same config, end to end.

### New file

`tests/Orders.UnitTests/KafkaFactStreamSubscriberConfigTests.cs` — two
facts, reflecting into `KafkaFactStreamSubscriber.BuildConsumerConfig`
against a constructed `OrdersSagaOptions`:

- `SO9_BuildConsumerConfig_SetsEarliestNotLatest_SoAFreshConsumerGroupDoesNotSkipTheBacklog`
- `SO9_UsesTheOrdersSagaGroupAndClientIdentity_AndStoresOffsetsOnlyAfterTheHandler`

Every `Assert.True`/`Assert.False` in the second fact carries an explicit
message naming the property and value, per CLAUDE.md's arming rule that a
failure whose message cannot name the claim (`Assert.False(x)` alone prints
only `Expected: False / Actual: True`) is not acceptable evidence — this
was caught on the FIRST arming attempt below (see "message fixed" note)
before finalising the file, so both sibling files' pre-existing bare
`Assert.False(config.EnableAutoOffsetStore)` form was NOT copied verbatim.

### Arming

- Backed up `src/Orders/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs`.
- Mutation: `EnableAutoOffsetStore = false` → `true`.
- Forced rebuild → succeeded.
- Ran `dotnet test tests/Orders.UnitTests/Orders.UnitTests.csproj --no-build --filter "FullyQualifiedName~KafkaFactStreamSubscriberConfigTests"`.
- **First attempt** (message-less `Assert.False`) failed with only `Assert.False() Failure / Expected: False / Actual: True` — did not name the claim, so the assertion was rewritten with explicit messages before accepting the arm as evidence.
- **Second attempt, with the rewritten assertions — Result: FAILED**, verbatim:
  ```
  EnableAutoOffsetStore was True — SO9 requires the offset to be stored ONLY after the handler completes, never by the library's own at-most-once default.
  ```
  1 failed, 1 passed, 2 total.
- Restored from backup (`diff`: empty, "RESTORE CONFIRMED IDENTICAL"), `touch`ed, forced rebuild.
- Confirming green run: **2/2 passed** (25 ms).

## Full regression sweep (all three pieces together)

| Suite | Result |
|---|---|
| Contracts.UnitTests | 24/24 |
| Orders.UnitTests | 500/500 |
| Gateway.UnitTests | 237/237 |
| Projector.UnitTests | 120/120 |
| Architecture.Tests | 50/50 |
| Gateway.IntegrationTests (`OrdersHttpTests`+`MoneyRepresentationHttpTests`) | 11/11 |
| Orders.IntegrationTests (`OrdersCreateAcceptanceTests`+`OrdersCancelAcceptanceTests`+`CatalogReferenceListAcceptanceTests`) | 24/24 |
| Orders.IntegrationTests (`SagaConsumptionTests.SO9…`) | 1/1 |
| Projector.IntegrationTests (`OffsetContractTests` — `PR38`+`PR5`) | 2/2 |

Not re-run in this dispatch (unaffected by any of the three pieces, no
edits made to their source or test files, and a full solution-wide
`./quality.sh` was judged out of budget for a two-piece-plus-one-guard
batch): Billing.\*, Fulfillment.\*, Notifications.\*, Seed.\*,
Cqrs.UnitTests, SharedKernel.UnitTests. `dotnet build --no-incremental` at
solution level succeeded on every rebuild performed during this dispatch
(9 full rebuilds total, each 0 warnings / 0 errors), which is a
whole-solution compile check across every project including those not
test-run again.

## Files touched

**Piece 1**:
- New: `src/Contracts/Rpc/OrdersRpcPayloads.cs`, `src/Contracts/Rpc/CatalogRpcPayloads.cs`
- Deleted: `src/Orders/Presentation/Rpc/OrdersCreatePayloads.cs`, `OrdersCancelPayloads.cs`, `CatalogReferenceListPayloads.cs`
- Edited (using added only): `src/Orders/Presentation/Rpc/OrdersCreateRequestValidator.cs`, `OrdersCancelRequestValidator.cs`, `CatalogReferenceListRequestValidator.cs`, `src/Orders/Presentation/OrdersCreateResponder.cs`, `src/Gateway/Application/Commands/PlaceOrderCommand.cs`, `CancelOrderCommand.cs`, `src/Gateway/Application/Queries/ListCatalogQuery.cs`, `src/Gateway/Presentation/Endpoints/CatalogEndpoints.cs`
- Edited (records removed, header updated): `src/Gateway/Application/Rpc/GatewayRpcPayloads.cs`
- Test usings added: `tests/Orders.UnitTests/OrdersCreateRequestValidatorTests.cs`, `OrdersCancelRequestValidatorTests.cs`, `CatalogReferenceListRequestValidatorTests.cs`, `OrdersCancelPayloadTests.cs`, `CatalogReferenceListPayloadTests.cs`; `tests/Orders.IntegrationTests/OrdersCreateIdempotentReplayTests.cs`, `CatalogReferenceListAcceptanceTests.cs`, `OrdersCancelResponderReadinessRaceTests.cs`, `SagaIntegrationTestSupport.cs`, `OrdersCreateResponderTraceContinuationTests.cs`; `tests/Gateway.UnitTests/CancelOrderCommandHandlerTests.cs`, `PlaceOrderCommandHandlerTests.cs`; `tests/Gateway.IntegrationTests/OrdersHttpTests.cs`

**Piece 2**:
- Edited: `tests/Projector.IntegrationTests/OffsetContractTests.cs` (assertion strengthened to a polling window; doc comment extended)
- No permanent change to `src/Projector/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs` (mutated twice for arming, restored both times, confirmed identical to the pre-dispatch checkout by `diff`)

**Piece 3**:
- New: `tests/Orders.UnitTests/KafkaFactStreamSubscriberConfigTests.cs`
- No permanent change to `src/Orders/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs` (mutated once for arming, restored, confirmed identical by `diff`)

`feature_list.json` was not touched (it was already dirty at the start of
this session from other in-flight work per the conversation's opening git
status — not edited by this dispatch).

## What I could not do / scope notes

- Did not run the FULL repository suite (`./quality.sh`) — ran the targeted
  suites above plus repeated whole-solution `--no-incremental` rebuilds
  instead, to stay inside a reasonable budget for a three-piece dispatch.
  Nothing touched in this dispatch reaches Billing/Fulfillment/
  Notifications/Seed source, so those suites were judged low-risk to skip
  this round.
- Piece 2's enumeration surfaced that Notifications has NO integration-level
  committed-offset guard at all (only the unit-level config test) — flagged
  in the piece 2 section above but not filed as a new backlog entry or
  fixed, since the brief named Projector's PR38 specifically and did not
  ask for a new entry to be filed.
- Piece 3: ported only the two facts (`AutoOffsetReset`, and the
  group/client/offset-store/partition-EOF group) the two sibling files
  already establish as the pattern; did not add anything beyond that
  pattern (e.g. did not add a bootstrap-servers assertion, which neither
  sibling file asserts either).

## Surprises

- The pre-existing `Assert.False(config.EnableAutoOffsetStore)` form in
  BOTH sibling files (Notifications' and Projector's) already carries the
  message-less anti-pattern CLAUDE.md's arming rule specifically calls out
  (A6 of feature 73's review) — I did not "fix" those two pre-existing
  files (out of this dispatch's scope), but did not copy the anti-pattern
  into the new Orders file once the first arming attempt surfaced it.
- Piece 2's confirming-failure run took 3m11s against private per-test
  containers (vs. 16s for a plain pass) — slow but not flaky: the extra
  time is the 7-second polling window observing a real premature commit,
  plus this run's container cold-start being slower than the other runs in
  this session. The failure message itself was immediate and deterministic
  once observed.
