using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Wire;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;

namespace OrderToCash.Orders.Presentation;

/// <summary>
/// The ONE Kafka <see cref="BackgroundService"/> in Orders (CLAUDE.md: "one
/// BackgroundService per transport") — subscribes to all three fact topics
/// through <see cref="IFactStreamSubscriber"/>, parses and validates the
/// envelope, routes on <c>eventType</c> (design.md §3.5), and dispatches
/// through the existing in-process dispatcher. One <see cref="IServiceScope"/>
/// per message, the shape <c>OrdersCreateResponder</c> already established.
/// </summary>
public sealed class SagaFactsConsumer(
    IFactStreamSubscriber subscriber,
    IServiceScopeFactory scopeFactory,
    ILogger<SagaFactsConsumer> logger) : BackgroundService
{
    /// <summary>The four facts the orchestrator produces itself (SO2) — consuming them would be a loop (saga.md §5).</summary>
    private static readonly HashSet<string> _selfProducedFacts = new(StringComparer.Ordinal)
    {
        "order.confirmed.v1", "order.completed.v1", "order.cancelled.v1", "order.saga_failed.v1",
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await subscriber.ConsumeAsync(SagaFactTopics.All, HandleMessageAsync, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — ConsumeAsync's own loop observed cancellation.
            }
            catch (Exception ex)
            {
                // A handler exception propagated out of ConsumeAsync — design.md
                // §3.3: nothing was stored, so the same offset is redelivered
                // on the next iteration. Re-entering rather than letting the
                // process crash is deliberate: a single poisonous message
                // must not crash-loop the whole service (design.md §3.3).
                logger.LogError(ex, "Saga fact consumer loop failed; re-entering after a short delay.");

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
            // Malformed — no trustworthy eventId (cannot dedup) and no
            // correlationId (cannot park). Acknowledged: a producer bug is
            // not fixable by redelivery (design.md §3.5).
            logger.LogError(
                ex,
                "Malformed fact on {Topic}[{Partition}]@{Offset}, raw value length {Length} bytes — acknowledged, not redelivered.",
                message.Topic,
                message.Partition,
                message.Offset,
                message.Value.Length);
            return;
        }

        if (_selfProducedFacts.Contains(envelope.EventType))
        {
            // SO2 — acknowledged with no dispatch, no scope, no store touch.
            return;
        }

        if (!FactCatalog.PayloadTypesByEventType.TryGetValue(envelope.EventType, out var payloadType))
        {
            // A well-formed envelope whose eventType is neither in the step
            // table nor FactCatalog — a future fact this consumer does not
            // yet know. Distinct from malformed: acknowledged at WARNING.
            logger.LogWarning(
                "Unrouted fact eventType '{EventType}' on {Topic}[{Partition}]@{Offset} — acknowledged.",
                envelope.EventType,
                message.Topic,
                message.Partition,
                message.Offset);
            return;
        }

        var factCommand = SagaFactCommands.FactCommandFor(envelope.EventType);

        if (factCommand is null)
        {
            // Belt-and-braces second layer for SO2 — unreachable given the
            // filter above, since FactCommandFor and the self-produced set
            // agree on the same four facts.
            return;
        }

        var payload = JsonSerializer.Deserialize(envelope.Payload, payloadType, JsonWire.Options)
            ?? throw new JsonException($"Fact payload for '{envelope.EventType}' deserialised to null.");

        var fact = new SagaFact(
            envelope.EventId,
            envelope.EventType,
            envelope.AggregateId,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.OccurredAt,
            payload);

        // ONE scope per message (design.md §5.1's "Scope discipline") — the
        // shape OrdersCreateResponder already established, and the reason
        // Dispatcher is registered scoped rather than singleton.
        using var scope = scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await factCommand(dispatcher, fact, cancellationToken).ConfigureAwait(false);
    }

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
