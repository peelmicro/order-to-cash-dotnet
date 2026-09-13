using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;
using OrderToCash.Fulfillment.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// The feature's own named risk, closed against a REAL responder rather
/// than a stand-in: this feature's brief — "the check is an end-to-end call
/// through a real broker against the real responders — not two green
/// suites. Prove each RPC you call against the actual responder, not
/// against a stub you wrote to match your own assumption." Boots
/// Fulfillment's REAL <see cref="OrderToCash.Fulfillment.Presentation.StockRpcResponder"/>
/// (real MS-SQL, real NATS, real Kafka — <see cref="OrderToCash.Fulfillment.FulfillmentHost"/>
/// verbatim, the same graph <c>docker-compose</c> runs) and drives
/// <c>GET /stock</c>/<c>POST /stock/replenish</c> through the Gateway's own
/// REAL HTTP pipeline — an actual socket round trip through both
/// processes' own NATS wire, never a mocked <c>INatsConnection</c> on
/// either side.
/// </summary>
[Collection(FulfillmentEndToEndCollection.Name)]
public sealed class FulfillmentStockEndToEndTests(KafkaContainerFixture kafka, MsSqlContainerFixture mssql, NatsContainerFixture nats)
{
    private async Task<(IHost FulfillmentHost, string ConnectionString)> StartFulfillmentAsync(string suffix)
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_fulfillment_gw_it_{suffix}_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
        }

        var builder = OrderToCash.Fulfillment.FulfillmentHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.ConnectionString = connectionString;
                options.Nats.Url = nats.Url;
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Relay.PollIntervalMs = 100;
                options.Responder.MaxConcurrentRequests = 32;
            });

        var host = builder.Build();
        await host.StartAsync();

        await using var probeConnection = new NatsConnection(new NatsOpts { Url = nats.Url });
        await WaitUntilReachableAsync(probeConnection);

        return (host, connectionString);
    }

    private static async Task WaitUntilReachableAsync(NatsConnection connection)
    {
        var probe = RpcJson.Serialize(new StockCheckRequestPayload("GW-IT-PROBE", [new StockCheckRequestLine("GW-IT-PROBE", 1)]));

        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                var reply = await connection.RequestAsync<byte[], byte[]>(
                    StockSubjects.StockCheck, probe, replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromMilliseconds(200) });
                if (reply.Data is not null)
                {
                    return;
                }
            }
            catch (NatsNoReplyException)
            {
            }
            catch (NatsNoRespondersException)
            {
                // Backlog id 63 — the server's IMMEDIATE "definitely nobody
                // subscribed" sentinel does not wait out the request's own
                // timeout, so a loop paced only by that timeout can burn
                // through all 100 attempts in about a millisecond. Paced
                // explicitly below. Found enumerating id 63's six named
                // sites — an 8th instance of the same unpaced shape, outside
                // that enumeration (this file boots a real Fulfillment host
                // from Gateway.IntegrationTests, a separate copy of
                // FulfillmentHostFixture's own probe).
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        }

        throw new TimeoutException("The Fulfillment responder never became reachable.");
    }

    [Fact]
    public async Task GetStock_ThroughTheRealGatewayPipeline_ReturnsWhatTheRealFulfillmentResponderHolds()
    {
        var (fulfillmentHost, connectionString) = await StartFulfillmentAsync("list");

        await using var db = mssql.CreateDbContext(connectionString);
        var now = DateTime.UtcNow;
        db.Stocks.Add(new Stock
        {
            Id = Guid.NewGuid(),
            CompanyCode = "IBERFOODS",
            ProductCode = "PRD-GWIT-001",
            Units = 40,
            ReservedUnits = 10,
            LowStockThreshold = 5,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        await using var gateway = await GatewayTestHost.StartAsync(options =>
        {
            options.Nats.Url = nats.Url;
            options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
            options.Mongo.Database = "otc_read_model_gw_it_unused";
        });

        var token = await LoginAsync(gateway);
        gateway.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await gateway.Client.GetAsync($"/stock?companyCode=IBERFOODS&productCode=PRD-GWIT-001");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"PRD-GWIT-001\"", body);
        Assert.Contains("\"availableUnits\":30", body);

        await fulfillmentHost.StopAsync();
    }

    [Fact]
    public async Task ReplenishStock_ThroughTheRealGatewayPipeline_ActuallyAddsUnitsInFulfillmentsRealDatabase()
    {
        var (fulfillmentHost, connectionString) = await StartFulfillmentAsync("replenish");

        await using var db = mssql.CreateDbContext(connectionString);
        var now = DateTime.UtcNow;
        db.Stocks.Add(new Stock
        {
            Id = Guid.NewGuid(),
            CompanyCode = "IBERFOODS",
            ProductCode = "PRD-GWIT-002",
            Units = 10,
            ReservedUnits = 0,
            LowStockThreshold = 5,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();

        await using var gateway = await GatewayTestHost.StartAsync(options =>
        {
            options.Nats.Url = nats.Url;
            options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
            options.Mongo.Database = "otc_read_model_gw_it_unused";
        });

        var token = await LoginAsync(gateway);
        gateway.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await gateway.Client.PostAsJsonAsync(
            "/stock/replenish",
            new { companyCode = "IBERFOODS", lines = new[] { new { productCode = "PRD-GWIT-002", units = 25 } } });
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.IsSuccessStatusCode,
            $"expected /stock/replenish to succeed; got {(int)response.StatusCode} {response.StatusCode}: {responseBody}");

        await using var verifyDb = mssql.CreateDbContext(connectionString);
        var updated = verifyDb.Stocks.AsNoTracking().Single(s => s.ProductCode == "PRD-GWIT-002");
        Assert.Equal(35, updated.Units);

        await fulfillmentHost.StopAsync();
    }

    private static async Task<string> LoginAsync(GatewayTestHost gateway)
    {
        var response = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponseModel>();
        return body!.AccessToken;
    }

    private sealed record LoginResponseModel(string AccessToken, string TokenType, int ExpiresIn);
}
