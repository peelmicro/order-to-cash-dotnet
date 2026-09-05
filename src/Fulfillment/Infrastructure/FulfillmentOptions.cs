using OrderToCash.Fulfillment.Infrastructure.Messaging;
using OrderToCash.Fulfillment.Infrastructure.Outbox;

namespace OrderToCash.Fulfillment.Infrastructure;

/// <summary>`FS18`/§6.2 — the responder's own concurrency bound.</summary>
public sealed class StockResponderOptions
{
    /// <summary>
    /// Default 32, chosen against ADO.NET's default <c>Max Pool Size</c> of
    /// 100: a request blocked on a stock row lock holds its pooled
    /// connection for the whole wait, so the bound must stay comfortably
    /// below the pool so a lock convoy degrades into WAITING rather than
    /// pool-exhaustion timeouts.
    /// </summary>
    public int MaxConcurrentRequests { get; set; } = 32;
}

/// <summary>The configuration <c>FulfillmentServiceCollectionExtensions</c> needs (design.md §10.1).</summary>
public sealed class FulfillmentOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public NatsOptions Nats { get; } = new();

    public KafkaOptions Kafka { get; } = new();

    public OutboxRelayOptions Relay { get; } = new();

    public StockResponderOptions Responder { get; } = new();
}
