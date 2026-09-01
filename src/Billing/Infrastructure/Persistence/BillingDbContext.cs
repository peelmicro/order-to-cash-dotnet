using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Infrastructure.Persistence.Configurations;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;

namespace OrderToCash.Billing.Infrastructure.Persistence;

/// <summary>
/// The EF Core write model for `otc_billing` (Databases doc §6): buyer
/// credit limits and their hold/release ledger, invoices (INVOIC) and their
/// lines, payments, the `INV-######` allocation counter, and the
/// reliability tables shared with Orders, Fulfillment and Notifications
/// (§4.3). Lives entirely under <c>Infrastructure/</c>; nothing here is
/// reachable from <c>Domain/</c> — <see
/// cref="OrderToCash.Architecture.Tests.DomainPurityTests"/> fails the build
/// if it ever is.
/// </summary>
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<Credit> Credits => Set<Credit>();

    public DbSet<CreditItem> CreditItems => Set<CreditItem>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<InvoiceNumberSequence> InvoiceNumberSequences => Set<InvoiceNumberSequence>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CreditConfiguration());
        modelBuilder.ApplyConfiguration(new CreditItemConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceItemConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceNumberSequenceConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
    }
}
