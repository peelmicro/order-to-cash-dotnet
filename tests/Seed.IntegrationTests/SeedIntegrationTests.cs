using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using OrderToCash.Billing.Infrastructure.Persistence;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;
using OrderToCash.Fulfillment.Infrastructure.Persistence;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using OrderToCash.Seed.Infrastructure.Mongo;
using OrderToCash.Seed.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Seed.IntegrationTests;

/// <summary>
/// Feature seed_job — the full round trip against real MS-SQL and MongoDB
/// containers: migrations, row counts against the dataset's own declared
/// sizes, idempotency (running twice is a no-op), and the
/// <c>order_timeline</c> document shape.
/// </summary>
[Collection(SeedContainersCollection.Name)]
public sealed class SeedIntegrationTests(SeedContainersFixture fixture)
{
    private async Task<(OrdersDbContext Orders, FulfillmentDbContext Fulfillment, BillingDbContext Billing, IMongoDatabase Mongo)>
        CreateFreshStackAsync(string suffix)
    {
        var ordersConnectionString = await fixture.CreateFreshDatabaseAsync($"otc_orders_seed_{suffix}");
        var fulfillmentConnectionString = await fixture.CreateFreshDatabaseAsync($"otc_fulfillment_seed_{suffix}");
        var billingConnectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_seed_{suffix}");

        var ordersDb = fixture.CreateOrdersDbContext(ordersConnectionString);
        var fulfillmentDb = fixture.CreateFulfillmentDbContext(fulfillmentConnectionString);
        var billingDb = fixture.CreateBillingDbContext(billingConnectionString);

        await ordersDb.Database.MigrateAsync();
        await fulfillmentDb.Database.MigrateAsync();
        await billingDb.Database.MigrateAsync();

        var mongo = fixture.CreateMongoDatabase($"otc_read_model_seed_{suffix}");

        return (ordersDb, fulfillmentDb, billingDb, mongo);
    }

    private static async Task RunSeedAsync(OrdersDbContext ordersDb, FulfillmentDbContext fulfillmentDb, BillingDbContext billingDb, IMongoDatabase mongo)
    {
        await OrdersSeedWriter.SeedMasterDataAsync(ordersDb);
        await FulfillmentSeedWriter.SeedStockAsync(fulfillmentDb);
        await BillingSeedWriter.SeedCreditsAsync(billingDb);

        await OrdersSeedWriter.SeedSagasAsync(ordersDb);
        await FulfillmentSeedWriter.SeedSagasAsync(fulfillmentDb);
        await BillingSeedWriter.SeedSagasAsync(billingDb);
        await MongoSeedWriter.SeedTimelinesAsync(mongo);
    }

    [Fact]
    public async Task Migrations_Apply_Against_Fresh_Databases()
    {
        var (ordersDb, fulfillmentDb, billingDb, _) = await CreateFreshStackAsync(Guid.NewGuid().ToString("N"));
        await using var o = ordersDb;
        await using var f = fulfillmentDb;
        await using var b = billingDb;

        Assert.True(await o.Database.CanConnectAsync());
        Assert.True(await f.Database.CanConnectAsync());
        Assert.True(await b.Database.CanConnectAsync());
    }

