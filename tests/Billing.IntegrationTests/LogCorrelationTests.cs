using System.Text.Json;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// D2 (review round 1) — R58/OR7, design.md §6, ledger L25, captured from
/// a FULLY configured real host (<c>BillingHost.CreateBuilder</c>'s own
/// <c>AddJsonConsole</c> + <c>IncludeScopes</c> +
/// <c>ActivityTrackingOptions</c> wiring), never asserted against the
/// logger abstraction. <see cref="Console.Out"/> is redirected BEFORE the
/// host is built (<c>AddJsonConsole</c>'s writer captures the
/// <see cref="TextWriter"/> reference once, at construction time).
/// </summary>
[Collection(BillingCollection.Name)]
public sealed class LogCorrelationTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    /// <summary>
    /// design.md §6's scope-push table, RPC responder row —
    /// <c>x-correlation-id</c> is supplied, <c>x-request-id</c> is
    /// deliberately OMITTED so <c>RequireMeta</c> fails and
    /// <c>BillingRpcResponder</c>'s catch logs a warning — with the
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
            (host, _) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "logcorrelation");
            try
            {
                await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
                correlationId = Guid.NewGuid();

                var headers = new NatsHeaders { { "x-correlation-id", correlationId.ToString() } };
                var request = RpcJson.Serialize(new CreditHoldRequestPayload("ORD-LOGCORR-01", "CarrefourEs", "IBERFOODS", new CreditMoney(1_000, "EUR")));
                await BillingHostFixture.RequestBareAsync(connection, CreditSubjects.CreditHold, request, headers);

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
