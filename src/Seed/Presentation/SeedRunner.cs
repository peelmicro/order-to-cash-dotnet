using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using OrderToCash.Seed.Infrastructure.Mongo;
using OrderToCash.Seed.Infrastructure.Persistence;

namespace OrderToCash.Seed.Presentation;

/// <summary>
/// The one-shot seed job's orchestration — ported from #7's
/// <c>apps/seed/src/index.ts</c>: applies the three MS-SQL services'
/// committed EF Core migrations (so a cold compose stack works), seeds
/// master data + the fabricated saga history across the three MS-SQL
/// databases and the MongoDB read model, then prints a row-count summary.
/// Idempotent — every write goes through <see cref="EfUpsert"/> or a Mongo
/// <c>replaceOne(upsert: true)</c>, so a second run changes nothing.
/// </summary>
public static class SeedRunner
{
    public static async Task<SeedSummary> RunAsync(CancellationToken cancellationToken = default)
    {
        var ordersConnectionString = OrdersSeedWriter.ConnectionString();
        var fulfillmentConnectionString = FulfillmentSeedWriter.ConnectionString();
        var billingConnectionString = BillingSeedWriter.ConnectionString();
        var (mongoConnectionUri, mongoDatabaseName) = SeedMongoConfig.Load();

        await using var ordersDb = OrdersSeedWriter.OpenDb(ordersConnectionString);
        await using var fulfillmentDb = FulfillmentSeedWriter.OpenDb(fulfillmentConnectionString);
        await using var billingDb = BillingSeedWriter.OpenDb(billingConnectionString);

        Console.WriteLine("[seed] applying migrations (orders, fulfillment, billing)...");
        await ordersDb.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await fulfillmentDb.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await billingDb.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        var mongoClient = new MongoClient(mongoConnectionUri);
        var mongoDatabase = mongoClient.GetDatabase(mongoDatabaseName);

        Console.WriteLine("[seed] writing master data (currencies, products, retailers, companies, stock, credits)...");
        await OrdersSeedWriter.SeedMasterDataAsync(ordersDb, cancellationToken).ConfigureAwait(false);
        await FulfillmentSeedWriter.SeedStockAsync(fulfillmentDb, cancellationToken).ConfigureAwait(false);
        await BillingSeedWriter.SeedCreditsAsync(billingDb, cancellationToken).ConfigureAwait(false);

        Console.WriteLine("[seed] writing sample saga history (5 completed + 1 cancelled)...");
        await OrdersSeedWriter.SeedSagasAsync(ordersDb, cancellationToken: cancellationToken).ConfigureAwait(false);
        await FulfillmentSeedWriter.SeedSagasAsync(fulfillmentDb, cancellationToken: cancellationToken).ConfigureAwait(false);
        await BillingSeedWriter.SeedSagasAsync(billingDb, cancellationToken: cancellationToken).ConfigureAwait(false);
        await MongoSeedWriter.SeedTimelinesAsync(mongoDatabase, cancellationToken: cancellationToken).ConfigureAwait(false);

        var ordersCounts = await OrdersSeedWriter.CountRowsAsync(ordersDb, cancellationToken).ConfigureAwait(false);
        var fulfillmentCounts = await FulfillmentSeedWriter.CountRowsAsync(fulfillmentDb, cancellationToken).ConfigureAwait(false);
        var billingCounts = await BillingSeedWriter.CountRowsAsync(billingDb, cancellationToken).ConfigureAwait(false);
        var mongoCount = await MongoSeedWriter.CountTimelinesAsync(mongoDatabase, cancellationToken).ConfigureAwait(false);

        return new SeedSummary(ordersCounts, fulfillmentCounts, billingCounts, mongoCount);
    }
}

public sealed record SeedSummary(
    OrdersSeedWriter.RowCounts Orders,
    FulfillmentSeedWriter.RowCounts Fulfillment,
    BillingSeedWriter.RowCounts Billing,
    long OrderTimelines);
