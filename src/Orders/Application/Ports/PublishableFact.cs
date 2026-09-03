namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// One outbox row, already rendered to wire bytes, ready for the producer
/// (design.md §5.3). <see cref="Key"/> is the Kafka partition key —
/// <c>correlationId</c>, rendered <c>Guid.ToString()</c> (R15).
/// </summary>
public sealed record PublishableFact(
    string Key,
    ReadOnlyMemory<byte> EnvelopeJson,
    IReadOnlyDictionary<string, string> Headers);
