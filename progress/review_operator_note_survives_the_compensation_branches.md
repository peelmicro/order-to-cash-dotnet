# Review — `operator_note_survives_the_compensation_branches` (id 71, phase 14) — round 1

**Verdict: REJECTED.** Status left at `in_review`, as the dispatching brief directs; the transition is the leader's.

**Where this leaves the feature.** The mechanism is correct and well guarded at the Orders boundary: 7 of 8 mutation probes were killed. Two blocking defects stop it:
- **D1** — bullets 1–2 are not met. No compensation branch proves the note on the read-model timeline. The reason given for stopping at the outbox (that a real Billing host would be needed) is false: feature 66's own harness disproves it.
- **D2** — ledger row 3 is wrong in both halves. Its mechanism describes an envelope overwrite that no code performs. Its named guard stayed green when the precedence it is named for was reversed (probe M7).

**Also required before re-review:**
- **D3** — bullet 7's `design.md §4.2` misattribution was moved, not removed.
- **D4** — the #7 guard enumeration filtered by filename.
- **D5** — ledger row 1 gives the wrong reason for the port staying nullable.

**Recommendation:** one fix round, then re-review. D1 is one `[Theory]` in an existing harness. D2–D5 are one test decision plus text corrections.

## What I ran, and what I did not

- **No full re-run.** The claims under test are per-feature. I re-summed `scratchpad/quality_id71_rerun.log` (08:19) from its 18 `Passed!` lines: **1815**, 0 `Failed!`. That matches the record and the leader.
- **Eight mutation probes**, one mutation per `dotnet build --no-incremental`, one named test each. Each was restored from a `cp` backup (`scratchpad/rev71/`), checked `cmp`-identical and `touch`ed. After the last restore came a forced rebuild of both Orders test projects, then a confirming green run: `CancelOrderCommandHandlerTests` **13/13**, and the 8 probed integration tests **8/8** (08:38). Logs are in `scratchpad/rev71/m{1..8}_*.log` and `final_*.log`.
- `./init.sh`: exit 0. It reported no feature `in_progress`, a clean backlog tripwire, and the shared spec byte-identical across 6 files (`test-matrix.md` exempt).
- **Process disclosure:** the `pgrep -fl "dotnet (build|test|format)"` in my final restore command printed `825600 bash`. That was its own shell's command line, the self-match `CLAUDE.md` names. It gated nothing: every probe ran in the foreground and had exited before the next started.
- **Not probed:**
  - a fabrication mutation against bullet 3's absence guards (A5);
  - a corruption of the `.dlq` publish itself, which is feature 27's code, guarded by its own L15 test.

## Probe table

| # | Family | Mutation | Site | Named test | Result |
|---|---|---|---|---|---|
| M1 | deletion | `FindOperatorCancelNoteAsync` returns `null` | `EfCoreSagaCommandStore.cs:308` | `SagaCommandStoreTests.FindOperatorCancelNoteAsync_CreditReleaseRowCarriesTheSyntheticEnvelope_ReturnsItsExactNote` | **killed** — `Assert.Equal() Failure: Strings differ / Expected: "Reverse-order compensation — credit relea"··· / Actual: null` |
| M2 | corruption | `note.GetString()` → `"CORRUPTED"` | `EfCoreSagaCommandStore.cs:336` | `SagaCommandStoreTests.FindOperatorCancelNoteAsync_StockReleaseRowCarriesTheSyntheticEnvelope_ReturnsItsExactNote` | **killed** — `Expected: "Cancel while stock_reserved — buyer chang"··· / Actual: "CORRUPTED"` |
| M3 | substitution | command tokens → real siblings `credit.hold` / `stock.reserve` | `EfCoreSagaCommandStore.cs:295-296` | `SagaCommandStoreTests.FindOperatorCancelNoteAsync_PrefersCreditReleaseOverStockRelease_WhenBothRowsExistForTheOrder` | **killed** — `Expected: "Credit released first — the canonical car"··· / Actual: null` |
| M4 | deletion (parity) | `stock.release` site: envelope and topic → `null` | `CancelOrderCommandHandler.cs:204`, `:213` | `OrdersCancelAcceptanceTests.StockReserved_OperatorCancelCompensationParks_PublishesADlqCopyByteEqualToTheStoredEnvelope` | **killed** — `Assert.NotNull() Failure: Value is null` |
| M5 | deletion, the other enqueue site | `credit.release` site: envelope → `null` | `CancelOrderCommandHandler.cs:172` | `CancelOrderCommandHandlerTests.CreditApprovedOrConfirmed_EnqueuesCreditReleaseOnly_StatusUnchangedAndBothReleasesPlannedInReverseOrderOfAcquisition` | **killed**, both `[InlineData]` cases — `Assert.NotNull() Failure: Value is null` |
| M6 | substitution (**not in the record**) | where the envelope is read, read the sibling `payload` column: `TriggeringEventEnvelope = c.Payload` | `EfCoreSagaCommandStore.cs:301` | `OrdersCancelAcceptanceTests.StockReserved_WithANote_TheNoteReachesTheCancelledOutboxRowAfterTheRealCompensationCompletes` | **killed** — `KeyNotFoundException: The given key was not present in the dictionary.`, thrown at the test's own `GetProperty("note")` (`OrdersCancelAcceptanceTests.cs:380`). This proves the end-to-end outbox test executes the store's read. The message does not name the note (A2) |
| M7 | substitution of ordering (**not in the record**) | precedence reversed to `stock.release ?? credit.release` | `EfCoreSagaCommandStore.cs:308` | `SagaCommandStoreTests.FindOperatorCancelNoteAsync_PrefersCreditReleaseOverStockRelease_WhenBothRowsExistForTheOrder` | **SURVIVED — 1/1 green** (D2) |
| M8 | payload corruption of the stored envelope (**not in the record**) | envelope `correlationId` → the request id | `OperatorCancelRequestedEnvelope.cs:40` | `CancelOrderCommandHandlerTests.StockReserved_EnqueuesStockReleaseWithReasonOrderCancelled_StatusUnchangedAndOnlyStockReleasePlanned` | **killed** — `Assert.Equal() Failure: Values differ`, at `:159` (the `CorrelationId` assertion) |

The record's four arms (`progress/impl_…md:199-202`) reproduce exactly.

## Acceptance bullet → test mapping (id 71 introduces no `R<n>`; `sdd: false`)

| Bullet (unit) | Tests | Verdict |
|---|---|---|
| 1–2 — note on the **read-model timeline** after the compensation chain, end to end (per branch ×3) | `stock_reserved`: `OrdersCancelAcceptanceTests.StockReserved_WithANote_…` (outbox row). `confirmed`: `Confirmed_WithANote_…` (outbox row). `credit_approved`: `CreditApproved_WithANote_TheEnqueuedCreditReleaseRowCarriesItInItsRealStoredEnvelope` (enqueued row only) | **NOT MET** — D1 |
| 3 — saga-decided cancellation carries no note and no key (per status ×2) | `stock_rejected`: `SagaCompensationStockRejectedTests.cs:74` (**integration**, real outbox payload, `DoesNotContain("\"note\"")`), plus unit `SagaFactHandlerTests.StockRejectedV1_DirectCancel_CarriesNoOperatorNoteWhenTheStoreHasNone`. `credit_rejected`: `SagaCompensationCreditRejectedTests.cs:113` (**integration**), plus unit `CreditRejectedV1_ThenStockReleasedV1_CarriesNoOperatorNoteWhenTheStoreHasNone` | **MET** per status. The brief understated this: `stock_rejected` does **not** rest on the unit test and the serializer guard alone — it has its own integration assertion at `:74` |
| 4 — armed in three families; the substitution's message names what broke | M1, M2 and M3 reproduced; M6 added. M3's message names the lost note, which is the retention claim | **MET** |
| 5 — the triggering fact's bytes stored while the row is open | Column from feature 27's `20260910091952_AddSagaCommandsDeadLetterColumns`; the write is at `EfCoreSagaCommandStore.cs:45` | **MET** (pre-existing) |
| 6 — synthetic `orders.cancel.requested` envelope and orders facts topic (per enqueue site ×2) | `stock.release` site: `CancelOrderCommandHandlerTests.cs:153-164` (topic `:154`, eventType `:157`, ids `:158-161`, payload `:162-164`), plus omission test `StockReserved_NoNoteSupplied_TheSyntheticEnvelopeOmitsTheNoteKeyEntirely` (`:192`). `credit.release` site: `:261-271` (topic `:262`, eventType `:265`). M4, M5 and M8 killed | **MET** |
| 7 — guards ported, comments corrected, `.dlq` byte-equal test, ledger row | #7's `cancel-order.handler.spec.ts:172`/`:178` and `:219`/`:220` ported at `:154`/`:157` and `:262`/`:265`. Old `Assert.Null` gone (below). `.dlq` test present, byte-equal, armed (M4). **Comment correction incomplete (D3). Ledger row 3 wrong (D2); row 1 reason wrong (D5)** | **PARTIAL** |

Old assertions gone, as search results over a superset (obj included) with no hits:
```
$ grep -rn "Assert.Null(triggeringFact" tests --include=*.cs ; echo "exit=$?"
exit=1
$ grep -rn "RPC-triggered — no triggering" src tests --include=*.cs ; echo "exit=$?"
exit=1
```

**The `.dlq` test (item 5).** It asserts `Assert.Equal(storedEnvelopeBytes, dlqMessage!.Message.Value)` over byte arrays: byte equality, not presence. It then checks `eventType` and the exact note. It selects its message **by content** (`ConsumeMatchingAsync` matches `correlationId == orderId`), not positionally, so id 74's positional-read class does not apply to it.

## D1 — bullets 1–2: the note is not proved on the timeline for any compensation branch (blocking)

**Bullets 1–2**, per branch ×3: the note lands *"on the read-model timeline entry after the real compensation chain completes, proved end to end"*.

**Delivered:** `StockReserved_WithANote…` (`OrdersCancelAcceptanceTests.cs:343`) and `Confirmed_WithANote…` (`:396`) stop at the `order.cancelled.v1` outbox row. `CreditApproved_WithANote…` (`:474`) stops at the enqueued `credit.release` row. No branch reaches Mongo.

### Ruling (c), first because (a) rests on it: the "real Billing host" premise is false

The record (`:81-85`) says a timeline test would need *"booting a real Billing host inside `Gateway.IntegrationTests` (no existing `ProjectReference`, no precedent) or duplicating Orders.IntegrationTests' stand-in-responder machinery"*. Neither is needed. `Gateway.IntegrationTests` already has every piece:
- `Gateway.IntegrationTests.csproj:46-73` references `src/Orders`, `src/Projector` and `src/Fulfillment`.
- `OperatorNoteReachesTimelineEndToEndTests.cs` boots the real `OrdersHost` (`StartOrdersAsync`) and the real `ProjectorHost` (`StartProjectorAsync`).
  - It seeds the order through the running host's own `IOrderRepository`/`IUnitOfWork` (`SeedPlacedOrderAsync`).
  - **The file the record cites as the pattern is the precedent the record says does not exist.**
- `tests/Gateway.IntegrationTests/StandInResponder.cs:17` is a **`public`** real-NATS stand-in for any subject, so the `internal` Orders stand-ins are irrelevant.
- The collection already carries `KafkaContainerFixture` (`OperatorNoteEndToEndCollection`), so publishing `credit.released.v1` or `stock.released.v1` is one producer call.
- Billing's only roles in these chains are answering `billing.credit.release` and emitting `credit.released.v1`.
  - The saga step fires on the **consumed fact** (`SagaStepTable.cs:192-196`, `:229-236`).
  - That is exactly how the delivered Orders tests already drive it, by publishing the facts themselves.

### Ruling (a): outbox row plus feature 66's projector test does not satisfy "on the read-model timeline, proved end to end"

The literal reading binds, for two reasons.

1. **The bullet names the proof boundary, and names it knowingly.** Id 71 is *"the unqualified half of id 66's bullet 1"*, and id 66 was closed by exactly this four-service shape. Moving the boundary to the outbox was a scope change the implementer made alone, on a premise ruled false above.
2. **The composition's "branch-agnostic" claim is not true of the Mongo write.**
   - The mapping *code* is shared (`src/Projector/Domain/Summaries.cs:89-103`), but it writes `compensationSteps` into the same `detail` document as `note`.
   - Only the compensation branches make that list non-empty.
   - **No integration test projects a non-empty `compensationSteps` into Mongo.** Feature 66 proved the bytes of an entry with `compensationSteps: []`, which are not the bytes these branches produce.

   Search result:

```
$ find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -ln "compensationSteps\|CompensationSteps" | grep -i "projector\|gateway"
tests/Projector.UnitTests/SummariesTests.cs
tests/Projector.IntegrationTests/TimelineProjectionTests.cs
tests/Projector.IntegrationTests/TestSupport/EnvelopeBuilders.cs

$ grep -n "compensationSteps\|CompensationSteps" tests/Projector.IntegrationTests/TimelineProjectionTests.cs tests/Projector.IntegrationTests/TestSupport/EnvelopeBuilders.cs
tests/Projector.IntegrationTests/TestSupport/EnvelopeBuilders.cs:158:        string cancellationReason = "buyer_requested", IReadOnlyList<CompensationStep>? compensationSteps = null,
tests/Projector.IntegrationTests/TestSupport/EnvelopeBuilders.cs:165:            when, new OrderCancelledPayload(orderReference, retailerCode, companyCode, cancellationReason, when, compensationSteps ?? [], note));
tests/Projector.IntegrationTests/TimelineProjectionTests.cs:145:            "\"cancellationReason\":\"stock_rejected\",\"cancelledAt\":\"2025-06-01T00:00:00.000Z\",\"compensationSteps\":[]}}";
```

