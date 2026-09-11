using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using OrderToCash.Cqrs;
using OrderToCash.Notifications.Application.Commands;
using OrderToCash.Notifications.Application.Ports;
using OrderToCash.Notifications.Infrastructure.Messaging;
using OrderToCash.Notifications.Presentation;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>
/// D1 (feature 23 review round 1) — guards the notified-fact ROUTING, not
/// just the seven handlers in isolation. Drives the real
/// <see cref="NotificationFactsConsumer"/> end to end through
/// <see cref="NotificationFactsConsumer.ExecuteAsync"/> itself (a fake
/// <see cref="IFactStreamSubscriber"/> in, a recording <see cref="IDispatcher"/>
/// out) so the filter/routing table is genuinely traversed — the exact path
/// <c>NotifyFactCommandHandlersTests</c> never reaches, since it constructs
/// each handler directly. Before this file existed, deleting five of the
/// seven entries from the consumer's notified-fact table left both the unit
/// and the (real Kafka + real MS-SQL) integration suite fully green.
/// </summary>
public sealed class NotificationFactsConsumerTests
{
    [Theory]
    [InlineData("order.placed.v1", typeof(NotifyOrderPlacedCommand))]
    [InlineData("order.confirmed.v1", typeof(NotifyOrderConfirmedCommand))]
    [InlineData("order.despatched.v1", typeof(NotifyOrderDespatchedCommand))]
    [InlineData("invoice.issued.v1", typeof(NotifyInvoiceIssuedCommand))]
    [InlineData("payment.received.v1", typeof(NotifyPaymentReceivedCommand))]
    [InlineData("order.completed.v1", typeof(NotifyOrderCompletedCommand))]
    [InlineData("order.cancelled.v1", typeof(NotifyOrderCancelledCommand))]
    public async Task EachOfTheSevenNotifiedFacts_ReachesItsOwnCommand(string eventType, Type expectedCommandType)
    {
        var message = BuildMessage(eventType, BuildNotifiedPayload(eventType));
        var dispatcher = new RecordingDispatcher();

        await RunOneMessageAsync(message, dispatcher);

        var sent = Assert.Single(dispatcher.SentCommands);
        Assert.Equal(expectedCommandType, sent.GetType());
    }

    /// <summary>
    /// R2-D5 (feature 23 review round 2) / backlog id 58 — <c>ToEnvelope</c>
    /// copies the six envelope metadata fields (everything but
    /// <c>Payload</c>) BY HAND into the typed envelope every template reads.
    /// The source envelope here gives all SIX their own distinct, known
    /// value (<see cref="BuildMessage"/>'s previous default made
    /// <c>AggregateId</c> and <c>CorrelationId</c> the SAME guid, which
    /// would have let those two fields be silently transposed and still
    /// pass) — so this asserts PROVENANCE, not mere non-collision
    /// (CLAUDE.md's "Assert.NotEqual can never prove provenance" rule):
    /// each field is checked against the specific source value it was
    /// supposed to carry, and a transposition of any two of the four Guid
    /// fields, or a hand-written substitution of any one, fails on the
    /// wrong value rather than passing by accident.
    /// </summary>
    [Theory]
    [InlineData("order.placed.v1", typeof(NotifyOrderPlacedCommand))]
    [InlineData("order.confirmed.v1", typeof(NotifyOrderConfirmedCommand))]
    [InlineData("order.despatched.v1", typeof(NotifyOrderDespatchedCommand))]
    [InlineData("invoice.issued.v1", typeof(NotifyInvoiceIssuedCommand))]
    [InlineData("payment.received.v1", typeof(NotifyPaymentReceivedCommand))]
    [InlineData("order.completed.v1", typeof(NotifyOrderCompletedCommand))]
    [InlineData("order.cancelled.v1", typeof(NotifyOrderCancelledCommand))]
    public async Task EachOfTheSevenNotifiedFacts_DispatchedEnvelopeCopiesEveryFieldFromTheSource(string eventType, Type expectedCommandType)
    {
        var eventId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero);

        var message = BuildMessage(eventType, BuildNotifiedPayload(eventType), eventId, aggregateId, correlationId, causationId, occurredAt);
        var dispatcher = new RecordingDispatcher();

        await RunOneMessageAsync(message, dispatcher);

        var sent = Assert.Single(dispatcher.SentCommands);
        Assert.Equal(expectedCommandType, sent.GetType());

        var envelope = sent.GetType().GetProperty("Envelope")!.GetValue(sent)!;
        var envelopeType = envelope.GetType();

