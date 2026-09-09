# impl_orders_cancel_responder

Feature id 41, phase 13, `sdd: false`. Status set to `in_review` as the final step.

## What was built

`orders.cancel`, a NATS RPC responder in Orders for operator-initiated
cancellation — the third subject on the existing `OrdersCreateResponder`
(matching feature `orders_catalog_responder`'s own precedent of extending
rather than forking a second `BackgroundService`). Unlike #7, which built
three of the four `saga.md` §4.3 branches and explicitly declined to build the
fourth because `billing.credit.release` did not exist anywhere in its stack,
**#8 builds all four branches in one pass** — `billing.credit.release`
already exists (`specs/shared/asyncapi.yaml:439-456`, implemented in
`src/Billing/Presentation/BillingRpcResponder.cs`'s `CreditSubjects.CreditRelease`
responder). This is a measurable reuse dividend: #7 needed a leader-authored
spec amendment plus a follow-up implementation pass to reach parity; #8
reached the same four-branch coverage in a single implementation pass because
it inherited that capability already built.

| Starting status | Behaviour |
|---|---|
| `placed` | Cancels immediately, reason `operator_cancelled`, via `Order.Cancel` — the same domain method, no bypass, no new domain modeling (bullet 2). |
| `stock_reserved` | Enqueues `stock.release` (reason `order_cancelled`) through the existing durable `ISagaCommandStore` + fast-path signal mechanism. Order stays `stock_reserved`; the EXISTING `stock.released.v1` step (already reason-parametric — `SagaStepTable.MapReason` already mapped `order_cancelled` before this feature touched anything) completes the cancellation with zero changes to that one variant. |
| `credit_approved` / `confirmed` | Enqueues `credit.release` (Billing's new-to-#8-but-already-shipped responder). Order stays where it is; when `credit.released.v1` arrives, the EXTENDED `SagaStepTable` (new variant, precondition `credit_approved`/`confirmed`) owes `stock.release` next; when the resulting `stock.released.v1` arrives, ANOTHER extended variant cancels with BOTH compensation steps in causal order (`credit_released` then `stock_released`) — the reverse-order-of-acquisition chain `saga.md` §4.3 requires. |
| `despatched` / `invoiced` / `paid` / `completed` / already-`cancelled` | Rejected with `ORDER_NOT_CANCELLABLE` (a domain error, never a 503) — `Order.Cancel`'s own `OrderNotCancellableError`, reused verbatim, mapped to the wire-documented code. |

### Files touched

**Application**
- `src/Orders/Application/Commands/CancelOrderCommand.cs` (new) — `CancelOrderCommand`/`CancelOrderResult`.
- `src/Orders/Application/Commands/CancelOrderErrors.cs` (new) — `CancelOrderError`/`OrderNotFoundError`.
- `src/Orders/Application/Commands/CancelOrderCommandHandler.cs` (new) — the four-branch handler.
- `src/Orders/Application/Sagas/SagaCommandKind.cs` — sixth member `CreditRelease` + token.
- `src/Orders/Application/Sagas/SagaCommandRequestFactory.cs` — `CreditRelease` case added; `StockRelease`'s generic case now throws (its reason is contextual), replaced for fact-driven callers by `BuildStockReleaseJson(order, triggeringFactEventType)` + `StockReleaseReasonFor` (derives `credit_rejected` vs `order_cancelled` from the triggering fact's own `eventType`).
- `src/Orders/Application/Sagas/SagaStepTable.cs` — `credit.released.v1` and `stock.released.v1` widened from one `SagaStep` to `IReadOnlyList<SagaStep>` (two new variants each); `For` (kept, now throws `InvalidOperationException` on the two now-ambiguous lookups), `Variants`, `ForStatus` (new); `CompensationStepsFromCreditThenStockRelease` (new, synthesises the `credit_released` step ahead of the observed `stock_released` one).
- `src/Orders/Application/Sagas/SagaFactHandler.cs` — precondition resolution generalised from a single equality check to `SagaStepTable.ForStatus`; the `StockRelease` payload build special-cased to the reason-aware factory method.
- `src/Orders/Application/Sagas/SagaDispatchEvents.cs` — `CreditReleasedForCancellationRecorded` (new).
- `src/Orders/Application/Sagas/OrderSagas.cs` — `CreditReleasedForCancellationRecordedHandler` (new, sixth class), signals `StockRelease`.
- `src/Orders/Application/Commands/SagaFactCommandHandlers.cs` — `HandleCreditReleasedFactCommandHandler` made conditional (was a plain delegation; `credit.released.v1` can now own a command).
- `src/Orders/Application/Ports/ISagaCommands.cs` — `ReleaseCreditAsync` added.

**Infrastructure**
- `src/Orders/Infrastructure/Messaging/Rpc/RpcSubjects.cs` — `OrdersCancel = "orders.cancel"`, `CreditRelease = "billing.credit.release"`.
- `src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs` — `CreditReleaseRequestPayload`/`CreditReleaseReplyPayload`.
- `src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs` — `ReleaseCreditAsync`.
- `src/Orders/Infrastructure/Saga/SagaCommandDispatcher.cs` — `CreditRelease` case in `InvokeAsync`.

