using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Infrastructure.Persistence.Configurations;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;

namespace OrderToCash.Orders.Infrastructure.Persistence;

/// <summary>
/// The EF Core write model for `otc_orders` (Databases doc §4): the seeded
/// reference catalogues (§4.1), the <c>Order</c> aggregate's persistence
/// rows (§4.2), the reliability tables shared with Fulfillment and Billing
/// (§4.3), and the saga orchestrator's durable state — unique to this
/// database (§4.4). Lives entirely under <c>Infrastructure/</c>; nothing
/// here is reachable from <c>Domain/</c> — <see
/// cref="OrderToCash.Architecture.Tests.DomainPurityTests"/> fails the build
/// if it ever is.
/// </summary>
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Currency> Currencies => Set<Currency>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Retailer> Retailers => Set<Retailer>();

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<OrderNumberSequence> OrderNumberSequences => Set<OrderNumberSequence>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    public DbSet<SagaCommand> SagaCommands => Set<SagaCommand>();

    public DbSet<SagaIgnoredFact> SagaIgnoredFacts => Set<SagaIgnoredFact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CurrencyConfiguration());
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new RetailerConfiguration());
        modelBuilder.ApplyConfiguration(new CompanyConfiguration());
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderItemConfiguration());
        modelBuilder.ApplyConfiguration(new OrderNumberSequenceConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
        modelBuilder.ApplyConfiguration(new SagaCommandConfiguration());
        modelBuilder.ApplyConfiguration(new SagaIgnoredFactConfiguration());
    }
}
