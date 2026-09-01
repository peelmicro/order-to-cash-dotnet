using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;

namespace OrderToCash.Billing.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_billing.credit_items` (Databases doc §6). Index
/// `(credit_id, order_reference)` for the "movements of this credit line for
/// this order" lookup.
/// </summary>
public sealed class CreditItemConfiguration : IEntityTypeConfiguration<CreditItem>
{
    public void Configure(EntityTypeBuilder<CreditItem> builder)
    {
        builder.ToTable("credit_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.CreditId).HasColumnName("credit_id");
        builder.Property(i => i.OrderReference).HasColumnName("order_reference").HasMaxLength(20).IsRequired();
        builder.HasIndex(i => new { i.CreditId, i.OrderReference });

        builder.Property(i => i.Amount).HasColumnName("amount");
        builder.Property(i => i.Type).HasColumnName("type").HasMaxLength(20).IsRequired();
        builder.Property(i => i.CreditDate).HasColumnName("credit_date").HasColumnType("datetime2(3)");

        builder.Property(i => i.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");

        // §6: "credit_id | FK -> credits". #7's committed DDL
        // (`apps/billing/drizzle/0000_brown_hammerhead.sql`) has this FK as
        // `ON DELETE no action`. No navigation property on either POCO —
        // referential integrity only (feature db_orders review D1's
        // pattern).
        builder.HasOne<Credit>()
            .WithMany()
            .HasForeignKey(i => i.CreditId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