The second stage of the first command filters `-l` output, which is paths only, so it cannot drop a content hit. Classification:
- `SummariesTests.cs` is a unit test. It asserts the dictionary value (`:146-150`) and never reaches BSON.
- `EnvelopeBuilders.cs:158`, `:165` is a parameter defaulting to `[]`, and no caller in the listed files passes it.
- `TimelineProjectionTests.cs:145` is a literal empty list.

### Ruling (b): `credit_approved` stopping at the enqueue

At the Orders-integration level alone this would be tolerable, for three reasons:
- The `credit_approved` variants are distinct step-table rows (`SagaStepTable.cs:195`, `:235`), but both are exercised by the unit `[Theory]` `SagaFactHandlerTests.CreditApprovedOrConfirmedVariant_StockReleasedV1_ReadsTheOperatorNoteBackFromTheStore_AndThreadsItOntoOrderCancelled` (`:386`).
- The chain after `credit.release` is identical code to `confirmed`.
- EF-seeding a status the natural flow never rests at has precedent in the same test class.

Under (a) it is **moot**: once the end-to-end test exists it is a `[Theory]`, and the third case costs one inline row.

### The test shape required

In `tests/Gateway.IntegrationTests/OperatorNoteReachesTimelineEndToEndTests.cs`, with the same collection and fixtures, write one `[Theory]` over `stock_reserved`, `credit_approved` and `confirmed`:
1. **Setup.** Start the real `OrdersHost` and `ProjectorHost` as feature 66's test does, and seed a placed order with `SeedPlacedOrderAsync`.
   - Move it to the starting status. A direct EF status update is acceptable for all three, on the precedent of `CreditApproved_WithANote…`.
   - Driving `stock_reserved`/`confirmed` with published facts, plus `StandInResponder`s for `fulfillment.stock.reserve`/`billing.credit.hold`, is equally acceptable.
   - **No real Billing or Fulfillment host is required.**
2. **Cancel.** `POST /orders/{id}/cancel` with a bracketed note through `GatewayTestHost`: real Gateway → real NATS → real Orders `orders.cancel` responder.
3. **Stand-ins.** `StandInResponder`s for `fulfillment.stock.release` and, on the credit branches, `billing.credit.release`.
4. **Facts.**
   - On the credit branches, poll the `credit.release` row to `sent`, then publish `credit.released.v1`.
   - Then poll `stock.release` to `sent` and publish `stock.released.v1`.
   - Key both by order id, on the real topics.
5. **Assertions.** Poll Mongo for the `order.cancelled.v1` timeline entry and assert two things:
   - **`detail.note` equals the supplied note exactly.**
   - **`detail.compensationSteps` has the branch's length**: 1 for `stock_reserved`, 2 for the credit branches.

   The second assertion proves the entry came from the compensation chain. Without it, a regression into the immediate branch would pass, since that branch carries the note with an empty list.
6. **Failure messages.** Read the note with `TryGetProperty`/`Contains` plus a message naming the note, not bare `GetProperty` (A2).
7. **Arm it.**
   - M1 (the store read returns `null`) must fail every case with a message naming the note.
   - Run one corruption probe on the store read or the Projector's `detail` write through the new test.
   - Record both in the record.

The existing Orders-level outbox tests may stay. They are good tests of a boundary, just not of the boundary the bullets name.

## D2 — ledger row 3: the mechanism is invented and the guard cannot see its property (blocking)

**The mechanism half.** Row 3 (`progress/impl_…md:102`) says *"`stock.release`'s row IS re-enqueued by the credit-held chain's second hop and its envelope is overwritten with the intermediate `credit.released.v1` fact's own bytes"*. No such write exists:

```
$ find src/Orders -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/Migrations/*' -print0 | xargs -0 grep -n "TriggeringEventEnvelope\s*="
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:45:            TriggeringEventEnvelope = triggeringEventEnvelope is null ? null : System.Text.Encoding.UTF8.GetString(triggeringEventEnvelope),
src/Orders/Application/Ports/ISagaCommandStore.cs:33:    byte[]? TriggeringEventEnvelope = null,
src/Orders/Application/Sagas/SagaFact.cs:43:    byte[]? TriggeringEventEnvelope = null,
```

Classification:
- `:45` is the only write, inside the **INSERT** in `EnqueueAsync`. A duplicate `(order_id, command)` is caught at `:60-67`, detached, and returns `AlreadyEnqueued`, **leaving the existing row untouched**.
- `ISagaCommandStore.cs:33` and `SagaFact.cs:43` are record parameter defaults, not writes.

In the credit-held chain no `stock.release` row exists before `credit.released.v1` arrives: the forward command was `stock.reserve`. That row is inserted once, carrying the fact's bytes. Nothing is re-enqueued and nothing is overwritten. The true invariant is simpler and stronger: **no code path ever rewrites an envelope.**

**The wrong mechanism has reached production source and tests.** Enumerated on the *retired* wording, scoped by path, unfiltered:

```
$ find src/Orders tests/Orders.UnitTests tests/Orders.IntegrationTests progress/impl_operator_note_survives_the_compensation_branches.md -type f \( -name '*.cs' -o -name '*.md' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "overwrit\|re-enqueue\|reenqueue"
progress/impl_operator_note_survives_the_compensation_branches.md:22:   first (never overwritten — the credit-held branches' canonical carrier), then `stock.release`
progress/impl_operator_note_survives_the_compensation_branches.md:102:| The invariant that lets one column serve both R29's dead-letter need and the note: `credit.release`'s row is never re-enqueued, so its envelope survives; `stock.release`'s row IS re-enqueued by the credit-held chain's second hop and its envelope is overwritten with the intermediate `credit.released.v1` fact's own bytes (design.md §4.2's existing "the fact that owed THIS row" semantics, left unchanged) | … |
tests/Orders.UnitTests/CancelOrderCommandHandlerTests.cs:259:        // NEVER gets overwritten by the later stock.release re-enqueue, so
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:268:    /// <c>stock.release</c> row's own envelope has already been overwritten
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:271:    /// at all — and <c>credit.release</c>'s row, never re-enqueued, is still
src/Orders/Domain/Order.cs:70:    /// <summary>Present iff <see cref="Status"/> is <see cref="OrderStatus.Cancelled"/> (O6). Immutable once set — … before it could overwrite it.</summary>
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:190:    /// <c>attempts</c> (never overwrites — the count must survive every
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:212:        // dispatcher has already reported `sent` is never overwritten
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:235:    /// (never overwrites <c>attempts</c>), but sets <c>next_attempt_at</c>
src/Orders/Application/Ports/ISagaCommandStore.cs:92:    /// overwritten <c>parked</c>. Returns <see langword="true"/> iff THIS
src/Orders/Application/Ports/ISagaCommandStore.cs:136:    /// <c>orders.cancel.requested</c> envelope and is NEVER re-enqueued, so
src/Orders/Application/Ports/ISagaCommandStore.cs:137:    /// its own envelope is never overwritten — unlike the <c>stock.release</c>
src/Orders/Application/Ports/ISagaCommandStore.cs:139:    /// overwritten with <c>credit.released.v1</c>'s OWN bytes (a real fact,
```

Two long lines are elided with `…` here only; 14 hits in total. Classification:
- **Wrong mechanism, must be rewritten (8):** record `:22` and `:102`; `CancelOrderCommandHandlerTests.cs:259`; `SagaCommandStoreTests.cs:268` and `:271`; `ISagaCommandStore.cs:136`, `:137` and `:139`.
- **Unrelated (6):**
  - `Order.cs:70` — cancellation-reason immutability;
  - `EfCoreSagaCommandStore.cs:190` and `:235` — the attempts count;
  - `EfCoreSagaCommandStore.cs:212` — `sent` status;
  - `ISagaCommandStore.cs:92` — `parked` status.

**The guard half.** Row 3 names `FindOperatorCancelNoteAsync_PrefersCreditReleaseOverStockRelease_WhenBothRowsExistForTheOrder` as the guard for *"checks `credit.release` before `stock.release`"*, and **M7 reversed that precedence with the test green**. The test's `stock.release` row carries a real `credit.released.v1` envelope with no note, so the extraction returns `null` in either order.

The test therefore guards something real — a note-less real envelope does not mask the `credit.release` note, which M3 confirms from the other side — but not the preference its name and the row claim. The ordering is observable only when **both** rows carry a synthetic envelope. The port's own doc comment (`ISagaCommandStore.cs:146-147`) says that never happens. It can happen in id 62's race (A3).

**What must change:**
- **Rewrite the eight lines** to the true invariant. The only write of `triggering_event_envelope` is the enqueue's INSERT; a duplicate enqueue leaves the row untouched. Enumerate again on the retired wording (`overwrit`, `re-enqueue`) after the edit, and record the command and its complete output.
- **Settle the precedence, one of two ways:**
  - **Either** keep the ordering claim and give it a guard that can fail: both rows carry a synthetic envelope with **different** notes, asserting the `credit.release` note wins, armed with M7.
  - **Or** declare the ordering irrelevant, and remove the claim from the test name, the port doc comment and the row, renaming the test to what it does guard.
- **Guard the rewritten row's invariant.** It must name a guard for the property it now claims, for example that a duplicate enqueue with a different envelope leaves the first envelope in place, armed by making the duplicate path update the row.

## D3 — the `design.md §4.2` misattribution was moved, not removed (required)

Bullet 7 asks that the §4.2 attributions be corrected, *"since §4.2 (design.md:191-193) speaks only of fact-triggered threading"*.
- **What was removed:** the `null` sites and their comment. `RPC-triggered — no triggering` returns no hits (above).
- **What remains:** the replacement text re-attaches §4.2, and R29, to the synthetic envelope:
  - `CancelOrderCommandHandler.cs:82-84` — *"the synthetic `orders.cancel.requested` envelope R29's dead-letter clause already threads through `saga_commands.triggering_event_envelope` (design.md §4.2)"*.
  - `OperatorCancelRequestedEnvelope.cs:15-16` — *"only stored in `saga_commands.triggering_event_envelope` (R29's dead-letter clause, design.md §4.2)"*.
- **Why that is wrong:**
  - The columns are §4.1 of `specs/observability_reliability/design.md`, the section ending just above `:191`.
  - §4.2 threads **fact** bytes through `SagaFactsConsumer`/`SagaFactHandler`.
  - The record itself says (`:212-214`) that the synthetic envelope is *"#7's own named diagnostic convention, not a requirement"* of R29.
- **Fix:** cite §4.1 for the column and #7's `cancel-order.handler.ts:272-286` for the synthetic envelope, and drop the claim that R29 threads it. The two lines above are where I read it, not a completeness claim. Enumerate `§4.2` across `src/Orders` and `tests/Orders.*` and classify every hit; `ISagaCommandStore.cs:140-141` and `SagaCommandStoreTests.cs:269-270` are already inside D2's rewrite.

## D4 — the #7 guard enumeration filtered by filename (required, record-only)

The record (`:113-124`) presents a content search, but its command names **nine files** by path, and 12 of #7's 21 hit lines are in files outside that list. That is the disguise `CLAUDE.md` names: a filename filter where the claim is about content. Re-run by content over all of #7's spec files, with `node_modules`/`dist` excluded by path:

```
$ cd order-to-cash-nestjs && find . -type f -name '*.ts' -not -path '*/node_modules/*' -not -path '*/dist/*' \( -name '*.spec.ts' -o -name '*.test.ts' \) -print0 | xargs -0 grep -n "triggeringEventEnvelope\|orders\.cancel\.requested\|\bnote\b"
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
./apps/projector/src/infrastructure/persistence/delta-to-pipeline.spec.ts:70:    // note) — cancellationReason's guard is independent of that.
./apps/orders/src/infrastructure/messaging/bare-json-nats.parity.spec.ts:24:// by execution below (see this file's own header note on arming) and
./apps/orders/src/infrastructure/messaging/bare-json-nats.parity.spec.ts:282:    // own header note on why a \b-bounded pattern misses compound
./apps/orders/src/infrastructure/messaging/idempotent-consumer.parity.spec.ts:505:      // service's source — see this file's own header note on scope), so
./apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:42:    triggeringEventEnvelope: triggeringEnvelope(),
./apps/orders/src/infrastructure/saga/saga-command-sweeper.spec.ts:42:    triggeringEventEnvelope: {
./apps/orders/src/infrastructure/saga/saga-command-dispatcher-log-trace-id.spec.ts:51:    triggeringEventEnvelope: triggeringEnvelope(),
./apps/orders/src/infrastructure/saga/saga-first-park-dead-letter-handler-log-trace-id.spec.ts:66:    triggeringEventEnvelope: {
./apps/orders/src/infrastructure/saga/saga-command-sweeper-log-trace-id.spec.ts:47:    triggeringEventEnvelope: {
```

