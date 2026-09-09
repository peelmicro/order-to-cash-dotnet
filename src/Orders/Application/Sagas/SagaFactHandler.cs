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
    ILogger<SagaFactHandler> logger)
{
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

                // R25, generalised: for a single-variant eventType this is
                // exactly the original equality check. For credit.released.v1
                // and stock.released.v1 (feature orders_cancel_responder,
                // more than one legal precondition each) this picks the ONE
                // variant whose precondition matches the order's CURRENT
                // status — null means none of the catalogued preconditions
                // are met, the same "ignored" outcome, generalised rather
                // than restricted to a single expected value.
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

                var owedCommand = ApplyStep(matchedStep, order, fact);

                await orders.SaveChangesAsync(ct).ConfigureAwait(false);

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
                        ? SagaCommandRequestFactory.BuildStockReleaseJson(order, fact.EventType)
                        : SagaCommandRequestFactory.BuildJson(command, order);
                    var enqueueOutcome = await commandStore.EnqueueAsync(
                        order.Id.Value,
                        order.OrderReference.Value,
                        command,
                        payloadJson,
                        fact.EventId,
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

    /// <summary>Applies the step's aggregate call(s) — already known to be legal, since the precondition was checked immediately above — and returns the command it owes, if any.</summary>
    private static SagaCommandKind? ApplyStep(SagaStep step, Domain.Order order, SagaFact fact)
    {
        switch (step)
        {
            case SagaStep.Advance advance:
                advance.Apply?.Invoke(order, fact);
                return advance.CommandAfter;

            case SagaStep.Cancel cancel:
                order.Cancel(cancel.Reason(fact), cancel.CompensationSteps(fact), fact.OccurredAt, UniqueId.From(fact.EventId));
                return null;

            default:
                throw new InvalidOperationException($"Unexpected step shape {step}.");
        }
    }
}