**Presentation**
- `src/Orders/Presentation/Rpc/OrdersCancelPayloads.cs` (new) — `OrdersCancelRequestPayload`/`OrdersCancelReplyPayload`.
- `src/Orders/Presentation/Rpc/OrdersCancelRequestValidator.cs` (new) — `InvalidOrdersCancelRequestError` + validator (`orderId` required, `reason` must equal the wire's `operator_cancelled` const).
- `src/Orders/Presentation/Rpc/OrdersCreateErrorMapper.cs` — three cases added: `InvalidOrdersCancelRequestError` → `VALIDATION_FAILED`, `OrderNotFoundError` → `NOT_FOUND`, `OrderNotCancellableError` → `ORDER_NOT_CANCELLABLE` (placed before the generic `DomainError` case, since `OrderNotCancellableError` derives from it).
- `src/Orders/Presentation/OrdersCreateResponder.cs` — third subscribe loop + `HandleOrdersCancelAsync` + `ToReplyPayload(CancelOrderResult)`.

**Tests**
- `tests/Orders.UnitTests/CancelOrderCommandHandlerTests.cs` (new) — all four branches, 11 tests.
- `tests/Orders.UnitTests/OrdersCancelRequestValidatorTests.cs` (new).
- `tests/Orders.UnitTests/OrdersCancelPayloadTests.cs` (new) — wire round-trip + BC23 key-set parity + G5 arming.
- `tests/Orders.UnitTests/OrdersCreateErrorMapperTests.cs` — three cases added.
- `tests/Orders.UnitTests/SagaStepTableTests.cs` — restructured: the generic 90-case theory now excludes the two multi-variant fact types (mirrors #7's own `MULTI_VARIANT_FACT_TYPES` exclusion), with dedicated blocks added for both.
- `tests/Orders.UnitTests/SagaFactCommandHandlerTests.cs` — two cases added (`credit.released.v1`'s `paid` variant still owes nothing; its two new variants publish the new dispatch event).
- `tests/Orders.UnitTests/OrderSagasTests.cs` — sixth mapping added.
- `tests/Orders.UnitTests/RpcSubjectsTests.cs` / `SagaRpcSubjectsTests.cs` — `orders.cancel` / `creditRelease` subject checks.
- `tests/Orders.UnitTests/NatsSagaCommandsAdapterTests.cs` — `CreditRelease` added to the two exhaustive theories.
- `tests/Orders.UnitTests/SagaCommandDispatcherTests.cs` — `CreditRelease` dispatch-mapping test.
- `tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs` (new) — real NATS + real MS-SQL + real Kafka, 5 tests: `placed` immediate, `stock_reserved` async completion, `credit_approved`/`confirmed` reverse-order-of-acquisition proof, terminal rejection, unknown order id.
- `tests/Orders.IntegrationTests/StandInSagaResponders.cs` — `StartCreditReleaseAsync` (sixth stand-in).

## Traceability

`sdd: false` — no `requirements.md`, no `R<n>` set of its own, matching the
other `sdd: false` features in this backlog; `specs/shared/test-matrix.md`
gets no new row. Every test is named for the acceptance bullet or design
point it proves.

**Bullet 1** — "POST /orders/{id}/cancel succeeds through the Gateway for a
cancellable order": the Gateway (id 25) does not exist yet (`src/Gateway/`
confirmed to still be placeholder files before writing any code, matching the
brief's own framing and `orders_catalog_responder`'s precedent). Proven at
the NATS boundary instead — `OrdersCancelAcceptanceTests`, real NATS, real
MS-SQL, real Kafka, the real responder resolved through the same
`AddOrdersOutbox` + `AddOrdersAcceptance` + `AddOrdersSaga` + `AddDispatcher`
composition the production host uses.

**Bullet 2** — "reuses the Order aggregate's existing cancelled state and
cancellationReason invariant — no new domain modeling": confirmed by reading
before writing any code — `Order.Cancel`, `CancellationReason.OperatorCancelled`
(already legal from ANY source per `IsReasonApplicable`), and
`CompensationStepKind.CreditReleased` (already defined) all pre-existed this
feature untouched. `src/Orders/Domain/` has zero diff in this feature — `git
diff --stat src/Orders/Domain` is empty, confirmed before writing this line.
Guarded by `CancelOrderCommandHandlerTests.Placed_CancelsImmediately_...` and
`Terminal_ThrowsOrderNotCancellableAndLeavesTheOrderUntouched` (exhaustive
over all five statuses `Order.Cancel` itself refuses, including an
already-`cancelled` order).

**Bullet 3** — "an order in a terminal state (invoiced, completed, already
cancelled) is rejected with a domain error, not a 503": guarded by the same
exhaustive theory above (asserts `OrderNotCancellableError`, never a silent
success) plus `OrdersCreateErrorMapperTests.Map_AnOrderNotCancellableError_...`
(asserts the wire code is `ORDER_NOT_CANCELLABLE`, explicitly not
`VALIDATION_FAILED` and not `UNAVAILABLE`) plus
`OrdersCancelAcceptanceTests.Terminal_RepliesOrderNotCancellableNot503_...`
(real wire, real `RpcError` body). Armed in both directions — see the arming
table below, rows A and B.

**Bullet 4** — "the operator note from CancelOrderRequest lands on the
read-model timeline": **not built, disclosed rather than silently dropped.**
`OrderCancelledPayload` — the only fact the Projector ever builds an
`order.cancelled` timeline entry from — has no `note`/`operatorNote` field on
the wire at all (`specs/shared/asyncapi.yaml:2603-2624`, checked line by
line, read-only per this feature's own scope). `#7 found the identical gap on
the identical spec section, independently, for the identical reason` — see
"What #7 did, and #8's identical gap" below. `CancelOrderCommand.Note` is
carried onto the command (the wire declares it, so it is read) but goes no
further — it is not silently swallowed; `CancelOrderCommandHandler`'s own XML
doc `<remarks>` states plainly that carrying it further would look like
progress toward the criterion without being any. No test claims this bullet
is met.

## What #7 did, and where #8 is ahead (from #7's checkout, cited)

Read from `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs/progress/impl_orders_cancel_responder.md`:

- #7's **original pass** built three of four branches (`placed`, `stock_reserved`,
  terminal rejection) and explicitly declined the `credit_approved`/`confirmed`
  branch: `billing.credit.release` existed at none of the three layers that
  would need it (`specs/shared/asyncapi.yaml`, `packages/contracts`,
  `apps/billing/src/presentation/credit.controller.ts`) — its own words:
  "I chose neither [fabricate a contract nor hand-roll an unauthorised
  shape] ... This needs a follow-up feature."
- The leader then amended `specs/shared/asyncapi.yaml` (the `creditRelease`/
  `creditReleaseReply` channels, `CreditReleaseRequestPayload`/
  `CreditReleaseReplyPayload`), and #7 ran a **second, follow-up pass** to
  close the gap: a Billing responder, and Orders' completion of the fourth
  branch — including the SAME two structural extensions #8 needed here
  (`SAGA_STEPS`'s value type widened for two multi-variant fact types, a
  sixth dispatch-owed event `CreditReleasedForCancellationRecorded` — #8's
  own class, independently named identically, confirmed by opening #7's
  `apps/orders/src/application/events/saga-dispatch.events.ts:61` AFTER
  already having written the C#, not before).
- **#8 did not need a follow-up pass.** `billing.credit.release` was already
  fully implemented before this feature started (confirmed by reading
  `src/Billing/Presentation/BillingRpcResponder.cs` and
  `CreditSubjects.CreditRelease` before writing any Orders code) — the spec
  amendment #7 needed a human gate for is already merged into #8's copy of
  `specs/shared/`. All four branches shipped in one pass, one session.

## The disclosed race — #8's equivalent, found and confirmed present, not fixed

#7's report discloses, in detail, a live-reproduced race between an
operator's cancel decision and the saga's own automatic forward progress —
narrower for the `stock_reserved` branch (strands one Fulfillment
reservation), materially worse for the `credit_approved`/`confirmed` branch
(can strand the entire order past `despatched` with a permanently
un-resolvable `saga_commands` row). **The identical class of race exists in
#8's architecture, by construction, and is disclosed here — not fixed, per
the same reasoning #7 gave.**

The mechanism is the same because the defence is the same one #7 names:
`SagaFactHandler`'s R25 precondition check (now `SagaStepTable.ForStatus`)
is an equality-only guard, correct and load-bearing for the fact-driven path,
but `CancelOrderCommandHandler` reads the order's status and enqueues a
compensating command inside ONE transaction, while the saga's OWN forward
progress (e.g. `credit.approved.v1` arriving and advancing `stock_reserved`
→ `credit_approved` → `confirmed`) runs in an INDEPENDENT transaction on an
INDEPENDENT consumer. Nothing serialises the two. If the order advances PAST
the status this handler observed before the compensating fact
(`stock.released.v1` or `credit.released.v1`) arrives, R25's
precondition-unmet check — correctly, by design — ignores that fact, which
can strand a genuinely-released resource on an order that keeps progressing.

This was not merely inferred from #7's report — it was **confirmed present
in #8's own integration harness while writing
`OrdersCancelAcceptanceTests.CreditApprovedOrConfirmed_...`**: the test
deliberately isolates the race by never starting a `despatch.create`
stand-in responder at all (the same isolation technique #7's own suite uses,
`startBillingApprovedOnlyResponder`/`startFulfillmentOnlyResponder`), which
would otherwise let the saga's own fast path race the operator's cancel
decision to `despatched` before the compensating facts land — exactly #7's
own disclosed failure mode. Documented here, not fixed: closing it needs
either a transactional re-check immediately before a forward-progress
command's own fast-path dispatch, or `CancelOrderCommandHandler` actively
superseding an already-owed forward command — both genuine saga-design
decisions outside this feature's bounded scope, matching #7's own
disposition of the identical finding.

## The ported-idiom ledger

Per CLAUDE.md's ledger rule (re-read fresh from disk before writing this,
amended twice in Phase 13): every row's "#7 relied on X" half is a claim
about #7's SOURCE, read from `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs`
with file and line, never inferred from what the framework would plausibly
have done. Every row's guard is a countable claim, armed below.

| #7 relied on | In #8 that property is supplied by | Guard |
|---|---|---|
| **Reason-parametric `stock.release`.** `apps/orders/src/application/saga-command-payloads.ts:60-75` — `stockReleaseReasonFor(fact: Envelope)`, switching on `fact.eventType`: `credit.rejected.v1` → `credit_rejected`; `credit.released.v1` → validates the fact's OWN payload `reason` field equals `order_cancelled` (throwing otherwise) and returns `order_cancelled`. Called at line 103 inside `buildSagaCommandPayload`'s `'stock.release'` case. #7's OWN direct-enqueue callers (`cancel-order.handler.ts`'s `beginStockReleaseCompensation`/`beginCreditReleaseCompensation`) build their payload literals inline instead, never through this function — the split #8 mirrors. | `SagaCommandRequestFactory.StockReleaseReasonFor(string triggeringFactEventType)` + `BuildStockReleaseJson(Order, string)` (`src/Orders/Application/Sagas/SagaCommandRequestFactory.cs`), called from `SagaFactHandler.HandleAsync`. Simplified relative to #7: keys off `fact.EventType` alone, does not additionally validate the fact's own payload `reason` field (a deliberate divergence — #8's two callers of `stock.release` are exhaustively known by `eventType` alone, so the extra defensive check would duplicate, not add, safety). `CancelOrderCommandHandler`'s own two direct enqueues build `StockReleaseRequestPayload`/`CreditReleaseRequestPayload` inline, never through this factory — the same split #7 established. | `CancelOrderCommandHandlerTests.StockReserved_EnqueuesStockReleaseWithReasonOrderCancelled_...` (deserialises the enqueued payload, asserts `Reason == "order_cancelled"`) — **armed, row C.** |
| **Multi-variant step-table lookup.** `apps/orders/src/application/saga-steps.ts:126` (`type SagaStepEntry = SagaStep \| readonly SagaStep[]`), `:129` (`SAGA_STEPS`'s value type widened to it), `:276-291` (`stepVariantsFor`, `stepForStatus`), `:304-315` (`stepFor`, kept for every single-variant caller, now throwing `saga-steps: stepFor("${eventType}") is ambiguous ... use stepForStatus` for the two multi-variant ones). | `SagaStepTable`'s `_rows` dictionary value type widened from `SagaStep` to `IReadOnlyList<SagaStep>` (`src/Orders/Application/Sagas/SagaStepTable.cs`); `For` kept, now throws `InvalidOperationException` with near-identical wording for the same two fact types; `Variants`/`ForStatus` added. Same three-way split, same throw-on-ambiguity discipline, independently arrived at (the naming similarity was noticed only when opening #7's file to write this row, not planned). | `SagaStepTableTests.For_ThrowsForBothMultiVariantEventTypes_RatherThanSilentlyPickingOne`, `..._ExposesThreeVariants_...` (×2) — **armed, rows E and F.** |
| **The dispatch-owed event for a fact whose owed command is now conditional.** `apps/orders/src/application/events/saga-dispatch.events.ts:61` (`class CreditReleasedForCancellationRecorded implements IEvent`), `apps/orders/src/application/commands/saga-fact.handlers.ts:185-193` (`HandleCreditReleasedFactHandler` — publishes it ONLY when `result.enqueued` is set), `apps/orders/src/application/sagas/order.sagas.ts:27,120` (sixth `ofType` branch, mapping to `IssueStockReleaseCommand`). #7's own report narrates finding this gap THE HARD WAY, live: "Building the step-table entry ... was not enough ... `stock.release` was never issued at all ... invisible to a 45-second test `waitFor`." | `CreditReleasedForCancellationRecorded` record (`SagaDispatchEvents.cs`, same name — independently converged, confirmed by opening #7's file only after this C# was already written); `HandleCreditReleasedFactCommandHandler` made conditional (`SagaFactCommandHandlers.cs`, mirrors `HandleCreditRejectedFactCommandHandler`'s exact shape); `CreditReleasedForCancellationRecordedHandler` (`OrderSagas.cs`, sixth class, signals `StockRelease`). #8 built the fast-path wiring FROM THE START (this ledger row and its guard were written before any integration test ran), so #8 did not independently rediscover the gap the hard way — inheriting #7's disclosed lesson was the point. | `SagaFactCommandHandlerTests.CreditReleasedV1_CreditApprovedOrConfirmedVariant_PublishesCreditReleasedForCancellationRecorded` (×2 statuses) and `OrderSagasTests.SO3_...`'s sixth mapping — **armed, row F** (the `SagaStepTable` deletion that also empties this path) and independently exercised by `OrderSagasTests`' own unmodified assertion shape. |

## Arming table

Every mutation below: introduced on the real file, `dotnet build --no-incremental`
forced before the confirming run, the named test(s) run and confirmed FAILING
with the verbatim message below, restored from a `cp` backup (never `git
checkout --`), restore verified `cmp` byte-identical, `dotnet build
--no-incremental` forced again, confirming green run.

| # | File mutated | Mutation | Named test(s) | FAIL message (verbatim) | Restored + green |
|---|---|---|---|---|---|
| A | `CancelOrderCommandHandler.cs` | `order.Cancel(...)` and `orders.SaveChangesAsync(...)` deleted from the `placed`/terminal (`default`) branch. | `Placed_CancelsImmediately_ReasonOperatorCancelledAndNoCompensationPlanned`; `Terminal_ThrowsOrderNotCancellableAndLeavesTheOrderUntouched` (×5 statuses) | `Assert.Equal() Failure: Values differ` `Expected: Cancelled` `Actual:   Placed` — and, for every terminal status, `Assert.Throws() Failure: No exception was thrown` `Expected: typeof(...OrderNotCancellableError)`. The interesting failure mode: with the call deleted, a TERMINAL order silently "succeeds" at its unchanged status rather than throwing — the exact shape #7's own arming table found for the identical class of guard. | Yes |
| B | `OrdersCreateErrorMapper.cs` | The `OrderNotCancellableError` → `ORDER_NOT_CANCELLABLE` case deleted; falls through to the generic `DomainError` case. | `OrdersCreateErrorMapperTests.Map_AnOrderNotCancellableError_MapsToOrderNotCancellableNotValidationFailedNotUnavailable` | `Assert.Equal() Failure: Strings differ` `Expected: "ORDER_NOT_CANCELLABLE"` `Actual:   "VALIDATION_FAILED"` | Yes |
| C | `CancelOrderCommandHandler.cs` | `stock.release`'s reason payload corrupted: `"order_cancelled"` → `"credit_rejected"` (R27's UNRELATED reason). | `CancelOrderCommandHandlerTests.StockReserved_EnqueuesStockReleaseWithReasonOrderCancelled_StatusUnchangedAndOnlyStockReleasePlanned` | `Assert.Equal() Failure: Strings differ` `Expected: "order_cancelled"` `Actual:   "credit_rejected"` | Yes |
| D | `CancelOrderCommandHandler.cs` | The `credit.release` enqueue call deleted from the `credit_approved`/`confirmed` branch. | `CancelOrderCommandHandlerTests.CreditApprovedOrConfirmed_EnqueuesCreditReleaseOnly_...` (×2 statuses) | `Assert.Single() Failure: The collection was empty` | Yes |
| E | `SagaStepTable.cs` | `stock.released.v1`'s two new variants deleted, reverted to the single `stock_reserved` variant. | `StockReleasedV1_ExposesThreeVariants_...`; `StockReleasedV1_CreditApprovedOrConfirmedVariant_CancelsWithBothCompensationStepsInCausalOrder` (×2 statuses) | `Assert.Equal() Failure: Values differ` `Expected: 3` `Actual: 1` — and `System.NullReferenceException : Object reference not set to an instance of an object.` (the theory casts `SagaStepTable.ForStatus(...)`'s now-`null` result). | Yes |
| F | `SagaStepTable.cs` | `credit.released.v1`'s two new variants deleted, reverted to the single `paid` variant (applied together with row E). | `CreditReleasedV1_ExposesThreeVariants_...`; `CreditReleasedV1_CreditApprovedOrConfirmedVariant_LeavesStatusUntouchedAndOwesStockRelease` (×2); `For_ThrowsForBothMultiVariantEventTypes_RatherThanSilentlyPickingOne`; `SagaFactCommandHandlerTests.CreditReleasedV1_CreditApprovedOrConfirmedVariant_PublishesCreditReleasedForCancellationRecorded` (×2) | `Assert.Equal() Failure: Values differ` `Expected: 3` `Actual: 1`; `NullReferenceException`; `Assert.Throws() Failure: No exception was thrown` `Expected: typeof(System.InvalidOperationException)`; `Assert.Single() Failure: The collection was empty`. 13 test cases failed across rows B–F in one combined run, all and only the expected ones (324 of the suite's other 337 remained green, confirming isolation). | Yes |
| G | `SagaStepTable.cs` | `CompensationStepsFromCreditThenStockRelease`'s causal order swapped: `stock_released` first, `credit_released` second. | `StockReleasedV1_CreditApprovedOrConfirmedVariant_CancelsWithBothCompensationStepsInCausalOrder` (×2 statuses) | `Assert.Equal() Failure: Values differ` `Expected: CreditReleased` `Actual:   StockReleased` | Yes |

Rows E, F applied together in one build/test cycle (independent statements
in the same file, verified to produce exactly the union of their individual
expected failures — 13 of 13, none extra, none missing); row G applied and
verified separately afterward since it targets the same method rows E/F's
mutation makes structurally unreachable.

**Interference note, disclosed for the record:** the first full-solution
`./quality.sh` run of this session was invalidated by my own concurrent
`dotnet build src/Orders --no-incremental` calls (part of arming row A)
racing the solution-wide `dotnet test`'s own build of `Seed`/`Projector`/
`Architecture.Tests` (all reference `Orders`) and, later in the same run,
racing `Orders.IntegrationTests` itself (one spurious
`NatsNoRespondersException` on `UnknownOrderId_RepliesNotFound`, a test that
had already passed cleanly, twice, in isolation before that point). That log
is not used as evidence anywhere in this report. All arming above and the
final `./quality.sh` run below were executed with no other build activity
running concurrently.

## Verification

- `Orders.UnitTests`: **337 passed, 0 failed, 0 skipped** (was 307 before
  this feature; +30 net, read off two `dotnet test` runs of this project
  alone — no further breakdown by file is claimed here, since the
  `SagaStepTableTests` restructuring both removed cases, from the excluded
  multi-variant fact types, and added others, and I did not separately count
  each file's own delta).
- `Orders.IntegrationTests`: **89 passed, 0 failed, 0 skipped** (was 84; +5,
  `OrdersCancelAcceptanceTests`), run twice for stability, both green.
- `./quality.sh` (full solution, 16 projects, run with no concurrent build
  activity): **exit 0** — `dotnet format --verify-no-changes` clean,
  `dotnet build` 0 warnings/0 errors, `dotnet test` **1276 passed, 0 failed,
  0 skipped** across all 16 projects (counted from this run's own "Passed!"
  lines: 50+23+21+65+337+226+119+34+87+16+6+12+52+56+83+89 = 1276; baseline
  from the brief was 1241, so +35 net, matching exactly Orders.UnitTests'
  +30 and Orders.IntegrationTests' +5). `Architecture.Tests`: 16/16,
  unchanged — no domain-purity or layering violation introduced.
- `./init.sh`: **exit 0.** Backlog coherence, session-file lockstep,
  superseded-rules check, backlog tripwire, commit-msg hook all `[OK]`; "no
  feature in_progress" confirms the status flip landed correctly; "40/60
  features done" (up from 39, feature 40 having been closed by a reviewer
  during this session, not by me). The two `[WARN]` lines (uncommitted
  changes, "run quality.sh before closing" — already run, above) are
  expected mid-session per `init.sh`'s own wording.
- `feature_list.json`: `git diff feature_list.json` shows my ONE line (id 41
  `"in_progress"` → `"in_review"`) plus THREE pre-existing hunks not
  authored by me, all left byte-for-byte untouched: id 40
  (`catalog_reference_list`, `"pending"` → `"done"` — closed by a reviewer
  before or during this session, not by me; I never touched that entry), id
  56 (an acceptance-text edit), and id 61 (a new backlog entry). Only id 40
  was not named in this feature's own brief; confirmed by reading the diff
  in full that it is unrelated to `orders_cancel_responder` and untouched by
  any file this feature edits.

## What I could not do, and why

- **Bullet 1's literal `POST /orders/{id}/cancel`** cannot be proven — the
  Gateway does not exist yet (feature 25, later this phase). Proven at the
  NATS boundary instead, as `orders_catalog_responder` already established
  the precedent for.
- **Bullet 4, the operator note on the timeline** — genuinely unbuildable
  within this feature's scope (`src/Orders/` only): the wire schema for
  `order.cancelled.v1`'s payload has no field for it, `specs/shared/` is
  read-only, and the Projector service that would surface it is a different
  service. #7 found and disclosed the identical gap for the identical
  reason, on the identical spec section, independently. Not fixed here;
  needs a `note`/`operatorNote` field added to `OrderCancelledPayload` in
  `specs/shared/asyncapi.yaml` (a spec amendment, human-gated), the
  regenerated contract, and `Projector`'s summary builder surfacing it — the
  same three-touch-point shape #7 named for the identical gap.
- **The operator-cancel-vs-saga-forward-progress race** — disclosed above,
  confirmed present in #8's own architecture and integration harness, not
  fixed. A genuine saga-design decision (transactional re-check before a
  forward-progress command's fast-path dispatch, or actively superseding an
  already-owed command), outside this feature's bounded scope, matching #7's
  own disposition of the identical finding.

## What surprised me

- The domain layer was **already fully built** for `operator_cancelled` from
  all four legal statuses, and `CompensationStepKind.CreditReleased` was
  **already defined**, before this feature touched anything — confirmed by
  reading, matching the acceptance criterion's own framing exactly. Unlike
  #7 (where the domain layer needed nothing changed either, but #7's own
  narrative treats this as expected), #8's spec additionally anticipates the
  `CompensationStep.summary` wire field (present in #8's copy of
  `specs/shared/asyncapi.yaml` but ABSENT from #7's checkout at the time #7
  wrote its own report) — a small, genuine divergence between the two specs
  worth naming even though this feature does not end up using `summary` for
  anything the note field itself could have used (the field exists on
  `CompensationStep`, not on `OrderCancelledPayload` directly, so it could
  not carry the operator note either).
- `SagaCommandRequestFactory`'s pre-existing code comment — written by a
  PRIOR feature (`order_saga_orchestrator`), before this feature existed —
  already named exactly what this feature would need almost word for word:
  "Feature 25's operator-cancellation flow will need its OWN enqueue path
  with `reason: order_cancelled` ... this factory is not where that decision
  would be made." Reading it confirmed the design this feature's handler
  ended up with (direct inline payload construction for the two RPC-triggered
  branches) before any code was written, rather than after.

## Fix round 2 (review defects D1, D2) — `progress/review_orders_cancel_responder.md`

Two blocking defects, both about the wire-visible decision this feature
introduced (`StockReleaseReasonFor`'s two-way switch) and about a required
schema field this feature's new reply payload omitted. Everything the
review confirmed sound — all four branches, the empty domain diff, bullet
3's two-mutation-family guard, the `SagaStepTable` extension, ledger rows 2
and 3, bullet 4's disclosure — was **not** re-touched, per the review's own
"What must change before re-review" list.

### D1 — `StockReleaseReasonFor` was unguarded end to end; ledger row 1 named a test that never calls it

**Root cause, restated precisely, because it is the thing that matters for
the next reader.** `SagaFactHandler.HandleAsync` (`SagaFactHandler.cs:114`)
is the ONLY production caller of `SagaCommandRequestFactory.BuildStockReleaseJson`
— it fires when `command == SagaCommandKind.StockRelease` for a fact-driven
step (`credit.rejected.v1`'s R27 row, or `credit.released.v1`'s two new
operator-cancel-compensation variants). `CancelOrderCommandHandler`'s own
`stock_reserved` branch is RPC-triggered, has no triggering fact, and builds
its `StockReleaseRequestPayload` inline — it never calls the factory. The
review's ledger row 1 named
`CancelOrderCommandHandlerTests.StockReserved_EnqueuesStockReleaseWithReasonOrderCancelled_...`
as row 1's guard, and that test exercises exactly the inline branch, never
the factory. So the factory's own two-arm switch had zero test coverage:
transposing the arms left 337/337 unit and 6/6 real-broker integration
green, exactly as the review's probe showed.

**Fix, in the three parts the review specified:**

1. **New unit tests that read through the factory, added to
   `tests/Orders.UnitTests/SagaFactHandlerTests.cs`** (the file already
   holds `FakeSagaCommandStore`, whose `Enqueued` list captures the exact
   JSON string `SagaFactHandler.HandleAsync` passed to
   `ISagaCommandStore.EnqueueAsync` — i.e. the factory's own output,
   deserialised back through `RpcJson.Deserialize<StockReleaseRequestPayload>`
   rather than re-implemented):
   - `CreditRejectedV1_StockReservedVariant_EnqueuesStockReleaseWithReasonCreditRejected_ThroughTheFactory`
     — an order at `StockReserved`, fact `credit.rejected.v1` fed to the
     real `SagaFactHandler`, asserts the enqueued payload's `Reason` is
     `"credit_rejected"`.
   - `CreditReleasedV1_CreditApprovedOrConfirmedVariant_EnqueuesStockReleaseWithReasonOrderCancelled_ThroughTheFactory`
     (theory, `CreditApproved` and `Confirmed`) — fact `credit.released.v1`,
     asserts `Reason == "order_cancelled"`.

   **Does this test execute the code the row is about?** Yes, and explicitly
   verified rather than assumed: both tests call `SagaFactHandler.HandleAsync`
   directly (no fake standing in for the factory), which internally calls
   `SagaCommandRequestFactory.BuildStockReleaseJson(order, fact.EventType)`
   at `SagaFactHandler.cs:114` — the ONE production call site — whose return
   value is the exact string captured by `FakeSagaCommandStore.Enqueued`.
   There is no re-implementation of the switch anywhere in the test; the
   assertion reads the factory's actual output.

2. **Integration read added at
   `tests/Orders.IntegrationTests/SagaCompensationCreditRejectedTests.cs`,
   immediately after the existing `Assert.NotNull(observedRelease)`:**
   `Assert.Equal("credit_rejected", observedRelease.Reason)`. This is the
   real NATS request the responder actually sent, over the real wire,
   through the real `CreditRejectedV1` → `stock.release` compensation path
   — the feature-17 shape the review named (a captured request asserted
   only `NotNull`) is now opened.

3. **Ledger row 1 corrected below**, replacing the original row's Guard
   column. The original table above (row "Reason-parametric `stock.release`")
   is left as written — this is an ADDITIVE correction, not an edit to that
   row, per this session's instruction not to rewrite what is already there.

   **Corrected Guard column for ledger row 1:**
   `SagaFactHandlerTests.CreditRejectedV1_StockReservedVariant_EnqueuesStockReleaseWithReasonCreditRejected_ThroughTheFactory`
   and
   `SagaFactHandlerTests.CreditReleasedV1_CreditApprovedOrConfirmedVariant_EnqueuesStockReleaseWithReasonOrderCancelled_ThroughTheFactory`
   (×2 statuses) — **armed, row H below**. Both drive the real
   `SagaFactHandler.HandleAsync`, whose only downstream call for
   `SagaCommandKind.StockRelease` is
   `SagaCommandRequestFactory.BuildStockReleaseJson` (`SagaFactHandler.cs:114`),
   and assert the `Reason` deserialised from the JSON string that call
   returned. Read, not assumed: opened `SagaFactHandler.cs:105-119` again
   while writing this row to confirm the call site is exactly the one these
   tests exercise, and that no other branch of `HandleAsync` calls
   `BuildStockReleaseJson`.

### D2 — `CreditReleaseReplyPayload` omitted the schema-required `availableCreditAfter`; BC23 was not extended to the pair this feature introduced

- Added `AvailableCreditAfter` (non-nullable `long`, third positional
  parameter, before the pre-existing optional `CreditCode`/`Currency`/
  `ReleasedAmount`) to `src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs`'s
  `CreditReleaseReplyPayload` — matching `specs/shared/asyncapi.yaml:3519-3541`'s
  `required: [released, orderReference, currency, availableCreditAfter]` and
  mirroring Billing's own copy's parameter order/positionality
  (`src/Billing/Infrastructure/Messaging/Rpc/CreditRpcPayloads.cs:33-39`),
  which already carried the field correctly. `Currency`'s own pre-existing
  optionality (nullable, despite being schema-required) was left exactly as
  it was — out of scope for D2, which named only the missing field, and
  Billing's own record has the identical asymmetry, so this is not a new
  inconsistency introduced here.
- Updated the two call sites that construct this record with positional
  arguments and would otherwise have bound `"CR-000001"`/other strings to
  the new `long` parameter (a build break, not a silent bug, since the
  parameter is non-nullable and the old third positional argument was a
  `string`):
  `tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs:173` and
  `tests/Orders.UnitTests/SagaCommandDispatcherTests.cs:193`, both switched
  to named arguments with `AvailableCreditAfter: 500_00`. Enumerated with
  `grep -rn "new CreditReleaseReplyPayload(" src tests` — 4 hits total, one
  in `Billing/Application/CreditReleaseService.cs` (a different type, in
  Billing's own namespace, unaffected) and one in each of the two files
  updated; no other call site exists.
- Added the two BC23 `[InlineData]` rows this feature's own convention
  required to `tests/Orders.UnitTests/SagaCommandPayloadTests.cs`'s existing
  `BC23_TheRetypedKeyListsAgreeWithTheKeySetsParsedFromAsyncApi` theory:
  `CreditReleaseRequestPayload` → `{ orderReference, retailerCode,
  companyCode }` and `CreditReleaseReplyPayload` → `{ released,
  orderReference, creditCode, currency, releasedAmount,
  availableCreditAfter }`, both parsed-vs-retyped sets confirmed equal.
  No new G5 case was added — the review asked only to confirm the
  **existing** G5 case (which targets `CreditHoldReplyPayload`, untouched
  by D2) still passes, which it does (see arming table row I).

### Arming table — fix round 2

Same discipline as the original table: mutate the real file, force
`dotnet build --no-incremental`, run the named test(s) and record the
verbatim FAIL, restore from a `cp` backup (never `git checkout --`), verify
`cmp` byte-identical, force `dotnet build --no-incremental` again, confirm
green.

| # | File mutated | Mutation | Named test(s) | FAIL message (verbatim) | Restored + green |
|---|---|---|---|---|---|
| H | `SagaCommandRequestFactory.cs:48-49` | Transposed `StockReleaseReasonFor`'s two arms: `"credit.rejected.v1" => "order_cancelled"`, `"credit.released.v1" => "credit_rejected"`. | `SagaFactHandlerTests.CreditRejectedV1_StockReservedVariant_EnqueuesStockReleaseWithReasonCreditRejected_ThroughTheFactory`; `SagaFactHandlerTests.CreditReleasedV1_CreditApprovedOrConfirmedVariant_EnqueuesStockReleaseWithReasonOrderCancelled_ThroughTheFactory` (×2 statuses) | All 3 cases: `Assert.Equal() Failure: Strings differ` — the first `Expected: "credit_rejected" / Actual: "order_cancelled"`, the other two `Expected: "order_cancelled" / Actual: "credit_rejected"`. | Yes — `cp` backup taken before mutation, restored, `cmp` reported byte-identical, `dotnet build --no-incremental` forced, confirming run: 3/3 green. |
| I | (no mutation — confirmation only) | Ran the pre-existing `SagaCommandPayloadTests.G5_TheGuardFailsAgainstAScratchCopyWhoseCreditHoldReplyPayloadPropertyWasRenamed` and the full `BC23_...` theory after adding the two new rows. | `G5_...`; `BC23_TheRetypedKeyListsAgreeWithTheKeySetsParsedFromAsyncApi` (12 cases, was 10) | N/A — confirms D2's addition did not disturb the existing guard. | `dotnet test tests/Orders.UnitTests --filter "FullyQualifiedName~SagaCommandPayloadTests"` → **24/24 passed** (whole `SagaCommandPayloadTests.cs`, read off this run, not inferred from the prior file's line count). |

### Verification — fix round 2

- `dotnet build tests/Orders.UnitTests --no-incremental` and `dotnet build
  tests/Orders.IntegrationTests --no-incremental`: both succeed, 0 warnings,
  0 errors, after the `CreditReleaseReplyPayload` signature change (proves
  the two call-site fixes are complete — a missed third call site would
  have failed the build, not run silently).
- `dotnet test tests/Orders.UnitTests` (isolated project run): **342/342
  passed** (337 baseline + 3 new `SagaFactHandlerTests` cases + 2 new BC23
  rows = 342).
- `dotnet test tests/Orders.IntegrationTests --filter
  "…SagaCompensationCreditRejectedTests|…OrdersCancelAcceptanceTests"`
  against real NATS/MS-SQL/Kafka: **6/6 passed**, including the new reason
  assertion at `SagaCompensationCreditRejectedTests.cs:83`.
- **Full-solution `./quality.sh`, run twice, both with no concurrent build
  activity from me.** First run: `dotnet test $SLN` reported ONE failure,
  `OrdersCancelAcceptanceTests.UnknownOrderId_RepliesNotFound` →
  `NATS.Client.Core.NatsNoRespondersException: No responders`. This test is
  untouched by D1 or D2 (it sends an `orders.cancel` RPC for an unknown
  order id immediately after `SagaIntegrationTestSupport.StartHostAsync`
  returns, with no wait for the responder's NATS subscription to be live —
  a pre-existing race, already disclosed in this file's own "Interference
  note" from the original implementation pass, which recorded the identical
  exception on the identical test under full-suite concurrent-container
  load). I did not treat one red run as the answer: I re-ran the full
  `./quality.sh` a second time, again with nothing else running
  concurrently, and it was fully green —
  `OrderToCash.Orders.IntegrationTests.dll`: **89/89 passed**, and every one
  of the 16 test projects passed, format check clean, build succeeded.
  Both runs' logs are consistent with a transient resource-contention flake
  under `dotnet test $SLN`'s parallel execution of six services' real-broker
  Testcontainers suites simultaneously (Billing.IntegrationTests took 4m37s
  and 4m48s across the two runs, Fulfillment.IntegrationTests 3m and 3m13s,
  Orders.IntegrationTests itself 6m47s and 6m53s — all running in the same
  process), not a regression from this round's two-file, additive change to
  `SagaCommandRequestFactory`/`SagaCommandPayloads`/their tests. **Reporting
  the count off the green run**, per the review's request:

  ```
  Passed!  - Failed: 0, Passed:  50, Total:  50 - OrderToCash.SharedKernel.UnitTests.dll
  Passed!  - Failed: 0, Passed:  23, Total:  23 - OrderToCash.Cqrs.UnitTests.dll
  Passed!  - Failed: 0, Passed:  21, Total:  21 - OrderToCash.Contracts.UnitTests.dll
  Passed!  - Failed: 0, Passed:  65, Total:  65 - OrderToCash.Notifications.UnitTests.dll
  Passed!  - Failed: 0, Passed: 119, Total: 119 - OrderToCash.Fulfillment.UnitTests.dll
  Passed!  - Failed: 0, Passed: 226, Total: 226 - OrderToCash.Billing.UnitTests.dll
  Passed!  - Failed: 0, Passed: 342, Total: 342 - OrderToCash.Orders.UnitTests.dll
  Passed!  - Failed: 0, Passed:  34, Total:  34 - OrderToCash.Seed.UnitTests.dll
  Passed!  - Failed: 0, Passed:   6, Total:   6 - OrderToCash.Seed.IntegrationTests.dll
  Passed!  - Failed: 0, Passed:  12, Total:  12 - OrderToCash.Notifications.IntegrationTests.dll
  Passed!  - Failed: 0, Passed:  16, Total:  16 - OrderToCash.Architecture.Tests.dll
  Passed!  - Failed: 0, Passed:  52, Total:  52 - OrderToCash.Projector.IntegrationTests.dll
  Passed!  - Failed: 0, Passed:  56, Total:  56 - OrderToCash.Fulfillment.IntegrationTests.dll
  Passed!  - Failed: 0, Passed:  83, Total:  83 - OrderToCash.Billing.IntegrationTests.dll
  Passed!  - Failed: 0, Passed:  87, Total:  87 - OrderToCash.Projector.UnitTests.dll
  Passed!  - Failed: 0, Passed:  89, Total:  89 - OrderToCash.Orders.IntegrationTests.dll
  ```

  Sum: **1281 tests across 16 projects, 0 failed** (baseline reported by the
  original implementation pass was 1276/1276; this round added exactly 5:
  3 new `SagaFactHandlerTests` cases + 2 new BC23 `[InlineData]` rows).
  `dotnet format --verify-no-changes: clean`, `dotnet build: succeeded`,
  `dotnet test: all tests passed`, `quality.sh finished` — all four gates
  green on this run.
- `./init.sh` — **exit 0**, re-run after the `feature_list.json` edit below.
  61 features parsed, 1 `in_progress` reported at the time of that run
  (before the id-41 transition — the transition to `in_review` was made
  immediately after, as this file's final step), backlog tripwire clean, no
  superseded rule text outside `progress/`.

### Files touched, fix round 2

- `src/Orders/Application/Sagas/SagaCommandRequestFactory.cs` — no
  behavioural change (the arming mutation was applied and reverted); this
  file is otherwise as the original implementation left it.
- `src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs` — `CreditReleaseReplyPayload` gains `AvailableCreditAfter`.
- `tests/Orders.UnitTests/SagaFactHandlerTests.cs` — two new tests (one
  `[Fact]`, one `[Theory]` ×2 cases) reading through the factory; one new
  `using`.
- `tests/Orders.UnitTests/SagaCommandPayloadTests.cs` — two new BC23
  `[InlineData]` rows.
- `tests/Orders.UnitTests/SagaCommandDispatcherTests.cs` — one call site
  updated to the new `CreditReleaseReplyPayload` shape.
- `tests/Orders.IntegrationTests/SagaCompensationCreditRejectedTests.cs` —
  one new assertion (`observedRelease.Reason == "credit_rejected"`).
- `tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs` — one call
  site updated to the new `CreditReleaseReplyPayload` shape.
- `feature_list.json` — id 41 `"in_progress"` → `"in_review"`, single line,
  verified with `git diff feature_list.json` to touch nothing else of mine
  and to leave the leader's four pre-existing hunks (id 40 `done`, the id-56
  acceptance edit, new entries id 61 and id 62) untouched.

### What I could not do, and why — fix round 2

Nothing was left undone from D1 or D2's scope. The full-solution flake on
`UnknownOrderId_RepliesNotFound` (first `quality.sh` run only) is disclosed
above rather than fixed — it is out of this round's scope (a
resource-contention timing race in test setup, unrelated to either defect),
already present before this round's changes, and the second green run of
the identical suite is the evidence that nothing in D1/D2's fix caused it.

Status set to `in_review` in `feature_list.json` as the final step.

## Fix round 3 (the disclosed race, corrected) — `UnknownOrderId_RepliesNotFound`'s red run was NOT a resource-contention flake

**The conclusion in fix round 2's own verification section was wrong, and
this section explains why, with the deterministic evidence the conclusion
skipped.** The instruction opening this round quoted CLAUDE.md's own rule
back at me: *"a green run is not evidence about a red one."* Round 2's
report ran `./quality.sh` a second time, got a clean run, and used that
green run as the reason to write off the red one as transient
resource-contention load. That is precisely the pattern the rule forbids —
the fact that "parallel container load is real" made the wrong conclusion
look plausible, which is exactly how CLAUDE.md says this mistake survives
review. Worse, round 2's own report had already **named the correct
mechanism in the same paragraph** it then dismissed: *"it sends an
`orders.cancel` RPC for an unknown order id immediately after
`SagaIntegrationTestSupport.StartHostAsync` returns, with no wait for the
responder's NATS subscription to be live."* That sentence is not a
resource-contention theory. It is a missing-synchronization defect, stated
correctly and then not acted on.

### Root cause, confirmed by reading, not inferred

`OrdersCreateResponder.ExecuteAsync` (`src/Orders/Presentation/OrdersCreateResponder.cs:52-62`)
starts three concurrent `SubscribeAsync` loops (`orders.create`,
`catalog.reference.list`, `orders.cancel`) via `Task.WhenAll`.
`BackgroundService.StartAsync` (the .NET base class, not this repository's
code) invokes `ExecuteAsync` directly and returns as soon as that call
either completes or yields at its first incomplete `await` — it does
**not** wait for the three `SubscribeAsync` calls' own network round trip to
the NATS server to land. `SagaIntegrationTestSupport.StartHostAsync`
(`tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs`, the shared
harness for all six saga integration test files) called `await
host.StartAsync();` and returned immediately — no probe, no wait, nothing
confirming the responder's subscriptions had actually landed server-side.

Every other caller of this shared harness happens to be protected by
accident, not by design: `SagaHappyPathTests`, `SagaPreconditionTests`,
`SagaCompensationStockRejectedTests`, `SagaCommandRetryTests` and
`SagaCompensationCreditRejectedTests` all call
`SagaIntegrationTestSupport.PlaceOrderAsync` (an **in-process** dispatcher
call, never a NATS RPC) before doing anything else, and the real
asynchronous work that call performs (DB transaction, an actual
`fulfillment.stock.check` RPC to a stand-in responder) absorbs enough real
wall-clock time for the responder's own three subscriptions — all started
together from the same `Task.WhenAll` — to land. `OrdersCancelAcceptanceTests.UnknownOrderId_RepliesNotFound`
is the one test in the whole suite that does not: it calls
`StartHostAsync` and sends its `orders.cancel` RPC as the very next
statement, with zero warm-up. That is the entire explanation for why this
was the only test observed to flake, and it has nothing to do with which
other Testcontainers suites happened to be running in the same `dotnet
test` process.

### Deterministic reproduction — enumeration first

Search performed before concluding no other file in this feature's own
scope has the identical gap (path-excluding, complete output, one
classification line per hit):

```
$ grep -rn "RequestAsync<byte\[\], byte\[\]>" tests/Orders.IntegrationTests --include=*.cs \
    | grep -v "StandInSagaResponders.cs\|StandInFulfillmentStockCheckResponder.cs\|OrdersCancelResponderReadinessRaceTests.cs"
```
23 hits, across `CatalogReferenceListAcceptanceTests.cs` (9 — a different
feature, `orders_catalog_responder`, builds and warms up its own host with
its own established wait, out of this round's scope), `OrdersCancelAcceptanceTests.cs`
(2 — the two RPC helpers `CancelAsync`/`CancelExpectingErrorAsync`, now
protected because `StartHostAsync` itself waits before returning),
`OrdersCreateAcceptanceTests.cs` (11 — already protected by its own local
`WaitUntilOrdersCreateReachableAsync`, called explicitly before every RPC)
and `SagaIntegrationTestSupport.cs` (1 — the new wait's own `RequestAsync`
call, not a gap). **Zero unclassified hits.** `StandInSagaResponders.cs`
and `StandInFulfillmentStockCheckResponder.cs` were excluded from the
search because they are the RESPONDER side (stand-ins), and separately
confirmed by reading (`StandInRpcResponder.StartAsync`,
`StandInFulfillmentStockCheckResponder`'s own `StartAvailableAsync`) to
already block on their own `WaitUntilSubscribedAsync` probe before
returning — including `StartCreditReleaseAsync`, the one stand-in this
feature itself added, which only calls the already-guarded generic
factory. So the one and only gap, in this feature's own files plus the
shared harness named in the brief, was `SagaIntegrationTestSupport.StartHostAsync`
itself.

Per the instruction, I did not fix-and-declare-gone. Before writing any
fix I forced the failure deterministically — a change of **kind**, not of
probability — with a synthetic subscriber whose own subscription is
delayed by a controlled 300ms, well past any single request's timeout,
so the outcome is certain rather than lucky. New file:
`tests/Orders.IntegrationTests/OrdersCancelResponderReadinessRaceTests.cs`,
three tests, all `[Collection(SagaCollection.Name)]` (no host built — only
`NatsContainerFixture`):

1. `RequestSentBeforeAnythingSubscribes_ThrowsNoResponders_EveryTime` — the
   PRODUCTION subject and payload shape (`RpcSubjects.OrdersCancel`,
   `OrdersCancelRequestPayload`), with literally nothing subscribed on the
   broker, reproduces the exact exception the flaky run showed,
   deterministically: `NatsNoRespondersException`. This is the certain
   limiting case of "the RPC races ahead of the subscription."
2. `WithoutTheWait_ADelayedSubscriberLosesTheRaceDeterministically` — a
   synthetic subscriber (own subject, own NATS connection, `Task.Delay(300ms)`
   before its own `SubscribeAsync`) proves a caller that does not wait
   loses the race **every time**, not occasionally — the general shape of
   the missing-synchronization defect, isolated from any Testcontainers
   parallelism.
3. `WithTheWait_ADelayedSubscriberNoLongerLosesTheRace` — the SAME
   300ms-delayed subscriber, but the caller goes through
   `SagaIntegrationTestSupport.WaitUntilReachableAsync` first (the exact
   function `StartHostAsync` now calls) — and succeeds.

### The fix

`SagaIntegrationTestSupport.StartHostAsync` now blocks, after
`host.StartAsync()` and before returning, on a real request/reply round
trip to `orders.cancel` (`WaitUntilOrdersResponderReachableAsync`, a thin
wrapper naming the specific subject/payload, over the extracted generic
`WaitUntilReachableAsync(connection, subject, probe, cancellationToken)`) —
matching the established pattern already proven correct elsewhere in this
codebase: `BillingHostFixture.WaitUntilReachableAsync`,
`FulfillmentHostFixture.WaitUntilReachableAsync`,
`OrdersCreateAcceptanceTests.WaitUntilOrdersCreateReachableAsync`,
`StandInRpcResponder.WaitUntilSubscribedAsync`. The probe order id is a
fresh `Guid` no order can ever hold, so the only possible success reply is
a harmless `NOT_FOUND` error body — never a real cancellation, never a
side effect on the fresh database `StartHostAsync` just created.

**One genuine strengthening over the four existing precedents, found while
arming this fix, not assumed.** All four precedents pace their retry loop
only via the per-attempt request timeout (200ms) — none has an explicit
delay between failed attempts. `NatsNoRespondersException` is the NATS
server's *immediate* "definitely nobody subscribed" sentinel: it does not
wait out the request's own timeout, it comes back in well under a
millisecond. A loop that paces only via that timeout can therefore burn
through all 100 attempts in a few milliseconds when every attempt hits
"no responders," and give up long before a genuinely slow subscription
lands — which is exactly what happened when I first wrote
`WaitUntilReachableAsync` without a pacing delay and ran it against the
300ms-delayed synthetic subscriber: it threw in ~1ms, all 100 attempts
exhausted near-instantly (see arming table row R2 below). I added an
unconditional `await Task.Delay(50ms, cancellationToken)` after every
failed attempt, which fixed it. **This is disclosed, not silently folded
in**: the same unpaced shape exists in `BillingHostFixture`,
`FulfillmentHostFixture`, `OrdersCreateAcceptanceTests` and
`StandInRpcResponder` (four files, all outside this round's scope — Orders
test support only), and in production those probes happen to work because
the real subscribe-to-probe window is normally microseconds, not hundreds
of milliseconds. It is a latent limitation there, not a live defect —
noted here for whoever next touches one of those four files under heavier
load than this repository has hit so far, not fixed here.

### Arming table — fix round 3

Same discipline as rounds 1 and 2: mutate the real file, force `dotnet
build --no-incremental`, run the named test(s), record the verbatim FAIL,
restore from a `cp` backup (never `git checkout --`), verify `cmp`
byte-identical, force `dotnet build --no-incremental` again, confirm
green.

| # | File mutated | Mutation | Named test(s) | FAIL message (verbatim) | Restored + green |
|---|---|---|---|---|---|
| R1 | (baseline — no mutation, first draft of the fix) | `WaitUntilReachableAsync` written WITHOUT the `Task.Delay(50ms)` pacing line (its first form). | `OrdersCancelResponderReadinessRaceTests.WithTheWait_ADelayedSubscriberNoLongerLosesTheRace` | `System.TimeoutException : 'orders.cancel.readiness-race-repro.8e2b335cd1d14c9c98fedc64b402e4f4' never became reachable.` — thrown after ~250ms wall-clock (100 attempts exhausted near-instantly against the still-300ms-away subscriber), proving the unpaced loop's real patience budget was far smaller than "100 × 200ms" implied. | N/A — this was the pre-fix draft itself, not a mutation of a working fix; superseded by adding the pacing line, confirmed passing (3/3) immediately after. |
| R2 | `SagaIntegrationTestSupport.cs` | The `Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)` pacing line deleted from `WaitUntilReachableAsync` (reverting to R1's shape) — a `cp` backup of the WORKING fix taken first. | `OrdersCancelResponderReadinessRaceTests.WithTheWait_ADelayedSubscriberNoLongerLosesTheRace` | `System.TimeoutException : 'orders.cancel.readiness-race-repro.dbc4d2c73a2c448eb99d70f84f842665' never became reachable.` at `SagaIntegrationTestSupport.cs:165`, `Failed: 1, Passed: 2, Total: 3`, `Duration: 284 ms`. | Yes — restored via `cp` from the pre-mutation backup, `cmp` reported byte-identical, `dotnet build --no-incremental` forced, confirming run: 3/3 green (`Duration: 483 ms`–`530 ms` across 5 repeats). |

`RequestSentBeforeAnythingSubscribes_ThrowsNoResponders_EveryTime` and
`WithoutTheWait_ADelayedSubscriberLosesTheRaceDeterministically` are not
themselves arming targets (they do not call the fix; they establish the
baseline the fix is judged against) — both were run alongside every arming
step above and stayed green throughout, confirming the mutation in R2
affected only the fix's own function, nothing else in the file.

### Verification — fix round 3

- `OrdersCancelResponderReadinessRaceTests`: **3/3 passed**, run 5
  consecutive times back to back with no failures (`dotnet test
  tests/Orders.IntegrationTests --no-build --filter
  "FullyQualifiedName~OrdersCancelResponderReadinessRaceTests"`, ×5) — the
  deterministic guard, not merely corroborating evidence.
- `OrdersCancelAcceptanceTests`: **5/5 passed** (full file, one run).
- `OrdersCancelAcceptanceTests.UnknownOrderId_RepliesNotFound` specifically,
  run **10 consecutive times** in its own process each time (`dotnet test
  ... --filter
  "FullyQualifiedName~OrdersCancelAcceptanceTests.UnknownOrderId_RepliesNotFound"`,
  ×10): **10/10 passed, 0 failed** — offered here as corroborating
  evidence only, per the instruction's own framing; the deterministic proof
  is `OrdersCancelResponderReadinessRaceTests` above, not this repeat count.
- The other five saga suites sharing `SagaIntegrationTestSupport.StartHostAsync`
  (`SagaHappyPathTests`, `SagaPreconditionTests`,
  `SagaCompensationStockRejectedTests`, `SagaCommandRetryTests`,
  `SagaCompensationCreditRejectedTests`): **9/9 passed**, one combined run —
  no regression from the added wait.
- Full `tests/Orders.IntegrationTests` project: **92/92 passed** (was 89 at
  the close of round 2; +3, `OrdersCancelResponderReadinessRaceTests`).
- **`./quality.sh`, run twice, both with nothing else running concurrently:**
  - Run 1: `dotnet format --verify-no-changes: clean`; `dotnet build:
    succeeded`; `dotnet test: all tests passed`; `quality.sh finished`.
    Sum of all 16 projects' own "Passed!" lines, read off this run:
    50+23+21+65+226+119+342+34+87+16+6+12+52+56+83+92 = **1284, 0 failed**
    (baseline reported at the top of this round's brief was 1281; this
    round added exactly 3, `OrdersCancelResponderReadinessRaceTests`).
  - Run 2: same four gates, same shape:
    50+23+21+65+119+226+342+34+87+16+6+12+56+83+92+52 = **1284, 0 failed**.
  - **Neither run showed the `UnknownOrderId_RepliesNotFound` failure (or
    any other) this time.** Two green runs are still not, by themselves,
    proof the race is closed — consistent with this round's own opening
    correction — the proof is the deterministic
    `OrdersCancelResponderReadinessRaceTests` suite and the arming table
    above; the two clean full-solution runs are additional, not
    substitute, evidence.
- `./init.sh`: **exit 0.** 61 features, no feature `in_progress`, "40/61
  features done", backlog tripwire clean, commit-msg hook installed,
  session file in lockstep. The two `[WARN]` lines (58 uncommitted changes;
  "run `./quality.sh` before closing," already run above) are expected
  mid-session.
- `feature_list.json`: **not touched.** `git status --short
  feature_list.json` shows the file modified, and `sed -n '577,588p'
  feature_list.json` confirms id 41's `"status"` is still `"in_review"` —
  the same pre-existing uncommitted state named in the brief, none of it
  mine.

### Files touched, fix round 3

- `tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs` — three new
  usings (`NATS.Client.Core`, `OrderToCash.Orders.Infrastructure.Messaging.Rpc`,
  `OrderToCash.Orders.Presentation.Rpc`); `StartHostAsync` now waits for
  `orders.cancel` reachability before returning;
  `WaitUntilOrdersResponderReachableAsync` (new, `orders.cancel`-specific)
  and `WaitUntilReachableAsync` (new, generic, subject-parameterised, the
  arming target) added. No existing method's signature or behaviour
  changed.
- `tests/Orders.IntegrationTests/OrdersCancelResponderReadinessRaceTests.cs`
  (new) — the three deterministic tests described above.

### What I could not do, and why — fix round 3

Nothing was left undone from this round's own scope. The latent
"no-pacing" shape in `BillingHostFixture`, `FulfillmentHostFixture`,
`OrdersCreateAcceptanceTests` and `StandInRpcResponder` is disclosed above
rather than fixed — those four files are outside `tests/Orders.IntegrationTests/`'s
saga-support scope (three of them are literally in different test
projects), the brief's scope bound is `tests/Orders.IntegrationTests/` and
its test support, and none of the four has yet been observed to fail from
it — a genuine finding for whoever next touches one of them under load
heavier than this repository has produced so far, not a defect this round
introduced or is positioned to fix.

Status remains `in_review` in `feature_list.json` — unchanged by this round, as instructed.
