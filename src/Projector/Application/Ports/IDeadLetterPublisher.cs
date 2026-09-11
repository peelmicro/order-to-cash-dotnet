// COPY OF — src/Orders/Application/Ports/IDeadLetterPublisher.cs
namespace OrderToCash.Projector.Application.Ports;

/// <summary>
/// Everything <c>KafkaDeadLetterPublisher</c>
/// (<c>Infrastructure/Messaging/DeadLetter/</c>) needs to build the
/// <c>DeadLetterHeaders</c> set (<c>specs/shared/asyncapi.yaml</c>) and
/// publish the UNMODIFIED original envelope bytes to
/// <c>&lt;source topic&gt;.dlq</c> — design.md §3.3, OR1.
/// </summary>
public sealed record DeadLetterPublication(
    string SourceTopic,
    ReadOnlyMemory<byte> OriginalEnvelope,
    ConsumerName FailedConsumer,
    int Attempts,
    string Error,
    string EventType,
    DateTimeOffset FirstFailedAt,
    DateTimeOffset FailedAt);

/// <summary>
/// Publishes a poison fact's unmodified original bytes to its source
/// topic's <c>.dlq</c> topic, after
/// <see cref="Infrastructure.Messaging.FactRetryDispatcher"/> (design.md
/// §3.2's canonical copy) has exhausted its retry budget — OR1, R16. The one
/// implementation, <c>KafkaDeadLetterPublisher</c>, is the only type in
/// <c>*.Infrastructure.Messaging.DeadLetter</c> touching
/// <c>Confluent.Kafka</c>'s producer API (<c>FactPublisherConfinementTests</c>,
/// widened design.md §3.3).
/// </summary>
public interface IDeadLetterPublisher
{
    /// <summary>Completes only when the broker has acknowledged the dead letter; throws otherwise.</summary>
    Task PublishAsync(DeadLetterPublication publication, CancellationToken cancellationToken);
}
