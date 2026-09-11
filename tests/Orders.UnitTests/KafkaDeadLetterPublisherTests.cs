using System.Diagnostics;
using System.Text;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging.DeadLetter;
using OrderToCash.Orders.Infrastructure.Observability;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// Review round-1 addendum — design.md §11 rows 6-7, OR4/R57's DLQ-side
/// claim. Ported from #7's <c>kafka-dlq-publisher.spec.ts</c> (2 of 2
/// cases; #7's file carries no third case) — read at
/// <c>order-to-cash-nestjs/apps/orders/src/infrastructure/messaging/kafka-dlq-publisher.spec.ts</c>.
/// Drives the REAL <see cref="KafkaDeadLetterPublisher"/> class through its
/// <see cref="Confluent.Kafka.IProducer{TKey,TValue}"/> test seam — no real
/// broker — so the class's own inline <c>Activity.Current?.Id</c> branch is
/// executed directly, never re-implemented. This is the branch
/// `NotificationDeadLetterTests`/`ProjectorDeadLetterTests`/`SagaDeadLetterTests`
/// only assert PRESENCE of (`ContainsKey("traceparent")`); this class proves
/// the header carries the REAL active trace id, extractable back to it, and
/// that no header is fabricated when nothing is active.
/// </summary>
public sealed class KafkaDeadLetterPublisherTests
{
    [Fact]
    public async Task PublishAsync_WithAnActiveSpan_PublishesATraceparentHeaderThatExtractsToTheSameRealTraceId()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OtcActivity.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var producer = new RecordingKafkaProducer();
        using var publisher = new KafkaDeadLetterPublisher(producer);

        Activity? activity;
        using (activity = OtcActivity.Source.StartActivity("test consume span"))
        {
            Assert.NotNull(activity);
            await publisher.PublishAsync(BuildPublication(), CancellationToken.None);
        }

        var produced = Assert.Single(producer.Produced);
        Assert.Equal("otc.orders.facts.v1.dlq", produced.Topic);

        var header = Assert.Single(produced.Message.Headers, h => h.Key == "traceparent");
        var traceparent = Encoding.UTF8.GetString(header.GetValueBytes());

        Assert.Equal(activity!.Id, traceparent);
        Assert.True(ActivityContext.TryParse(traceparent, null, out var extracted), $"'{traceparent}' did not parse as a W3C traceparent.");
        Assert.Equal(activity.TraceId, extracted.TraceId);
    }

    [Fact]
    public async Task PublishAsync_WithNoActiveSpan_PublishesNoTraceparentHeaderAtAll()
    {
        Assert.Null(Activity.Current);

        var producer = new RecordingKafkaProducer();
        using var publisher = new KafkaDeadLetterPublisher(producer);

        await publisher.PublishAsync(BuildPublication(), CancellationToken.None);

        var produced = Assert.Single(producer.Produced);
        Assert.DoesNotContain(produced.Message.Headers, h => h.Key == "traceparent");
    }

    /// <summary>
    /// Review round 2, D6 — CLAUDE.md: "for fields the test does not
    /// control — clocks — inject the source or bracket the value, or the
    /// field is unguarded however many probes you run." The two cases above
    /// only prove <c>traceparent</c>'s VALUE; every OTHER
    /// <c>DeadLetterHeaders</c> field was asserted only by the
    /// <c>.dlq</c>-consuming integration test's
    /// <c>DateTimeOffset.TryParse</c> — a check a VALID but WRONG timestamp
    /// satisfies. This asserts every non-trace header's VALUE against the
    /// publication's own known inputs. Armed by swapping
    /// <c>x-first-failed-at</c>'s rendered value for
    /// <c>publication.FailedAt</c> — see the round-2 fix record.
    /// </summary>
    [Fact]
    public async Task PublishAsync_RendersEveryDeadLetterHeaderValue_NotJustWhetherItParses()
    {
        var producer = new RecordingKafkaProducer();
        using var publisher = new KafkaDeadLetterPublisher(producer);

        await publisher.PublishAsync(BuildPublication(), CancellationToken.None);

        var produced = Assert.Single(producer.Produced);
        var headers = produced.Message.Headers.ToDictionary(h => h.Key, h => Encoding.UTF8.GetString(h.GetValueBytes()), StringComparer.Ordinal);

        Assert.Equal(ConsumerNames.ToToken(ConsumerName.OrdersSaga), headers["x-failed-consumer"]);
        Assert.Equal("3", headers["x-attempts"]);
        Assert.Equal("boom", headers["x-error"]);
        Assert.Equal("otc.orders.facts.v1", headers["x-original-topic"]);
        Assert.Equal("order.placed.v1", headers["x-event-type"]);
        Assert.Equal("2026-08-26T10:00:00.0000000+00:00", headers["x-first-failed-at"]);
        Assert.Equal("2026-08-26T10:00:01.0000000+00:00", headers["x-failed-at"]);
    }

    private static DeadLetterPublication BuildPublication() =>
        new(
            SourceTopic: "otc.orders.facts.v1",
            OriginalEnvelope: new ReadOnlyMemory<byte>("poison"u8.ToArray()),
            FailedConsumer: ConsumerName.OrdersSaga,
            Attempts: 3,
            Error: "boom",
            EventType: "order.placed.v1",
            FirstFailedAt: new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.Zero),
            FailedAt: new DateTimeOffset(2026, 8, 26, 10, 0, 1, TimeSpan.Zero));
}
