using Microsoft.EntityFrameworkCore;
using OrderToCash.Notifications.Infrastructure.Persistence.Configurations;
using OrderToCash.Notifications.Infrastructure.Persistence.Entities;

namespace OrderToCash.Notifications.Infrastructure.Persistence;

/// <summary>
/// The EF Core write model for `otc_notifications` (Databases doc §7). The
/// Notifications service has no aggregate — it consumes facts and sends
/// emails through Mailtrap — so this context has exactly one table,
/// `processed_events`, and no outbox: this service produces no facts, only
/// consumes them, so a transactional-outbox pattern would have nothing to
/// guarantee. Lives entirely under <c>Infrastructure/</c>; nothing here is
/// reachable from <c>Domain/</c> — <see
/// cref="OrderToCash.Architecture.Tests.DomainPurityTests"/> fails the build
/// if it ever is.
/// </summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProcessedEventConfiguration());
    }
}
