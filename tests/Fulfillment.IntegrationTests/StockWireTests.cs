using System.Text.Json;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>`FS4` — the bare-JSON wire, a trilogy contract rather than a framework artefact (ledger L9).</summary>
[Collection(FulfillmentCollection.Name)]
public sealed class StockWireTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Theory]
    [InlineData(StockSubjects.StockCheck)]
    [InlineData(StockSubjects.StockReserve)]
    [InlineData(StockSubjects.StockRelease)]
    [InlineData(StockSubjects.StockList)]
    [InlineData(StockSubjects.StockReplenish)]
    public async Task FS4_AnswersABareJsonRequestWithABareJsonReply_OnAllFiveSubjects(string subject)
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, $"wire-{subject.Replace('.', '-')}");
        using var hostDisposer = host;

        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, Guid.NewGuid(), "ACME", "P1", units: 10);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var (requestBytes, headers) = BuildRequest(subject);

        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, subject, requestBytes, headers);

        using var document = JsonDocument.Parse(reply.Data!);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.False(root.TryGetProperty("response", out _), "the reply must not carry a framework packet's 'response' key.");
        Assert.False(root.TryGetProperty("isDisposed", out _));
        Assert.False(root.TryGetProperty("id", out _));

        await host.StopAsync();
    }

    [Fact]
    public async Task FS4_AnswersABareJsonRpcError_OnAValidationFailure()
    {
        var (host, _) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "wire-error");
        using var _disposeHost = host;

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        var malformed = RpcJson.Serialize(new StockCheckRequestPayload("", []));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockCheck, malformed);

        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);
        Assert.Equal("VALIDATION_FAILED", error.Code);

        await host.StopAsync();
    }

    private static (byte[] Payload, NatsHeaders? Headers) BuildRequest(string subject) => subject switch
    {
        StockSubjects.StockCheck => (RpcJson.Serialize(new StockCheckRequestPayload("ACME", [new StockCheckRequestLine("P1", 1)])), null),
        StockSubjects.StockReserve => (RpcJson.Serialize(new StockReserveRequestPayload("ORD-000099", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 1)])), BuildHeaders()),
        StockSubjects.StockRelease => (RpcJson.Serialize(new StockReleaseRequestPayload("ORD-000098", "order_cancelled")), BuildHeaders()),
        StockSubjects.StockList => (RpcJson.Serialize(new StockListRequestPayload(1, 25)), null),
        StockSubjects.StockReplenish => (RpcJson.Serialize(new StockReplenishRequestPayload("ACME", [new StockReplenishRequestLine("P1", 1)])), null),
        _ => throw new ArgumentOutOfRangeException(nameof(subject)),
    };

    private static NatsHeaders BuildHeaders() => new()
    {
        { "x-correlation-id", Guid.NewGuid().ToString() },
        { "x-request-id", Guid.NewGuid().ToString() },
    };
}
