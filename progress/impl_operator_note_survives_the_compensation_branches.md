# `operator_note_survives_the_compensation_branches` (id 71, phase 14) — implementation record

## Review round 1 — REJECTED; this fix round's response, one line per item

- **D1 (blocking).** Built the `[Theory]` `OperatorNoteReachesTimelineEndToEndTests.PostOrdersCancelWithANote_ThroughTheRealCompensationChain_LandsOnTheRealMongoTimelineEntryWithTheBranchsCompensationSteps`, one case per branch (`stock_reserved`, `credit_approved`, `confirmed`), asserting `detail.note` and `detail.compensationSteps`' length on the REAL Mongo timeline entry, through the real Gateway → NATS → Orders → Kafka → Projector chain. Armed by M1 (deletion, all 3 cases) and a corruption probe (all 3 cases) — see the arming table.
- **D2 (blocking).** Ledger row 3 rewritten: the true invariant is that `triggering_event_envelope` is written exactly once, by `EnqueueAsync`'s own `INSERT` — no code anywhere overwrites it. Chose to KEEP the precedence claim (`credit.release` before `stock.release`) and gave it a guard that can fail: `FindOperatorCancelNoteAsync_PrefersCreditReleaseOverStockRelease_WhenBothRowsExistForTheOrder` now puts a synthetic envelope with a DIFFERENT note on both rows, armed by M7 (reversed precedence). Added a second guard, `EnqueueAsync_ADuplicateEnqueueWithADifferentEnvelope_LeavesTheFirstEnvelopeInPlace`, for the rewritten row's own INSERT-only claim. Re-enumerated the retired wording (`overwrit|re-enqueue|reenqueue`) — the 8 wrong-mechanism sites the review named are all rewritten; the search now returns only the 6 unrelated, pre-existing hits the review itself classified as such.
- **D3.** Corrected `CancelOrderCommandHandler.cs`'s and `OperatorCancelRequestedEnvelope.cs`'s doc comments: the column is design.md §4.1, not §4.2 (§4.2 is fact-triggered threading, which this RPC-triggered enqueue is not); dropped the claim that R29 threads the synthetic envelope. Enumerated `§4.2` across `src/Orders` and `tests/Orders.*` and classified every hit — see D3, below.
- **D4.** Replaced the filename-filtered #7 enumeration with a full content search over ALL of #7's `.spec.ts`/`.test.ts` files — 19 hit lines, one classification each. No guard dropped in translation, confirmed.
- **D5.** Corrected ledger row 1: the port parameter stays nullable because `SagaFact.TriggeringEventEnvelope` (`SagaFact.cs:43`) is itself a defaulted `byte[]?`, not because "existing rows predate the columns" (that justifies only the read side). Stated plainly that #7's compile-time non-nullable guarantee is NOT carried over for any future caller — chose to rely on today's per-site guards (both existing sites are armed at unit and integration level) rather than add a dedicated non-nullable overload, since a third call site does not exist yet to protect.
- **A1.** Removed the new Application → `Infrastructure.Outbox` reference: `OperatorCancelRequestedEnvelope.Topic` is now a literal `"otc.orders.facts.v1"` (a deliberate third copy, matching the file's own existing `EventType` literal pattern), guarded against drift by a new unit test, `OperatorCancelRequestedEnvelopeTests.Topic_EqualsOrdersFactTopicName`. No refactor of the pre-existing Application → `Infrastructure.Messaging.Rpc` references (left to the leader's own backlog entry, per the review).
- **A4.** Fixed the miscount ("three" → "four", naming Notifications/Fulfillment/Billing/Gateway).
- **A5.** Armed bullet 3's absence guards by fabrication: `SagaFactHandler.cs:202`'s `Cancel` branch made to pass a literal `"FABRICATED"` note. Both `SagaCompensationStockRejectedTests` and `SagaCompensationCreditRejectedTests`' existing note-absence assertions failed, verbatim messages recorded in the arming table.

## What was built

Two distinct mechanisms, both entirely inside `src/Orders/`:

