using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Presentation;
using OrderToCash.Orders.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Feature <c>orders_acceptance</c>'s three acceptance items, end to end,
/// over the REAL transport: a real NATS broker (<see cref="NatsContainerFixture"/>),
/// a real MS-SQL database, the real <c>orders.create</c> responder
/// (<see cref="OrdersCreateResponder"/>) resolved through the SAME
/// <c>AddOrdersOutbox</c> + <c>AddOrdersAcceptance</c> + <c>AddDispatcher</c>
/// composition <c>Program.cs</c> uses, and a real stand-in Fulfillment
/// responder (<see cref="StandInFulfillmentStockCheckResponder"/>) answering
/// <c>fulfillment.stock.check</c> — never a mocked <c>INatsConnection</c>,
/// because the transport itself is what this feature proves. Fulfillment
/// (feature 17) does not exist yet, so the stand-in is the only honest way
/// to prove the CLIENT side without waiting for it.
/// </summary>
[Collection(NatsCollection.Name)]
public sealed class OrdersCreateAcceptanceTests(NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    [Fact]
    public async Task AcceptanceItems1And2_OrdersCreate_ChecksStockSynchronouslyAndReturnsTheOrderIdSynchronously()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_accept1_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        using var host = BuildHost(connectionString);
        await host.StartAsync();
        try
        {
            await using var fulfillment = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            await WaitUntilOrdersCreateReachableAsync(caller, CancellationToken.None);

            var request = new OrdersCreateRequestPayload(
                RequestId: null,
                OrderPersistenceTestSupport.RetailerCode,
                OrderPersistenceTestSupport.CompanyCode,
                OrderPersistenceTestSupport.Currency,
                Lines:
                [
                    new OrdersCreateRequestLine(OrderPersistenceTestSupport.ProductCode1, 2, UnitPrice: 1_000, LineDiscount: 50),
                    new OrdersCreateRequestLine(OrderPersistenceTestSupport.ProductCode2, 1, UnitPrice: 500, LineDiscount: 0),
                ],
                OrderDiscount: null,
                Notes: "acceptance test order");

            var replyMsg = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.OrdersCreate,
                RpcJson.Serialize(request),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);

            Assert.NotNull(replyMsg.Data);
            var reply = RpcJson.Deserialize<OrdersCreateReplyPayload>(replyMsg.Data!);

            // Item 2: the order id, reference and status come back
            // synchronously, on this one request/reply round trip.
            Assert.NotEqual(Guid.Empty, reply.OrderId);
            Assert.Equal("ORD-000001", reply.OrderReference);
            Assert.Equal("placed", reply.Status);
            Assert.Equal(OrderPersistenceTestSupport.Currency, reply.Currency);

            // The #7 fixture-defect rule: three DISTINCT, non-zero amounts,
            // so a mapping that returns the wrong one of the three fails
            // this exact assertion (armed — see impl report).
            Assert.Equal(2_500, reply.InitialAmount);
            Assert.Equal(50, reply.InitialDiscount);
            Assert.Equal(2_450, reply.TotalAmount);

            // The order genuinely exists in the database — not merely a
            // reply carrying plausible-looking numbers.
            await using var assertDb = mssql.CreateDbContext(connectionString);
            var row = await assertDb.Orders.SingleAsync(o => o.Id == reply.OrderId);
            Assert.Equal("placed", row.Status);
            Assert.Equal(2_500, row.InitialAmount);
            Assert.Equal(50, row.InitialDiscount);
            Assert.Equal(2_450, row.TotalAmount);

            // review A1: the negative acceptance tests assert
            // OutboxMessages.CountAsync() == 0, which means nothing unless
            // the POSITIVE twin asserts the count a successful placement
            // DOES produce — one row, order.placed.v1, through the real
            // orders.create path (R13/R14 themselves stay proven elsewhere;
            // this is what makes THIS test's own pairing mean something).
            var outboxRow = await assertDb.OutboxMessages.SingleAsync();
            Assert.Equal("order.placed.v1", outboxRow.EventType);
            Assert.Equal(reply.OrderId, outboxRow.AggregateId);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// Acceptance item 1's other half: the stock check subject really is
    /// hit, with the request's own lines — proven by an assertion INSIDE
    /// the stand-in responder's own answer function (only reachable if the
    /// responder truly called fulfillment.stock.check over the wire).
    /// </summary>
    [Fact]
    public async Task AcceptanceItem1_OrdersCreate_CallsFulfillmentStockCheckWithTheRequestsOwnCompanyAndLines()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_accept2_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        using var host = BuildHost(connectionString);
        await host.StartAsync();
        try
        {
            StockCheckRequestPayload? observed = null;
            await using var fulfillment = await StandInFulfillmentStockCheckResponder.StartAsync(
                nats.Url,
                request =>
                {
                    observed = request;
                    return new StockCheckReplyPayload(true, request.Lines.Select(l => new StockCheckReplyLine(l.ProductCode, l.Quantity, l.Quantity, true)).ToList());
                },
                CancellationToken.None);
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            await WaitUntilOrdersCreateReachableAsync(caller, CancellationToken.None);

            var request = new OrdersCreateRequestPayload(
                RequestId: null,
                OrderPersistenceTestSupport.RetailerCode,
                OrderPersistenceTestSupport.CompanyCode,
                OrderPersistenceTestSupport.Currency,
                Lines: [new OrdersCreateRequestLine(OrderPersistenceTestSupport.ProductCode1, 3, UnitPrice: null, LineDiscount: null)],
                OrderDiscount: null,
                Notes: null);

            var replyMsg = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.OrdersCreate,
                RpcJson.Serialize(request),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);

            Assert.NotNull(replyMsg.Data);
            RpcJson.Deserialize<OrdersCreateReplyPayload>(replyMsg.Data!); // the request succeeded

            Assert.NotNull(observed);
            Assert.Equal(OrderPersistenceTestSupport.CompanyCode, observed!.CompanyCode);
            var line = Assert.Single(observed.Lines);
            Assert.Equal(OrderPersistenceTestSupport.ProductCode1, line.ProductCode);
            Assert.Equal(3, line.Quantity);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// Acceptance item 3, and CLAUDE.md's suppression-direction guard over
    /// the wire: a stock rejection returns an <c>RpcError</c> whose code is
    /// <c>STOCK_UNAVAILABLE</c>, and NO order row is ever written.
    /// </summary>
    [Fact]
    public async Task AcceptanceItem3_OrdersCreate_RejectsWithStockUnavailableAndPersistsNoOrderWhenFulfillmentReportsShort()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_accept3_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        using var host = BuildHost(connectionString);
        await host.StartAsync();
        try
        {
            await using var fulfillment = await StandInFulfillmentStockCheckResponder.StartUnavailableAsync(nats.Url, CancellationToken.None);
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            await WaitUntilOrdersCreateReachableAsync(caller, CancellationToken.None);

            var request = new OrdersCreateRequestPayload(
                RequestId: null,
                OrderPersistenceTestSupport.RetailerCode,
                OrderPersistenceTestSupport.CompanyCode,
                OrderPersistenceTestSupport.Currency,
                Lines: [new OrdersCreateRequestLine(OrderPersistenceTestSupport.ProductCode1, 2, UnitPrice: 1_000, LineDiscount: 50)],
                OrderDiscount: null,
                Notes: null);

            var replyMsg = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.OrdersCreate,
                RpcJson.Serialize(request),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);

            Assert.NotNull(replyMsg.Data);
            var error = RpcJson.Deserialize<RpcErrorPayload>(replyMsg.Data!);

            Assert.Equal("STOCK_UNAVAILABLE", error.Code);

            await using var assertDb = mssql.CreateDbContext(connectionString);
            Assert.Equal(0, await assertDb.Orders.CountAsync());
            Assert.Equal(0, await assertDb.OutboxMessages.CountAsync());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// review A9 (round 2) — A2's validation claim, proven at the level A2
    /// was actually about: a malformed request reaches the caller as
    /// <c>VALIDATION_FAILED</c> over the REAL wire, never
    /// <c>INTERNAL_ERROR</c>. No stand-in Fulfillment responder is started
    /// — deliberately: <c>OrdersCreateRequestValidator.Validate</c> runs
    /// before <c>ToCommand</c>, so a request missing <c>lines</c> never
    /// reaches the stock check at all, and this test would still pass if a
    /// stand-in happened to be running, which would prove nothing about
    /// ordering. Absence of a stand-in is itself part of the proof.
    /// </summary>
    [Fact]
    public async Task OrdersCreate_ARequestMissingLinesIsRefusedAsValidationFailedNotInternalErrorAndPersistsNoOrder()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_accept7_{Guid.NewGuid():N}");
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
            await WaitUntilOrdersCreateReachableAsync(caller, CancellationToken.None);

            var request = new OrdersCreateRequestPayload(
                RequestId: null,
                OrderPersistenceTestSupport.RetailerCode,
                OrderPersistenceTestSupport.CompanyCode,
                OrderPersistenceTestSupport.Currency,
                Lines: [],
                OrderDiscount: null,
                Notes: null);

            var replyMsg = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.OrdersCreate,
                RpcJson.Serialize(request),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);

            Assert.NotNull(replyMsg.Data);
            var error = RpcJson.Deserialize<RpcErrorPayload>(replyMsg.Data!);

            Assert.Equal("VALIDATION_FAILED", error.Code);
            Assert.NotEqual("INTERNAL_ERROR", error.Code);
            Assert.Contains("lines", error.Message, StringComparison.Ordinal);

            await using var assertDb = mssql.CreateDbContext(connectionString);
            Assert.Equal(0, await assertDb.Orders.CountAsync());
            Assert.Equal(0, await assertDb.OutboxMessages.CountAsync());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// Review D1 — the OUTAGE half. Nobody is subscribed to
    /// <c>fulfillment.stock.check</c> at all (no stand-in started), so
    /// <see cref="NATS.Client.Core.NatsNoRespondersException"/> is the REAL
    /// exception <c>NatsStockAvailabilityChecker</c> observes — never
    /// constructed by hand, never injected through a fake port. Distinct
    /// from <see cref="AcceptanceItem_OrdersCreate_MapsASilentStockCheckResponderToTimeoutAndPersistsNoOrder"/>'s
    /// TIMEOUT case by design.md §9.2, and the two were interchangeable in
    /// production before this pair existed (D1's own arming evidence, §5 of
    /// the impl report) — a caller-side outage (<c>UNAVAILABLE</c>) is not
    /// the same reply as a responder that is up but silent (<c>TIMEOUT</c>),
    /// because feature 42 retries one forever and the other never.
    /// </summary>
    [Fact]
    public async Task AcceptanceItem_OrdersCreate_MapsNoStockCheckResponderToUnavailableAndPersistsNoOrder()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_accept5_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        using var host = BuildHost(connectionString);
        await host.StartAsync();
        try
        {
            // Deliberately NO StandInFulfillmentStockCheckResponder — this
            // is the point of the test.
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            await WaitUntilOrdersCreateReachableAsync(caller, CancellationToken.None);

            var request = new OrdersCreateRequestPayload(
                RequestId: null,
                OrderPersistenceTestSupport.RetailerCode,
                OrderPersistenceTestSupport.CompanyCode,
                OrderPersistenceTestSupport.Currency,
                Lines: [new OrdersCreateRequestLine(OrderPersistenceTestSupport.ProductCode1, 1, UnitPrice: 1_000, LineDiscount: null)],
                OrderDiscount: null,
                Notes: null);

            var replyMsg = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.OrdersCreate,
                RpcJson.Serialize(request),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);

            Assert.NotNull(replyMsg.Data);
            var error = RpcJson.Deserialize<RpcErrorPayload>(replyMsg.Data!);

            Assert.Equal("UNAVAILABLE", error.Code);
            Assert.Equal(RpcSubjects.StockCheck, ((System.Text.Json.JsonElement)error.Details!["subject"]!).GetString());

            await using var assertDb = mssql.CreateDbContext(connectionString);
            Assert.Equal(0, await assertDb.Orders.CountAsync());
            Assert.Equal(0, await assertDb.OutboxMessages.CountAsync());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// Review D1 — the TIMEOUT half. <see cref="StandInFulfillmentStockCheckResponder.StartSilentAsync"/>
    /// IS subscribed (so <c>NatsNoRespondersException</c> never fires — the
    /// harness's own startup probe proves it, over the same subscription),
    /// but never answers the real request, so
    /// <see cref="NATS.Client.Core.NatsNoReplyException"/> is the REAL
    /// exception observed once <c>NatsOptions.StockCheckTimeoutMs</c>
    /// elapses. Shrunk to keep this test fast.
    /// </summary>
    [Fact]
    public async Task AcceptanceItem_OrdersCreate_MapsASilentStockCheckResponderToTimeoutAndPersistsNoOrder()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_accept6_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        using var host = BuildHost(connectionString, stockCheckTimeoutMs: 500);
        await host.StartAsync();
        try
        {
            await using var fulfillment = await StandInFulfillmentStockCheckResponder.StartSilentAsync(nats.Url, CancellationToken.None);
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            await WaitUntilOrdersCreateReachableAsync(caller, CancellationToken.None);

            var request = new OrdersCreateRequestPayload(
                RequestId: null,
                OrderPersistenceTestSupport.RetailerCode,
                OrderPersistenceTestSupport.CompanyCode,
                OrderPersistenceTestSupport.Currency,
                Lines: [new OrdersCreateRequestLine(OrderPersistenceTestSupport.ProductCode1, 1, UnitPrice: 1_000, LineDiscount: null)],
                OrderDiscount: null,
                Notes: null);

            var replyMsg = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.OrdersCreate,
                RpcJson.Serialize(request),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);

            Assert.NotNull(replyMsg.Data);
            var error = RpcJson.Deserialize<RpcErrorPayload>(replyMsg.Data!);

            Assert.Equal("TIMEOUT", error.Code);
            Assert.Equal(RpcSubjects.StockCheck, ((System.Text.Json.JsonElement)error.Details!["subject"]!).GetString());
            Assert.Equal(500, ((System.Text.Json.JsonElement)error.Details!["timeoutMs"]!).GetInt32());

            await using var assertDb = mssql.CreateDbContext(connectionString);
            Assert.Equal(0, await assertDb.Orders.CountAsync());
            Assert.Equal(0, await assertDb.OutboxMessages.CountAsync());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary><c>requestId</c> is on the wire because <c>asyncapi.yaml</c> declares it — carried and ignored (idempotent replay is the reliability feature's own acceptance criterion, out of scope here). Proves it does not break a normal placement.</summary>
    [Fact]
    public async Task OrdersCreate_ARequestIdOnTheWireIsCarriedButHasNoEffectOnThisFeaturesBehaviour()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_accept4_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);
        }

        using var host = BuildHost(connectionString);
        await host.StartAsync();
        try
        {
            await using var fulfillment = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            await WaitUntilOrdersCreateReachableAsync(caller, CancellationToken.None);

            var request = new OrdersCreateRequestPayload(
                RequestId: Guid.NewGuid(),
                OrderPersistenceTestSupport.RetailerCode,
                OrderPersistenceTestSupport.CompanyCode,
                OrderPersistenceTestSupport.Currency,
                Lines: [new OrdersCreateRequestLine(OrderPersistenceTestSupport.ProductCode1, 1, UnitPrice: 1_000, LineDiscount: null)],
                OrderDiscount: null,
                Notes: null);

            var replyMsg = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.OrdersCreate,
                RpcJson.Serialize(request),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);

            Assert.NotNull(replyMsg.Data);
            var reply = RpcJson.Deserialize<OrdersCreateReplyPayload>(replyMsg.Data!);
            Assert.Equal("placed", reply.Status);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// <see cref="IHost.StartAsync"/> awaits every <see cref="IHostedService.StartAsync"/>
    /// call, but <see cref="BackgroundService.StartAsync"/> returns as soon
    /// as its <c>ExecuteAsync</c> is SCHEDULED, not once the NATS
    /// subscription inside it has actually landed server-side — the same
    /// subscribe-side race <c>StandInFulfillmentStockCheckResponder</c>'s
    /// own probe exists to close, mirrored here for
    /// <see cref="OrdersCreateResponder"/>. Sends a request an unknown
    /// <c>retailerCode</c> deliberately makes cheap and side-effect-free
    /// (a <c>NOT_FOUND</c> reply, resolved before the stock check ever
    /// runs) and retries until SOME reply arrives. Found live: the OUTAGE
    /// test below has no stand-in to provide the incidental warm-up delay
    /// every other test's stand-in construction happened to supply, and
    /// failed once under load with <c>NatsNoRespondersException</c> — on
    /// <c>orders.create</c> itself, not on the stock check under test.
    /// </summary>
    private static async Task WaitUntilOrdersCreateReachableAsync(INatsConnection caller, CancellationToken cancellationToken)
    {
        var probe = new OrdersCreateRequestPayload(
            RequestId: null,
            RetailerCode: "NATS-PROBE-CONNECTIVITY",
            CompanyCode: "NATS-PROBE-CONNECTIVITY",
            Currency: "EUR",
            Lines: [new OrdersCreateRequestLine("NATS-PROBE-CONNECTIVITY", 1, null, null)],
            OrderDiscount: null,
            Notes: null);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var reply = await caller.RequestAsync<byte[], byte[]>(
                    RpcSubjects.OrdersCreate,
                    RpcJson.Serialize(probe),
                    replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromMilliseconds(200) },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

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
            }
        }

        throw new TimeoutException("orders.create responder never became reachable.");
    }

    private IHost BuildHost(string connectionString, int? stockCheckTimeoutMs = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddOrdersOutbox(options =>
        {
            options.ConnectionString = connectionString;
            // No real Kafka needed for this feature's own scope — the relay
            // is feature outbox_and_idempotency's own claim, already proven
            // in OutboxRelayTests. Disabled here so this suite depends on
            // nothing but NATS + MS-SQL.
            options.Relay.Enabled = false;
            options.Kafka.BootstrapServers = "127.0.0.1:1";
        });
        builder.Services.AddOrdersAcceptance(options =>
        {
            options.Nats.Url = nats.Url;
            if (stockCheckTimeoutMs is { } timeoutMs)
            {
                options.Nats.StockCheckTimeoutMs = timeoutMs;
            }
        });
        builder.Services.AddDispatcher(typeof(PlaceOrderCommand).Assembly);

        return builder.Build();
    }
}
