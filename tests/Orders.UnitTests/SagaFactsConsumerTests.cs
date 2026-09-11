using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Infrastructure.Messaging;
using OrderToCash.Orders.Presentation;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §3.5 — routing only, against a fake <see cref="IFactStreamSubscriber"/>
/// and a recording <see cref="IDispatcher"/>: each of the ten consumed facts
/// reaches its own fact command; each of the four self-produced facts
/// reaches NO dispatch, opens no scope and touches no store (SO2); a
/// malformed value is acknowledged and logged; an unknown <c>eventType</c> is
/// acknowledged and logged distinctly from malformed.
/// </summary>
public sealed class SagaFactsConsumerTests
{
    [Theory]
    [InlineData("order.placed.v1", typeof(HandleOrderPlacedFactCommand))]
    [InlineData("stock.reserved.v1", typeof(HandleStockReservedFactCommand))]
    [InlineData("stock.rejected.v1", typeof(HandleStockRejectedFactCommand))]
    [InlineData("credit.approved.v1", typeof(HandleCreditApprovedFactCommand))]
    [InlineData("credit.rejected.v1", typeof(HandleCreditRejectedFactCommand))]
    [InlineData("stock.released.v1", typeof(HandleStockReleasedFactCommand))]
    [InlineData("order.despatched.v1", typeof(HandleOrderDespatchedFactCommand))]
    [InlineData("invoice.issued.v1", typeof(HandleInvoiceIssuedFactCommand))]
    [InlineData("payment.received.v1", typeof(HandlePaymentReceivedFactCommand))]
    [InlineData("credit.released.v1", typeof(HandleCreditReleasedFactCommand))]
    public async Task EachConsumedFact_ReachesItsOwnFactCommand(string eventType, Type expectedCommandType)
    {
        var message = BuildMessage(eventType, BuildPayload(eventType));
        var dispatcher = new RecordingDispatcher();

        await RunOneMessageAsync(message, dispatcher);

        var sent = Assert.Single(dispatcher.SentCommands);
        Assert.Equal(expectedCommandType, sent.GetType());
    }

    /// <summary>
    /// Backlog id 60 — <c>BuildMessage</c>'s previous default gave
    /// <c>AggregateId</c> and <c>CorrelationId</c> the SAME guid (both set
    /// from one shared <c>correlationId</c> local), which would have let
    /// <see cref="SagaFactsConsumer"/>'s hand-written <c>SagaFact</c>
    /// construction transpose those two positions and still pass every test
    /// in this file — nothing here asserted <c>SagaFact</c>'s field values
    /// at all. This asserts PROVENANCE, not mere non-collision (CLAUDE.md's
    /// "Assert.NotEqual can never prove provenance" rule): each of the five
    /// non-payload fields is checked against the specific source value it
    /// was supposed to carry, using four independently-generated Guids, so a
    /// transposition of any two of them fails on the wrong value rather than
    /// passing by accident.
    /// </summary>
    [Theory]
    [InlineData("order.placed.v1", typeof(HandleOrderPlacedFactCommand))]
    [InlineData("stock.reserved.v1", typeof(HandleStockReservedFactCommand))]
    [InlineData("stock.rejected.v1", typeof(HandleStockRejectedFactCommand))]
    [InlineData("credit.approved.v1", typeof(HandleCreditApprovedFactCommand))]
    [InlineData("credit.rejected.v1", typeof(HandleCreditRejectedFactCommand))]
    [InlineData("stock.released.v1", typeof(HandleStockReleasedFactCommand))]
    [InlineData("order.despatched.v1", typeof(HandleOrderDespatchedFactCommand))]
    [InlineData("invoice.issued.v1", typeof(HandleInvoiceIssuedFactCommand))]
    [InlineData("payment.received.v1", typeof(HandlePaymentReceivedFactCommand))]
    [InlineData("credit.released.v1", typeof(HandleCreditReleasedFactCommand))]
    public async Task EachConsumedFact_DispatchedFactCopiesEveryEnvelopeFieldFromTheSource(string eventType, Type expectedCommandType)
    {
        var eventId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero);

        var message = BuildMessage(eventType, BuildPayload(eventType), eventId, aggregateId, correlationId, causationId, occurredAt);
        var dispatcher = new RecordingDispatcher();

        await RunOneMessageAsync(message, dispatcher);

        var sent = Assert.Single(dispatcher.SentCommands);
        Assert.Equal(expectedCommandType, sent.GetType());

