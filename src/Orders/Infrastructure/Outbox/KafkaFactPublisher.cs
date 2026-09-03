using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Outbox;

/// <summary>
/// The one implementation of <see cref="IFactPublisher"/> — design.md §5.3.
/// <c>Confluent.Kafka</c> directly, not a wrapper: the relay needs explicit
/// control of the key, the idempotence flags and the acknowledgement point.
/// The producer is <see cref="IDisposable"/> (<c>CA2213</c> is an error in
/// this repository, so a forgotten dispose fails the build).
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

    /// <summary>The <see cref="ProducerConfig"/> this adapter builds — exposed so <c>KafkaFactPublisherConfigTests</c> (OI7) can assert on it without mocking a broker.</summary>
    public static ProducerConfig BuildProducerConfig(KafkaOptions options) => new()
    {
        BootstrapServers = options.BootstrapServers,
        ClientId = options.ClientId,
        // OI7: a client-internal retry must neither reorder a partition's
        // records nor create a broker-side duplicate of a record the broker
        // already accepted. EnableIdempotence pins Acks=All and keeps
        // retries effectively unbounded; MaxInFlight stays at librdkafka's
        // default of 5 DELIBERATELY — the idempotent producer preserves
        // per-partition order at up to five in-flight requests, which is
        // the substantive difference from #7's kafkajs client, which pins
        // maxInFlightRequests = 1 to get the same guarantee (design.md
        // §5.3).
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

            // ProduceAsync completes when the broker acknowledges (Acks.All
            // above) or throws — never a fire-and-forget Produce() with a
            // delivery-report callback, so a failure here propagates
            // synchronously to the relay's own await (R14, OI14).
            await _producer.ProduceAsync(OrdersFactTopic.Name, message, cancellationToken);
        }
    }

    public void Dispose() => _producer.Dispose();

    private static IProducer<string, byte[]> BuildProducer(KafkaOptions options) =>
        new ProducerBuilder<string, byte[]>(BuildProducerConfig(options)).Build();
}
