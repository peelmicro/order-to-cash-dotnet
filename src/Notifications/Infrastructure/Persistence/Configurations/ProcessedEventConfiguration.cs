using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Notifications.Infrastructure.Persistence.Entities;

namespace OrderToCash.Notifications.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_notifications.processed_events` (Databases doc §7 — byte-identical
/// to `otc_orders`, `otc_fulfillment` and `otc_billing`, §4.3). Unique index
/// `(event_id, consumer)` — a Kafka redelivery of a fact already recorded
/// here must be rejected, not silently duplicated, which is what makes a
/// duplicate email send impossible even under a consumer-group replay.
/// Configuration copied verbatim from
/// `OrderToCash.Orders.Infrastructure.Persistence.Configurations.ProcessedEventConfiguration`.
/// </summary>
public sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("processed_events");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.EventId).HasColumnName("event_id");
        builder.Property(p => p.Consumer).HasColumnName("consumer").HasMaxLength(50).IsRequired();
        builder.HasIndex(p => new { p.EventId, p.Consumer }).IsUnique();

        builder.Property(p => p.ProcessedAt).HasColumnName("processed_at").HasColumnType("datetime2(3)");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
    }
}
