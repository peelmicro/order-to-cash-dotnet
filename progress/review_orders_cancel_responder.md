# review_orders_cancel_responder

Feature id 41, phase 13, `sdd: false`. Reviewed against `feature_list.json` id 41's four acceptance bullets, `specs/shared/saga.md` §4.3's generalisation table, and `specs/shared/asyncapi.yaml`'s `orders.cancel` channel.

## Verdict: REJECTED

Two blocking defects, both of the same family and both invisible to everything else in this harness: a wire-visible decision this feature newly introduced is guarded by nothing, and the ledger row that names its guard names a test that exercises a different code path. The four branches are genuinely built and genuinely work — the architecture, the reverse-order-of-acquisition chain, the terminal rejection and the `SagaStepTable` extension are all correct and all armed. The rejection is about what happens the next time someone edits `StockReleaseReasonFor`.

Effort record deliberately NOT appended to `progress/history.md`: a feature is not closeable while rejected, and the record belongs to the closing pass so that the follow-up round is counted in it.

## Scope of my own verification

I did not re-run `./quality.sh`; the implementer's 1276/1276 claim is about the full suite and re-running it would be duplicated cost. What I ran instead, all from my own session:

- `dotnet test tests/Orders.UnitTests` — **337 passed, 0 failed, 0 skipped**, matching the report.
- `dotnet test tests/Architecture.Tests` — **16 passed, 0 failed**. Domain purity and layering verified by running NetArchTest, not by eye (C3).
- `dotnet test tests/Orders.IntegrationTests --filter "…SagaCompensationCreditRejectedTests|…OrdersCancelAcceptanceTests"` against real NATS / MS-SQL / Kafka — **6 passed**, run *under mutation* (see D1).
- Eight mutation probes of my own, four deletion-family and four corruption-family, each with a forced `--no-incremental` rebuild before both the failing and the confirming run, each restored from a `cp` backup and verified with `cmp` (never `git checkout --`). All six touched files confirmed byte-identical to backup afterwards, whole-`src/` diffstat back to its pre-probe shape (16 files, 615 insertions, 70 deletions), and a final forced-rebuild green run at 337/337.
- `diff -rq specs/shared` against the #7 checkout: identical except `test-matrix.md`, as C7 expects.
- Ledger citations opened in `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs` and read.

## Defects

### D1 (BLOCKING) — `StockReleaseReasonFor` is unguarded end to end, and ledger row 1's named guard cannot fail

**File:** `src/Orders/Application/Sagas/SagaCommandRequestFactory.cs:46-55` (the mapping), `src/Orders/Application/Sagas/SagaFactHandler.cs:114` (its only caller).

This feature converted `stock.release`'s `reason` from a hardcoded literal into a two-way switch on the triggering fact's `eventType`. That switch is a new decision point on the wire, and nothing anywhere tests it.

**Probe (corruption family).** I transposed the two arms:

```csharp
"credit.rejected.v1" => "order_cancelled",
"credit.released.v1" => "credit_rejected",
```

- `dotnet test tests/Orders.UnitTests` → `Passed!  - Failed: 0, Passed: 337, Skipped: 0`
- `dotnet test tests/Orders.IntegrationTests --filter "…SagaCompensationCreditRejectedTests|…OrdersCancelAcceptanceTests"` → `Passed!  - Failed: 0, Passed: 6, Skipped: 0, Duration: 45 s` — real broker, real database, real responder.

**Why the named guard cannot fail.** Ledger row 1 names `CancelOrderCommandHandlerTests.StockReserved_EnqueuesStockReleaseWithReasonOrderCancelled_…`. That test drives `CancelOrderCommandHandler.BeginStockReleaseCompensationAsync` (`CancelOrderCommandHandler.cs:172`), which builds `new StockReleaseRequestPayload(order.OrderReference.Value, "order_cancelled")` **inline** and never calls the factory. The row's own "in #8 supplied by" column says as much — "`CancelOrderCommandHandler`'s own two direct enqueues … never through this factory" — and then names a test of those direct enqueues as the factory's guard. The test is a good test; it guards a different thing. This is precisely the post-feature-19 rule in `CLAUDE.md`: *"the test re-implemented the conversion instead of reading through the mapper … naming a guard in `tasks.md` creates the obligation to arm it; it does not discharge it."*

**Why it matters, not merely that it is unguarded.** With the arms transposed:

- R27's credit-rejection compensation asks Fulfillment to release with `order_cancelled`, so an order cancelled because credit was refused is labelled on the wire as an operator cancellation.
- This feature's **own** fourth branch asks with `credit_rejected`. The returning `stock.released.v1` goes through `SagaStepTable.MapReason` to `CancellationReason.CreditRejected`, which `Order.IsReasonApplicable` (`src/Orders/Domain/Order.cs:422-428`) permits **only** from `StockReserved`. From `credit_approved`/`confirmed` it throws `CancellationReasonNotApplicableError` — the credit-hold cancel branch dead-ends and the order is stranded mid-compensation.

So the single decision this feature added to the wire is the single thing 337 unit tests and 6 real-container integration tests cannot see.

**Contributing cause, worth fixing at the same time.** `tests/Orders.IntegrationTests/SagaCompensationCreditRejectedTests.cs:33,78` captures the observed request into `observedRelease` and then asserts only `Assert.NotNull(observedRelease)`. That is the feature-17 shape verbatim — a guard that counts the row without opening it — and it was survivable while the reason was a constant. This feature made it a branch and did not add the read. `OrdersCancelAcceptanceTests.cs:122` does assert the reason, but on the `stock_reserved` branch whose payload is built inline, so it does not reach the factory either; and the `credit_approved`/`confirmed` test's `stock.release` stand-in (`OrdersCancelAcceptanceTests.cs:174-181`) never looks at `request.Reason` at all, while the `stock.released.v1` fact it then publishes hardcodes `"order_cancelled"` regardless of what was asked for — which is exactly why the mutation survived the integration run.

**Enumeration behind the absence claim** (`grep -rn --include=*.cs -e "StockReleaseReasonFor" -e "BuildStockReleaseJson" src tests`), 7 hits, classified:

