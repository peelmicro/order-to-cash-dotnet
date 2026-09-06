using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OrderToCash.Notifications.Application.Ports;

namespace OrderToCash.Notifications.Infrastructure.Messaging.Consumers;

/// <summary>
/// The ONE type in this service touching <c>Confluent.Kafka</c>'s consumer
/// API (<see cref="OrderToCash.Architecture.Tests.FactConsumerConfinementTests"/>
/// enforces this at namespace granularity, service-agnostic). Implements
/// <see cref="IFactStreamSubscriber"/>'s offset-commit-after-handler
/// contract — copied from Orders' own <c>KafkaFactStreamSubscriber</c>
/// (order_saga_orchestrator design.md §3.1-§3.3), with two deliberate
/// per-service differences: <see cref="BuildConsumerConfig"/>'s
/// <c>GroupId</c>/<c>ClientId</c>, and <see cref="ConsumerConfig.AutoOffsetReset"/>
/// — see that method's own remarks for why this service's default is the
/// OPPOSITE of Orders'.
/// </summary>
public sealed class KafkaFactStreamSubscriber(IOptions<NotificationsKafkaOptions> options) : IFactStreamSubscriber
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
        // blocking it before the first `await` would stall host startup —
        // so this yields first, exactly as Orders' own subscriber does.
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
                    consumeResult.Message.Value);

                // The offset is stored ONLY after the handler has run to
                // completion. A throwing handler propagates unchanged —
                // nothing is stored, and the exception surfaces to
                // NotificationFactsConsumer's own retry loop.
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
    /// <b>Why <see cref="AutoOffsetReset.Latest"/>, the OPPOSITE of Orders'
    /// <see cref="AutoOffsetReset.Earliest"/>, and not an oversight.</b>
    /// Orders' saga orchestrator MUST replay the full retained history on a
    /// fresh consumer group: it owns the order state machine, and an order
    /// placed before the orchestrator's own group ever subscribed still has
    /// to reach a terminal state. Notifications owns no aggregate and no
    /// state machine at all (domain-model.md §6) — a fact this service's own
    /// consumer group has genuinely never seen is simply a fact that gets no
    /// email, an omission rather than an inconsistency, and this durable
    /// ledger (<c>processed_events</c>) only ever prevents a DUPLICATE send
    /// for a fact already recorded; it cannot un-see a fact this group never
    /// received. Pointing a fresh consumer group at these topics with
    /// <c>Earliest</c> would therefore mail every historical order once,
    /// which is exactly the incident that made this service's ledger durable
    /// in the first place (see <c>NotificationsDbContext</c>'s own header) —
    /// the ledger and this setting are independent defences against two
    /// different failure modes, and neither substitutes for the other.
    /// </summary>
    private static ConsumerConfig BuildConsumerConfig(NotificationsKafkaOptions options) => new()
    {
        BootstrapServers = options.BootstrapServers,
        GroupId = "notifications", // identical to ConsumerNames.ToToken(ConsumerName.Notifications) — one value for both the broker-side and dedup-ledger identity of this service.
        ClientId = "otc-notifications",
        AutoOffsetReset = AutoOffsetReset.Latest,
        EnableAutoCommit = true, // commits STORED offsets only.
        EnableAutoOffsetStore = false, // the whole point — see the class remarks.
        EnablePartitionEof = false,
    };
}
