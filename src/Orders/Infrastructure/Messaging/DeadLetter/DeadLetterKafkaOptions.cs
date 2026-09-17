namespace OrderToCash.Orders.Infrastructure.Messaging.DeadLetter;

/// <summary>
/// Producer settings for <see cref="KafkaDeadLetterPublisher"/> — a
/// DEDICATED client id, distinct from the outbox relay's own
/// <c>otc-orders</c> producer (design.md §3.3: the DLQ publish is a
/// republication, not an outbox flush, and the two producers' delivery
/// reports must never be attributed to one another in the broker's own
/// client metrics).
/// </summary>
public sealed class DeadLetterKafkaOptions
{
    /// <summary>
    /// Backlog id 104 — empty means "not set explicitly". <c>string.Empty</c>
    /// rather than <c>"localhost:9092"</c>, so <c>AddOrdersSaga</c> can tell
    /// an unconfigured value apart from a deliberate one and fall back to
    /// <c>OrdersSagaOptions.Kafka.BootstrapServers</c> — the same broker the
    /// service's own Kafka client already points at — instead of silently
    /// defaulting to a broker on <c>localhost</c> that may not be the one
    /// under test.
    /// </summary>
    public string BootstrapServers { get; set; } = string.Empty;

    public string ClientId { get; set; } = "otc-orders-dlq";
}