Hits the record did not classify:
- **`gateway/orders.integration.spec.ts:234`** supplies a note through the Gateway and asserts only `status`, `compensationPlanned` and `x-correlation-id`, nothing about the note. **Not applicable.**
- **Envelope fixtures in fact-triggered or dead-letter specs, asserting nothing about operator cancels. Not applicable:**
  - `saga-command-retry.integration.spec.ts:151`;
  - `saga-command-dispatcher.spec.ts:42`;
  - `saga-command-sweeper.spec.ts:42`;
  - `saga-command-dispatcher-log-trace-id.spec.ts:51`;
  - `saga-command-sweeper-log-trace-id.spec.ts:47`.
- **The word "note" in prose. Not applicable:**
  - `health-probes.integration.spec.ts:36`;
  - `auth-rate-limit.integration.spec.ts:135`;
  - `place.accessibility.spec.ts:13`;
  - `delta-to-pipeline.spec.ts:70`;
  - `bare-json-nats.parity.spec.ts:24` and `:282`;
  - `idempotent-consumer.parity.spec.ts:505`.

**The pattern also missed a guard on the mechanism.** `triggeringEventTopic` is absent from it, and #7 asserts the topic at `cancel-order.handler.spec.ts:172` and `:219`. Both **are** ported, at `CancelOrderCommandHandlerTests.cs:154` and `:262`.

**Outcome: no guard was dropped in translation.** #7's only dead-letter integration spec for saga commands (`saga-command-dead-letter.integration.spec.ts:86`) is fact-triggered, and #8's operator-cancel `.dlq` test is additional. The defect is the record's method claim. Replace its section with this enumeration and one classification line per hit.

## D5 — ledger row 1 gives the wrong reason the port stays nullable (required, record-only)

**Ruling on the decision:** keeping the envelope parameter nullable is **acceptable for this feature**. Both operator-cancel enqueue sites are guarded and armed, at unit level (M5, M8) and at integration level (M4).

**The row's reason is wrong.** It says the port stays nullable because *"existing rows predate the columns"*.
- That justifies only the **read** side: the column and `SagaCommandRecord` (`specs/observability_reliability/design.md` §4.1, *"Nullable, because every row already in the table predates them"*).
- The **write** parameter is nullable because `SagaFact.TriggeringEventEnvelope` is a defaulted `byte[]?` (`SagaFact.cs:43`), passed straight through at `SagaFactHandler.cs:140`, *"so tests that do not care about dead-lettering are not forced to supply it"*.
- #7's port refuses at compile time any enqueue without an envelope (`saga-command-store.port.ts:40`). **#8 does not supply that property for any future caller.** It supplies site-by-site guards for the two sites that exist today.

The row must say that plainly. Its history half is otherwise correct (citations below).

## Verified correct — the ledger's history halves (item 6)

**Row 1.**
- #7's `cancel-order.handler.ts:175` and `:234` call `buildTriggeringEnvelope` (`:272-286`), which carries `note` when present (`:283`).
- #7's port declares `triggeringEventEnvelope: Envelope` non-nullable at `saga-command-store.port.ts:40` and `:51`.
- All read from #7's checkout at HEAD `bf45af0`.

**Row 2 — #7 never reads the note back.** Search over #7's non-spec sources:

```
$ find apps libs -type f -name '*.ts' -not -path '*/node_modules/*' -not -path '*/dist/*' -print0 | xargs -0 grep -n "triggeringEventEnvelope" | grep -v "\.spec\.ts:"
apps/orders/src/application/saga-fact-handler.ts:153:            triggeringEventEnvelope: envelope,
apps/orders/src/application/cancel-order.handler.ts:175:      triggeringEventEnvelope: this.buildTriggeringEnvelope(orderId, requestId, note),
apps/orders/src/application/cancel-order.handler.ts:234:      triggeringEventEnvelope: this.buildTriggeringEnvelope(orderId, requestId, note),
apps/orders/src/application/ports/saga-command-store.port.ts:40:  readonly triggeringEventEnvelope: Envelope;
apps/orders/src/application/ports/saga-command-store.port.ts:51:  readonly triggeringEventEnvelope: Envelope;
apps/orders/src/infrastructure/saga/drizzle-saga-command-store.ts:35:    triggeringEventEnvelope: row.triggeringEventEnvelope as Envelope,
apps/orders/src/infrastructure/saga/drizzle-saga-command-store.ts:62:        triggeringEventEnvelope: input.triggeringEventEnvelope,
apps/orders/src/infrastructure/saga/saga-first-park-dead-letter-handler.ts:44:    const envelope: Envelope = row.triggeringEventEnvelope;
apps/orders/src/infrastructure/persistence/schema/saga-commands.schema.ts:61:    triggeringEventEnvelope: json('triggering_event_envelope'),
```

(The `grep -v` stage excludes `.spec.ts` paths from a claim about production source; the matched text never contains `.spec.ts:`.)

Classification:
- **Writes:** `saga-fact-handler.ts:153`, `cancel-order.handler.ts:175` and `:234`, `drizzle-saga-command-store.ts:62`.
- **Type declarations:** `saga-command-store.port.ts:40` and `:51`, `saga-commands.schema.ts:61`.
- **Row mapping:** `drizzle-saga-command-store.ts:35`.
- **The only consumer:** the dead-letter publish, `saga-first-park-dead-letter-handler.ts:44`.

**No success-path read exists.** `Order.cancel` (`apps/orders/src/domain/order.ts:397-401`) takes `(reason, ctx, compensationSteps)` and no note. The row correctly presents #8's read-back as **new #8-only behaviour, not a port**.

**#7 counterpart for the benchmark:** `grep -n "operator_note" order-to-cash-nestjs/feature_list.json` prints nothing. **#8-only.**

## Rulings on the remaining items

- **No SA-3 (verified).** `grep -rnE "saga_commands|saga command" specs/shared/*.md` returns exactly the five hits the notes name: `domain-model.md:275` and `:475`, `test-matrix.md:127`, `requirements.md:564`, `saga.md:31`. All are prose; none describes a column. Nothing in this review is rooted in `specs/shared/`, so no `SA-n` is owed.
- **The red first run is accepted (item 8).**
  - `quality_id71.log` shows **57** `Failed` lines, each `[1 ms]`, all thrown from `KafkaContainerFixture.InitializeAsync()` (`tests/Projector.IntegrationTests/TestSupport/KafkaContainerFixture.cs:49`), with Docker's `Bind for 0.0.0.0:35127 failed: port is already allocated`. The failure happens at container creation, before any test body.
  - Id 71 touches only `src/Orders` and the Orders test projects (record `:34-74`).
  - The unmodified full re-run is green at 1815.
  - I agree with the leader's recurrence watch: a second port-allocation red would make this a harness defect worth an entry.
- **Architecture.** The one `Domain/` change (`Domain/Events/OrderCancelled.cs`) is comment-only: `git diff -U0 HEAD` shows no changed line that is not `///`. There is no `decimal`, `TODO` or `Console`/`Debug` write in the six touched production files (grep exit 1). No new inter-service interaction: the synthetic envelope is stored and dead-lettered, never published as a fact.

## Advisories (not blocking)

- **A1 — Application → `Infrastructure.Outbox`, new.** `OperatorCancelRequestedEnvelope.cs:3` imports `OrderToCash.Orders.Infrastructure.Outbox` for `OrdersFactTopic.Name` (`:49`).
  - `find src/Orders/Application -name '*.cs' -print0 | xargs -0 grep -n "OrdersFactTopic\|Infrastructure\.Outbox"` returns only this file (`:3`, `:48`, `:49`).
  - Application → `Infrastructure.Messaging.Rpc` has precedent in three files: `ISagaCommands.cs:1`, `SagaCommandRequestFactory.cs:1`, `CancelOrderCommandHandler.cs:5`.
  - #7 **injects** the topic into the handler (`cancel-order.handler.spec.ts:118` passes `ORDERS_FACTS_TOPIC` to the constructor).
  - No NetArchTest rule forbids Application → Infrastructure in #8.
  - **Recommend the leader file a backlog entry** for that rule, enumerating the four existing sites first, rather than leaving it to a sentence.
- **A2 — the end-to-end outbox assertions fail unhelpfully.** When the note is lost, `GetProperty("note")` (`OrdersCancelAcceptanceTests.cs:380`, and the `Confirmed` twin) fails with `KeyNotFoundException`, as M6 showed. Fold the fix into D1's new test.
- **A3 — id 62's race makes the lookup's disambiguation visible.**
  - `FindOperatorCancelNoteAsync` identifies an operator cancel **by envelope content, not by the cancellation's reason or branch**.
  - If an operator cancel inserts a synthetic `stock.release` row, and a later saga-decided step's enqueue of the same command returns `AlreadyEnqueued` (`EfCoreSagaCommandStore.cs:60-67`), that row keeps the operator's envelope. Whichever `Cancel` step completes the order will then read the note back.
  - This is reasoning from the code, **not probed**, and it is the same interleaving id 62 exists for.
  - **Route it as an acceptance line on backlog id 62** (leader-owned), stating how bullet 3 behaves under that race, not as a sentence here.
- **A4 — record miscount.** `progress/impl_…md:254-255` says *"three that also start Kafka containers (Notifications, Fulfillment, Billing, Gateway)"*, which lists four.
- **A5 — bullet 3's absence guards were not probed by fabrication.** They can fail only if something invents a note. The mutation with teeth is passing a literal note at `SagaFactHandler.cs:202`. Worth running in the fix round, since it is one build.

## `CHECKPOINTS.md`

**C1 — harness**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` exist.
- [x] `progress/current.md`, `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (plus suite_runner).
- [x] Every agent declares its model. `implementer` is `sonnet`; `test_maintainer` and `suite_runner` are `haiku`. `leader`, `spec_author` and `reviewer` state deliberate inheritance in their descriptions.
- [x] `./init.sh` exits 0.

**C2 — state**
- [x] At most one `in_progress` (zero; id 71 is `in_review`).
- [x] Every status valid (`init.sh`).
- [ ] Every `done` feature has passing tests — not walked for other features; id 71 is not `done`.
- [x] `progress/current.md` describes the active session (id 71, `in_review`).
- [ ] Every `blocked` feature records why — not walked; id 71 is not blocked.

**C3 — architecture**
- [x] Domain purity. `Architecture.Tests` passed 25/25 in `quality_id71_rerun.log` (not re-run by me). Id 71's only Domain change is comment-only.
- [x] No cross-service DB access; id 71 is wholly inside `src/Orders`.
- [x] No shared runtime code added.
- [x] No `Domain/` → `OrderToCash.Cqrs` (architecture suite, as above).
- [x] `SharedKernel` untouched.
- [x] No `decimal` in touched files (grep exit 1).
- [x] No new inter-service interaction; nothing to classify.
- [x] No stray debug logging or context-free TODOs in touched files (grep exit 1).
- A1 is a layering note outside these boxes.

**C4 — verification**
- [x] `./quality.sh` passed (08:19 re-run, 1815, `[OK] quality.sh finished`).
- [x] Domain tests pure; no domain test touched.
- [x] Integration tests use real MS-SQL, Kafka and NATS containers.
- [ ] Coverage thresholds — not independently verified. The log's coverage section prints no per-layer percentages, only `[OK] quality.sh finished`.
- [x] No Jest.

**C5 — clean close**
- [x] No suspicious untracked files. The `git status` `??` list is feature 27's and id 71's sources, tests and records only.
- [ ] History entry with effort — appended as **round-1 effort to date**, not a closing entry, since the feature is not closed.
- [x] `feature_list.json` reflects the truth: `in_review`; I made no edit.
- [ ] Human told what was done and how to test it — the leader's, at close.
- [x] No commit by the reviewer (HEAD `909394f`).

**C6 — SDD**
- n/a (`sdd: false`).

**C7 — reuse fidelity**
- [x] `specs/shared/` byte-identical to #7 except `test-matrix.md`: `diff -rq specs/shared order-to-cash-nestjs/specs/shared` prints only `test-matrix.md`.
- [x] No deviation introduced; no amendment owed.
- [x] No `R<n>` claimed by id 71.
- [ ] n8n workflows — not in this feature's scope; not walked.
- [ ] Black-box API script — not in scope; not walked.
- [x] Effort record honest — appended, marked rejected.
- [ ] README benchmark — the leader's, at wrap-up.

## What must change before re-review

