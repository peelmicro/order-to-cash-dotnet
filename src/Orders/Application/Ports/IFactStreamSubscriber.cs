namespace OrderToCash.Orders.Application.Ports;

/// <summary>One consumed Kafka message, transport-neutral (design.md §3.1) — never a <c>Confluent.Kafka</c> type, so this port stays clean of the one namespace <c>FactConsumerConfinementTests</c> confines to <c>*.Infrastructure.Messaging.Consumers</c>.</summary>
public sealed record FactStreamMessage(string Topic, int Partition, long Offset, ReadOnlyMemory<byte> Value);

/// <summary>
/// Consumes the fact stream, offset-commit-after-handler (SO9, design.md
/// §3.1, §3.3). The single implementation, <c>KafkaFactStreamSubscriber</c>,
/// is the one type in the repository touching <c>Confluent.Kafka</c>'s
/// consumer API.
/// </summary>
public interface IFactStreamSubscriber
{
    /// <summary>
    /// Consumes each message once, in arrival order, invoking
    /// <paramref name="handler"/> to completion BEFORE the message's offset
    /// becomes eligible for commit (SO9). A handler that throws propagates:
    /// the offset is NOT stored, the loop surfaces the failure to its
    /// caller, and the message is redelivered from the last committed
    /// offset. Returns (rather than throwing) only on graceful cancellation.
    /// </summary>
    Task ConsumeAsync(
        IReadOnlyList<string> topics,
        Func<FactStreamMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken);
}
