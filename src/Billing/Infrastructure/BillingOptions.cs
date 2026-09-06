using OrderToCash.Billing.Infrastructure.Messaging;
using OrderToCash.Billing.Infrastructure.Outbox;

namespace OrderToCash.Billing.Infrastructure;

/// <summary>`BC21`/design.md §4.1 — the responder's own concurrency bound. Default 32, chosen against ADO.NET's default <c>Max Pool Size</c> of 100, the same reasoning <c>fulfillment_stock/design.md</c> §6.2 gives.</summary>
public sealed class CreditResponderOptions
{
    public int MaxConcurrentRequests { get; set; } = 32;
}

/// <summary>The configuration <c>BillingServiceCollectionExtensions</c> needs (design.md §14.1).</summary>
public sealed class BillingOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public NatsOptions Nats { get; } = new();

    public KafkaOptions Kafka { get; } = new() { ClientId = "otc-billing" };

    public OutboxRelayOptions Relay { get; } = new();

    public CreditResponderOptions Responder { get; } = new();
}
