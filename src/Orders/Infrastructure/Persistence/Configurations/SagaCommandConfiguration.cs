using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;

namespace OrderToCash.Orders.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_orders.saga_commands` (Databases doc §4.4). Indexes: unique
/// `(order_id, command)` — a step can never owe the same command twice;
/// `(status, created_at)` and `(status, next_attempt_at)` — the sweeper's
/// two claim predicates (re-issue stale `pending` rows; retry `parked` rows
/// whose backoff has elapsed). All three are reproduced.
/// </summary>
public sealed class SagaCommandConfiguration : IEntityTypeConfiguration<SagaCommand>
{
    public void Configure(EntityTypeBuilder<SagaCommand> builder)
    {
        builder.ToTable("saga_commands");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.OrderId).HasColumnName("order_id");
        builder.Property(s => s.OrderReference).HasColumnName("order_reference").HasMaxLength(20).IsRequired();
        builder.Property(s => s.Command).HasColumnName("command").HasMaxLength(30).IsRequired();

        // MS-SQL has no `json` column type — #7's `json` becomes `nvarchar(max)`.
        builder.Property(s => s.Payload).HasColumnName("payload").HasColumnType("nvarchar(max)").IsRequired();

        builder.Property(s => s.TriggeringEventId).HasColumnName("triggering_event_id");

        builder.Property(s => s.Status).HasColumnName("status").HasMaxLength(10).IsRequired()
            .HasDefaultValue("pending");
        builder.Property(s => s.Attempts).HasColumnName("attempts").HasDefaultValue(0);
        builder.Property(s => s.LastError).HasColumnName("last_error").HasColumnType("nvarchar(max)");
        builder.Property(s => s.NextAttemptAt).HasColumnName("next_attempt_at").HasColumnType("datetime2(3)");

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");
        builder.Property(s => s.SentAt).HasColumnName("sent_at").HasColumnType("datetime2(3)");

        builder.HasIndex(s => new { s.OrderId, s.Command }).IsUnique();
        builder.HasIndex(s => new { s.Status, s.CreatedAt });
        builder.HasIndex(s => new { s.Status, s.NextAttemptAt });
    }
}
