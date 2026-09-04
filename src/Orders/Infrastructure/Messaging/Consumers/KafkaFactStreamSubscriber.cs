using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Messaging.Consumers;

/// <summary>
/// The ONE type in the repository touching <c>Confluent.Kafka</c>'s consumer
/// API (design.md §3.1-§3.3; <c>FactConsumerConfinementTests</c> enforces
/// this at namespace granularity). Implements <see cref="IFactStreamSubscriber"/>'s
/// SO9 contract: the offset is stored ONLY after the handler returns
/// successfully.
/// </summary>
/// <remarks>
/// <b>The §3.2 default table, and where the numbers came from</b> — quoted
/// from the <c>Confluent.Kafka</c> 2.15.0 XML documentation shipped with the
/// pinned package
/// (<c>~/.nuget/packages/confluent.kafka/2.15.0/lib/net10.0/Confluent.Kafka.xml</c>),
/// not from memory or the web:
/// <list type="bullet">
/// <item><c>session.timeout.ms</c> default 45 000 — broker-side liveness,
/// NOT the binding constraint: librdkafka heartbeats from its own background
/// thread, so a slow application handler does not miss them.</item>
/// <item><c>heartbeat.interval.ms</c> default 3 000 — sent independently of
/// this class's own <c>Consume()</c> cadence.</item>
/// <item><c>max.poll.interval.ms</c> default 300 000 — THIS is the binding
/// constraint: if <c>Consume()</c> is not called within it, librdkafka fails
/// the member and the group rebalances. SO4's ~16.5 s worst case is not even
/// on this loop (SO10, design.md §5.5), so the real headroom is 300 s, twenty
/// times #7's own 30 s kafkajs session-timeout budget.</item>
/// <item><c>enable.auto.commit</c> kept <see langword="true"/> — commits
/// STORED offsets only (see below).</item>
/// <item><c>enable.auto.offset.store</c> default <see langword="true"/>,
/// OVERRIDDEN to <see langword="false"/> here — the whole point of SO9. The
/// library default stores a message's offset the moment <c>Consume()</c>
/// RETURNS it, and the background committer commits stored offsets every
/// <c>auto.commit.interval.ms</c> (default 5 000) regardless of what the
/// handler did — at-most-once, not at-least-once. This class stores the
/// offset itself, and only after the handler has run to completion.</item>
/// <item><c>auto.offset.reset</c> default <c>largest</c>, OVERRIDDEN to
/// <see cref="AutoOffsetReset.Earliest"/> (SO1) — a first boot with the
/// default would SKIP every fact already on the topic, exactly the live
/// stack's steady state at this feature's own first boot (design.md
/// §8.2).</item>
/// </list>
/// </remarks>
public sealed class KafkaFactStreamSubscriber(IOptions<OrdersSagaOptions> options) : IFactStreamSubscriber
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
        // (design.md §3.1) — so this yields first.
        await Task.Yield();

        consumer.Subscribe(topics);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // A BOUNDED poll — returns null when nothing arrived, so the
                // cancellation token is observed every cycle.
                // Consume(CancellationToken) is deliberately not used: it
                // blocks indefinitely and only unblocks on cancellation,
                // which makes a graceful drain harder to reason about for
                // the sake of one fewer wake-up per poll interval.
                var consumeResult = consumer.Consume(TimeSpan.FromMilliseconds(options.Value.Kafka.PollTimeoutMs));

                if (consumeResult is null || consumeResult.IsPartitionEOF)
                {
                    continue;
                }

                var message = new FactStreamMessage(
                    consumeResult.Topic,
                    consumeResult.Partition.Value,
                    consumeResult.Offset.Value,
                    consumeResult.Message.Value);

                // SO9's whole point: the offset is stored ONLY after the
                // handler has run to completion. A throwing handler
                // propagates unchanged — nothing is stored, and the
                // exception surfaces to SagaFactsConsumer's own loop.
                await handler(message, cancellationToken).ConfigureAwait(false);

                consumer.StoreOffset(consumeResult);
            }
        }
        finally
        {
            // Commits the final stored offsets and leaves the group cleanly
            // — reached on graceful cancellation, and NOT reached (by
            // design) when the handler above throws: the caller's own retry
            // loop re-enters ConsumeAsync, which re-subscribes and resumes
            // from the last COMMITTED offset (design.md §3.3).
            consumer.Close();
        }
    }

    private static ConsumerConfig BuildConsumerConfig(OrdersSagaOptions options) => new()
    {
        BootstrapServers = options.Kafka.BootstrapServers,
        GroupId = "orders.saga", // identical to ConsumerNames.ToToken(ConsumerName.OrdersSaga) — one value for both the broker-side and dedup-ledger identity of "the orchestrator".
        ClientId = "otc-orders-saga", // distinct from the outbox relay producer's "otc-orders".
        AutoOffsetReset = AutoOffsetReset.Earliest, // SO1 — a change from the client default (`largest`), not a restatement of it.
        EnableAutoCommit = true, // commits STORED offsets only.
        EnableAutoOffsetStore = false, // SO9 — the whole point; see the class remarks.
        EnablePartitionEof = false,
    };
}
