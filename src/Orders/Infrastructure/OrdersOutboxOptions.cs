using OrderToCash.Orders.Infrastructure.Outbox;

namespace OrderToCash.Orders.Infrastructure;

/// <summary>The configuration <see cref="OrdersOutboxServiceCollectionExtensions.AddOrdersOutbox"/> needs — populated by the caller's <c>Action&lt;OrdersOutboxOptions&gt;</c>, not by binding <c>IConfiguration</c> here (design.md §8: feature 15's <c>Program.cs</c> does the binding, where the configuration root actually exists).</summary>
public sealed class OrdersOutboxOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public KafkaOptions Kafka { get; } = new();

    public OutboxRelayOptions Relay { get; } = new();
}
