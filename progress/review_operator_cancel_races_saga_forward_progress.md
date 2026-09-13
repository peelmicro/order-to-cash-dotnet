# Review — operator_cancel_races_saga_forward_progress (id 62, phase 14) — review round 1

**Verdict: REJECTED.** Two blocking findings, both cheap: **D1** — the retired credit-first claim is still asserted at four sites, one of them production source (`SagaCommandKind.cs:15-22`); **D2** — the note bullet requires the operator's note to reach `order.cancelled.v1` *under the race*, and no raced test reads it. The production behaviour I probed is sound: 6 of 7 code mutations killed (the survivor, M5, is advisory A2), the fact → owed-command class re-derives to the record's table row for row, and bullet 0 is independently reproduced on pre-id-62 code with all four race tests failing on resource-level assertions.

Bullets are numbered 0–9 in `feature_list.json` order (0 = reproduction, 2 = R25, 4 = the note under the race, 5–9 = SA-4's).

## What I ran and what I did not

- **Not re-run:** the full `./quality.sh`. The last full run is `/tmp/claude-1000/quality_fixround1_final.log`: 18 `Passed!` lines, 1844, 0 failed, `[OK] quality.sh finished`. Fix round 2 is covered by `g_format_check.log` (0 bytes), `g_solution_build.log` (0 warnings, 0 errors, 20:02:28), `g_orders_unittests.log` (457) and `g_orders_integrationtests.log` (146). The newest `src/`/`tests/` mtime is 19:59:47, older than that build.
- **Ran instead:** seven mutations (M1–M6 and L1, each on named tests), one reproduction of bullet 0 in a scratch copy of `909394f`, the fact → owed-command enumeration, and four retired-wording enumerations. Logs are in `/tmp/claude-1000/-home-juanpabloperez-Work-Projects-Assessments-order-to-cash-dotnet/0096c34a-f6e2-40ed-9571-1199ef58ea03/scratchpad/logs/`.

## CHECKPOINTS.md

**C1 — harness**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh`, `progress/current.md`, `progress/history.md` exist (read this session).
- [ ] Agent definitions and model declarations: not walked (id 62 does not touch them).
- [x] `./init.sh` exit 0, run after this review's status transition. Output is at the bottom.

**C2 — state**
- [x] At most one `in_progress`: after this transition, only id 62 (`init.sh`).
- [x] Every status is valid (`init.sh`).
- [x] `progress/current.md` describes the active session.
- n/a — no `done` or `blocked` transition here.

**C3 — architecture**
- [x] No `Domain/` file is touched: `git status --short src tests` lists 27 entries (26 `M`, 1 `??`), none under `Domain/`. `Architecture.Tests` passed 25 in the last full run (not re-run).
- [x] No cross-service DB access. `RecordingFulfillmentStandIn` is a NATS subscriber, and `src/Fulfillment` is unchanged.
- [x] No new shared runtime project, no `Domain` → `Cqrs` reference, `SharedKernel` untouched, no `decimal`: no domain file changed.
- [x] Kafka-fact / NATS-RPC: no new interaction. `stock.release`, `credit.release` and `despatch.create` remain RPC commands; `stock.released.v1`, `credit.released.v1` and `credit.approved.v1` remain Kafka facts.
- [x] No stray debug output or TODOs. `git status --short src tests | awk '{print $2}' | xargs grep -n -E "TODO|FIXME|Console\.Write|Debug\.Write"` printed nothing (xargs exit 123 = no match).

**C4 — verification**
- [x] `quality.sh` passed at 1844 (log above, not re-run).
- [x] Domain tests are still pure (none changed).
- [x] Integration tests run on Testcontainers, and the stand-ins are real NATS subscribers.
- [ ] Coverage thresholds: the log shows `[OK] dotnet test: all tests passed` and `[OK] quality.sh finished`. I did not locate the per-layer gate line, so this is not independently verified.
- [x] No Jest (no web change).

**C5 — session**
- [x] The one untracked file is expected: `tests/Orders.IntegrationTests/RecordingFulfillmentStandIn.cs`.
- [ ] History entry: not written, because the verdict is REJECTED.
- [x] `feature_list.json` reflects the state: id 62 → `in_progress`, one line.
- [x] No commit made.

**C6 — SDD:** n/a (`sdd: false`).

**C7 — reuse fidelity**
- [x] `diff -rq specs/shared ../order-to-cash-nestjs/specs/shared` reports only `test-matrix.md`, whose last commit is `d8d71c7` and which is not in id 62's diff. SA-4's three files are byte-identical in both repositories.
- [x] SA-4 is recorded in `history.md` (uncommitted, awaiting the human — settled).
- [x] The R25 reuse is genuine: M3 killed. A2 records a gap.
- n/a — n8n, API script and effort record (rejection).

## Traceability — bullet × case × arm (population: `python3 -c "…len(f['acceptance'])"` → **10**)

| # | Bullet | Named cases | Arm that saw each case fail | Does the message name the claim? |
|---|---|---|---|---|
| 0 | race reproduced on today's code | `Confirmed_ReleaseWins`, `Confirmed_DespatchWins`, `StockReserved_LateApproval_AfterStockReleased`, `StockReserved_LateApproval_BeforeStockReleased` | A2 (After/Before, `credit.release` count 1→0); F2 (DespatchWins `:282`, 0→1); F4 (Before `:489`); **R0 (reviewer): all four on `909394f`** (probe 6) | yes (A1 no — it failed on the planned-list order) |
| 1 | defence stated with its cost | `OperatorCancelRowLockConcurrencyTests.ConcurrentGetByIdAsync_…(firstActorIsOperatorCancel: True)`, `(False)` | pass-1 rows 5–6 (record `:180-181`, lock call deleted) — not re-run | yes |
| 2 | R25 keeps ignoring; no retry storm | `Confirmed_OperatorFirst_LateDespatchedV1IsIgnoredByPreconditionUnmet`; `SagaFactHandlerTests.CreditApprovedV1_AtCancelledWithASagaDecidedReason_IsIgnoredByPreconditionUnmet(StockRejected)`, `(CreditRejected)`; `SagaPreconditionTests.R25_EachOfTheTenConsumedFacts_…` (pre-existing, at `completed`); `SagaStepTableTests.ForStatus_ReturnsNull…` ×2, `…ExposesThreeVariants…` ×2; `Confirmed_ReleaseWins` (PRECONDITION_FAILED is terminal) | **M3** kills `(CreditRejected)`; A4 kills `Confirmed_ReleaseWins` (`:110`, retry storm); **M5 survived** (A2) | yes |
| 3 | armed: remove the fix, the reproducing test fails | as bullet 0 | A1, A2, F2, F4, R0 | yes |
| 4 | note lookup under the race, by content | `SagaCommandStoreTests.FindOperatorCancelNoteAsync_UnderTheRace_TheOperatorsNoteIsOnTheCommandThatSortsSecond_…` | A5 (`a5_mutated2.log`: `Expected: "Operator cancelled — under the race, the "··· Actual: null`) | yes — but **D2**: no raced case reads the note |
| 5 | SA-4: stock first; `compensationPlanned`; `stock.released.v1` owes `credit.release`; `credit.released.v1` cancels; tests updated by content | `CancelOrderCommandHandlerTests.CreditApprovedOrConfirmed_EnqueuesStockReleaseOnly_…` ×2; `OrdersCancelAcceptanceTests.CreditApprovedOrConfirmed_IssuesStockReleaseStrictlyBeforeCreditRelease_…`; `OrdersCancelPayloadTests.OrdersCancelReplyPayload_CompensationPending_…`; `SagaStepTableTests.StockReleasedV1_CreditApprovedOrConfirmedVariant_LeavesStatusUntouchedAndOwesCreditRelease` ×2; `SagaFactHandlerTests.StockReleasedV1_…OwesCreditReleaseAsANoOpAdvance` ×2; `SagaFactCommandHandlerTests.StockReleasedV1_…PublishesStockReleasedForCancellationRecorded` ×2; `SagaStepTableTests.CreditReleasedV1_…BothCompensationStepsInReleaseOrder` ×2; `SagaFactHandlerTests.CreditReleasedV1_…CancelsWithStepsInStockThenCreditOrder` ×2; Gateway `OperatorNoteReachesTimelineEndToEndTests` (confirmed, credit_approved) | A6 (handler ×2); **M1** (ReleaseOrder ×2); **M2** (4 cases); **M6** (the three `Confirmed` cases) | yes |
| 6 | confirmed branch, both outcomes, arbitrating recording stand-in | `Confirmed_ReleaseWins`, `Confirmed_DespatchWins` | ReleaseWins: A4 (`:110`), F3 (`:129` → now `:145`, `DeadLetteredAt`), R0 (`:117`); DespatchWins: A1, F2 (`:282`), R0 (`:278`) | yes |
| 7 | stock_reserved branch, both orderings, recording Billing | `…AfterStockReleased`, `…BeforeStockReleased`, `…WithTheSweeperDisabled_…` | A2 (both); F4 (Before `:489`); G1 (sweeper-disabled `:598`); R0 (After `:385`, Before `:485`) | yes |
| 8 | supersede removed; no refusal; synthetic-envelope selection armed by substitution | `SagaCommandStoreTests.HasAcceptedOperatorCancelAsync_ARealFactEnvelopeOnStockRelease_ReturnsFalse`, `…TheSyntheticOperatorEnvelopeOnStockRelease_ReturnsTrue`; `SagaFactHandlerTests.CreditApprovedV1_LateForAnAcceptedOperatorCancel_AtStockReserved_…`, `…AtCancelledOperatorCancelled_…`, `…AtStockReservedWithNoAcceptedOperatorCancel_…`; supersede removal by enumeration (probe 4, E1) | A3 (ReturnsFalse, "any row"); **M4** (ReturnsTrue, sibling token); **M3** (AtCancelledOperatorCancelled) | yes |
| 9 | no dispatch gate; row lock stays; ledger guard | `SagaCommandDispatcher.cs` and `SagaCommandDispatchWorker.cs` absent from `git status`; `EfCoreOrderRepository.cs:106` `WITH (UPDLOCK, ROWLOCK)`; `DespatchCreateTests.Concurrency_DespatchCreateRacingASimultaneousStockRelease_ExactlyOneWinsAndEmitsExactlyOneFact` | A7 attempt 2 (`a7_mutated2.log`, red); **L1** (reverse direction, green — probe 5) | yes |

**Every bullet × case with no arm.** This list is the whole population above, not a sample.
- **Bullet 2:**
  - `Confirmed_OperatorFirst_…`, which record `:33` says passes with or without the fix.
  - `ForStatus_ReturnsNull…` ×2 and `…ExposesThreeVariants…` ×2, both structural.
  - **No case exists** for `stock.released.v1` or `credit.released.v1` at `cancelled` (A2).
- **Bullet 5:**
  - `OrdersCancelPayloadTests.OrdersCancelReplyPayload_CompensationPending_…`.
  - `OrdersCancelAcceptanceTests.CreditApprovedOrConfirmed_IssuesStockReleaseStrictlyBeforeCreditRelease_…`: its `:263` ordering assertion was never recorded failing, because A1 ran against `Confirmed_DespatchWins` only.
  - The three `stock.released.v1` unit tests' `CreditApproved` cases (M6 armed `Confirmed` only).
  - The Gateway E2E theory.
- **Bullet 6:**
  - `Confirmed_ReleaseWins`: the `order.saga_failed.v1` count (`:173-174`) and the credit-release/cancel tail (`:157-177`).
  - `Confirmed_DespatchWins`: the `despatched` status (`:278`) and zero `order.cancelled.v1` (`:284-285`).
- **Bullet 7:** `…AfterStockReleased`'s F4 absences (`:397-400`) — unarmed, and I accept the reasoning at record `:789`.
- **Bullet 8:** `SagaFactHandlerTests` `…AtStockReserved_…` and `…AtStockReservedWithNoAcceptedOperatorCancel_…` (unit). F4's integration arm covers the fall-through.

None of these is a basis for the rejection.

## Probe 2 — independent mutations

Every row followed the same protocol:
1. `cp -p` a backup to `scratchpad/bak/`.
2. Mutate by exact string, with a Python helper that asserts exactly one occurrence.
3. `dotnet build <project> --no-incremental`.
4. `dotnet test --no-build --filter <named cases>`.
5. Restore with `cp` from the backup, then `cmp`.
6. `touch` the file and run `dotnet build --no-incremental` again.
7. Re-run the same filter green.

Only one build or test was alive at a time; `pgrep -a dotnet | grep -E " (build|test|format)( |$)"` was empty before each.

| # | Family | Mutation | Named test(s) | Verbatim result | Restore |
|---|---|---|---|---|---|
| M1 | payload corruption (`order.cancelled.v1` reason) | `SagaStepTable.cs:135` `"order_cancelled" => CancellationReason.OperatorCancelled` → `CreditRejected` | `SagaStepTableTests.CreditReleasedV1_CreditApprovedOrConfirmedVariant_CancelsWithReasonOperatorCancelledAndBothCompensationStepsInReleaseOrder` | 2/2 FAIL: `OrderToCash.Orders.Domain.Errors.CancellationReasonNotApplicableError : Cancellation reason 'credit_rejected' does not apply to order status 'confirmed'.` **Killed.** The domain's own table (`Order.cs:459-465`) allows only `OperatorCancelled` from these statuses, so no wrong reason can reach the wire silently. | `cmp` identical; 2/2 green |
| M2 | ordering (compensation steps) | `SagaStepTable.cs:159-173` swaps the two `OrderCompensationStep` entries | the above + `SagaFactHandlerTests.CreditReleasedV1_CreditApprovedOrConfirmedVariant_CancelsWithStepsInStockThenCreditOrder` | 4/4 FAIL: `Assert.Equal() Failure: Values differ Expected: StockReleased Actual: CreditReleased`. **Killed.** | `cmp` identical; 4/4 green |
| M3 | substitution (a valid reason) | `SagaFactHandler.cs:97` `order.CancellationReason == Domain.CancellationReason.OperatorCancelled` → `CreditRejected` | `SagaFactHandlerTests.CreditApprovedV1_LateForAnAcceptedOperatorCancel_AtCancelledOperatorCancelled_IssuesCreditReleaseOnly`; `…CreditApprovedV1_AtCancelledWithASagaDecidedReason_IsIgnoredByPreconditionUnmet` | 2 of 3 FAIL: the late case `Expected: Processed Actual: Ignored`; `(reason: CreditRejected)` `Expected: Ignored Actual: Processed`; `(StockRejected)` passed as expected. **Killed in both directions.** | `cmp` identical; 3/3 green |
| M4 | substitution (sibling command token) | `EfCoreSagaCommandStore.cs:377` inside `HasAcceptedOperatorCancelAsync`: `StockReleaseToken = "stock.release"` → `"credit.release"` | `SagaCommandStoreTests.HasAcceptedOperatorCancelAsync_TheSyntheticOperatorEnvelopeOnStockRelease_ReturnsTrue` | 1/1 FAIL: `Assert.True() Failure Expected: True Actual: False` at `SagaCommandStoreTests.cs:428`. **Killed.** | `cmp` identical; 1/1 green |
| M5 | a correct no-op turned into a throw (R25 → retry storm) | `SagaFactHandler.cs:150`, inside `if (matchedStep is null)`: `if (order.Status == Domain.OrderStatus.Cancelled && fact.EventType is "stock.released.v1" or "credit.released.v1") { throw new InvalidOperationException("M5 probe: …"); }` | the full `Orders.UnitTests` suite | `Passed!  - Failed: 0, Passed: 457, Skipped: 0, Total: 457`. **Survived** (A2). | `cmp` identical; 457/457 green |
| M6 | substitution (the command that status already owes) | `SagaStepTable.cs:225` `new SagaStep.Advance(OrderStatus.Confirmed, Apply: null, SagaCommandKind.CreditRelease)` → `DespatchCreate` | `SagaStepTableTests.StockReleasedV1_…LeavesStatusUntouchedAndOwesCreditRelease`; `SagaFactHandlerTests.StockReleasedV1_…OwesCreditReleaseAsANoOpAdvance`; `SagaFactCommandHandlerTests.StockReleasedV1_…PublishesStockReleasedForCancellationRecorded` | 3 of 6 FAIL (every `Confirmed` case): `Assert.Equal() Failure: Values differ Expected: CreditRelease Actual: DespatchCreate`. **Killed.** | `cmp` identical; 6/6 green |
| L1 | lock, reverse direction | `EfCoreStockItemRepository.cs:68` `FROM   dbo.reservations WITH (UPDLOCK, HOLDLOCK)` → `FROM   dbo.reservations`; the `dbo.stock` hints (`:51`, `:98`) kept | `DespatchCreateTests.Concurrency_DespatchCreateRacingASimultaneousStockRelease_ExactlyOneWinsAndEmitsExactlyOneFact` | `Passed!  - Failed: 0, Passed: 1`. **Green** (probe 5). | `cmp` identical; 1/1 green |

**Why these seven.**
- **M1 and M2** attack the two payload fields SA-4 now builds from a different fact.
- **M3, M4 and M6** substitute a valid sibling: a real reason, a real command token, and the command the status already owes. That is the family whose green suite hides correct behaviour aimed at the wrong target.
- **M5** is bullet 2, restricted to the two facts whose variant sets SA-4 rewrote.
- **L1** is the ledger claim's other direction.

**Byte-identity at close.** Every file I mutated compares identical to its backup:
```
$ cmp src/Orders/Application/Sagas/SagaStepTable.cs bak/SagaStepTable.cs.m1   → identical
$ cmp src/Orders/Application/Sagas/SagaStepTable.cs bak/SagaStepTable.cs.m2   → identical
$ cmp src/Orders/Application/Sagas/SagaFactHandler.cs bak/SagaFactHandler.cs.m3   → identical
$ cmp src/Orders/Application/Sagas/SagaFactHandler.cs bak/SagaFactHandler.cs.m5   → identical
$ cmp src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs bak/EfCoreSagaCommandStore.cs.m4   → identical
$ cmp (M6 restore) src/Orders/Application/Sagas/SagaStepTable.cs bak/SagaStepTable.cs.m6   → cmp: identical
$ cmp (L1 restore) src/Fulfillment/Infrastructure/Persistence/EfCoreStockItemRepository.cs bak/EfCoreStockItemRepository.cs.a7r   → cmp: identical
```
`git status --short src tests` lists 27 entries, the same set as at the start.

## Probe 3 — the fact → owed-command class, re-derived by content

```
$ grep -n -E 'SagaCommandKind\.[A-Z][A-Za-z]+|"[a-z]+\.[a-z_]+\.v1"|EventType ==|PublishAsync\(|IEventHandler<|public sealed record|signal\.Signal\(' src/Orders/Application/Sagas/SagaStepTable.cs src/Orders/Application/Sagas/SagaFactHandler.cs src/Orders/Application/Commands/SagaFactCommandHandlers.cs src/Orders/Application/Sagas/SagaDispatchEvents.cs src/Orders/Application/Sagas/OrderSagas.cs src/Orders/Application/Commands/CancelOrderCommandHandler.cs | grep -v -E '^\S+:[0-9]+:\s*///'
src/Orders/Application/Sagas/OrderSagas.cs:16:public sealed class OrderPlacedFactRecordedHandler(ISagaCommandSignal signal) : IEventHandler<OrderPlacedFactRecorded>
src/Orders/Application/Sagas/OrderSagas.cs:20:        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.StockReserve));
src/Orders/Application/Sagas/OrderSagas.cs:25:public sealed class OrderMarkedStockReservedHandler(ISagaCommandSignal signal) : IEventHandler<OrderMarkedStockReserved>
src/Orders/Application/Sagas/OrderSagas.cs:29:        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.CreditHold));
src/Orders/Application/Sagas/OrderSagas.cs:34:public sealed class CreditRejectionRecordedHandler(ISagaCommandSignal signal) : IEventHandler<CreditRejectionRecorded>
src/Orders/Application/Sagas/OrderSagas.cs:38:        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.StockRelease));
src/Orders/Application/Sagas/OrderSagas.cs:43:public sealed class OrderConfirmedBySagaHandler(ISagaCommandSignal signal) : IEventHandler<OrderConfirmedBySaga>
src/Orders/Application/Sagas/OrderSagas.cs:47:        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.DespatchCreate));
src/Orders/Application/Sagas/OrderSagas.cs:52:public sealed class OrderMarkedDespatchedHandler(ISagaCommandSignal signal) : IEventHandler<OrderMarkedDespatched>
src/Orders/Application/Sagas/OrderSagas.cs:56:        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.InvoiceIssue));
src/Orders/Application/Sagas/OrderSagas.cs:70:public sealed class StockReleasedForCancellationRecordedHandler(ISagaCommandSignal signal) : IEventHandler<StockReleasedForCancellationRecorded>
src/Orders/Application/Sagas/OrderSagas.cs:74:        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.CreditRelease));
src/Orders/Application/Sagas/OrderSagas.cs:87:public sealed class LateCreditApprovalForCancellationRecordedHandler(ISagaCommandSignal signal) : IEventHandler<LateCreditApprovalForCancellationRecorded>
src/Orders/Application/Sagas/OrderSagas.cs:91:        signal.Signal(new SagaCommandRef(@event.OrderId, SagaCommandKind.CreditRelease));
src/Orders/Application/Commands/SagaFactCommandHandlers.cs:41:            await dispatcher.PublishAsync(new OrderPlacedFactRecorded(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
src/Orders/Application/Commands/SagaFactCommandHandlers.cs:54:            await dispatcher.PublishAsync(new OrderMarkedStockReserved(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
src/Orders/Application/Commands/SagaFactCommandHandlers.cs:88:            object dispatchOwedEvent = enqueued.Command == SagaCommandKind.CreditRelease
src/Orders/Application/Commands/SagaFactCommandHandlers.cs:92:            await dispatcher.PublishAsync(dispatchOwedEvent, cancellationToken).ConfigureAwait(false);
src/Orders/Application/Commands/SagaFactCommandHandlers.cs:105:            await dispatcher.PublishAsync(new CreditRejectionRecorded(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
src/Orders/Application/Commands/SagaFactCommandHandlers.cs:126:            await dispatcher.PublishAsync(new StockReleasedForCancellationRecorded(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
src/Orders/Application/Commands/SagaFactCommandHandlers.cs:139:            await dispatcher.PublishAsync(new OrderMarkedDespatched(enqueued.OrderId, command.Fact.CorrelationId), cancellationToken).ConfigureAwait(false);
src/Orders/Application/Sagas/SagaDispatchEvents.cs:13:public sealed record OrderPlacedFactRecorded(Guid OrderId, Guid CorrelationId);
src/Orders/Application/Sagas/SagaDispatchEvents.cs:15:public sealed record OrderMarkedStockReserved(Guid OrderId, Guid CorrelationId);
src/Orders/Application/Sagas/SagaDispatchEvents.cs:17:public sealed record CreditRejectionRecorded(Guid OrderId, Guid CorrelationId);
src/Orders/Application/Sagas/SagaDispatchEvents.cs:19:public sealed record OrderConfirmedBySaga(Guid OrderId, Guid CorrelationId);
src/Orders/Application/Sagas/SagaDispatchEvents.cs:21:public sealed record OrderMarkedDespatched(Guid OrderId, Guid CorrelationId);
src/Orders/Application/Sagas/SagaDispatchEvents.cs:35:public sealed record StockReleasedForCancellationRecorded(Guid OrderId, Guid CorrelationId);
src/Orders/Application/Sagas/SagaDispatchEvents.cs:52:public sealed record LateCreditApprovalForCancellationRecorded(Guid OrderId, Guid CorrelationId);
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:180:            signal.Signal(new SagaCommandRef(command.OrderId, kind));
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:215:            SagaCommandKind.StockRelease,
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:222:        return outcome == EnqueueOutcome.Enqueued ? SagaCommandKind.StockRelease : null;
src/Orders/Application/Sagas/SagaFactHandler.cs:36:    private const string CreditApprovedEventType = "credit.approved.v1";
src/Orders/Application/Sagas/SagaFactHandler.cs:94:                if (fact.EventType == CreditApprovedEventType)
src/Orders/Application/Sagas/SagaFactHandler.cs:103:                        var creditReleasePayloadJson = SagaCommandRequestFactory.BuildJson(SagaCommandKind.CreditRelease, order);
src/Orders/Application/Sagas/SagaFactHandler.cs:107:                            SagaCommandKind.CreditRelease,
src/Orders/Application/Sagas/SagaFactHandler.cs:116:                            enqueued = new SagaCommandRef(order.Id.Value, SagaCommandKind.CreditRelease);
src/Orders/Application/Sagas/SagaFactHandler.cs:122:                                SagaCommandKind.CreditRelease,
src/Orders/Application/Sagas/SagaFactHandler.cs:215:                    var payloadJson = command == SagaCommandKind.StockRelease
src/Orders/Application/Sagas/SagaStepTable.cs:164:            EventType: "stock.released.v1",
src/Orders/Application/Sagas/SagaStepTable.cs:178:            "order.placed.v1",
src/Orders/Application/Sagas/SagaStepTable.cs:179:            new SagaStep.Advance(OrderStatus.Placed, Apply: null, SagaCommandKind.StockReserve));
src/Orders/Application/Sagas/SagaStepTable.cs:182:            "stock.reserved.v1",
src/Orders/Application/Sagas/SagaStepTable.cs:186:                SagaCommandKind.CreditHold));
src/Orders/Application/Sagas/SagaStepTable.cs:189:            "stock.rejected.v1",
src/Orders/Application/Sagas/SagaStepTable.cs:196:            "credit.approved.v1",
src/Orders/Application/Sagas/SagaStepTable.cs:204:                SagaCommandKind.DespatchCreate));
src/Orders/Application/Sagas/SagaStepTable.cs:207:            "credit.rejected.v1",
src/Orders/Application/Sagas/SagaStepTable.cs:208:            new SagaStep.Advance(OrderStatus.StockReserved, Apply: null, SagaCommandKind.StockRelease));
src/Orders/Application/Sagas/SagaStepTable.cs:221:            "stock.released.v1",
src/Orders/Application/Sagas/SagaStepTable.cs:224:                new SagaStep.Advance(OrderStatus.CreditApproved, Apply: null, SagaCommandKind.CreditRelease),
src/Orders/Application/Sagas/SagaStepTable.cs:225:                new SagaStep.Advance(OrderStatus.Confirmed, Apply: null, SagaCommandKind.CreditRelease),
src/Orders/Application/Sagas/SagaStepTable.cs:229:            "order.despatched.v1",
src/Orders/Application/Sagas/SagaStepTable.cs:233:                SagaCommandKind.InvoiceIssue));
src/Orders/Application/Sagas/SagaStepTable.cs:236:            "invoice.issued.v1",
src/Orders/Application/Sagas/SagaStepTable.cs:243:            "payment.received.v1",
src/Orders/Application/Sagas/SagaStepTable.cs:256:            "credit.released.v1",
src/Orders/Application/Sagas/SagaStepTable.cs:270:        yield return Pair("order.confirmed.v1", new SagaStep.Skip());
src/Orders/Application/Sagas/SagaStepTable.cs:271:        yield return Pair("order.completed.v1", new SagaStep.Skip());
src/Orders/Application/Sagas/SagaStepTable.cs:272:        yield return Pair("order.cancelled.v1", new SagaStep.Skip());
src/Orders/Application/Sagas/SagaStepTable.cs:273:        yield return Pair("order.saga_failed.v1", new SagaStep.Skip());
```

**Derived table.** Fact → owed command (enqueue site) → event published → command signalled.

| Fact | Owed command (enqueue site) | Event published | Signalled |
|---|---|---|---|
| `order.placed.v1` | `StockReserve` (`SagaStepTable:179`) | `OrderPlacedFactRecorded` (`:41`) | `StockReserve` (`OrderSagas:20`) |
| `stock.reserved.v1` | `CreditHold` (`:186`) | `OrderMarkedStockReserved` (`:54`) | `CreditHold` (`:29`) |
| `credit.approved.v1`, ordinary advance | `DespatchCreate` (`:204`) | `OrderConfirmedBySaga` (`:88-92`) | `DespatchCreate` (`:47`) |
| `credit.approved.v1`, late branch | `CreditRelease` (`SagaFactHandler:107`) | `LateCreditApprovalForCancellationRecorded`, chosen at `:88` by `enqueued.Command` | `CreditRelease` (`:91`) |
| `credit.rejected.v1` | `StockRelease` (`:208`) | `CreditRejectionRecorded` (`:105`) | `StockRelease` (`:38`) |
| `stock.released.v1` at `credit_approved`/`confirmed` | `CreditRelease` (`:224-225`) | `StockReleasedForCancellationRecorded` (`:126`) | `CreditRelease` (`:74`) |
| `order.despatched.v1` | `InvoiceIssue` (`:233`) | `OrderMarkedDespatched` (`:139`) | `InvoiceIssue` (`:56`) |
| `stock.rejected.v1`, `invoice.issued.v1`, `payment.received.v1`, `credit.released.v1` (all three variants) | none | plain delegation | — |

The RPC-triggered site is the one non-fact entry: `CancelOrderCommandHandler:215` enqueues `StockRelease`, returned at `:222` and signalled at `:180`.

**Classification: matches the record's F1 table (record `:712-726`) row for row, and no mismatch remains.** M6's third failing case ties `HandleStockReleasedFactCommandHandler`'s enqueue to `CreditRelease` by test.

## Probe 4 — retired wording, enumerated by content, on both retired claims

Two notes on method. Every command excludes `bin/`, `obj/` and `node_modules/` by path: `find … -not -path` or directory pruning, never output filtering. Enumerations E3 and E4 join each line with the next two after stripping comment markers (and, for E4, XML tags), because single-line and tag-sensitive patterns both missed hits in this round.

**E1 — supersede, single line**
```
$ find src tests specs apps n8n scripts -type f \( -name '*.cs' -o -name '*.md' -o -name '*.yaml' -o -name '*.yml' -o -name '*.json' -o -name '*.ts' -o -name '*.tsx' -o -name '*.sql' \) -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/node_modules/*' -print0 | xargs -0 grep -n -i -E 'supersed|HasPendingCompensation|pending compensation|PendingCompensation'
specs/outbox_and_idempotency/tasks.md:100          → harness `.superseded-rules` sweep — unrelated
specs/shared/requirements.md:221                   → R28 "pending compensation is a credit rejection" — unrelated
specs/order_saga_orchestrator/requirements.md:24   → R28, same — unrelated
specs/order_saga_orchestrator/design.md:268        → R28, same — unrelated
specs/order_saga_orchestrator/design.md:374        → feature 42's taxonomy table "superseded" — unrelated
tests/Orders.IntegrationTests/OperatorCancelRacesSagaForwardProgressTests.cs:376  → historical ("no longer exists") — correct
tests/Orders.IntegrationTests/OperatorCancelRacesSagaForwardProgressTests.cs:608  → historical ("the retired supersede guard") — correct
src/Orders/Application/Ports/ISagaIgnoredFactRecorder.cs:5                        → historical ("removed with its only producer") — correct
src/Orders/Application/Sagas/SagaFactHandler.cs:176, :177, :185                    → historical ("SA-4 … retired it") — correct
src/Orders/Application/Commands/CancelOrderCommandHandler.cs:45, :55, :69          → historical ("RETIRED it") — correct
```
**Supersede: no live claim.**

**E2 — credit first, single line** (same `find`, same exclusions)
```
$ … | xargs -0 grep -n -i -E 'credit[._]release[a-z]*"?\]?,? *"?\[?stock[._]release|credit[._]releas.{0,80}(then|before|first|followed by).{0,80}stock[._]releas|CreditThenStock|CreditReleasedForCancellationRecorded|BeginCreditReleaseCompensation|credit[- ]first|credit hold first|release (the )?credit (hold )?first|StockReleaseReasonFor.{0,40}credit\.released'
specs/shared/saga.md:247                                              → SA-4's own text calling credit-first wrong — correct
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:316            → lexicographic sort of two tokens — correct
tests/Gateway.IntegrationTests/OperatorNoteReachesTimelineEndToEndTests.cs:341 → "pre-SA-4 shape (credit first, then stock)" historical — correct
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:288          → lookup precedence, not enqueue order — correct
src/Orders/Application/Sagas/SagaStepTable.cs:144                     → SA-4 rationale — correct
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:30, :31 → historical ("was retired with it") — correct
```

**E3 — credit first, three-line window** (`scratchpad/joined_grep.py`, roots `src tests specs docs apps n8n scripts README.md`)
```
$ python3 joined_grep.py '(credit[._ ]releas\w*|credit hold|credit)\W{0,6}(\w+\W+){0,6}?(then|before|first|followed by|, then)\W+(\w+\W+){0,4}?stock[._ ]releas|credit[._]release\W{1,6}stock[._]release|CreditThenStock|CreditReleasedForCancellationRecorded|BeginCreditReleaseCompensation|credit[- ]first' src tests specs docs apps n8n scripts README.md
src/Orders/Application/Commands/OperatorCancelRequestedEnvelope.cs:30, :31  → as E2 — correct
src/Orders/Application/Sagas/SagaCommandKind.cs:19                          → **LIVE retired claim (D1 site 1)**
src/Orders/Application/Sagas/SagaStepTable.cs:144                           → as E2 — correct
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:288                → as E2 — correct
tests/Gateway.IntegrationTests/OperatorNoteReachesTimelineEndToEndTests.cs:341 → as E2 — correct
tests/Orders.IntegrationTests/OperatorCancelRacesSagaForwardProgressTests.cs:418 → window false positive ("credit.release only … the compensation then completes") — correct
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:316, :353            → sort order — correct
tests/Orders.UnitTests/SagaFactHandlerTests.cs:434                          → **LIVE retired order (D1 site 3)**
tests/Orders.UnitTests/SagaFactHandlerTests.cs:495                          → R27 credit.rejected path — correct
specs/shared/asyncapi.yaml:3223, specs/shared/openapi.yaml:1379             → `enum` item lists; each description beside them states stock first — correct
specs/shared/openapi.yaml:1553, specs/shared/saga.md:201                    → R28 credit.rejected path — correct
```

**E4 — the claim's other phrasings, three-line window, XML tags stripped** (`scratchpad/joined_grep_notags.py`)
```
$ python3 joined_grep_notags.py 'only\W+CancelOrderCommandHandler|both of its enqueue sites|its two enqueue sites|two direct enqueue|enqueues it directly|never reachable from credit\.release|CancelOrderCommandHandler\W+(\w+\W+){0,6}?(enqueues|enqueue)\W+(\w+\W+){0,4}?credit\.release|credit\.released\.v1 then stock\.released\.v1|credit hold is released FIRST|no fact-driven\W+(\w+\W+){0,3}?row ever names|stock\.released\.v1\W+(\w+\W+){0,4}?(completes|completed) the (cancellation|chain)' src tests specs docs apps n8n scripts README.md
src/Orders/Application/Ports/ISagaCommandStore.cs:23                  → **LIVE: "both of its enqueue sites" (D1 site 2)**
src/Orders/Application/Sagas/SagaCommandKind.cs:18, :20, :22          → **LIVE (D1 site 1)**
tests/Gateway.IntegrationTests/OrdersHttpTests.cs:207                 → "only CancelOrderCommandHandlerTests at the unit level", about test coverage — unrelated
tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs:339      → stock_reserved branch — correct
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:323, :324      → **LIVE inverted claim (D1 site 4)**
tests/Orders.UnitTests/CancelOrderCommandHandlerTests.cs:116          → stock_reserved branch — correct
tests/Orders.UnitTests/SagaFactHandlerTests.cs:434                    → **LIVE (D1 site 3)**
```
A first, tag-sensitive version of E4 also hit `SagaStepTable.cs:254` ("the pre-SA-4 shape, where this fact type was the no-op first hop"). That line is historical, and correct.

## Probe 5 — the Fulfillment lock ledger row (record `:574-580`)

- **#7 half — correct.** Read from `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs`:
  - `apps/fulfillment/src/application/stock-reservation.handler.ts:103` `const stockIds = await this.stock.stockIdsOfOrder(orderReference);`, and `:109` `const items = await this.stock.lockByIdsForOrder(tx, stockIds, orderReference);`.
  - `despatch-creation.handler.ts:64` `stockIdsOfOrder`, and `:70` `lockByIdsForOrder`.
  - One lock, shared by both handlers, as the row says.
- **#8 half — correct.**
  - `LockForOrderAsync` is at `EfCoreStockItemRepository.cs:36`.
  - It is called by `StockReservationService.cs:32` and `:93`, and by `DespatchCreationService.cs:55` (grep output this session).
  - The hints are at `:51` (`dbo.stock WITH (UPDLOCK, HOLDLOCK, ROWLOCK)`) and `:68` (`dbo.reservations WITH (UPDLOCK, HOLDLOCK)`).
- **The guard executes that code.**
  - `DespatchCreateTests.cs:231-232` sends a real `despatch.create` and a real `stock.release` to a real Fulfillment host over NATS.
  - With both hints removed, A7 attempt 2 went red (`a7_mutated2.log`, 17:45:14): `exactly one of despatch.create / stock.release must win the race for ORD-900000 (despatch reply: {"orderReference":"ORD-900000","despatchReference":"DES-000001",…,"created":true,…`.
- **Both directions, now run.** The record ran one:

  | Stock hints | Reservations hint | Result | Source |
  |---|---|---|---|
  | removed | kept | green | A7 attempt 1, `a7_mutated.log` |
  | kept | removed | green | **L1**, this review |
  | removed | removed | red | A7 attempt 2 |

  **Either lock alone serialises one order's release against its despatch.** Both callers lock the same stock rows first and then the same reservation rows. So record `:594` ("the reservations lock is what actually arbitrates release vs. despatch for one order") names one direction as the mechanism. A1, advisory. The ledger row itself does not carry that sentence, and both of its halves stand.
- **Caveat.** The guard is a 10-iteration real race, so a green is weaker evidence than a red. The unserialised mutation failed on iteration 0, which shows the race does manifest when nothing serialises it.

## Probe 6 — bullet 0

**The evidence on file does not reproduce the stranding by itself.**
- Pass 1's verbatim failures (`:25-31`) name the rejected mechanism (`expected a 'superseded' saga_ignored_facts row …`), not a stranded resource.
- Pass 2 did not reproduce first (`:513`, disclosed).
- A1 failed on the planned-list order.
- A2 and F2 are resource-level, but they are mutations of the fixed tree, not today's code.

**Reviewer reproduction, R0.**
- **Base.** `git archive 909394f | tar -x -C scratchpad/pre62` — the commit before id 62's first pass (`d8d71c7` contains that pass).
- **The code really is credit-first there.** `CancelOrderCommandHandler.cs:164` enqueues `SagaCommandKind.CreditRelease`, and `SagaStepTable.cs:235-236` gives `credit.released.v1`'s `credit_approved`/`confirmed` variants `StockRelease`.
- **Tests copied in.** Today's `OperatorCancelRacesSagaForwardProgressTests.cs` and `RecordingFulfillmentStandIn.cs`.
- **Scratch-only variant edits.** Two are forced: `909394f` predates observability and the group-clear helper, so the first build failed with `CS1061 'SagaCommand' does not contain a definition for 'DeadLettered…'` and `CS0117 … StopHostAndWaitForGroupToClearAsync`. Three more move each failure onto a resource claim. Complete `diff` against today's file:
```
<             Assert.Equal(["stock_release", "credit_release"], reply.CompensationPlanned);        (×2, both confirmed tests)
<                 Assert.Null(despatchRow.DeadLetteredAt);
<             Assert.Equal(["despatch.create", "stock.release"], fulfillment.CommandsProcessed);   (Confirmed_DespatchWins)
<             await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);   (×6)
>             await host.StopAsync(); host.Dispose();                                               (×6)
```
- **Run.** Build 0 errors (`pre62_build2.log`), then the four tests (`pre62_test.log`): **4/4 FAIL**, each on a resource assertion.

  | Test | Variant line | Verbatim failure | What it shows |
  |---|---|---|---|
  | `Confirmed_ReleaseWins` | `:117` `Assert.Equal(["stock.release"], fulfillment.CommandsProcessed)` | `Collections differ … Expected: ["stock.release"] Actual: []` | the contested stock was never released first |
  | `Confirmed_DespatchWins` | `:278` `Assert.Equal(0, creditReleaseRowCount)` | `Expected: 0 Actual: 1` | a credit release issued on an order that then despatched (#7's Finding 1) |
  | `StockReserved_LateApproval_AfterStockReleased` | `:385` `Assert.Equal(1, creditReleaseRowCount)` | `Expected: 1 Actual: 0` | the late-approved hold is stranded |
  | `StockReserved_LateApproval_BeforeStockReleased` | `:485` `Assert.Equal(0, earlyDespatchCreateRowCount)` | `Expected: 0 Actual: 1` | the order kept progressing: `despatch.create` issued after the cancel was accepted |

**Judgement: bullet 0 is met,** on this reproduction. None of the four failing assertions depends on the two features `909394f` lacks.

## Probe 7 — determinism (all six `[Fact]`s in `OperatorCancelRacesSagaForwardProgressTests.cs`)

- **`Confirmed_ReleaseWins`** — forced by a gate. `RecordingFulfillmentStandIn.cs:201` awaits `HoldDespatchCreateGate` before touching state; the test calls `Reset` at `:89` and `Open` at `:123`.
  - There is one wall-clock bound (A3). The held `despatch.create` attempt times out at `Command.TimeoutMs = 10_000` (`:52`). Cancel plus the sweeper's claim of `stock.release` (`Sweeper.IntervalMs = 500`, `SagaIntegrationTestSupport.cs:58`) must fit inside that window. Otherwise a retried `despatch.create` queues behind the gate, and `CommandsProcessed` gains a second entry.
  - That is a margin of roughly 20×, not a sleep standing in for an ordering.
- **`Confirmed_DespatchWins`** — sequencing: it waits for `despatch.create` to be `sent` (`:244`) before cancelling. The `Task.Delay(1s)` at `:273` only gives an absent `credit.release` time to appear, as the comment at `:271-272` says.
- **`…AfterStockReleased`** — sequencing: it waits for `cancelled` (`:365`) before the late fact. `Task.Delay(1s)` at `:380` is for absent effects, stated.
- **`…BeforeStockReleased`** — sequencing: `stock.release` sent (`:465`), then `credit.release` sent (`:474`), then `stock.released.v1` published (`:500`).
- **`…WithTheSweeperDisabled_…`** and **`Confirmed_OperatorFirst_…`** — sequencing only.

There are no repetition loops. `WaitForSagaCommandCountAsync` returns 0 silently on timeout (`SagaIntegrationTestSupport.cs:285-301`). Every such wait is followed by an assertion that reads the awaited state, so a timeout still fails the test, as G1 and F4's second attempt show.

## Findings

### D1 — BLOCKING — the retired credit-first claim is still asserted at four sites, one in production source

1. **`src/Orders/Application/Sagas/SagaCommandKind.cs:15-22`** (production; last commit `14d9a66`, outside id 62's diff). It says: *"the credit hold is released FIRST when an operator cancels an order that is credit_approved/confirmed, before stock.release follows. No fact-driven SagaStepTable row ever names this as a CommandAfter — CancelOrderCommandHandler enqueues it directly."*
   - The ordering is false: SA-4 releases stock first.
   - The second sentence is false: `SagaStepTable.cs:224-225` names `CreditRelease` as `CommandAfter`.
   - The third is false: `CancelOrderCommandHandler` never enqueues `credit.release`, only `StockRelease` (`:215`).
2. **`src/Orders/Application/Ports/ISagaCommandStore.cs:21-25`** (changed by id 62): *"CancelOrderCommandHandler's operator-cancel compensation rows … both of its enqueue sites carry the synthetic … envelope."* Since SA-4 there is one enqueue site, `BeginStockReleaseCompensationAsync`. The record (`:550`) and `CancelOrderCommandHandler.cs:100-101` both say so.
3. **`tests/Orders.UnitTests/SagaFactHandlerTests.cs:433-439`**, on `CreditApprovedOrConfirmedVariant_CreditReleasedV1_ReadsTheOperatorNoteBackFromTheStore_…`: *"the TWO-hop chain (credit.released.v1 then stock.released.v1) … the FIRST hop is a no-op Advance."* The body (`:453-460`) runs `stock.released.v1` first. The comment states the retired order over a test asserting the new one.
4. **`tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:321-327`**, the rationale of the store-level guard for bullet 4: a `credit.release` row carrying a real fact's envelope is *"never reachable from credit.release in production today since only CancelOrderCommandHandler ever enqueues it."* Under SA-4 this is inverted. `CancelOrderCommandHandler` never enqueues `credit.release`, and every `credit.release` row now comes from a fact-driven path (`SagaFactHandler.cs:104`, `:218`) with a real fact's envelope. The shape this test seeds is the production shape of both SA-4 interleaves: release-wins at `confirmed`, and a late approval at `stock_reserved`.

**Why it matters.** `CLAUDE.md` requires a retired claim to be enumerated on its own wording, and all four sites are exactly what #9 would read as settled. Id 71's review round 2 (D6) was rejected on this class alone. Both earlier sweeps missed these sites for the same reasons: the implementer's F5 command (record `:793`) and the leader's pattern were single-line and token-based (`credit_release`, `CreditThenStock`, `BeginCreditReleaseCompensation`), site 1 is outside the diff, and XML markup hides sites 2 and 4 from `\W`-bounded patterns.

### D2 — BLOCKING — bullet 4 ("under the race … the note that reaches `order.cancelled.v1` is the operator's own"): no raced test reads the note

- **No raced test reads it.** `Confirmed_ReleaseWins` (`:111`, `:113`), `…BeforeStockReleased` (`:456`, `:458`) and `…AfterStockReleased` (`:352`, `:354`) each send an operator note, and none reads `order.cancelled.v1`'s payload.
- **Two of them exercise the case that matters.** In `Confirmed_ReleaseWins` and `…BeforeStockReleased`, a `credit.release` row carrying a real fact's envelope exists when the cancellation completes. `FindOperatorCancelNoteAsync` checks that row first (`EfCoreSagaCommandStore.cs:312-315`). That is the one interleave where content-versus-position selection decides the outcome.
- **What covers the claim today:**
  - a store-level test that seeds rows and does not race (its rationale is D1 site 4, inverted);
  - the non-raced `OrdersCancelAcceptanceTests.Confirmed_WithANote_…` (`:405`).

The composition is therefore guarded and a real defect is unlikely. The gap is the bullet's own letter, which a raced test can close with one assertion.

### Advisories (non-blocking)

- **A1 — the ledger mechanism is stated in one direction.** Record `:594` and `:598` attribute the arbitration to the reservations lock; L1 shows the stock lock alone also arbitrates (probe 5). Correct the sentence to "either lock alone serialises; removing both fails". The rule is `CLAUDE.md`'s *"the probe runs both ways or the row states which way it was run"*.
- **A2 — M5 survived; bullet 2 has no case for the two facts SA-4 rewired, at `cancelled`.** Enumerated population:
  - `SagaPreconditionTests` covers all ten facts at `completed` only.
  - `Confirmed_OperatorFirst` covers `order.despatched.v1` at `cancelled`.
  - `SagaCompensationStockRejectedTests:94` covers `stock.rejected.v1` at `cancelled`.
  - The unit tests cover `credit.approved.v1` at `cancelled` and `order.despatched.v1` at `placed`.

  The structural precondition-set tests pin the table, so only a handler-level regression gets through. Recommended: a unit `[Theory]` of `stock.released.v1` and `credit.released.v1` against `Cancelled`/`OperatorCancelled`, expecting `Ignored`, `precondition_unmet`, nothing enqueued and no throw, armed with M5.
- **A3 — `Confirmed_ReleaseWins` rests on the 10 s timeout margin (probe 7).** State that bound in the test's comment.
- **A4 — "`stock.release` releases nothing" is not observable in `Confirmed_DespatchWins`.** The stand-in records which commands it processed, not their outcomes, and the test never publishes `stock.released.v1` whatever the arbitration decided. What is guarded is the Orders side (no `credit.release` without the fact). Recording the reply outcome and asserting `already_released` would make bullet 6's "releases nothing" a test claim.
- **A5 — bullet 7 says the stand-in Billing "RECORDS holds and releases".** `…AfterStockReleased` records holds (`:311-320`, `:350`); `…BeforeStockReleased` does not.
- **A6 — bullet 1's uncontended cost is asserted, not measured.** Record `:76-77` says "well under a millisecond", but `OperatorCancelRowLockConcurrencyTests` measures blocking, not overhead. The bullet asks only for a statement.

**Routing.** No finding's root cause is `specs/shared/`. SA-4's §4.3 text and the three §5 rows match the code as built. No `SA-n` is needed. Backlog id 79 (#7 alignment) and id 80 (head-of-line blocking) are already filed. If A2–A5 are not folded into the fix round, the leader should decide whether to file them; I have not written `feature_list.json` beyond the status line.

## What must change before re-review

1. **D1.**
   - Correct the four sites.
   - Re-enumerate on the **retired** wording, with lines joined across breaks and XML tags stripped. E3 and E4 above, or equivalents, will do.
   - Paste the complete output and classify every hit.
2. **D2.**
   - In `Confirmed_ReleaseWins` and `StockReserved_LateApproval_BeforeStockReleased`, assert that the `order.cancelled.v1` outbox payload's `note` equals the operator's note.
   - Arm with one mutation of `FindOperatorCancelNoteAsync` that returns the `credit.release` row's extraction whenever that row exists (selection by command, not by content).
   - Show both raced tests fail at the note assertion, verbatim; then restore, `cmp`, force a rebuild, and confirm green.
3. **Recommended in the same round:** A2's test armed by M5; A1's record sentence; A3's comment; A5's hold recording.
4. **Verification:** `dotnet format --verify-no-changes`, the solution build, `Orders.UnitTests` and `Orders.IntegrationTests` (the round touches Orders only). Reconcile counts by name against 457 and 146.

## Settled items checked, not re-adjudicated

- **Gateway scope exception.** `git diff` shows the two wait/publish blocks swapped plus a six-line SA-4 comment (+11/−5), and that is correct under SA-4.
- **Id 80.** `Confirmed_ReleaseWins` documents its sweeper dependence at `:74-88`. No id-62 claim is wrong because of it: bullet 6 is proven through the sweeper path, and the fast-path claim is proven separately by the sweeper-disabled test (G1).
- **No gate, no refusal.** `CancelOrderCommandHandler.cs:149-167` has no refusal branch, and neither `SagaCommandDispatcher.cs` nor `SagaCommandDispatchWorker.cs` is modified.

## Time

Reviewer: one session, 2026-09-11 18:17:24Z → ≈18:55Z (20:17 → ≈20:55 CEST), ≈38 min. It included seven mutation cycles, one pre-id-62 build and run (≈4 min), and no full suite. No history entry is written, because the verdict is REJECTED.

## Status transition and init.sh

**`feature_list.json`: one line changed.** Line 859, id 62, went from `"status": "in_review",` to `"status": "in_progress",`, edited with `sed -i '859s/…/…/'` after checking the line's exact content. No other line was touched, and the file was not rewritten.

**Check.** I snapshotted `git diff -- feature_list.json` before the edit and compared it with the diff after:
```
$ diff fl_before.diff fl_after.diff
2c2
< index d54907b..06c038a 100644
---
> index d54907b..8eec80b 100644
5,12c5
< @@ -856,13 +856,18 @@
<        "phase": 14,
<        "title": "An operator cancel reads the order status …",
<        "sdd": false,
< -      "status": "in_progress",
< +      "status": "in_review",
<        "acceptance": [
<          "the race is reproduced by a test that FAILS on today's code — …",
---
> @@ -862,7 +862,12 @@
```
- **What it shows.** Id 62's status line is now equal to HEAD's value, so its hunk has left the diff. Every other hunk (the leader's SA-4 acceptance lines and backlog entries) is byte-identical apart from the shifted hunk header.
- **Parse.** The JSON parses: id 62 is `in_progress`, and the counts are `{'done': 53, 'pending': 25, 'in_progress': 1}`.

**`./init.sh` exit 0** (`scratchpad/logs/init_review.log`):
```
[OK]    1 feature in_progress: operator_cancel_races_saga_forward_progress
[OK]    SDD coherence: 8 sdd feature(s) past pending have their triple-doc
[OK]    progress: 53/79 features done
[OK]    shared spec byte-identical to #7 across 6 file(s); test-matrix.md exempt (per-assessment Status column)
```
No build or test process of mine is left running (`pgrep -a dotnet | grep -E " (build|test|format)( |$)"` prints nothing). No commit was made.

---

# Review round 2 — 2026-09-12

**Verdict: APPROVED.** Both round-1 blockers are closed, and I verified each independently rather than on the record's word: I re-derived the D1 enumeration with my own commands and added a new one keyed to a different wording, I armed D2 with a *different* mutation from the implementer's, and I re-applied M5 myself to A2's new `[Theory]`. All six advisories are addressed. One new finding, **advisory**: a single stale sentence of the D1 class survives at `SagaCommandStoreTests.cs:270-272`, found only by the new enumeration, and it is routed below rather than left to "the next feature".

## What I ran, and what I did not

- **Not re-run:** the full `./quality.sh`. The last full run remains `quality_fixround1_final.log` (18 projects, 1844, 0 failed). Fix rounds 2 and 3 touched Orders only, and their evidence is `r1_format_check.log` (0 bytes), `r1_solution_build.log` (0 warnings, 0 errors), `r1_orders_unittests.log` (**459**) and `r1_orders_integrationtests.log` (**146**).
- **Ran myself:** one integration arm (2 tests × 2 runs), one unit arm (2 cases), one full `Orders.UnitTests` (**459/459**), and four content enumerations.
- **Arithmetic, stated rather than implied:** 1844 + A2's two unit cases = **1846**. No full suite has been run at 1846; the figure is by name, from `r1_orders_unittests.log`'s 459 against fix round 1's 457.

## Probe 8 — D1 re-derived independently (my own commands, not the record's quotation)

I re-ran my round-1 E3 and E4 and added **E5** and **E6**, keyed to wordings neither side had searched. Roots `src tests specs docs apps n8n scripts README.md`; `bin/`, `obj/` and `node_modules/` pruned by directory, never by output filtering.

**E3 (joined 3-line window) — 14 hits, all legitimate.** `OperatorCancelRequestedEnvelope.cs:30-31` historical; `ISagaCommandStore.cs:28` this round's correction, past tense; `SagaStepTable.cs:144` SA-4's own rationale; `EfCoreSagaCommandStore.cs:288` and `SagaCommandStoreTests.cs:316`, `:358` lookup precedence and string sort; `OperatorNoteReachesTimelineEndToEndTests.cs:341` historical; `OperatorCancelRacesSagaForwardProgressTests.cs:451` window false positive; `SagaFactHandlerTests.cs:496` R27's path; `asyncapi.yaml:3223`, `openapi.yaml:1379`, `:1553`, `saga.md:201` R28's `credit.rejected.v1` path, spec unchanged.

**E4 (joined, XML tags stripped) — 5 hits, all legitimate.** `SagaCommandKind.cs:27` is now the corrected text; `OrdersHttpTests.cs:207` is about test coverage; `OrdersCancelAcceptanceTests.cs:339` and `CancelOrderCommandHandlerTests.cs:116` are the `stock_reserved` branch; `SagaCommandStoreTests.cs:325` is this round's correction.

**The four corrected sites, read and checked against the code, not against the record:**
- `SagaCommandKind.cs:15-35` now says stock is released **first**, cites `SagaStepTable` `:224-225` as the row that **does** name `CreditRelease` as a `CommandAfter`, and says `CancelOrderCommandHandler` enqueues only `StockRelease` (`:215`). All three match `SagaStepTable.cs:224-225` and `CancelOrderCommandHandler.cs:215`, and my round-1 probe-3 table.
- `ISagaCommandStore.cs:21-29` now says SA-4 leaves exactly **one** enqueue site, `BeginStockReleaseCompensationAsync`. Matches `CancelOrderCommandHandler.cs:204-223`.
- `SagaFactHandlerTests.cs:432-440` now states the chain as `stock.released.v1` first, then `credit.released.v1`, which is what the body at `:453-460` runs.
- `SagaCommandStoreTests.cs:312-339` now states that `CancelOrderCommandHandler` never enqueues `credit.release`, that every such row comes from a fact-driven path, and that the seeded shape is the production shape of both SA-4 interleaves. Correct.

**E5 (widened claim wording) and E6 (envelope assignment) — the new passes.** E6 is the one that matters: it asks *which row carries the synthetic envelope and which carries a real fact's bytes*, a wording neither my round-1 patterns nor the fix round's re-run could match.

```
$ python3 joined_grep_notags.py 'stock\.release\W+(\w+\W+){0,8}?(REAL|real fact)|credit\.release\W+(\w+\W+){0,8}?synthetic|synthetic\W+(\w+\W+){0,8}?credit\.release|carrying a REAL|real fact.{0,3}s (own )?(envelope|bytes)|credit-held chain|note-less REAL envelope' src tests specs docs apps n8n scripts README.md
src/Orders/Application/Ports/ISagaCommandStore.cs:167      → the credit.release row carries THAT real fact's bytes — correct under SA-4
src/Orders/Application/Ports/ISagaCommandStore.cs:186, :191 → HasAcceptedOperatorCancelAsync's content rule — correct
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:337, :361 → content test and R27's real-envelope case — correct
tests/Orders.IntegrationTests/LogCorrelationTests.cs:119   → traceparent, unrelated
tests/Orders.IntegrationTests/OperatorCancelRacesSagaForwardProgressTests.cs:188 → D2's own new comment — correct
tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs:233 → "over the real wire", unrelated
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:207 → describes THIS test's seeded fixture — acceptable (see F1's note)
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:270-272 → **STALE: the pre-SA-4 shape in the present tense (F1)**
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:276  → "the retired version" — historical, correct
tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:322, :326-327, :330, :356, :377, :564 → this round's corrections and fixture prose — correct
```

## Probe 9 — D2, armed with a different mutation from the implementer's

Their arm changed the **selection rule**. To test the same claim from another direction I deleted the **note read** itself, so a passing test would have to be reading the note from somewhere else.

- **Mutation** (`SagaFactHandler.cs`, `ApplyStepAsync`): `var note = await commandStore.FindOperatorCancelNoteAsync(order.Id.Value, cancellationToken).ConfigureAwait(false);` → `string? note = null;`
- **Named tests:** `Confirmed_ReleaseWins`, `StockReserved_LateApproval_BeforeStockReleased`.
- **Result — both FAIL at the note assertion, not at a timeout** (`scratchpad/logs/r2d2_test.log`, `Failed: 2, Passed: 0`):
  - `expected the order.cancelled.v1 outbox payload to carry "note": "Retailer requested cancellation just as despatch.create was already in flight.", but it carries no note key at all. payload: {"orderReference":"ORD-000001","retai…` — at `OperatorCancelRacesSagaForwardProgressTests.cs:198`.
  - `expected the order.cancelled.v1 outbox payload to carry "note": "Buyer cancelled while credit approval was still in flight.", but it carries no note key at all. payload: {…}` — at `:571`.
- **Restore:** `cmp` reported identical (`CMP_IDENTICAL` in the log), forced rebuild, then both tests green (`r2d2_green.log`, `Passed: 2`).

**The interleave is real in both tests, checked in the code rather than in the comment.** In `Confirmed_ReleaseWins` the `credit.release` row is enqueued by `stock.released.v1`'s Advance variant, so it carries that fact's own envelope; in `…BeforeStockReleased` it is enqueued by the late-approval branch (`SagaFactHandler.cs:104-112`) carrying `credit.approved.v1`'s envelope. In both, the operator's note sits on the `stock.release` row, and `FindOperatorCancelNoteAsync` reads the `credit.release` row **first** (`EfCoreSagaCommandStore.cs:312-315`) — so content-versus-position genuinely decides the outcome. The implementer's own arm (`d2_mutated.log`) shows the same two failures under the selection mutation, at the as-run lines `:188` and `:544`.

## Probe 10 — A2's `[Theory]`, and M5 re-applied by me

- **All four named properties are asserted** (`SagaFactHandlerTests.cs:614-636`): `Assert.Equal(SagaFactOutcome.Ignored, result.Outcome)`; `Assert.Single(ignoredFacts.Records)` with `Assert.Equal(SagaIgnoredFactMarker.PreconditionUnmet, record.Marker)`; `Assert.Empty(store.Enqueued)` and `Assert.Null(result.Enqueued)`; and "no throw" is structural — the mutation makes the method itself throw, which xUnit reports as the failure. `[InlineData("stock.released.v1")]` and `[InlineData("credit.released.v1")]`, against `Cancelled`/`OperatorCancelled`.
- **I re-applied M5 exactly where I specified it in round 1** — inside `if (matchedStep is null)` in `SagaFactHandler.cs` — and ran that theory alone:
  - `Failed: 2, Passed: 0`, both with `System.InvalidOperationException : M5 probe: a late release fact at cancelled now throws instead of being ignored`.
  - Restored, `cmp` identical, forced rebuild, then the **full** `Orders.UnitTests`: `Passed: 459, Failed: 0`.
- This is the mutation that survived in round 1 against 457 tests. It now kills, and the test that kills it is the one A2 asked for.

## Probe 11 — the remaining advisories

- **A1 — two directions now on record.** The corrected paragraph states that either lock alone serialises and only removing both fails, and tabulates all three combinations, crediting the reverse direction to my L1. That matches what I measured.
- **A3 —** the margin is stated above `configureSaga` (`:54-62`): the 10 000 ms window against the 500 ms sweeper interval, ≈20×, and what would catch a breach.
- **A4 —** `RecordingFulfillmentStandIn` now records each `stock.release` reply's own outcome (`:36`, `:66`, `:185-197`), and `Confirmed_DespatchWins:291` asserts `["already_released"]`. Bullet 6's "releases nothing" is a test claim now, not an absence.
- **A5 —** `…BeforeStockReleased` records `credit.hold` (`:460`, `:466`) and asserts it (`:498`), as `…AfterStockReleased` already did.
- **A6 —** the uncontended cost is labelled an estimate, with what `OperatorCancelRowLockConcurrencyTests` does and does not measure (`record :75-86`).

## Finding

### F1 — ADVISORY — one sentence of the D1 class survives, and it is the pre-SA-4 shape in the present tense

`tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:270-272`:

> *"…so the ordinary credit-held chain has `stock.release` carrying a REAL fact's bytes (no note), which `FindOperatorCancelNoteAsync_ARealFactEnvelope_ReturnsNull` already covers."*

- **It was exactly true before SA-4.** Then the operator cancel enqueued `credit.release` with the synthetic envelope, and `stock.release` arrived later from the fact-driven step carrying `credit.released.v1`'s real bytes. I confirmed that shape in the pre-id-62 tree in round 1 (`909394f`, `SagaStepTable.cs:235-236`).
- **Under SA-4 it is inverted** for the chain it names: in a credit-held operator cancellation the `stock.release` row carries the **synthetic** envelope and the `credit.release` row carries a real fact's bytes (`CancelOrderCommandHandler.cs:204-223`, `SagaFactHandler.cs:104`, `:218`).
- **The test it points at models a different chain.** `FindOperatorCancelNoteAsync_ARealFactEnvelope_ReturnsNull` (`:497-499`) seeds a `StockRelease` row with a `credit.rejected.v1` envelope — R27's path, where the hold was *rejected*, so no credit is held at all.
- **Why it is advisory, not blocking.** It is a doc comment on a defensive test; the sentence's purpose (why the test seeds both rows synthetic) survives; the reading is ambiguous enough that a charitable one is defensible; and no behaviour, assertion or ledger row depends on it. Every behavioural claim in this feature is now armed and killed.
- **Why it is still worth writing down.** It is the same class as D1 — a retired shape asserted in the present tense — in the same file, and it escaped three sweeps (mine in round 1, the fix round's re-run of my two commands, and the leader's) because all three searched *release order*, and this sentence is about *envelope assignment*. That is `CLAUDE.md`'s own lesson: enumerate on the wording of the claim being retired, and where a thing has been wrong twice, enumerate for both.
- `SagaCommandStoreTests.cs:206-211` is **not** a second instance: it describes that test's own seeded fixture ("for this order"), not a production shape. It would read better with an SA-4 note, and the correction below can take it in the same pass.

**Routing — named, not deferred.** The tree is uncommitted, so this does not need a review round: the leader should dispatch `test_maintainer` (text-only, its constitutional scope) to correct `SagaCommandStoreTests.cs:270-272` — "under SA-4 the operator-cancel chain carries the synthetic envelope on `stock.release` and a real fact's bytes on `credit.release`; the R27 credit-rejected chain is the one `…ARealFactEnvelope_ReturnsNull` covers" — before the feature's commit, re-running `Orders.IntegrationTests` only if the file's assertions change (they need not). If the leader would rather not open a pass, it belongs in the backlog as its own numbered entry. I have not written `feature_list.json` beyond the status line, so the choice is the leader's; what it may not become is "the next feature that touches this file".

## CHECKPOINTS.md — round 2

- **C1** — [x] harness files present; [x] `./init.sh` exit 0, run after the transition (output below); [ ] agent definitions not walked (untouched by id 62).
- **C2** — [x] one feature `in_progress` before this transition, and after it id 62 is `done` with none left `in_progress` (`init.sh`); [x] statuses valid; [x] `current.md` describes the active session.
- **C3** — [x] no `Domain/` file touched (27 modified files, none under `Domain/`); [x] no cross-service DB access; [x] no new shared project, no `Domain` → `Cqrs`, `SharedKernel` untouched, no `decimal`; [x] Kafka-fact / NATS-RPC classification unchanged; [x] no stray debug or TODO.
- **C4** — [x] `quality.sh` green at 1844 at the last full run, plus Orders-only verification at 459/146 for rounds 2–3; [x] domain tests pure; [x] Testcontainers throughout; [ ] coverage gate line still not located in the log, so still not independently verified; [x] no Jest.
- **C5** — [x] one expected untracked test file; [x] history entry with its effort record written at this approval; [x] `feature_list.json` reflects the state; [x] no commit made by me.
- **C6** — n/a (`sdd: false`).
- **C7** — [x] `diff -rq specs/shared ../order-to-cash-nestjs/specs/shared` reports only `test-matrix.md` (Status column), with SA-4 now committed in both repositories (#8 `1affd4a`, #7 `63f130e`); [x] the amendment is recorded in `history.md` and the README registry; [x] R25's reuse is genuine, and now has a `cancelled` case of its own; [x] effort record written.

## Byte-identity at close

```
$ cmp src/Orders/Application/Sagas/SagaFactHandler.cs scratchpad/bak/SagaFactHandler.cs.r2d2      → identical
$ cmp src/Orders/Application/Sagas/SagaFactHandler.cs scratchpad/bak/SagaFactHandler.cs.r2a2      → identical
$ cmp src/Orders/Application/Sagas/SagaFactHandler.cs /tmp/claude-1000/arm_backups/SagaFactHandler_a2.cs → identical
$ grep -rn "M5 probe" src tests   (bin/obj excluded by path)                                      → no output
```
`SagaFactHandler.cs` is the only file whose mtime postdates the fix round's verification runs, and it is byte-identical to the state those runs covered — which is what makes `r1_orders_integrationtests.log`'s 146 still apply to the tree as it stands. `git status --short src tests` lists 28 entries (27 modified, 1 expected untracked); the extra modified file against round 1 is `SagaCommandKind.cs`, D1 site 1. Nothing of mine is running.

## Time

Round 2: 2026-09-12 03:33:17Z → ≈03:52Z (05:33 → ≈05:52 CEST), ≈19 minutes, one session, including one integration arm (two tests, mutated and restored), one unit arm, one full `Orders.UnitTests` run and four enumerations. Round 1 was ≈38 minutes. Reviewer total ≈57 minutes across both rounds.

## Status transition and init.sh — round 2

**`feature_list.json`: one line changed.** Line 859, id 62, `"status": "in_review",` → `"status": "done",`, edited with `sed -i '859s/…/…/'` after checking the line's exact content. The file was not rewritten.

**Check — diff before against diff after:**
```
$ diff fl_before_r2.diff fl_after_r2.diff
2c2
< index 8eec80b..06c038a 100644
---
> index 8eec80b..fcd7bec 100644
10c10
< +      "status": "in_review",
---
> +      "status": "done",
```
One value changed, on one line, inside the hunk that was already there. Everything else in the diff — the leader's SA-4 acceptance bullets and the id 79 and 80 entries — is untouched.

**Parse:** the file parses, 79 entries, id 62 `done`, counts `{'done': 54, 'pending': 25}` — no feature left `in_progress` or `in_review`.

**`./init.sh` exit 1, on a lockstep that is the leader's to clear, not a defect in this feature** (`scratchpad/logs/init_r2.log`):
```
[OK]    no feature in_progress
[OK]    SDD coherence: 8 sdd feature(s) past pending have their triple-doc
[OK]    progress: 54/79 features done
[FAIL]  progress/current.md claims a feature while none is active: "**Feature:** `operator_cancel_races_saga_forward_progress` (id 62, phase 14)"
[OK]    backlog tripwire: no feature lost, no done reverted
[OK]    shared spec byte-identical to #7 across 6 file(s); test-matrix.md exempt (per-assessment Status column)
```
Every backlog and spec check passes. The single failure is `init.sh`'s §4 lockstep: `progress/current.md` still names id 62 as the active feature now that none is `in_progress`. `current.md` is the leader's file and outside a reviewer's single-line remit, so it is **routed to the leader to reset**, exactly as the same condition was at id 71's close. It is not a finding against id 62.

**Byte-identity at close.** Every file I mutated in either round is identical to its backup:
```
$ cmp src/Orders/Application/Sagas/SagaFactHandler.cs            bak/SagaFactHandler.cs.r2a2          → identical
$ cmp src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs   bak/EfCoreSagaCommandStore.cs.m4     → identical
$ cmp src/Fulfillment/…/EfCoreStockItemRepository.cs             bak/EfCoreStockItemRepository.cs.a7r → identical
$ cmp src/Orders/Application/Sagas/SagaStepTable.cs              bak/SagaStepTable.cs.m6              → identical
```
`git status --short src tests` lists 28 entries (27 modified, 1 expected untracked). Nothing of mine is running, and I made no commit.

**Effort record:** written to `progress/history.md` at this approval, with the pass table, the 1821 → 1846 reconciliation and the note that #7 declined this defect, so there is no ratio.