    /// <summary>
    /// Feature seed_job acceptance: "same currencies, products, retailers,
    /// companies, GLNs, credit limits and stock as #7" (row counts) and
    /// "sample completed orders and one cancelled order".
    /// </summary>
    [Fact]
    public async Task Row_Counts_Match_The_Datasets_Own_Declared_Sizes()
    {
        var (ordersDb, fulfillmentDb, billingDb, mongo) = await CreateFreshStackAsync(Guid.NewGuid().ToString("N"));
        await using var o = ordersDb;
        await using var f = fulfillmentDb;
        await using var b = billingDb;

        await RunSeedAsync(o, f, b, mongo);

        var ordersCounts = await OrdersSeedWriter.CountRowsAsync(o);
        Assert.Equal(3, ordersCounts.Currencies);
        Assert.Equal(12, ordersCounts.Products);
        Assert.Equal(7, ordersCounts.Retailers);
        Assert.Equal(22, ordersCounts.Companies);
        Assert.Equal(6, ordersCounts.Orders);
        Assert.Equal(5 * 2 + 1, ordersCounts.OrderItems); // 5 completed sagas x 2 lines + 1 cancelled saga x 1 line
        Assert.Equal((5 * 3) + (1 * 2), ordersCounts.Outbox); // completed: placed+confirmed+completed; cancelled: placed+cancelled

        var fulfillmentCounts = await FulfillmentSeedWriter.CountRowsAsync(f);
        Assert.Equal(5, fulfillmentCounts.Despatches); // one per completed saga
        Assert.Equal((5 * 2) + (1 * 2), fulfillmentCounts.Outbox); // completed: reserved+despatched (2); cancelled: reserved+released (2)
        // review_seed_job.md D3: was `> 0`; the live and #7-oracle value is
        // exactly 215 (saga-derived pairs + the per-product baseline for
        // every company the sample sagas never touch — StockSeed.cs).
        Assert.Equal(215, fulfillmentCounts.Stock);
        Assert.Equal(11, fulfillmentCounts.Reservations); // one per order line (5 completed sagas x 2 + 1 cancelled saga x 1)
        Assert.Equal(10, fulfillmentCounts.DespatchItems); // 5 completed sagas x 2 lines each; the cancelled saga has no despatch

        var billingCounts = await BillingSeedWriter.CountRowsAsync(b);
        Assert.Equal(154, billingCounts.Credits); // 7 retailers * 22 companies
        Assert.Equal(5, billingCounts.Invoices); // one per completed saga
        Assert.Equal(5, billingCounts.Payments);
        Assert.Equal(5 * 3, billingCounts.CreditItems); // hold, consume, release per completed saga
        Assert.Equal(10, billingCounts.InvoiceItems); // 5 completed sagas x 2 lines each; the cancelled saga has no invoice
        Assert.Equal((5 * 4) + (1 * 1), billingCounts.Outbox); // completed: approved+issued+received+released (4); cancelled: rejected (1)

        var mongoCount = await MongoSeedWriter.CountTimelinesAsync(mongo);
        Assert.Equal(6, mongoCount);
    }

    [Fact]
    public async Task Running_The_Seed_Twice_Is_A_No_Op()
    {
        var (ordersDb, fulfillmentDb, billingDb, mongo) = await CreateFreshStackAsync(Guid.NewGuid().ToString("N"));
        await using var o = ordersDb;
        await using var f = fulfillmentDb;
        await using var b = billingDb;

        await RunSeedAsync(o, f, b, mongo);

        var firstOrdersCounts = await OrdersSeedWriter.CountRowsAsync(o);
        var firstFulfillmentCounts = await FulfillmentSeedWriter.CountRowsAsync(f);
        var firstBillingCounts = await BillingSeedWriter.CountRowsAsync(b);
        var firstMongoCount = await MongoSeedWriter.CountTimelinesAsync(mongo);
        var firstChecksum = await ComputeChecksumAsync(o, f, b, mongo);

        // Second run, same target stack.
        await RunSeedAsync(o, f, b, mongo);

        var secondOrdersCounts = await OrdersSeedWriter.CountRowsAsync(o);
        var secondFulfillmentCounts = await FulfillmentSeedWriter.CountRowsAsync(f);
        var secondBillingCounts = await BillingSeedWriter.CountRowsAsync(b);
        var secondMongoCount = await MongoSeedWriter.CountTimelinesAsync(mongo);
        var secondChecksum = await ComputeChecksumAsync(o, f, b, mongo);

        Assert.Equal(firstOrdersCounts, secondOrdersCounts);
        Assert.Equal(firstFulfillmentCounts, secondFulfillmentCounts);
        Assert.Equal(firstBillingCounts, secondBillingCounts);
        Assert.Equal(firstMongoCount, secondMongoCount);
        Assert.Equal(firstChecksum, secondChecksum);
    }

