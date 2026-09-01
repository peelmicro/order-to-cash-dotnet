using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

namespace OrderToCash.Fulfillment.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_fulfillment.reservations` (Databases doc §5). Index
/// `(order_reference, status)` for the sweeper's/operator's "reservations of
/// this order in status X" lookups.
/// </summary>
public sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("reservations");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.StockId).HasColumnName("stock_id");

        builder.Property(r => r.CompanyCode).HasColumnName("company_code").HasMaxLength(20).IsRequired();
        builder.Property(r => r.RetailerCode).HasColumnName("retailer_code").HasMaxLength(20).IsRequired();
        builder.Property(r => r.ProductCode).HasColumnName("product_code").HasMaxLength(30).IsRequired();
        builder.Property(r => r.OrderReference).HasColumnName("order_reference").HasMaxLength(20).IsRequired();

        builder.Property(r => r.Units).HasColumnName("units");
        builder.Property(r => r.Status).HasColumnName("status").HasMaxLength(20).IsRequired();

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");

        builder.HasIndex(r => new { r.OrderReference, r.Status });

        // §5: "stock_id | char(36) FK -> stock". No navigation property on
        // either POCO — referential integrity only (feature db_orders review
        // D1's pattern, applied from the start here). #7's committed DDL
        // (`apps/fulfillment/drizzle/0000_nappy_mad_thinker.sql:75`) has this
        // FK as `ON DELETE no action`.
        builder.HasOne<Stock>()
            .WithMany()
            .HasForeignKey(r => r.StockId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
