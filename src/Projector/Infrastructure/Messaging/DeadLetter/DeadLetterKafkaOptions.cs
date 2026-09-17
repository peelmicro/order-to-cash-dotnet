// COPY OF — src/Orders/Infrastructure/Messaging/DeadLetter/DeadLetterKafkaOptions.cs
namespace OrderToCash.Projector.Infrastructure.Messaging.DeadLetter;

/// <summary>Producer settings for <see cref="KafkaDeadLetterPublisher"/> — a DEDICATED client id (design.md §3.3).</summary>
public sealed class DeadLetterKafkaOptions
{
    /// <summary>
    /// Backlog id 104 — empty means "not set explicitly". <c>string.Empty</c>
    /// rather than <c>"localhost:9092"</c>, so <c>AddProjector</c> can tell an
    /// unconfigured value apart from a deliberate one and fall back to
    /// <c>ProjectorOptions.Kafka.BootstrapServers</c> — the same broker the
    /// service's own Kafka client already points at — instead of silently
    /// defaulting to a broker on <c>localhost</c> that may not be the one
    /// under test.
    /// </summary>
    public string BootstrapServers { get; set; } = string.Empty;

    public string ClientId { get; set; } = "otc-projector-dlq";
}
