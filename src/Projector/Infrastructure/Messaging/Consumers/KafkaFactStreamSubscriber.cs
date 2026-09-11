using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OrderToCash.Projector.Application.Ports;

namespace OrderToCash.Projector.Infrastructure.Messaging.Consumers;

/// <summary>
/// The ONE type in this service touching <c>Confluent.Kafka</c>'s consumer
/// API (<c>FactConsumerConfinementTests</c> enforces this at namespace
/// granularity). Implements <see cref="IFactStreamSubscriber"/>'s
/// offset-commit-after-handler contract — copied from Orders' own
/// <c>KafkaFactStreamSubscriber</c>, with two deliberate differences:
/// <see cref="BuildConsumerConfig"/>'s <c>GroupId</c>/<c>ClientId</c>, and
/// <see cref="AutoOffsetReset"/> — see that method's own remarks for why
/// this service's default is <see cref="AutoOffsetReset.Earliest"/>, the
/// SAME as Orders' but the OPPOSITE of Notifications' (<c>PR5</c>, ledger
/// <b>L31</b>).
/// </summary>
public sealed class KafkaFactStreamSubscriber(IOptions<ProjectorKafkaOptions> options) : IFactStreamSubscriber
{
    public async Task ConsumeAsync(
        IReadOnlyList<string> topics,
        Func<FactStreamMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        var config = BuildConsumerConfig(options.Value);

        using var consumer = new ConsumerBuilder<Ignore, byte[]>(config).Build();

        // Consume()/Commit() are synchronous, blocking calls; a
        // BackgroundService.ExecuteAsync runs on a thread-pool thread and
        // blocking it before the first `await` would stall host startup
        // (ledger L35) — so this yields first, exactly as Orders' and
        // Notifications' own subscribers do.
        await Task.Yield();

        consumer.Subscribe(topics);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // A BOUNDED poll — returns null when nothing arrived, so the
                // cancellation token is observed every cycle.
                var consumeResult = consumer.Consume(TimeSpan.FromMilliseconds(options.Value.PollTimeoutMs));

                if (consumeResult is null || consumeResult.IsPartitionEOF)
                {
                    continue;
                }

                var message = new FactStreamMessage(
                    consumeResult.Topic,
                    consumeResult.Partition.Value,
                    consumeResult.Offset.Value,
                    consumeResult.Message.Value,
                    DecodeHeaders(consumeResult.Message.Headers));

                // The offset is stored ONLY after the handler has run to
                // completion. A throwing handler propagates unchanged —
                // nothing is stored, and the exception surfaces to
                // ProjectorFactsConsumer's own retry loop.
                await handler(message, cancellationToken).ConfigureAwait(false);

                consumer.StoreOffset(consumeResult);
            }
        }
        finally
        {
            // Commits the final stored offsets and leaves the group cleanly
            // — reached on graceful cancellation, and NOT reached (by
            // design) when the handler above throws.
            consumer.Close();
        }
    }

    /// <summary>
    /// <b>Why <see cref="AutoOffsetReset.Earliest"/>, deliberately the
    /// OPPOSITE of Notifications' <see cref="AutoOffsetReset.Latest"/>.</b>
    /// The projector's store is DERIVED, and its defining property
    /// (<c>feature_list.json</c> id 24's own acceptance bullet 2) is that
    /// replaying the topics reconstructs it. A projector starting at the log
    /// head produces a permanently and silently incomplete read model — a
    /// correctness violation, not a missed email (Notifications' own
    /// justification does not transfer: it owns no aggregate and its effect
    /// is an external side effect, an omission rather than an
    /// inconsistency). <c>PR15</c> is what makes replay safe: a replayed
    /// fact either applies once or is suppressed, and either way the
    /// document converges to the same bytes.
    /// </summary>
    /// <summary>OR4/design.md §5.3 — decodes the raw Kafka header bytes to the string map <see cref="FactStreamMessage.HeaderMap"/> carries, so the consumer can extract <c>traceparent</c>/<c>tracestate</c> without ever referencing <c>Confluent.Kafka</c> outside this class.</summary>
    private static IReadOnlyDictionary<string, string> DecodeHeaders(Headers? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var decoded = new Dictionary<string, string>(headers.Count, StringComparer.Ordinal);
        foreach (var header in headers)
        {
            decoded[header.Key] = System.Text.Encoding.UTF8.GetString(header.GetValueBytes());
        }

        return decoded;
    }

    private static ConsumerConfig BuildConsumerConfig(ProjectorKafkaOptions options) => new()
    {
        BootstrapServers = options.BootstrapServers,
        GroupId = "projector", // identical to ConsumerNames.ToToken(ConsumerName.Projector) — one value for both the broker-side and dedup-ledger identity of this service.
        ClientId = "otc-projector",
        AutoOffsetReset = AutoOffsetReset.Earliest, // PR5/PR38 — a change from the client default (`latest`), not a restatement of it.
        EnableAutoCommit = true, // commits STORED offsets only.
        EnableAutoOffsetStore = false, // the whole point — see the class remarks.
        EnablePartitionEof = false,
    };
}
