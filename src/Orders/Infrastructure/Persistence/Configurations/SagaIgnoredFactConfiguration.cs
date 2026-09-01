using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;

namespace OrderToCash.Orders.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_orders.saga_ignored_facts` (Databases doc §4.4). Index on
/// `correlation_id` — the operator lookup "what happened to this order's
/// facts".
/// </summary>
public sealed class SagaIgnoredFactConfiguration : IEntityTypeConfiguration<SagaIgnoredFact>
{
    public void Configure(EntityTypeBuilder<SagaIgnoredFact> builder)
    {
        builder.ToTable("saga_ignored_facts");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.EventId).HasColumnName("event_id");
        builder.Property(s => s.EventType).HasColumnName("event_type").HasMaxLength(60).IsRequired();
        builder.Property(s => s.OrderId).HasColumnName("order_id");
        builder.Property(s => s.CorrelationId).HasColumnName("correlation_id");
        builder.Property(s => s.ObservedStatus).HasColumnName("observed_status").HasMaxLength(20);
        builder.Property(s => s.ExpectedStatus).HasColumnName("expected_status").HasMaxLength(20);
        builder.Property(s => s.Marker).HasColumnName("marker").HasMaxLength(20).IsRequired();

        builder.Property(s => s.RecordedAt).HasColumnName("recorded_at").HasColumnType("datetime2(3)");

        builder.HasIndex(s => s.CorrelationId);
    }
}
