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
    public string BootstrapServers { get; set; } = "localhost:9092";

    public string ClientId { get; set; } = "otc-orders-dlq";
}