    /// <summary>
    /// The <c>order_timeline</c> documents round-trip and carry every §8
    /// field with the right types.
    /// </summary>
    [Fact]
    public async Task Order_Timeline_Documents_Carry_Every_Field_With_The_Right_Types()
    {
        var (ordersDb, fulfillmentDb, billingDb, mongo) = await CreateFreshStackAsync(Guid.NewGuid().ToString("N"));
        await using var o = ordersDb;
        await using var f = fulfillmentDb;
        await using var b = billingDb;

        await RunSeedAsync(o, f, b, mongo);

        var collection = MongoSeedWriter.Collection(mongo);
        var documents = await collection.Find(FilterDefinition<OrderTimelineDocument>.Empty).ToListAsync();

        Assert.Equal(6, documents.Count);

        var completed = documents.Single(d => d.Status == "completed" && d.OrderReference == "ORD-000001");
        Assert.Equal(completed.Id, completed.OrderId);
        Assert.NotNull(completed.OrderDate);
        Assert.NotNull(completed.Retailer);
        Assert.Equal("CarrefourEs", completed.Retailer!.Code);
        Assert.NotNull(completed.Company);
        Assert.Equal("IBERFOODS", completed.Company!.Code);
        Assert.Null(completed.CancellationReason);
        Assert.NotNull(completed.Currency);
        Assert.NotNull(completed.Totals);
        Assert.True(completed.Totals!.TotalAmount > 0);
        Assert.NotEmpty(completed.Items);
        Assert.NotNull(completed.References);
        Assert.NotNull(completed.References!.DespatchReference);
        Assert.NotNull(completed.References.InvoiceReference);
        Assert.NotNull(completed.References.PaymentReference);
        Assert.Equal(9, completed.Events.Count);
        Assert.True(completed.HeaderComplete);
        Assert.NotEmpty(completed.UpdatedAt);
        Assert.Equal(98, completed.StatusRank);
        Assert.Equal(2, completed.TimelineOrderVersion);
        Assert.Equal(9, completed.ProcessedEventKeys.Count);
        Assert.All(completed.ProcessedEventKeys, key => Assert.StartsWith("projector:", key, StringComparison.Ordinal));
        Assert.All(completed.Events, e => Assert.NotEqual(Guid.Empty.ToString(), e.CausationId));

        var cancelled = documents.Single(d => d.Status == "cancelled");
        Assert.Equal("credit_rejected", cancelled.CancellationReason);
        Assert.Null(cancelled.References!.DespatchReference);
        Assert.Null(cancelled.References.InvoiceReference);
        Assert.Null(cancelled.References.PaymentReference);
        Assert.Equal(5, cancelled.Events.Count);
        // The cancelled order's timeline shows the compensation sequence
        // (release, then cancel) — specs/shared/saga.md §4.2 Path B.
        Assert.Equal(
            ["order.placed.v1", "stock.reserved.v1", "credit.rejected.v1", "stock.released.v1", "order.cancelled.v1"],
            [.. cancelled.Events.Select(e => e.EventType)]);
        Assert.Equal(99, cancelled.StatusRank);
        Assert.NotNull(cancelled.Events.First(e => e.EventType == "credit.rejected.v1").Detail);
    }

