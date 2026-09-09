using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using OrderToCash.Gateway.Application.Stream;

namespace OrderToCash.Gateway.Infrastructure.Messaging;

/// <summary>
/// Subscribes to the projector's own read-model update signal
/// (<c>src/Projector/Infrastructure/Signal/NatsUpdateSignalPublisher.cs</c>
/// — <c>readmodel.order.updated.&lt;orderId&gt;</c> and
/// <c>readmodel.timeline.appended.&lt;orderId&gt;</c>, wildcarded on the
/// last token so ONE subscription per subject covers every order) and
/// feeds every decoded frame into <see cref="StreamHub"/>. Fire-and-forget,
/// subscribe-only — this service answers no fact and publishes nothing on
/// NATS itself. ONE <see cref="BackgroundService"/> for this transport
/// (CLAUDE.md: "one BackgroundService per transport") — it shares the
/// Gateway's single outbound <see cref="INatsConnection"/> with
/// <c>NatsRpcClient</c>, exactly the way <c>src/Projector</c>'s own
/// outbound connection is shared in the opposite direction (publish only,
/// never subscribe).
/// </summary>
/// <remarks>
/// Ported from #7's <c>infrastructure/messaging/nats-stream-signal.adapter.ts</c>
/// (<c>NatsStreamSignalAdapter</c>). One structural adaptation, recorded in
/// the ported-idiom ledger (<c>progress/impl_gateway_sse_push.md</c>): #7
/// decodes each frame's <c>orderId</c> out of the JSON PAYLOAD (a
/// <c>JSONCodec&lt;{ orderId: string } &amp; Record&lt;string, unknown&gt;&gt;</c>)
/// and re-serialises the decoded object back out with <c>JSON.stringify</c>
/// when it later writes the SSE frame. This subscriber instead reads
/// <c>orderId</c> off the NATS SUBJECT itself — the trailing token of
/// <c>readmodel.order.updated.&lt;orderId&gt;</c>/
/// <c>readmodel.timeline.appended.&lt;orderId&gt;</c>, which
/// <c>NatsUpdateSignalPublisher</c> constructs from
/// <c>document.OrderId.ToString("D")</c> — and forwards the payload BYTES
/// completely untouched into <see cref="StreamHub.Publish"/>. Stronger, not
/// weaker: the SSE `data:` line is then guaranteed byte-identical to what
/// the projector actually published, never a structural equivalent
/// produced by a second serialiser round trip, and orderId extraction no
/// longer depends on the payload happening to carry an `orderId` field of
/// its own. <see cref="JsonDocument.Parse(ReadOnlySpan{byte})"/> is used
/// ONLY to reject a payload that is not even syntactically valid JSON
/// before it is ever handed to an SSE client as a `data:` line — never to
/// read a value out of it.
/// </remarks>
public sealed class NatsStreamSignalSubscriber(
    INatsConnection connection,
    StreamHub hub,
    ILogger<NatsStreamSignalSubscriber> logger) : BackgroundService
{
    public const string OrderUpdatedWildcardSubject = "readmodel.order.updated.*";
    public const string TimelineAppendedWildcardSubject = "readmodel.timeline.appended.*";

    private const string OrderUpdatedEventType = "order.updated";
    private const string TimelineAppendedEventType = "timeline.appended";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = new[]
        {
            ConsumeAsync(OrderUpdatedWildcardSubject, OrderUpdatedEventType, stoppingToken),
            ConsumeAsync(TimelineAppendedWildcardSubject, TimelineAppendedEventType, stoppingToken),
        };

        await Task.WhenAll(loops).ConfigureAwait(false);
    }

    private async Task ConsumeAsync(string wildcardSubject, string eventType, CancellationToken stoppingToken)
    {
        await foreach (var message in connection.SubscribeAsync<byte[]>(wildcardSubject, cancellationToken: stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var orderId = ExtractOrderIdFromSubject(message.Subject);
                var data = message.Data ?? throw new JsonException("empty payload");

                using (var validityCheck = JsonDocument.Parse(data))
                {
                    _ = validityCheck; // syntactic-validity check only — the bytes below are forwarded untouched, see the class remarks.
                }

                hub.Publish(eventType, orderId, Encoding.UTF8.GetString(data));
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                // A malformed signal frame — log-and-continue, never bring
                // the subscription down: a bad frame is a
                // notification-channel problem, not a read-model-
                // correctness one (the read model itself is written by the
                // projector alone, unaffected by anything here). Matches
                // #7's own `nats-stream-signal.adapter.ts` "log-and-skip,
                // never break consumption of the next well-formed frame"
                // behaviour.
                logger.LogWarning(
                    ex,
                    "gateway: nats-stream-signal — failed to decode a signal frame. subject={Subject}",
                    message.Subject);
            }
        }
    }

    /// <summary>
    /// The trailing dot-separated token of <paramref name="subject"/> —
    /// <c>readmodel.order.updated.9f1e2d3c-...</c> → <c>9f1e2d3c-...</c>,
    /// the exact format <c>NatsUpdateSignalPublisher</c> constructs the
    /// subject from (<c>document.OrderId.ToString("D")</c>). <c>internal</c>
    /// (not <c>private</c>) — the same <c>InternalsVisibleTo</c> seam
    /// <c>MongoOrderReadModel</c> already establishes — so
    /// <c>NatsStreamSignalSubscriberTests</c> can drive this pure parsing
    /// step directly, without a live NATS broker.
    /// </summary>
    internal static Guid ExtractOrderIdFromSubject(string subject)
    {
        var lastDot = subject.LastIndexOf('.');
        if (lastDot < 0 || lastDot == subject.Length - 1)
        {
            throw new FormatException($"subject \"{subject}\" has no trailing orderId token.");
        }

        return Guid.Parse(subject[(lastDot + 1)..]);
    }
}
