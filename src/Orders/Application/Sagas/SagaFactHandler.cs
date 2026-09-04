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
        var step = SagaStepTable.For(fact.EventType);

        // Absent or Skip — no I/O. Unreachable in practice (SagaFactsConsumer
        // filters self-produced facts before any dispatch, SO2), and the
        // belt-and-braces is deliberate (design.md §5.1 step 1).
        if (step is null or SagaStep.Skip)
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

                var precondition = PreconditionOf(step);

                if (order.Status != precondition)
                {
                    // R25 — equality only, no ranges. The redelivery-safety
                    // argument (design.md §4.4) is what makes this lossless.
                    await ignoredFactRecorder.RecordAsync(
                        new SagaIgnoredFactRecord(fact.EventId, fact.EventType, order.Id.Value, fact.CorrelationId, SagaIgnoredFactMarker.PreconditionUnmet, order.Status, precondition),
                        ct).ConfigureAwait(false);
                    logger.LogInformation(
                        "Saga ignored {EventType} ({EventId}) for order {OrderId}: observed status {Observed}, expected {Expected}.",
                        fact.EventType,
                        fact.EventId,
                        order.Id,
                        order.Status,
                        precondition);
                    ignored = true;
                    return;
                }

                var owedCommand = ApplyStep(step, order, fact);

                await orders.SaveChangesAsync(ct).ConfigureAwait(false);

                if (owedCommand is { } command)
                {
                    var payloadJson = SagaCommandRequestFactory.BuildJson(command, order);
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
