// COPY OF — src/Orders/Infrastructure/Messaging/DeadLetter/KafkaDeadLetterPublisher.cs
using System.Globalization;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OrderToCash.Projector.Application.Ports;

namespace OrderToCash.Projector.Infrastructure.Messaging.DeadLetter;

/// <summary>
/// The one implementation of <see cref="IDeadLetterPublisher"/> — design.md
/// §3.3. Publishes the UNMODIFIED original envelope bytes, byte-for-byte, to
/// <c>&lt;source topic&gt;.dlq</c>, carrying the <c>DeadLetterHeaders</c> set
/// <c>specs/shared/asyncapi.yaml</c> declares.
/// </summary>
public sealed class KafkaDeadLetterPublisher : IDeadLetterPublisher, IDisposable
{
    private readonly IProducer<string, byte[]> _producer;

    public KafkaDeadLetterPublisher(IOptions<DeadLetterKafkaOptions> options)
        : this(BuildProducer(options.Value))
    {
    }

    /// <summary>Test seam — a caller may hand in an already-built producer (e.g. pointed at a Testcontainers broker) without going through <see cref="IOptions{TOptions}"/>.</summary>
    public KafkaDeadLetterPublisher(IProducer<string, byte[]> producer) => _producer = producer;

    public static ProducerConfig BuildProducerConfig(DeadLetterKafkaOptions options) => new()
    {
        BootstrapServers = options.BootstrapServers,
        ClientId = options.ClientId,
        EnableIdempotence = true,
        Acks = Acks.All,
        MessageSendMaxRetries = int.MaxValue,
        MaxInFlight = 5,
    };

    public async Task PublishAsync(DeadLetterPublication publication, CancellationToken cancellationToken)
    {
        var headers = new Headers();
        void AddHeader(string key, string value) => headers.Add(key, System.Text.Encoding.UTF8.GetBytes(value));

        AddHeader("x-failed-consumer", ConsumerNames.ToToken(publication.FailedConsumer));
        AddHeader("x-attempts", publication.Attempts.ToString(CultureInfo.InvariantCulture));
        AddHeader("x-error", publication.Error);
        AddHeader("x-original-topic", publication.SourceTopic);
        AddHeader("x-first-failed-at", publication.FirstFailedAt.ToString("O", CultureInfo.InvariantCulture));
        AddHeader("x-failed-at", publication.FailedAt.ToString("O", CultureInfo.InvariantCulture));
        AddHeader("x-event-type", publication.EventType);

        var traceparent = System.Diagnostics.Activity.Current?.Id;
        if (traceparent is not null)
        {
            AddHeader("traceparent", traceparent);
        }

        var message = new Message<string, byte[]>
        {
            Key = publication.EventType,
            Value = publication.OriginalEnvelope.ToArray(),
            Headers = headers,
        };

        await _producer.ProduceAsync($"{publication.SourceTopic}.dlq", message, cancellationToken);
    }

    public void Dispose() => _producer.Dispose();

    private static IProducer<string, byte[]> BuildProducer(DeadLetterKafkaOptions options) =>
        new ProducerBuilder<string, byte[]>(BuildProducerConfig(options)).Build();
}
