using System.Text.Json;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Presentation.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>`R34` integration half, `FS9`, `FS10`, and the release happy path — over the REAL responder.</summary>
[Collection(FulfillmentCollection.Name)]
public sealed class StockReleaseIdempotencyTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task ReleaseHappyPath_ReleasedReply_RowsReleased_CounterDown_AndExactlyOneStockReleasedV1CarryingTheReason()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "release-happy");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 10, reservedUnits: 3);
        await FulfillmentHostFixture.SeedReservationAsync(mssql, connectionString, stockId, "ACME", "RETAILER1", "P1", "ORD-000010", 3, "reserved");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        var request = RpcJson.Serialize(new StockReleaseRequestPayload("ORD-000010", "order_cancelled"));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockRelease, request, headers);

        var payload = RpcJson.Deserialize<StockReleaseReplyPayload>(reply.Data!);
        Assert.Equal("released", payload.Outcome);
        Assert.Single(payload.Released!);

        var row = await FulfillmentHostFixture.FindStockAsync(mssql, connectionString, "ACME", "P1");
        Assert.Equal(0, row!.ReservedUnits);

        var reservations = await FulfillmentHostFixture.ReservationsOfAsync(mssql, connectionString, "ORD-000010");
        Assert.Equal("released", Assert.Single(reservations).Status);

        var factRows = await FulfillmentHostFixture.WaitForAsync(
            () => FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "stock.released.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10));
        var factRow = Assert.Single(factRows);

        // D2: G7 names "carrying the request's reason" — deserialise the
        // wire payload and read it, rather than trusting the row exists.
        var factPayload = JsonSerializer.Deserialize<StockReleasedPayload>(factRow.Payload, JsonWire.Options);
        Assert.Equal("order_cancelled", factPayload!.Reason);

        await host.StopAsync();
    }

    [Fact]
    public async Task R34_AnswersSuccessAndEmitsNoSecondFact_WhenEveryReservationIsAlreadyReleased()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "release-r34");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 10);
        await FulfillmentHostFixture.SeedReservationAsync(mssql, connectionString, stockId, "ACME", "RETAILER1", "P1", "ORD-000011", 3, "released");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        var request = RpcJson.Serialize(new StockReleaseRequestPayload("ORD-000011", "order_cancelled"));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockRelease, request, headers);

        var payload = RpcJson.Deserialize<StockReleaseReplyPayload>(reply.Data!);
        Assert.Equal("already_released", payload.Outcome);
        Assert.Empty(payload.Released!);

        var factRows = await FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value);
        Assert.Empty(factRows);

        await host.StopAsync();
    }

    [Fact]
    public async Task FS9_AnswersAlreadyReleasedWithAnEmptyListAndEmitsNothing_ForAnOrderThatNeverHeldAReservation()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "release-fs9");
        using var _ = host;

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        var request = RpcJson.Serialize(new StockReleaseRequestPayload("ORD-000012", "order_cancelled"));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockRelease, request, headers);

        var payload = RpcJson.Deserialize<StockReleaseReplyPayload>(reply.Data!);
        Assert.Equal("already_released", payload.Outcome);
        Assert.Empty(payload.Released!);

        var factRows = await FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value);
        Assert.Empty(factRows);

        await host.StopAsync();
    }

    [Fact]
    public async Task FS10_RepliesPreconditionFailedAndEmitsNothing_WhenTheOrdersReservationsAreConsumed()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "release-fs10");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 7, reservedUnits: 0);
        await FulfillmentHostFixture.SeedReservationAsync(mssql, connectionString, stockId, "ACME", "RETAILER1", "P1", "ORD-000013", 3, "consumed");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        var request = RpcJson.Serialize(new StockReleaseRequestPayload("ORD-000013", "order_cancelled"));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockRelease, request, headers);

        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);
        Assert.Equal("PRECONDITION_FAILED", error.Code);

        var reservations = await FulfillmentHostFixture.ReservationsOfAsync(mssql, connectionString, "ORD-000013");
        Assert.Equal("consumed", Assert.Single(reservations).Status);

        var factRows = await FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value);
        Assert.Empty(factRows);

        await host.StopAsync();
    }

    private static NatsHeaders BuildHeaders(UniqueId correlationId, UniqueId requestId) => new()
    {
        { "x-correlation-id", correlationId.Value.ToString() },
        { "x-request-id", requestId.Value.ToString() },
    };
}
