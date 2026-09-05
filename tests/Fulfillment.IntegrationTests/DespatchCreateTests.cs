using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Presentation.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>`R36`, F6/F7/F8 — <c>despatch.create</c> consuming reservations, over the REAL responder (real MS-SQL, NATS, Kafka).</summary>
[Collection(FulfillmentCollection.Name)]
public sealed class DespatchCreateTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    [Fact]
    public async Task HappyPath_ConsumesTheReservation_CreatesTheDespatchAndDespatchItemRows_AndEmitsExactlyOneOrderDespatchedV1CarryingTheDespatchedFields()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "despatch-happy");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 10, reservedUnits: 4);
        await FulfillmentHostFixture.SeedReservationAsync(mssql, connectionString, stockId, "ACME", "RETAILER1", "P1", "ORD-000020", 4, "reserved");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var headers = BuildHeaders(correlationId, UniqueId.New());

        var before = DateTimeOffset.UtcNow;
        var request = RpcJson.Serialize(new DespatchCreateRequestPayload("ORD-000020"));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.DespatchCreate, request, headers);
        var after = DateTimeOffset.UtcNow;

        var payload = RpcJson.Deserialize<DespatchCreateReplyPayload>(reply.Data!);
        Assert.True(payload.Created);
        Assert.Matches("^DES-[0-9]{6,}$", payload.DespatchReference);
        var line = Assert.Single(payload.Lines!);
        Assert.Equal("P1", line.ProductCode);
        Assert.Equal(4, line.Units);

        // Reservations move to consumed (acceptance bullet 1).
        var reservations = await FulfillmentHostFixture.ReservationsOfAsync(mssql, connectionString, "ORD-000020");
        Assert.Equal("consumed", Assert.Single(reservations).Status);

        var stockRow = await FulfillmentHostFixture.FindStockAsync(mssql, connectionString, "ACME", "P1");
        Assert.Equal(6, stockRow!.Units); // consumed units left the stock (not just reservedUnits)
        Assert.Equal(0, stockRow.ReservedUnits);

        // Despatch + despatch_items rows.
        var despatch = await FulfillmentHostFixture.FindDespatchAsync(mssql, connectionString, "ORD-000020");
        Assert.NotNull(despatch);
        Assert.Equal(payload.DespatchReference, despatch!.DespatchReference);
        var items = await FulfillmentHostFixture.DespatchItemsOfAsync(mssql, connectionString, despatch.Id);
        var item = Assert.Single(items);
        Assert.Equal("P1", item.ProductCode);
        Assert.Equal(4, item.Units);

        // Exactly one order.despatched.v1 (acceptance bullet 2), carrying the despatched fields (F7).
        var factRows = await FulfillmentHostFixture.WaitForAsync(
            () => FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value, "order.despatched.v1"),
            rows => rows.Count > 0,
            TimeSpan.FromSeconds(10));
        var factRow = Assert.Single(factRows);

        var factPayload = JsonSerializer.Deserialize<OrderDespatchedPayload>(factRow.Payload, JsonWire.Options);
        Assert.Equal("ORD-000020", factPayload!.OrderReference);
        Assert.Equal(payload.DespatchReference, factPayload.DespatchReference);
        Assert.Equal("ACME", factPayload.CompanyCode);
        Assert.Equal("RETAILER1", factPayload.RetailerCode);
        var factLine = Assert.Single(factPayload.Lines);
        Assert.Equal("P1", factLine.ProductCode);
        Assert.Equal(4, factLine.Units);

        // A4 (review round 1): despatchDate is the one required field sourced
        // from the clock rather than the request or the reservations — assert
        // it lands inside the window the request was actually served in, so a
        // wrong/defaulted/mismapped clock value on the wire fails this test.
        Assert.InRange(factPayload.DespatchDate, before.AddSeconds(-1), after.AddSeconds(1));

        await host.StopAsync();
    }

    [Fact]
    public async Task F8_AReissuedDespatchCreate_ReturnsTheExistingDespatchReferenceWithCreatedFalse_AndEmitsNoSecondFact()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "despatch-f8");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 10, reservedUnits: 3);
        await FulfillmentHostFixture.SeedReservationAsync(mssql, connectionString, stockId, "ACME", "RETAILER1", "P1", "ORD-000021", 3, "reserved");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });

        var firstCorrelationId = UniqueId.New();
        var firstRequest = RpcJson.Serialize(new DespatchCreateRequestPayload("ORD-000021"));
        var firstReply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.DespatchCreate, firstRequest, BuildHeaders(firstCorrelationId, UniqueId.New()));
        var firstPayload = RpcJson.Deserialize<DespatchCreateReplyPayload>(firstReply.Data!);
        Assert.True(firstPayload.Created);

        var secondCorrelationId = UniqueId.New();
        var secondRequest = RpcJson.Serialize(new DespatchCreateRequestPayload("ORD-000021"));
        var secondReply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.DespatchCreate, secondRequest, BuildHeaders(secondCorrelationId, UniqueId.New()));
        var secondPayload = RpcJson.Deserialize<DespatchCreateReplyPayload>(secondReply.Data!);

        Assert.False(secondPayload.Created);
        Assert.Equal(firstPayload.DespatchReference, secondPayload.DespatchReference);

        var despatch = await FulfillmentHostFixture.FindDespatchAsync(mssql, connectionString, "ORD-000021");
        Assert.NotNull(despatch);
        var items = await FulfillmentHostFixture.DespatchItemsOfAsync(mssql, connectionString, despatch!.Id);
        Assert.Single(items); // still exactly one line — no second insert

        var secondFactRows = await FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, secondCorrelationId.Value);
        Assert.Empty(secondFactRows);

        await host.StopAsync();
    }

    [Fact]
    public async Task Precondition_NeverReserved_RepliesPreconditionFailedAndCreatesNoDespatchAndEmitsNothing()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "despatch-never-reserved");
        using var _ = host;

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var request = RpcJson.Serialize(new DespatchCreateRequestPayload("ORD-000022"));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.DespatchCreate, request, BuildHeaders(correlationId, UniqueId.New()));

        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);
        Assert.Equal("PRECONDITION_FAILED", error.Code);

        Assert.Null(await FulfillmentHostFixture.FindDespatchAsync(mssql, connectionString, "ORD-000022"));
        var factRows = await FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value);
        Assert.Empty(factRows);

        await host.StopAsync();
    }

    [Fact]
    public async Task Precondition_EveryReservationAlreadyReleased_RepliesPreconditionFailedAndCreatesNoDespatchAndEmitsNothing()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "despatch-all-released");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 10);
        await FulfillmentHostFixture.SeedReservationAsync(mssql, connectionString, stockId, "ACME", "RETAILER1", "P1", "ORD-000023", 3, "released");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var correlationId = UniqueId.New();
        var request = RpcJson.Serialize(new DespatchCreateRequestPayload("ORD-000023"));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.DespatchCreate, request, BuildHeaders(correlationId, UniqueId.New()));

        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);
        Assert.Equal("PRECONDITION_FAILED", error.Code);

        Assert.Null(await FulfillmentHostFixture.FindDespatchAsync(mssql, connectionString, "ORD-000023"));
        var reservations = await FulfillmentHostFixture.ReservationsOfAsync(mssql, connectionString, "ORD-000023");
        Assert.Equal("released", Assert.Single(reservations).Status); // untouched
        var factRows = await FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, correlationId.Value);
        Assert.Empty(factRows);

        await host.StopAsync();
    }

    [Fact]
    public async Task FS3_RepliesValidationFailedAndCreatesNothing_WhenTheCorrelationHeaderIsMissing()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "despatch-fs3");
        using var _ = host;

        var stockId = Guid.NewGuid();
        await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", "P1", units: 10, reservedUnits: 2);
        await FulfillmentHostFixture.SeedReservationAsync(mssql, connectionString, stockId, "ACME", "RETAILER1", "P1", "ORD-000024", 2, "reserved");

        await using var connection = new NatsConnection(new NatsOpts { Url = nats.Url });
        var request = RpcJson.Serialize(new DespatchCreateRequestPayload("ORD-000024"));
        var reply = await FulfillmentHostFixture.RequestBareAsync(connection, StockSubjects.DespatchCreate, request, headers: null);

        var error = RpcJson.Deserialize<RpcErrorPayload>(reply.Data!);
        Assert.Equal("VALIDATION_FAILED", error.Code);

        Assert.Null(await FulfillmentHostFixture.FindDespatchAsync(mssql, connectionString, "ORD-000024"));
        var reservations = await FulfillmentHostFixture.ReservationsOfAsync(mssql, connectionString, "ORD-000024");
        Assert.Equal("reserved", Assert.Single(reservations).Status); // untouched — nothing was dispatched

        await host.StopAsync();
    }

    /// <summary>
    /// A3 (review round 1): `test-matrix.md`'s R36 row named this integration
    /// case explicitly and it was previously realised only by argument (the
    /// shared lock protocol + FS6/FS7), not by a probe of its own.
    /// <c>DespatchCreationService.CreateAsync</c>'s own doc comment states the
    /// mechanism: a <c>despatch.create</c> and a <c>stock.release</c> for the
    /// SAME order block on the SAME stock rows (design.md §4.3/§4.4), so
    /// exactly one of the two can win, and the loser must observe the
    /// winner's committed state under the SAME lock rather than a stale one.
    /// This test does not assume which side wins — either is a legitimate
    /// race outcome — but asserts BOTH sides are mutually exclusive and that
    /// exactly one fact is emitted for the order, never both, never neither.
    /// </summary>
    [Fact]
    public async Task Concurrency_DespatchCreateRacingASimultaneousStockRelease_ExactlyOneWinsAndEmitsExactlyOneFact()
    {
        var (host, connectionString) = await FulfillmentHostFixture.StartHostAsync(mssql, nats, kafka, "despatch-race-release");
        using var _ = host;

        for (var i = 0; i < 10; i++)
        {
            var product = $"RACE-DESP-{i}";
            var orderReference = $"ORD-{900000 + i}";
            var stockId = Guid.NewGuid();
            await FulfillmentHostFixture.SeedStockAsync(mssql, connectionString, stockId, "ACME", product, units: 10, reservedUnits: 4);
            await FulfillmentHostFixture.SeedReservationAsync(mssql, connectionString, stockId, "ACME", "RETAILER1", product, orderReference, 4, "reserved");

            await using var despatchConnection = new NatsConnection(new NatsOpts { Url = nats.Url });
            await using var releaseConnection = new NatsConnection(new NatsOpts { Url = nats.Url });

            var despatchCorrelationId = UniqueId.New();
            var releaseCorrelationId = UniqueId.New();

            var despatchRequest = RpcJson.Serialize(new DespatchCreateRequestPayload(orderReference));
            var releaseRequest = RpcJson.Serialize(new StockReleaseRequestPayload(orderReference, "order_cancelled"));

            var despatchTask = FulfillmentHostFixture.RequestBareAsync(despatchConnection, StockSubjects.DespatchCreate, despatchRequest, BuildHeaders(despatchCorrelationId, UniqueId.New()), TimeSpan.FromSeconds(15));
            var releaseTask = FulfillmentHostFixture.RequestBareAsync(releaseConnection, StockSubjects.StockRelease, releaseRequest, BuildHeaders(releaseCorrelationId, UniqueId.New()), TimeSpan.FromSeconds(15));

            var results = await Task.WhenAll(despatchTask, releaseTask);

            using var despatchJson = JsonDocument.Parse(results[0].Data!);
            using var releaseJson = JsonDocument.Parse(results[1].Data!);

            var despatchCreated = despatchJson.RootElement.TryGetProperty("created", out var createdProp) && createdProp.GetBoolean();
            var releaseReleased = releaseJson.RootElement.TryGetProperty("outcome", out var outcomeProp) && outcomeProp.GetString() == "released";

            Assert.True(
                despatchCreated ^ releaseReleased,
                $"exactly one of despatch.create / stock.release must win the race for {orderReference} (despatch reply: {Encoding.UTF8.GetString(results[0].Data!)}, release reply: {Encoding.UTF8.GetString(results[1].Data!)})");

            var reservation = Assert.Single(await FulfillmentHostFixture.ReservationsOfAsync(mssql, connectionString, orderReference));

            if (despatchCreated)
            {
                Assert.Equal("consumed", reservation.Status);

                // The loser re-read under the SAME lock and found the
                // reservation already `consumed` (a terminal state, F4/FS10)
                // — StockItem.Release refuses this as PRECONDITION_FAILED
                // rather than treating it as F5's "nothing to release"
                // no-op, which applies only when the reservation was never
                // held (or already released), never when it was consumed.
                Assert.Equal("PRECONDITION_FAILED", releaseJson.RootElement.GetProperty("code").GetString());

                var winningFactRows = await FulfillmentHostFixture.WaitForAsync(
                    () => FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, despatchCorrelationId.Value, "order.despatched.v1"),
                    rows => rows.Count > 0,
                    TimeSpan.FromSeconds(10));
                Assert.Single(winningFactRows);

                Assert.Empty(await FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, releaseCorrelationId.Value)); // the loser emits nothing
            }
            else
            {
                Assert.Equal("released", reservation.Status);
                Assert.Equal("PRECONDITION_FAILED", despatchJson.RootElement.GetProperty("code").GetString()); // the loser was refused, no despatch created

                var winningFactRows = await FulfillmentHostFixture.WaitForAsync(
                    () => FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, releaseCorrelationId.Value, "stock.released.v1"),
                    rows => rows.Count > 0,
                    TimeSpan.FromSeconds(10));
                Assert.Single(winningFactRows);

                Assert.Empty(await FulfillmentHostFixture.OutboxRowsForAsync(mssql, connectionString, despatchCorrelationId.Value)); // the loser emits nothing
                Assert.Null(await FulfillmentHostFixture.FindDespatchAsync(mssql, connectionString, orderReference));
            }
        }

        await host.StopAsync();
    }

    private static NatsHeaders BuildHeaders(UniqueId correlationId, UniqueId requestId) => new()
    {
        { "x-correlation-id", correlationId.Value.ToString() },
        { "x-request-id", requestId.Value.ToString() },
    };
}
