using Microsoft.EntityFrameworkCore;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Orders.Infrastructure.Messaging.Consumers;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// design.md §11, ported cases 72-73 — closing the review round 3 gap:
/// <c>MetricsExposureTests</c> proves <c>otc_outbox_lag_ms</c> and
/// <c>otc_dlq_depth</c> against real infrastructure, but neither
/// <c>otc_fact_processing_latency_ms</c> (case 72 — "from a real
/// Kafka-delivered fact") nor <c>otc_saga_completion_ms</c> (case 73 —
/// "measured between two real timestamps") had an INTEGRATION-level guard;
/// both existing guards for those two instruments
/// (<c>FactRetryDispatcherTests</c>, <c>SagaFactHandlerTests</c>) drive the
/// real production classes but over a <c>FakeClock</c> and a hand-built
/// <c>FactStreamMessage</c>, never a real broker delivery or a real
/// wall-clock <c>OrderDate</c>. These two cases close that gap.
/// </summary>
[Collection(SagaCollection.Name)]
public sealed class RealInfraMetricsProvenanceTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Ported case 72 — a healthy, well-formed fact for an order that does
    /// not exist (the SO8 "unknown order" ignore, the same shape
    /// <c>SagaDeadLetterTests</c>' own second half already uses) is
    /// delivered by a REAL Kafka broker, consumed by the REAL
    /// <c>SagaFactsConsumer</c>/<c>FactRetryDispatcher</c> pair, and records
    /// a REAL, wall-clock-timed measurement — never a <c>FakeClock</c>-driven
    /// one, which is all <c>FactRetryDispatcherTests</c> can prove.
    ///
    /// D8 (review round 3): the VALUE assertion below is a real upper bound
    /// — this test's own observed window, tighter than #7's fixed
    /// <c>toBeLessThan(30_000)</c> (metrics-exposure.integration.spec.ts:197)
    /// — never a substitute for the EXACT unit-level assertions.
    /// <c>FactRetryDispatcherTests</c>' <c>OtcFactProcessingLatencyMs_RecordedOnSuccess_TaggedByConsumer</c>
    /// and <c>…RecordedOnTheExhaustedRetryDlqPathToo</c> are what reject a
    /// wrong-but-plausible anchor (ticks-for-milliseconds, seconds-for-
    /// milliseconds); a real-infra bound this wide could never catch either.
    /// </summary>
    [Fact]
    public async Task OtcFactProcessingLatencyMs_RecordedFromARealKafkaDeliveredFact_TaggedByTheRealConsumer()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "metrics-latency");
        try
        {
            using var capture = MetricCapture.ForInstrument("otc_fact_processing_latency_ms");

            var correlationId = Guid.NewGuid();
            var beforePublish = DateTimeOffset.UtcNow;
            var payload = new StockReservedPayload("ORD-DOES-NOT-EXIST", OrderPersistenceTestSupport.CompanyCode, []);
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers,
                SagaFactTopics.FulfillmentFacts,
                "stock.reserved.v1",
                correlationId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                payload,
                CancellationToken.None);

            var ignoredCount = await SagaIntegrationTestSupport.WaitForSagaIgnoredFactCountAsync(
                connectionString, mssql, correlationId, eventType: "stock.reserved.v1", marker: "unknown_order", _wait);
            Assert.True(ignoredCount > 0, "The fact never reached the SO8 ignore path — the real dispatcher never ran.");

            // The DB row is written INSIDE process(), and Record() fires
            // immediately after process() returns successfully — a few
            // microseconds later, well inside a paced poll (design.md
            // §12's own "synchronise on terminal evidence, never a bare
            // sleep"). NOT Assert.Single: under a full ./quality.sh run
            // OTHER test classes in OTHER xUnit collections (e.g.
            // MsSqlCollection's IdempotentConsumerTests) build their OWN
            // real Orders saga host concurrently and record their OWN
            // consumer="OrdersSaga" measurements on the SAME process-wide
            // OtcMetrics.Meter — this capture sees those too. Filtering by
            // tag (rather than counting) still proves the claim: a real
            // Kafka-delivered fact reached the real dispatcher and recorded
            // a real, correctly-tagged measurement. The consumer-tag
            // corruption arm still fails this assertion because the
            // mutation is in the shared compiled DLL, so EVERY concurrent
            // caller's tag is corrupted too — none would match.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!capture.Measurements.Any(m => m.Tags.Any(t => t.Key == "consumer" && string.Equals(t.Value?.ToString(), "OrdersSaga", StringComparison.Ordinal))) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            var afterObserved = DateTimeOffset.UtcNow;

            // The real upper bound: this test's own observed window
            // (publish to observation), never a value the production code
            // computed for itself. A wrong-but-plausible anchor (e.g.
            // recording Ticks instead of milliseconds) would produce a
            // value many orders of magnitude larger than this bound and
            // fail here too — but the EXACT unit cases above are what
            // catch it deterministically; this bound only rejects gross
            // magnitude errors under real wall-clock noise.
            var upperBoundMs = (afterObserved - beforePublish).TotalMilliseconds;
            Assert.Contains(capture.Measurements, m =>
                m.Value >= 0 &&
                m.Value <= upperBoundMs &&
                string.Equals(m.Tags.SingleOrDefault(t => t.Key == "consumer").Value?.ToString(), "OrdersSaga", StringComparison.Ordinal));
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    /// <summary>
    /// Ported case 73 — places a REAL order (its <c>OrderDate</c> stamped by
    /// the host's own real <c>IClock</c>), then delivers a REAL
    /// <c>stock.rejected.v1</c> fact over Kafka, which the saga's own step
    /// table (<c>SagaStepTable.cs</c>) maps to a DIRECT cancel from
    /// <c>Placed</c> with no compensation steps — the shortest real path to
    /// a terminal status. The recorded <c>otc_saga_completion_ms</c> value
    /// is bounded against two independently-read REAL wall-clock
    /// timestamps this test itself takes, never a <c>FakeClock</c> value.
    ///
    /// A12 (review round 3): this bound alone is NOT tight enough to reject
    /// a wrong-but-plausible anchor — probe B1b measured
    /// <c>clock.UtcNow - fact.OccurredAt</c> substituted for
    /// <c>clock.UtcNow - order.OrderDate</c> and it stayed inside this
    /// bound twice, because the window (publish-to-observation, dominated
    /// by the status poll) is wide enough to swallow the difference
    /// between the two timestamps. The EXACT unit-level value guard is
    /// <c>SagaFactHandlerTests</c>' three <c>OtcSagaCompletionMs_*</c>
    /// recording cases, which reject that substitution deterministically
    /// (its fixture's fact timestamp differs from <c>OrderDate</c> by a
    /// fixed 5 minutes). This integration case instead proves REAL host-
    /// clock and persistence provenance — the closing fact carries an
    /// <c>OccurredAt</c> several minutes in the PAST relative to publish
    /// time, so a completion measured from the wrong anchor lands orders
    /// of magnitude outside this bound too, tightening it against that
    /// specific substitution without pretending to replace the unit cases.
    /// </summary>
    [Fact]
    public async Task OtcSagaCompletionMs_RecordedFromRealWallClockTimestamps_ADirectCancelViaStockRejected()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(mssql, kafka, nats, "metrics-completion");
        try
        {
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);

            var placed = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var orderId = placed.OrderId.Value;

            DateTimeOffset orderDate;
            await using (var db = mssql.CreateDbContext(connectionString))
            {
                orderDate = new DateTimeOffset(await db.Orders.Where(o => o.Id == orderId).Select(o => o.OrderDate).SingleAsync(), TimeSpan.Zero);
            }

            using var capture = MetricCapture.ForInstrument("otc_saga_completion_ms");

            var beforePublish = DateTimeOffset.UtcNow;
            // A12 (review round 3): OccurredAt is stamped 5 MINUTES in the
            // PAST, deliberately far outside this test's own (sub-second)
            // publish-to-observation window. A correct implementation
            // (clock.UtcNow - order.OrderDate) is unaffected — OrderDate is
            // the order's own real placement time, not this fact's
            // OccurredAt. The B1b substitution (clock.UtcNow -
            // fact.OccurredAt) would instead land ~5 minutes outside the
            // bound below, so this integration case now also rejects that
            // specific wrong-but-plausible anchor — on top of, never
            // instead of, SagaFactHandlerTests' exact unit-level guard.
            await StandInSagaResponders.PublishFactAsync(
                kafka.BootstrapServers,
                SagaFactTopics.FulfillmentFacts,
                "stock.rejected.v1",
                orderId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(-5),
                new StockRejectedPayload(placed.OrderReference.Value, OrderPersistenceTestSupport.CompanyCode, [], "out_of_stock"),
                CancellationToken.None);

            await SagaIntegrationTestSupport.WaitForOrderStatusAsync(connectionString, mssql, orderId, "cancelled", _wait);

            // NOT Assert.Single, for the same reason as the row-72 case
            // above: under a full ./quality.sh run, other SagaCollection-
            // disjoint test classes (different xUnit collections) may
            // complete or cancel their OWN, DIFFERENT orders concurrently,
            // each recording its OWN outcome="cancelled"/"completed"
            // measurement on the same process-wide meter. This test's OWN
            // measurement is the one — and, in practice, the only one —
            // whose value falls inside bounds computed from THIS order's
            // OWN real OrderDate and THIS test's own two independently-read
            // wall-clock readings; an unrelated concurrent order's value
            // would have to coincide with this order's specific elapsed
            // window by chance. A12 correction: the consumer-tag/value
            // corruption arms do NOT "collapse toward 0ms" — B1b measured
            // a plausible wrong anchor (fact.OccurredAt) landing INSIDE
            // this bound when OccurredAt was near "now". The far-past
            // OccurredAt above, not a claim about corruption's direction,
            // is what makes this bound reject that specific substitution.
            // The general-purpose EXACT value guard remains
            // SagaFactHandlerTests' three OtcSagaCompletionMs_* cases.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!capture.Measurements.Any(m => m.Tags.Any(t => t.Key == "outcome" && string.Equals(t.Value?.ToString(), "cancelled", StringComparison.Ordinal))) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            var afterObserved = DateTimeOffset.UtcNow;

            var cancelledMeasurements = capture.Measurements
                .Where(m => m.Tags.Any(t => t.Key == "outcome" && string.Equals(t.Value?.ToString(), "cancelled", StringComparison.Ordinal)))
                .ToList();
            Assert.NotEmpty(cancelledMeasurements);

            // clock.UtcNow inside SagaFactHandler is read AFTER the fact was
            // published (>= beforePublish - orderDate) and BEFORE this
            // assertion runs (<= afterObserved - orderDate) — real,
            // independently-taken bounds, never a value this test computed
            // FOR the production code.
            var lowerBoundMs = (beforePublish - orderDate).TotalMilliseconds;
            var upperBoundMs = (afterObserved - orderDate).TotalMilliseconds;
            Assert.True(
                cancelledMeasurements.Any(m => m.Value >= lowerBoundMs && m.Value <= upperBoundMs),
                $"No outcome=cancelled measurement fell within the real bound [{lowerBoundMs}ms, {upperBoundMs}ms] (beforePublish/afterObserved - orderDate) — observed values: {string.Join(", ", cancelledMeasurements.Select(m => m.Value))}. Not measured from the real OrderDate/wall clock.");
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }
}
