// COPY OF — src/Orders/Application/Ports/IFactPublisher.cs
namespace OrderToCash.Fulfillment.Application.Ports;

/// <summary>
/// The relay's only collaborator for talking to the fact stream. The one
/// implementation is <c>KafkaFactPublisher</c>, in
/// <c>Infrastructure/Outbox/</c> — the only namespace
/// <c>FactPublisherConfinementTests</c> allows to reference
/// <c>Confluent.Kafka</c>.
/// </summary>
public interface IFactPublisher
{
    /// <summary>Completes only when the broker has acknowledged EVERY fact; throws otherwise. Never reports partial success.</summary>
    Task PublishAsync(IReadOnlyList<PublishableFact> facts, CancellationToken cancellationToken);
}
