using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Observability;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// OR4/R57, design.md §5.2-§5.5, ledger L21-L24 — trace propagation over
/// REAL transports: a real NATS socket, a real Kafka broker, a real MS-SQL
/// database. Every continuation assertion here extracts the header back and
/// compares the REAL trace id — never merely "a header is present" (§5.5).
/// Spans are asserted against an in-memory exporter, never a live
/// collector, per design.md §12.
/// </summary>
[Collection(SagaCollection.Name)]
public sealed class TraceContextPropagationTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private static TracerProvider BuildRecordingProvider(RecordingActivityExporter exporter) =>
        Sdk.CreateTracerProviderBuilder()
            .AddSource(OtcActivity.SourceName)
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build();

    /// <summary>
    /// R57_OR4_NatsRpc — over a REAL socket: the caller
    /// (<see cref="NatsStockAvailabilityChecker"/>, the real production
    /// class) injects the active trace into a fresh <see cref="NatsHeaders"/>
    /// per call; the "server" side extracts it and starts a child span —
    /// the SAME trace id, a FRESH span id, whose parent is the injected
    /// span. Never merely "a header is present" (ledger L24).
    /// </summary>
    [Fact]
    public async Task R57_OR4_NatsRpc_InjectsTheActiveTraceIdOverARealSocket_AndTheResponderContinuesTheSameTraceWithAFreshSpanId()
    {
        using var exporter = new RecordingActivityExporter();
        using var provider = BuildRecordingProvider(exporter);

        var serverObservedContext = new TaskCompletionSource<ActivityContext?>();

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        await using var subscription = await StartRawResponderAsync(connection, RpcSubjects.StockCheck, headers =>
        {
            // The SAME mechanism the real responders use (TraceContext.ExtractNats
            // + a child Activity started under the restored context) —
            // proven here at the wire level since the production responder
            // classes' extraction methods are `private`/service-internal.
            var extractedContext = TraceContext.ExtractNats(headers);
            serverObservedContext.TrySetResult(extractedContext);

            using var serverActivity = extractedContext is { } parent
                ? OtcActivity.Source.StartActivity("rpc fulfillment.stock.check", ActivityKind.Server, parentContext: parent)
                : OtcActivity.Source.StartActivity("rpc fulfillment.stock.check", ActivityKind.Server);

            return """{"available":true,"lines":[]}""";
        });

        var checker = new NatsStockAvailabilityChecker(connection, Options.Create(new NatsOptions()));

        // A real, PACED readiness probe (CLAUDE.md: never a bare sleep,
        // never an unpaced attempt-counted loop) — the subscription above
        // is only live once the broker has processed it, and a request
        // sent before that lands as NatsNoRespondersException/Timeout, the
        // exact "subscribe-side race" SagaIntegrationTestSupport's own
        // remarks name for OrdersCreateResponder's three subjects.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (true)
        {
            try
            {
                await checker.CheckAsync("ACME", [new StockAvailabilityLine("P1", new Quantity(1))], CancellationToken.None);
                break;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }
        }

        // The warm-up call above may itself have been observed by the
        // responder — reset the capture before the call this test actually
        // asserts on.
        serverObservedContext = new TaskCompletionSource<ActivityContext?>();

        Activity? callerActivity;
        using (callerActivity = OtcActivity.Source.StartActivity("test caller"))
        {
            await checker.CheckAsync("ACME", [new StockAvailabilityLine("P1", new Quantity(1))], CancellationToken.None);
        }

        var extracted = await serverObservedContext.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(extracted);
        Assert.Equal(callerActivity!.TraceId, extracted!.Value.TraceId);
        Assert.Equal(callerActivity.SpanId, extracted.Value.SpanId);

        provider.ForceFlush();
        var serverSpan = Assert.Single(exporter.Exported, a => a.DisplayName == "rpc fulfillment.stock.check" && a.TraceId == callerActivity.TraceId);
        Assert.Equal(callerActivity.TraceId, serverSpan.TraceId);
        Assert.NotEqual(callerActivity.SpanId, serverSpan.SpanId);
        Assert.Equal(callerActivity.SpanId, serverSpan.ParentSpanId);
    }

    /// <summary>R57_OR4_KafkaFacts — a domain event written under an active span carries that trace into <c>outbox.trace_parent</c>, and the relayed message's headers extract to the SAME trace (a fresh span id, minted by the relay's own child "outbox.publish" span).</summary>
    [Fact]
    public async Task R57_OR4_KafkaFacts_ADomainEventWrittenUnderAnActiveSpanCarriesThatTraceIntoOutboxTraceParent_AndTheRelayedMessagesHeadersExtractToTheSameTrace()
    {
        using var exporter = new RecordingActivityExporter();
        using var provider = BuildRecordingProvider(exporter);

        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_trace_kafka_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var order = OrderPersistenceTestSupport.Place(new OrderNumber(101), clock.UtcNow, UniqueId.New());

        Activity? writeActivity;
        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock, new OrderFactPayloadMapper()));
            var unitOfWork = new EfCoreUnitOfWork(db);

            using (writeActivity = OtcActivity.Source.StartActivity("test writer"))
            {
                await unitOfWork.ExecuteAsync(async ct => { await repository.AddAsync(order, null, ct); await repository.SaveChangesAsync(ct); }, CancellationToken.None);
            }
        }

        // EfCoreUnitOfWork itself starts a CHILD "writemodel.transaction"
        // span around the work (ledger L23) — so the outbox row's own
        // trace_parent is THAT child's id, not the test's own root
        // activity's id. Same TRACE, by construction a DIFFERENT span.
        provider.ForceFlush();
        var writemodelSpan = Assert.Single(exporter.Exported, a => a.DisplayName == "writemodel.transaction");
        Assert.Equal(writeActivity!.TraceId, writemodelSpan.TraceId);
        Assert.Equal(writeActivity.SpanId, writemodelSpan.ParentSpanId);

        await using (var assertDb = mssql.CreateDbContext(connectionString))
        {
            var row = await assertDb.OutboxMessages.SingleAsync();
            Assert.NotNull(row.TraceParent);
            Assert.Equal(writemodelSpan.Id, row.TraceParent);
        }

        using (var publisher = new KafkaFactPublisher(new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build()))
        {
            await using var db = mssql.CreateDbContext(connectionString);
            var relay = new OutboxRelay(db, publisher, clock, Options.Create(new OutboxRelayOptions { BatchSize = 10 }), new FakeDlqDepthGauge(), NullLogger<OutboxRelay>.Instance);
            var result = await relay.RunOnceAsync(CancellationToken.None);
            Assert.Equal(1, result.Published);
        }

        provider.ForceFlush();
        var publishSpan = Assert.Single(exporter.Exported, a => a.DisplayName == "outbox.publish");
        Assert.Equal(writeActivity!.TraceId, publishSpan.TraceId);
        Assert.NotEqual(writemodelSpan.SpanId, publishSpan.SpanId);
        Assert.Equal(writemodelSpan.SpanId, publishSpan.ParentSpanId);

        // And the CONSUMED message's own headers extract to the exact same trace id.
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"trace-kafka-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(OrdersFactTopic.Name);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        ConsumeResult<string, byte[]>? consumed = null;
        while (DateTime.UtcNow < deadline)
        {
            var candidate = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (candidate is null)
            {
                continue;
            }

            if (candidate.Message.Key == order.Id.Value.ToString())
            {
                consumed = candidate;
                break;
            }
        }

        Assert.NotNull(consumed);
        var traceParentHeader = consumed!.Message.Headers.FirstOrDefault(h => h.Key == "traceparent");
        Assert.NotNull(traceParentHeader);
        var consumedTraceParent = System.Text.Encoding.UTF8.GetString(traceParentHeader!.GetValueBytes());
        var consumedContext = TraceContext.ContextFromTraceParent(consumedTraceParent);
        Assert.NotNull(consumedContext);
        Assert.Equal(writeActivity.TraceId, consumedContext!.Value.TraceId);
        Assert.Equal(publishSpan.SpanId, consumedContext.Value.SpanId);
    }

    /// <summary>R57_OR4 — a write with NO active span produces NO traceparent header at all, at every hop: stored trace_parent is null, and the published Kafka message carries no traceparent header (never a fabricated root).</summary>
    [Fact]
    public async Task R57_OR4_AWriteWithNoActiveSpanProducesNoTraceparentHeaderAtAll()
    {
        Assert.Null(Activity.Current);

        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_trace_nospan_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var order = OrderPersistenceTestSupport.Place(new OrderNumber(102), clock.UtcNow, UniqueId.New());

        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock, new OrderFactPayloadMapper()));
            var unitOfWork = new EfCoreUnitOfWork(db);
            await unitOfWork.ExecuteAsync(async ct => { await repository.AddAsync(order, null, ct); await repository.SaveChangesAsync(ct); }, CancellationToken.None);
        }

        await using (var assertDb = mssql.CreateDbContext(connectionString))
        {
            var row = await assertDb.OutboxMessages.SingleAsync();
            Assert.Null(row.TraceParent);
        }

        using (var publisher = new KafkaFactPublisher(new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build()))
        {
            await using var db = mssql.CreateDbContext(connectionString);
            var relay = new OutboxRelay(db, publisher, clock, Options.Create(new OutboxRelayOptions { BatchSize = 10 }), new FakeDlqDepthGauge(), NullLogger<OutboxRelay>.Instance);
            var result = await relay.RunOnceAsync(CancellationToken.None);
            Assert.Equal(1, result.Published);
        }

        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"trace-nospan-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(OrdersFactTopic.Name);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        ConsumeResult<string, byte[]>? consumed = null;
        while (DateTime.UtcNow < deadline)
        {
            var candidate = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (candidate is null)
            {
                continue;
            }

            if (candidate.Message.Key == order.Id.Value.ToString())
            {
                consumed = candidate;
                break;
            }
        }

        Assert.NotNull(consumed);
        Assert.DoesNotContain(consumed!.Message.Headers, h => h.Key == "traceparent");
    }

    /// <summary>OR4/design.md §5.4, ledger L23 — for one placement, the exported span list contains the writemodel.transaction span BETWEEN the RPC span and the publish span, matched by parent-span id — not merely that three spans exist.</summary>
    [Fact]
    public async Task R56_OR4_TheWriteDatabaseHop_SitsBetweenTheRpcSpanAndThePublishSpan_ByParentSpanId()
    {
        using var exporter = new RecordingActivityExporter();
        using var provider = BuildRecordingProvider(exporter);

        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_trace_txn_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        var clock = new FakeClock(FakeClock.UtcNowToTheMillisecond());
        var order = OrderPersistenceTestSupport.Place(new OrderNumber(103), clock.UtcNow, UniqueId.New());

        Activity? rpcSpan;
        Activity? transactionSpan;
        await using (var db = mssql.CreateDbContext(connectionString))
        {
            var repository = new EfCoreOrderRepository(db, new OutboxWriter(clock, new OrderFactPayloadMapper()));
            var unitOfWork = new EfCoreUnitOfWork(db);

            using (rpcSpan = OtcActivity.Source.StartActivity("rpc orders.create", ActivityKind.Server))
            {
                using (transactionSpan = OtcActivity.Source.StartActivity("probe"))
                {
                    // The transaction span itself is started INSIDE ExecuteAsync;
                    // this outer "probe" activity exists only so
                    // Activity.Current is non-null when ExecuteAsync begins,
                    // matching production (the responder's own span is
                    // already active by the time the handler calls
                    // unitOfWork.ExecuteAsync).
                }

                await unitOfWork.ExecuteAsync(async ct => { await repository.AddAsync(order, null, ct); await repository.SaveChangesAsync(ct); }, CancellationToken.None);
            }
        }

        var topic = $"otc.orders.trace.test.{Guid.NewGuid():N}";
        using (var publisher = new KafkaFactPublisher(new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build()))
        {
            await using var db = mssql.CreateDbContext(connectionString);
            var relay = new OutboxRelay(db, publisher, clock, Options.Create(new OutboxRelayOptions { BatchSize = 10 }), new FakeDlqDepthGauge(), NullLogger<OutboxRelay>.Instance);
            await relay.RunOnceAsync(CancellationToken.None);
        }

        provider.ForceFlush();
        var writemodelSpan = Assert.Single(exporter.Exported, a => a.DisplayName == "writemodel.transaction");
        var publishSpan = Assert.Single(exporter.Exported, a => a.DisplayName == "outbox.publish");

        Assert.Equal(rpcSpan!.TraceId, writemodelSpan.TraceId);
        Assert.Equal(rpcSpan.SpanId, writemodelSpan.ParentSpanId);
        Assert.Equal(writemodelSpan.SpanId, publishSpan.ParentSpanId);
        Assert.Equal(rpcSpan.TraceId, publishSpan.TraceId);
    }

    private static async Task<IAsyncDisposable> StartRawResponderAsync(NatsConnection connection, string subject, Func<NatsHeaders?, string> handleAndReply)
    {
        var cts = new CancellationTokenSource();
        var loop = Task.Run(async () =>
        {
            await foreach (var message in connection.SubscribeAsync<byte[]>(subject, cancellationToken: cts.Token))
            {
                var replyJson = handleAndReply(message.Headers);
                await message.ReplyAsync(System.Text.Encoding.UTF8.GetBytes(replyJson), cancellationToken: cts.Token);
            }
        }, cts.Token);

        return new StopSubscription(cts, loop);
    }

    private sealed class StopSubscription(CancellationTokenSource cts, Task loop) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await cts.CancelAsync();
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
            }

            cts.Dispose();
        }
    }
}
