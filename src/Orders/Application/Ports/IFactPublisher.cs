namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// The relay's only collaborator for talking to the fact stream (design.md
/// §5.3). The one implementation is <c>KafkaFactPublisher</c>, in
/// <c>Infrastructure/Outbox/</c> — the only namespace
/// <c>FactPublisherConfinementTests</c> (OI16) allows to reference
/// <c>Confluent.Kafka</c>.
/// </summary>
public interface IFactPublisher
{
    /// <summary>Completes only when the broker has acknowledged EVERY fact; throws otherwise. Never reports partial success.</summary>
    Task PublishAsync(IReadOnlyList<PublishableFact> facts, CancellationToken cancellationToken);
}
