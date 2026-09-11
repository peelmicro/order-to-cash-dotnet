using System.Text.Json;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>
/// D2 (review round 1) — R58/OR7, design.md §6, ledger L25, captured from
/// a FULLY configured real host (<c>FulfillmentHost.CreateBuilder</c>'s
/// own <c>AddJsonConsole</c> + <c>IncludeScopes</c> +
/// <c>ActivityTrackingOptions</c> wiring), never asserted against the
/// logger abstraction. <see cref="Console.Out"/> is redirected BEFORE the
/// host is built (<c>AddJsonConsole</c>'s writer captures the
/// <see cref="TextWriter"/> reference once, at construction time).
/// </summary>
[Collection(FulfillmentCollection.Name)]
public sealed class LogCorrelationTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    /// <summary>
    /// design.md §6's scope-push table, RPC responder row —
    /// <c>x-correlation-id</c> is supplied, <c>x-request-id</c> is
    /// deliberately OMITTED so <c>RpcMetaExtractor.TryExtract</c> fails and
    /// <c>StockRpcResponder</c>'s catch logs a warning — with the
    /// correlation scope (pushed from <c>x-correlation-id</c> BEFORE the
    /// try block) already active on that very line.
    /// </summary>
    [Fact]
    public async Task R58_OR7_ARealRpcFailureLogLineCarriesTheRequestsCorrelationIdAndATraceId()
    {
        using var capture = new CapturedConsole();
        Guid correlationId;
        IHost host;

        using (capture.Redirect())
        {
            (host, _) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "logcorrelation");
            try
            {
                await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
                correlationId = Guid.NewGuid();

                var headers = new NatsHeaders { { "x-correlation-id", correlationId.ToString() } };
                var request = RpcJson.Serialize(new StockReserveRequestPayload("ORD-LOGCORR-01", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 1)]));
                await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReserve, request, headers);

                await Task.Delay(500);
            }
            finally
            {
                await host.StopAsync();
                host.Dispose();
            }
        }

        var records = capture.ParseJsonLines();
        var mine = records.Where(r => ScopeValue(r, "correlationId") == correlationId.ToString()).ToList();
        Assert.NotEmpty(mine);

        foreach (var record in mine)
        {
            Assert.False(string.IsNullOrEmpty(ScopeValue(record, "TraceId")));
        }
    }

    private static string? ScopeValue(JsonDocument doc, string key)
    {
        if (!doc.RootElement.TryGetProperty("Scopes", out var scopes))
        {
            return null;
        }

        foreach (var scope in scopes.EnumerateArray())
        {
            if (scope.TryGetProperty(key, out var value))
            {
                return value.ToString();
            }
        }

        return null;
    }
}