| Hit | Classification |
|---|---|
| `src/Orders/Application/Sagas/SagaFactHandler.cs:114` | the one production caller — source, not a test |
| `src/Orders/Application/Sagas/SagaCommandRequestFactory.cs:21,36,43,44,46` | declaration site and its doc comments — source |
| `src/Orders/Application/Commands/CancelOrderCommandHandler.cs:165` | doc comment — source |

Zero hits under `tests/`. No test calls either method.

**To clear D1:** assert the `reason` on the `stock.release` request the responder actually observes, on **both** fact-driven paths — `credit.rejected.v1` → `credit_rejected` and `credit.released.v1` → `order_cancelled` — and arm it by transposing the two arms of `StockReleaseReasonFor`, recording the verbatim failure. Correct ledger row 1's Guard column to name the test that reads through the factory. A unit test over `SagaCommandRequestFactory.BuildStockReleaseJson(order, eventType)` is the cheapest form and is sufficient; the integration read at `SagaCompensationCreditRejectedTests.cs:78` is worth adding regardless, since it costs one line.

### D2 (BLOCKING) — the new `billing.credit.release` payload pair skips the BC23 spec-parity guard, and the reply record is missing a schema-required field

**File:** `src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs:137-142`.

Orders' `CreditReleaseReplyPayload` declares `Released, OrderReference, CreditCode, Currency, ReleasedAmount`. `specs/shared/asyncapi.yaml:3519-3541` declares six properties and lists **`availableCreditAfter` as required**. Billing's own copy of the same schema has it (`src/Billing/Infrastructure/Messaging/Rpc/CreditRpcPayloads.cs:33-39`).

`tests/Orders.UnitTests/SagaCommandPayloadTests.cs:186-195` carries a BC23 `[InlineData]` row for every one of the ten pre-existing saga-command payload schemas, with a G5 arming case proving the guard falsifiable. This feature added the eleventh and twelfth records to the same file and added neither a BC23 row nor a round-trip test — and the omission is not hypothetical, it is the reason the missing required field shipped. The implementer added exactly this guard for the *other* new payloads of the same feature (`tests/Orders.UnitTests/OrdersCancelPayloadTests.cs:64-65`, with its own G5 case), so the convention was in hand and applied unevenly rather than unknown.

Harmless at runtime today — Orders only deserialises this reply, and `System.Text.Json` ignores the extra key — which is the whole reason only the guard could have caught it. One consequence is visible now: the stand-in at `tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs:167` serialises Orders' partial record, so the fourth branch's "end to end" proof answers with a reply body that would fail schema validation.

**To clear D2:** add `AvailableCreditAfter` to Orders' `CreditReleaseReplyPayload`, add the two BC23 `[InlineData]` rows for `CreditReleaseRequestPayload` and `CreditReleaseReplyPayload`, and confirm the existing G5 case still arms them.

## Advisories (not blocking, do not re-do work for these)

