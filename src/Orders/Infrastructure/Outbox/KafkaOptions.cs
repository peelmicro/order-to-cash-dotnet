namespace OrderToCash.Orders.Infrastructure.Outbox;

/// <summary>The outbox relay producer's own configuration. Bound by the service's own registration extension, never by this class.</summary>
public sealed class KafkaOptions
{
    /// <summary><c>kafka:29092</c> inside compose; <c>localhost:9092</c> for a host process. <c>KAFKA_INTERNAL_HOST</c> / <c>KAFKA_HOST_PORT</c> in <c>.env</c> stay the source of truth for the broker itself.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// The producer's Kafka client id. Deliberately has NO default and is
    /// <c>required</c>: this file is byte-identical in every service
    /// (design.md §8.2.4), so the one value that must differ per service is
    /// pushed out to that service's own options class, and omitting it is a
    /// CS9035 build error rather than a producer that silently registers as
    /// librdkafka's default and shares an identity with another service.
    /// </summary>
    public required string ClientId { get; set; }
}