    /// <summary>
    /// review_seed_job.md D1 (BLOCKING) — the six live <c>order_timeline</c>
    /// documents, asserted field-by-field against
    /// <c>OracleFixtures/order_timeline_from_number7.json</c>: #7's OWN
    /// <c>apps/seed/src/writers/mongo.writer.ts#toTimelineDocument</c>,
    /// executed over #7's own <c>SAGAS</c>
    /// (<c>node --experimental-transform-types</c>, #7's source files
    /// copied verbatim with only <c>@otc/shared-kernel</c> and
    /// <c>mongodb</c> repointed — the same technique
    /// <see cref="DeterministicParityTests"/>-equivalent oracle values used,
    /// and the same technique the reviewer used independently). This is
    /// what
    /// <see cref="Order_Timeline_Documents_Carry_Every_Field_With_The_Right_Types"/>
    /// was missing: THAT test only checked presence/shape (non-null,
    /// non-zero, count) — it survived both of the reviewer's mutation
    /// probes (blank every <c>causationId</c>; corrupt every total). THIS
    /// test compares actual values, including <c>causationId</c> on every
    /// event, every total, every item field, party name/gln, every
    /// timestamp, and every event's summary/detail — so both probes (and a
    /// third, on <c>items[]</c>) fail here. See progress/impl_seed_job.md's
    /// arming table for the forced-rebuild proof.
    /// </summary>
    [Fact]
    public async Task Order_Timeline_Documents_Match_The_Values_Number7s_ToTimelineDocument_Produced()
    {
        var (ordersDb, fulfillmentDb, billingDb, mongo) = await CreateFreshStackAsync(Guid.NewGuid().ToString("N"));
        await using var o = ordersDb;
        await using var f = fulfillmentDb;
        await using var b = billingDb;

        await RunSeedAsync(o, f, b, mongo);

        var collection = MongoSeedWriter.Collection(mongo);
        var liveByReference = (await collection.Find(FilterDefinition<OrderTimelineDocument>.Empty).ToListAsync())
            .ToDictionary(d => d.OrderReference!, d => d, StringComparer.Ordinal);

        using var oracle = LoadOracleTimelineDocuments();
        var oracleDocuments = oracle.RootElement.EnumerateArray().ToList();
        Assert.Equal(6, oracleDocuments.Count);

        foreach (var expected in oracleDocuments)
        {
            var orderReference = expected.GetProperty("orderReference").GetString()!;
            Assert.True(liveByReference.TryGetValue(orderReference, out var actual), $"no live document for {orderReference}");
            AssertTimelineDocumentMatchesOracle(expected, actual!, orderReference);
        }
    }

    private static JsonDocument LoadOracleTimelineDocuments()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "OracleFixtures", "order_timeline_from_number7.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static string? GetNullableString(JsonElement element, string propertyName)
    {
        var property = element.GetProperty(propertyName);
        return property.ValueKind == JsonValueKind.Null ? null : property.GetString();
    }

