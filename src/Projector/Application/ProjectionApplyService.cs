using Microsoft.Extensions.Logging;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Domain;

namespace OrderToCash.Projector.Application;

/// <summary>
/// <c>FactProjection.Project</c> → <see cref="IReadModelWriter"/>, with the
/// update-signal publication wired as the writer's post-apply callback.
/// <c>PR18</c>'s ordering (signal only after a genuine apply) and
/// <c>PR19</c>'s log-and-swallow live HERE and nowhere else under
/// <c>src/Projector/</c> — see <c>ProjectorConsumesOnlyTests</c>'s text-scan
/// guard.
/// </summary>
public sealed class ProjectionApplyService(
    IReadModelWriter writer,
    IUpdateSignalPublisher signalPublisher,
    ILogger<ProjectionApplyService> logger)
{
    public async Task ApplyAsync(FactEnvelope envelope, CancellationToken cancellationToken)
    {
        var delta = FactProjection.Project(envelope);

        await writer.ApplyAsync(
            delta,
            envelope.EventId,
            async (document, ct) =>
            {
                // PR18: only ever invoked by the writer when the fact was
                // genuinely applied (Processed) — a suppressed redelivery
                // (Duplicate) never reaches here, so "no signal for a
                // suppressed redelivery" is structural, not a second `if`.
                try
                {
                    await signalPublisher.PublishAsync(document, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // PR19: log and swallow, never rethrow. The projection
                    // has already applied — a rethrow would produce a
                    // redelivery that PR6's filter suppresses, so the signal
                    // could NEVER be emitted on a later attempt, and the
                    // partition would block while failing forever. The loss
                    // is bounded and already accounted for by
                    // openapi.yaml's /orders/stream (bounded buffer, client
                    // re-fetches, the read model is the source of truth).
                    logger.LogError(
                        ex,
                        "Failed to publish update signal for eventId {EventId}, orderId {OrderId} on subjects " +
                        "readmodel.order.updated.<orderId> / readmodel.timeline.appended.<orderId> — logged and " +
                        "swallowed, the fact is acknowledged, not redelivered.",
                        envelope.EventId,
                        document.OrderId);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }
}
