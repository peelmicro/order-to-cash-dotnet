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

/// <summary>`R32`/`R33` integration halves, `FS3`, `FS5` — over the REAL responder, real MS-SQL, real NATS, real Kafka.</summary>
[Collection(FulfillmentCollection.Name)]
public sealed class StockReserveTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    // Renamed per review A1: this test does not read the outbox at all — the
    // fact's emission is asserted separately by FS3 below. A name claiming
    // "AndEmitsExactlyOneStockReservedV1" here would be cheap to believe and
    // wrong, exactly the class of overstatement the review flagged.
    [Fact]
    public async Task AcceptedPath_OneReservedRowPerLine_RaisesReservedUnits()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "reserve-accepted");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 10);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var requestId = UniqueId.New();
        var headers = BuildHeaders(correlationId, requestId);

        var request = RpcJson.Serialize(new StockReserveRequestPayload("ORD-000001", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 4)]));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReserve, request, headers);

        var payload = RpcJson.Deserialize<StockReserveReplyPayload>(reply.Data!);
        Assert.Equal("accepted", payload.Outcome);
        Assert.Single(payload.Reservations!);

        var row = await FulfillmentHostFixture.FindStockAsync(mssql, connectionString, "ACME", "P1");
        Assert.Equal(4, row!.ReservedUnits);

        var reservations = await FulfillmentHostFixture.ReservationsOfAsync(mssql, connectionString, "ORD-000001");
        var reservation = Assert.Single(reservations);
        Assert.Equal("reserved", reservation.Status);

        await host.StopAsync();
    }

    [Fact]
    public async Task FS3_StampsCorrelationIdFromTheHeaderAndCausationIdFromTheRequestId_OnTheEmittedStockReservedFact()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "reserve-fs3");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 10);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var requestId = UniqueId.New();
        var headers = BuildHeaders(correlationId, requestId);

        var request = RpcJson.Serialize(new StockReserveRequestPayload("ORD-000002", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 2)]));
        await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReserve, request, headers);

        var outboxRows = await FulfillmentHostFixture.WaitForAsync(
            () => FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "stock.reserved.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10));

        var row = Assert.Single(outboxRows);
        Assert.Equal(correlationId.Value, row.CorrelationId);
        Assert.Equal(requestId.Value, row.CausationId);

        // Recommended by review round 2 §8: the mapper's payload fields are
        // otherwise unobserved on the accepted path (probe R2-D showed a
        // fabricated retailerCode reaches the wire unnoticed on THIS path
        // before this assertion existed).
        var factPayload = JsonSerializer.Deserialize<StockReservedPayload>(row.Payload, JsonWire.Options);
        Assert.Equal("RETAILER1", factPayload!.RetailerCode);

        await host.StopAsync();
    }

    /// <summary>
    /// `R33` integration shape, ledger-adjacent D1 fix: on the rejected path
    /// nothing else is written — no reservation rows, no counter change —
    /// so the outbox row IS the transaction's only observable artefact.
    /// `tasks.md` G3 and `design.md` §14 both require asserting it directly
    /// rather than through the reply alone; the review's D1 finding is that
    /// this test asserted the reply and stopped, so deleting the branch's
    /// only persistence (skipping `SaveChangesAsync` on the rejected path)
    /// left this whole suite green.
    /// </summary>
    [Fact]
    public async Task RejectedPath_ZeroRowsCreated_AndOneStockRejectedV1RowInTheOutboxNamingRequestedAndAvailable()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "reserve-rejected");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 2);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        var request = RpcJson.Serialize(new StockReserveRequestPayload("ORD-000003", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 5)]));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReserve, request, headers);

        var payload = RpcJson.Deserialize<StockReserveReplyPayload>(reply.Data!);
        Assert.Equal("rejected", payload.Outcome);
        var shortage = Assert.Single(payload.Shortages!);
        Assert.Equal(5, shortage.Requested);
        Assert.Equal(2, shortage.Available);

        var row = await FulfillmentHostFixture.FindStockAsync(mssql, connectionString, "ACME", "P1");
        Assert.Equal(0, row!.ReservedUnits);

        var reservations = await FulfillmentHostFixture.ReservationsOfAsync(mssql, connectionString, "ORD-000003");
        Assert.Empty(reservations);

        // D1: the outbox row is the transaction's only artefact on the
        // rejected path — assert it directly, not just through the reply.
        var outboxRows = await FulfillmentHostFixture.WaitForAsync(
            () => FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "stock.rejected.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10));

        var factRow = Assert.Single(outboxRows);
        var factPayload = JsonSerializer.Deserialize<StockRejectedPayload>(factRow.Payload, JsonWire.Options);
        var factShortage = Assert.Single(factPayload!.Shortages);
        Assert.Equal("P1", factShortage.ProductCode);
        Assert.Equal(5, factShortage.Requested);
        Assert.Equal(2, factShortage.Available);

        await host.StopAsync();
    }

    [Fact]
    public async Task FS5_AnswersAlreadyReservedWithTheExistingReservations_ChangingNoCounterAndEmittingNoSecondFact_WhenReIssued()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "reserve-fs5-reissue");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 10);

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers1 = BuildHeaders(correlationId, UniqueId.New());
        var headers2 = BuildHeaders(correlationId, UniqueId.New()); // sweeper re-issue: same correlation, fresh request id

        var request = RpcJson.Serialize(new StockReserveRequestPayload("ORD-000004", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 3)]));

        var firstReply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReserve, request, headers1);
        var firstPayload = RpcJson.Deserialize<StockReserveReplyPayload>(firstReply.Data!);
        Assert.Equal("accepted", firstPayload.Outcome);

        var secondReply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReserve, request, headers2);
        var secondPayload = RpcJson.Deserialize<StockReserveReplyPayload>(secondReply.Data!);

        Assert.Equal("already_reserved", secondPayload.Outcome);
        Assert.Equal(firstPayload.Reservations!.Select(r => r.ReservationId), secondPayload.Reservations!.Select(r => r.ReservationId));

        var row = await FulfillmentHostFixture.FindStockAsync(mssql, connectionString, "ACME", "P1");
        Assert.Equal(3, row!.ReservedUnits); // unchanged by the re-issue

        var factRows = await FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "stock.reserved.v1");
        Assert.Single(factRows); // no second fact

        await host.StopAsync();
    }

    /// <summary>
    /// `FS5` — an order whose ONLY reservation is already <c>released</c>
    /// still short-circuits to <c>already_reserved</c>: this is #7's exact
    /// rejected defect (D1) reproduced live, over the REAL responder and a
    /// REAL row lock, not just the unit fakes.
    /// </summary>
    [Fact]
    public async Task FS5_AnswersAlreadyReserved_ForAnOrderWhoseOnlyReservationIsAlreadyReleased_ReservingNothingNew()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "reserve-fs5-released");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 10, reservedUnits: 0);
        await FulfillmentHostFixture.SeedReservationAsync(mssql, connectionString, stockId, "ACME", "RETAILER1", "P1", "ORD-000005", 3, "released");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        var request = RpcJson.Serialize(new StockReserveRequestPayload("ORD-000005", "RETAILER1", "ACME", [new StockReserveRequestLine("P1", 3)]));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.StockReserve, request, headers);

        var payload = RpcJson.Deserialize<StockReserveReplyPayload>(reply.Data!);
        Assert.Equal("already_reserved", payload.Outcome);

        var row = await FulfillmentHostFixture.FindStockAsync(mssql, connectionString, "ACME", "P1");
        Assert.Equal(0, row!.ReservedUnits); // still zero — nothing new reserved

        var reservations = await FulfillmentHostFixture.ReservationsOfAsync(mssql, connectionString, "ORD-000005");
        var reservation = Assert.Single(reservations);
        Assert.Equal("released", reservation.Status); // untouched

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
