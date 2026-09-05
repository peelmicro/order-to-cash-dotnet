// COPY OF — src/Orders/Infrastructure/Outbox/KafkaFactPublisher.cs
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OrderToCash.Fulfillment.Application.Ports;

namespace OrderToCash.Fulfillment.Infrastructure.Outbox;

/// <summary>
/// The one implementation of <see cref="IFactPublisher"/>. <c>Confluent.Kafka</c>
/// directly, not a wrapper: the relay needs explicit control of the key, the
/// idempotence flags and the acknowledgement point. The producer is
/// <see cref="IDisposable"/> (<c>CA2213</c> is an error in this repository).
/// </summary>
public sealed class KafkaFactPublisher : IFactPublisher, IDisposable
{
    private readonly IProducer<string, byte[]> _producer;

    public KafkaFactPublisher(IOptions<KafkaOptions> options)
        : this(BuildProducer(options.Value))
    {
    }

    /// <summary>Test seam — a caller may hand in an already-built producer (e.g. pointed at a Testcontainers broker) without going through <see cref="IOptions{TOptions}"/>.</summary>
    public KafkaFactPublisher(IProducer<string, byte[]> producer) => _producer = producer;

    /// <summary>The <see cref="ProducerConfig"/> this adapter builds — exposed so a config test can assert on it without mocking a broker.</summary>
    public static ProducerConfig BuildProducerConfig(KafkaOptions options) => new()
    {
        BootstrapServers = options.BootstrapServers,
        ClientId = options.ClientId,
        EnableIdempotence = true,
        Acks = Acks.All,
        MessageSendMaxRetries = int.MaxValue,
        MaxInFlight = 5,
    };

    /// <summary>Completes only when the broker has acknowledged EVERY fact; throws otherwise. Never reports partial success — every <c>ProduceAsync</c> is awaited before this method returns.</summary>
    public async Task PublishAsync(IReadOnlyList<PublishableFact> facts, CancellationToken cancellationToken)
    {
        foreach (var fact in facts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var headers = new Headers();
            foreach (var (key, value) in fact.Headers)
            {
                headers.Add(key, System.Text.Encoding.UTF8.GetBytes(value));
            }

            var message = new Message<string, byte[]>
            {
                Key = fact.Key,
                Value = fact.EnvelopeJson.ToArray(),
                Headers = headers,
            };

            await _producer.ProduceAsync(FulfillmentFactTopic.Name, message, cancellationToken);
        }
    }

    public void Dispose() => _producer.Dispose();

    private static IProducer<string, byte[]> BuildProducer(KafkaOptions options) =>
        new ProducerBuilder<string, byte[]>(BuildProducerConfig(options)).Build();
}
