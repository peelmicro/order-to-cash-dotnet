using Microsoft.Extensions.Logging;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Application.Sagas;

/// <summary>
/// The ONE generic transactional unit (design.md §5.1) — the ten
/// <c>ICommandHandler&lt;Handle...FactCommand&gt;</c> wrappers (§5.3, group
/// G) are one-line delegations to this. Composes the EXISTING, UNMODIFIED
/// <c>IdempotentConsumer</c> — through <see cref="IIdempotentSagaRunner"/>,
/// its thin fakeable seam — with the aggregate's command methods; issues NO
/// dispatch and NO RPC — the dispatch-owed event publish happens strictly
/// after this returns (§5.5), in the wrapping command handler.
/// </summary>
public sealed class SagaFactHandler(
    IOrderRepository orders,
    IIdempotentSagaRunner idempotentRunner,
    ISagaIgnoredFactRecorder ignoredFactRecorder,
    ISagaCommandStore commandStore,
    ISagaCompletionRecorder completionRecorder,
    SagaCommandRequestFactory requestFactory,
    IClock clock,
    ILogger<SagaFactHandler> logger)
{
    /// <summary>
    /// Id 62/SA-4 — the one <c>eventType</c> that needs a check BEFORE the
    /// generic status-precondition dispatch below: an order whose operator
    /// cancellation was already accepted can still receive a late
    /// <c>credit.approved.v1</c> for the hold issued before that
    /// cancellation (saga.md §4.3, "A credit approval that arrives after the
    /// cancellation"). Left to the generic dispatch, a <c>StockReserved</c>
    /// order would silently take the ORDINARY Advance (approve, confirm,
    /// dispatch <c>despatch.create</c>) — exactly the fact this feature
    /// exists to stop stranding a hold or resurrecting a cancelled order.
    /// </summary>
    private const string CreditApprovedEventType = "credit.approved.v1";

    public async Task<SagaFactResult> HandleAsync(SagaFact fact, CancellationToken cancellationToken)
    {
        var variants = SagaStepTable.Variants(fact.EventType);

        // Absent or [Skip] — no I/O. Unreachable in practice (SagaFactsConsumer
        // filters self-produced facts before any dispatch, SO2), and the
        // belt-and-braces is deliberate (design.md §5.1 step 1). A Skip row
        // is always single-variant (SagaStepTable never pairs it with
        // anything else), so this check is enough to rule Skip out before
        // any order is ever loaded.
        if (variants is null || variants is [SagaStep.Skip])
        {
            return new SagaFactResult(SagaFactOutcome.Ignored, null);
        }

        var ignored = false;
        SagaCommandRef? enqueued = null;

        var outcome = await idempotentRunner.RunOnceAsync(
            fact.EventId,
            async ct =>
            {
                var order = await orders.GetByIdAsync(UniqueId.From(fact.CorrelationId), ct).ConfigureAwait(false);

                if (order is null)
                {
                    // SO8 — a fact can never legitimately precede its own
                    // order's row (R13), so this is cross-environment
                    // residue, not an ordering problem.
                    await ignoredFactRecorder.RecordAsync(
                        new SagaIgnoredFactRecord(fact.EventId, fact.EventType, OrderId: null, fact.CorrelationId, SagaIgnoredFactMarker.UnknownOrder),
                        ct).ConfigureAwait(false);
                    logger.LogWarning(
                        "Saga ignored {EventType} ({EventId}): correlationId {CorrelationId} matches no order.",
                        fact.EventType,
                        fact.EventId,
                        fact.CorrelationId);
                    ignored = true;
                    return;
                }

                // Id 62/SA-4 — checked BEFORE the generic precondition
                // dispatch (see CreditApprovedEventType's own remarks): a
                // late credit.approved.v1 for an order whose operator
                // cancellation was already accepted issues credit.release
                // and NOTHING else — no transition, no order.confirmed.v1,
                // no despatch.create. Two shapes: the order is STILL
                // StockReserved with its own operator-cancel row already
                // enqueued (the compensation's stock.release is under way
                // but has not yet completed), or the order is ALREADY
                // Cancelled with reason OperatorCancelled (the compensation
                // finished before this late fact arrived). Every OTHER
                // stale combination — Cancelled/stock_rejected,
                // Cancelled/credit_rejected, or any status with no accepted
                // operator cancel — falls through to the generic dispatch
                // below, which R25 ignores exactly as before.
                if (fact.EventType == CreditApprovedEventType)
                {
                    var lateForAnAcceptedOperatorCancel = order.Status == Domain.OrderStatus.Cancelled
                        ? order.CancellationReason == Domain.CancellationReason.OperatorCancelled
                        : order.Status == Domain.OrderStatus.StockReserved
                            && await commandStore.HasAcceptedOperatorCancelAsync(order.Id.Value, ct).ConfigureAwait(false);

                    if (lateForAnAcceptedOperatorCancel)
                    {
                        var creditReleasePayloadJson = requestFactory.BuildJson(SagaCommandKind.CreditRelease, order);
                        var lateEnqueueOutcome = await commandStore.EnqueueAsync(
                            order.Id.Value,
                            order.OrderReference.Value,
                            SagaCommandKind.CreditRelease,
                            creditReleasePayloadJson,
                            fact.EventId,
                            fact.TriggeringEventEnvelope,
                            fact.TriggeringEventTopic,
                            ct).ConfigureAwait(false);

                        if (lateEnqueueOutcome == EnqueueOutcome.Enqueued)
                        {
                            enqueued = new SagaCommandRef(order.Id.Value, SagaCommandKind.CreditRelease);
                        }
                        else
                        {
                            logger.LogWarning(
                                "Saga command {Command} for order {OrderId} was already enqueued; not signalling a second dispatch.",
                                SagaCommandKind.CreditRelease,
                                order.Id);
                        }

                        logger.LogInformation(
                            "Saga processed a LATE {EventType} ({EventId}) for order {OrderId}: an operator cancellation was already accepted, so only credit.release is owed — no transition, no despatch.",
                            fact.EventType,
                            fact.EventId,
                            order.Id);

                        // Processed, not ignored (R25's bookkeeping is for a
                        // fact whose effect is genuinely nothing) — this one
                        // has a real, durable effect even though the order's
                        // own Advance/Cancel machinery is bypassed entirely.
                        return;
                    }
                }

                // R25, generalised: for a single-variant eventType this is
                // exactly the original equality check. For credit.released.v1
                // and stock.released.v1 (more than one legal precondition
                // each) this picks the ONE variant whose precondition
                // matches the order's CURRENT status — null means none of
                // the catalogued preconditions are met, the same "ignored"
                // outcome, generalised rather than restricted to a single
                // expected value.
                var matchedStep = SagaStepTable.ForStatus(fact.EventType, order.Status);

                if (matchedStep is null)
                {
                    // The FIRST catalogued variant's precondition — for a
                    // single-variant eventType this IS the (only) expected
                    // status; for a multi-variant one it is one informative
                    // representative of several, never left null (R25's
                    // pre-existing persisted-record shape has exactly one
                    // ExpectedStatus column). The FULL set of legal
                    // preconditions is in this log line's own message,
                    // never discarded.
                    var expectedStatus = PreconditionOf(variants[0]);

                    await ignoredFactRecorder.RecordAsync(
                        new SagaIgnoredFactRecord(fact.EventId, fact.EventType, order.Id.Value, fact.CorrelationId, SagaIgnoredFactMarker.PreconditionUnmet, order.Status, expectedStatus),
                        ct).ConfigureAwait(false);
                    logger.LogInformation(
                        "Saga ignored {EventType} ({EventId}) for order {OrderId}: observed status {Observed}, expected one of [{ExpectedStatuses}].",
                        fact.EventType,
                        fact.EventId,
                        order.Id,
                        order.Status,
                        string.Join(", ", variants.Select(PreconditionOf)));
                    ignored = true;
                    return;
                }

                // Id 62's first pass had a broad "supersede any Advance"
                // guard here (HasPendingCompensationAsync). SA-4 (the
                // human-gated shared-spec amendment ruled 2026-09-11)
                // retired it: the credit_approved/confirmed race is now
                // resolved by releasing the CONTESTED resource (stock)
                // first and letting Fulfillment's own one-lock arbitration
                // decide against a despatch already requested (saga.md
                // §4.3, "The despatch already requested"), and the
                // stock_reserved race is resolved by the CreditApprovedEventType
                // check above. No Advance step needs superseding any more.
                var owedCommand = await ApplyStepAsync(matchedStep, order, fact, ct).ConfigureAwait(false);

                await orders.SaveChangesAsync(ct).ConfigureAwait(false);

                // otc_saga_completion_ms (OR5/design.md §7, ported cases
                // 74-78) — recorded ONLY when THIS call is the one that
                // landed the order on a terminal status. The precondition
                // check above already guarantees at-most-once: a fact
                // arriving after the order is already terminal finds no
                // matching precondition and never reaches here (the SAME
                // mechanism that makes the compensation-completing cancel
                // record exactly one, not two — no separate bookkeeping is
                // needed). Measured from the order's own OrderDate, per
                // design.md §7's table.
                if (order.Status is Domain.OrderStatus.Completed or Domain.OrderStatus.Cancelled)
                {
                    var outcomeTag = order.Status == Domain.OrderStatus.Completed ? "completed" : "cancelled";
                    completionRecorder.Record(outcomeTag, clock.UtcNow - order.OrderDate);
                }

                if (owedCommand is { } command)
                {
                    // stock.release's own reason is contextual (R27's
                    // credit_rejected vs. the operator-cancel compensation's
                    // order_cancelled) — SagaCommandRequestFactory.BuildJson's
                    // generic overload deliberately throws for this one
                    // command; the reason-aware builder derives it from the
                    // TRIGGERING fact's own eventType (never inferred any
                    // other way).
                    var payloadJson = command == SagaCommandKind.StockRelease
                        ? requestFactory.BuildStockReleaseJson(order, fact.EventType)
                        : requestFactory.BuildJson(command, order);
                    var enqueueOutcome = await commandStore.EnqueueAsync(
                        order.Id.Value,
                        order.OrderReference.Value,
                        command,
                        payloadJson,
                        fact.EventId,
                        fact.TriggeringEventEnvelope,
                        fact.TriggeringEventTopic,
                        ct).ConfigureAwait(false);

                    if (enqueueOutcome == EnqueueOutcome.Enqueued)
                    {
                        enqueued = new SagaCommandRef(order.Id.Value, command);
                    }
                    else
                    {
                        // A duplicate-key hit means the command is already
                        // owed or already sent — logged because reaching it
                        // means a dedup record was lost (design.md §6.3).
                        logger.LogWarning(
                            "Saga command {Command} for order {OrderId} was already enqueued; not signalling a second dispatch.",
                            command,
                            order.Id);
                    }
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (outcome == IdempotentSagaRunOutcome.Duplicate)
        {
            return new SagaFactResult(SagaFactOutcome.Duplicate, null);
        }

        return new SagaFactResult(ignored ? SagaFactOutcome.Ignored : SagaFactOutcome.Processed, enqueued);
    }

    private static Domain.OrderStatus PreconditionOf(SagaStep step) => step switch
    {
        SagaStep.Advance advance => advance.Precondition,
        SagaStep.Cancel cancel => cancel.Precondition,
        _ => throw new InvalidOperationException($"Unexpected step shape {step}."),
    };

    /// <summary>
    /// Applies the step's aggregate call(s) — already known to be legal,
    /// since the precondition was checked immediately above — and returns
    /// the command it owes, if any. Instance (not <see langword="static"/>,
    /// unlike before feature <c>operator_note_survives_the_compensation_branches</c>,
    /// id 71) because the <see cref="SagaStep.Cancel"/> branch now reads
    /// <see cref="commandStore"/> for the operator's note — see
    /// <see cref="ISagaCommandStore.FindOperatorCancelNoteAsync"/>. Every
    /// SAGA-DECIDED caller of this same branch (<c>stock.rejected.v1</c>'s
    /// direct cancel, and <c>stock.released.v1</c>'s <c>StockReserved</c>
    /// variant with reason <c>credit_rejected</c>, R27) enqueued no
    /// operator-cancel row at all, so the lookup returns
    /// <see langword="null"/> for them; every OPERATOR-INITIATED caller
    /// (<c>stock.released.v1</c>'s <c>StockReserved</c> variant with reason
    /// <c>order_cancelled</c>, and — SA-4 — <c>credit.released.v1</c>'s
    /// <c>CreditApproved</c>/<c>Confirmed</c> variants) finds the note on
    /// the <c>stock.release</c> row <see cref="OrderToCash.Orders.Application.Commands.CancelOrderCommandHandler"/>
    /// enqueued directly. No branch on WHICH cancel this is needs writing
    /// here; the store already disambiguates by envelope content.
    /// </summary>
    private async Task<SagaCommandKind?> ApplyStepAsync(SagaStep step, Domain.Order order, SagaFact fact, CancellationToken cancellationToken)
    {
        switch (step)
        {
            case SagaStep.Advance advance:
                advance.Apply?.Invoke(order, fact);
                return advance.CommandAfter;

            case SagaStep.Cancel cancel:
                var note = await commandStore.FindOperatorCancelNoteAsync(order.Id.Value, cancellationToken).ConfigureAwait(false);
                order.Cancel(cancel.Reason(fact), cancel.CompensationSteps(fact), fact.OccurredAt, UniqueId.From(fact.EventId), note);
                return null;

            default:
                throw new InvalidOperationException($"Unexpected step shape {step}.");
        }
    }
}