    private static void AssertTimelineDocumentMatchesOracle(JsonElement expected, OrderTimelineDocument actual, string orderReference)
    {
        Assert.Equal(expected.GetProperty("_id").GetString(), actual.Id);
        Assert.Equal(expected.GetProperty("orderId").GetString(), actual.OrderId);
        Assert.Equal(orderReference, actual.OrderReference);
        Assert.Equal(expected.GetProperty("orderDate").GetString(), actual.OrderDate);
        Assert.Equal(expected.GetProperty("status").GetString(), actual.Status);
        Assert.Equal(GetNullableString(expected, "cancellationReason"), actual.CancellationReason);
        Assert.Equal(GetNullableString(expected, "currency"), actual.Currency);

        var retailer = expected.GetProperty("retailer");
        Assert.NotNull(actual.Retailer);
        Assert.Equal(retailer.GetProperty("code").GetString(), actual.Retailer!.Code);
        Assert.Equal(retailer.GetProperty("name").GetString(), actual.Retailer.Name);
        Assert.Equal(retailer.GetProperty("gln").GetString(), actual.Retailer.Gln);

        var company = expected.GetProperty("company");
        Assert.NotNull(actual.Company);
        Assert.Equal(company.GetProperty("code").GetString(), actual.Company!.Code);
        Assert.Equal(company.GetProperty("name").GetString(), actual.Company.Name);
        Assert.Equal(company.GetProperty("gln").GetString(), actual.Company.Gln);

        var totals = expected.GetProperty("totals");
        Assert.NotNull(actual.Totals);
        Assert.Equal(totals.GetProperty("initialAmount").GetInt64(), actual.Totals!.InitialAmount);
        Assert.Equal(totals.GetProperty("initialDiscount").GetInt64(), actual.Totals.InitialDiscount);
        Assert.Equal(totals.GetProperty("totalAmount").GetInt64(), actual.Totals.TotalAmount);

        var expectedItems = expected.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(expectedItems.Count, actual.Items.Count);
        for (var i = 0; i < expectedItems.Count; i++)
        {
            var expectedItem = expectedItems[i];
            var actualItem = actual.Items[i];
            Assert.Equal(expectedItem.GetProperty("productCode").GetString(), actualItem.ProductCode);
            Assert.Equal(expectedItem.GetProperty("name").GetString(), actualItem.Name);
            Assert.Equal(expectedItem.GetProperty("quantity").GetInt32(), actualItem.Quantity);
            Assert.Equal(expectedItem.GetProperty("unitPrice").GetInt64(), actualItem.UnitPrice);
            Assert.Equal(expectedItem.GetProperty("lineDiscount").GetInt64(), actualItem.LineDiscount);
        }

        var references = expected.GetProperty("references");
        Assert.NotNull(actual.References);
        Assert.Equal(GetNullableString(references, "despatchReference"), actual.References!.DespatchReference);
        Assert.Equal(GetNullableString(references, "invoiceReference"), actual.References.InvoiceReference);
        Assert.Equal(GetNullableString(references, "paymentReference"), actual.References.PaymentReference);

        var expectedEvents = expected.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(expectedEvents.Count, actual.Events.Count);
        for (var i = 0; i < expectedEvents.Count; i++)
        {
            var expectedEvent = expectedEvents[i];
            var actualEvent = actual.Events[i];
            Assert.Equal(expectedEvent.GetProperty("eventId").GetString(), actualEvent.EventId);
            Assert.Equal(expectedEvent.GetProperty("eventType").GetString(), actualEvent.EventType);
            Assert.Equal(expectedEvent.GetProperty("occurredAt").GetString(), actualEvent.OccurredAt);
            Assert.Equal(expectedEvent.GetProperty("summary").GetString(), actualEvent.Summary);
            // review_seed_job.md D1, probe B: this is the assertion the
            // original test never made — every causal edge, checked.
            Assert.Equal(expectedEvent.GetProperty("causationId").GetString(), actualEvent.CausationId);

            if (expectedEvent.TryGetProperty("detail", out var expectedDetail))
            {
                Assert.NotNull(actualEvent.Detail);
                foreach (var property in expectedDetail.EnumerateObject())
                {
                    Assert.True(
                        actualEvent.Detail!.TryGetValue(property.Name, out var actualValue),
                        $"event {actualEvent.EventType} is missing detail key '{property.Name}'");
                    var expectedString = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.GetRawText();
                    var actualString = Convert.ToString(actualValue, CultureInfo.InvariantCulture);
                    Assert.Equal(expectedString, actualString);
                }
            }
            else
            {
                Assert.Null(actualEvent.Detail);
            }
        }

        Assert.Equal(expected.GetProperty("headerComplete").GetBoolean(), actual.HeaderComplete);
        Assert.Equal(expected.GetProperty("updatedAt").GetString(), actual.UpdatedAt);
        Assert.Equal(expected.GetProperty("statusRank").GetInt32(), actual.StatusRank);
        Assert.Equal(expected.GetProperty("timelineOrderVersion").GetInt32(), actual.TimelineOrderVersion);

        var expectedProcessedEventKeys = expected.GetProperty("processedEventKeys").EnumerateArray()
            .Select(e => e.GetString()!)
            .ToList();
        Assert.Equal(expectedProcessedEventKeys, actual.ProcessedEventKeys);
    }

