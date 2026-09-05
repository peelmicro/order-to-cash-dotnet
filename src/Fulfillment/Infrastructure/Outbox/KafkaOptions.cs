// COPY OF — src/Orders/Infrastructure/Outbox/KafkaOptions.cs
namespace OrderToCash.Fulfillment.Infrastructure.Outbox;

/// <summary>The producer's own configuration. Bound by <c>AddFulfillment*</c>, never by this class.</summary>
public sealed class KafkaOptions
{
    /// <summary><c>kafka:29092</c> inside compose; <c>localhost:9092</c> for a host process.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>design.md §8.2 — must not collide with Orders' own <c>otc-orders</c> client id.</summary>
    public string ClientId { get; set; } = "otc-fulfillment";
}
