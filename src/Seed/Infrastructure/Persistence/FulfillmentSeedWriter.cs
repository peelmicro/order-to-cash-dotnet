using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Contracts.Wire;
using OrderToCash.Fulfillment.Infrastructure.Persistence;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;
using OrderToCash.Seed.Domain.Data;
using OrderToCash.Seed.Domain.Deterministic;
using OrderToCash.Seed.Domain.Sagas;

namespace OrderToCash.Seed.Infrastructure.Persistence;

/// <summary>
/// Writes the Fulfillment DB (<c>otc_fulfillment</c>): initial stock plus,
/// per seeded saga, the <c>reservations</c> (<c>consumed</c> for completed
/// orders, <c>released</c> for the cancelled one), the
/// <c>despatches</c>/<c>despatch_items</c> for completed orders, and the
/// already-published <c>outbox</c> rows — ported from #7's
/// <c>apps/seed/src/writers/fulfillment-db.writer.ts</c>, reusing the real
/// <see cref="FulfillmentDbContext"/>.
/// </summary>
public static class FulfillmentSeedWriter
{
    public static string ConnectionString() => SeedDbConfig.BuildConnectionString("MSSQL_DB_FULFILLMENT", "otc_fulfillment");

    public static FulfillmentDbContext OpenDb(string connectionString)
    {
        var options = new DbContextOptionsBuilder<FulfillmentDbContext>().UseSqlServer(connectionString).Options;
        return new FulfillmentDbContext(options);
    }

    public static async Task SeedStockAsync(FulfillmentDbContext db, CancellationToken cancellationToken = default)
    {
        var ts = MasterDataTimestamp.Value;

        foreach (var item in StockCatalog.All)
        {
            await db.UpsertAsync<Stock>(
                item.Id,
                () => new Stock { Id = item.Id, CreatedAt = ts },
                entity =>
                {
                    entity.CompanyCode = item.CompanyCode;
                    entity.ProductCode = item.ProductCode;
                    entity.Units = item.Units;
                    entity.ReservedUnits = item.ReservedUnits;
                    entity.LowStockThreshold = item.LowStockThreshold;
                    entity.UpdatedAt = ts;
                },
                cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task SeedSagasAsync(
        FulfillmentDbContext db,
        IReadOnlyList<OrderSagaFixture>? sagas = null,
        CancellationToken cancellationToken = default)
    {
        sagas ??= SagaFixtures.All;

        foreach (var saga in sagas)
        {
            foreach (var reservation in saga.Reservations)
            {
                var stockId = SagaFixtures.StockRowId(reservation.CompanyCode, reservation.ProductCode);

                await db.UpsertAsync<Reservation>(
                    reservation.Id,
                    () => new Reservation { Id = reservation.Id, CreatedAt = reservation.CreatedAt },
                    entity =>
                    {
                        entity.StockId = stockId;
                        entity.CompanyCode = reservation.CompanyCode;
                        entity.RetailerCode = reservation.RetailerCode;
                        entity.ProductCode = reservation.ProductCode;
                        entity.OrderReference = saga.OrderReference;
                        entity.Units = reservation.Units;
                        entity.Status = reservation.Status;
                        entity.UpdatedAt = reservation.UpdatedAt;
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            if (saga.Despatch is { } despatch)
            {
                await db.UpsertAsync<Despatch>(
                    despatch.Id,
                    () => new Despatch { Id = despatch.Id, CreatedAt = despatch.DespatchDate },
                    entity =>
                    {
                        entity.DespatchReference = despatch.DespatchReference;
                        entity.DespatchDate = despatch.DespatchDate;
                        entity.CompanyCode = despatch.CompanyCode;
                        entity.RetailerCode = despatch.RetailerCode;
                        entity.OrderReference = saga.OrderReference;
                        entity.UpdatedAt = despatch.DespatchDate;
                    },
                    cancellationToken).ConfigureAwait(false);

                foreach (var item in despatch.Items)
                {
                    var itemId = DeterministicId.Of($"order:{saga.Sequence}:despatch-item:{item.ProductCode}");

                    await db.UpsertAsync<DespatchItem>(
                        itemId,
                        () => new DespatchItem { Id = itemId, DespatchId = despatch.Id, CreatedAt = despatch.DespatchDate },
                        entity =>
                        {
                            entity.ProductCode = item.ProductCode;
                            entity.Units = item.Units;
                            entity.UpdatedAt = despatch.DespatchDate;
                        },
                        cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (var row in saga.FulfillmentOutbox)
            {
                await UpsertOutboxAsync(db, row, cancellationToken).ConfigureAwait(false);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertOutboxAsync(FulfillmentDbContext db, OutboxFixture row, CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(row.Payload, row.Payload.GetType(), JsonWire.Options);

        await db.UpsertAsync<OutboxMessage>(
            row.Id,
            () => new OutboxMessage { Id = row.Id, CreatedAt = row.OccurredAt },
            entity =>
            {
                entity.EventId = row.EventId;
                entity.EventType = row.EventType;
                entity.AggregateId = row.AggregateId;
                entity.CorrelationId = row.CorrelationId;
                entity.CausationId = row.CausationId;
                entity.Payload = payloadJson;
                entity.OccurredAt = row.OccurredAt;
                entity.PublishedAt = row.PublishedAt;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public sealed record RowCounts(int Stock, int Reservations, int Despatches, int DespatchItems, int Outbox);

    public static async Task<RowCounts> CountRowsAsync(FulfillmentDbContext db, CancellationToken cancellationToken = default) =>
        new(
            await db.Stocks.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.Reservations.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.Despatches.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.DespatchItems.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.OutboxMessages.CountAsync(cancellationToken).ConfigureAwait(false));
}