- **A1 — narrowed coverage on the two multi-variant fact types, judged adequate.** The generic `FactsCrossStatuses` theory previously ran `credit.released.v1` and `stock.released.v1` across all nine statuses; both are now excluded (`SagaStepTableTests.cs:31-35`) and the "no variant matches" case is checked at `Placed` only (`:262-268`). I probed the loss rather than assuming it: `CreditReleasedV1_ExposesThreeVariants_…` and `StockReleasedV1_ExposesThreeVariants_…` pin both the variant **count** and the exact **precondition set**, so adding a fourth variant or moving one to a wrong status fails, and `ForStatus` is a pure equality find over that pinned set. The exclusion mirrors #7's `MULTI_VARIANT_FACT_TYPES`. Recorded so the next reader does not re-derive it.
- **A2 — the disclosed operator-cancel-vs-forward-progress race is accurately described and genuinely out of scope,** but is recorded only in `progress/impl_orders_cancel_responder.md` and an XML `<remarks>`. Both compensation branches enqueue without touching the aggregate, so no concurrency token would catch it; the mechanism is exactly as the report states and #7 disposed of the identical finding the same way. Recommend the leader file a backlog entry — a race that lives only in prose is one `grep` away from being missed, which is this repository's own recorded lesson.
- **A3 — `OrdersCreateResponder` now serves three subjects and its class-level `<remarks>` still reasons about only two** (`src/Orders/Presentation/OrdersCreateResponder.cs:38-45` explains `orders.create`'s and `catalog.reference.list`'s loop concurrency and does not mention `orders.cancel`'s). Cosmetic; the code is correct.

## Acceptance bullets

| Bullet | Finding |
|---|---|
| 1 — `POST /orders/{id}/cancel` succeeds through the Gateway | **Met as far as it can be.** The Gateway (id 25) does not exist; proven at the NATS boundary by `OrdersCancelAcceptanceTests` against real NATS + MS-SQL + Kafka through the real responder and the production DI composition, matching `orders_catalog_responder`'s precedent. I ran these. |
| 2 — reuses the existing cancelled state and `cancellationReason` invariant, no new domain modeling | **Met.** `git diff --stat src/Orders/Domain` and `git status --porcelain src/Orders/Domain` are both **empty** — the domain layer was not touched. `Order.Cancel` is the single guard for both the immediate and the terminal outcome. |
| 3 — terminal state rejected with a domain error, not a 503 | **Met, guarded in both mutation families.** Deletion is armed by the implementer (row A). I probed the *kind* of rejection separately, since guarding *that* it is rejected does not guard *what* it is: changing `ORDER_NOT_CANCELLABLE` to `UNAVAILABLE` at `OrdersCreateErrorMapper.cs:75` failed `OrdersCreateErrorMapperTests.Map_AnOrderNotCancellableError_MapsToOrderNotCancellableNotValidationFailedNotUnavailable` with `Assert.Equal() Failure: Strings differ`. The wire-level half is proven at `OrdersCancelAcceptanceTests.Terminal_RepliesOrderNotCancellableNot503_…` against a real `RpcError` body. |
| 4 — the operator note lands on the read-model timeline | **Not built. Disclosure honest and complete; not a rejection basis** per the leader's prior adjudication (`SA-2` is at the gate; `specs/shared/openapi.yaml:1344-1347` promises a behaviour `specs/shared/asyncapi.yaml:2602-2627`'s own event contract cannot carry, identically in #7). Verified independently below. |

**Bullet 4, disclosure verified as a search result.** `grep -rni --include=*.cs -e "operatornote" -e "\bnote\b" -e "timeline" tests/` returns zero occurrences of `operatorNote` anywhere; every `note` hit is either a constructor argument passed and never asserted (`OrdersCancelRequestValidatorTests.cs:29,38,47,56,75`, `CancelOrderCommandHandlerTests.cs:34,74,103,142,168,198`, `OrdersCancelAcceptanceTests.cs:336,350` — including the one place a non-null note is actually sent over the wire, `:336`, where nothing downstream is asserted), or the wire **key** `"note"` in the BC23 request-schema key set (`OrdersCancelPayloadTests.cs:25,64,92`), or the English word in a prose comment (`BillingHostFixture.cs:23`, `OutboxWireParityTests.cs:98`, `GoldenEnvelopeParityTests.cs:28,163`). Every `timeline` hit is Projector/Seed read-model machinery predating this feature. **No test claims this bullet is met.** One difference from #7 worth recording rather than faulting: #7 threaded the note into a synthetic triggering envelope for DLQ diagnostics (`cancel-order.handler.ts:271-285`); #8 cannot, because its `saga_commands` row stores only a `triggering_event_id` `Guid` and no envelope (`ISagaCommandStore.cs:29-35`, `SagaCommandConfiguration.cs:30`). Stopping at the command record is the correct call here, and the report says so plainly.

## The four branches — probed, not taken on trust

The leader's specific concern was branch 4 being "wired but dead for lack of a responder", the shape #7's catalogue endpoints had. It is not:

- Subject constants agree exactly: `RpcSubjects.CreditRelease = "billing.credit.release"` (`src/Orders/…/RpcSubjects.cs:32`) and `CreditSubjects.CreditRelease = "billing.credit.release"` (`src/Billing/Presentation/Rpc/CreditSubjects.cs:15`).
- Billing's responder calls `RequireMeta(message.Headers, …)` **before** dispatch (`BillingRpcResponder.cs:219`) and would refuse a header-less request. `NatsSagaCommandsAdapter.SendAsync` builds a fresh `NatsHeaders` with `x-correlation-id`/`x-request-id` for **every** subject including this one, so the call would be answered by real Billing, not only by the stand-in.
- The **request** record is field-for-field identical on both sides (`string OrderReference, string RetailerCode, string CompanyCode`). Only the reply diverges — that is D2.
- The reverse-order-of-acquisition claim is proven by ordering, not by co-occurrence: two separate stand-in responders append to one shared queue and the test asserts `["credit.release", "stock.release"]` exactly (`OrdersCancelAcceptanceTests.cs:263`), with `despatch.create` deliberately unanswered so forward progress cannot race it.

## The `SagaStepTable` extension — existing rows unchanged

Probed rather than read (leader's item 3). Changing the **pre-existing** `stock.released.v1` / `StockReserved` variant's compensation builder from `CompensationStepsFrom` to the new `CompensationStepsFromCreditThenStockRelease` failed `R28_SO7_StockReleasedV1_StockReservedVariant_CancelsWithExactlyOneStockReleasedCompensationStepBuiltFromTheObservedFact` on both theory cases with `Assert.Single() Failure: The collection contained 2 items`. The pre-existing row's behaviour is still guarded and still means what it meant; the retitle did not hollow it out. `For` retaining its single-variant contract for the other twelve fact types is likewise still guarded (see the ledger table below).

## Ported-idiom ledger — every citation opened in #7, every guard probed

| Row | "#7 relied on X" — citation resolves? | Guard — seen to fail? |
|---|---|---|
| 1. Reason-parametric `stock.release` | **Yes.** `apps/orders/src/application/saga-command-payloads.ts:60-75` is `stockReleaseReasonFor(fact: Envelope)`, switching on `fact.eventType`, validating `credit.released.v1`'s own payload `reason` and throwing otherwise; called from the `'stock.release'` case of `buildSagaCommandPayload` (~:103). `cancel-order.handler.ts:157,212`'s two direct-enqueue methods do build their payload literals inline, as the row says. The deliberate #8 simplification (key off `eventType` only) is stated, not hidden. | **NO — see D1.** The named test never reaches the code the row is about; transposing the mapping leaves 337 unit and 6 real-container integration tests green. |
| 2. Multi-variant step-table lookup | **Yes.** `saga-steps.ts:126` is `type SagaStepEntry = SagaStep \| readonly SagaStep[]`, `:129` widens `SAGA_STEPS`, `:275-297` are `stepVariantsFor`/`stepForStatus`, `:304-315` is `stepFor` throwing `saga-steps: stepFor("…") is ambiguous — N variants exist; use stepForStatus(eventType, status) instead`. #8's wording is near-identical, as claimed. | **Yes.** Deleting the ambiguity throw from `SagaStepTable.For` failed `For_ThrowsForBothMultiVariantEventTypes_RatherThanSilentlyPickingOne` — `Assert.Throws() Failure: No exception was thrown`. |
| 3. Dispatch-owed event for a now-conditional command | **Yes, including the independent-convergence claim.** `apps/orders/src/application/events/saga-dispatch.events.ts:61` is `export class CreditReleasedForCancellationRecorded` — the same name #8 chose; `commands/saga-fact.handlers.ts:189-195` publishes it only when `result.enqueued`; `sagas/order.sagas.ts:27` imports it and `:118-125` is the sixth branch mapping it to `IssueStockReleaseCommand`. | **Yes, both families, probed directly rather than through row F's indirect step-table deletion.** *Deletion:* removing the conditional publish from `HandleCreditReleasedFactCommandHandler` failed `SagaFactCommandHandlerTests.CreditReleasedV1_CreditApprovedOrConfirmedVariant_PublishesCreditReleasedForCancellationRecorded` on both statuses — `Assert.Single() Failure: The collection was empty`. *Corruption:* changing the sixth saga class's signalled kind from `StockRelease` to `CreditRelease` failed `OrderSagasTests.SO3_EachDispatchOwedEvent_SignalsItsOwnSagaCommandAndNothingElse` — `Assert.Equal() Failure: Values differ`. |

Two of three rows are sound and well-cited. Row 1 is the ledger's own failure mode one level up, and it is the second consecutive feature in which a row's guard column was the weak half — worth saying out loud, because the enumeration keeps working while the arming does not.

## My own mutation probes, in full

| # | Family | File / mutation | Result |
|---|---|---|---|
| P1 | corruption | `CancelOrderCommandHandler.cs:147` — transpose `RetailerCode`/`CompanyCode` on `CreditReleaseRequestPayload` | **FAILS** `CancelOrderCommandHandlerTests.CreditApprovedOrConfirmed_EnqueuesCreditReleaseOnly_…` ×2 — `Assert.Equal() Failure: Strings differ`. Fixture codes are distinct (`RETAILER-01`/`COMPANY-01`), so the transposition genuinely bites. |
| P2 | corruption | `OrdersCreateErrorMapper.cs:75` — `ORDER_NOT_CANCELLABLE` → `UNAVAILABLE` | **FAILS** `OrdersCreateErrorMapperTests.Map_AnOrderNotCancellableError_…NotUnavailable` — `Assert.Equal() Failure: Strings differ` |
| P3 | corruption | `SagaStepTable.cs:194` — pre-existing `StockReserved` variant's compensation builder swapped for the new one | **FAILS** `R28_SO7_StockReleasedV1_StockReservedVariant_…` ×2 — `Assert.Single() Failure: The collection contained 2 items` |
| P4 | deletion | `SagaFactCommandHandlers.cs` — delete the conditional `PublishAsync` | **FAILS** `SagaFactCommandHandlerTests.CreditReleasedV1_CreditApprovedOrConfirmedVariant_Publishes…` ×2 — `Assert.Single() Failure: The collection was empty` |
| P5 | corruption | `OrderSagas.cs` — sixth handler signals `CreditRelease` instead of `StockRelease` | **FAILS** `OrderSagasTests.SO3_EachDispatchOwedEvent_SignalsItsOwnSagaCommandAndNothingElse` — `Assert.Equal() Failure: Values differ` |
| P6 | deletion | `SagaStepTable.cs` — delete the `For` ambiguity throw | **FAILS** `For_ThrowsForBothMultiVariantEventTypes_RatherThanSilentlyPickingOne` — `Assert.Throws() Failure: No exception was thrown` |
| **P7** | **corruption** | **`SagaCommandRequestFactory.cs:48-49` — transpose both `StockReleaseReasonFor` arms** | **SURVIVES. 337/337 unit green; 6/6 integration green against real NATS/MS-SQL/Kafka.** → **D1** |
| **P8** | **static** | **`CreditReleaseReplyPayload` vs `asyncapi.yaml:3519-3541`** | **Missing required `availableCreditAfter`; no BC23 row exists that would have caught it.** → **D2** |

Restore discipline: every mutation restored from a `cp` backup, verified with `cmp` (all six files reported byte-identical), `dotnet build --no-incremental` forced before every confirming run, final green at 337/337, whole-`src/` diffstat back to 16 files / 615 insertions / 70 deletions.

## CHECKPOINTS.md — boxes walked

**C1 — harness complete**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer.
- [x] Every agent definition declares its model.
- [x] `./init.sh` exits 0 (leader-verified before dispatch).

**C2 — state coherent**
- [x] At most one feature `in_progress` — id 41 returned to `in_progress` by this review, and it is the only one.
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it.
- [x] `progress/current.md` describes the active session.
- [x] Every `blocked` feature records why. (None blocked.)

**C3 — architecture respected**
- [x] No forbidden framework reference inside any `Domain/` folder — **verified by running `tests/Architecture.Tests`, 16/16 passed**, not by eye.
- [x] No cross-service DB access. Orders reaches Billing only over `billing.credit.release` NATS RPC; `CreditReleaseRequestPayload` carries `orderReference`/`retailerCode`/`companyCode` as business identifiers, no FK.
- [x] No shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs`.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` (architecture suite).
- [x] `src/SharedKernel` still has zero `PackageReference`.
- [x] No `decimal` in domain arithmetic — this feature touched no domain file at all.
- [x] Every interaction correctly classified: `orders.cancel` and `billing.credit.release` are RPC (a request expecting an answer); `credit.released.v1`/`stock.released.v1` are Kafka facts driving the saga. No Kafka-as-request-bus, no RPC-for-facts.
- [x] No stray debug logging, no context-free TODOs.

**C4 — verification real**
- [ ] **`./quality.sh` passes** — not re-run by me by design; the implementer reports exit 0 / 1276 passed. Left unmarked because D1 and D2 require code changes, so the run that matters has not happened yet.
- [x] Domain tests are pure — no framework, DB or broker in the domain suite.
- [x] Integration tests use Testcontainers against real MsSql/Kafka/NATS — I ran six of them and watched the containers come up (45 s).
- [ ] **Coverage thresholds** — not independently verified this round; deferred to the re-review's `quality.sh`.
- [x] No Jest anywhere; xUnit is the backend runner.

**C5 — session close**
- [x] No suspicious untracked files. Untracked set is this feature's and feature 40's source, tests and progress notes.
- [ ] **`progress/history.md` entry with effort record** — deliberately not written: id 41 is not closing this round.
- [x] `feature_list.json` reflects true state — id 41 set back to `in_progress` by this review.
- [x] The human will be told what was done and how to test it (leader's report).
- [x] Claude did not commit.

**C6 — SDD**: not applicable, `sdd: false`. No `specs/orders_cancel_responder/` is required and none is claimed; `specs/shared/test-matrix.md` correctly gets no new row.

**C7 — reuse fidelity**
- [x] **`specs/shared/` byte-identical to #7's** except `test-matrix.md` — verified with `diff -rq specs/shared …/order-to-cash-nestjs/specs/shared`, whose only output is the expected `test-matrix.md` line.
- [x] No silent fork; `SA-2` is an explicit, human-gated amendment in flight, not an edit.
- [x] The `R<n>` ids reused (R24, R25, R27, R28, SO2, SO3, SO7) genuinely name the same behaviours here — checked against the step-table rows and `saga.md` §4.3.
- [ ] `n8n/workflows/*.json` fire green against the .NET Gateway — not applicable yet; the Gateway is id 25.
- [ ] Black-box API script parity — not applicable yet; same reason.
- [ ] `progress/history.md` effort records complete — pending this feature's close.
- [ ] README benchmark section — pending.

## Traceability

`sdd: false`, so there is no `requirements.md` and no feature-local `R<n>` set. The mapping I verified is acceptance bullet → named test, given in the bullets table above, plus the reused shared-spec ids the step table transcribes:

| Claim | Test verified to exist and to bite |
|---|---|
| `saga.md` §4.3 row "Operator cancels while `placed`" | `CancelOrderCommandHandlerTests.Placed_CancelsImmediately_ReasonOperatorCancelledAndNoCompensationPlanned`; `OrdersCancelAcceptanceTests.Placed_ThroughTheRealNatsWire_…` |
| §4.3 row "Operator cancels while `stock_reserved`" | `CancelOrderCommandHandlerTests.StockReserved_EnqueuesStockReleaseWithReasonOrderCancelled_…`; `OrdersCancelAcceptanceTests.StockReserved_EnqueuesStockReleaseOverTheRealWire_…` |
| §4.3 row "Operator cancels while `credit_approved` or `confirmed`", release order | `CancelOrderCommandHandlerTests.CreditApprovedOrConfirmed_EnqueuesCreditReleaseOnly_…` (armed by my P1); `OrdersCancelAcceptanceTests.CreditApprovedOrConfirmed_IssuesCreditReleaseStrictlyBeforeStockRelease_ReverseOrderOfAcquisition` (ordered queue, not co-occurrence) |
| §4.3 "cancellation impossible from `despatched` onwards" | `CancelOrderCommandHandlerTests.Terminal_ThrowsOrderNotCancellableAndLeavesTheOrderUntouched` ×5; `OrdersCreateErrorMapperTests.Map_AnOrderNotCancellableError_…` (armed by my P2); `OrdersCancelAcceptanceTests.Terminal_RepliesOrderNotCancellableNot503_…` |
| §4.3 point 3 "both steps must be visible", causal order | `SagaStepTableTests.StockReleasedV1_CreditApprovedOrConfirmedVariant_CancelsWithBothCompensationStepsInCausalOrder` ×2 |
| R24 (`credit.released.v1` from `paid` completes the order) unchanged | `SagaStepTableTests.R24_CreditReleasedV1_PaidVariant_CompletesTheOrderAndOwesNothing` |
| R25 precondition check, generalised | `SagaStepTableTests.ForStatus_ReturnsNullWhenNoVariantsPreconditionMatches_TheGeneralisedR25Case` (see A1) |
| R28/SO7 unchanged | `SagaStepTableTests.R28_SO7_StockReleasedV1_StockReservedVariant_…` (armed by my P3) |
| `asyncapi.yaml` `OrdersCancelRequestPayload`/`OrdersCancelReplyPayload` wire shape | `OrdersCancelPayloadTests` incl. BC23 parity + G5 arming |
| `asyncapi.yaml` `CreditReleaseRequestPayload`/`CreditReleaseReplyPayload` wire shape | **MISSING — D2** |
| `stock.release` reason selection | **MISSING — D1** |

## What must change before re-review

1. **D1** — guard the value `StockReleaseReasonFor`/`BuildStockReleaseJson` produces, on both fact-driven paths; arm it by transposing the two arms and record the verbatim failure. Add the one-line reason assertion at `SagaCompensationCreditRejectedTests.cs:78` where the request is already captured. Correct ledger row 1's Guard column to name a test that reads through the factory.
2. **D2** — add `AvailableCreditAfter` to Orders' `CreditReleaseReplyPayload`; add BC23 `[InlineData]` rows for both new credit-release schemas in `SagaCommandPayloadTests`.
3. Re-run `./quality.sh` with no concurrent build activity and report the count off that run.
4. Nothing else. Bullets 1–3 are met, the four branches are correct, the `SagaStepTable` extension is sound and does not disturb existing rows, the race and the bullet-4 gap are honestly and completely disclosed, and ledger rows 2 and 3 are correct and armed. **Do not re-do any of that work.**

## Note on phase 13

**Phase 13 is not closed.** Ids 25 (`gateway_rest_auth`), 26 (`gateway_sse_push`), 56 (`composition_root_env_reads_are_unguarded`), 60 (`envelope_fixture_collisions_defeat_provenance_assertions`) and 61 (`catalog_ordering_claim_guarded_for_products_only`) all remain, plus id 41 now back in `in_progress`.

`feature_list.json` was edited on one line only (id 41 `"in_review"` → `"in_progress"`); the leader's two uncommitted hunks (the id-56 acceptance edit and the new id-61 entry), and the id-40 hunk, are untouched. No `git checkout --` was run on any file at any point in this review.

---

# Round 2 — 2026-09-08

Round 1 above is closed and unamended. This round judges the implementer's **Fix round 2** (D1, D2) and **Fix round 3** (the readiness race the leader sent back after rejecting round 2's "transient flake" conclusion).

## Verdict: APPROVED

D1 and D2 are cleared: the reason switch is now guarded in both a unit and an integration test that genuinely read through the factory, and I armed both myself; the schema-required field is present and the BC23 rows exist. Fix round 3 was not asked for by me and is the strongest work in the feature — the deterministic reproduction is real, I reproduced its two claims independently, and its second finding (an unpaced retry loop is not a retry loop) is a genuine discovery about this repository's own test harness that four earlier features shipped without noticing.

Four non-blocking findings below, one of which (**B1**) is a guard-strength gap I created myself in round 1 by prescribing the narrow fix, and one of which (**B2**) narrows a disclosure the leader has already filed as backlog id 63.

**The leader's decision to send fix round 3 was correct, and is worth recording as such.** Round 2's report reached the right *mechanism* in the same paragraph it dismissed it, then used a green run as evidence about a red one. Had it been approved as filed, the harness would have kept a race that fires under exactly the conditions phase 13's remaining features (a Gateway calling six NATS subjects) will create.

## Scope of my own verification

I did not re-run `./quality.sh`; the leader reports it green twice (**1284 passed / 0 failed / 16 projects, exit 0**) with nothing concurrent, and the claim under test this round is not about the full suite. What I ran, all in my own session, every mutation restored from a `cp` backup and confirmed with `cmp`, every confirming run preceded by `dotnet build --no-incremental`:

- `dotnet test tests/Orders.UnitTests` — **342 passed, 0 failed** (final, post-restore, forced rebuild).
- `dotnet test tests/Architecture.Tests` — **16 passed, 0 failed**. C3 verified by running NetArchTest, not by eye.
- `dotnet test tests/Orders.IntegrationTests --filter "…SagaCompensationCreditRejectedTests|…OrdersCancelAcceptanceTests|…OrdersCancelResponderReadinessRaceTests"` against real NATS / MS-SQL / Kafka — **9 passed, 0 failed** (final), and the same set run **under mutation** (see Q1 below).
- Six mutations of my own this round: three against the readiness fix, one against the reason switch (unit), one against the reason switch (integration), one against the credit-release reply record.
- `git diff --stat src/` — **16 files, 616 insertions, 70 deletions**, exactly round 1's 615 plus D2's single added line.

Two files carry an mtime later than the implementer's last write — `tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs` (22:08) and `OrdersCancelResponderReadinessRaceTests.cs` (22:09). Those are **my** `cp` restores, not edits; both were confirmed byte-identical to their pre-mutation backups with `cmp` and both suites are green after a forced rebuild.

## Round 1's items

### D1 — CLEARED, and armed by me in both families

`tests/Orders.UnitTests/SagaFactHandlerTests.cs:122-163` adds `CreditRejectedV1_StockReservedVariant_EnqueuesStockReleaseWithReasonCreditRejected_ThroughTheFactory` and `CreditReleasedV1_CreditApprovedOrConfirmedVariant_EnqueuesStockReleaseWithReasonOrderCancelled_ThroughTheFactory` (×2 statuses). **Does the test execute the code the ledger row is about?** Read, not assumed: both build the real `SagaFactHandler`, call `HandleAsync`, and deserialise the JSON string captured by `FakeSagaCommandStore.Enqueued` — which is the exact return value of `SagaCommandRequestFactory.BuildStockReleaseJson(order, fact.EventType)` at `src/Orders/Application/Sagas/SagaFactHandler.cs:114`, the one production call site. Nothing re-implements the switch.

- **My probe (unit).** Transposed both arms of `StockReleaseReasonFor` (`SagaCommandRequestFactory.cs:48-49`): `Failed: 3, Passed: 339, Total: 342` — all three new cases, `Assert.Equal() Failure: Strings differ / Expected: "order_cancelled" / Actual: "credit_rejected"` and its mirror. Round 1's identical mutation left 337/337 green.
- **My probe (integration, the decisive one).** The **same** transposition, against real NATS / MS-SQL / Kafka, run over the **same two suites that survived it in round 1**: `Failed: 1, Passed: 5, Total: 6, Duration: 47 s` — `SagaCompensationCreditRejectedTests.R27_R28_SO6_SO7_ReleasesThenCancelsInCausalOrderWithOneCompensationStepAndNeverRetriesTheRejectedHold`, `Assert.Equal() Failure: Strings differ`. That is round 1's P7 reversed: the one-line read added at `SagaCompensationCreditRejectedTests.cs:83` turned a suite that counted the row into one that opens it.
- Restored, `cmp` byte-identical, forced rebuild, 9/9 and 342/342 green.

**Ledger row 1's corrected Guard column is sound.** It names the two tests above, which execute the factory. Rows 2 and 3 were verified in round 1 and are untouched.

### D2 — CLEARED as prescribed; see B1 for what the prescription did not cover

`src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs:137-143` now declares `CreditReleaseReplyPayload(bool Released, string OrderReference, long AvailableCreditAfter, string? CreditCode = null, string? Currency = null, long? ReleasedAmount = null)` — matching `specs/shared/asyncapi.yaml:3519-3541` and Billing's own copy, with the two call sites moved to named arguments. The two BC23 `[InlineData]` rows are at `tests/Orders.UnitTests/SagaCommandPayloadTests.cs:199-200` and pass against the spec parsed from `specs/shared/asyncapi.yaml`; `G5` still passes (24/24 in the file's own filtered run, and the whole 342 green here).

## Fix round 3 — probed, not accepted

### Q1. Is the deterministic reproduction real, or an exception type any typo would produce?

**Real, and I verified it by breaking it in two different directions rather than by reading it.**

`OrdersCancelResponderReadinessRaceTests` is three tests. Test 1 (`RequestSentBeforeAnythingSubscribes_ThrowsNoResponders_EveryTime`) is documentation, not a guard — it asserts that NATS answers `NatsNoRespondersException` when nothing is subscribed, which any subject, including a typo, would satisfy. The report presents it as the "certain limiting case", which is fair, and it does no harm. **The load-bearing pair is tests 2 and 3**, which share one generated subject (`$"orders.cancel.readiness-race-repro.{Guid.NewGuid():N}"`, so a typo is not expressible) and one 300 ms-delayed synthetic subscriber, and differ only in whether the caller goes through `SagaIntegrationTestSupport.WaitUntilReachableAsync` first. Test 3 asserts **success**, so a wrong subject fails it rather than passing it.

| # | My mutation | Result |
|---|---|---|
| Q1a | Subscriber delay `300 ms` → `TimeSpan.Zero` in both tests | `WithoutTheWait_ADelayedSubscriberLosesTheRaceDeterministically` **FAILS 3 runs out of 3** — `Assert.Throws() Failure: No exception was thrown / Expected: typeof(NATS.Client.Core.NatsNoRespondersException)`. The 300 ms is load-bearing: the test is asserting the subscriber's lateness, not a constant. |
| Q1b | Subscriber delay `300 ms` → `30 s` in both tests | `WithTheWait_ADelayedSubscriberNoLongerLosesTheRace` **FAILS** after **5 s** — `System.TimeoutException : '…readiness-race-repro.5d4dba0e…' never became reachable.` Test 3's success genuinely depends on the subscriber becoming live; it is not satisfiable by an already-ready broker. |

So the claim "a change of kind, not of probability" holds: with the delay in place the race is lost every time, and the wait is what changes the outcome.

### Q2. The pacing finding — confirmed, and it is the more valuable half

| # | My mutation | Result |
|---|---|---|
| Q2 | Deleted `await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)` from `SagaIntegrationTestSupport.WaitUntilReachableAsync` (`:164`) | `WithTheWait_…` **FAILS** — `System.TimeoutException : '…' never became reachable.` in **204 ms**, against a subscriber 300 ms away. |

That is the report's claim reproduced exactly: 100 attempts nominally budgeted at 200 ms each burn out in a fifth of a second, because `NatsNoRespondersException` is the server's immediate sentinel and does not consume the request timeout. With the pacing line restored, Q1b shows the loop's real patience is **≈5 s** — so the loop now genuinely waits, and the nominal budget and the actual budget agree to within a factor of one rather than a factor of a hundred. Row R2 of the implementer's arming table is confirmed independently.

### Q3. The disclosure of the same latent shape elsewhere — verified as a search result, and it is **incomplete**

The claim is that the unpaced shape also exists in four other files, all "protected only by accident". I enumerated rather than re-read. Two commands, complete output classified below:

```
$ grep -rn --include=*.cs "static .*WaitUntil" tests/
tests/Fulfillment.IntegrationTests/FulfillmentHostFixture.cs:62
tests/Billing.IntegrationTests/BillingHostFixture.cs:77
tests/Orders.IntegrationTests/OrdersCreateAcceptanceTests.cs:649
tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs:110,138
tests/Orders.IntegrationTests/CatalogReferenceListAcceptanceTests.cs:351
$ grep -rn --include=*.cs -e "NatsNoRespondersException" -e "NatsNoReplyException" tests/
  … 31 hits; the two additional retry loops among them are
tests/Orders.IntegrationTests/StandInSagaResponders.cs:114 (StandInRpcResponder.WaitUntilSubscribedAsync)
tests/Orders.IntegrationTests/StandInFulfillmentStockCheckResponder.cs:194
```

| Readiness loop | Paced? | In the disclosure? |
|---|---|---|
| `SagaIntegrationTestSupport.WaitUntilReachableAsync:138` (50 ms paced) | **yes** (50 ms) | it *is* the fix |
| `BillingHostFixture.cs:77` | no | yes |
| `FulfillmentHostFixture.cs:62` | no | yes |
| `OrdersCreateAcceptanceTests.cs:649` | no | yes |
| `StandInSagaResponders.cs:114` (`StandInRpcResponder.WaitUntilSubscribedAsync`) | no | yes |
| **`CatalogReferenceListAcceptanceTests.cs:351`** | **no** | **NO** |
| **`StandInFulfillmentStockCheckResponder.cs:194`** | **no** | **NO** |

Six unpaced loops, not four — see **B2**. Both misses are in `tests/Orders.IntegrationTests/`, the very project this round edited, and both were **excluded by hand** from round 3's own grep on a *readiness* ground ("responder side, already blocks on its own probe"; "different feature, has its own established wait") which is true and orthogonal to the *pacing* question the disclosure was making. The enumeration was run; the exclusion list was the prose.

### Q4. Did fix round 3 touch `src/`? — No, verified two ways

- **Mtimes.** The newest file under `src/` is `SagaCommandPayloads.cs` at **20:49:40** (fix round 2's D2 edit); fix round 3's artefacts are at **21:23:07** and **21:26:51**. No `src/` file was written after 20:49.
- **Diffstat.** `git diff --stat src/` reports **16 files, 616 insertions, 70 deletions**, against round 1's recorded **615 insertions** — exactly the one line D2 added (`long AvailableCreditAfter,`) and nothing else. The untracked `src/` set is unchanged.

## Findings (none blocking)

### B1 — the D2 guard cannot fail on the D2 defect, and I am the reason it is shaped that way

**File:** `tests/Orders.UnitTests/SagaCommandPayloadTests.cs:186-207`, against `src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs:137-143`.

Orders' `BC23_TheRetypedKeyListsAgreeWithTheKeySetsParsedFromAsyncApi` compares a **hand-retyped key list** with the **spec**. For the other ten schemas that is one half of a pair: the matching `AssertKeys` round-trip test (`:28-143`) serialises the real record and compares its keys to the same list, so record drift fails. **The two new credit-release schemas have the BC23 half and no `AssertKeys` half**, so no test in Orders reads these two records at all.

**My probe.** Removed `AvailableCreditAfter` from Orders' `CreditReleaseReplyPayload` and adjusted the two named-argument call sites — i.e. reconstructed the exact defect D2 named — then forced a rebuild: `Passed! - Failed: 0, Passed: 342`. Restored, `cmp` byte-identical on all three files, green again.

Billing's copy of the same schema is guarded properly: `tests/Billing.UnitTests/CreditRpcPayloadTests.cs:29-35` reflects over `payloadType.GetProperties()` and compares to the spec, so the identical defect in Billing fails immediately. Orders has the weaker form for these two schemas only.

**Why this is a finding and not a rejection.** The shipped code is correct and spec-conformant; nobody claimed a guard they do not have; the field has no consumer in Orders today (extra or missing keys are invisible to `System.Text.Json` on the read side); and round 1's own "to clear D2" list asked for exactly the three things that were delivered. The gap is mine as much as the implementer's. **Recommend a backlog entry**: add the two `AssertKeys` cases in `SagaCommandPayloadTests`, or — better, and cheaper to keep true — port Billing's reflection-over-the-record form into Orders' file so every present and future saga payload is covered by construction. Whoever takes it should arm it by deleting the property, which the probe above shows is currently green.

### B2 — the unpaced-loop disclosure names four files; there are six

Per Q3. `tests/Orders.IntegrationTests/CatalogReferenceListAcceptanceTests.cs:351` and `tests/Orders.IntegrationTests/StandInFulfillmentStockCheckResponder.cs:194` carry the identical unpaced 100-attempt loop and are absent from the disclosure. Nothing is broken today — all six work because the real subscribe-to-probe window is microseconds. **Backlog id 63 should be widened from four files to six**; the leader owns that entry.

### B3 — the fix's own call site is unguarded, and inherently so

Deleting `await WaitUntilOrdersResponderReachableAsync(...)` from `SagaIntegrationTestSupport.StartHostAsync` (`:86-90`) leaves the suite green, because the race it closes is probabilistic — that is the whole reason it survived twenty-odd features. `OrdersCancelResponderReadinessRaceTests` arms the **loop**, not its **use**. I record this rather than ask for it: a deterministic guard on the call site would require injecting a delay into the production responder's own subscription, which is a worse trade than the residual risk. Anyone deleting that call should know only a flake will tell them.

### B4 — #7 solved this exact race, with a cheaper primitive, and said so in a comment

`apps/orders/src/infrastructure/messaging/test-support/stub-stock-check-responder.ts:49-58` in the #7 checkout: *"`async` and `await`s a `connection.flush()` (a PING/PONG round trip) before returning — otherwise a caller that sends its request immediately after starting this responder can race ahead of the SUB frame reaching the server, and NATS answers 'no responders'."* #7 closed the stub side with a one-line flush and did **not** close the app side (`saga-integration-harness.ts:413` starts the microservice and then blocks only on **Kafka** consumer-group readiness). So #7 knew the mechanism, fixed half of it, and its saga harness carries the other half unclosed to this day — with the mitigating detail that NestJS's `startAllMicroservices()` at least issues the SUB before returning, whereas .NET's `BackgroundService.StartAsync` returns before `SubscribeAsync` is even called. **#8's exposure is strictly larger, and its fix is strictly more expensive** (six hand-built probe loops where nats.js offered `flush()`). This is a ported-idiom observation in everything but name, and id 25's brief should carry it: the Gateway will open NATS clients to six subjects, and `NatsConnection.PingAsync()` is the .NET analogue of `flush()` where the connection is reachable.

## CHECKPOINTS.md — boxes walked, round 2

**C1 — harness complete**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer.
- [x] Every agent definition declares its model.
- [x] `./init.sh` exits 0 — leader-verified before dispatch, and re-run by me after this round's `feature_list.json` transition.

**C2 — state coherent**
- [x] At most one feature `in_progress` — zero while id 41 sat `in_review`; this review moves it to `done`, so still zero.
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it.
- [x] `progress/current.md` describes the active session (leader's file; still names id 41).
- [x] Every `blocked` feature records why. (None blocked.)

**C3 — architecture respected**
- [x] No forbidden framework reference inside any `Domain/` folder — **`tests/Architecture.Tests` run by me, 16/16 passed**. This feature's `src/Orders/Domain` diff remains empty.
- [x] No cross-service DB access — `billing.credit.release` is NATS RPC carrying business identifiers.
- [x] No shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs`.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` (architecture suite).
- [x] `src/SharedKernel` still has zero `PackageReference` (architecture suite).
- [x] No `decimal` in domain arithmetic — the one `src/` change this round is a `long` field.
- [x] Every interaction correctly classified as Kafka-fact or NATS-RPC.
- [x] No stray debug logging, no context-free TODOs.

**C4 — verification real**
- [x] **`./quality.sh` passes** — leader-verified twice with nothing concurrent, exit 0, **1284 passed / 0 failed / 16 projects**. I ran the specific suites under test instead of duplicating it, and every one of my mutations was followed by a forced-rebuild green run.
- [x] Domain tests are pure.
- [x] Integration tests use Testcontainers against real MsSql/Kafka/NATS — I ran nine of them, twice, including under mutation.
- [ ] **Coverage thresholds** — not independently verified. `A7` (the inert coverage gate) has been open since phase 8 and is unchanged by this feature; this box stays empty for the same reason it did at the last five closes.
- [x] No Jest anywhere; xUnit is the backend runner.

**C5 — session close**
- [x] No suspicious untracked files — 58 entries, all `*.cs` or `*.md` belonging to features 40 and 41, plus `src/Orders/Application/Queries/` (feature 40). No build output, no `*.tmp`.
- [x] **`progress/history.md` entry with effort record** — appended by this review.
- [x] `feature_list.json` reflects true state — id 41 set `done` by this review, one line, `git diff` read to confirm nothing else moved.
- [x] The human will be told what was done and how to test it (leader's report).
- [x] Claude did not commit.

**C6 — SDD**: not applicable, `sdd: false`.

**C7 — reuse fidelity**
- [x] **`specs/shared/` byte-identical to #7's** except `test-matrix.md` — verified in round 1 with `diff -rq`; the leader re-verified untouched before dispatching this round.
- [x] No silent fork; `SA-2` remains an explicit, human-gated amendment in flight.
- [x] The `R<n>` ids reused (R24, R25, R27, R28, SO2, SO3, SO7) name the same behaviours here.
- [ ] `n8n/workflows/*.json` against the .NET Gateway — not applicable yet; the Gateway is id 25.
- [ ] Black-box API script parity — same reason.
- [x] `progress/history.md` effort records complete and honest — this feature's is appended below, including the two rounds that were #8's own misses.
- [ ] README benchmark section — pending, phase close.

## Traceability — the two rows that were missing in round 1

| Claim | Test verified to exist **and to bite** |
|---|---|
| `asyncapi.yaml` `CreditReleaseRequestPayload`/`CreditReleaseReplyPayload` wire shape | `SagaCommandPayloadTests.BC23_…` rows 11 and 12 (spec-side only — see **B1**) |
| `stock.release` reason selection, `credit.rejected.v1` → `credit_rejected` | `SagaFactHandlerTests.CreditRejectedV1_StockReservedVariant_EnqueuesStockReleaseWithReasonCreditRejected_ThroughTheFactory`; `SagaCompensationCreditRejectedTests.R27_R28_SO6_SO7_…:83` — both armed by me |
| `stock.release` reason selection, `credit.released.v1` → `order_cancelled` | `SagaFactHandlerTests.CreditReleasedV1_CreditApprovedOrConfirmedVariant_EnqueuesStockReleaseWithReasonOrderCancelled_ThroughTheFactory` (×2 statuses) — armed by me |
| The saga harness returns only once `orders.cancel` is live | `OrdersCancelResponderReadinessRaceTests.WithTheWait_ADelayedSubscriberNoLongerLosesTheRace` — armed by me in two directions (Q1b, Q2). The **call site** is unguarded by design — **B3** |

Acceptance bullet 4 remains unmet pending the human's `SA-2` ruling, exactly as in round 1: `specs/shared/openapi.yaml:1344-1347` promises the operator note reaches the timeline and `asyncapi.yaml`'s `OrderCancelledPayload` has no field to carry it, identically in #7. Verified honest by enumeration in round 1; **not a rejection basis, and not re-litigated here.**

## Phase 13 is not closed

Ids **25** (`gateway_rest_auth`), **26** (`gateway_sse_push`), **56** (`composition_root_env_reads_are_unguarded`), **60** (`envelope_fixture_collisions_defeat_provenance_assertions`), **61** (`catalog_ordering_claim_guarded_for_products_only`) and **63** (the unpaced readiness loops — which **B2** says should be widened from four files to six) all remain open.
