using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Configurations;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

namespace OrderToCash.Fulfillment.Infrastructure.Persistence;

/// <summary>
/// The EF Core write model for `otc_fulfillment` (Databases doc §5): stock
/// per (company, product), reservations, despatch advices (DESADV) and
/// their lines, the `DES-######` allocation counter, and the reliability
/// tables shared with Orders and Billing (§4.3). Lives entirely under
/// <c>Infrastructure/</c>; nothing here is reachable from <c>Domain/</c> —
/// <see cref="OrderToCash.Architecture.Tests.DomainPurityTests"/> fails the
/// build if it ever is.
/// </summary>
public sealed class FulfillmentDbContext(DbContextOptions<FulfillmentDbContext> options) : DbContext(options)
{
    public DbSet<Stock> Stocks => Set<Stock>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<Despatch> Despatches => Set<Despatch>();

    public DbSet<DespatchItem> DespatchItems => Set<DespatchItem>();

    public DbSet<DespatchNumberSequence> DespatchNumberSequences => Set<DespatchNumberSequence>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new StockConfiguration());
        modelBuilder.ApplyConfiguration(new ReservationConfiguration());
        modelBuilder.ApplyConfiguration(new DespatchConfiguration());
        modelBuilder.ApplyConfiguration(new DespatchItemConfiguration());
        modelBuilder.ApplyConfiguration(new DespatchNumberSequenceConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
    }
}
