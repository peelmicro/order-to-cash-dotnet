using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using OrderToCash.Cqrs;
using OrderToCash.Notifications.Application.Commands;
using OrderToCash.Notifications.Application.Ports;
using OrderToCash.Notifications.Infrastructure.Messaging;
using OrderToCash.Notifications.Infrastructure.Messaging.Consumers;
using OrderToCash.Notifications.Infrastructure.Observability;

namespace OrderToCash.Notifications.Presentation;

/// <summary>
/// The ONE Kafka <see cref="BackgroundService"/> in Notifications (CLAUDE.md:
/// "one BackgroundService per transport") — subscribes to all three fact
/// topics through <see cref="IFactStreamSubscriber"/>, parses and validates
/// the envelope (the same shape and the same seven checks Orders'
/// <c>SagaFactsConsumer</c> uses), routes the SEVEN notified facts
/// (domain-model.md §7.3) to the existing in-process dispatcher, and
/// acknowledges everything else — <c>stock.*</c>, <c>credit.*</c> and
/// <c>order.saga_failed.v1</c> — with no dispatch at all (domain-model.md
/// §7.3's own instruction: "Notifications deliberately does not notify on
/// stock.* and credit.* facts").
/// </summary>
public sealed class NotificationFactsConsumer(
    IFactStreamSubscriber subscriber,
    IServiceScopeFactory scopeFactory,
    FactRetryDispatcher factRetryDispatcher,
    ILogger<NotificationFactsConsumer> logger) : BackgroundService
{
    /// <summary>
    /// THE single source of truth for "which facts this service notifies
    /// on" (domain-model.md §7.3's "Notifications" row) — deliberately ONE
    /// table, not a filter set plus a separate routing switch. A prior
    /// revision carried both: a <c>HashSet</c> of the seven notified
    /// <c>eventType</c>s, consulted only to decide whether to dispatch, and
    /// a second, independently-written <c>switch</c> naming the same seven
    /// strings again to decide WHAT to dispatch. Nothing forced the two to
    /// agree — deleting an entry from the <c>HashSet</c> alone silently
    /// stopped that fact being notified forever, with both the unit and the
    /// integration suite fully green, because every handler-level guard
    /// constructs its handler directly and never traverses either list
    /// (feature 23 review round 1, D1). <c>Dictionary.Keys</c> IS the
    /// notified-fact set now: there is no second list to restate or drift
    /// from, and <see cref="NotificationFactsConsumerTests"/> (in
    /// <c>Notifications.UnitTests</c>) drives every one of the seven
    /// through this exact table via <see cref="ExecuteAsync"/> itself, not
    /// through a shortcut around it.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<Envelope<JsonElement>, IDispatcher, CancellationToken, Task>> _routes =
        new Dictionary<string, Func<Envelope<JsonElement>, IDispatcher, CancellationToken, Task>>(StringComparer.Ordinal)
        {
            ["order.placed.v1"] = (envelope, dispatcher, ct) =>
                dispatcher.SendAsync(new NotifyOrderPlacedCommand(ToEnvelope<OrderPlacedPayload>(envelope)), ct),
            ["order.confirmed.v1"] = (envelope, dispatcher, ct) =>
                dispatcher.SendAsync(new NotifyOrderConfirmedCommand(ToEnvelope<OrderConfirmedPayload>(envelope)), ct),
            ["order.despatched.v1"] = (envelope, dispatcher, ct) =>
                dispatcher.SendAsync(new NotifyOrderDespatchedCommand(ToEnvelope<OrderDespatchedPayload>(envelope)), ct),
            ["invoice.issued.v1"] = (envelope, dispatcher, ct) =>
                dispatcher.SendAsync(new NotifyInvoiceIssuedCommand(ToEnvelope<InvoiceIssuedPayload>(envelope)), ct),
            ["payment.received.v1"] = (envelope, dispatcher, ct) =>
                dispatcher.SendAsync(new NotifyPaymentReceivedCommand(ToEnvelope<PaymentReceivedPayload>(envelope)), ct),
            ["order.completed.v1"] = (envelope, dispatcher, ct) =>
                dispatcher.SendAsync(new NotifyOrderCompletedCommand(ToEnvelope<OrderCompletedPayload>(envelope)), ct),
            ["order.cancelled.v1"] = (envelope, dispatcher, ct) =>
                dispatcher.SendAsync(new NotifyOrderCancelledCommand(ToEnvelope<OrderCancelledPayload>(envelope)), ct),
        };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await subscriber.ConsumeAsync(NotificationFactTopics.All, HandleMessageAsync, stoppingToken).ConfigureAwait(false);
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
                logger.LogError(ex, "Notification fact consumer loop failed; re-entering after a short delay.");

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
            // correlationId. Acknowledged: a producer bug is not fixable by
            // redelivery.
            logger.LogError(
                ex,
                "Malformed fact on {Topic}[{Partition}]@{Offset}, raw value length {Length} bytes — acknowledged, not redelivered.",
                message.Topic,
                message.Partition,
                message.Offset,
                message.Value.Length);
            return;
        }

        if (!_routes.TryGetValue(envelope.EventType, out var route))
        {
            // stock.*, credit.*, order.saga_failed.v1 and any future/unknown
            // fact this service does not notify on — acknowledged, no scope,
            // no dispatch, no ledger row (domain-model.md §7.3).
            return;
        }

        // OR1 (design.md §3.1's own table): everything from here onward is
        // wrapped by the retry-then-dead-letter dispatcher — the envelope
        // guard and the not-notified-on-this-fact branch above are NOT,
        // since neither is a failure a redelivery could ever fix.
        Task Process(CancellationToken ct) => ProcessFactAsync(envelope, route, ct);

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
            ConsumerName.Notifications,
            Process,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ProcessFactAsync(
        Envelope<JsonElement> envelope,
        Func<Envelope<JsonElement>, IDispatcher, CancellationToken, Task> route,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await route(envelope, dispatcher, cancellationToken).ConfigureAwait(false);
    }

    private static Envelope<TPayload> ToEnvelope<TPayload>(Envelope<JsonElement> envelope)
    {
        var payload = JsonSerializer.Deserialize<TPayload>(envelope.Payload, JsonWire.Options)
            ?? throw new JsonException($"Fact payload for '{envelope.EventType}' deserialised to null.");

        return new Envelope<TPayload>(
            envelope.EventId,
            envelope.EventType,
            envelope.AggregateId,
            envelope.CorrelationId,
            envelope.CausationId,
            envelope.OccurredAt,
            payload);
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