        var fact = (SagaFact)sent.GetType().GetProperty("Fact")!.GetValue(sent)!;
        Assert.Equal(eventId, fact.EventId);
        Assert.Equal(eventType, fact.EventType);
        Assert.Equal(aggregateId, fact.AggregateId);
        Assert.Equal(correlationId, fact.CorrelationId);
        Assert.Equal(causationId, fact.CausationId);
        Assert.Equal(occurredAt, fact.OccurredAt);

        // observability_reliability, OR3/R29's dead-letter clause (design.md
        // §4.2, ledger L15) — the RAW message bytes, byte-for-byte, never a
        // re-serialised Envelope<T> (which would reorder keys and drop
        // unknown fields), and the source topic they were consumed from.
        Assert.Equal(message.Value.ToArray(), fact.TriggeringEventEnvelope);
        Assert.Equal(message.Topic, fact.TriggeringEventTopic);
    }

    /// <summary>
    /// <c>observability_reliability</c>, design.md §4.2 (ledger L15) — the
    /// SAME proof as <c>EachConsumedFact_DispatchedFactCopiesEveryEnvelopeFieldFromTheSource</c>,
    /// but against bytes whose key ORDER a re-serialisation through
    /// <c>Envelope&lt;JsonElement&gt;</c> could never reproduce (the
    /// declared field order is <c>eventId, eventType, aggregateId,
    /// correlationId, causationId, occurredAt, payload</c>; this message's
    /// bytes are hand-written in the reverse order). A re-serialising
    /// implementation still parses and dispatches correctly — every OTHER
    /// assertion in this file would stay green — so only a genuine
    /// byte-for-byte comparison against these exact bytes can catch it.
    /// </summary>
    [Fact]
    public async Task OR3_TheTriggeringFactCarriedIntoSagaFactIsTheExactRawBytes_NeverAReSerialisedEnvelope()
    {
        var scrambledOrderBytes = """{"payload":{},"occurredAt":"2026-01-02T03:04:05.678Z","causationId":"11111111-1111-1111-1111-111111111111","correlationId":"22222222-2222-2222-2222-222222222222","aggregateId":"33333333-3333-3333-3333-333333333333","eventType":"order.placed.v1","eventId":"44444444-4444-4444-4444-444444444444"}"""u8.ToArray();
        var message = new FactStreamMessage("otc.orders.facts.v1", 0, 0, scrambledOrderBytes);
        var dispatcher = new RecordingDispatcher();

        await RunOneMessageAsync(message, dispatcher);

        var sent = Assert.Single(dispatcher.SentCommands);
        var fact = (SagaFact)sent.GetType().GetProperty("Fact")!.GetValue(sent)!;

        Assert.Equal(scrambledOrderBytes, fact.TriggeringEventEnvelope);
    }

    [Theory]
    [InlineData("order.confirmed.v1")]
    [InlineData("order.completed.v1")]
    [InlineData("order.cancelled.v1")]
    [InlineData("order.saga_failed.v1")]
    public async Task SO2_EachSelfProducedFact_ReachesNoDispatchAndOpensNoScope(string eventType)
    {
        var message = BuildMessage(eventType, BuildSelfProducedPayload(eventType));
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

    /// <summary>
    /// OR1 — the wrapped delegate's failure must actually be observed by
    /// the INJECTED <see cref="FactRetryDispatcher"/>, not merely swallowed
    /// or handled some other way. A poison <see cref="IDispatcher"/> makes
    /// the wrapped process fail on every attempt; with <c>MaxAttempts = 1</c>
    /// the real dispatcher exhausts on its first attempt and publishes to
    /// the (recording, fake) DLQ — a side effect that can only happen if
    /// <c>SagaFactsConsumer</c> genuinely routed the call THROUGH
    /// <see cref="FactRetryDispatcher.DispatchAsync"/>.
    /// </summary>
    [Fact]
    public async Task OR1_TheDispatchGenuinelyGoesThroughTheInjectedRetryDispatcher_NotAroundIt()
    {
        var message = BuildMessage("order.placed.v1", BuildPayload("order.placed.v1"));
        var poisonDispatcher = new ThrowingDispatcher();
        var deadLetters = new RecordingDeadLetterPublisher();
        var factRetryDispatcher = BuildRealFactRetryDispatcher(deadLetters: deadLetters, options: new FactRetryOptions { MaxAttempts = 1, BackoffMs = 500 });

        var subscriber = new FakeFactStreamSubscriber([message]);
        var services = new ServiceCollection();
        services.AddSingleton<IDispatcher>(poisonDispatcher);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var consumer = new SagaFactsConsumer(subscriber, scopeFactory, factRetryDispatcher, NullLogger<SagaFactsConsumer>.Instance);

        await consumer.StartAsync(CancellationToken.None);

        // Poll DIRECTLY for the dead letter the REAL FactRetryDispatcher's
        // own exhaustion path publishes — never `subscriber.Delivered`,
        // whose TaskCompletionSource is set only once `handler(...)`
        // (HandleMessageAsync) RETURNS (see FakeFactStreamSubscriber below).
        // A bypass that calls the wrapped delegate directly lets the poison
        // exception propagate OUT of HandleMessageAsync uncaught, so
        // Delivered is never set and a wait gated behind it degrades to an
        // uninformative TimeoutException that cannot distinguish "the
        // dispatcher hung" from "the dispatch went around it entirely."
        // Paced, bounded, and its own failure NAMES the reason.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (deadLetters.Published.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        await consumer.StopAsync(CancellationToken.None);

        Assert.True(
            deadLetters.Published.Count > 0,
            "No dead letter was published within 5s. The dispatch appears to have gone AROUND FactRetryDispatcher: an unhandled exception from the wrapped delegate would escape HandleMessageAsync directly — bypassing DispatchAsync's own retry-then-exhaust-then-publish logic — rather than being caught, retried and dead-lettered by it.");

        var published = Assert.Single(deadLetters.Published);
        Assert.Equal("order.placed.v1", published.EventType);
        Assert.Equal(1, published.Attempts);
        Assert.Equal(1, poisonDispatcher.Invocations);
    }

    /// <summary>
    /// OR1 (design.md §3.1's own table) — the envelope guard, the
    /// unrouted-eventType branch and SO2's self-produced skip must NEVER
    /// reach <see cref="FactRetryDispatcher.DispatchAsync"/> at all. Proven
    /// with <c>MaxAttempts = 0</c>: if the dispatcher were EVER invoked —
    /// regardless of what the wrapped delegate would have done — the loop
    /// condition (<c>1 &lt;= 0</c>) is false immediately, so it falls
    /// straight through to the dead-letter publish with zero real
    /// attempts. A dead letter appearing here can only mean one of these
    /// three branches was wrongly routed through the dispatcher.
    /// </summary>
    [Theory]
    [InlineData("order.confirmed.v1")]
    [InlineData("order.completed.v1")]
    [InlineData("order.cancelled.v1")]
    [InlineData("order.saga_failed.v1")]
    public async Task OR1_TheEnvelopeGuardUnroutedEventTypeAndSO2BranchesAreNotWrapped_TheDispatcherIsNeverInvoked(string selfProducedEventType)
    {
        var malformed = new FactStreamMessage("otc.orders.facts.v1", 0, 0, "{ not valid json"u8.ToArray());
        var unrouted = BuildMessage("future.fact.v1", new { });
        var selfProduced = BuildMessage(selfProducedEventType, BuildSelfProducedPayload(selfProducedEventType));

        var deadLetters = new RecordingDeadLetterPublisher();
        var poisonOptions = new FactRetryOptions { MaxAttempts = 0, BackoffMs = 500 };

        foreach (var message in new[] { malformed, unrouted, selfProduced })
        {
            var dispatcher = new RecordingDispatcher();
            var factRetryDispatcher = BuildRealFactRetryDispatcher(deadLetters: deadLetters, options: poisonOptions);
            var subscriber = new FakeFactStreamSubscriber([message]);
            var services = new ServiceCollection();
            services.AddSingleton<IDispatcher>(dispatcher);
            var provider = services.BuildServiceProvider();
            var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            var consumer = new SagaFactsConsumer(subscriber, scopeFactory, factRetryDispatcher, NullLogger<SagaFactsConsumer>.Instance);

            await consumer.StartAsync(CancellationToken.None);
            await subscriber.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(50);
            await consumer.StopAsync(CancellationToken.None);
        }

        Assert.Empty(deadLetters.Published);
    }

    private static async Task RunOneMessageAsync(FactStreamMessage message, RecordingDispatcher dispatcher, CountingScopeFactory? scopeFactory = null)
    {
        var subscriber = new FakeFactStreamSubscriber([message]);
        var services = new ServiceCollection();
        services.AddSingleton<IDispatcher>(dispatcher);
        var provider = services.BuildServiceProvider();
        scopeFactory ??= new CountingScopeFactory(dispatcher, provider);

        var consumer = new SagaFactsConsumer(subscriber, scopeFactory, BuildRealFactRetryDispatcher(), NullLogger<SagaFactsConsumer>.Instance);

        await consumer.StartAsync(CancellationToken.None);
        await subscriber.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Give the handler a moment to complete after delivery.
        await Task.Delay(50);
        await consumer.StopAsync(CancellationToken.None);
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
        // Each field defaults to its OWN distinct Guid.NewGuid()/UtcNow —
        // never shared across two positional slots — so a test that asserts
        // one field's value cannot pass merely because two source fields
        // happened to collide (backlog id 60; see
        // EachConsumedFact_DispatchedFactCopiesEveryEnvelopeFieldFromTheSource).
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

    /// <summary>
    /// A REAL <see cref="FactRetryDispatcher"/> over instant fakes — every
    /// case in this file expects the wrapped process delegate to succeed on
    /// its first attempt, so this dispatcher's own retry/backoff/DLQ
    /// machinery is never exercised here (OR1's own behaviour is proven in
    /// <c>FactRetryDispatcherTests</c> and <c>SagaFactsConsumerTests</c>'
    /// own OR1 cases below).
    /// </summary>
    private static FactRetryDispatcher BuildRealFactRetryDispatcher(
        IFactRetryDelay? delay = null,
        IDeadLetterPublisher? deadLetters = null,
        FactRetryOptions? options = null) =>
        new(
            new FakeClock(DateTimeOffset.UtcNow),
            delay ?? new InstantFactRetryDelay(),
            deadLetters ?? new RecordingDeadLetterPublisher(),
            Options.Create(options ?? new FactRetryOptions()),
            NullLogger<FactRetryDispatcher>.Instance);

    private static object BuildPayload(string eventType) => eventType switch
    {
        "order.placed.v1" => new OrderPlacedPayload("ORD-000001", "RETAILER1", "COMPANY1", "4006381333931", "5001234567890", "EUR", DateTimeOffset.UtcNow, [], 0, 0, 0),
        "stock.reserved.v1" => new StockReservedPayload("ORD-000001", "COMPANY1", []),
        "stock.rejected.v1" => new StockRejectedPayload("ORD-000001", "COMPANY1", [], "insufficient_stock"),
        "credit.approved.v1" => new CreditApprovedPayload("ORD-000001", "RETAILER1", "COMPANY1", "CR-000001", "EUR", 1000, 4000),
        "credit.rejected.v1" => new CreditRejectedPayload("ORD-000001", "RETAILER1", "COMPANY1", "EUR", 1000, 500, "over_limit"),
        "stock.released.v1" => new StockReleasedPayload("ORD-000001", "COMPANY1", [], "credit_rejected"),
        "order.despatched.v1" => new OrderDespatchedPayload("ORD-000001", "DES-000001", DateTimeOffset.UtcNow, "COMPANY1", "RETAILER1", []),
        "invoice.issued.v1" => new InvoiceIssuedPayload("ORD-000001", "INV-000001", DateTimeOffset.UtcNow, "RETAILER1", "COMPANY1", "EUR", [], 1000, 0, 1000),
        "payment.received.v1" => new PaymentReceivedPayload("ORD-000001", "INV-000001", "PAY-000001", "EUR", 1000, DateTimeOffset.UtcNow, "gateway"),
        "credit.released.v1" => new CreditReleasedPayload("ORD-000001", "RETAILER1", "COMPANY1", "EUR", 1000, 5000, "order_cancelled"),
        _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
    };

    private static object BuildSelfProducedPayload(string eventType) => eventType switch
    {
        "order.confirmed.v1" => new OrderConfirmedPayload("ORD-000001", "RETAILER1", "COMPANY1", "EUR", 1000, DateTimeOffset.UtcNow),
        "order.completed.v1" => new OrderCompletedPayload("ORD-000001", "RETAILER1", "COMPANY1", "EUR", 1000, DateTimeOffset.UtcNow),
        "order.cancelled.v1" => new OrderCancelledPayload("ORD-000001", "RETAILER1", "COMPANY1", "stock_rejected", DateTimeOffset.UtcNow, []),
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

    /// <summary>Throws on every <see cref="SendAsync{TCommand}"/> call — the wrapped process's failure, for OR1's "genuinely through" case.</summary>
    private sealed class ThrowingDispatcher : IDispatcher
    {
        public int Invocations { get; private set; }

        public Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken) where TCommand : ICommand
        {
            Invocations++;
            throw new InvalidOperationException("simulated handler failure");
        }

        public Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken) where TCommand : ICommand<TResult> => throw new NotSupportedException();

        public Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken) where TQuery : IQuery<TResult> => throw new NotSupportedException();

        public Task PublishAsync(object @event, CancellationToken cancellationToken) => throw new NotSupportedException();
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

    /// <summary>Counts how many scopes were opened — SO2's "opens no scope" assertion.</summary>
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
