using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;

namespace OrderToCash.Billing.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_billing.outbox` (Databases doc §4.3 — byte-identical to
/// `otc_orders` and `otc_fulfillment`; feature db_billing's cross-context
/// parity test guards this). Indexes: `(published_at, seq)` is the relay's
/// poll index; `(published_at, occurred_at)` serves the outbox-lag metric.
/// `Seq` is `bigint IDENTITY(1,1)`, never assigned by the application.
/// Configuration copied verbatim from
/// `OrderToCash.Orders.Infrastructure.Persistence.Configurations.OutboxMessageConfiguration`
/// and
/// `OrderToCash.Fulfillment.Infrastructure.Persistence.Configurations.OutboxMessageConfiguration`,
/// not re-derived — this feature's task instructions, so the reliability
/// tables stay byte-identical and the cross-context parity test has nothing
/// to find.
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");

        builder.Property(o => o.EventId).HasColumnName("event_id");
        builder.HasIndex(o => o.EventId).IsUnique();

        builder.Property(o => o.EventType).HasColumnName("event_type").HasMaxLength(60).IsRequired();
        builder.Property(o => o.AggregateId).HasColumnName("aggregate_id");
        builder.Property(o => o.CorrelationId).HasColumnName("correlation_id");
        builder.Property(o => o.CausationId).HasColumnName("causation_id");

        // MS-SQL has no `json` column type — #7's `json` becomes `nvarchar(max)`,
        // which preserves insertion order (CLAUDE.md's JSON-wire-shape rule).
        builder.Property(o => o.Payload).HasColumnName("payload").HasColumnType("nvarchar(max)").IsRequired();

        builder.Property(o => o.OccurredAt).HasColumnName("occurred_at").HasColumnType("datetime2(3)");
        builder.Property(o => o.PublishedAt).HasColumnName("published_at").HasColumnType("datetime2(3)");
        builder.Property(o => o.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");

        builder.Property(o => o.Seq)
            .HasColumnName("seq")
            .ValueGeneratedOnAdd()
            .UseIdentityColumn(1, 1);
        builder.HasIndex(o => o.Seq).IsUnique();

        builder.Property(o => o.TraceParent).HasColumnName("trace_parent").HasMaxLength(64);

        // The relay's poll index: WHERE published_at IS NULL ORDER BY seq.
        builder.HasIndex(o => new { o.PublishedAt, o.Seq });

        // The outbox-lag metric.
        builder.HasIndex(o => new { o.PublishedAt, o.OccurredAt });
    }
}
