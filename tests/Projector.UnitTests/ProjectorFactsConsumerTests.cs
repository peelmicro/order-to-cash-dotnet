using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using OrderToCash.Cqrs;
using OrderToCash.Projector.Application.Commands;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Presentation;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary>
/// Drives the REAL <see cref="ProjectorFactsConsumer"/> end to end
/// (<c>PR36</c>): a fake <see cref="IFactStreamSubscriber"/> in, a
/// recording <see cref="IDispatcher"/> out — the exact path
/// <c>ProjectFactCommandHandlerTests</c> never reaches, since it constructs
/// the handler directly.
/// </summary>
public sealed class ProjectorFactsConsumerTests
{
    public static IEnumerable<object[]> AllFourteenFacts()
    {
        foreach (var eventType in FactCatalog.PayloadTypesByEventType.Keys)
        {
            yield return [eventType];
        }
    }

    /// <summary><c>PR36</c>: each of the fourteen facts reaches the handler through <c>ExecuteAsync</c> itself, typed by the catalogue.</summary>
    [Theory]
    [MemberData(nameof(AllFourteenFacts))]
    public async Task PR36_EachOfTheFourteenFactsReachesTheHandlerThroughExecuteAsyncItself_TypedByTheCatalogue(string eventType)
    {
        var payload = BuildPayload(eventType);
        var message = BuildMessage(eventType, payload);
        var dispatcher = new RecordingDispatcher();

        await RunOneMessageAsync(message, dispatcher);

        var sent = Assert.Single(dispatcher.SentCommands);
        var command = Assert.IsType<ProjectFactCommand>(sent);
        Assert.Equal(eventType, command.Envelope.EventType);
        Assert.IsType(FactCatalog.PayloadTypesByEventType[eventType], command.Envelope.Payload);
    }

