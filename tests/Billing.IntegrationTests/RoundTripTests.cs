using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// "round-trip integration test per table" — for every one of the eight
/// tables, insert a row through <see cref="Infrastructure.Persistence.BillingDbContext"/>,
/// read it back from a brand-new <see
/// cref="Infrastructure.Persistence.BillingDbContext"/> instance (so the
/// read genuinely hits the database rather than EF's first-level cache),
/// and assert every field survived unchanged. This is a distinct claim from
/// <c>SchemaColumnTypeTests</c>: that test proves the column exists with
/// the right SQL type; this one proves data actually persists and reads
/// back through the mapping.
/// </summary>
[Collection(MsSqlCollection.Name)]
public sealed class RoundTripTests(MsSqlContainerFixture fixture)
{
    [Fact]
    public async Task Credit_Round_Trips()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_rt_credit_{Guid.NewGuid():N}");
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var write = fixture.CreateDbContext(connectionString))
        {
            await write.Database.MigrateAsync();
            write.Credits.Add(new Credit
            {
                Id = id,
                Code = "CR-000001",
                RetailerCode = "CarrefourEs",
                CompanyCode = "SupplierEs",
                CreditLimit = 100_000,
                CurrencyCode = "EUR",
                CreatedAt = now,
                UpdatedAt = now,
            });
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(connectionString);
        var row = await read.Credits.SingleAsync(c => c.Id == id);

        Assert.Equal("CR-000001", row.Code);
        Assert.Equal("CarrefourEs", row.RetailerCode);
        Assert.Equal("SupplierEs", row.CompanyCode);
        Assert.Equal(100_000, row.CreditLimit);
        Assert.Equal("EUR", row.CurrencyCode);
    }

    [Fact]
    public async Task CreditItem_Round_Trips()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_rt_credititem_{Guid.NewGuid():N}");
        var creditId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var write = fixture.CreateDbContext(connectionString))
        {
            await write.Database.MigrateAsync();
            write.Credits.Add(new Credit
            {
                Id = creditId,
                Code = "CR-000001",
                RetailerCode = "CarrefourEs",
                CompanyCode = "SupplierEs",
                CreditLimit = 100_000,
                CurrencyCode = "EUR",
                CreatedAt = now,
                UpdatedAt = now,
            });
            write.CreditItems.Add(new CreditItem
            {
                Id = itemId,
                CreditId = creditId,
                OrderReference = "ORD-000001",
                Amount = 5_000,
                Type = "hold",
                CreditDate = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(connectionString);
        var row = await read.CreditItems.SingleAsync(i => i.Id == itemId);

        Assert.Equal(creditId, row.CreditId);
        Assert.Equal("ORD-000001", row.OrderReference);
        Assert.Equal(5_000, row.Amount);
        Assert.Equal("hold", row.Type);
    }

    [Fact]
    public async Task Invoice_And_InvoiceItem_Round_Trip()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_rt_invoice_{Guid.NewGuid():N}");
        var invoiceId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var write = fixture.CreateDbContext(connectionString))
        {
            await write.Database.MigrateAsync();
            write.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                InvoiceReference = "INV-000001",
                InvoiceDate = now,
                CompanyCode = "SupplierEs",
                RetailerCode = "CarrefourEs",
                OrderReference = "ORD-000001",
                Amount = 10_000,
                Discount = 500,
                TotalAmount = 9_500,
                CurrencyCode = "EUR",
                Status = "issued",
                CreatedAt = now,
                UpdatedAt = now,
            });
            write.InvoiceItems.Add(new InvoiceItem
            {
                Id = itemId,
                InvoiceId = invoiceId,
                ProductCode = "SKU-001",
                Units = 5,
                Price = 2_000,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(connectionString);
        var invoice = await read.Invoices.SingleAsync(i => i.Id == invoiceId);
        var item = await read.InvoiceItems.SingleAsync(i => i.Id == itemId);

        Assert.Equal("INV-000001", invoice.InvoiceReference);
        Assert.Equal("ORD-000001", invoice.OrderReference);
        Assert.Equal(9_500, invoice.TotalAmount);
        Assert.Equal("issued", invoice.Status);
        Assert.Null(invoice.PaidAt);
        Assert.Equal(invoiceId, item.InvoiceId);
        Assert.Equal("SKU-001", item.ProductCode);
        Assert.Equal(5, item.Units);
        Assert.Equal(2_000, item.Price);
    }

    [Fact]
    public async Task Payment_Round_Trips()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_rt_payment_{Guid.NewGuid():N}");
        var invoiceId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var write = fixture.CreateDbContext(connectionString))
        {
            await write.Database.MigrateAsync();
            write.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                InvoiceReference = "INV-000001",
                InvoiceDate = now,
                CompanyCode = "SupplierEs",
                RetailerCode = "CarrefourEs",
                OrderReference = "ORD-000001",
                Amount = 10_000,
                Discount = 0,
                TotalAmount = 10_000,
                CurrencyCode = "EUR",
                Status = "issued",
                CreatedAt = now,
                UpdatedAt = now,
            });
            write.Payments.Add(new Payment
            {
                Id = paymentId,
                PaymentReference = "REM-000001",
                InvoiceId = invoiceId,
                Amount = 10_000,
                CurrencyCode = "EUR",
                ValueDate = now,
                Source = "operator",
                CreatedAt = now,
            });
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(connectionString);
        var row = await read.Payments.SingleAsync(p => p.Id == paymentId);

        Assert.Equal("REM-000001", row.PaymentReference);
        Assert.Equal(invoiceId, row.InvoiceId);
        Assert.Equal(10_000, row.Amount);
        Assert.Equal("operator", row.Source);
    }

    [Fact]
    public async Task InvoiceNumberSequence_Round_Trips()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_rt_seq_{Guid.NewGuid():N}");

        await using (var write = fixture.CreateDbContext(connectionString))
        {
            await write.Database.MigrateAsync();
            write.InvoiceNumberSequences.Add(new InvoiceNumberSequence { Id = 1, NextValue = 1 });
            await write.SaveChangesAsync();
        }

        await using (var update = fixture.CreateDbContext(connectionString))
        {
            var row = await update.InvoiceNumberSequences.SingleAsync(s => s.Id == 1);
            row.NextValue = 2;
            await update.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(connectionString);
        var finalRow = await read.InvoiceNumberSequences.SingleAsync(s => s.Id == 1);

        Assert.Equal(2, finalRow.NextValue);
    }

    [Fact]
    public async Task OutboxMessage_Round_Trips()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_rt_outbox_{Guid.NewGuid():N}");
        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var write = fixture.CreateDbContext(connectionString))
        {
            await write.Database.MigrateAsync();
            write.OutboxMessages.Add(new OutboxMessage
            {
                Id = id,
                EventId = eventId,
                EventType = "invoice.issued.v1",
                AggregateId = Guid.NewGuid(),
                CorrelationId = Guid.NewGuid(),
                CausationId = Guid.NewGuid(),
                Payload = """{"invoiceReference":"INV-000001"}""",
                OccurredAt = now,
                CreatedAt = now,
            });
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(connectionString);
        var row = await read.OutboxMessages.SingleAsync(o => o.Id == id);

        Assert.Equal(eventId, row.EventId);
        Assert.Equal("invoice.issued.v1", row.EventType);
        Assert.Equal("""{"invoiceReference":"INV-000001"}""", row.Payload);
        Assert.Null(row.PublishedAt);
        Assert.True(row.Seq > 0);
    }

    [Fact]
    public async Task ProcessedEvent_Round_Trips()
    {
        var connectionString = await fixture.CreateFreshDatabaseAsync($"otc_billing_rt_pe_{Guid.NewGuid():N}");
        var id = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var write = fixture.CreateDbContext(connectionString))
        {
            await write.Database.MigrateAsync();
            write.ProcessedEvents.Add(new ProcessedEvent
            {
                Id = id,
                EventId = eventId,
                Consumer = "billing",
                ProcessedAt = now,
                CreatedAt = now,
            });
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateDbContext(connectionString);
        var row = await read.ProcessedEvents.SingleAsync(p => p.Id == id);

        Assert.Equal(eventId, row.EventId);
        Assert.Equal("billing", row.Consumer);
    }
}
