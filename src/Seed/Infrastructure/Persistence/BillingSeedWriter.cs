using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Infrastructure.Persistence;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;
using OrderToCash.Contracts.Wire;
using OrderToCash.Seed.Domain.Data;
using OrderToCash.Seed.Domain.Deterministic;
using OrderToCash.Seed.Domain.Sagas;

namespace OrderToCash.Seed.Infrastructure.Persistence;

/// <summary>
/// Writes the Billing DB (<c>otc_billing</c>): a credit limit for every
/// retailer plus, per seeded saga, the append-only <c>credit_items</c>
/// ledger (hold -&gt; consume -&gt; release for completed orders; nothing at
/// all for the cancelled one), the
/// <c>invoices</c>/<c>invoice_items</c>/<c>payments</c> for completed
/// orders, and the already-published <c>outbox</c> rows — ported from #7's
/// <c>apps/seed/src/writers/billing-db.writer.ts</c>, reusing the real
/// <see cref="BillingDbContext"/>.
/// </summary>
public static class BillingSeedWriter
{
    public static string ConnectionString() => SeedDbConfig.BuildConnectionString("MSSQL_DB_BILLING", "otc_billing");

    public static BillingDbContext OpenDb(string connectionString)
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(connectionString).Options;
        return new BillingDbContext(options);
    }

    public static async Task SeedCreditsAsync(BillingDbContext db, CancellationToken cancellationToken = default)
    {
        var ts = MasterDataTimestamp.Value;

        foreach (var credit in Credits.All)
        {
            await db.UpsertAsync<Credit>(
                credit.Id,
                () => new Credit { Id = credit.Id, CreatedAt = ts },
                entity =>
                {
                    entity.Code = credit.Code;
                    entity.RetailerCode = credit.RetailerCode;
                    entity.CompanyCode = credit.CompanyCode;
                    entity.CreditLimit = (int)credit.CreditLimit;
                    entity.CurrencyCode = credit.CurrencyCode;
                    entity.UpdatedAt = ts;
                },
                cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task SeedSagasAsync(
        BillingDbContext db,
        IReadOnlyList<OrderSagaFixture>? sagas = null,
        CancellationToken cancellationToken = default)
    {
        sagas ??= SagaFixtures.All;

        foreach (var saga in sagas)
        {
            foreach (var entry in saga.CreditLedgerEntries)
            {
                await db.UpsertAsync<CreditItem>(
                    entry.Id,
                    () => new CreditItem { Id = entry.Id, CreatedAt = entry.CreditDate },
                    entity =>
                    {
                        entity.CreditId = entry.CreditId;
                        entity.OrderReference = entry.OrderReference;
                        entity.Amount = (int)entry.Amount;
                        entity.Type = entry.Type;
                        entity.CreditDate = entry.CreditDate;
                        entity.UpdatedAt = entry.CreditDate;
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            if (saga.Invoice is { } invoice)
            {
                await db.UpsertAsync<Invoice>(
                    invoice.Id,
                    () => new Invoice { Id = invoice.Id, CreatedAt = invoice.InvoiceDate },
                    entity =>
                    {
                        entity.InvoiceReference = invoice.InvoiceReference;
                        entity.InvoiceDate = invoice.InvoiceDate;
                        entity.CompanyCode = saga.CompanyCode;
                        entity.RetailerCode = saga.RetailerCode;
                        entity.OrderReference = saga.OrderReference;
                        entity.Amount = (int)invoice.Amount;
                        entity.Discount = (int)invoice.Discount;
                        entity.TotalAmount = (int)invoice.TotalAmount;
                        entity.CurrencyCode = saga.Currency;
                        entity.Status = invoice.Status;
                        entity.PaidAt = invoice.PaidAt;
                        entity.UpdatedAt = invoice.PaidAt;
                    },
                    cancellationToken).ConfigureAwait(false);

                foreach (var item in invoice.Items)
                {
                    var itemId = DeterministicId.Of($"order:{saga.Sequence}:invoice-item:{item.ProductCode}");

                    await db.UpsertAsync<InvoiceItem>(
                        itemId,
                        () => new InvoiceItem { Id = itemId, InvoiceId = invoice.Id, CreatedAt = invoice.InvoiceDate },
                        entity =>
                        {
                            entity.ProductCode = item.ProductCode;
                            entity.Units = item.Units;
                            entity.Price = (int)item.Price;
                            entity.UpdatedAt = invoice.InvoiceDate;
                        },
                        cancellationToken).ConfigureAwait(false);
                }

                var payment = invoice.Payment;
                await db.UpsertAsync<Payment>(
                    payment.Id,
                    () => new Payment { Id = payment.Id, CreatedAt = payment.ValueDate },
                    entity =>
                    {
                        entity.PaymentReference = payment.PaymentReference;
                        entity.InvoiceId = invoice.Id;
                        entity.Amount = (int)payment.Amount;
                        entity.CurrencyCode = saga.Currency;
                        entity.ValueDate = payment.ValueDate;
                        entity.Source = payment.Source;
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (var row in saga.BillingOutbox)
            {
                await UpsertOutboxAsync(db, row, cancellationToken).ConfigureAwait(false);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertOutboxAsync(BillingDbContext db, OutboxFixture row, CancellationToken cancellationToken)
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

    public sealed record RowCounts(int Credits, int CreditItems, int Invoices, int InvoiceItems, int Payments, int Outbox);

    public static async Task<RowCounts> CountRowsAsync(BillingDbContext db, CancellationToken cancellationToken = default) =>
        new(
            await db.Credits.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.CreditItems.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.Invoices.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.InvoiceItems.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.Payments.CountAsync(cancellationToken).ConfigureAwait(false),
            await db.OutboxMessages.CountAsync(cancellationToken).ConfigureAwait(false));
}
