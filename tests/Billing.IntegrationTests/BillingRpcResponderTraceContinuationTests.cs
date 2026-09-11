using System.Diagnostics;
using NATS.Client.Core;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Infrastructure.Observability;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// D1 (review round 1) — <c>R56</c>/<c>R57</c>/<c>OR4</c>, design.md §5.2,
/// §5.5, ledger L24. <c>TraceContextPropagationTests</c> (Orders) proves
/// the NATS continuation mechanism with a STAND-IN responder, and its own
/// comment says so. This drives it through the REAL, production
/// <see cref="BillingRpcResponder"/> class itself, over a real NATS socket —
/// never merely "a header is present" (design.md §5.5): every assertion
/// here extracts the header back and compares the REAL trace id, and
/// requires a DIFFERENT span id.
/// </summary>
[Collection(BillingCollection.Name)]
public sealed class BillingRpcResponderTraceContinuationTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    private static TracerProvider BuildRecordingProvider(RecordingActivityExporter exporter) =>
        Sdk.CreateTracerProviderBuilder()
            .AddSource(OtcActivity.SourceName)
            .AddProcessor(new SimpleActivityExportProcessor(exporter))
            .Build();

    /// <summary><c>credit.list</c> needs no correlation headers and no seeded state, isolating the responder's own extraction call.</summary>
    [Fact]
    public async Task D1_CreditListContinuesTheInboundTraceThroughTheRealResponder_WithAFreshSpanIdUnderTheSameTraceId()
    {
        using var exporter = new RecordingActivityExporter();
        using var provider = BuildRecordingProvider(exporter);

        var (host, _) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "trace-credit-list");
        using var _ = host;

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        Activity? callerActivity;
        using (callerActivity = OtcActivity.Source.StartActivity("test caller"))
        {
            var headers = new NatsHeaders { { "traceparent", callerActivity!.Id! } };
            var request = RpcJson.Serialize(new CreditListRequestPayload(1, 25));
            await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditList, request, headers);
        }

        provider.ForceFlush();
        var serverSpan = Assert.Single(exporter.Exported, a => a.DisplayName == $"rpc {CreditSubjects.CreditList}" && a.TraceId == callerActivity!.TraceId);
        Assert.Equal(callerActivity!.TraceId, serverSpan.TraceId);
        Assert.NotEqual(callerActivity.SpanId, serverSpan.SpanId);
        Assert.Equal(callerActivity.SpanId, serverSpan.ParentSpanId);

        await host.StopAsync();
    }

    /// <summary>
    /// design.md §5.3, ledger L22-adjacent — #7's own
    /// <c>outbox-relay-trace-linkage.integration.spec.ts</c> shape (review
    /// D1), carried through the REAL RPC responder: a <c>credit.hold</c>
    /// request carrying an inbound <c>traceparent</c> stamps
    /// <c>outbox.trace_parent</c> from the CONTINUED span (same trace id as
    /// the caller); one carrying none still stamps a real, non-null
    /// <c>trace_parent</c> from the responder's OWN fresh-root span — never
    /// a fabricated continuation of a caller that never sent one.
    /// </summary>
    [Fact]
    public async Task D1_CreditHoldStampsOutboxTraceParentFromTheContinuedSpan_WithAnInboundHeader_AndFromItsOwnFreshRootWithout()
    {
        using var exporter = new RecordingActivityExporter();
        using var provider = BuildRecordingProvider(exporter);

        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "trace-credit-hold");
        using var _ = host;

        await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, "CR-TRACE-01", "CarrefourEs", "IBERFOODS", 500_000);

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
            var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-900003", "CarrefourEs", "IBERFOODS", new CreditMoney(1_000, "EUR")));
            await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, headers);
        }

        var withHeaderRows = await BillingHostFixture.WaitForAsync(
            () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationIdWith.Value, "credit.approved.v1"),
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
        var requestWithout = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-900004", "CarrefourEs", "IBERFOODS", new CreditMoney(1_000, "EUR")));
        await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, requestWithout, headersWithout);

        var withoutHeaderRows = await BillingHostFixture.WaitForAsync(
            () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationIdWithout.Value, "credit.approved.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10));
        var withoutHeaderRow = Assert.Single(withoutHeaderRows);

        Assert.NotNull(withoutHeaderRow.TraceParent);
        var withoutHeaderContext = TraceContext.ContextFromTraceParent(withoutHeaderRow.TraceParent);
        Assert.NotNull(withoutHeaderContext);
        Assert.NotEqual(callerActivity.TraceId, withoutHeaderContext!.Value.TraceId);

        await host.StopAsync();
    }
}
