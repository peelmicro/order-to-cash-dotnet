using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using OrderToCash.Seed.Domain.Data;
using OrderToCash.Seed.Domain.Deterministic;
using OrderToCash.Seed.Domain.Sagas;

namespace OrderToCash.Seed.Infrastructure.Persistence;

/// <summary>
/// Writes the Orders DB (<c>otc_orders</c>): the reference catalogue
/// (currencies, products, retailers, companies) plus, per seeded saga, the
/// <c>orders</c> row, its <c>order_items</c> and its already-published
/// <c>outbox</c> rows — ported from #7's
/// <c>apps/seed/src/writers/orders-db.writer.ts</c>, reusing the real
/// <see cref="OrdersDbContext"/> rather than a second, hand-rolled
/// connection shape.
/// </summary>
public static class OrdersSeedWriter
{
    public static string ConnectionString() => SeedDbConfig.BuildConnectionString("MSSQL_DB_ORDERS", "otc_orders");

    public static OrdersDbContext OpenDb(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>().UseSqlServer(connectionString).Options;
        return new OrdersDbContext(options);
    }

    public static async Task SeedMasterDataAsync(OrdersDbContext db, CancellationToken cancellationToken = default)
    {
        var ts = MasterDataTimestamp.Value;

        foreach (var currency in Currencies.All)
        {
            await db.UpsertAsync<Currency>(
                currency.Id,
                () => new Currency { Id = currency.Id, CreatedAt = ts },
                entity =>
                {
                    entity.Code = currency.Code;
                    entity.IsoNumber = currency.IsoNumber;
                    entity.Symbol = currency.Symbol;
                    entity.DecimalPoints = currency.DecimalPoints;
                    entity.UpdatedAt = ts;
                },
                cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var product in Products.All)
        {
            await db.UpsertAsync<Product>(
                product.Id,
                () => new Product { Id = product.Id, CreatedAt = ts },
                entity =>
                {
                    entity.Code = product.Code;
                    entity.Ean = product.Ean;
                    entity.Name = product.Name;
                    entity.Description = product.Description;
                    entity.Price = product.Price;
                    entity.CurrencyId = Currencies.IdByCode(product.CurrencyCode);
                    entity.DisabledAt = null;
                    entity.UpdatedAt = ts;
                },
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var retailer in Retailers.All)
        {
            await db.UpsertAsync<Retailer>(
                retailer.Id,
                () => new Retailer { Id = retailer.Id, CreatedAt = ts },
                entity =>
                {
                    entity.Code = retailer.Code;
                    entity.Name = retailer.Name;
                    entity.Country = retailer.Country;
                    entity.Vat = retailer.Vat;
                    entity.Gln = retailer.Gln;
                    entity.CurrencyId = Currencies.IdByCode(retailer.CurrencyCode);
                    entity.DisabledAt = null;
                    entity.UpdatedAt = ts;
                },
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var company in Companies.All)
        {
            await db.UpsertAsync<Company>(
                company.Id,
                () => new Company { Id = company.Id, CreatedAt = ts },
                entity =>
                {
                    entity.Code = company.Code;
                    entity.Name = company.Name;
                    entity.Country = company.Country;
                    entity.Vat = company.Vat;
                    entity.Gln = company.Gln;
                    entity.CurrencyId = Currencies.IdByCode(company.CurrencyCode);
                    entity.DisabledAt = null;
                    entity.UpdatedAt = ts;
                },
                cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task SeedSagasAsync(
        OrdersDbContext db,
        IReadOnlyList<OrderSagaFixture>? sagas = null,
        CancellationToken cancellationToken = default)
    {
        sagas ??= SagaFixtures.All;

        foreach (var saga in sagas)
        {
            var companyId = Companies.ByCode(saga.CompanyCode).Id;
            var retailerId = Retailers.ByCode(saga.RetailerCode).Id;
            var currencyId = Currencies.IdByCode(saga.Currency);
            var notes = saga.Status == "cancelled" ? "demo — compensation path (credit_rejected, .99 rule)" : null;

            await db.UpsertAsync<Order>(
                saga.OrderId,
                () => new Order { Id = saga.OrderId, CreatedAt = saga.OrderDate },
                entity =>
                {
                    entity.OrderReference = saga.OrderReference;
                    entity.OrderDate = saga.OrderDate;
                    entity.CompanyId = companyId;
                    entity.RetailerId = retailerId;
                    entity.CurrencyId = currencyId;
                    entity.InitialAmount = saga.InitialAmount;
                    entity.InitialDiscount = saga.InitialDiscount;
                    entity.TotalAmount = saga.TotalAmount;
                    entity.Status = saga.Status;
                    entity.CancellationReason = saga.CancellationReason;
                    entity.Notes = notes;
                    entity.UpdatedAt = saga.UpdatedAt;
                },
                cancellationToken).ConfigureAwait(false);

            foreach (var line in saga.Lines)
            {
                var itemId = DeterministicId.Of($"order:{saga.Sequence}:item:{line.ProductCode}");
                var productId = Products.ByCode(line.ProductCode).Id;

                await db.UpsertAsync<OrderItem>(
                    itemId,
                    () => new OrderItem { Id = itemId, OrderId = saga.OrderId, CreatedAt = saga.OrderDate },
                    entity =>
                    {
                        entity.ProductId = productId;
                        entity.Description = line.Description;
                        entity.Price = line.UnitPrice;
                        entity.Quantity = line.Quantity;
                        entity.Discount = line.LineDiscount;
                        entity.UpdatedAt = saga.OrderDate;
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (var row in saga.OrdersOutbox)
            {
                await UpsertOutboxAsync(db, row, cancellationToken).ConfigureAwait(false);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertOutboxAsync(OrdersDbContext db, OutboxFixture row, CancellationToken cancellationToken)
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

    public sealed record RowCounts(int Currencies, int Products, int Retailers, int Companies, int Orders, int OrderItems, int Outbox);

    public static async Task<RowCounts> CountRowsAsync(OrdersDbContext db, CancellationToken cancellationToken = default) =>
        new(
            await db.Currencies.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.Products.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.Retailers.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.Companies.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.Orders.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.OrderItems.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.OutboxMessages.CountAsync(cancellationToken).ConfigureAwait(false));
}