1. **D1** — add the `[Theory]` in `OperatorNoteReachesTimelineEndToEndTests.cs` in the shape above. For each of the three branches, assert `detail.note` exactly and the branch's `detail.compensationSteps` length on the real Mongo timeline, and arm it (M1, plus one corruption).
2. **D2** — rewrite the eight wrong-mechanism lines (enumerated above) to the INSERT-only invariant, and re-enumerate on the retired wording. Then either give the precedence a guard that can fail (both rows synthetic, different notes, armed with M7) or remove the precedence claim from the test name, the port doc comment and the row. The rewritten row must name an armed guard for the property it now claims.
3. **D3** — correct `CancelOrderCommandHandler.cs:82-84` and `OperatorCancelRequestedEnvelope.cs:15-16` (§4.1 for the column, #7's `cancel-order.handler.ts:272-286` for the envelope, no R29 attribution). Enumerate `§4.2` across `src/Orders` and `tests/Orders.*`, and classify every hit.
4. **D4** — replace the record's #7 enumeration with the content search above, one classification line per hit, including the topic assertions.
5. **D5** — correct ledger row 1's nullability reason, and state that #7's compile-time property is not supplied for future callers.
6. Record every new probe with its verbatim failure, and reconcile the suite count against 1815.

---

## Round 2 — 2026-09-11

**Verdict: REJECTED.** Status left at `in_review`; I made no edit to `feature_list.json`.

**Where this leaves the feature.** Every mechanism, test and guard round 1 asked for now exists and can fail: **six of six round-2 probes were killed**, including M7, which survived round 1. D1, D2, A1, A2, A4 and A5 are closed. One defect of D2's own class blocks, and it is text only:
- **D6 (blocking, new to this review, present since round 1)** — the claim id 71 exists to retire, *"an operator-cancel compensation row carries no triggering envelope"*, is still asserted in **five comment blocks**. Two are on the port whose nullability D5 accepted on the strength of an honest disclosure, and one of those (`ISagaCommandStore.cs:53-55`) **instructs the next caller to pass `null`** for exactly those rows. D3 is therefore not closed either: two of its `§4.2` hits sit inside these sentences.
- **D7 (required, record-only)** — the D4 totals line sums to 19 by mixing units.

**Round 1 missed D6, and that is my miss.** My D3 enumeration searched the citation (`§4.2`) and one exact phrase (`RPC-triggered — no triggering`), not the claim. The record's sweep did the same. Both searched the wording of the *correction*, not of the claim being retired: the trap `CLAUDE.md` records from feature 24.

**Recommendation:** one text-only fix round, then a **read-only round 3**. Round 3 means re-running the two enumerations below, reading the diff, and a clean `dotnet build` plus `dotnet format --verify-no-changes`. No mutation probe is owed if no non-comment line changes.

### What I ran, and what I did not

- **No full re-run.** I re-summed `scratchpad/quality_full_clean.log` (10:39:39) from its 18 `Passed!` lines: **1820**, 0 `Failed!`, ending `[OK] quality.sh finished`. It matches the record (`:681-704`) and the leader.
- **Six probes**, one mutation per `dotnet build --no-incremental`, one named test each (the D1 theory counts as one test with three cases).
  - Each was restored by `cp` from `scratchpad/rev71r2/*.bak`, `cmp`-identical, the changed line re-read, and `touch`ed.
  - After the last restore: forced rebuilds of `Gateway.IntegrationTests`, `Orders.IntegrationTests` and `Orders.UnitTests`, each `0 Warning(s) 0 Error(s)`. `grep "REVIEW PROBE"` over `src`/`tests` returns nothing.
  - **Confirming greens:** `Orders.UnitTests` (`OperatorCancelRequestedEnvelopeTests` + `CancelOrderCommandHandlerTests`) **14/14**; the four probed Orders integration tests **4/4** (34 s); the D1 theory **3/3** (30 s), run through `dotnet test OrderToCash.sln` so it also answers harness question (b).
  - Logs: `scratchpad/rev71r2/`.
- `./init.sh`: exit 0. `diff -rq specs/shared ../order-to-cash-nestjs/specs/shared` prints only `test-matrix.md`.
- **Process disclosure:** my first `pgrep -fl "dotnet (build|test|format)"` printed `466317 bash`, its own shell self-matching. Every wait was on a PID with `kill -0`, and no two `dotnet` runs of mine overlapped.
- **Not re-probed:**
  - A5's fabrication arm, accepted from the record's row 9 with its verbatim messages.
  - The D1 `compensationSteps`-length assertion (`OperatorNoteReachesTimelineEndToEndTests.cs:365-366`). I read it, but did not mutate it. R2-1's failure output shows the real timeline carrying exactly the lengths the `[InlineData]` expect: `stock_reserved` 1, `credit_approved` 2, `confirmed` 2.

### Probe table (round 2)

| # | Family | Mutation | Site | Named test | Result |
|---|---|---|---|---|---|
| R2-1 | deletion (M1) | read-back returns `null`: `_ = (creditReleaseEnvelope, stockReleaseEnvelope); return ExtractOperatorCancelNote(null);` | `EfCoreSagaCommandStore.cs:308` | `OperatorNoteReachesTimelineEndToEndTests.PostOrdersCancelWithANote_ThroughTheRealCompensationChain_LandsOnTheRealMongoTimelineEntryWithTheBranchsCompensationSteps`, all 3 cases | **killed 3/3** — e.g. `branch 'stock_reserved': expected detail.note to equal "Cancelled from stock_reserved, end to end through the real compensation chain.", but the timeline entry carries no note key at all. detail: { "cancellationReason" : "operator_cancelled", "compensationSteps" : [{ "step" : "stock_released", "eventType" : "stock.released.v1", … }] }`; the same shape for `credit_approved` and `confirmed`, each with `credit_released` then `stock_released` |
| R2-2 | deletion (M1), for A2 | same | same | `OrdersCancelAcceptanceTests.StockReserved_WithANote_TheNoteReachesTheCancelledOutboxRowAfterTheRealCompensationCompletes` and `.Confirmed_WithANote_…` | **killed 2/2** — `expected the order.cancelled.v1 outbox payload to carry "note": "Buyer called to cancel before despatch.", but it carries no note key at all. payload: {…}` and `… "Retailer requested cancellation after credit was already approved.", but it carries no note key at all …` |
| R2-3 | ordering substitution (M7) | `ExtractOperatorCancelNote(stockReleaseEnvelope) ?? ExtractOperatorCancelNote(creditReleaseEnvelope)` | `EfCoreSagaCommandStore.cs:308` | `SagaCommandStoreTests.FindOperatorCancelNoteAsync_PrefersCreditReleaseOverStockRelease_WhenBothRowsExistForTheOrder` | **killed** (round 1: survived) — `Assert.Equal() Failure: Strings differ / Expected: "Credit released first — the canonical car"··· / Actual: "A DIFFERENT note — must lose to the credi"···` |
| R2-4 | the invariant: a duplicate writes | after `Detached`, the duplicate-key `catch` runs `ExecuteUpdateAsync(setters => setters.SetProperty(c => c.TriggeringEventEnvelope, row.TriggeringEventEnvelope))` on the existing row | `EfCoreSagaCommandStore.cs:66-67` | `SagaCommandStoreTests.EnqueueAsync_ADuplicateEnqueueWithADifferentEnvelope_LeavesTheFirstEnvelopeInPlace` | **killed** — `Expected: "the first envelope's own note" / Actual: "a SECOND, different note — must never lan"···` |
| R2-5 | payload corruption, my own, one branch | only the `credit.release` site builds its envelope with `note + " [R2-CORRUPTED]"`; the `stock.release` site (`:209`) untouched | `CancelOrderCommandHandler.cs:177` | the D1 theory, `credit_approved` case only (`DisplayName~credit_approved`) | **killed** — `branch 'credit_approved': expected detail.note to equal "Cancelled from credit_approved, end to end through the real compensation chain.", got "Cancelled from credit_approved, end to end through the real compensation chain. [R2-CORRUPTED]". detail: {…}`. It also proves end to end that the credit branches take their note from the `credit.release` row, the precedence R2-3 guards at the store |
| R2-6 | substitution of a real sibling | `Topic = "otc.billing.facts.v1"` (the value of `BillingFactTopic.Name`, `src/Billing/Infrastructure/Outbox/BillingFactTopic.cs:14`) | `OperatorCancelRequestedEnvelope.cs:66` | `OperatorCancelRequestedEnvelopeTests.Topic_EqualsOrdersFactTopicName` | **killed** — `Assert.Equal() Failure: Strings differ / Expected: "otc.orders.facts.v1" / Actual: "otc.billing.facts.v1"` |

### D1 — CLOSED (unit: per compensation branch ×3, on the read-model timeline)

Checked against the required shape (round 1 `:119-134`), one line per point, in `tests/Gateway.IntegrationTests/OperatorNoteReachesTimelineEndToEndTests.cs`:
1. **Setup:**
   - real `OrdersHost` (`StartOrdersAsync`, `:78-110`) and real `ProjectorHost` (`:193-209`);
   - `SeedPlacedOrderAsync` (`:319`), then an EF status update (`SetOrderStatusAsync`, `:391-398`), an allowed option;
   - no Billing or Fulfillment host.
2. **Cancel:** `POST /orders/{id}/cancel` through `GatewayTestHost` with a per-case bracketed note (`:332-334`).
3. **Stand-ins:** `StandInResponder` for `fulfillment.stock.release` always, and for `billing.credit.release` on the credit branches (`:315-316`, `:377-389`).
4. **Facts:**
   - on the credit branches, poll `credit.release` to `sent`, then publish `credit.released.v1` (`:336-342`);
   - then poll `stock.release` to `sent` and publish `stock.released.v1` (`:344-347`);
   - both keyed by order id on `SagaFactTopics` (`:421-430`).
5. **Assertions:** `detail.note` exactly (`:358-363`) and `compensationSteps.Count` (`:365-366`).
6. **Messages:** `TryGetElement` plus a message naming the note (`:358-363`), shown live by R2-1.
7. **Armed:** R2-1 (deletion, 3/3) and R2-5 (corruption, one branch), plus the record's rows 5-6.

### D2 — CLOSED

- **The mechanism half matches the code.** The only write of the column is still `EfCoreSagaCommandStore.cs:45`, inside the INSERT. The port's doc (`ISagaCommandStore.cs:133-156`) and the record's row 3 (`:146`) now state the INSERT-only invariant.
- **The precedence is kept and falsifiable** (R2-3). The rewritten row's own invariant has an armed guard (R2-4).
- **The retired wording, enumerated by content with bin/obj excluded by path:**

```
$ find src tests -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n -i "overwrit\|re-enqueue\|reenqueue"
tests/Projector.UnitTests/DeltaToPipelineTests.cs:96:        // null fill value — never overwritten with something invented.
tests/Orders.IntegrationTests/SagaConsumptionTests.cs:187:            // (ThrowOnceGate.BeforeEnqueueAsync), so what happens next is
tests/Orders.IntegrationTests/SagaConsumptionTests.cs:266:        public async Task BeforeEnqueueAsync(CancellationToken cancellationToken)
tests/Orders.IntegrationTests/SagaConsumptionTests.cs:286:            await gate.BeforeEnqueueAsync(cancellationToken);
tests/Notifications.UnitTests/NotificationMimeMessageBuilderTests.cs:32:        // — the guard here is that OUR value is never silently overwritten
tests/Projector.IntegrationTests/OutOfOrderFactsTests.cs:10:/// "overwrite" scenario: no two of the fourteen facts ever write the same
tests/Projector.IntegrationTests/OutOfOrderFactsTests.cs:23:    public async Task R52_AppendsTheTimelineEntryWithoutRegressingTheDocumentStatusOrOverwritingNewerReferences()
src/Orders/Domain/Order.cs:70:    /// <summary>Present iff … a second <c>Cancel</c> is refused by the state machine before it could overwrite it.</summary>
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:190:    /// <c>attempts</c> (never overwrites — the count must survive every
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:212:        // dispatcher has already reported `sent` is never overwritten
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:235:    /// (never overwrites <c>attempts</c>), but sets <c>next_attempt_at</c>
src/Orders/Application/Ports/ISagaCommandStore.cs:92:    /// overwritten <c>parked</c>. Returns <see langword="true"/> iff THIS
```

12 lines; the `Order.cs:70` line is elided with `…` here only. None is a live envelope-overwrite claim. Classification:
- `DeltaToPipelineTests.cs:96`, `OutOfOrderFactsTests.cs:10`, `:23` — Projector, unrelated.
- `SagaConsumptionTests.cs:187`, `:266`, `:286` — `BeforeEnqueueAsync`, which matches `reenqueue` only because this run is case-insensitive; unrelated.
- `NotificationMimeMessageBuilderTests.cs:32` — Notifications, unrelated.
- `Order.cs:70` — cancellation-reason immutability.
- `EfCoreSagaCommandStore.cs:190`, `:235` — the attempts count; `:212` — the `sent` status.
- `ISagaCommandStore.cs:92` — the `parked` status.

In progress files, the hits are corrections or quotations, not live claims:
- record `:6`, `:101`, `:146`, `:153`, `:364`;
- `current.md:99`, quoting the round-1 finding;
- `history.md:2209`, quoting the rejection;
- `history.md:1550`, spec-file mtimes of another feature;
- this review.

**My own round-1 miscount:** round 1 said *"14 hits in total … Unrelated (6)"*, but its printed output (`:166-178`) is **13** lines, 5 of them unrelated. The record's *"only the 6 unrelated"* (`:6`) inherits my figure; within that scope the true number is 5. The error is mine, not the implementer's.

**A6 (advisory, introduced by the fix).** `SagaCommandStoreTests.cs:315` says *"The first envelope's exact bytes must survive, byte-for-byte, not merely 'some note'"*. `:343` asserts only the extracted note.
- The realistic regression is caught: an overwrite with the duplicate's own envelope (R2-4).
- A rewrite that kept the note but changed any other byte would pass. That matters to R29's byte-equal `.dlq`.
- **Fix:** assert the stored column equals `UTF8(firstEnvelope)`, or reword `:315` to what is asserted. It is the same edit class as D6.

### D3 — NOT CLOSED (folded into D6)

The `§4.2` enumeration reproduces the record's exactly: the same command (record `:249`), **25** lines. The two named sites are corrected: `CancelOrderCommandHandler.cs:84-86` and `OperatorCancelRequestedEnvelope.cs:15-17` now cite §4.1 and #7's `cancel-order.handler.ts:272-286`, with no R29 claim.

But two of the four hits the record classed as *"generic doc comments describing the COLUMN/PARAMETER shape … left untouched"* (`:267-271`) are not generic:
- **`ISagaCommandStore.cs:16`** opens a sentence that ends at `:19-20`, *"`null` … for one enqueued with no triggering fact at all (`CancelOrderCommandHandler`'s RPC-triggered compensation rows)"*.
- **`ISagaCommandStore.cs:51`** opens one that ends at `:53-55`, *"Every caller must be explicit: `null` for a command with no triggering fact at all (`CancelOrderCommandHandler`'s RPC-triggered compensation rows)"*.

Both are `§4.2` citations **on the operator-cancel path**, stating the behaviour id 71 removed. The classification judged the citation, not the sentence it sits in.

### D4 — CLOSED on the enumeration; one count defect (D7)

- **The enumeration reproduces.** Re-run at #7 HEAD `bf45af0`, the command gives **19** lines. Sorted, they are `cmp`-identical to the record's `:167-185`.
- **The "21" was mine.** Round 1's printed output (`:219-237`) is 19 lines. My round-1 transcript shows *21* only in prose, never from a count. The likeliest origin is 19 plus the two `triggeringEventTopic` assertions (`cancel-order.handler.spec.ts:172`, `:219`) I cited by hand, but I cannot show that. **I withdraw the figure, and with it round 1's *"12 of them outside that list"*.**
- **D7 — the totals line does not reconcile with its own classification.**
  - Record `:233-236` says *"4 ported assertions … 2 strengthened … 1 superseded … 12 not applicable"*, which sums to 19 only by mixing units.
  - The per-line classification (`:190-231`) gives: **2** ported lines (items 7, 9; the two topic assertions are not among the 19 lines); **1** strengthened (item 8; item 6 is marked not applicable); **1** superseded (item 3); **15** not applicable (items 1, 2, 4, 5, 6, 10–19). 2 + 1 + 1 + 15 = 19.
  - Extending the pattern with `triggeringEventTopic` adds 8 lines. Two are `:172` and `:219`, ported per items 7 and 9. Six are fixture lines in the same specs as items 2 and 14–18 (`saga-command-retry.integration.spec.ts:160`, `saga-command-dispatcher.spec.ts:43`, `saga-command-sweeper.spec.ts:51`, `saga-command-dispatcher-log-trace-id.spec.ts:52`, `saga-command-sweeper-log-trace-id.spec.ts:56`, `saga-first-park-dead-letter-handler-log-trace-id.spec.ts:75`), not applicable.
  - **Fix:** state the totals per unit, lines or assertions, not both in one sum.
- **The outcome is unchanged:** no guard was dropped in translation.

### D5 — the statement is CLOSED and the decision accepted; D6 undercuts it

- **Row 1** (record `:144`) cites the real reason. `SagaFact.cs:43` is `byte[]? TriggeringEventEnvelope = null,`, and `SagaFactHandler.cs:140` passes `fact.TriggeringEventEnvelope,` through; both verified.
- **It states plainly** that #7's compile-time guarantee (`saga-command-store.port.ts:40`, `:51`) is not carried over for future callers, and that a third site could pass `null`.
- **Ruling: honest, and acceptable.** The two existing sites are guarded and armed: M4, M5 and M8 from round 1, and R2-5 now hits the `credit.release` site end to end. The loss for future callers is unguarded by construction. The record says so, which is what round 1 asked.
- **Current callers,** as a search result: `find src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' | xargs grep -n "EnqueueAsync("` finds the declaration (`ISagaCommandStore.cs:58`), the implementation (`EfCoreSagaCommandStore.cs:26`) and **three** calls: `SagaFactHandler.cs:134`, `CancelOrderCommandHandler.cs:179`, `:211`.
- **But** the only substitute for the compile-time guarantee is per-site guards plus documentation. The port's documentation at `ISagaCommandStore.cs:53-55` tells the next operator-cancel-shaped caller the opposite, to pass `null`. That turns a disclosed gap into an instructed one, and it is D6's first fix.

### D6 — the retired claim survives in five comment blocks (blocking)

**Enumerated on the retired claim's wording, not the correction's.** Two commands, bin/obj excluded by path, complete output.

```
$ find src tests -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n -i "RPC-triggered\|no triggering fact\|null-skip\|null alongside\|RPC triggered"
tests/Orders.UnitTests/SagaFirstParkDeadLetterHandlerTests.cs:118:        // A row enqueued before this feature, or an RPC-triggered
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:17:/// <c>SagaFactHandler</c>, which this RPC-triggered enqueue is not) and,
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:21:/// not require this envelope (an operator cancel has no triggering fact in
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:85:/// <c>SagaFactsConsumer</c>/<c>SagaFactHandler</c>, which this RPC-triggered
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:164:    /// it is RPC-triggered, exactly the "own enqueue path" the factory's own
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:197:    /// for the fact-driven caller. This caller has no triggering fact at
src/Orders/Application/Ports/ISagaCommandStore.cs:19:/// before this feature, or one enqueued with no triggering fact at all
src/Orders/Application/Ports/ISagaCommandStore.cs:20:/// (<c>CancelOrderCommandHandler</c>'s RPC-triggered compensation rows).
src/Orders/Application/Ports/ISagaCommandStore.cs:55:    /// RPC-triggered compensation rows), the fact's own bytes otherwise.
src/Orders/Application/Sagas/SagaCommandRequestFactory.cs:39:    /// enqueue for the <c>stock_reserved</c> branch has no triggering fact at
src/Orders/Application/Sagas/SagaCommandRequestFactory.cs:40:    /// all (it is RPC-triggered) and does not call this method — it builds
src/Orders/Infrastructure/Saga/SagaFirstParkDeadLetterHandler.cs:98:            // A row enqueued before this feature, or an RPC-triggered
src/Orders/Infrastructure/Saga/SagaFirstParkDeadLetterHandler.cs:99:            // compensation row with no triggering fact at all
src/Orders/Infrastructure/Persistence/Entities/SagaCommand.cs:46:    /// fact at all (an RPC-triggered compensation row).

$ find src tests -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n -i "no triggering envelope\|nothing to republish\|carries no triggering\|with no triggering\|no triggering fact\|dead-letters nothing\|feature 27's A2\|null envelope\|envelope: null\|triggeringEventEnvelope: null"
tests/Orders.UnitTests/SagaFirstParkDeadLetterHandlerTests.cs:128:        var claimed = new SagaCommandRecord(…, TriggeringEventEnvelope: null, TriggeringEventTopic: null);
tests/Orders.UnitTests/SagaFactHandlerTests.cs:494:        TriggeringEventEnvelope: null,
tests/Orders.IntegrationTests/SagaFirstParkDeadLetterTests.cs:55:            await seedStore.EnqueueAsync(orderId, "ORD-000001", SagaCommandKind.StockReserve, …, triggeringEventEnvelope: null, triggeringEventTopic: null, …);
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:41:        await store.EnqueueAsync(_orderId, "ORD-000001", SagaCommandKind.StockReserve, …, triggeringEventEnvelope: null, triggeringEventTopic: null, …);
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:78:            await store.EnqueueAsync(orderId, "ORD-000002", SagaCommandKind.StockReserve, …, triggeringEventEnvelope: null, triggeringEventTopic: null, …);
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:103:        var first = await store.EnqueueAsync(orderId, "ORD-000003", SagaCommandKind.CreditHold, …, triggeringEventEnvelope: null, triggeringEventTopic: null, …);
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:106:        var second = await store.EnqueueAsync(orderId, "ORD-000003", SagaCommandKind.CreditHold, …, triggeringEventEnvelope: null, triggeringEventTopic: null, …);
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:140:        await store.EnqueueAsync(orderId, "ORD-000004", SagaCommandKind.StockRelease, …, triggeringEventEnvelope: null, triggeringEventTopic: null, …);
src/Orders/Infrastructure/Saga/SagaFirstParkDeadLetterHandler.cs:99:            // compensation row with no triggering fact at all
src/Orders/Infrastructure/Saga/SagaFirstParkDeadLetterHandler.cs:100:            // (CancelOrderCommandHandler) — nothing to republish.
src/Orders/Infrastructure/Saga/SagaFirstParkDeadLetterHandler.cs:102:                "SagaFirstParkDeadLetterHandler: row {CommandId} carries no triggering envelope; .dlq publish skipped.",
src/Orders/Application/Ports/ISagaCommandStore.cs:19:/// before this feature, or one enqueued with no triggering fact at all
src/Orders/Application/Sagas/SagaCommandRequestFactory.cs:39:    /// enqueue for the <c>stock_reserved</c> branch has no triggering fact at
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:21:/// not require this envelope (an operator cancel has no triggering fact in
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:197:    /// for the fact-driven caller. This caller has no triggering fact at
src/Orders/Infrastructure/Persistence/Entities/SagaCommand.cs:45:    /// for a row enqueued before this feature, or one with no triggering
```

Long argument lists are elided with `…` here only: 14 lines, then 16.

**False since id 71 — the five blocks to rewrite:**
1. **`ISagaCommandStore.cs:16-20`** (`SagaCommandRecord` summary) says the envelope is `null` for *"`CancelOrderCommandHandler`'s RPC-triggered compensation rows"*. Those rows now carry the synthetic envelope.
2. **`ISagaCommandStore.cs:51-55`** (`EnqueueAsync`'s `triggeringEventEnvelope` parameter) says *"Every caller must be explicit: `null` for … `CancelOrderCommandHandler`'s RPC-triggered compensation rows"*. It is false, and it is prescriptive (D5).
3. **`SagaCommand.cs:44-46`** (the entity) says `null` for *"an RPC-triggered compensation row"*.
4. **`SagaFirstParkDeadLetterHandler.cs:98-100`** (the null-skip branch) names the operator-cancel row (`CancelOrderCommandHandler`) as having *"nothing to republish"*. Id 71's own `OrdersCancelAcceptanceTests.StockReserved_OperatorCancelCompensationParks_PublishesADlqCopyByteEqualToTheStoredEnvelope` proves those rows now republish.
5. **`SagaFirstParkDeadLetterHandlerTests.cs:118-120`** is the comment on `OR3_AWinningClaimWithNoTriggeringEnvelope_AppendsTheFactButPublishesNoDlqCopy`, naming `CancelOrderCommandHandler`'s rows as the no-envelope case. The test stays valid for rows that predate the column; only the comment is wrong.

**True, not the retired claim:**
- `CancelOrderCommandHandler.cs:85` — D3's corrected text.
- `CancelOrderCommandHandler.cs:164`, `:197` — no triggering fact *for the request-payload factory*, which is still true.
- `OperatorCancelRequestedEnvelope.cs:17`, `:21` — D3's corrected text.
- `SagaCommandRequestFactory.cs:39-40` — the payload factory is not called by this path, true.
- `SagaFirstParkDeadLetterHandler.cs:102` — a generic log message, true for pre-feature rows.
- The eight `null` literals in tests — fixtures and seed rows: `SagaFirstParkDeadLetterHandlerTests.cs:128` is block 5's fixture; `SagaFactHandlerTests.cs:494` is a fact fixture; `SagaFirstParkDeadLetterTests.cs:55` and `SagaCommandStoreTests.cs:41`, `:78`, `:103`, `:106`, `:140` are fact-less store seeds. None is a claim about operator cancels.

**Why this blocks, when it is only text:**
- **It is D2's class, one step removed.** A wrong mechanism written into the production port the ledger cites is the feature-24 round-3 precedent `CLAUDE.md` records: *"the disproved claim alive … in the production source file the row exists to justify"*.
- **It is the rule on how to find it.** *Enumerate on the wording of the claim being retired.*
- **Id 71 falsified these five blocks, so id 71 owns them.** Four of the five files are feature 27's; `ISagaCommandStore.cs` is also a file id 71 edited.

**Also fold in, same edit:** `OperatorCancelRequestedEnvelope.cs:59-61` says *"unlike the three pre-existing Application → `Infrastructure.Messaging.Rpc` references"*. The file itself imports `OrderToCash.Orders.Infrastructure.Messaging.Rpc` (`:2`) for `RpcJson` (`:48`). The file is untracked, so that is a fourth such edge, and a new one. The Rpc edge's *removal* is id 76's (below); the sentence miscounting it is this feature's.

### A1 — CLOSED; its remainder routed to id 76

- `OperatorCancelRequestedEnvelope.cs` no longer references `Infrastructure.Outbox`. Its `Topic` is a literal (`:66`), guarded by `OperatorCancelRequestedEnvelopeTests.Topic_EqualsOrdersFactTopicName`, which **R2-6 killed with a real sibling**.
- **The wider class is routed, not expected here.** Backlog id 76 (`application_layer_depends_on_infrastructure_unguarded`) is in `feature_list.json` (`:1073`). Its first acceptance bullet enumerates *every* Application file referencing an Infrastructure namespace, by content, so it will find `OperatorCancelRequestedEnvelope.cs:2`.
- **One correction for the leader:** id 76's sighting list says the file references `Infrastructure.Outbox`, *"which id 71's fix round removes"*. That is true of Outbox, but silent on the `Infrastructure.Messaging.Rpc` import that remains. **Recommend amending that sighting line** so the entry does not read as though id 71 left the file clean.

### A2, A3, A4, A5

- **A2 — CLOSED.** No bare `GetProperty("note")` remains: the four sites are `TryGetProperty` at `OrdersCancelAcceptanceTests.cs:383`, `:460`, `:520` and `:609`. R2-2 shows both outbox cases failing with the note named.
- **A3 — routed.** The id-62 acceptance line is present in the working `feature_list.json`, in the leader's hunk at `@@ -862 +864,2 @@`.
- **A4 — CLOSED.** Record `:417-420`: *"four that also start Kafka containers (Notifications, Fulfillment, Billing, Gateway)"*.
- **A5 — CLOSED per the record,** arming row 9 (`:365`), with both verbatim messages. Not re-probed by me.

### The harness change outside id 71's scope — rulings

The change: root `test.runsettings` sets `DOTNET_hostBuilder__reloadConfigOnChange=false`, wired through `Directory.Build.props:31` (`<RunSettingsFilePath>$(MSBuildThisFileDirectory)test.runsettings</RunSettingsFilePath>`).

**(a) Is it safe? Yes.** Search results:

```
$ find src tests -type f \( -name '*.cs' -o -name '*.json' -o -name '*.csproj' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "reloadOnChange\|ReloadOnChange\|reloadConfigOnChange\|IOptionsMonitor\|OptionsMonitor\|GetReloadToken\|ChangeToken\|FileSystemWatcher\|PhysicalFileProvider\|hostBuilder__\|hostBuilder:"
(no output; exit 123 = grep found nothing)
$ find tests -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "appsettings"
(no output)
$ find src tests -name 'appsettings*.json' -not -path '*/bin/*' -not -path '*/obj/*'
(no output)
```

- **There is no `appsettings*.json` anywhere under `src/` or `tests/`.** Reload has nothing to reload. The watcher exists only because the default optional, reload-on-change JSON sources watch the content root whether or not a file is there.
- **No code reads a reload token or `IOptionsMonitor`.**
- **No test writes a file a host reads.** Tests that write files at all (`grep "File\.WriteAll\|File\.AppendAll\|new StreamWriter\|File\.Create("`), 10 hits, each a scratch file for a guard's non-vacuity probe:
  - `CreditRpcPayloadTests.cs:82`, `SagaCommandPayloadTests.cs:252`, `OrdersCreateErrorMapperTests.cs:163`, `CatalogReferenceListPayloadTests.cs:121`, `OrdersCancelPayloadTests.cs:88`, `StockRpcPayloadTests.cs:188`, `GatewayRpcPayloadTests.cs:118` — corrupted scratch payload copies;
  - `BillingConsumesNoFactsTests.cs:92`, `CentsRuleFixtureGuardTests.cs:47` — scratch source files;
  - `ReadModelSoleWriterTests.cs:69` — a scratch `RogueWriter.cs` in a temp directory.
- **Production hosts are unaffected.** Only VSTest test hosts receive the variable.

**(b) Does it reach the test hosts? Yes, observed both ways.**
- **Bare project run** (R2-1's `dotnet test tests/Gateway.IntegrationTests/…`): test host PID 474271's `/proc/…/environ` carried `DOTNET_hostBuilder__reloadConfigOnChange=false`. It held **0** inotify descriptors while real Orders, Projector and Gateway hosts were live, and the user-wide count never rose above its **115** floor.
- **Solution-level run** (`dotnet test OrderToCash.sln --no-build --filter …`, `quality.sh`'s invocation shape): **nine** test hosts sampled, every one carrying the variable, each holding **0** inotify descriptors; peak user total **115**. That the variable also *takes effect* rests on those zero counts, and on the implementer's with/without measurement (+8 → +3).
- **Caveat 1:** my solution run omitted `quality.sh`'s `--collect:"XPlat Code Coverage" --results-directory`. `--collect` adds a data collector to the effective run settings rather than replacing the file, and the implementer's sampled full run (peak 118) is consistent. **I did not read a `quality.sh` test host's environment directly.**
- **Caveat 2, paths it does not reach:**
  - `dotnet test --settings <other file>`, which replaces it;
  - `dotnet vstest <dll>` or running a test assembly directly;
  - an IDE runner that ignores `RunSettingsFilePath`.

**(c) Is it guarded? No, and that is acceptable today, but it should not stay that way.**
- **Search result:** `find . -name '*.runsettings'` (node_modules/bin/obj excluded) returns only `./test.runsettings`. `grep -rn "RunSettingsFilePath\|--settings\|VSTestSetting"` over `*.props`/`*.targets`/`*.csproj`/`*.sh`/`*.yml`/`*.yaml` returns only `Directory.Build.props:28` (a comment) and `:31`. No test asserts the variable.
- **Silent removal** would surface only as an `IOException` red on a machine near the limit. This machine is exactly that, with 115 of 128 held by the desktop at rest. On a quieter runner it would never surface.
- **Why acceptable:** it guards no correctness property, and its absence fails loudly, naming inotify, rather than wrongly.
- **Why not permanent:** it can be armed by change of kind, not by load, for one test. Assert inside any host-building test project that a built host's `IConfiguration["hostBuilder:reloadConfigOnChange"]` is `"false"`, armed by deleting the `RunSettingsFilePath` line.

**(d) Should it be its own backlog entry? Yes. The leader files it; I do not write `feature_list.json` beyond id 71's status line.**
- It is outside id 71's scope: neither `src/Orders` nor its tests.
- It changes how every test project runs.
- Its commit record needs its own subject.
- It carries open items of its own: the guard in (c), the bypass paths in (b), and the thin margin.
- **Suggested acceptance:**
  - the landed change;
  - the with/without measurement, citing id 71's record `:622-633` rather than re-running it;
  - the (a) search as the safety proof;
  - the (c) guard, armed;
  - the bypass paths stated in `test.runsettings`' own comment.
- **Not rooted in `specs/shared/`:** no `SA-n`.

**Nothing in this round is rooted in `specs/shared/`**, so no `SA-n` is owed.

### `CHECKPOINTS.md` (round 2)

**C1 — harness**
- [x] Harness files present.
- [x] `./init.sh` exits 0 (run 10:59).

**C2 — state**
- [x] At most one `in_progress`: zero; id 71 is `in_review`.
- [x] Statuses valid (`init.sh`).
- [ ] Every `done` feature has passing tests — not walked; id 71 is not `done`.
- [x] `progress/current.md` names id 71 and the round-2 dispatch.
- [ ] `blocked` features record why — not walked.

**C3 — architecture**
- [x] Domain purity. `Architecture.Tests` 25/25 in `quality_full_clean.log`, not re-run by me; no `Domain/` change this round.
- [x] No cross-service DB access; the round's code stays in `src/Orders` and the Orders/Gateway test projects.
- [x] No shared runtime code added; `test.runsettings` is harness, not runtime.
- [x] No new inter-service interaction.
- [ ] Application → Infrastructure layering — **not enforced anywhere**; routed to id 76, not required here.

**C4 — verification**
- [x] `./quality.sh` passed (10:39, 1820, `[OK] quality.sh finished`).
- [x] The probed integration tests use real MS-SQL, Kafka, NATS and MongoDB containers.
- [x] Six probes killed; confirming greens after a forced rebuild.
- [ ] Coverage thresholds — not independently verified. The log prints only per-report line percentages (e.g. `6.6%`, `4.9%`), which are not the per-layer gates.
- [x] No Jest.

**C5 — clean close**
- [x] No stray untracked files beyond the features in flight and `test.runsettings`, which is disclosed.
- [x] Probe backups exist only in the scratchpad; no probe marker in `src`/`tests`.
- [ ] History entry with effort — updated as **effort to date**; not closeable.
- [x] `feature_list.json`: I made no edit; id 71 stays `in_review`.
- [x] No commit by the reviewer.

**C6 — SDD:** n/a (`sdd: false`).

**C7 — reuse fidelity**
- [x] `specs/shared/` differs from #7 only in `test-matrix.md`.
- [x] No `R<n>` claimed; no amendment owed.
- [x] Effort record honest: extended in `progress/history.md` with the fix round, the close-out and this review.

### What must change before round 3

1. **D6.** Rewrite the five blocks listed above: `ISagaCommandStore.cs:16-20` and `:51-55`, `SagaCommand.cs:44-46`, `SagaFirstParkDeadLetterHandler.cs:98-100`, and `SagaFirstParkDeadLetterHandlerTests.cs:118-120`.
   - **The true statement:** rows `CancelOrderCommandHandler` enqueues carry the synthetic `orders.cancel.requested` envelope (id 71), and are republished on first park. `null` means a row that predates the column, or a caller that supplies none.
   - **For `:51-55`, per D5:** say operator-cancel callers must supply the synthetic envelope. Also say the parameter is nullable only because `SagaFact.TriggeringEventEnvelope` is a defaulted fixture convenience.
   - **Also correct** `OperatorCancelRequestedEnvelope.cs:59-61`'s count.
   - **Then** re-run both D6 enumeration commands exactly as written above, and record their complete output with one classification line per hit.
2. **D7.** Restate the D4 totals (record `:233-236`) in one unit, reconciling against items 1–19.
3. **A6** (advisory, same edit class). Assert the stored bytes at `SagaCommandStoreTests.cs:343`, or reword `:315`.
4. **Leader, not the implementer:**
   - file the harness change as its own backlog entry (rulings (c) and (d));
   - amend id 76's sighting line (A1).

**Round 3 will be read-only** unless a non-comment line changes. It will re-run the two D6 commands, read the diff of the listed blocks, and check a clean `dotnet build` and `dotnet format --verify-no-changes`. If an assertion changes (A6), that one test is re-armed with R2-4 plus a byte-level variant.

## Round 3 — 2026-09-11

**Verdict: APPROVED.** Id 71 is set to `done` by a one-line edit of its `"status"` line (`feature_list.json:995`); `diff` of `git diff -U0 -- feature_list.json` before and after my edit differs only in that `+` line and the index hash.

**Where this leaves the feature.** Every round-2 item is closed at the unit it names. D6/D3: no live comment says operator-cancel rows carry no envelope, by three content enumerations and one comment-block sweep. A6: armed by me and killed on the bytes. A1: 4 by search. D7: the per-line totals reconcile. Two findings remain, both non-blocking and neither a false statement about behaviour:
- **R3-F1** — one `cref` inside a rewritten D6 block does not resolve, and one from round 1 does not either. The record's *"every rewritten `<see cref>` resolves"* was offered on the strength of a build that **cannot fail on a `cref`**: `Directory.Build.props:22` sets `GenerateDocumentationFile` to `false`. This is a repository-wide class, so it is routed as one (below), not fixed at two lines.
- **R3-F2** — record `:160` and `:233-236` still carry the superseded D4 figures, without a pointer to the correction at `:845`.

**Leader action required by this approval:** `./init.sh` now exits **1**, solely on §4, *"`progress/current.md` claims a feature while none is active"*. That is the expected consequence of the `done` transition; the brief reserves `current.md` to the leader. Every other `init.sh` section printed `[OK]` or `[WARN]` (log: `scratchpad/rev71r3/init.log`).

### What I ran, and what I did not

- **No full re-run.** I re-summed `scratchpad/quality_full_clean.log` (10:39) from its `Passed!` lines: **1820**, with `[OK] quality.sh finished`. Since then, the fix round added no test. `grep -cE "\[(Fact|Theory)"` gives `SagaCommandStoreTests.cs` 12 and `SagaFirstParkDeadLetterHandlerTests.cs` 4, matching the fix round's filtered 12/12 and the 4 inside its 18/18.
- **One probe, A6**, full protocol, described below.
- **Forced builds**, one at a time, with no other `dotnet` process alive (`ps -eo pid,args | grep -E "dotnet (build|test|format)|quality\.sh" | grep -v grep` empty before each; never `pgrep -f`):
  - `tests/Orders.IntegrationTests` twice (armed, then restored);
  - `src/Orders/Orders.csproj`;
  - `tests/Orders.UnitTests`.
  - Every build: **0 Warning(s), 0 Error(s)**.
- **A doc-enabled build of `src/Orders`**, into the scratchpad only. Command: `-p:GenerateDocumentationFile=true -p:NoWarn=CS1591 -p:TreatWarningsAsErrors=false -p:OutputPath=scratchpad/rev71r3/docout/ -p:IntermediateOutputPath=scratchpad/rev71r3/docobj/`. `find src tests -type f -newermt '2026-09-11 11:27:10'` afterwards returns **0** files, so the build wrote nothing into the repository. It was needed because the ordinary build cannot answer item 5 of the brief (R3-F1).
- **Not re-run:** `dotnet format --verify-no-changes`, taken from the record (`:968`). My restore was `cmp`-identical to the pre-probe file, so the probe cannot have changed formatting.

### A6 — CLOSED, armed (R3-1)

| # | Family | Mutation | Site | Named test | Result |
|---|---|---|---|---|---|
| R3-1 | corruption: same note, different field | in the duplicate-key `catch`, after `Detached`: read the stored envelope, splice a fresh GUID over the `eventId` value (after the 12-byte `{"eventId":"` prefix, guarded to throw if the prefix is absent), and `ExecuteUpdateAsync` it back | `EfCoreSagaCommandStore.cs:66-67` | `SagaCommandStoreTests.EnqueueAsync_ADuplicateEnqueueWithADifferentEnvelope_LeavesTheFirstEnvelopeInPlace` | **killed** at `SagaCommandStoreTests.cs:line 352`: `Assert.Equal() Failure: Collections differ / ↓ (pos 12) / Expected: [···, 58, 34, 102, 99, 51, ···] / Actual: [···, 58, 34, 54, 102, 100, ···]` |

- **It failed on the bytes, not the note.** The stack names `:352`, the byte assertion, so the note assertion at `:347` passed under the mutation. That is exactly the blind spot A6 named. The difference sits at byte 12, the first byte of the `eventId` value.
- **Protocol:**
  - `cp -p` backup to `scratchpad/rev71r3/EfCoreSagaCommandStore.cs.bak`, then `cmp` differed at line 67 (armed);
  - `dotnet build --no-incremental`, then one test, **1 failed**;
  - `cp` restore, `cmp: IDENTICAL`, lines `:66-67` re-read (`Detached;` then `return EnqueueOutcome.AlreadyEnqueued;`), `touch`;
  - searching `src`/`tests` for the marker and the probe's variable name returned exit 123 (nothing);
  - forced rebuild, then the confirming green: `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.
  - Because the armed binary failed and the rebuilt one passed, the rebuild demonstrably took effect.

### D6 and D3 — CLOSED (unit: each comment site claiming operator-cancel rows carry no envelope)

**The five rewritten blocks, read:**
- `ISagaCommandStore.cs:12-31` — `null` means a row predating the columns, or an enqueue site that supplies none (none exists today). Operator-cancel rows are *"NOT such a row"*. True.
- `ISagaCommandStore.cs:56-76` — operator-cancel callers *"MUST supply the synthetic `orders.cancel.requested` envelope"*. It is nullable only because `SagaFact.TriggeringEventEnvelope` is a defaulted `byte[]?`. #7's compile-time guarantee is disclosed as not carried over. This is D5's required wording, and true.
- `SagaCommand.cs:40-52` — the same statement, plus *"written exactly once, by the enqueue's own `INSERT`"*, matching `EfCoreSagaCommandStore.cs:45` (the only write, verified in round 2). True.
- `SagaFirstParkDeadLetterHandler.cs:98-105` — the operator-cancel row is *"NOT this case … IS republished below"*. True, per `OrdersCancelAcceptanceTests.StockReserved_OperatorCancelCompensationParks_PublishesADlqCopyByteEqualToTheStoredEnvelope`.
- `SagaFirstParkDeadLetterHandlerTests.cs:118-125` — the fixture *"only covers the pre-feature/no-envelope case"*. The test body is unchanged; its `null` fixture moved `:128 → :133` because the comment grew by five lines. True.

**Enumeration 1, the retired claim's wording** (round 2's command, re-run):

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

**9 lines**, the same set as the fix round's `:783-791`. Classification:
- `ISagaCommandStore.cs:61` — the rewritten parameter doc: *"RPC-triggered, not fact-triggered, and MUST supply"* the envelope.
- `SagaCommandRequestFactory.cs:39`, `:40` — the stock-release request **payload** factory is not called by the direct enqueue (block `:30-42`, read). True, and not about envelopes.
- `OperatorCancelRequestedEnvelope.cs:17`, `:21` — D3's round-1 correction (§4.1, no R29 claim).
- `CancelOrderCommandHandler.cs:85` — D3's round-1 correction.
- `CancelOrderCommandHandler.cs:164`, `:197` — the request is built inline, not through the payload factory (`:160-166`, `:194-200`, read). `:199-200` go on to say the pair *"is the SAME id-71 synthetic envelope"*. True.
- `SagaCommand.cs:47` — the rewritten entity doc.

**Enumeration 2** (round 2's command, re-run):

```
$ find src tests -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n -i "no triggering envelope\|nothing to republish\|carries no triggering\|with no triggering\|no triggering fact\|dead-letters nothing\|feature 27's A2\|null envelope\|envelope: null\|triggeringEventEnvelope: null"
tests/Orders.UnitTests/SagaFirstParkDeadLetterHandlerTests.cs:133:        var claimed = new SagaCommandRecord(_commandId, order.Id.Value, order.OrderReference.Value, SagaCommandKind.CreditRelease, "{}", _triggeringEventId, Attempts: 0, TriggeringEventEnvelope: null, TriggeringEventTopic: null);
tests/Orders.UnitTests/SagaFactHandlerTests.cs:494:        TriggeringEventEnvelope: null,
tests/Orders.IntegrationTests/SagaFirstParkDeadLetterTests.cs:55:            await seedStore.EnqueueAsync(orderId, "ORD-000001", SagaCommandKind.StockReserve, "{}", Guid.NewGuid(), triggeringEventEnvelope: null, triggeringEventTopic: null, CancellationToken.None);
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:41:        await store.EnqueueAsync(_orderId, "ORD-000001", SagaCommandKind.StockReserve, "{}", Guid.NewGuid(), triggeringEventEnvelope: null, triggeringEventTopic: null, CancellationToken.None);
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:78:            await store.EnqueueAsync(orderId, "ORD-000002", SagaCommandKind.StockReserve, "{}", Guid.NewGuid(), triggeringEventEnvelope: null, triggeringEventTopic: null, CancellationToken.None);
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:103:        var first = await store.EnqueueAsync(orderId, "ORD-000003", SagaCommandKind.CreditHold, "{\"first\":true}", Guid.NewGuid(), triggeringEventEnvelope: null, triggeringEventTopic: null, CancellationToken.None);
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:106:        var second = await store.EnqueueAsync(orderId, "ORD-000003", SagaCommandKind.CreditHold, "{\"second\":true}", Guid.NewGuid(), triggeringEventEnvelope: null, triggeringEventTopic: null, CancellationToken.None);
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:140:        await store.EnqueueAsync(orderId, "ORD-000004", SagaCommandKind.StockRelease, "{}", Guid.NewGuid(), triggeringEventEnvelope: null, triggeringEventTopic: null, CancellationToken.None);
src/Orders/Infrastructure/Saga/SagaFirstParkDeadLetterHandler.cs:100:            // envelope (none exists today) — nothing to republish. An
src/Orders/Infrastructure/Saga/SagaFirstParkDeadLetterHandler.cs:107:                "SagaFirstParkDeadLetterHandler: row {CommandId} carries no triggering envelope; .dlq publish skipped.",
src/Orders/Application/Sagas/SagaCommandRequestFactory.cs:39:    /// enqueue for the <c>stock_reserved</c> branch has no triggering fact at
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:21:/// not require this envelope (an operator cancel has no triggering fact in
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:197:    /// for the fact-driven caller. This caller has no triggering fact at
```

**13 lines**, the same set as the fix round's `:809-821`. Classification:
- **Fixtures and seeds, not claims (8):**
  - `SagaFirstParkDeadLetterHandlerTests.cs:133` — the no-envelope fixture for rows that predate the column;
  - `SagaFactHandlerTests.cs:494` — a fact fixture;
  - `SagaFirstParkDeadLetterTests.cs:55` and `SagaCommandStoreTests.cs:41`, `:78`, `:103`, `:106`, `:140` — fact-less store seeds for lease, park and duplicate tests.
- `SagaFirstParkDeadLetterHandler.cs:100` — the rewritten comment: *"nothing to republish"* applies to the pre-feature case only.
- `SagaFirstParkDeadLetterHandler.cs:107` — the runtime log message, true on the branch it sits in.
- `SagaCommandRequestFactory.cs:39`, `OperatorCancelRequestedEnvelope.cs:21`, `CancelOrderCommandHandler.cs:197` — as classified under enumeration 1.

**The leader's pattern, and my own:**

```
$ find src tests -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -nE "RPC-triggered compensation rows\)|null.{0,40}RPC-triggered|RPC-triggered.{0,60}(no triggering|null)"; echo "exit=$?"
exit=123
```

A line `grep` cannot see a claim that wraps across comment lines, and every one of D6's five sites wrapped. So my own check works at the unit the claim is made in, the **comment block**. It joins each run of consecutive `//`/`///` lines, and reports a block when it names the subject (`CancelOrderCommandHandler|operator[- ]cancel|RPC[- ]triggered`) **and** a negation (`langword="null"|\bnull\b|no triggering|no envelope|nothing to republish|carries no`), over `src` and `tests`, with bin/obj excluded by directory name. Complete output (script inline in my transcript; blocks are file:first-last line):

```
src/Projector/Domain/Summaries.cs:82-88
src/Orders/Infrastructure/Persistence/Entities/SagaCommand.cs:40-52
src/Orders/Infrastructure/Saga/SagaFirstParkDeadLetterHandler.cs:98-105
src/Orders/Domain/Events/OrderCancelled.cs:5-20
src/Orders/Application/Ports/ISagaCommandStore.cs:12-31
src/Orders/Application/Ports/ISagaCommandStore.cs:51-77
src/Orders/Application/Ports/ISagaCommandStore.cs:147-177
src/Orders/Application/Sagas/SagaFactHandler.cs:177-191
src/Orders/Application/Sagas/SagaStepTable.cs:35-45
src/Orders/Application/Sagas/SagaCommandRequestFactory.cs:30-42
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:7-23
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:10-97
tests/Orders.UnitTests/SagaFirstParkDeadLetterHandlerTests.cs:118-125
tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs:530-540
tests/Orders.IntegrationTests/OutboxWireParityTests.cs:261-267
blocks: 15
```

**15 blocks**, each read in full. Classification:
- **The rewritten blocks (5):** `SagaCommand.cs:40-52`, `SagaFirstParkDeadLetterHandler.cs:98-105`, `ISagaCommandStore.cs:12-31`, `ISagaCommandStore.cs:51-77`, `SagaFirstParkDeadLetterHandlerTests.cs:118-125`. True, as read above; `:77` adds only that the topic is *"`null` alongside a `null` envelope"*, which is true.
- **D3's round-1 corrections (2):** `OperatorCancelRequestedEnvelope.cs:7-23` and `CancelOrderCommandHandler.cs:10-97`. The `null` match in the latter is not about envelopes.
- **`SagaCommandRequestFactory.cs:30-42`** — the payload factory, as under enumeration 1.
- **`ISagaCommandStore.cs:147-177`** — `FindOperatorCancelNoteAsync`: the synthetic envelope is inserted with the row, and the lookup returns `null` only for saga-decided cancels or when no note was given. True.
- **`SagaFactHandler.cs:177-191`** — the lookup returns `null` for the *non*-operator callers of the `Cancel` branch. True.
- **`OrderCancelled.cs:5-20`** — `Note` is `null` for saga-decided cancellations, and the async branches get it from the stored envelope. True.
- **`OrdersCancelAcceptanceTests.cs:530-540`** — *"Armed by restoring the `null` at the enqueue site"* describes the arming. True.
- **`SagaStepTable.cs:35-45`** — a `null` step lookup; unrelated.
- **`Summaries.cs:82-88`** (Projector) and **`OutboxWireParityTests.cs:261-267`** — a JSON `"note":null` is never written; unrelated to envelopes.

**No live site asserts the retired claim.** D6 is closed; D3's two hits inside it are closed with it.

### A1's count — CLOSED

```
$ find src/Orders/Application -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -nE "Infrastructure\.Messaging\.Rpc|Messaging\.Rpc"
src/Orders/Application/Ports/ISagaCommands.cs:1:using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
src/Orders/Application/Sagas/SagaCommandRequestFactory.cs:1:using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:2:using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:59:    /// Infrastructure edge). Application → <c>Infrastructure.Messaging.Rpc</c>
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:5:using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
```

- **5 lines: 4 `using` directives, in 4 files**, plus `OperatorCancelRequestedEnvelope.cs:59`, the corrected comment itself.
- My broader `grep -nE "Infrastructure"` over the same file set found no fully-qualified `Messaging.Rpc` type use. `ISagaCommands.cs:22` names the directory as a `<c>` path (`Infrastructure/Messaging/Rpc/…`), not a reference.
- The comment's **FOUR** (`OperatorCancelRequestedEnvelope.cs:59-66`) and its four file names are correct.
- **Id 76's first bullet** (`feature_list.json:1079`) names `OperatorCancelRequestedEnvelope` and its line-2 `Messaging.Rpc` import, *"amended after id 71's review round 2, advisory A1"*. Confirmed.

### D7 — CLOSED on the arithmetic; residue is R3-F2

- Record `:845-847`: *"2 ported lines (items 7, 9), 1 strengthened (item 8), 1 superseded (item 3), 15 not applicable (items 1, 2, 4, 5, 6, 10–19)"*.
- Items 1, 2, 4, 5, 6 are 5, and items 10–19 are 10, so 15.
- 2 + 1 + 1 + 15 = **19**, the line count of the command output at `:167-185`.
- Each item's own verdict at `:190-231` matches its bucket.

### Item 5 — the build, and why the build was not enough (R3-F1)

- **The ordinary builds are clean.** `src/Orders`, `tests/Orders.UnitTests` and `tests/Orders.IntegrationTests`, each with `--no-incremental`: 0 warnings, 0 errors.
- **But that proves nothing about doc comments.** `Directory.Build.props:22` is `<GenerateDocumentationFile>false</GenerateDocumentationFile>`, so the compiler only parses XML doc comments and never reports an unresolved `cref`. `TreatWarningsAsErrors` has nothing to promote.
- **So the fix round's *"Build succeeded … every rewritten `<see cref>` resolves"* (record `:965-967`)** is a claim whose check could not fail.
- **The doc-enabled scratch build answers it.** It reports 36 warnings, 32 unique by site. Histogram: 16 × CS1573, 12 × CS1574, 1 × CS1584, 3 × CS1734. Filtered to the files id 71 touched:

```
$ grep -oE "/[^ ]+\.cs\([0-9]+,[0-9]+\): warning CS1[57][0-9]{2}: .*" scratchpad/rev71r3/docbuild.log | sed -E 's# \[/[^]]*\]$##; s#^.*/order-to-cash-dotnet/##' | sort -u | grep -E "ISagaCommandStore\.cs|SagaCommand\.cs|OperatorCancelRequestedEnvelope\.cs|SagaFirstParkDeadLetterHandler\.cs|CancelOrderCommandHandler\.cs|SagaFactHandler\.cs|EfCoreSagaCommandStore\.cs|OrderCancelled\.cs"
src/Orders/Application/Commands/CancelOrderCommandHandler.cs(34,16): warning CS1574: XML comment has cref attribute 'OrderNotCancellableError' that could not be resolved
src/Orders/Application/Ports/ISagaCommandStore.cs(26,49): warning CS1574: XML comment has cref attribute 'FindOperatorCancelNoteAsync' that could not be resolved
src/Orders/Application/Ports/ISagaCommandStore.cs(79,14): warning CS1573: Parameter 'orderId' has no matching param tag in the XML comment for 'ISagaCommandStore.EnqueueAsync(Guid, string, SagaCommandKind, string, Guid, byte[]?, string?, CancellationToken)' (but other parameters do)
src/Orders/Application/Ports/ISagaCommandStore.cs(80,16): warning CS1573: Parameter 'orderReference' has no matching param tag in the XML comment for 'ISagaCommandStore.EnqueueAsync(Guid, string, SagaCommandKind, string, Guid, byte[]?, string?, CancellationToken)' (but other parameters do)
src/Orders/Application/Ports/ISagaCommandStore.cs(81,25): warning CS1573: Parameter 'command' has no matching param tag in the XML comment for 'ISagaCommandStore.EnqueueAsync(Guid, string, SagaCommandKind, string, Guid, byte[]?, string?, CancellationToken)' (but other parameters do)
src/Orders/Application/Ports/ISagaCommandStore.cs(82,16): warning CS1573: Parameter 'payload' has no matching param tag in the XML comment for 'ISagaCommandStore.EnqueueAsync(Guid, string, SagaCommandKind, string, Guid, byte[]?, string?, CancellationToken)' (but other parameters do)
src/Orders/Application/Ports/ISagaCommandStore.cs(83,14): warning CS1573: Parameter 'triggeringEventId' has no matching param tag in the XML comment for 'ISagaCommandStore.EnqueueAsync(Guid, string, SagaCommandKind, string, Guid, byte[]?, string?, CancellationToken)' (but other parameters do)
src/Orders/Application/Ports/ISagaCommandStore.cs(86,27): warning CS1573: Parameter 'cancellationToken' has no matching param tag in the XML comment for 'ISagaCommandStore.EnqueueAsync(Guid, string, SagaCommandKind, string, Guid, byte[]?, string?, CancellationToken)' (but other parameters do)
src/Orders/Application/Ports/ISagaFirstParkDeadLetterHandler.cs(22,99): warning CS1573: Parameter 'cancellationToken' has no matching param tag in the XML comment for 'ISagaFirstParkDeadLetterHandler.HandleAsync(SagaCommandRecord, int, string, CancellationToken)' (but other parameters do)
src/Orders/Application/Sagas/SagaFactHandler.cs(183,20): warning CS1574: XML comment has cref attribute 'commandStore' that could not be resolved
```

Classification:
- **`ISagaCommandStore.cs(26,49)` CS1574, `FindOperatorCancelNoteAsync`** — **id 71's fix round.** It sits inside rewritten D6 block 1, the `SagaCommandRecord` summary, where an unqualified member of `ISagaCommandStore` does not resolve. It also points at *"own remarks"*, but the target has a `<summary>`, not `<remarks>`. The other rewritten `cref`s produce no diagnostic, so they resolve: `ISagaCommandStore.EnqueueAsync` (`:28`), `SagaFact.TriggeringEventEnvelope` (`:68`), `Saga.EfCoreSagaCommandStore` (`SagaCommand.cs:44`).
- **`SagaFactHandler.cs(183,20)` CS1574, `commandStore`** — **id 71, round 1.** A primary-constructor parameter is not a `cref` target.
- **`CancelOrderCommandHandler.cs(34,16)` CS1574, `OrderNotCancellableError`** — pre-existing, `orders_cancel_responder` (feature 41).
- **`ISagaCommandStore.cs(79-86)` × 6 CS1573** — `EnqueueAsync` documents only the two `triggeringEvent*` parameters, which feature 27 added. Pre-existing.
- **`ISagaFirstParkDeadLetterHandler.cs(22,99)` CS1573** — feature 27; matched my filter only by file-name substring.

**Why R3-F1 does not block.** Neither broken `cref` states anything false: the member name is still readable in source, and the repository publishes no documentation. What *is* false is a verification claim in the record, and this review corrects it. And the defect is a **class**: 12 unresolved `cref`s in the Orders build closure alone, before anyone has looked at the other five services. Fixing id 71's two would close two lines and leave the class live. `CLAUDE.md` asks for the class to be enumerated before any instance is fixed.

**Routed, not narrated — recommended backlog entry for the leader to file** (not rooted in `specs/shared/`, so no `SA-n`):
- **Name:** `doc_comment_crefs_are_never_compiler_checked`.
- **Acceptance:**
  - (1) the class enumerated first, as a search result: a doc-enabled build (`GenerateDocumentationFile=true`, `NoWarn=CS1591`) of every project, one classification line per CS1573/CS1574/CS1584/CS1734 site, naming `ISagaCommandStore.cs:26` and `SagaFactHandler.cs:183` as id 71's;
  - (2) every unresolved `cref` fixed or rewritten as `<c>`;
  - (3) the check made standing, by enabling doc generation under `TreatWarningsAsErrors` (with CS1591 suppressed) or an equivalent `quality.sh` step;
  - (4) armed, by adding one unresolved `cref` and seeing the build fail, naming it.

### R3-F2 — superseded D4 figures left unmarked (non-blocking, record-only)

- **Record `:233-236`** still reads *"Totals: 4 ported assertions … 2 strengthened … 1 superseded … 12 not applicable"*. Its correction is at `:845`, in a later section, and nothing at `:233` points to it.
- **Record `:160`** still says round 1 *"found 12 hit lines outside that list"*, a figure round 2 withdrew.
- **Found by enumerating on the retired wording:** `grep -n "4 ported\|12 not applicable\|12 hit lines\|2 strengthened" progress/impl_operator_note_survives_the_compensation_branches.md` gives `:160`, `:233`, `:236`.
- **Not blocking:** the file is an append-only log of rounds; the correction names the location it corrects (`:841-842`); and the outcome, no guard dropped, does not change.
- **Recommended:** the leader adds a one-line *"superseded — see Review round 2 fixes, D7"* pointer at `:160` and `:233` before commit. `progress/` is the leader's to edit.

### R3-A1 — advisory

- `ISagaCommandStore.cs:77` and `SagaCommand.cs:55` describe the topic as the one the envelope *"was consumed from"*. For operator-cancel rows it was stored, not consumed.
- The value, `otc.orders.facts.v1`, is `SagaFactTopics.OrdersFacts` (`SagaFactTopics.cs:14`), so the set claim holds. Only the verb is imprecise, and the parameter doc directly above states the operator-cancel case. Fold into R3-F1's entry if convenient.

### Item 6 — the harness change's routing: confirmed

Backlog **id 77** `test_hosts_exhaust_the_per_user_inotify_limit` (`feature_list.json:1088`, `pending`, phase 14) carries round 2's suggested acceptance:
- the landed `test.runsettings` / `RunSettingsFilePath` change;
- a guard reading the built host's `IConfiguration["hostBuilder:reloadConfigOnChange"]`, armed two ways;
- confirmation under `quality.sh`'s coverage invocation;
- the measurement;
- the safety search.

Not re-probed, per the brief.

### Traceability (acceptance bullet → test)

- **Carried from round 1 (`:41-60`) and round 2 without re-walking.** Round 3 renamed, added or removed no test.
- **The one change is A6's strengthened assertion.** It serves bullet 5 (store the bytes) and the INSERT-only invariant of ledger row 3, and R3-1 proves it now guards bytes rather than only the note.
- **Nothing in this round is rooted in `specs/shared/`**, so no `SA-n` is owed.

### `CHECKPOINTS.md` (round 3)

**C1 — harness**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh`, `progress/current.md`, `progress/history.md` exist (`init.sh` §1).
- [x] `.claude/agents/` holds the five roles plus `suite_runner`.
- [x] Every agent declares its model: `implementer` sonnet, `test_maintainer` and `suite_runner` haiku; `reviewer`, `leader` and `spec_author` state in `description:` that they deliberately inherit.
- [ ] `./init.sh` exits 0 — **exits 1, solely on §4 lockstep**, because `current.md` still names id 71 after my `done` transition. The leader owns `current.md`. It exited 0 before the transition (record `:974`).

**C2 — state**
- [x] At most one `in_progress`: 0.
- [x] Statuses valid (`init.sh` §3 `[OK]`, 52/76 done).
- [x] Id 71, now `done`, has passing tests: the 1820-green log, the fix round's filtered greens, and my R3-1 confirming green.
- [ ] `progress/current.md` describes the active session — **stale after this close; the leader updates it.**
- [x] `blocked` features record why: `grep -c '"status": "blocked"'` is 0.

**C3 — architecture**
- [x] Domain purity: `Architecture.Tests` 25/25 in `quality_full_clean.log:139`. No `Domain/` file was touched by the round-2 fix round (its list, record `:980-988`).
- [x] No cross-service DB access; the round's changes stay in `src/Orders` and its tests.
- [x] No shared runtime code added; no `Domain/` reference to `OrderToCash.Cqrs` (inside the 25/25).
- [x] `SharedKernel` has zero `PackageReference`: the one `grep` hit is `SharedKernel.csproj:10`, a comment.
- [x] No `decimal` in domain arithmetic: the one hit is `src/Projector/Domain/MoneyFormat.cs:10`, a doc comment saying there is none.
- [x] No new inter-service interaction.
- [x] No stray debug logging or TODOs: `grep -n "TODO\|Console\.Write"` over the six files the fix round touched, exit 1.

**C4 — verification**
- [x] `./quality.sh` passed (10:39, 1820). Not re-run; only comments and one assertion changed since.
- [x] Domain tests pure (architecture suite).
- [x] Integration tests hit real containers; R3-1 ran against the real MS-SQL Testcontainer.
- [ ] Coverage thresholds — **not enforced anywhere.** `quality.sh:80` is `# TODO(feature 34 — sonarqube_quality_gates, phase 21): enforce >=80% domain / >=60% overall`. Pre-existing and already routed to feature 34; not id 71's.
- [x] No Jest: `package.json` search, exit 123.

**C5 — clean close**
- [x] No suspicious untracked files: `git status --porcelain --untracked-files=all | grep -E "\.(tmp|bak|orig|log)$|/bin/|/obj/"`, exit 1. The probe backup lives only in the scratchpad.
- [x] `progress/history.md` id 71 entry carries the closing effort record (updated this round).
- [x] `feature_list.json` reflects id 71's true state; only its status line is mine.
- [ ] The human told what was done and how to test it — the leader's, at session close.
- [x] No commit by the reviewer: HEAD is still `909394f`.

**C6 — SDD:** n/a (`sdd: false`).

**C7 — reuse fidelity**
- [x] `diff -rq specs/shared ../order-to-cash-nestjs/specs/shared` prints only `test-matrix.md`.
- [x] No deviation or amendment; no `R<n>` claimed.
- [x] Effort record complete and honest, with round 2's estimated end corrected to its transcript timestamp.
- n/a this feature: `n8n` workflows, the black-box API script, the README benchmark section (the leader's, at wrap-up).
