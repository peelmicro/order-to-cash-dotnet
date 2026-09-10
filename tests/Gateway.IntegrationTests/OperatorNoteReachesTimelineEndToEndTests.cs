using System.Net.Http.Headers;
using System.Net.Http.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;
using NATS.Client.Core;
using OrderToCash.Orders;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using OrderToCash.Orders.Presentation.Rpc;
using OrderToCash.Projector;
using OrderToCash.Projector.Infrastructure.Messaging.Consumers;
using OrderToCash.SharedKernel;
using Xunit;
using DomainOrder = OrderToCash.Orders.Domain.Order;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// Feature <c>operator_note_reaches_the_timeline</c>, acceptance bullet 1 —
/// the walk #7's own reviewer named as owed for this exact gap (N4's shape,
/// reused): <c>POST /orders/{id}/cancel</c> through the REAL Gateway, over a
/// REAL NATS RPC round trip to the REAL Orders <c>orders.cancel</c>
/// responder (<see cref="OrdersHost.CreateBuilder"/>, unmodified — the same
/// precedent <see cref="FulfillmentStockEndToEndTests"/> already
/// establishes for booting another service's real host from this project),
/// through a REAL <c>order.cancelled.v1</c> fact on Kafka, consumed by the
/// REAL, unmodified <see cref="ProjectorHost"/>
/// (<see cref="StreamProjectorEndToEndTests"/>'s own precedent), landing on
/// a REAL MongoDB timeline document. No stand-in on any of the four hops.
/// </summary>
/// <remarks>
/// <b>Test-only, and named as the brief that dispatched this feature asked:</b>
/// this file adds a <c>ProjectReference</c> to <c>src/Orders/Orders.csproj</c>
/// in <c>Gateway.IntegrationTests.csproj</c> — <c>src/Gateway</c> itself is
/// untouched, exactly the "test-only change" pattern
/// <c>FulfillmentStockEndToEndTests</c> (Fulfillment) and
/// <c>StreamProjectorEndToEndTests</c> (Projector) already establish for
/// the other two services this project boots.
///
/// <b>Why the order is seeded directly rather than placed through
/// <c>POST /orders</c>:</b> placing an order for real requires a live
/// <c>fulfillment.stock.check</c> NATS responder (R31, synchronous, before
/// anything is persisted), which is out of THIS feature's bounded scope
/// (<c>src/Contracts/</c>, <c>src/Orders/</c>, <c>src/Projector/</c>) to
/// stand up. The seed below uses the REAL <see cref="IOrderRepository"/>/
/// <see cref="IUnitOfWork"/>/<see cref="DomainOrder.Place"/> resolved from
/// the REAL, running <c>OrdersHost</c>'s own DI container — the identical
/// code path <c>PlaceOrderCommandHandler</c> itself uses once the stock
/// check has passed — so the CANCEL path under test (the one this feature
/// changed) runs entirely unmodified and for real; only the PRECONDITION
/// (an existing <c>placed</c> order) is established by a shortcut.
/// </remarks>
[Collection(OperatorNoteEndToEndCollection.Name)]
public sealed class OperatorNoteReachesTimelineEndToEndTests(
    KafkaContainerFixture kafka, MsSqlContainerFixture mssql, NatsContainerFixture nats, MongoContainerFixture mongo)
{
    private const string RetailerCode = "RETAILER-NOTE";
    private const string CompanyCode = "COMPANY-NOTE";
    private const string Currency = "EUR";
    private const string ProductCode = "PROD-NOTE-1";
    private static readonly GLN _buyerGln = new("4006381333931");
    private static readonly GLN _supplierGln = new("5001234567890");

    private async Task<(IHost Host, string ConnectionString)> StartOrdersAsync(string suffix)
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_note_e2e_{suffix}_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<OrdersDbContext>().UseSqlServer(connectionString).Options;
        await using (var seedDb = new OrdersDbContext(options))
        {
            await seedDb.Database.MigrateAsync();
            await SeedReferenceDataAsync(seedDb);
        }

        var builder = OrdersHost.CreateBuilder(
            args: [],
            configureOutbox: o =>
            {
                o.ConnectionString = connectionString;
                o.Kafka.BootstrapServers = kafka.BootstrapServers;
                o.Relay.PollIntervalMs = 100;
            },
            configureAcceptance: o => o.Nats.Url = nats.Url,
            configureSaga: o =>
            {
                o.Kafka.BootstrapServers = kafka.BootstrapServers;
                o.Kafka.PollTimeoutMs = 200;
            });

        var host = builder.Build();
        await host.StartAsync();

        await using var probe = new NatsConnection(new NatsOpts { Url = nats.Url });
        await WaitUntilOrdersCancelReachableAsync(probe);

        return (host, connectionString);
    }

    /// <summary>
    /// Same paced-retry shape <c>SagaIntegrationTestSupport.WaitUntilReachableAsync</c>
    /// already establishes (backlog id 63) — <see cref="NatsNoRespondersException"/>
    /// is the server's IMMEDIATE sentinel and does not wait out the
    /// request's own timeout, so an unpaced loop can exhaust its whole
    /// budget in about a millisecond.
    /// </summary>
    private static async Task WaitUntilOrdersCancelReachableAsync(INatsConnection connection)
    {
        var probe = RpcJson.Serialize(new OrdersCancelRequestPayload(Guid.NewGuid(), OrderReference: null, "operator_cancelled", Note: null));

        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                var reply = await connection.RequestAsync<byte[], byte[]>(
                    RpcSubjects.OrdersCancel, probe, replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromMilliseconds(200) });
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

            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        }

        throw new TimeoutException("'orders.cancel' never became reachable.");
    }

    private static async Task SeedReferenceDataAsync(OrdersDbContext db)
    {
        var now = DateTime.UtcNow;
        var currencyId = Guid.NewGuid();

        db.Currencies.Add(new Currency { Id = currencyId, Code = Currency, IsoNumber = "978", Symbol = "€", DecimalPoints = 2, CreatedAt = now, UpdatedAt = now });
        db.Retailers.Add(new Retailer { Id = Guid.NewGuid(), Code = RetailerCode, Name = "Note Retailer", Country = "FR", Vat = "FR00000000010", Gln = _buyerGln.Value, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        db.Companies.Add(new Company { Id = Guid.NewGuid(), Code = CompanyCode, Name = "Note Company", Country = "FR", Vat = "FR00000000011", Gln = _supplierGln.Value, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        db.Products.Add(new Product { Id = Guid.NewGuid(), Code = ProductCode, Ean = "1000000000123", Name = "Note Product", Description = "For the operator-note e2e test", Price = 1_000, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });

        await db.SaveChangesAsync();
    }

    /// <summary>Places an order for real, through the SAME <see cref="IOrderRepository"/>/<see cref="IUnitOfWork"/> the responder's own command handlers use — see this class's own remarks for why <c>POST /orders</c> itself is bypassed.</summary>
    private static async Task<Guid> SeedPlacedOrderAsync(IHost ordersHost, long orderSequence)
    {
        using var scope = ordersHost.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var now = DateTimeOffset.UtcNow;
        var order = DomainOrder.Place(
            orderReference: new OrderNumber(orderSequence),
            orderDate: now,
            retailerCode: RetailerCode,
            buyerGln: _buyerGln,
            companyCode: CompanyCode,
            supplierGln: _supplierGln,
            currency: Currency,
            lines: [new OrderLineRequest(ProductCode, "Note product", new Quantity(1), new Money(1_000, Currency), Money.Zero(Currency))],
            notes: null,
            occurredAt: now,
            causationId: UniqueId.New());

        await unitOfWork.ExecuteAsync(
            async ct =>
            {
                await repository.AddAsync(order, ct).ConfigureAwait(false);
                await repository.SaveChangesAsync(ct).ConfigureAwait(false);
                return 0;
            },
            CancellationToken.None);

        return order.Id.Value;
    }

    private async Task<IHost> StartProjectorAsync(string mongoDatabase)
    {
        var builder = ProjectorHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Kafka.PollTimeoutMs = 200;
                options.Nats.Url = nats.Url;
                options.Mongo.ConnectionUri = mongo.ConnectionString;
                options.Mongo.Database = mongoDatabase;
            });

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private async Task CreateTopicIfMissingAsync(string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 6, ReplicationFactor = 1 }]);
        }
        catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
        {
        }
    }

    private static async Task<string> LoginAsync(GatewayTestHost gateway)
    {
        var response = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponseModel>();
        return body!.AccessToken;
    }

    /// <summary>
    /// Acceptance bullet 1 — the exact supplied note, bracketed (CLAUDE.md's
    /// provenance rule — never merely "a" note), reaching a REAL Mongo
    /// timeline document's <c>events[].detail.note</c> after a genuine
    /// <c>POST /orders/{id}/cancel</c> round trip through all four real
    /// services.
    /// </summary>
    [Fact(Timeout = 120_000)]
    public async Task PostOrdersCancelWithANote_ThroughTheRealFourServiceChain_LandsOnTheRealMongoTimelineEntry()
    {
        await CreateTopicIfMissingAsync(ProjectorFactTopics.OrdersFacts);
        await CreateTopicIfMissingAsync(ProjectorFactTopics.FulfillmentFacts);
        await CreateTopicIfMissingAsync(ProjectorFactTopics.BillingFacts);

        var (ordersHost, _) = await StartOrdersAsync("note-e2e");
        var mongoDatabase = $"otc_read_model_note_e2e_{Guid.NewGuid():N}";
        var projectorHost = await StartProjectorAsync(mongoDatabase);
        try
        {
            var orderId = await SeedPlacedOrderAsync(ordersHost, orderSequence: 500001);

            await using var gateway = await GatewayTestHost.StartAsync(options =>
            {
                options.Nats.Url = nats.Url;
                options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
                options.Mongo.Database = "otc_read_model_note_e2e_gateway_unused";
            });

            var token = await LoginAsync(gateway);
            gateway.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            const string note = "Cancelled end-to-end: buyer called to cancel before despatch.";
            var response = await gateway.Client.PostAsJsonAsync($"/orders/{orderId}/cancel", new { note });
            Assert.Equal((System.Net.HttpStatusCode)202, response.StatusCode);

            var collection = mongo.FreshCollection(mongoDatabase);
            var doc = await PollForCancelledDocumentAsync(collection, orderId, TimeSpan.FromSeconds(60));

            var events = doc["events"].AsBsonArray;
            var cancelledEntry = events.Select(e => e.AsBsonDocument).Single(e => e["eventType"].AsString == "order.cancelled.v1");
            Assert.Equal(note, cancelledEntry["detail"]["note"].AsString);
        }
        finally
        {
            await projectorHost.StopAsync();
            projectorHost.Dispose();
            await ordersHost.StopAsync();
            ordersHost.Dispose();
        }
    }

    private static async Task<BsonDocument> PollForCancelledDocumentAsync(IMongoCollection<BsonDocument> collection, Guid orderId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var doc = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", orderId.ToString("D"))).FirstOrDefaultAsync();
            if (doc is not null && doc["events"].AsBsonArray.Any(e => e.AsBsonDocument["eventType"].AsString == "order.cancelled.v1"))
            {
                return doc;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Order {orderId} never gained an order.cancelled.v1 timeline entry within {timeout}.");
    }

    private sealed record LoginResponseModel(string AccessToken, string TokenType, int ExpiresIn);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OperatorNoteEndToEndCollection :
    ICollectionFixture<KafkaContainerFixture>, ICollectionFixture<MsSqlContainerFixture>,
    ICollectionFixture<NatsContainerFixture>, ICollectionFixture<MongoContainerFixture>
{
    public const string Name = "GatewayOperatorNoteEndToEnd";
}
