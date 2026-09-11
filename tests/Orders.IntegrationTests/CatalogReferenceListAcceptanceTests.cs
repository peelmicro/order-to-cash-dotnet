using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using OrderToCash.Orders.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Feature <c>orders_catalog_responder</c>'s acceptance, over the REAL
/// transport: a real NATS broker, a real MS-SQL database, the real
/// <c>catalog.reference.list</c> responder resolved through the SAME
/// <c>AddOrdersOutbox</c> + <c>AddOrdersAcceptance</c> + <c>AddDispatcher</c>
/// composition <c>Program.cs</c> uses — the identical harness shape
/// <c>OrdersCreateAcceptanceTests</c> already established for
/// <c>orders.create</c>. The Gateway (feature 25) does not exist yet, so
/// this suite proves the responder at the NATS boundary directly, which is
/// this feature's actual deliverable — "<c>GET /catalog/*</c> through the
/// Gateway" (acceptance bullet 1) is a later feature's claim to prove, not
/// this one's.
/// </summary>
[Collection(NatsCollection.Name)]
public sealed class CatalogReferenceListAcceptanceTests(NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    [Fact]
    public async Task CatalogReferenceList_KindsOmitted_ReturnsAllFourCollectionsFromTheSeededReferenceData()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_catalog_accept1_{Guid.NewGuid():N}");
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
            await WaitUntilCatalogReferenceListReachableAsync(caller, CancellationToken.None);

            var request = new CatalogReferenceListRequestPayload(Kinds: null, IncludeDisabled: null);
            var replyMsg = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.CatalogReferenceList,
                RpcJson.Serialize(request),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);

            Assert.NotNull(replyMsg.Data);
            var reply = RpcJson.Deserialize<CatalogReferenceListReplyPayload>(replyMsg.Data!);

            Assert.NotNull(reply.Products);
            Assert.NotNull(reply.Retailers);
            Assert.NotNull(reply.Companies);
            Assert.NotNull(reply.Currencies);

            // Field-by-field against the seeded fixture, not merely
            // non-empty — proves the responder is the same reference-data
            // lookup PlaceOrderCommandHandler resolves the retailer/company/
            // product codes against, and not a second, differently-shaped
            // read path.
            var product = Assert.Single(reply.Products!, p => p.Code == OrderPersistenceTestSupport.ProductCode1);
            Assert.Equal("1000000000017", product.Ean);
            Assert.Equal("Product One", product.Name);
            Assert.Equal("First product", product.Description);
            Assert.Equal(1_000, product.Price);
            Assert.Equal(OrderPersistenceTestSupport.Currency, product.Currency);
            Assert.True(product.Enabled);

            // review-style field-swap trap (OrdersCreateAcceptanceTests'
            // "three DISTINCT amounts" rule, applied here): retailer and
            // company carry DIFFERENT names, VAT numbers and GLNs, so a
            // responder mapping bug that swapped a field WITHIN one party's
            // own payload (e.g. Code into Name's slot) fails on its own
            // terms rather than by coincidence.
            var retailer = Assert.Single(reply.Retailers!, r => r.Code == OrderPersistenceTestSupport.RetailerCode);
            Assert.Equal("Test Retailer", retailer.Name);
            Assert.Equal("FR", retailer.Country);
            Assert.Equal("FR00000000000", retailer.Vat);
            Assert.Equal(OrderPersistenceTestSupport.BuyerGlnValue, retailer.Gln);
            Assert.Equal(OrderPersistenceTestSupport.Currency, retailer.Currency);
            Assert.True(retailer.Enabled);

            var company = Assert.Single(reply.Companies!, c => c.Code == OrderPersistenceTestSupport.CompanyCode);
            Assert.Equal("Test Company", company.Name);
            Assert.Equal("FR", company.Country);
            Assert.Equal("FR00000000001", company.Vat);
            Assert.Equal(OrderPersistenceTestSupport.SupplierGlnValue, company.Gln);
            Assert.Equal(OrderPersistenceTestSupport.Currency, company.Currency);
            Assert.True(company.Enabled);

            var currency = Assert.Single(reply.Currencies!, c => c.Code == OrderPersistenceTestSupport.Currency);
            Assert.Equal("978", currency.IsoNumber);
            Assert.Equal("€", currency.Symbol);
            Assert.Equal(2, currency.DecimalPoints);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task CatalogReferenceList_KindsCarriesOnlyProducts_TheReplyOmitsTheOtherThreeCollectionsFromTheWireEntirely()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_catalog_accept2_{Guid.NewGuid():N}");
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
            await WaitUntilCatalogReferenceListReachableAsync(caller, CancellationToken.None);

            var request = new CatalogReferenceListRequestPayload(Kinds: ["products"], IncludeDisabled: null);
            var replyMsg = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.CatalogReferenceList,
                RpcJson.Serialize(request),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);

            Assert.NotNull(replyMsg.Data);

            // Raw JSON, not the typed record — a typed deserialisation would
            // turn an ABSENT key and a PRESENT-but-null key into the exact
            // same C# `null`, which is precisely the distinction "only the
            // requested collections are present" (asyncapi.yaml) is about.
            using var document = JsonDocument.Parse(replyMsg.Data!);
            Assert.True(document.RootElement.TryGetProperty("products", out _));
            Assert.False(document.RootElement.TryGetProperty("retailers", out _));
            Assert.False(document.RootElement.TryGetProperty("companies", out _));
            Assert.False(document.RootElement.TryGetProperty("currencies", out _));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// Review defect D1: <c>OrdersCreateResponder.cs:124</c>'s
    /// <c>var includeDisabled = request.IncludeDisabled ?? false;</c> is the
    /// ONLY wire-level translation of the request's <c>includeDisabled</c>
    /// field, and nothing previously drove it end to end — the shared
    /// <see cref="OrderPersistenceTestSupport.SeedReferenceDataAsync"/>
    /// fixture (other suites depend on it staying minimal) has no disabled
    /// row, and all other requests in this file pass
    /// <c>IncludeDisabled: null</c>. This test seeds ONE disabled product
    /// LOCAL to itself — never widening the shared fixture — and drives BOTH
    /// spellings of the flag through the real NATS wire, asserting the two
    /// replies DIFFER on that one row. Forcing the responder's translation to
    /// a constant <c>true</c> fails the "omitted" assertion below; forcing it
    /// to a constant <c>false</c> fails the "true" assertion — the two
    /// directions the review required.
    /// </summary>
    [Fact]
    public async Task CatalogReferenceList_IncludeDisabledTrueVersusOmitted_TheDisabledProductAppearsOnlyWhenRequested()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_catalog_accept5_{Guid.NewGuid():N}");
        Guid currencyId;
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
            await OrderPersistenceTestSupport.SeedReferenceDataAsync(seedDb);

            currencyId = await seedDb.Currencies
                .Where(c => c.Code == OrderPersistenceTestSupport.Currency)
                .Select(c => c.Id)
                .SingleAsync();

            var now = DateTime.UtcNow;
            seedDb.Products.Add(new Product
            {
                Id = Guid.NewGuid(),
                Code = "PROD-DISABLED",
                Ean = "1000000000031",
                Name = "Disabled Product",
                Description = "Withdrawn from sale",
                Price = 250,
                CurrencyId = currencyId,
                DisabledAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await seedDb.SaveChangesAsync();
        }

        using var host = BuildHost(connectionString);
        await host.StartAsync();
        try
        {
            await using var caller = new NatsConnection(new NatsOpts { Url = nats.Url });
            await WaitUntilCatalogReferenceListReachableAsync(caller, CancellationToken.None);

            // Direction 1: IncludeDisabled TRUE must surface the disabled
            // row, carrying enabled: false. Dies if the responder ignores
            // the wire flag and always filters disabled rows out (the
            // constant-false mutation).
            var trueRequest = new CatalogReferenceListRequestPayload(Kinds: ["products"], IncludeDisabled: true);
            var trueReplyMsg = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.CatalogReferenceList,
                RpcJson.Serialize(trueRequest),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);
            Assert.NotNull(trueReplyMsg.Data);
            var trueReply = RpcJson.Deserialize<CatalogReferenceListReplyPayload>(trueReplyMsg.Data!);
            var disabledRow = Assert.Single(trueReply.Products!, p => p.Code == "PROD-DISABLED");
            Assert.False(disabledRow.Enabled);

            // Direction 2: IncludeDisabled omitted (null, the schema's
            // documented default) must NOT surface it. Dies if the
            // responder ignores the wire flag and always includes disabled
            // rows (the constant-true mutation).
            var omittedRequest = new CatalogReferenceListRequestPayload(Kinds: ["products"], IncludeDisabled: null);
            var omittedReplyMsg = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.CatalogReferenceList,
                RpcJson.Serialize(omittedRequest),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);
            Assert.NotNull(omittedReplyMsg.Data);
            var omittedReply = RpcJson.Deserialize<CatalogReferenceListReplyPayload>(omittedReplyMsg.Data!);
            Assert.DoesNotContain(omittedReply.Products!, p => p.Code == "PROD-DISABLED");

            // The explicit `false` spelling of the same default, on the same
            // connection — proves the responder does not merely happen to
            // treat `null` specially.
            var explicitFalseRequest = new CatalogReferenceListRequestPayload(Kinds: ["products"], IncludeDisabled: false);
            var explicitFalseReplyMsg = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.CatalogReferenceList,
                RpcJson.Serialize(explicitFalseRequest),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);
            Assert.NotNull(explicitFalseReplyMsg.Data);
            var explicitFalseReply = RpcJson.Deserialize<CatalogReferenceListReplyPayload>(explicitFalseReplyMsg.Data!);
            Assert.DoesNotContain(explicitFalseReply.Products!, p => p.Code == "PROD-DISABLED");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task CatalogReferenceList_ARequestWithAnUnknownKind_RefusesAsValidationFailedNotInternalError()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_catalog_accept3_{Guid.NewGuid():N}");
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
            await WaitUntilCatalogReferenceListReachableAsync(caller, CancellationToken.None);

            var request = new CatalogReferenceListRequestPayload(Kinds: ["widgets"], IncludeDisabled: null);
            var replyMsg = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.CatalogReferenceList,
                RpcJson.Serialize(request),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);

            Assert.NotNull(replyMsg.Data);
            var error = RpcJson.Deserialize<RpcErrorPayload>(replyMsg.Data!);

            Assert.Equal("VALIDATION_FAILED", error.Code);
            Assert.NotEqual("INTERNAL_ERROR", error.Code);
            Assert.Contains("widgets", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// Both subjects this responder now answers really do work concurrently
    /// on the same NATS connection: an <c>orders.create</c> call and a
    /// <c>catalog.reference.list</c> call, both answered without either
    /// blocking the other's loop.
    /// </summary>
    [Fact]
    public async Task CatalogReferenceList_TheOrdersCreateSubjectOnTheSameResponderStillAnswersConcurrently()
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_catalog_accept4_{Guid.NewGuid():N}");
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
            await WaitUntilCatalogReferenceListReachableAsync(caller, CancellationToken.None);

            var catalogRequest = new CatalogReferenceListRequestPayload(Kinds: ["currencies"], IncludeDisabled: null);
            var catalogReply = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.CatalogReferenceList,
                RpcJson.Serialize(catalogRequest),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);
            Assert.NotNull(catalogReply.Data);
            var catalogPayload = RpcJson.Deserialize<CatalogReferenceListReplyPayload>(catalogReply.Data!);
            Assert.NotNull(catalogPayload.Currencies);

            // orders.create still answers a NOT_FOUND (unknown retailer) —
            // cheap, side-effect-free, and enough to prove the OTHER loop on
            // this same responder is alive too.
            var ordersRequest = new OrdersCreateRequestPayload(
                RequestId: null,
                RetailerCode: "UNKNOWN-RETAILER",
                CompanyCode: OrderPersistenceTestSupport.CompanyCode,
                Currency: OrderPersistenceTestSupport.Currency,
                Lines: [new OrdersCreateRequestLine(OrderPersistenceTestSupport.ProductCode1, 1, null, null)],
                OrderDiscount: null,
                Notes: null);
            var ordersReply = await caller.RequestAsync<byte[], byte[]>(
                RpcSubjects.OrdersCreate,
                RpcJson.Serialize(ordersRequest),
                replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromSeconds(10) },
                cancellationToken: CancellationToken.None);
            Assert.NotNull(ordersReply.Data);
            var ordersError = RpcJson.Deserialize<RpcErrorPayload>(ordersReply.Data!);
            Assert.Equal("NOT_FOUND", ordersError.Code);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// Backlog id 63 — this used to be its own unpaced 100-attempt loop
    /// (paced only by the per-attempt request timeout), which
    /// <see cref="NatsNoRespondersException"/>'s IMMEDIATE nature let expire
    /// in about a millisecond. Delegates to
    /// <see cref="SagaIntegrationTestSupport.WaitUntilReachableAsync"/> —
    /// the SAME generic retry/catch loop, already paced and already proven
    /// deterministically correct by
    /// <c>OrdersCancelResponderReadinessRaceTests</c> — rather than carrying
    /// a second, separately-armed copy of the identical mechanism.
    /// </summary>
    private static Task WaitUntilCatalogReferenceListReachableAsync(INatsConnection caller, CancellationToken cancellationToken)
    {
        var probe = new CatalogReferenceListRequestPayload(Kinds: ["currencies"], IncludeDisabled: null);
        return SagaIntegrationTestSupport.WaitUntilReachableAsync(caller, RpcSubjects.CatalogReferenceList, RpcJson.Serialize(probe), cancellationToken);
    }

    private IHost BuildHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddOrdersOutbox(options =>
        {
            options.ConnectionString = connectionString;
            options.Relay.Enabled = false;
            // Mechanism-2 classification: this host's StopAsync/Dispose
            // sites below do NOT need the group-clearance wait —
            // "127.0.0.1:1" is deliberately unreachable, so SagaFactsConsumer
            // can never actually join a real "orders.saga" group on ANY
            // broker; mechanism 2 needs a real shared broker to cross a
            // test boundary, which is structurally impossible here.
            options.Kafka.BootstrapServers = "127.0.0.1:1";
        });
        builder.Services.AddOrdersAcceptance(options => options.Nats.Url = nats.Url);
        builder.Services.AddDispatcher(typeof(PlaceOrderCommand).Assembly);

        return builder.Build();
    }
}
