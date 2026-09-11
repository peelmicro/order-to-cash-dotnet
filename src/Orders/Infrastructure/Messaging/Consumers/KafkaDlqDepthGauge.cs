using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Observability;
using OrderToCash.Orders.Infrastructure.Outbox;

namespace OrderToCash.Orders.Infrastructure.Messaging.Consumers;

/// <summary>
/// OR5/design.md §7, ported cases 67-69 — <c>otc_dlq_depth</c>: sums
/// <c>(high − low)</c> across every partition, per <c>.dlq</c> topic,
/// independently — a topic that does not exist yet reports <c>0</c> and
/// never throws. The ONE type outside <c>*.Infrastructure.Messaging.Consumers</c>
/// that is NOT allowed to touch <c>Confluent.Kafka</c>'s consumer client is
/// everything else in this service — <c>FactConsumerConfinementTests</c>
/// confines <see cref="IConsumer{TKey,TValue}"/>/<see cref="ConsumerBuilder{TKey,TValue}"/>/
/// <see cref="ConsumerConfig"/> to this namespace, which is why this class
/// (built for <see cref="OrderToCash.Orders.Infrastructure.Outbox.OutboxRelay"/>,
/// design.md §7's "OutboxRelay's own cycle") lives HERE rather than beside
/// it. Never the long-lived relay producer's config (ledger L27's sibling
/// concern, applied here to a dedicated, short-timeout consumer instead).
/// </summary>
public sealed class KafkaDlqDepthGauge : IDlqDepthGauge, IDisposable
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);

    private readonly IAdminClient _admin;
    private readonly IConsumer<Ignore, Ignore> _consumer;
    private readonly IReadOnlyList<string> _dlqTopics;

    public KafkaDlqDepthGauge(IOptions<KafkaOptions> options)
    {
        var bootstrapServers = options.Value.BootstrapServers;

        // The three saga-fact topics are the only ones this service's own
        // dead-letter publisher (KafkaDeadLetterPublisher, design.md §3.3)
        // ever republishes to — SagaFactTopics.All, `.dlq`-suffixed.
        _dlqTopics = SagaFactTopics.All.Select(topic => $"{topic}.dlq").ToList();

        _admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = bootstrapServers,
            SocketTimeoutMs = (int)_timeout.TotalMilliseconds,
        }).Build();

        // A DEDICATED, short-timeout client — never the long-lived relay
        // producer (ledger L27's sibling concern) — that never subscribes
        // or joins a group; QueryWatermarkOffsets is the only call made on
        // it, so a random per-process group id is fine.
        _consumer = new ConsumerBuilder<Ignore, Ignore>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"otc-dlq-depth-probe-{Guid.NewGuid():N}",
            SocketTimeoutMs = (int)_timeout.TotalMilliseconds,
        }).Build();
    }

    public Task RecordAsync(CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var topic in _dlqTopics)
        {
            total += GetTopicDepth(topic);
        }

        OtcMetrics.DlqDepth.Record(total);
        return Task.CompletedTask;
    }

    private long GetTopicDepth(string topic)
    {
        try
        {
            var metadata = _admin.GetMetadata(topic, _timeout);
            var topicMetadata = metadata.Topics.SingleOrDefault(t => t.Topic == topic);

            if (topicMetadata is null || topicMetadata.Error.Code != ErrorCode.NoError || topicMetadata.Partitions.Count == 0)
            {
                // A topic that has not been created yet — 0, never a throw
                // (ported case 68).
                return 0;
            }

            long depth = 0;
            foreach (var partition in topicMetadata.Partitions)
            {
                var watermarks = _consumer.QueryWatermarkOffsets(new TopicPartition(topic, new Partition(partition.PartitionId)), _timeout);
                depth += Math.Max(0, watermarks.High.Value - watermarks.Low.Value);
            }

            return depth;
        }
        catch (KafkaException)
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _admin.Dispose();
        _consumer.Dispose();
    }
}
