using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Observability;
using OrderToCash.Orders.Presentation;
using OrderToCash.Orders.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// D1 (review round 1) — <c>R56</c>/<c>R57</c>/<c>OR4</c>, design.md §5.2,
/// §5.5, ledger L24. This file's own <see cref="TraceContextPropagationTests"/>
/// sibling proves the NATS continuation mechanism with a STAND-IN
/// responder, and its own comment says so — the production responder
/// classes' extraction is "private/service-internal". This drives it
/// through the REAL, production <see cref="OrdersCreateResponder"/> class
/// itself (<c>catalog.reference.list</c> — no persistence beyond seeded
/// reference data), over a real NATS socket. Never merely "a header is
/// present" (design.md §5.5): the assertion extracts the header back and
/// compares the REAL trace id, and requires a DIFFERENT span id.
/// </summary>
[Collection(NatsCollection.Name)]
public sealed class OrdersCreateResponderTraceContinuationTests(NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    private static TracerProvider BuildRecordingProvider(RecordingActivityExporter exporter) =>
        Sdk.CreateTracerProviderBuilder()
            .AddSource(OtcActivity.SourceName)
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build();

    [Fact]
    public async Task D1_CatalogReferenceListContinuesTheInboundTraceThroughTheRealResponder_WithAFreshSpanIdUnderTheSameTraceId()
    {
        using var exporter = new RecordingActivityExporter();
        using var provider = BuildRecordingProvider(exporter);

        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_catalog_trace_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        using var host = BuildHost(connectionString);
        await host.StartAsync();
        try
        {
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });

            var probe = new CatalogReferenceListRequestPayload(Kinds: ["currencies"], IncludeDisabled: null);
            await SagaIntegrationTestSupport.WaitUntilReachableAsync(caller, RpcSubjects.CatalogReferenceList, RpcJson.Serialize(probe), CancellationToken.None);

            Activity? callerActivity;
            using (callerActivity = OtcActivity.Source.StartActivity("test caller"))
            {
                var headers = new NatsHeaders { { "traceparent", callerActivity!.Id! } };
                var request = new CatalogReferenceListRequestPayload(Kinds: ["currencies"], IncludeDisabled: null);
                await caller.RequestAsync<byte[], byte[]>(
                    RpcSubjects.CatalogReferenceList,
                    RpcJson.Serialize(request),
                    headers: headers,
                    replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                    cancellationToken: CancellationToken.None);
            }

            provider.ForceFlush();
            var serverSpan = Assert.Single(exporter.Exported, a => a.DisplayName == $"rpc {RpcSubjects.CatalogReferenceList}" && a.TraceId == callerActivity!.TraceId);
            Assert.Equal(callerActivity!.TraceId, serverSpan.TraceId);
            Assert.NotEqual(callerActivity.SpanId, serverSpan.SpanId);
            Assert.Equal(callerActivity.SpanId, serverSpan.ParentSpanId);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private IHost BuildHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddOrdersOutbox(options =>
        {
            options.ConnectionString = connectionString;
            options.Relay.Enabled = false;
            // Mechanism-2 classification: this host's StopAsync/Dispose
            // sites below do NOT need the group-clearance wait —
            // "127.0.0.1:1" is deliberately unreachable, so SagaFactsConsumer
            // can never actually join a real "orders.saga" group on ANY
            // broker; mechanism 2 needs a real shared broker to cross a
            // test boundary, which is structurally impossible here.
            options.Kafka.BootstrapServers = "127.0.0.1:1";
        });
        builder.Services.AddOrdersAcceptance(options => options.Nats.Url = nats.Url);
        builder.Services.AddDispatcher(typeof(PlaceOrderCommand).Assembly);

        return builder.Build();
    }
}
