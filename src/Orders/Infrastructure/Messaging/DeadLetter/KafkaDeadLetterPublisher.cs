using System.Globalization;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Messaging.DeadLetter;

/// <summary>
/// The one implementation of <see cref="IDeadLetterPublisher"/> — design.md
/// §3.3. Publishes the UNMODIFIED original envelope bytes, byte-for-byte
/// (never a re-serialised <see cref="OrderToCash.Contracts.Envelopes.Envelope{TPayload}"/>),
/// to <c>&lt;source topic&gt;.dlq</c>, carrying the <c>DeadLetterHeaders</c>
/// set <c>specs/shared/asyncapi.yaml</c> declares.
/// </summary>
/// <remarks>
/// Lives in <c>Infrastructure/Messaging/DeadLetter/</c> rather than
/// <c>Infrastructure/Outbox/</c> — design.md §3.3's widened confinement:
/// <c>Projector</c> and <c>Notifications</c> own no outbox at all, and a
/// folder literally named <c>Outbox</c> in a service with none would be a
/// lie the next reader has to disprove. <c>FactPublisherConfinementTests</c>
/// is widened to admit this namespace for exactly the four Kafka producer
/// types, and re-armed after the widening (tasks.md A1e).
/// </remarks>
public sealed class KafkaDeadLetterPublisher : IDeadLetterPublisher, IDisposable
{
    private readonly IProducer<string, byte[]> _producer;

    public KafkaDeadLetterPublisher(IOptions<DeadLetterKafkaOptions> options)
        : this(BuildProducer(options.Value))
    {
    }

    /// <summary>Test seam — a caller may hand in an already-built producer (e.g. pointed at a Testcontainers broker) without going through <see cref="IOptions{TOptions}"/>.</summary>
    public KafkaDeadLetterPublisher(IProducer<string, byte[]> producer) => _producer = producer;

    /// <summary>The <see cref="ProducerConfig"/> this adapter builds — exposed for the same reason <c>KafkaFactPublisher.BuildProducerConfig</c> is.</summary>
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

        // OR4's own hop (design.md §5), deliberately guarded here rather
        // than fabricated: no traceparent header at all when no span is
        // active — which is every call until feature 27's own A3 group
        // wires OpenTelemetry into these hosts.
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

        // ProduceAsync completes when the broker acknowledges (Acks.All
        // above) or throws — never a fire-and-forget Produce().
        await _producer.ProduceAsync($"{publication.SourceTopic}.dlq", message, cancellationToken);
    }

    public void Dispose() => _producer.Dispose();

    private static IProducer<string, byte[]> BuildProducer(DeadLetterKafkaOptions options) =>
        new ProducerBuilder<string, byte[]>(BuildProducerConfig(options)).Build();
}