        Assert.Equal(eventId, envelopeType.GetProperty("EventId")!.GetValue(envelope));
        Assert.Equal(eventType, envelopeType.GetProperty("EventType")!.GetValue(envelope));
        Assert.Equal(aggregateId, envelopeType.GetProperty("AggregateId")!.GetValue(envelope));
        Assert.Equal(correlationId, envelopeType.GetProperty("CorrelationId")!.GetValue(envelope));
        Assert.Equal(causationId, envelopeType.GetProperty("CausationId")!.GetValue(envelope));
        Assert.Equal(occurredAt, envelopeType.GetProperty("OccurredAt")!.GetValue(envelope));
    }

    /// <summary>domain-model.md §7.3 — every OTHER fact on the three topics is acknowledged with no dispatch and no scope opened.</summary>
    [Theory]
    [InlineData("stock.reserved.v1")]
    [InlineData("credit.approved.v1")]
    [InlineData("order.saga_failed.v1")]
    public async Task AnExcludedFact_ReachesNoDispatchAndOpensNoScope(string eventType)
    {
        var message = BuildMessage(eventType, BuildExcludedPayload(eventType));
        var dispatcher = new RecordingDispatcher();
        var scopeFactory = new CountingScopeFactory(dispatcher);

        await RunOneMessageAsync(message, dispatcher, scopeFactory);

        Assert.Empty(dispatcher.SentCommands);
        Assert.Equal(0, scopeFactory.ScopesCreated);
    }

    [Fact]
    public async Task AMalformedValue_IsAcknowledgedAndDispatchesNothing()
    {
        var message = new FactStreamMessage("otc.orders.facts.v1", 0, 0, "{ not valid json"u8.ToArray());
        var dispatcher = new RecordingDispatcher();

        await RunOneMessageAsync(message, dispatcher);

        Assert.Empty(dispatcher.SentCommands);
    }

    [Fact]
    public async Task AnUnknownEventType_IsAcknowledgedAndDispatchesNothing()
    {
        var message = BuildMessage("future.fact.v1", new { });
        var dispatcher = new RecordingDispatcher();

        await RunOneMessageAsync(message, dispatcher);

        Assert.Empty(dispatcher.SentCommands);
    }

    private static async Task RunOneMessageAsync(FactStreamMessage message, RecordingDispatcher dispatcher, CountingScopeFactory? scopeFactory = null)
    {
        var subscriber = new FakeFactStreamSubscriber([message]);
        var services = new ServiceCollection();
        services.AddSingleton<IDispatcher>(dispatcher);
        var provider = services.BuildServiceProvider();
        scopeFactory ??= new CountingScopeFactory(dispatcher, provider);

        var consumer = new NotificationFactsConsumer(subscriber, scopeFactory, BuildRealFactRetryDispatcher(), NullLogger<NotificationFactsConsumer>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        await subscriber.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Give the handler a moment to complete after delivery.
        await Task.Delay(50);
        await consumer.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// A REAL <see cref="FactRetryDispatcher"/> over instant fakes — every
    /// case in this file expects the wrapped process delegate to succeed on
    /// its first attempt, so this dispatcher's own retry/backoff/DLQ
    /// machinery is never exercised here (OR1's own behaviour lives in
    /// <c>FactRetryDispatcherTests</c>).
    /// </summary>
    internal static FactRetryDispatcher BuildRealFactRetryDispatcher(
        IFactRetryDelay? delay = null,
        IDeadLetterPublisher? deadLetters = null,
        FactRetryOptions? options = null) =>
        new(
            new FakeClock(),
            delay ?? new InstantFactRetryDelay(),
            deadLetters ?? new RecordingDeadLetterPublisher(),
            Options.Create(options ?? new FactRetryOptions()),
            NullLogger<FactRetryDispatcher>.Instance);

    /// <summary>A no-wait <see cref="IFactRetryDelay"/> — unit tests run instantly, and every call is recorded so OR1's exact-backoff-sequence assertions can inspect it.</summary>
    internal sealed class InstantFactRetryDelay : IFactRetryDelay
    {
        public List<int> Delays { get; } = [];

        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            Delays.Add(milliseconds);
            return Task.CompletedTask;
        }
    }

    /// <summary>Records every dead letter it was asked to publish — never touches a real broker.</summary>
    internal sealed class RecordingDeadLetterPublisher : IDeadLetterPublisher
    {
        public List<DeadLetterPublication> Published { get; } = [];

        public Task PublishAsync(DeadLetterPublication publication, CancellationToken cancellationToken)
        {
            Published.Add(publication);
            return Task.CompletedTask;
        }
    }

    /// <summary>A settable fake — matching this service's own <c>IClock</c> port.</summary>
    internal sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    private static FactStreamMessage BuildMessage(
        string eventType,
        object payload,
        Guid? eventId = null,
        Guid? aggregateId = null,
        Guid? correlationId = null,
        Guid? causationId = null,
        DateTimeOffset? occurredAt = null)
    {
        // Each field defaults to its OWN distinct Guid.NewGuid()/UtcNow — never
        // shared across two positional slots — so a test that asserts one
        // field's value cannot pass merely because two source fields happened
        // to collide (see EachOfTheSevenNotifiedFacts_DispatchedEnvelopeCopiesEveryFieldFromTheSource).
        var envelope = new Envelope<object>(
            eventId ?? Guid.NewGuid(),
            eventType,
            aggregateId ?? Guid.NewGuid(),
            correlationId ?? Guid.NewGuid(),
            causationId ?? Guid.NewGuid(),
            occurredAt ?? DateTimeOffset.UtcNow,
            payload);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWire.Options);
        return new FactStreamMessage("otc.orders.facts.v1", 0, 0, bytes);
    }

    private static object BuildNotifiedPayload(string eventType) => eventType switch
    {
        "order.placed.v1" => new OrderPlacedPayload("ORD-000001", "RETAILER1", "COMPANY1", "4006381333931", "5001234567890", "EUR", DateTimeOffset.UtcNow, [], 0, 0, 0),
        "order.confirmed.v1" => new OrderConfirmedPayload("ORD-000001", "RETAILER1", "COMPANY1", "EUR", 1000, DateTimeOffset.UtcNow),
        "order.despatched.v1" => new OrderDespatchedPayload("ORD-000001", "DES-000001", DateTimeOffset.UtcNow, "COMPANY1", "RETAILER1", []),
        "invoice.issued.v1" => new InvoiceIssuedPayload("ORD-000001", "INV-000001", DateTimeOffset.UtcNow, "RETAILER1", "COMPANY1", "EUR", [], 1000, 0, 1000),
        "payment.received.v1" => new PaymentReceivedPayload("ORD-000001", "INV-000001", "PAY-000001", "EUR", 1000, DateTimeOffset.UtcNow, "gateway"),
        "order.completed.v1" => new OrderCompletedPayload("ORD-000001", "RETAILER1", "COMPANY1", "EUR", 1000, DateTimeOffset.UtcNow),
        "order.cancelled.v1" => new OrderCancelledPayload("ORD-000001", "RETAILER1", "COMPANY1", "stock_rejected", DateTimeOffset.UtcNow, []),
        _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
    };

    private static object BuildExcludedPayload(string eventType) => eventType switch
    {
        "stock.reserved.v1" => new StockReservedPayload("ORD-000001", "COMPANY1", [new ReservationRef(Guid.NewGuid(), "SKU-1", 1)]),
        "credit.approved.v1" => new CreditApprovedPayload("ORD-000001", "RETAILER1", "COMPANY1", "CR-000001", "EUR", 1000, 4000),
        "order.saga_failed.v1" => new OrderSagaFailedPayload("ORD-000001", "stock.reserve", 3, "timeout", DateTimeOffset.UtcNow),
        _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
    };

    private sealed class FakeFactStreamSubscriber(IReadOnlyList<FactStreamMessage> messages) : IFactStreamSubscriber
    {
        public TaskCompletionSource Delivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ConsumeAsync(IReadOnlyList<string> topics, Func<FactStreamMessage, CancellationToken, Task> handler, CancellationToken cancellationToken)
        {
            foreach (var message in messages)
            {
                await handler(message, cancellationToken).ConfigureAwait(false);
            }

            Delivered.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private sealed class RecordingDispatcher : IDispatcher
    {
        public List<object> SentCommands { get; } = [];

        public Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken) where TCommand : ICommand
        {
            SentCommands.Add(command!);
            return Task.CompletedTask;
        }

        public Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken) where TCommand : ICommand<TResult> => throw new NotSupportedException();

        public Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken) where TQuery : IQuery<TResult> => throw new NotSupportedException();

        public Task PublishAsync(object @event, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>Counts how many scopes were opened — the exclusion assertion's "opens no scope" half.</summary>
    private sealed class CountingScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _provider;

        public CountingScopeFactory(RecordingDispatcher dispatcher, IServiceProvider? provider = null)
        {
            if (provider is not null)
            {
                _provider = provider;
                return;
            }

            var services = new ServiceCollection();
            services.AddSingleton<IDispatcher>(dispatcher);
            _provider = services.BuildServiceProvider();
        }

        public int ScopesCreated { get; private set; }

        public IServiceScope CreateScope()
        {
            ScopesCreated++;
            return _provider.CreateScope();
        }
    }
}
