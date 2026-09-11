using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Wire;
using OrderToCash.Cqrs;
using OrderToCash.Projector.Application.Commands;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Domain;
using OrderToCash.Projector.Infrastructure.Messaging;
using OrderToCash.Projector.Infrastructure.Messaging.Consumers;
using OrderToCash.Projector.Infrastructure.Observability;

namespace OrderToCash.Projector.Presentation;

/// <summary>
/// The ONE Kafka <see cref="BackgroundService"/> in this service (CLAUDE.md:
/// "one BackgroundService per transport") — subscribes to all three fact
/// topics through <see cref="IFactStreamSubscriber"/> in one call, and
/// routes EVERY one of the fourteen facts through <c>FactCatalog</c>, the
/// ONE table (<c>PR36</c>): there is no second list of <c>eventType</c>
/// strings anywhere under <c>src/Projector/</c>. <c>PR3</c>/<c>PR4</c>'s
/// log-and-acknowledge branches; <c>PR4</c> is never a bare <c>return</c>
/// with no log.
/// </summary>
public sealed class ProjectorFactsConsumer(
    IFactStreamSubscriber subscriber,
    IServiceScopeFactory scopeFactory,
    FactRetryDispatcher factRetryDispatcher,
    ILogger<ProjectorFactsConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await subscriber.ConsumeAsync(ProjectorFactTopics.All, HandleMessageAsync, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — ConsumeAsync's own loop observed cancellation.
            }
            catch (Exception ex)
            {
                // A handler exception propagated out of ConsumeAsync —
                // nothing was stored, so the same offset is redelivered on
                // the next iteration. Re-entering rather than letting the
                // process crash is deliberate: a single poisonous message
                // must not crash-loop the whole service.
                logger.LogError(ex, "Projector fact consumer loop failed; re-entering after a short delay.");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Shutdown requested during the backoff delay.
                }
            }
        }
    }

    private async Task HandleMessageAsync(FactStreamMessage message, CancellationToken cancellationToken)
    {
        Envelope<JsonElement> envelope;

        try
        {
            envelope = JsonSerializer.Deserialize<Envelope<JsonElement>>(message.Value.Span, JsonWire.Options)
                ?? throw new JsonException("Envelope deserialised to null.");
            ValidateEnvelope(envelope);
        }
        catch (Exception ex)
        {
            // PR3: malformed — no trustworthy eventId (cannot dedup) and no
            // correlationId. Acknowledged: a producer bug is not fixable by
            // redelivery. Nothing is written, no signal is published.
            logger.LogError(
                ex,
                "Malformed fact on {Topic}[{Partition}]@{Offset}, raw value length {Length} bytes — acknowledged, not redelivered.",
                message.Topic,
                message.Partition,
                message.Offset,
                message.Value.Length);
            return;
        }

        if (!FactCatalog.PayloadTypesByEventType.TryGetValue(envelope.EventType, out var payloadType))
        {
            // PR4: an unknown eventType is LOGGED, never a bare `return` —
            // PR2's completeness test makes this branch unreachable for any
            // of the fourteen declared facts, so reaching it here means a
            // future fact type shipped on the wire before this service
            // learned about it.
            logger.LogError(
                "Unknown eventType '{EventType}' on {Topic}[{Partition}]@{Offset} — acknowledged, not projected.",
                envelope.EventType,
                message.Topic,
                message.Partition,
                message.Offset);
            return;
        }

        // OR1 (design.md §3.1's own table): everything from the payload
        // deserialisation onward is wrapped by the retry-then-dead-letter
        // dispatcher — the envelope guard and the unrouted-eventType branch
        // above are NOT, since neither is a failure a redelivery could ever
        // fix.
        Task Process(CancellationToken ct) => ProcessFactAsync(envelope, payloadType, ct);

        // OR4/design.md §5.3, ledger L22/L24 — extracted ONCE, wrapping the
        // WHOLE DispatchAsync call (every retry attempt AND the eventual
        // DLQ publish), not each individual attempt.
        var context = TraceContext.ExtractKafka(message.HeaderMap);
        using var activity = context is { } parent
            ? OtcActivity.Source.StartActivity($"consume {envelope.EventType}", ActivityKind.Consumer, parentContext: parent)
            : OtcActivity.Source.StartActivity($"consume {envelope.EventType}", ActivityKind.Consumer);

        // design.md §6's scope-push table, fact consumer row —
        // envelope.correlationId.
        using var correlationScope = logger.BeginScope(new Dictionary<string, object> { ["correlationId"] = envelope.CorrelationId });

        await factRetryDispatcher.DispatchAsync(
            message.Topic,
            message,
            envelope.EventId,
            envelope.EventType,
            envelope.CorrelationId,
            ConsumerName.Projector,
            Process,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessFactAsync(Envelope<JsonElement> envelope, Type payloadType, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize(envelope.Payload, payloadType, JsonWire.Options)
            ?? throw new JsonException($"Fact payload for '{envelope.EventType}' deserialised to null.");

        var factEnvelope = new FactEnvelope(
            envelope.EventId,
            envelope.EventType,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.OccurredAt,
            payload);

        using var scope = scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        try
        {
            await dispatcher.SendAsync(new ProjectFactCommand(factEnvelope), cancellationToken).ConfigureAwait(false);
        }
        catch (UnknownFactTypeError ex)
        {
            // PR4 (ledger L11, design.md §3.1's table): a deliberate
            // acknowledge, not a failure — should be unreachable given PR2's
            // completeness guard, but swallowed HERE, INSIDE the wrapped
            // delegate, so it can never reach the retry dispatcher's error
            // path and dead-letter a fact this service is specified to
            // ignore.
            logger.LogWarning(
                ex,
                "UnknownFactTypeError for eventType '{EventType}' swallowed inside process — should be unreachable given PR2's completeness guard.",
                envelope.EventType);
        }
    }

    /// <summary>The seven <c>R11</c> checks — copied verbatim from Notifications' own <c>ValidateEnvelope</c> (<c>PR3</c>).</summary>
    private static void ValidateEnvelope(Envelope<JsonElement> envelope)
    {
        if (envelope.EventId == Guid.Empty)
        {
            throw new JsonException("envelope.eventId is empty.");
        }

        if (string.IsNullOrEmpty(envelope.EventType))
        {
            throw new JsonException("envelope.eventType is empty.");
        }

        if (envelope.AggregateId == Guid.Empty)
        {
            throw new JsonException("envelope.aggregateId is empty.");
        }

        if (envelope.CorrelationId == Guid.Empty)
        {
            throw new JsonException("envelope.correlationId is empty.");
        }

        if (envelope.CausationId == Guid.Empty)
        {
            throw new JsonException("envelope.causationId is empty.");
        }

        if (envelope.OccurredAt == default)
        {
            throw new JsonException("envelope.occurredAt is default.");
        }
    }
}
