using Confluent.Kafka;

namespace OrderToCash.Projector.UnitTests;

/// <summary>
/// A recording <see cref="IProducer{TKey, TValue}"/> — no real broker, no
/// real network call. <c>KafkaDeadLetterPublisher.PublishAsync</c> only
/// ever calls <see cref="ProduceAsync"/>; every other member throws
/// <see cref="NotSupportedException"/>, so a future change reaching for one
/// of them fails loudly here rather than silently no-op-ing.
/// </summary>
internal sealed class RecordingKafkaProducer : IProducer<string, byte[]>
{
    private readonly List<(string Topic, Message<string, byte[]> Message)> _produced = [];

    public IReadOnlyList<(string Topic, Message<string, byte[]> Message)> Produced => _produced;

    public Handle Handle => throw new NotSupportedException();

    public string Name => "recording-producer";

    public Task<DeliveryResult<string, byte[]>> ProduceAsync(string topic, Message<string, byte[]> message, CancellationToken cancellationToken = default)
    {
        _produced.Add((topic, message));
        return Task.FromResult(new DeliveryResult<string, byte[]> { Topic = topic, Message = message });
    }

    public Task<DeliveryResult<string, byte[]>> ProduceAsync(TopicPartition topicPartition, Message<string, byte[]> message, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public void Produce(string topic, Message<string, byte[]> message, Action<DeliveryReport<string, byte[]>>? deliveryHandler = null) =>
        throw new NotSupportedException();

    public void Produce(TopicPartition topicPartition, Message<string, byte[]> message, Action<DeliveryReport<string, byte[]>>? deliveryHandler = null) =>
        throw new NotSupportedException();

    public int Poll(TimeSpan timeout) => throw new NotSupportedException();

    public int Flush(TimeSpan timeout) => throw new NotSupportedException();

    public void Flush(CancellationToken cancellationToken = default)
    {
    }

    public void InitTransactions(TimeSpan timeout) => throw new NotSupportedException();

    public void BeginTransaction() => throw new NotSupportedException();

    public void CommitTransaction(TimeSpan timeout) => throw new NotSupportedException();

    public void CommitTransaction() => throw new NotSupportedException();

    public void AbortTransaction(TimeSpan timeout) => throw new NotSupportedException();

    public void AbortTransaction() => throw new NotSupportedException();

    public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout) =>
        throw new NotSupportedException();

    public int AddBrokers(string brokers) => throw new NotSupportedException();

    public void SetSaslCredentials(string username, string password) => throw new NotSupportedException();

    public void Dispose()
    {
    }
}
