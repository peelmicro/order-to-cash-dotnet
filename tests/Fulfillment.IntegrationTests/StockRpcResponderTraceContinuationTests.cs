using System.Diagnostics;
using NATS.Client.Core;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Infrastructure.Observability;
using OrderToCash.Fulfillment.Presentation.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>
/// D1 (review round 1) — <c>R56</c>/<c>R57</c>/<c>OR4</c>, design.md §5.2,
/// §5.5, ledger L24. <c>TraceContextPropagationTests</c> (Orders) proves
/// the NATS continuation mechanism with a STAND-IN responder, and its own
/// comment says so. This drives it through the REAL, production
/// <see cref="StockRpcResponder"/> class itself, over a real NATS socket —
/// never merely "a header is present" (design.md §5.5): every assertion
/// here extracts the header back and compares the REAL trace id, and
/// requires a DIFFERENT span id.
/// </summary>
[Collection(FulfillmentCollection.Name)]
public sealed class StockRpcResponderTraceContinuationTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    private static TracerProvider BuildRecordingProvider(RecordingActivityExporter exporter) =>
        Sdk.CreateTracerProviderBuilder()
            .AddSource(OtcActivity.SourceName)
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build();

    /// <summary>
    /// The cheapest real hop: <c>stock.check</c> needs no correlation
    /// headers and no persisted state beyond an empty stock table, so this
    /// isolates the ONE thing D1 is about — the responder's own extraction
    /// call (<c>StockRpcResponder.cs</c>'s <c>ProcessRequestAsync</c>).
    /// </summary>
    [Fact]
    public async Task D1_StockCheckContinuesTheInboundTraceThroughTheRealResponder_WithAFreshSpanIdUnderTheSameTraceId()
    {
        using var exporter = new RecordingActivityExporter();
        using var provider = BuildRecordingProvider(exporter);

        var (host, _) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "trace-check");
        using var _ = host;

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        Activity? callerActivity;
        using (callerActivity = OtcActivity.Source.StartActivity("test caller"))
        {
            var headers = new NatsHeaders { { "traceparent", callerActivity!.Id! } };
            var request = RpcJson.Serialize(new StockCheckRequestPayload("ACME", [new StockCheckRequestLine("P1", 1)]));
            await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockCheck, request, headers);
        }

        provider.ForceFlush();
        var serverSpan = Assert.Single(exporter.Exported, a => a.DisplayName == $"rpc {StockSubjects.StockCheck}" && a.TraceId == callerActivity!.TraceId);
        Assert.Equal(callerActivity!.TraceId, serverSpan.TraceId);
        Assert.NotEqual(callerActivity.SpanId, serverSpan.SpanId);
        Assert.Equal(callerActivity.SpanId, serverSpan.ParentSpanId);

        await host.StopAsync();
    }

    /// <summary>
    /// design.md §5.3, ledger L22-adjacent — #7's own
    /// <c>outbox-relay-trace-linkage.integration.spec.ts</c> shape (review
    /// D1), carried through the REAL RPC responder rather than a direct
    /// repository call: a <c>stock.reserve</c> request carrying an inbound
    /// <c>traceparent</c> stamps <c>outbox.trace_parent</c> from the
    /// CONTINUED span (same trace id as the caller); one carrying none
    /// still stamps a real, non-null <c>trace_parent</c> — the responder
    /// ALWAYS wraps request handling in its own span (a fresh root when
    /// there is nothing to continue, §5.5) — but that root's trace id is
    /// its OWN, never fabricated as a continuation of a caller that never
    /// sent one.
    /// </summary>
    [Fact]
    public async Task D1_StockReserveStampsOutboxTraceParentFromTheContinuedSpan_WithAnInboundHeader_AndFromItsOwnFreshRootWithout()
    {
        using var exporter = new RecordingActivityExporter();
        using var provider = BuildRecordingProvider(exporter);

        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "trace-reserve");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 10);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        // WITH an inbound traceparent.
        var correlationIdWith = UniqueId.New();
        Activity? callerActivity;
        using (callerActivity = OtcActivity.Source.StartActivity("test caller"))
        {
            var headers = new NatsHeaders
            {
                { "x-correlation-id", correlationIdWith.Value.ToString() },
                { "x-request-id", UniqueId.New().Value.ToString() },
                { "traceparent", callerActivity!.Id! },
            };
            // "ORD-900001", not a hand-picked slug — StockRequestValidator
            // requires orderReference to match ^ORD-[0-9]{6,}$.
            var request = RpcJson.Serialize(new StockReserveRequestPayload("ORD-900001", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 1)]));
            await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReserve, request, headers);
        }

        var withHeaderRows = await FulfillmentHostFixture.WaitForAsync(
            () => FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationIdWith.Value, "stock.reserved.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10));
        var withHeaderRow = Assert.Single(withHeaderRows);
        Assert.NotNull(withHeaderRow.TraceParent);
        var withHeaderContext = TraceContext.ContextFromTraceParent(withHeaderRow.TraceParent);
        Assert.NotNull(withHeaderContext);
        Assert.Equal(callerActivity!.TraceId, withHeaderContext!.Value.TraceId);

        // WITHOUT any traceparent header at all.
        var correlationIdWithout = UniqueId.New();
        var headersWithout = new NatsHeaders
        {
            { "x-correlation-id", correlationIdWithout.Value.ToString() },
            { "x-request-id", UniqueId.New().Value.ToString() },
        };
        var requestWithout = RpcJson.Serialize(new StockReserveRequestPayload("ORD-900002", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 1)]));
        await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReserve, requestWithout, headersWithout);

        var withoutHeaderRows = await FulfillmentHostFixture.WaitForAsync(
            () => FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationIdWithout.Value, "stock.reserved.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10));
        var withoutHeaderRow = Assert.Single(withoutHeaderRows);

        // Still populated — the responder's OWN root span is active
        // throughout the handler — but a DIFFERENT trace than the "with"
        // call's, proving it is a genuine, independent root rather than a
        // leaked/shared one.
        Assert.NotNull(withoutHeaderRow.TraceParent);
        var withoutHeaderContext = TraceContext.ContextFromTraceParent(withoutHeaderRow.TraceParent);
        Assert.NotNull(withoutHeaderContext);
        Assert.NotEqual(callerActivity.TraceId, withoutHeaderContext!.Value.TraceId);

        await host.StopAsync();
    }
}
