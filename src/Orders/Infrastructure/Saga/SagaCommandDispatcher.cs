using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Infrastructure.Saga;

/// <summary>
/// The one member <see cref="SagaCommandDispatchWorker"/> and
/// <see cref="SagaCommandSweeper"/> depend on — resolved from DI rather than
/// the concrete <see cref="SagaCommandDispatcher"/> class, so
/// <c>SagaCommandDispatchWorkerTests</c> (SO10) can substitute a
/// controllable fake and prove the worker's OWN hand-off behaviour with no
/// database and no real NATS/Kafka — <c>IOutboxRelay</c>'s own precedent.
/// </summary>
public interface ISagaCommandDispatcher
{
    /// <summary>The fast path (<see cref="SagaCommandDispatchWorker"/>): claims by <c>(orderId, command)</c> — the only identity a channel signal carries — then issues.</summary>
    Task DispatchAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken);

    /// <summary>The sweeper's path: the row is ALREADY claimed (<see cref="ISagaCommandStore.ClaimDueAsync"/> claimed it under its own lease), so this issues directly with no second claim — claiming twice would make the sweeper's own claim invisible to itself.</summary>
    Task DispatchClaimedAsync(SagaCommandRecord claimed, CancellationToken cancellationToken);
}

/// <summary>
/// Claim (SO11, §6.3) ⇒ issue with SO4's in-line retry policy ⇒ mark
/// <c>sent</c> on ANY resolved reply including a business rejection (SO6) ⇒
/// park on exhaustion (SO5). A TERMINAL business rejection
/// (<see cref="SagaCommandBusinessRejectionError"/>, feature 42) short-circuits
/// straight to <see cref="ISagaCommandStore.RejectAsync"/> on its FIRST
/// occurrence, skipping the remaining attempts/backoff and
/// <see cref="ISagaCommandStore.ParkAsync"/>'s retry-eligible path entirely.
/// The order status is never touched here — that is the fact handler's job
/// alone (R29).
/// </summary>
/// <remarks>
/// Invoked from exactly two places — <see cref="SagaCommandDispatchWorker"/>
/// (the fast path, off the consume loop, SO10) and
/// <see cref="SagaCommandSweeper"/> (the guarantee) — never through
/// <see cref="Ports.ISagaCommandSignal"/> or the in-process dispatcher
/// itself.
///
/// <b>The §3.2 budget derivation, restated here because this is where it is
/// actually spent.</b> Worst case: <c>MaxAttempts × TimeoutMs +
/// Σ(BackoffMs × 2^n for n in 0..MaxAttempts-2)</c> = 3 × 5 000 + 500 + 1 000
/// = 16 500 ms. This runs on the dispatch worker or the sweeper, never the
/// Kafka consume loop (SO10, design.md §5.5) — so it is bounded by nothing on
/// the consume path, and by <c>max.poll.interval.ms</c> (300 000 ms) only in
/// the degenerate case where SO10's decoupling is later removed. Any future
/// re-tuning of these numbers should re-check against that constraint rather
/// than against a number nobody can source (design.md §3.2, §6.2).
/// </remarks>
public sealed class SagaCommandDispatcher(
    ISagaCommandStore store,
    ISagaCommands sagaCommands,
    ISagaRetryDelay delay,
    IOptions<OrdersSagaOptions> options,
    ILogger<SagaCommandDispatcher> logger) : ISagaCommandDispatcher
{
    public async Task DispatchAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken)
    {
        var claimed = await store.TryClaimAsync(orderId, command, cancellationToken).ConfigureAwait(false);

        if (claimed is null)
        {
            // A stale signal, a row already sent, or one currently held by a
            // concurrent claimant/the sweeper — a SILENT no-op (design.md §6.2).
            return;
        }

        await DispatchClaimedAsync(claimed, cancellationToken).ConfigureAwait(false);
    }

    public async Task DispatchClaimedAsync(SagaCommandRecord claimed, CancellationToken cancellationToken)
    {
        var policy = options.Value.Command;
        Exception? lastFailure = null;

        // FS2: the order id and the row id, stable across every attempt of
        // this cycle AND across every sweeper re-issue of this same row —
        // built once per DispatchClaimedAsync call, not once per attempt,
        // because it is the SAME value on every attempt by construction
        // (both fields come from `claimed`, never from the attempt loop).
        var meta = new SagaCommandMeta(UniqueId.From(claimed.OrderId), UniqueId.From(claimed.Id));

        for (var attempt = 1; attempt <= policy.MaxAttempts; attempt++)
        {
            try
            {
                await InvokeAsync(claimed.Command, claimed.Payload, meta, cancellationToken).ConfigureAwait(false);

                // A reply was delivered — including outcome: rejected (SO6):
                // the responder has emitted (or will emit) the rejection
                // FACT, and only that fact moves the saga. `sent` means "a
                // reply was delivered", never "the saga advanced".
                await store.MarkSentAsync(claimed.Id, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (SagaCommandBusinessRejectionError ex)
            {
                // Feature 42: a TERMINAL business rejection short-circuits
                // the retry loop immediately — no further in-line attempts,
                // no backoff delay, and NOT ParkAsync's retry-eligible path.
                // The responder has already given a definitive "no" from its
                // own domain (e.g. PRECONDITION_FAILED); a second/third
                // attempt at the same subject can only ever reproduce the
                // identical rejection, so retrying it is pure waste — and,
                // before this fix, an unresolvable infinite retry.
                await store.RejectAsync(claimed.Id, attempt, ex.Message, cancellationToken).ConfigureAwait(false);

                logger.LogError(
                    ex,
                    "Saga command {Command} for order {OrderId} rejected (terminal business rejection {RpcErrorCode}) on attempt {Attempt}: {Message}",
                    claimed.Command,
                    claimed.OrderId,
                    ex.RpcErrorCode,
                    attempt,
                    ex.Message);

                return;
            }
            catch (Exception ex) when (ex is SagaCommandTimeoutError or SagaCommandTransportError)
            {
                lastFailure = ex;

                logger.LogWarning(
                    ex,
                    "Saga command {Command} for order {OrderId} failed on attempt {Attempt}/{MaxAttempts}: {Message}",
                    claimed.Command,
                    claimed.OrderId,
                    attempt,
                    policy.MaxAttempts,
                    ex.Message);

                if (attempt < policy.MaxAttempts)
                {
                    var backoffMs = policy.BackoffMs * (1 << (attempt - 1));
                    await delay.DelayAsync(backoffMs, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        // SO5 — exhausted: park durably with the accumulated attempts and
        // the last error, and log a structured saga-failure entry.
        var errorMessage = lastFailure?.Message ?? "unknown error";
        await store.ParkAsync(claimed.Id, policy.MaxAttempts, errorMessage, cancellationToken).ConfigureAwait(false);

        logger.LogError(
            lastFailure,
            "Saga command {Command} for order {OrderId} parked after {MaxAttempts} attempts: {Message}",
            claimed.Command,
            claimed.OrderId,
            policy.MaxAttempts,
            errorMessage);
    }

    private Task InvokeAsync(SagaCommandKind command, string payloadJson, SagaCommandMeta meta, CancellationToken cancellationToken) => command switch
    {
        SagaCommandKind.StockReserve => sagaCommands.ReserveStockAsync(RpcJson.Deserialize<StockReserveRequestPayload>(System.Text.Encoding.UTF8.GetBytes(payloadJson)), meta, cancellationToken),
        SagaCommandKind.StockRelease => sagaCommands.ReleaseStockAsync(RpcJson.Deserialize<StockReleaseRequestPayload>(System.Text.Encoding.UTF8.GetBytes(payloadJson)), meta, cancellationToken),
        SagaCommandKind.DespatchCreate => sagaCommands.CreateDespatchAsync(RpcJson.Deserialize<DespatchCreateRequestPayload>(System.Text.Encoding.UTF8.GetBytes(payloadJson)), meta, cancellationToken),
        SagaCommandKind.CreditHold => sagaCommands.HoldCreditAsync(RpcJson.Deserialize<CreditHoldRequestPayload>(System.Text.Encoding.UTF8.GetBytes(payloadJson)), meta, cancellationToken),
        SagaCommandKind.InvoiceIssue => sagaCommands.IssueInvoiceAsync(RpcJson.Deserialize<InvoiceIssueRequestPayload>(System.Text.Encoding.UTF8.GetBytes(payloadJson)), meta, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unrecognised SagaCommandKind member."),
    };
}