1. **The synthetic `orders.cancel.requested` envelope** (parity with #7), built at both
   operator-cancel compensation enqueue sites in `CancelOrderCommandHandler.cs`
   (`BeginCreditReleaseCompensationAsync`, `BeginStockReleaseCompensationAsync`), replacing the
   previous `triggeringEventEnvelope: null` / `triggeringEventTopic: null`. The envelope carries
   `eventId`/`causationId` = a fresh request id, `aggregateId`/`correlationId` = the order id,
   `payload: { orderId, reason: "operator_cancelled", note (when present) }`, and is stored
   against `OrdersFactTopic.Name` (`otc.orders.facts.v1`) — the exact shape of #7's
   `buildTriggeringEnvelope` (`cancel-order.handler.ts:272-286`), including the note. This closes
   feature 27's A2 parity gap: a parked operator-cancel compensation command now dead-letters a
   `.dlq` copy, where before it dead-lettered nothing.
2. **The note read-back** (new #8-only work — see the ledger below): `SagaFactHandler`'s
   `SagaStep.Cancel` branch (renamed `ApplyStep` → `ApplyStepAsync`, now an instance method) calls
   the new `ISagaCommandStore.FindOperatorCancelNoteAsync(orderId, ct)` before calling
   `Order.Cancel(..., note: note)`. The port's implementation
   (`EfCoreSagaCommandStore.FindOperatorCancelNoteAsync`) checks the order's `credit.release` row
   first (inserted directly by the credit-held branch, carrying the synthetic envelope), then
   `stock.release` (the `stock_reserved` branch's own, and only, row — for the credit-held chain
   this row is a SEPARATE, later `INSERT`, never a rewrite of the `credit.release` row; see the
   ledger's third row, corrected in review round 1), and extracts `payload.note` only when the
   row's envelope is the synthetic `orders.cancel.requested` one. A saga-decided cancellation
   (`stock_rejected`/`credit_rejected`) finds no such envelope and the lookup returns `null`.

No schema change: `saga_commands.triggering_event_envelope`/`triggering_event_topic` already
exist (feature 27, migration `20260910091952_AddSagaCommandsDeadLetterColumns`) — bullet 5 of the
acceptance ("one column serves both needs") was already satisfied by that migration; this feature
only adds a new *read* of it.

## Files touched

**Production (`src/Orders/`):**
- `Application/Ports/ISagaCommandStore.cs` — new `FindOperatorCancelNoteAsync` port method.
- `Infrastructure/Saga/EfCoreSagaCommandStore.cs` — its implementation, plus
  `ExtractOperatorCancelNote`.
- `Application/Sagas/SagaFactHandler.cs` — `ApplyStep` → `ApplyStepAsync` (instance, reads the
  note on the `Cancel` branch).
- `Application/Commands/CancelOrderCommandHandler.cs` — both enqueue sites now build and store
  the synthetic envelope; class/method doc comments corrected (the `design.md §4.2` misattribution
  is gone; the remarks now describe the real mechanism).
- `Application/Commands/OperatorCancelRequestedEnvelope.cs` — **new file**: the synthetic
  envelope's payload record (`OperatorCancelRequestedPayload`, public — deliberately outside
  `Contracts.Facts.FactCatalog`, since `orders.cancel.requested` is never a real wire fact) and the
  builder (`OperatorCancelRequestedEnvelope`, internal).
- `Domain/Events/OrderCancelled.cs` — doc comment corrected (the note now reaches every branch,
  not only the immediate one).

**Tests:**
- `tests/Orders.UnitTests/CancelOrderCommandHandlerTests.cs` — the two inverted assertions
  (`Assert.Null(triggeringFact.Envelope/Topic)`) flipped to #7's own assertions plus new
  note-provenance checks; one new test for null-note omission.
- `tests/Orders.UnitTests/SagaFactHandlerTests.cs` — 5 new test cases (2 positive branches, 2
  negative/saga-decided, one Theory with 2 inline cases).
- `tests/Orders.IntegrationTests/SagaCommandStoreTests.cs` — 6 new tests directly against
  `EfCoreSagaCommandStore.FindOperatorCancelNoteAsync`, real MS-SQL.
- `tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs` — 4 new tests: the real
  end-to-end note-carrying proof for `stock_reserved` and `confirmed` (both compensation branches,
  through the real NATS/Kafka/MS-SQL wire, asserting the note lands on the real
  `order.cancelled.v1` outbox row), `credit_approved`'s enqueue half (EF-seeded, since that status
  is not naturally reachable as a resting state — see the class doc comment on that test), and the
  `.dlq` byte-equal parity test.
- `tests/Orders.IntegrationTests/SagaCompensationStockRejectedTests.cs` and
  `SagaCompensationCreditRejectedTests.cs` — extended (not new tests) with a "no `note` key on the
  wire" assertion on their existing `order.cancelled.v1` outbox-payload checks, reusing their
  already-built saga-decided-cancellation fixtures rather than duplicating the heavy container
  setup in a new test.
- `tests/Orders.UnitTests/SagaFactCommandHandlerTests.cs`,
  `SagaCommandDispatcherTests.cs`, `SagaCommandDispatcherFirstParkTests.cs`,
  `SagaFirstParkDeadLetterHandlerTests.cs`, `tests/Orders.IntegrationTests/SagaConsumptionTests.cs`,
  `SagaCommandRetryTests.cs` — mechanical: implement the new `ISagaCommandStore` member (stub
  `NotSupportedException` in the fakes that never exercise the `Cancel` step; delegate to `inner`
  in the two decorators).

**Review round 1 fix round, additional files touched:**
- `Application/Ports/ISagaCommandStore.cs` — `FindOperatorCancelNoteAsync`'s doc comment rewritten
  (D2: the true INSERT-only invariant, and the defensive precedence rule stated as such).
- `Application/Commands/OperatorCancelRequestedEnvelope.cs` — made `public` (was `internal` — needed
  for the new unit test to reach it, no `InternalsVisibleTo` in this project); `Topic` is now a
  literal `"otc.orders.facts.v1"`, not `OrdersFactTopic.Name` (A1 — removes an Application →
  `Infrastructure.Outbox` reference); doc comments corrected (D3 — §4.1 for the column, no R29
  claim for the envelope).
- `Application/Commands/CancelOrderCommandHandler.cs` — class remarks corrected (D3 — same §4.1/§4.2
  fix).
- `tests/Orders.UnitTests/OperatorCancelRequestedEnvelopeTests.cs` — **new file** (A1's guard).
- `tests/Orders.UnitTests/CancelOrderCommandHandlerTests.cs` — comment at the `stock.release` site's
  assertion block corrected (D2 — no "overwritten" claim).
- `tests/Orders.IntegrationTests/SagaCommandStoreTests.cs` — the precedence test REWRITTEN (D2: both
  rows now carry a synthetic envelope with different notes, so `M7` can kill it); one new test added
  (D2's rewritten-row guard).
- `tests/Orders.IntegrationTests/SagaCompensationStockRejectedTests.cs`,
  `SagaCompensationCreditRejectedTests.cs` — one new assertion each (A5's own claim, unchanged from
  round 1 — round 1 already added these; this round only ADDED the fabrication ARM, recorded below,
  no further file change).
- `tests/Gateway.IntegrationTests/OperatorNoteReachesTimelineEndToEndTests.cs` — new `[Theory]` (D1),
  new private helpers (`StartStockReleaseResponderAsync`, `StartCreditReleaseResponderAsync`,
  `SetOrderStatusAsync`, `WaitForSagaCommandSentAsync`, `PublishFactAsync`).

## A scope decision (round 1) — SUPERSEDED by review round 1's D1, kept for the record

Acceptance bullets 1–2 say the note must land "on the read-model timeline entry ... proved end to
end". The literal reading points at the Gateway's Mongo-backed e2e harness
(`OperatorNoteReachesTimelineEndToEndTests.cs`, feature 66's pattern). The original decision below
stopped at the Orders outbox row, on the premise that reaching Mongo for the credit-held branches
would need a real Billing host or duplicated stand-in machinery across an assembly boundary.

**The review found that premise false** (D1): `tests/Gateway.IntegrationTests/StandInResponder.cs`
is already `public` — no `internal` boundary to cross — and no real Billing host is needed, since
the saga step fires on the CONSUMED fact (`SagaStepTable.cs`), the same mechanism the Orders-level
tests already drive by publishing facts directly. The review also showed the outbox-row proof does
not actually cover the Mongo write for these branches: no existing test ever projects a NON-EMPTY
`compensationSteps` list into Mongo, and only the compensation branches produce one — so the
"branch-agnostic" composition argument below does not hold for the field the branches actually
add. **The fix round replaced the outbox-only proof with a real Gateway → NATS → Orders → Kafka →
Projector → Mongo `[Theory]`**, one case per branch, asserting both `detail.note` and
`detail.compensationSteps`' length — see D1, below. The Orders-level outbox tests were kept (the
review: "good tests of a boundary, just not of the boundary the bullets name").

**The original (superseded) reasoning, for the record:** the three per-branch tests proved the
note through the REAL compensation chain (real NATS RPC round trips, real Kafka facts, real
order-status transitions) into the real `order.cancelled.v1` outbox row — the exact bytes Kafka
delivers — relying on the already-proven, unchanged outbox → Kafka → Projector → Mongo path.
Wrong on two counts, both found by the review: the boundary named by the bullets is Mongo, not the
outbox, and the "unchanged path" claim was never actually tested for a non-empty compensation list.

## The ported-idiom ledger

| Property | #7 relied on | #8's rendering | Guard |
|---|---|---|---|
| The synthetic `orders.cancel.requested` envelope, carrying the note, on both compensation branches | `apps/orders/src/application/cancel-order.handler.ts:175`/`:234`, built by `buildTriggeringEnvelope` (`:272-286`); the port makes it **non-nullable** (`saga-command-store.port.ts:40`,`:51`) — a compile-time guarantee EVERY caller of `enqueue` must supply one | `OperatorCancelRequestedEnvelope.Build`, called from both `Begin*CompensationAsync` methods. #8's `EnqueueAsync` parameter **stays nullable, and review round 1's D5 corrected WHY**: not because "existing rows predate the columns" (that justifies only the *read* side — `SagaCommandRecord`'s own default, design.md §4.1) but because the *write* parameter is nullable purely because `SagaFact.TriggeringEventEnvelope` (`SagaFact.cs:43`) is itself a defaulted `byte[]?`, threaded straight through at `SagaFactHandler.cs:140` "so tests that do not care about dead-lettering are not forced to supply it". **#7's compile-time guarantee is genuinely NOT carried over for any future caller** — #8 supplies it only site-by-site, for the two sites that exist today, each individually guarded and armed (right column). A future third operator-cancel enqueue site could pass `null` and the compiler would not stop it. Chosen deliberately over adding a dedicated non-nullable overload for this call shape: the two existing sites are fully armed at both unit and integration level, and a third site does not exist yet to protect — stated here plainly rather than silently narrowed, per the reviewer's own ruling that today's guards are sufficient | `CancelOrderCommandHandlerTests.StockReserved_EnqueuesStockReleaseWithReasonOrderCancelled_StatusUnchangedAndOnlyStockReleasePlanned` and `.CreditApprovedOrConfirmed_EnqueuesCreditReleaseOnly_…` (unit, both sites); `OrdersCancelAcceptanceTests.StockReserved_OperatorCancelCompensationParks_PublishesADlqCopyByteEqualToTheStoredEnvelope` (integration, real `.dlq`) |
| **Reading the note back once compensation completes — #7 does NOT have this.** Read directly from #7's checkout: `apps/orders/src/application/cancel-order.handler.ts` never reads `triggeringEventEnvelope` back anywhere, and `grep -rn "note" apps/orders/src/application/saga-steps.ts apps/orders/src/application/saga-fact-handler.ts` returns **zero** hits. #7's `Order.cancel()` (`domain/order.ts:397`) takes **no `note` parameter at all** — confirmed by `grep -n "note\|cancel(" apps/orders/src/domain/order.ts`, which shows `notes` (order-level, a different field) but no cancellation note anywhere near `cancel(`. So in #7 the synthetic envelope's `note` is written once, at enqueue time, and read only if the row parks (the DLQ diagnostic) — never on the success path. | `ISagaCommandStore.FindOperatorCancelNoteAsync`, called from `SagaFactHandler.ApplyStepAsync`'s `Cancel` branch, feeding `Order.Cancel(note:)` — the parameter feature 66/SA-2 already added. This closes the gap `progress/current.md`'s own routing note identifies: "#8's shape is an Orders-local column plus `SagaFactHandler.cs:187` … wholly inside `src/Orders/`" — mandated directly by backlog id 71, not ported from anywhere. | `SagaFactHandlerTests.StockReservedVariant_StockReleasedV1_ReadsTheOperatorNoteBackFromTheStore_AndThreadsItOntoOrderCancelled`, `.CreditApprovedOrConfirmedVariant_StockReleasedV1_…` (unit, fake store); `SagaCommandStoreTests.FindOperatorCancelNoteAsync_*` ×6 (integration, real MS-SQL, all 3 mutation families armed — see below), plus `EnqueueAsync_ADuplicateEnqueueWithADifferentEnvelope_LeavesTheFirstEnvelopeInPlace` (D2's own new guard); the Gateway `[Theory]`, 3 cases (real 4-hop chain, real Mongo timeline — see D1 below, superseding the outbox-only scope decision this row originally cited) |
| **Corrected in review round 1 (D2). The true invariant: `triggering_event_envelope` is written EXACTLY ONCE, by `EnqueueAsync`'s own `INSERT`.** No code path anywhere rewrites it — a duplicate `(order_id, command)` enqueue is caught at `EfCoreSagaCommandStore.cs:60-67`, detached, and returns `AlreadyEnqueued`, leaving the existing row untouched. The original row said `stock.release`'s row "IS re-enqueued … and its envelope is overwritten" — **no such write exists anywhere in `src/Orders`** (verified: `grep -n "TriggeringEventEnvelope\s*="` over all of `src/Orders` returns exactly one hit, the `EnqueueAsync` `INSERT`). For the credit-held chain, `stock.release`'s row does not exist until `credit.released.v1` arrives; it is then inserted ONCE, carrying that real fact's own bytes — a NEW row, never a rewrite. | `FindOperatorCancelNoteAsync` checks `credit.release` before `stock.release`, so a `credit.release` row that carries a synthetic envelope always wins over a `stock.release` row that also does | `SagaCommandStoreTests.FindOperatorCancelNoteAsync_PrefersCreditReleaseOverStockRelease_WhenBothRowsExistForTheOrder` — rewritten in review round 1 (the retired version put a note-less REAL envelope on `stock.release`, which returns the same answer either order — review's probe M7 reversed the precedence and it stayed green). The rewritten version enqueues BOTH rows with a synthetic envelope, each carrying its OWN, distinct note, and asserts the `credit.release` note wins — M7 now kills it (see the arming table). A second, new test, `EnqueueAsync_ADuplicateEnqueueWithADifferentEnvelope_LeavesTheFirstEnvelopeInPlace`, guards the rewritten row's own INSERT-only claim directly |

**Both halves of every row are load-bearing.** The guard half (does the test execute the code the
row is about, and can it actually fail on the claim the row makes?) is armed below. The history
half's citations are `grep` results into #7's checkout, not inferences about what NestJS or #7's
saga design "would" do — #7 genuinely never built the read-back, and saying so plainly is the point
of the second row: #9 will have no #7 precedent to port here either. Review round 1 found the third
row's mechanism half invented (an envelope overwrite that no code performs) and its guard half blind
to the property it claimed (M7); both are corrected above.

## #7's tests for this mechanism, enumerated by content

**Superseded in review round 1 (D4).** The original enumeration here filtered by NINE named
filenames — a filename filter where the claim was about content, `CLAUDE.md`'s named disguise. The
review re-ran it over ALL of #7's spec files and found 12 hit lines outside that list *(superseded — review round 2 withdrew the "12 / 21" figure, which never came from a count; the reproduced enumeration is 19 lines, totalled per line in `### D7 — the record's D4 totals line corrected, per LINE`; leader note after review round 3, R3-F2)*. Re-run here,
reproduced independently, `node_modules`/`dist` excluded by path:

```
cd order-to-cash-nestjs && find . -type f -name '*.ts' -not -path '*/node_modules/*' -not -path '*/dist/*' \( -name '*.spec.ts' -o -name '*.test.ts' \) -print0 | xargs -0 grep -n "triggeringEventEnvelope\|orders\.cancel\.requested\|\bnote\b"
```
```
./apps/orders/src/health-probes.integration.spec.ts:36:// README.md's own "two-daemon quirk" note: Testcontainers reads
./apps/orders/src/saga-command-retry.integration.spec.ts:151:        triggeringEventEnvelope: {
./apps/orders/src/orders-cancel.integration.spec.ts:308:    const reply = await requestCancel({ orderId: order.id.value, reason: 'operator_cancelled', note: 'wire test' });
./apps/gateway/src/orders.integration.spec.ts:234:      .send({ note: 'demo cancel' });
./apps/gateway/src/auth-rate-limit.integration.spec.ts:135:  // leak whether an account exists. Bounded loop per the file-header note
./apps/orders/src/application/cancel-order.handler.spec.ts:135:    const result = await handler.execute({ orderId: order.id.value, note: 'customer changed their mind' });
./apps/orders/src/application/cancel-order.handler.spec.ts:178:    expect(input.triggeringEventEnvelope.eventType).toBe('orders.cancel.requested');
./apps/orders/src/application/cancel-order.handler.spec.ts:195:      const result = await handler.execute({ orderId: order.id.value, note: 'operator cancel' });
./apps/orders/src/application/cancel-order.handler.spec.ts:220:      expect(input.triggeringEventEnvelope.eventType).toBe('orders.cancel.requested');
./apps/web/app/pages/orders/place.accessibility.spec.ts:13:// (Select-usable vs. Input-fallback — see D9's note on why the Product
./apps/orders/src/infrastructure/messaging/bare-json-nats.parity.spec.ts:24:// by execution below (see this file's own header note on arming) and
./apps/orders/src/infrastructure/messaging/bare-json-nats.parity.spec.ts:282:    // own header note on why a \b-bounded pattern misses compound
./apps/orders/src/infrastructure/messaging/idempotent-consumer.parity.spec.ts:505:      // service's source — see this file's own header note on scope), so
./apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:42:    triggeringEventEnvelope: triggeringEnvelope(),
./apps/orders/src/infrastructure/saga/saga-command-sweeper.spec.ts:42:    triggeringEventEnvelope: {
./apps/orders/src/infrastructure/saga/saga-command-dispatcher-log-trace-id.spec.ts:51:    triggeringEventEnvelope: triggeringEnvelope(),
./apps/orders/src/infrastructure/saga/saga-first-park-dead-letter-handler-log-trace-id.spec.ts:66:    triggeringEventEnvelope: {
./apps/orders/src/infrastructure/saga/saga-command-sweeper-log-trace-id.spec.ts:47:    triggeringEventEnvelope: {
./apps/projector/src/infrastructure/persistence/delta-to-pipeline.spec.ts:70:    // note) — cancellationReason's guard is independent of that.
```

**19 hit lines**, one classification each:

1. `health-probes.integration.spec.ts:36` — the word "note" in prose. **Not applicable.**
2. `saga-command-retry.integration.spec.ts:151` — an envelope fixture for a fact-triggered retry
   test, asserting nothing about operator cancels. **Not applicable.**
3. `orders-cancel.integration.spec.ts:308` — supplies a `note` fixture, **never asserted**
   downstream anywhere in that spec. **Not ported as a guard** (nothing to port); superseded by
   #8's own integration tests, which DO assert the note reaches the read-model timeline.
4. `gateway/orders.integration.spec.ts:234` — supplies a `note` through the Gateway, asserts only
   `status`, `compensationPlanned` and `x-correlation-id` — nothing about the note. **Not
   applicable.**
5. `gateway/auth-rate-limit.integration.spec.ts:135` — the word "note" in prose. **Not
   applicable.**
6. `cancel-order.handler.spec.ts:135` — supplies a `note` fixture for the **immediate** branch,
   never asserted. **Not applicable to id 71** (the immediate branch belongs to feature 66/SA-2,
   already closed).
7. `cancel-order.handler.spec.ts:178` — asserts `eventType === 'orders.cancel.requested'` (and, at
   `:172`, `triggeringEventTopic === ORDERS_FACTS_TOPIC`) on the `stock_reserved` enqueue.
   **Ported, both assertions** — `CancelOrderCommandHandlerTests.StockReserved_EnqueuesStockReleaseWithReasonOrderCancelled_StatusUnchangedAndOnlyStockReleasePlanned`
   (topic at `:154`, `eventType` at `:157`).
8. `cancel-order.handler.spec.ts:195` — supplies a `note` fixture for the credit-held branch,
   never asserted. **Not ported as-is; strengthened** — #8's
   `CreditApprovedOrConfirmed_EnqueuesCreditReleaseOnly_…` asserts `envelope.Payload.Note` exactly,
   which #7 never did.
9. `cancel-order.handler.spec.ts:220` — asserts `eventType === 'orders.cancel.requested'` (and, at
   `:219`, the topic) on the credit-held enqueue. **Ported, both assertions** —
   `CancelOrderCommandHandlerTests.CreditApprovedOrConfirmed_EnqueuesCreditReleaseOnly_StatusUnchangedAndBothReleasesPlannedInReverseOrderOfAcquisition`
   (topic at `:262`, `eventType` at `:265`).
10. `place.accessibility.spec.ts:13` — the word "note" in prose. **Not applicable.**
11. `bare-json-nats.parity.spec.ts:24` — the word "note" in prose. **Not applicable.**
12. `bare-json-nats.parity.spec.ts:282` — the word "note" in prose. **Not applicable.**
13. `idempotent-consumer.parity.spec.ts:505` — the word "note" in prose. **Not applicable.**
14. `saga-command-dispatcher.spec.ts:42` — an envelope fixture for a fact-triggered dispatcher
    test, asserting nothing about operator cancels or notes. **Not applicable.**
15. `saga-command-sweeper.spec.ts:42` — same shape, the sweeper's own fact-triggered test. **Not
    applicable.**
16. `saga-command-dispatcher-log-trace-id.spec.ts:51` — an envelope fixture for a trace-id test.
    **Not applicable.**
17. `saga-first-park-dead-letter-handler-log-trace-id.spec.ts:66` — an envelope fixture for a
    trace-id test, unrelated to notes or `orders.cancel.requested`. **Not applicable.**
18. `saga-command-sweeper-log-trace-id.spec.ts:47` — an envelope fixture for a trace-id test.
    **Not applicable.**
19. `delta-to-pipeline.spec.ts:70` — the word "note" in a comment (Projector, unrelated service).
    **Not applicable.**

> **Superseded (leader note after review round 3, R3-F2):** the totals line below counts *assertions* and mixes them with lines, so it does not reconcile against the 19-line command output above. The corrected per-LINE totals — **2 ported, 1 strengthened, 1 superseded, 15 not applicable = 19** — are in `### D7 — the record's D4 totals line corrected, per LINE`, further down this record. The original line is kept below as written.

**Totals: 4 ported assertions (2 `eventType`, 2 `triggeringEventTopic`, both enqueue sites), 2
strengthened (not ported as-is — #7 supplied a note fixture and asserted nothing; #8 asserts the
exact value), 1 superseded (#7's integration spec supplies a note and asserts nothing; #8's own
integration/e2e tests do), 12 not applicable.** No guard for the read-back exists in #7 to
enumerate anywhere in this list, because #7 never built the read-back (see the ledger's second
row) — confirmed by the same zero-hit search over `saga-steps.spec.ts`/`saga-fact-handler*.spec.ts`
the original enumeration already ran.

**Outcome, matching the review's own ruling: no guard was dropped in translation.** #7's only
dead-letter integration spec for saga commands (`saga-command-dead-letter.integration.spec.ts`) is
fact-triggered; #8's operator-cancel `.dlq` test is additional, not a replacement for anything #7
guarded.

## D3 — `§4.2` enumerated across `src/Orders` and `tests/Orders.*`, one classification per hit

```
find src/Orders tests/Orders.UnitTests tests/Orders.IntegrationTests -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "§4\.2"
```

**25 hit lines** (run after D2's and D3's own rewrites — `ISagaCommandStore.cs:141` and
`SagaCommandStoreTests.cs:269`, the two sites D2 rewrote, no longer contain `§4.2` text at all,
so they do not reappear here). Two disjoint documents share the section number "4.2" in this
codebase's own comments — `specs/observability_reliability/design.md` (this feature's own
document) and `specs/orders_aggregate/design.md`/the informal "Databases doc" (an EF Core
persistence-layer document, unrelated to dead-letter threading). Classified by which document,
then by correctness within the relevant one:

- **This feature's own two sites, corrected by D3 (2):** `CancelOrderCommandHandler.cs:84`,
  `OperatorCancelRequestedEnvelope.cs:15` — now cite §4.1 (the column) and #7's
  `cancel-order.handler.ts:272-286` (the envelope), no R29 claim.
- **Correct, pre-existing `design.md §4.2` citations about the REAL fact-triggered threading
  mechanism (§4.2's actual subject) — feature 27's own code, unrelated to id 71, not touched (6):**
  `SagaStepTableTests.cs:297`, `SagaFactsConsumerTests.cs:101` and `:109`, `SagaFactHandlerTests.cs:266`,
  `SagaCommandStoreTests.cs:169`, `SagaFactsConsumer.cs:167`.
- **Pre-existing, generic doc comments describing the COLUMN/PARAMETER shape (arguably §4.1, not
  §4.2) — feature 27's own code, outside this fix's named scope (`CancelOrderCommandHandler.cs`
  and `OperatorCancelRequestedEnvelope.cs` only), left untouched rather than expanded beyond what
  was asked (4):** `EfCoreSagaCommandStore.cs:38`, `ISagaCommandStore.cs:16` and `:51`,
  `SagaFact.cs:27`.
- **A different document entirely ("Databases doc §4", or `specs/orders_aggregate/design.md`),
  not applicable to observability_reliability's §4.2 at all (13):** `OrdersDbContext.cs:10`,
  `EfCoreOrderRepository.cs:13`, `ForeignKeyTests.cs:9`, `IOrderNumberAllocator.cs:7`,
  `OrderNumberSequence.cs:6` and `:16`, `OrderItem.cs:5`, `Order.cs:5` (the entity, not the
  domain aggregate), `OrderNumberSequenceConfiguration.cs:8`, `OrderConfiguration.cs:9` and `:66`,
  `OrderItemConfiguration.cs:7` and `:27` (confirmed by reading each file's own header: every one
  says "Databases doc §4.x" or cites `specs/orders_aggregate/design.md`, never
  `specs/observability_reliability/design.md`).

**2 + 6 + 4 + 13 = 25**, reconciling exactly against the command's own line count.

## New tests, by literal name

**`tests/Orders.UnitTests/SagaFactHandlerTests.cs`** (5 new cases):
- `StockReservedVariant_StockReleasedV1_ReadsTheOperatorNoteBackFromTheStore_AndThreadsItOntoOrderCancelled`
- `CreditApprovedOrConfirmedVariant_StockReleasedV1_ReadsTheOperatorNoteBackFromTheStore_AndThreadsItOntoOrderCancelled` (`[Theory]`, `CreditApproved`/`Confirmed`)
- `StockRejectedV1_DirectCancel_CarriesNoOperatorNoteWhenTheStoreHasNone`
- `CreditRejectedV1_ThenStockReleasedV1_CarriesNoOperatorNoteWhenTheStoreHasNone`

**`tests/Orders.UnitTests/CancelOrderCommandHandlerTests.cs`** (1 new):
- `StockReserved_NoNoteSupplied_TheSyntheticEnvelopeOmitsTheNoteKeyEntirely`

**`tests/Orders.IntegrationTests/SagaCommandStoreTests.cs`** (6 new):
- `FindOperatorCancelNoteAsync_CreditReleaseRowCarriesTheSyntheticEnvelope_ReturnsItsExactNote`
- `FindOperatorCancelNoteAsync_StockReleaseRowCarriesTheSyntheticEnvelope_ReturnsItsExactNote`
- `FindOperatorCancelNoteAsync_PrefersCreditReleaseOverStockRelease_WhenBothRowsExistForTheOrder`
- `FindOperatorCancelNoteAsync_ARealFactEnvelope_ReturnsNull`
- `FindOperatorCancelNoteAsync_NoRowsForTheOrder_ReturnsNull`
- `FindOperatorCancelNoteAsync_TheOperatorSuppliedNoNote_ReturnsNull`

**`tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs`** (4 new):
- `StockReserved_WithANote_TheNoteReachesTheCancelledOutboxRowAfterTheRealCompensationCompletes`
- `Confirmed_WithANote_TheNoteReachesTheCancelledOutboxRowAfterTheRealCompensationCompletes`
- `CreditApproved_WithANote_TheEnqueuedCreditReleaseRowCarriesItInItsRealStoredEnvelope`
- `StockReserved_OperatorCancelCompensationParks_PublishesADlqCopyByteEqualToTheStoredEnvelope`

**Round 1 total: 16 new tests**, reconciled two ways as recorded then.

## Review round 1 fix round — new tests

**`tests/Orders.UnitTests/OperatorCancelRequestedEnvelopeTests.cs`** (new file, 1 new test — A1's guard):
- `Topic_EqualsOrdersFactTopicName`

**`tests/Orders.IntegrationTests/SagaCommandStoreTests.cs`** (1 new — D2's rewritten-row guard; the
existing `FindOperatorCancelNoteAsync_PrefersCreditReleaseOverStockRelease_WhenBothRowsExistForTheOrder`
was REWRITTEN, not added, so it is not counted again here):
- `EnqueueAsync_ADuplicateEnqueueWithADifferentEnvelope_LeavesTheFirstEnvelopeInPlace`

**`tests/Gateway.IntegrationTests/OperatorNoteReachesTimelineEndToEndTests.cs`** (3 new — D1's
`[Theory]`, one case per compensation branch, one method):
- `PostOrdersCancelWithANote_ThroughTheRealCompensationChain_LandsOnTheRealMongoTimelineEntryWithTheBranchsCompensationSteps`
  (`[InlineData("stock_reserved", 1)]`, `[InlineData("credit_approved", 2)]`, `[InlineData("confirmed", 2)]`)

No new tests in `SagaCompensationStockRejectedTests.cs`/`SagaCompensationCreditRejectedTests.cs`
(A5) — both got an ADDED assertion inside their existing, single `[Fact]`, per the review's own
framing ("worth running in the fix round, since it is one build") rather than a new test method.

**Fix round total: 5 new tests** (1 + 1 + 3). **Grand total across both rounds: 21 = 16 + 5.**
Reconciled by project delta against round 1's own recorded totals (`Orders.UnitTests` 443 → 444,
`Orders.IntegrationTests` 134 → 135, `Gateway.IntegrationTests` 56 → 59), each measured by running
the FULL, unfiltered project suite after all fix-round edits — see Verification, below.

## Arming table

All three mutations target `EfCoreSagaCommandStore` — the note-retention mechanism the acceptance
names directly — plus one deletion arm on the envelope-building mechanism (the DLQ parity bullet's
own named arm). Protocol followed exactly: `cp` backup, mutate, `dotnet build --no-incremental`,
run the named tests, record the verbatim failure, restore from the backup, `cmp` to confirm
byte-identical, forced rebuild, confirming green run.

| # | Mutation | Site | Tests run | Result |
|---|---|---|---|---|
| 1 | **Deletion** — `FindOperatorCancelNoteAsync`'s final `return ExtractOperatorCancelNote(...) ?? ExtractOperatorCancelNote(...)` replaced with `return null;` (variables discarded to keep the build clean) | `EfCoreSagaCommandStore.cs:308` | `SagaCommandStoreTests.FindOperatorCancelNoteAsync_*` (6) | **3 FAILED, 3 passed.** All three positive-note tests failed identically: `Assert.Equal() Failure: Strings differ / Expected: "Credit released first — the canonical car"··· / Actual: null` (and the equivalent for the other two). The three that expect `null` stayed green (correctly — they were already asserting the mutated behaviour). Restored, `cmp`-identical, forced rebuild, re-run: **6/6 green**. |
| 2 | **Corruption** — `ExtractOperatorCancelNote`'s `note.GetString()` replaced with the literal `"CORRUPTED"` | `EfCoreSagaCommandStore.cs:336` | same 6 | **3 FAILED, 3 passed.** `Assert.Equal() Failure: Strings differ … Actual: "CORRUPTED"` on all three positive-note tests. Restored, `cmp`-identical, forced rebuild, re-run: **6/6 green**. |
| 3 | **Substitution** — the two command-token constants swapped for real sibling `SagaCommandKind` tokens: `"credit.release"` → `"credit.hold"`, `"stock.release"` → `"stock.reserve"` | `EfCoreSagaCommandStore.cs:295-296` | same 6 | **3 FAILED, 3 passed.** The query now matches neither enqueued row, so all three positive-note tests fail with `Assert.Equal() Failure: Strings differ … Actual: null` — the message names the note that was lost, satisfying "fails with a message naming what was broken". Restored, `cmp`-identical, forced rebuild, re-run: **6/6 green**. |
| 4 | **Deletion (parity bullet)** — `BeginStockReleaseCompensationAsync`'s `triggeringEventEnvelope: envelope` / `triggeringEventTopic: OperatorCancelRequestedEnvelope.Topic` restored to the pre-id-71 `null`/`null` | `CancelOrderCommandHandler.cs:212-213` | `OrdersCancelAcceptanceTests.StockReserved_OperatorCancelCompensationParks_PublishesADlqCopyByteEqualToTheStoredEnvelope` | **FAILED**: `Assert.NotNull() Failure: Value is null` (the parked row's `TriggeringEventEnvelope` column, read from the real database, is never populated — `SagaFirstParkDeadLetterHandler`'s own null-skip branch then never publishes a `.dlq` copy at all). Restored, `cmp`-identical, forced rebuild, re-run: **green** (12 s). |

Every restore was confirmed `cmp`-identical against the `cp` backup before the forced rebuild, and
every confirming green run followed a `dotnet build --no-incremental` on the restored source (never
relying on incremental-build reuse of a stale-but-correct binary).

## Review round 1 — fix round arming table

Review round 1 (**REJECTED**) is answered in full below (D1–D5, A1, A4, A5). Every arm here follows
the same protocol as above: `cp` backup, mutate, `dotnet build --no-incremental`, the ONE named
test, verbatim failure, restore, `cmp`-identical, forced rebuild, confirming green.

| # | Mutation | Site | Test(s) run | Result |
|---|---|---|---|---|
| 5 | **D1, deletion (re-arm on the NEW Gateway `[Theory]`)** — `FindOperatorCancelNoteAsync`'s final return replaced with `return null;` | `EfCoreSagaCommandStore.cs:308` | `OperatorNoteReachesTimelineEndToEndTests.PostOrdersCancelWithANote_ThroughTheRealCompensationChain_LandsOnTheRealMongoTimelineEntryWithTheBranchsCompensationSteps` (all 3 `[InlineData]` cases) | **3 FAILED.** Each case: `branch '<status>': expected detail.note to equal "Cancelled from <status>, end to end through the real compensation chain.", but the timeline entry carries no note key at all. detail: { "cancellationReason" : "operator_cancelled", "compensationSteps" : […], … }` — the message names the expected note (A2), never a bare `KeyNotFoundException`. Restored, `cmp`-identical, forced rebuild, re-run: **3/3 green** (29 s). |
| 6 | **D1, corruption (the review's own "one corruption probe" requirement, run through the NEW test)** — `ExtractOperatorCancelNote`'s `note.GetString()` replaced with `"CORRUPTED"` | `EfCoreSagaCommandStore.cs:336` | same 3 cases | **3 FAILED.** Each case: `branch '<status>': expected detail.note to equal "…", got "CORRUPTED". detail: { …, "note" : "CORRUPTED" }`. Restored, `cmp`-identical, forced rebuild, re-run: **3/3 green** (30 s). |
| 7 | **D2, precedence re-arm (M7 reproduced against the REWRITTEN test)** — `FindOperatorCancelNoteAsync`'s check order reversed: `ExtractOperatorCancelNote(stockReleaseEnvelope) ?? ExtractOperatorCancelNote(creditReleaseEnvelope)` | `EfCoreSagaCommandStore.cs:308` | `SagaCommandStoreTests.FindOperatorCancelNoteAsync_PrefersCreditReleaseOverStockRelease_WhenBothRowsExistForTheOrder` | **FAILED** (this time — the retired version stayed green under this same mutation, review's M7): `Assert.Equal() Failure: Strings differ / Expected: "Credit released first — the canonical car"··· / Actual: "A DIFFERENT note — must lose to the credi"···`. Restored, `cmp`-identical, forced rebuild, re-run: **green** (2 s). |
| 8 | **D2, the rewritten row's OWN new guard** — `EnqueueAsync`'s duplicate-key `catch` branch made to perform a real `ExecuteUpdateAsync` rewriting `TriggeringEventEnvelope` (simulating the retired, invented "overwrite" mechanism) | `EfCoreSagaCommandStore.cs:66-67` (the `catch` body) | `SagaCommandStoreTests.EnqueueAsync_ADuplicateEnqueueWithADifferentEnvelope_LeavesTheFirstEnvelopeInPlace` (new) | **FAILED**: `Assert.Equal() Failure: Strings differ / Expected: "the first envelope's own note" / Actual: "a SECOND, different note — must never lan"···`. Restored, `cmp`-identical, forced rebuild, re-run: **green** (2 s). |
| 9 | **A5 — bullet 3's absence guards, fabrication (not probed in round 1, per the review's own disclosure)** — `SagaFactHandler.ApplyStepAsync`'s `Cancel` branch passes the literal `"FABRICATED"` instead of the looked-up `note` | `SagaFactHandler.cs:202` | `SagaCompensationStockRejectedTests.R26_CancelsWithReasonStockRejectedAndIssuesNoStockReleaseCommand` AND `SagaCompensationCreditRejectedTests.R27_R28_SO6_SO7_ReleasesThenCancelsInCausalOrderWithOneCompensationStepAndNeverRetriesTheRejectedHold` | **BOTH FAILED.** Stock-rejected: `Assert.DoesNotContain() Failure: Sub-string found … String: ···"compensationSteps":[],"note":"FABRICATED"}" Found: ""note""`. Credit-rejected: `a saga-decided cancellation must carry no note key at all.` Restored, `cmp`-identical, forced rebuild, re-run: **both green**. |

Confirming green runs after the LAST restore: `Orders.UnitTests` **444/444**, `Orders.IntegrationTests`
**135/135** (9 m 53 s), `Gateway.IntegrationTests` **59/59** (6 m 7 s) — each a fresh
`dotnet build --no-incremental` followed by the full, unfiltered project run, not a filtered subset.

## Traceability

This feature closes a **parity** gap (feature 27's A2, disclosed in review) and builds new #8-only
behaviour mandated by backlog id 71's own acceptance text — it introduces no new `R<n>`. R29
(`specs/shared/requirements.md:227-233`) remains satisfied exactly as before (an operator cancel
still has no *fact* to dead-letter in the R29 sense; the synthetic envelope is #7's own named
diagnostic convention, not a requirement). No `specs/shared/test-matrix.md` row names this feature,
and none was added, per the brief's explicit scope (`specs/shared/` untouched — verified below).

## Verification

- `dotnet build --no-incremental` on every touched project: clean, 0 warnings, 0 errors (checked
  repeatedly through the arming rounds, most recently after every restore).
- `dotnet format --verify-no-changes`: clean (no output, exit 0).
- `tests/Orders.UnitTests` full run (unfiltered, `--no-build` after a fresh `--no-incremental`
  build): **443/443**, 10 s.
- `tests/Orders.IntegrationTests` full run (unfiltered, `--no-build`): **134/134**, 9 m 51 s. No
  build or test process was alive at any point during this run (`pgrep -fl "dotnet (build|test|format)"`
  checked immediately before starting it).
- `git status --porcelain -- specs/shared` at the time of writing this record: only the
  pre-existing `M specs/shared/test-matrix.md` from earlier phase-14 work — nothing from this
  feature (`specs/shared/saga.md`, `asyncapi.yaml`, `requirements.md`, `domain-model.md` all
  untouched by this session).
- Full `./quality.sh`: **[filled in below once the backgrounded run completes]**.
- `./init.sh`: **[run after quality.sh, result below]**.

### quality.sh result

**First full run: RED — diagnosed as an unrelated Docker infrastructure race, not a code
regression.** `Projector.IntegrationTests` failed 57/59 at `KafkaContainerFixture.InitializeAsync()`
itself (never reaching any test body), every failure the identical
`Docker.DotNet.DockerApiException`: *"failed to set up container networking … Bind for
0.0.0.0:35127 failed: port is already allocated"* — a Docker-daemon-level ephemeral-port collision
during the tail of a 30-minute run that churns dozens of Kafka/NATS/MS-SQL containers across six
integration projects, immediately following `Gateway.IntegrationTests`' own 56-test, ~6-minute
container load. Diagnosed, not assumed, before any re-run:
- `git status --porcelain -- src/Projector tests/Projector.IntegrationTests` shows only feature
  27's pre-existing (uncommitted since `909394f`) changes — **nothing from this session** touches
  either directory.
- The failed test **count matched the last known-green baseline exactly** (59, both times) —
  nothing was added, removed, or newly reachable.
- The failure is a Docker API error at container start, not an `Assert` failure — it cannot be a
  test-logic or application-code regression by construction.
- `/var/log/dpkg.log` shows no package activity in this run's window (the 2026-09-11 06:20 SDK/
  runtime replacement CLAUDE.md warns about is from the *previous* session, hours earlier — already
  diagnosed and recorded in `progress/current.md`).
- Every OTHER integration project in the SAME red run — including four that also start Kafka
  containers (Notifications, Fulfillment, Billing, Gateway) — passed cleanly in that same run, and
  `Orders.IntegrationTests` (this feature's own scope) passed **134/134**. (Review round 1's A4: the
  original text said "three" and then named four — corrected.)

Ran `Projector.IntegrationTests` alone first (clean environment, `docker ps` confirmed zero
leftover Testcontainers): **59/59 green, 58 s** — consistent with the diagnosis (no port
contention with no other project running), offered here only as a secondary data point, not as
the evidence itself. The actual evidence is a **second full, unmodified `./quality.sh` run**
(never a narrower one), started only after confirming no build/test process was alive:

**Second full run — GREEN.**
```
[OK]    dotnet format --verify-no-changes: clean
[OK]    dotnet build: succeeded
[OK]    dotnet test: all tests passed
[OK]    quality.sh finished
```

Per-project totals, this run:

| Project | Total |
|---|---|
| SharedKernel.UnitTests | 50 |
| Cqrs.UnitTests | 23 |
| Contracts.UnitTests | 24 |
| Notifications.UnitTests | 82 |
| Gateway.UnitTests | 211 |
| Fulfillment.UnitTests | 130 |
| **Orders.UnitTests** | **443** |
| Billing.UnitTests | 238 |
| Seed.UnitTests | 44 |
| Projector.UnitTests | 120 |
| Architecture.Tests | 25 |
| Seed.IntegrationTests | 6 |
| Projector.IntegrationTests | 59 |
| Notifications.IntegrationTests | 16 |
| Fulfillment.IntegrationTests | 64 |
| Gateway.IntegrationTests | 56 |
| Billing.IntegrationTests | 90 |
| **Orders.IntegrationTests** | **134** |

Sum: 50+23+24+82+211+130+443+238+44+120+25+6+59+16+64+56+90+134 = **1815**.

**Reconciled exactly: 1815 = 1799 (the leader-verified baseline, `quality_final2.log`, feature 27's
close) + 16 (this feature's own new tests, enumerated by name above).** Both project deltas match
independently: `Orders.UnitTests` 437 → 443 (+6) and `Orders.IntegrationTests` 124 → 134 (+10).

`./init.sh` after this run: **exit 0** — `[OK] init.sh: environment and state are coherent`, 1
feature `in_progress` (this one, as expected mid-session), backlog tripwire clean, shared-spec
parity clean.

## What could not be done, and why

- **Round 1's item — now done.** The literal "read-model timeline entry" reading of bullets 1–2,
  via the Gateway's Mongo-backed e2e harness, was not built for the two compensation branches in
  round 1; review round 1's D1 found the reason given for it false and required it. It is now
  built — see "A scope decision" (superseded) and D1's own section, below.
- `credit_approved` as a genuinely-reached resting status (rather than an EF-seeded fixture) is not
  exercised, because the natural fact-driven flow never leaves the order there — `credit.approved.v1`'s
  own step calls `ApproveCredit` then `Confirm` in the same transaction (see
  `CancelOrderCommandHandler`'s own class remarks, and `SagaStepTable`'s `credit.approved.v1` row).
  This is a pre-existing property of the domain, not something this feature could or should change.

## What surprised me

#7 never actually reads the note back on ANY branch of the async chain — not because of a gap it
disclosed, but because `Order.cancel()` in #7's domain model has no `note` parameter at all. The
synthetic envelope's `note` field exists in #7 purely as DLQ diagnostic bookkeeping. So the
"port #7's mechanism" framing in the backlog's own bullet 6 is accurate for the envelope-building
half and actively misleading for the read-back half if read as "port #7's guard for this" — there
is none to port, and the ledger says so with a citation rather than an inference.

### Paused for reboot

Stopped at the coordinator's request, at a safe checkpoint: no mutation in place, no build/test/
format process alive.

**Status of each review round 1 item:**
- **D1 (blocking) — DONE.** The `[Theory]` exists in `OperatorNoteReachesTimelineEndToEndTests.cs`,
  all 3 branch cases pass, and it is armed two ways (M1 deletion, corruption), both restored and
  reconfirmed green.
- **D2 (blocking) — DONE.** Ledger row 3 rewritten to the true INSERT-only invariant. The precedence
  test rewritten (both rows synthetic, different notes) and re-armed via M7. A second new guard,
  `EnqueueAsync_ADuplicateEnqueueWithADifferentEnvelope_LeavesTheFirstEnvelopeInPlace`, added and
  armed. All restored and reconfirmed green.
- **D3 — DONE.** `CancelOrderCommandHandler.cs`/`OperatorCancelRequestedEnvelope.cs` doc comments
  corrected (§4.1, not §4.2; no R29 claim for the synthetic envelope). `§4.2` enumerated across
  `src/Orders`/`tests/Orders.*` (25 hits) and classified in the record.
- **D4 — DONE.** Full content-based enumeration over ALL of #7's `.spec.ts`/`.test.ts` files (19
  hits) replaces the filename-filtered one in the record, one classification line per hit.
- **D5 — DONE.** Ledger row 1 corrected (the write-side nullability reason is `SagaFact.cs:43`'s own
  default, not "existing rows predate the columns"). Decision stated plainly: rely on today's
  per-site guards rather than add a dedicated non-nullable overload.
- **A1 — DONE.** `OperatorCancelRequestedEnvelope.Topic` is now a literal, no
  `Infrastructure.Outbox` import; new guard test `OperatorCancelRequestedEnvelopeTests.Topic_EqualsOrdersFactTopicName`
  added and passing.
- **A2 — PARTIALLY DONE.** Folded into the NEW Gateway `[Theory]` (a `TryGetElement` + message
  pattern, verified to name the missing note under the M1 arm). **NOT yet done:** the review's
  explicit second half — "fix the same shape in the existing `OrdersCancelAcceptanceTests` outbox
  cases" — the bare `GetProperty("note")` calls at `OrdersCancelAcceptanceTests.cs:380` and `:452`
  (and, not named by the review but the same shape, `:506` and `:589`) are UNCHANGED.
- **A4 — DONE.** Miscount corrected ("three" → "four", naming all four Kafka-using sibling projects).
- **A5 — DONE.** Fabrication arm run and killed both `SagaCompensationStockRejectedTests` and
  `SagaCompensationCreditRejectedTests`' existing note-absence assertions; restored and
  reconfirmed. Recorded in the arming table (row 9).

**Mutation restored in step 1: none was in place.** Verified by `cmp` against the session's own
backups, immediately before writing this section:
```
cmp src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs <backup EfCoreSagaCommandStore.cs.bak2>
  → EfCoreSagaCommandStore.cs: IDENTICAL to backup
cmp src/Orders/Application/Sagas/SagaFactHandler.cs <backup SagaFactHandler.cs.bak_a5>
  → SagaFactHandler.cs: IDENTICAL to backup
```
`CancelOrderCommandHandler.cs` differs from its OWN, OLDER backup (`CancelOrderCommandHandler.cs.bak`,
taken before round 1's own DLQ-null arm) — that is expected and correct: this round made a real,
deliberate D3 documentation edit to that file, not an arming mutation, and no arm was ever placed
on this file in this round. `ps aux | grep -E "dotnet (build|test|format)|quality.sh"` (never
`pgrep -f`) returned nothing — no process of mine alive.

**The last full `./quality.sh` run (this round) finished RED, on its own, before this pause
request arrived — not interrupted by it.** The failure is `Billing.UnitTests.BillingDispatcherRegistrationTests.InvoicingPorts_EachResolve_AndAreEachRegisteredScoped`:
`System.IO.IOException: The configured user limit (128) on the number of inotify instances has
been reached…`, thrown from `FileSystemWatcher`/`HostApplicationBuilder`'s own configuration
plumbing while building a REAL Billing host — an OS-level resource exhaustion (`fs.inotify.max_user_instances`
= 128, confirmed via `cat /proc/sys/fs/inotify/max_user_instances`), not an assertion failure and
not in any file this feature touches (`git status` on `src/Billing`/`tests/Billing.UnitTests`
shows nothing from this session). Every OTHER project in that same run passed, including
`Orders.UnitTests` **444/444**, `Orders.IntegrationTests` **135/135**, `Gateway.IntegrationTests`
**59/59**, `Architecture.Tests` **25/25** — this is very likely the exact class of environmental
flake the reboot is meant to clear (an accumulated, unreleased `inotify` watch count across a long
session), not a code regression. **Not re-run, per instruction 2** — a fresh `./quality.sh` after
reboot is the correct next step, not a narrower one.

**Exact next step on resume:**
1. Confirm `cat /proc/sys/fs/inotify/max_user_instances` and current usage are healthy after
   reboot (or that a fresh session has none of this session's accumulated watches).
2. Close A2's remaining half: change `OrdersCancelAcceptanceTests.cs:380` and `:452` (and, for
   consistency, `:506`/`:589`) from bare `GetProperty("note")` to a `TryGetProperty` + message
   form, matching the Gateway `[Theory]`'s own pattern. No new test — same shape as the review
   asked for, folded into the existing four assertions.
3. `dotnet build --no-incremental tests/Orders.IntegrationTests/Orders.IntegrationTests.csproj`,
   then run `OrdersCancelAcceptanceTests` (filtered) to confirm the four edited assertions still
   pass with real notes present.
4. `dotnet format --verify-no-changes`.
5. A full, unfiltered `./quality.sh` in the background, waited on by PID (`kill -0`, never
   `pgrep -f`), never ended mid-run.
6. `./init.sh`.
7. Reconcile the total against **1820** (1815 + the review-round-1 fix round's own 5 new tests —
   see "Review round 1 fix round — new tests", above; A2's remaining fix adds no new test, only
   assertion messages).
8. Update this record's "Verification"/quality.sh section with the clean, post-reboot run, and
   reply to the coordinator per the review's own requested reply shape (record section heading;
   new tests by name and count; each arm's verbatim failure; D2/D5 choices; D4 totals; quality.sh
   total and its green line).
9. `feature_list.json`: no change — id 71 stays `in_review`, per the coordinator's own instruction.

### Resumed after reboot — A2's remaining half, the inotify class fix, and the clean full run

**A2's remaining half — DONE.** `OrdersCancelAcceptanceTests.cs`'s four bare
`GetProperty("note")` calls (`:380`, `:452`, `:506`, `:589` before this edit) are now
`TryGetProperty("note", out …)` + a message naming the expected note and dumping the payload
actually received — the identical shape the Gateway `[Theory]` already established (D1). No new
test method: the same four `[Fact]`s, same assertions on success, only the failure-path message
changed, exactly as the review's A2 and the pre-reboot record's "exact next step" both specified.

**Armed once** (M1 — the same read-back deletion as arming-table row 1/5, re-run here against the
newly-edited assertion): `EfCoreSagaCommandStore.FindOperatorCancelNoteAsync`'s final `return`
replaced with `return null;` (discarding both `ExtractOperatorCancelNote` calls to keep the build
clean), `dotnet build --no-incremental tests/Orders.IntegrationTests`, ran
`StockReserved_WithANote_TheNoteReachesTheCancelledOutboxRowAfterTheRealCompensationCompletes`:

```
Failed OrderToCash.Orders.IntegrationTests.OrdersCancelAcceptanceTests.StockReserved_WithANote_TheNoteReachesTheCancelledOutboxRowAfterTheRealCompensationCompletes [11 s]
  Error Message:
   expected the order.cancelled.v1 outbox payload to carry "note": "Buyer called to cancel before despatch.", but it carries no note key at all. payload: {"orderReference":"ORD-000001", … "compensationSteps":[{"step":"stock_released", …}]}
```

Names the missing note, never a bare `KeyNotFoundException` — satisfying A2. Restored from the
session's backup (`EfCoreSagaCommandStore.cs.bak_a2`), confirmed byte-identical:
`cmp src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs <backup> → IDENTICAL`, forced a
rebuild (`touch` + `dotnet build --no-incremental`), then ran `OrdersCancelAcceptanceTests`
(unfiltered, all 9 `[Fact]`s in the class) — **9/9 green**, 1 m 20 s. `dotnet format
--verify-no-changes`: clean (no output, exit 0).

**Instruction 4 (the inotify class) was superseded mid-task by the coordinator's own measurement,
sent while this section was in progress — the class was real (structural, item 4c), not reboot
residue, and the coordinator's replacement order (fix the class BEFORE the full run) is what was
followed. The original instruction 4's (a)/(b)/(d) sub-steps were therefore not executed in that
shape; the coordinator's own replacement measurement, fix and re-measurement are recorded below in
their place.**

**Diagnosis, measured, not assumed:** `max_user_instances` = 128. Pre-run floor (no build/test
process alive, freshly rebooted session): **115** inotify instances already open for this user —
confirmed **not** ours: `find /proc/[0-9]*/fd -lname 'anon_inode:inotify' | sed -E
's#/proc/([0-9]+)/.*#\1#' | sort -u` resolved to desktop-session processes only (gnome-shell,
tracker-miner, ibus-*, evolution-*, gvfs-*, kwalletd5, postman-agent, …) — none of them `dotnet`.
`grep -rnE "reloadConfigOnChange|reloadOnChange|DOTNET_hostBuilder__" src/ tests/ quality.sh
Directory.Build.props` (before the fix) returned nothing: no host in this repository disabled the
generic host's default `appsettings*.json` reload-on-change, and each `*Host.CreateBuilder` calls
`Host.CreateApplicationBuilder(args)`, which creates one `FileSystemWatcher` (one inotify
instance) per built host for that reload machinery. `tests/Gateway.IntegrationTests` alone builds
real hosts in 8 of its 26 files.

**Measured by change of kind — one project, same conditions, with and without the fix, both
sampled every 1 s (`find /proc/[0-9]*/fd -lname 'anon_inode:inotify' | wc -l`):**

| Run | Floor (desktop-only, first sample) | Peak | Delta above floor |
|---|---|---|---|
| `Gateway.IntegrationTests`, **without** the fix | 115 | **123** | +8 |
| `Gateway.IntegrationTests`, **with** the fix | 115 | **118** | +3 |

Both runs passed **59/59** (6 m 15 s / 6 m 13 s) — the fix changes nothing about test outcomes,
only the inotify instance count. A single project alone came within 5 of the 128 limit
unfixed; `quality.sh` runs roughly a dozen host-building projects in parallel, which is why the
pre-reboot run's `Billing.UnitTests` host construction hit the wall.

**The fix — smallest change covering every test host, chosen over the alternative:** rejected
exporting `DOTNET_hostBuilder__reloadConfigOnChange=false` only inside `quality.sh`, because the
coordinator's own instruction required covering the plain `dotnet test` path too, and an
environment export inside one script does not reach that path. Instead:
- `/test.runsettings` (new file, repo root): a `RunSettings` XML setting exactly that one
  environment variable for the test host process.
- `Directory.Build.props`: one new property, `<RunSettingsFilePath>$(MSBuildThisFileDirectory)test.runsettings</RunSettingsFilePath>`,
  added to the existing root `PropertyGroup` (verified `$(MSBuildThisFileDirectory)` resolves to
  the repo root regardless of which project imports it, via `dotnet msbuild
  tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj -getProperty:RunSettingsFilePath`
  → the absolute root path).

`RunSettingsFilePath` is read by the VSTest MSBuild tasks for **every** project's `dotnet test`
invocation — whole-solution (`quality.sh`'s own `dotnet test OrderToCash.sln`) or single-project —
so both paths the coordinator named are covered by one file, with no `quality.sh` change needed.
It is inert for `src/` (non-test) projects: the property is simply unused outside a VSTest run, so
`dotnet build` is unaffected — confirmed by `dotnet build` and `dotnet format --verify-no-changes`
both staying clean after the change.

**Why no test's proof is weakened:** a content-based enumeration (`grep -rn "reloadOnChange\|ReloadOnChange\|FileSystemWatcher\|appsettings" tests/ --include=*.cs`)
found no test in this repository that edits `appsettings*.json` at runtime and asserts the host
picked up the change, or that asserts `FileSystemWatcher`/reload behaviour at all — the property
this fix disables is not one any test's own proof depends on. Every touched project's build stayed
clean; no test file was edited by this fix.

**Never touched:** `fs.inotify.max_user_instances` itself — that is the user's machine, per the
coordinator's explicit instruction, and was not raised.

**The full, unfiltered `./quality.sh` run, with the fix in place, sampled every 5 s throughout:**

```
[OK]    dotnet format --verify-no-changes: clean
[OK]    dotnet build: succeeded
[OK]    dotnet test: all tests passed
[OK]    quality.sh finished
```

Pre-run floor: 115 (desktop-only, confirmed no build/test process alive first: `ps aux | grep -E
"dotnet (build|test|format)|quality.sh"` returned nothing). **Peak during the entire ~30-minute
run: 118** — 10 below the 128 limit, and only 3 above the desktop floor, consistent with the
single-project measurement above and nowhere near the ceiling. Outcome per the coordinator's own
framing: **the class was structural and is now fixed** — not merely "consistent with the
reboot-cleared explanation," since the same class of run (one host-heavy project) was measured
BOTH before and after the fix, on the SAME rebooted, otherwise-idle session, and only the fix
changed the number.

**Per-project totals, this run** (18 projects):

| Project | Total |
|---|---|
| SharedKernel.UnitTests | 50 |
| Cqrs.UnitTests | 23 |
| Contracts.UnitTests | 24 |
| Fulfillment.UnitTests | 130 |
| Notifications.UnitTests | 82 |
| Gateway.UnitTests | 211 |
| **Orders.UnitTests** | **444** |
| Billing.UnitTests | 238 |
| Seed.UnitTests | 44 |
| Seed.IntegrationTests | 6 |
| Notifications.IntegrationTests | 16 |
| Fulfillment.IntegrationTests | 64 |
| Billing.IntegrationTests | 90 |
| Projector.UnitTests | 120 |
| **Gateway.IntegrationTests** | **59** |
| Architecture.Tests | 25 |
| **Orders.IntegrationTests** | **135** |
| Projector.IntegrationTests | 59 |

Sum: 50+23+24+130+82+211+444+238+44+6+16+64+90+120+59+25+135+59 = **1820**.

**Reconciled exactly: 1820 = 1815 (round-1 baseline, this record's own "Verification" section
above) + 5 (the fix round's own new tests — "Review round 1 fix round — new tests," above).** The
three projects that changed match the fix round's own new-test enumeration exactly:
`Orders.UnitTests` 443 → 444 (+1, A1's `Topic_EqualsOrdersFactTopicName`),
`Orders.IntegrationTests` 134 → 135 (+1, D2's `EnqueueAsync_ADuplicateEnqueueWithADifferentEnvelope_LeavesTheFirstEnvelopeInPlace`),
`Gateway.IntegrationTests` 56 → 59 (+3, D1's `[Theory]`'s three `[InlineData]` cases). Every other
project's total is byte-identical to the round-1 baseline table. A2's own fix added no test (only
assertion messages), so it contributes zero to this delta, as predicted.

`./init.sh` after this run: **exit 0** — `[OK] init.sh: environment and state are coherent`, 0
features `in_progress` (correct: id 71 stays `in_review`, never re-opened to `in_progress` by this
session), backlog tripwire clean, shared-spec parity clean, `progress: 51/75 features done`
unchanged.

**Files touched by this resumed segment, beyond the paused session's own list:**
- `tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs` — A2's remaining half (four
  assertion-message changes, no new test).
- `test.runsettings` (new file, repo root) and `Directory.Build.props` (one new property) — the
  inotify class fix. Neither is under `src/`, `tests/` or `apps/web/`.
- This record.

No change to `feature_list.json`, `progress/current.md`, `specs/**`, or
`progress/review_operator_note_survives_the_compensation_branches.md` — per the coordinator's
explicit scope for this resumed segment. Id 71 stays `in_review`.

## Review round 2 fixes — D3/D6 stale comments, D7, A6, A1 count

Review round 2 (**REJECTED**) found one new blocking defect (D6, present since round 1) and three
required, non-blocking items (D7, A6, A1's own miscount). All four are text-only or single-assertion
changes; no test was added or removed. This section answers each in full, one line per fix, then the
enumeration, the arm, and verification.

### D6 (blocking) and D3 (folded into D6) — five stale comment blocks rewritten

The claim id 71 retires — *"an operator-cancel compensation row carries no triggering envelope"* —
was still asserted, false, in five comment blocks (two of them on the port whose nullability D5
accepted). **Enumerated on the retired claim's wording first**, per the reviewer's own two commands,
run again here before any edit to confirm the round-2 transcript reproduces:

```
$ find src tests -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n -i "RPC-triggered\|no triggering fact\|null-skip\|null alongside\|RPC triggered"
```
14 lines, matching the review's own count exactly (`review_…md:528-542`).

```
$ find src tests -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n -i "no triggering envelope\|nothing to republish\|carries no triggering\|with no triggering\|no triggering fact\|dead-letters nothing\|feature 27's A2\|null envelope\|envelope: null\|triggeringEventEnvelope: null"
```
16 lines, matching the review's own count exactly (`review_…md:544-561`).

**Rewrote the five false blocks**, each to state plainly that `CancelOrderCommandHandler`'s
operator-cancel rows carry the synthetic `orders.cancel.requested` envelope (id 71) and are
republished on first park like any other row, and that `null` means only a row that predates the
column, or (today, none) an enqueue site that genuinely supplies no envelope:
1. `src/Orders/Application/Ports/ISagaCommandStore.cs:16-20` (`SagaCommandRecord`'s summary).
2. `src/Orders/Application/Ports/ISagaCommandStore.cs:51-55`→ now `:56-76` (`EnqueueAsync`'s
   `triggeringEventEnvelope` param) — per D5's own correction, now also states that operator-cancel
   callers **must** supply the synthetic envelope, and that the parameter stays nullable only
   because `SagaFact.TriggeringEventEnvelope` (`SagaFact.cs:43`) is itself a defaulted `byte[]?`,
   never because #7's compile-time non-nullable guarantee (`saga-command-store.port.ts:40`, `:51`)
   is carried over — it is not, for any future caller.
3. `src/Orders/Infrastructure/Persistence/Entities/SagaCommand.cs:44-46` (the entity's doc comment).
4. `src/Orders/Infrastructure/Saga/SagaFirstParkDeadLetterHandler.cs:98-100` (the null-skip branch's
   comment) — the log message at `:107` (formerly `:102`) is untouched: it is a generic runtime
   message, true for the pre-feature/no-envelope case that remains.
5. `tests/Orders.UnitTests/SagaFirstParkDeadLetterHandlerTests.cs:118-120` (the comment on
   `OR3_AWinningClaimWithNoTriggeringEnvelope_AppendsTheFactButPublishesNoDlqCopy`) — the test body
   is UNCHANGED (it still fixtures a `null` envelope directly via the record constructor, which
   remains a valid case: a row that predates the column, or one whose enqueue site genuinely
   supplies none); only the comment's false claim that this represents `CancelOrderCommandHandler`'s
   rows is corrected.

**Also folded in, same edit class:** `OperatorCancelRequestedEnvelope.cs:59-61` — see A1, below.

**Re-ran both enumeration commands after the edit**, complete output:

```
$ find src tests -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n -i "RPC-triggered\|no triggering fact\|null-skip\|null alongside\|RPC triggered"
src/Orders/Application/Ports/ISagaCommandStore.cs:61:    /// operator-cancel compensation rows are RPC-triggered, not
src/Orders/Application/Sagas/SagaCommandRequestFactory.cs:39:    /// enqueue for the <c>stock_reserved</c> branch has no triggering fact at
src/Orders/Application/Sagas/SagaCommandRequestFactory.cs:40:    /// all (it is RPC-triggered) and does not call this method — it builds
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:17:/// <c>SagaFactHandler</c>, which this RPC-triggered enqueue is not) and,
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:21:/// not require this envelope (an operator cancel has no triggering fact in
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:85:/// <c>SagaFactsConsumer</c>/<c>SagaFactHandler</c>, which this RPC-triggered
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:164:    /// it is RPC-triggered, exactly the "own enqueue path" the factory's own
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:197:    /// for the fact-driven caller. This caller has no triggering fact at
src/Orders/Infrastructure/Persistence/Entities/SagaCommand.cs:47:    /// today). An RPC-triggered compensation row (an operator cancel,
```
9 lines (down from 14). Classification, every line:
- `ISagaCommandStore.cs:61` — the REWRITTEN param doc's own true statement ("RPC-triggered, not
  fact-triggered"). Accurate.
- `SagaCommandRequestFactory.cs:39-40` — pre-existing, about the request-PAYLOAD factory not being
  called by this path (true, unrelated to envelope presence). Accurate, untouched.
- `OperatorCancelRequestedEnvelope.cs:17`, `:21` — D3's round-1 correction (§4.1 for the column, no
  R29 claim). Accurate, untouched this round.
- `CancelOrderCommandHandler.cs:85`, `:164`, `:197` — D3's round-1 correction / true statements about
  the request-payload factory. Accurate, untouched this round.
- `SagaCommand.cs:47` — the REWRITTEN entity doc's own true statement. Accurate.

**No false "no envelope" claim about `CancelOrderCommandHandler`'s rows remains anywhere in this
list.**

```
$ find src tests -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n -i "no triggering envelope\|nothing to republish\|carries no triggering\|with no triggering\|no triggering fact\|dead-letters nothing\|feature 27's A2\|null envelope\|envelope: null\|triggeringEventEnvelope: null"
tests/Orders.IntegrationTests/SagaFirstParkDeadLetterTests.cs:55: … triggeringEventEnvelope: null, triggeringEventTopic: null, …
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:41: … triggeringEventEnvelope: null, triggeringEventTopic: null, …
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:78: … triggeringEventEnvelope: null, triggeringEventTopic: null, …
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:103: … triggeringEventEnvelope: null, triggeringEventTopic: null, …
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:106: … triggeringEventEnvelope: null, triggeringEventTopic: null, …
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:140: … triggeringEventEnvelope: null, triggeringEventTopic: null, …
tests/Orders.UnitTests/SagaFirstParkDeadLetterHandlerTests.cs:133: … TriggeringEventEnvelope: null, TriggeringEventTopic: null …
tests/Orders.UnitTests/SagaFactHandlerTests.cs:494: TriggeringEventEnvelope: null,
src/Orders/Infrastructure/Saga/SagaFirstParkDeadLetterHandler.cs:100: // envelope (none exists today) — nothing to republish. An
src/Orders/Infrastructure/Saga/SagaFirstParkDeadLetterHandler.cs:107: "SagaFirstParkDeadLetterHandler: row {CommandId} carries no triggering envelope; .dlq publish skipped.",
src/Orders/Application/Sagas/SagaCommandRequestFactory.cs:39: /// enqueue for the <c>stock_reserved</c> branch has no triggering fact at
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:21: /// not require this envelope (an operator cancel has no triggering fact in
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:197: /// for the fact-driven caller. This caller has no triggering fact at
```
13 lines (down from 16). Classification, every line:
- The eight `triggeringEventEnvelope`/`TriggeringEventEnvelope: null` fixture/seed literals
  (`SagaFirstParkDeadLetterTests.cs:55`, `SagaCommandStoreTests.cs:41`/`:78`/`:103`/`:106`/`:140`,
  `SagaFirstParkDeadLetterHandlerTests.cs:133`, `SagaFactHandlerTests.cs:494`) — pre-existing
  fact-less seeds and fixtures for OTHER tests (leases, rejection, dispatch), never a claim about
  operator cancels. Accurate, unrelated, untouched.
- `SagaFirstParkDeadLetterHandler.cs:100` — the REWRITTEN comment's own true statement. Accurate.
- `SagaFirstParkDeadLetterHandler.cs:107` — the pre-existing, generic runtime log message, true for
  the case that remains (a row that predates the column, or a future no-envelope site). Accurate,
  untouched.
- `SagaCommandRequestFactory.cs:39`, `OperatorCancelRequestedEnvelope.cs:21`,
  `CancelOrderCommandHandler.cs:197` — the same three true, unrelated statements already classified
  above. Accurate.

**No false claim survives either enumeration. D6 and D3 are both closed.**

### D7 — the record's D4 totals line corrected, per LINE

The original totals sentence (this record, in "#7's tests for this mechanism, enumerated by
content") mixed two units — assertions and lines — and summed to 19 only by coincidence. Restated
per LINE, matching the 19-line command output directly above it in that section:

**Corrected totals: 2 ported lines (items 7, 9 — the `eventType` assertions), 1 strengthened (item
8), 1 superseded (item 3), 15 not applicable (items 1, 2, 4, 5, 6, 10–19).**
2 + 1 + 1 + 15 = 19, reconciling exactly against the 19-line command output.
(Each ported line's paired `triggeringEventTopic` assertion — `cancel-order.handler.spec.ts:172`,
`:219` — is additional context cited in the same classification line, not itself one of the 19 hit
lines the command returned, so it is not double-counted in this per-line total.)

The outcome is unchanged: no guard was dropped in translation.

### A6 — the byte-for-byte claim made true, and armed on the mutation a note-only assertion cannot see

`tests/Orders.IntegrationTests/SagaCommandStoreTests.cs`'s
`EnqueueAsync_ADuplicateEnqueueWithADifferentEnvelope_LeavesTheFirstEnvelopeInPlace` claimed (its own
doc comment) that *"the first envelope's exact bytes must survive, byte-for-byte"*, but asserted only
the extracted note. **Fixed:** after the existing note assertion, the stored row's
`TriggeringEventEnvelope` column is read back directly (`assertDb.SagaCommands.AsNoTracking()`) and
compared byte-for-byte to `firstEnvelope` via `System.Text.Encoding.UTF8.GetBytes(...)`. The doc
comment now states both properties it guards.

**Armed with the exact mutation a note-only assertion cannot see: a duplicate enqueue that writes an
envelope carrying the SAME note but a different other field (`eventId`).** Two build/run cycles,
`cp` backup taken first (`EfCoreSagaCommandStore.cs.bak`, `SagaCommandStoreTests.cs.bak`), full
protocol (`dotnet build --no-incremental`, one named test, verbatim result, restore, `cmp`, forced
rebuild, confirming green):

**Production mutation (both cycles), `EfCoreSagaCommandStore.cs`'s duplicate-key `catch`:**
```csharp
db.Entry(row).State = EntityState.Detached;
// REVIEW PROBE A6: simulate a real overwrite on a duplicate enqueue.
await db.SagaCommands
    .Where(c => c.OrderId == orderId && c.Command == row.Command)
    .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.TriggeringEventEnvelope, row.TriggeringEventEnvelope), cancellationToken)
    .ConfigureAwait(false);
return EnqueueOutcome.AlreadyEnqueued;
```
(The same real-overwrite shape review round 2's own R2-4 used, reused here as the vehicle for a
different envelope payload.)

**Cycle A — reproduce the PRE-CHANGE test (note-only, no byte assertion) against this mutation, with
`secondEnvelope` rebuilt to carry the SAME note as `firstEnvelope` but a fresh `eventId`** (each call
to `BuildSyntheticEnvelopeBytes` mints a new `Guid` for `eventId`/`causationId`, so two calls with an
identical note string produce different bytes):
```csharp
var secondEnvelope = BuildSyntheticEnvelopeBytes(orderId, "the first envelope's own note");
// … byte-equality block commented out, reproducing the PRE-CHANGE (note-only) assertion set …
```
`dotnet build --no-incremental tests/Orders.IntegrationTests` → 0 Warning(s), 0 Error(s). Ran the one
named test:
```
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 3 s
```
**The pre-change, note-only test PASSES against this mutation** — proving the note-only assertion is
blind to a same-note/different-field overwrite, exactly as A6 named.

**Cycle B — re-enable the byte assertion, same mutation still in place, nothing else changed:**
```
dotnet build --no-incremental tests/Orders.IntegrationTests
Build succeeded. 0 Warning(s) 0 Error(s)
```
```
Failed OrderToCash.Orders.IntegrationTests.SagaCommandStoreTests.EnqueueAsync_ADuplicateEnqueueWithADifferentEnvelope_LeavesTheFirstEnvelopeInPlace [5 s]
  Error Message:
   Assert.Equal() Failure: Collections differ
                        ↓ (pos 12)
Expected: [···, 58, 34, 97, 54, 48, ···]
Actual:   [···, 58, 34, 100, 100, 50, ···]
                        ↑ (pos 12)
```
**Killed** — the byte assertion catches exactly the mutation the note-only assertion missed in Cycle
A.

**Restore:** both files `cp`-restored from their pre-mutation backups (taken immediately after the
byte assertion was added, before any probe), confirmed `cmp`-identical:
```
EfCoreSagaCommandStore.cs: IDENTICAL to backup
SagaCommandStoreTests.cs: IDENTICAL to backup
```
`grep -n "REVIEW PROBE" src tests` (recursive) → no hits — no probe marker left in either file.
Forced rebuild (`touch` both files, `dotnet build --no-incremental tests/Orders.IntegrationTests`):
0 Warning(s), 0 Error(s). Confirming green, the FULL `SagaCommandStoreTests` class (unfiltered):
```
Passed!  - Failed: 0, Passed: 12, Skipped: 0, Total: 12, Duration: 40 s
```

### A1 — the true count of Application → `Infrastructure.Messaging.Rpc` references

`OperatorCancelRequestedEnvelope.cs:59-61` said *"unlike the three pre-existing Application →
`Infrastructure.Messaging.Rpc` references"*, while the file's own `:2` imports that namespace for
`RpcJson` (`:48`) — miscounting by not counting itself. Enumerated, using directives and
fully-qualified names, bin/obj excluded by path:

```
$ find src/Orders/Application -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -nE "OrderToCash\.Orders\.Infrastructure\.Messaging\.Rpc"
src/Orders/Application/Ports/ISagaCommands.cs:1:using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
src/Orders/Application/Sagas/SagaCommandRequestFactory.cs:1:using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:2:using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:5:using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
```

**True count: FOUR**, all `using` directives, no other fully-qualified reference anywhere in
`src/Orders/Application`: `ISagaCommands.cs`, `SagaCommandRequestFactory.cs`,
`OperatorCancelRequestedEnvelope.cs` (this file, previously uncounted) and
`CancelOrderCommandHandler.cs`. **Fixed:** `OperatorCancelRequestedEnvelope.cs:59-66`'s doc comment
now states "FOUR", names all four files including itself, and explains the miscount was this
comment's own prior wording not counting its own file's import. All four remain unrefactored,
routed to backlog id 76 (`application_layer_depends_on_infrastructure_unguarded`) exactly as review
round 1 and round 2 both direct — **not touched by this fix round**, per the leader's, not the
implementer's, ownership of that entry (round 2's own ruling on A1's remainder).

### Verification

- **Test count unchanged — no test added or removed, only comments and one assertion strengthened.**
  `[Fact]`/`[Theory]` counts in every touched file, before and after this round's edits:
  - `SagaCommandStoreTests.cs`: **12** (unchanged — the one existing test's body gained an assertion).
  - `SagaFirstParkDeadLetterHandlerTests.cs`: **4** (unchanged — comment only).
  - `ISagaCommandStore.cs`, `SagaCommand.cs`, `SagaFirstParkDeadLetterHandler.cs`,
    `OperatorCancelRequestedEnvelope.cs`: production files, no `[Fact]`/`[Theory]`.
  - The suite total therefore stays **1820** (round-1 fix round's own reconciled total, above) — not
    independently re-summed by a full `quality.sh` run this round, per the brief ("a full quality.sh
    is NOT required: only comments and one assertion change").
- `dotnet build --no-incremental OrderToCash.sln` (full solution, after all edits, mutation restored
  and re-confirmed first): **Build succeeded, 0 Warning(s), 0 Error(s)** — every rewritten `<see
  cref>` resolves.
- `dotnet format --verify-no-changes`: clean, no output, exit 0.
- Filtered runs, all green:
  - `SagaCommandStoreTests` (`Orders.IntegrationTests`): **12/12**.
  - `SagaFirstParkDeadLetterHandlerTests` + `OperatorCancelRequestedEnvelopeTests` +
    `CancelOrderCommandHandlerTests` (`Orders.UnitTests`, one filtered run): **18/18**.
  - `SagaFirstParkDeadLetterTests` (`Orders.IntegrationTests`): **1/1**.
- `./init.sh`: **exit 0** — `[OK] init.sh: environment and state are coherent`, 0 features
  `in_progress`, backlog tripwire clean, shared-spec parity clean, `progress: 51/76 features done`.
- No two `dotnet build`/`test`/`format` processes overlapped: `ps aux | grep -E "dotnet
  (build|test|format)|quality.sh"` (never `pgrep -f`) checked clear before every build in this
  round.

**Files touched this round:**
- `src/Orders/Application/Ports/ISagaCommandStore.cs` — D6 blocks 1 and 2 rewritten.
- `src/Orders/Infrastructure/Persistence/Entities/SagaCommand.cs` — D6 block 3 rewritten.
- `src/Orders/Infrastructure/Saga/SagaFirstParkDeadLetterHandler.cs` — D6 block 4 rewritten.
- `tests/Orders.UnitTests/SagaFirstParkDeadLetterHandlerTests.cs` — D6 block 5's comment rewritten
  (test body unchanged).
- `src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs` — A1's count corrected.
- `tests/Orders.IntegrationTests/SagaCommandStoreTests.cs` — A6's byte-equality assertion added.
- This record.

No change to `feature_list.json`, `progress/current.md`, `specs/**`, or
`progress/review_operator_note_survives_the_compensation_branches.md`. Id 71 stays `in_review`.