    private static async Task<string> ComputeChecksumAsync(
        OrdersDbContext ordersDb,
        FulfillmentDbContext fulfillmentDb,
        BillingDbContext billingDb,
        IMongoDatabase mongo)
    {
        var builder = new StringBuilder();

        foreach (var row in await ordersDb.Orders.AsNoTracking().OrderBy(o => o.Id).ToListAsync())
        {
            builder.Append($"order|{row.Id}|{row.OrderReference}|{row.Status}|{row.TotalAmount}|{row.CancellationReason}\n");
        }

        foreach (var row in await ordersDb.OutboxMessages.AsNoTracking().OrderBy(x => x.Id).ToListAsync())
        {
            builder.Append($"orders-outbox|{row.Id}|{row.EventId}|{row.EventType}|{row.Payload}\n");
        }

        foreach (var row in await fulfillmentDb.Stocks.AsNoTracking().OrderBy(s => s.Id).ToListAsync())
        {
            builder.Append($"stock|{row.Id}|{row.CompanyCode}|{row.ProductCode}|{row.Units}|{row.ReservedUnits}\n");
        }

        foreach (var row in await fulfillmentDb.Reservations.AsNoTracking().OrderBy(r => r.Id).ToListAsync())
        {
            builder.Append($"reservation|{row.Id}|{row.Status}|{row.Units}\n");
        }

        foreach (var row in await billingDb.Credits.AsNoTracking().OrderBy(c => c.Id).ToListAsync())
        {
            builder.Append($"credit|{row.Id}|{row.Code}|{row.RetailerCode}|{row.CompanyCode}|{row.CreditLimit}\n");
        }

        foreach (var row in await billingDb.CreditItems.AsNoTracking().OrderBy(c => c.Id).ToListAsync())
        {
            builder.Append($"credit-item|{row.Id}|{row.CreditId}|{row.Type}|{row.Amount}\n");
        }

        var collection = MongoSeedWriter.Collection(mongo);
        var documents = await collection.Find(FilterDefinition<OrderTimelineDocument>.Empty)
            .SortBy(d => d.Id)
            .ToListAsync();

        foreach (var document in documents)
        {
            builder.Append($"timeline|{document.Id}|{document.Status}|{document.Events.Count}|{document.UpdatedAt}\n");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Feature outbox_and_idempotency, task H6: the seed pre-publishes every
    /// row on purpose (<c>OutboxFixture.PublishedAt</c> is non-nullable) —
    /// this proves the real relay agrees, against the ORDERS write model,
    /// with a fake publisher that must never be called.
    /// </summary>
    /// <remarks>
    /// <c>OutboxRelay</c> is typed to <c>OrdersDbContext</c> specifically
    /// (design.md §5.1) — Fulfillment's and Billing's own relays are
    /// features 17-22's, not this feature's or seed_job's (design.md §11:
    /// "No Fulfillment, Billing, Notifications or Projector code"). Rather
    /// than build per-service relay adapters out of scope to satisfy this
    /// task's literal wording, the SAME invariant is proven for
    /// Fulfillment and Billing the way <c>OI11</c> already does — reading
    /// the identically-shaped <c>outbox</c> table directly — and only the
    /// Orders half runs through the real <see cref="OutboxRelay"/> class.
    /// Recorded as a deliberate, narrow deviation in
    /// progress/impl_outbox_and_idempotency.md.
    /// </remarks>
    [Fact]
    public async Task TheRelayFindsNoUnpublishedRecordInAnySeededWriteModel()
    {
        var (ordersDb, fulfillmentDb, billingDb, mongo) = await CreateFreshStackAsync(Guid.NewGuid().ToString("N"));
        await using var o = ordersDb;
        await using var f = fulfillmentDb;
        await using var b = billingDb;

        await RunSeedAsync(o, f, b, mongo);

        var publisher = new NeverCalledFactPublisher();
        var relay = new OutboxRelay(
            o,
            publisher,
            new FixedClock(DateTimeOffset.UtcNow),
            Options.Create(new OutboxRelayOptions { BatchSize = 100 }),
            NullLogger<OutboxRelay>.Instance);

        var result = await relay.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, result.Claimed);
        Assert.Equal(0, result.Published);
        Assert.Equal(0, publisher.CallCount);

        // Fulfillment and Billing: same invariant, read directly — see the
        // remarks above for why this half does not go through OutboxRelay.
        Assert.Equal(0, await f.OutboxMessages.CountAsync(row => row.PublishedAt == null));
        Assert.Equal(0, await b.OutboxMessages.CountAsync(row => row.PublishedAt == null));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class NeverCalledFactPublisher : IFactPublisher
    {
        public int CallCount { get; private set; }

        public Task PublishAsync(IReadOnlyList<PublishableFact> facts, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