    [Fact]
    public async Task PR3_AMalformedEnvelope_IsLoggedAndAcknowledged_WithNoWriteAndNoSignal()
    {
        var message = new FactStreamMessage("otc.orders.facts.v1", 0, 0, "{ not valid json"u8.ToArray());
        var dispatcher = new RecordingDispatcher();
        var logger = new RecordingLogger<ProjectorFactsConsumer>();

        await RunOneMessageAsync(message, dispatcher, logger);

        Assert.Empty(dispatcher.SentCommands); // the writer is never reached — nothing dispatched at all.
        Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error && e.Message.Contains("Malformed", StringComparison.Ordinal)); // N10: the ATTEMPT is what's asserted.
    }

    [Fact]
    public async Task PR4_AnUnknownEventType_IsLoggedAndAcknowledged_NeverSilentlyDiscarded()
    {
        var message = BuildMessage("future.fact.v1", new { });
        var dispatcher = new RecordingDispatcher();
        var logger = new RecordingLogger<ProjectorFactsConsumer>();

        await RunOneMessageAsync(message, dispatcher, logger);

        Assert.Empty(dispatcher.SentCommands);
        Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error && e.Message.Contains("Unknown eventType", StringComparison.Ordinal)); // N10: proves the LOG branch was actually taken, not a bare return.
    }

    /// <summary><c>PR37</c> — one sentinel per copied field, at the wire→dispatched-fact hop.</summary>
    [Fact]
    public async Task PR37_EveryEnvelopeFieldReachesTheDispatchedFactVerbatim_SentinelPerField()
    {
        var sentinelEventId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var sentinelCorrelationId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var sentinelCausationId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var sentinelOccurredAt = new DateTimeOffset(2032, 1, 2, 3, 4, 5, 678, TimeSpan.Zero);
        const string eventType = "order.placed.v1";

        var envelope = new Envelope<object>(
            sentinelEventId,
            eventType,
            sentinelCorrelationId,
            sentinelCorrelationId,
            sentinelCausationId,
            sentinelOccurredAt,
            BuildPayload(eventType));

        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWire.Options);
        var message = new FactStreamMessage("otc.orders.facts.v1", 0, 0, bytes);
        var dispatcher = new RecordingDispatcher();

        await RunOneMessageAsync(message, dispatcher);

        var command = Assert.IsType<ProjectFactCommand>(Assert.Single(dispatcher.SentCommands));
        Assert.Equal(sentinelEventId, command.Envelope.EventId);
        Assert.Equal(sentinelCorrelationId, command.Envelope.CorrelationId);
        Assert.Equal(sentinelCausationId, command.Envelope.CausationId);
        Assert.Equal(sentinelOccurredAt, command.Envelope.OccurredAt);
    }

    private static object BuildPayload(string eventType) => eventType switch
    {
        "order.placed.v1" => new OrderPlacedPayload("ORD-000001", "RET1", "COM1", "bgln", "sgln", "EUR", DateTimeOffset.UtcNow, [], 0, 0, 0),
        "stock.reserved.v1" => new StockReservedPayload("ORD-000001", "COM1", [new ReservationRef(Guid.NewGuid(), "SKU1", 1)]),
        "stock.rejected.v1" => new StockRejectedPayload("ORD-000001", "COM1", [new Shortage("SKU1", 2, 1)], "insufficient_stock"),
        "stock.released.v1" => new StockReleasedPayload("ORD-000001", "COM1", [new ReservationRef(Guid.NewGuid(), "SKU1", 1)], "order_cancelled"),
        "credit.approved.v1" => new CreditApprovedPayload("ORD-000001", "RET1", "COM1", "CR-1", "EUR", 1, 1),
        "credit.rejected.v1" => new CreditRejectedPayload("ORD-000001", "RET1", "COM1", "EUR", 1, 1, "insufficient_credit"),
        "credit.released.v1" => new CreditReleasedPayload("ORD-000001", "RET1", "COM1", "EUR", 1, 1, "invoice_paid"),
        "order.confirmed.v1" => new OrderConfirmedPayload("ORD-000001", "RET1", "COM1", "EUR", 1, DateTimeOffset.UtcNow),
        "order.despatched.v1" => new OrderDespatchedPayload("ORD-000001", "DES-1", DateTimeOffset.UtcNow, "COM1", "RET1", []),
        "invoice.issued.v1" => new InvoiceIssuedPayload("ORD-000001", "INV-1", DateTimeOffset.UtcNow, "RET1", "COM1", "EUR", [], 1, 0, 1),
        "payment.received.v1" => new PaymentReceivedPayload("ORD-000001", "INV-1", "PAY-1", "EUR", 1, DateTimeOffset.UtcNow, "bank_transfer"),
        "order.completed.v1" => new OrderCompletedPayload("ORD-000001", "RET1", "COM1", "EUR", 1, DateTimeOffset.UtcNow),
        "order.cancelled.v1" => new OrderCancelledPayload("ORD-000001", "RET1", "COM1", "buyer_requested", DateTimeOffset.UtcNow, []),
        "order.saga_failed.v1" => new OrderSagaFailedPayload("ORD-000001", "cmd", 1, "err", DateTimeOffset.UtcNow),
        _ => new { },
    };

    private static FactStreamMessage BuildMessage(string eventType, object payload)
    {
        var correlationId = Guid.NewGuid();
        var envelope = new Envelope<object>(Guid.NewGuid(), eventType, correlationId, correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow, payload);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonWire.Options);
        return new FactStreamMessage("otc.orders.facts.v1", 0, 0, bytes);
    }

    private static Task RunOneMessageAsync(FactStreamMessage message, RecordingDispatcher dispatcher) =>
        RunOneMessageAsync(message, dispatcher, new RecordingLogger<ProjectorFactsConsumer>());

    private static async Task RunOneMessageAsync(FactStreamMessage message, RecordingDispatcher dispatcher, RecordingLogger<ProjectorFactsConsumer> logger)
    {
        var subscriber = new FakeFactStreamSubscriber([message]);
        var services = new ServiceCollection();
        services.AddSingleton<IDispatcher>(dispatcher);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var consumer = new ProjectorFactsConsumer(subscriber, scopeFactory, logger);

        await consumer.StartAsync(CancellationToken.None);
        await subscriber.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(50);
        await consumer.StopAsync(CancellationToken.None);
    }

    /// <summary>Records every log CALL — not merely a residue — so <c>PR3</c>/<c>PR4</c> can assert the ATTEMPT (<c>N10</c>), not just the dispatcher's silence.</summary>
    private sealed class RecordingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

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
}
