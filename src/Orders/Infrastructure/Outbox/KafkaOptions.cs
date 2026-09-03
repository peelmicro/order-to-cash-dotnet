namespace OrderToCash.Orders.Infrastructure.Outbox;

/// <summary>The producer's own configuration — design.md §8. Bound by <c>AddOrdersOutbox</c>, never by this class.</summary>
public sealed class KafkaOptions
{
    /// <summary><c>kafka:29092</c> inside compose; <c>localhost:9092</c> for a host process. <c>KAFKA_INTERNAL_HOST</c> / <c>KAFKA_HOST_PORT</c> in <c>.env</c> stay the source of truth for the broker itself.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    public string ClientId { get; set; } = "otc-orders";
}
