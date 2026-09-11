using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;

namespace OrderToCash.Orders.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_orders.orders` — the write model of the `Order` aggregate
/// (Databases doc §4.2). Indexes: `(retailer_id, status)` for "orders of
/// this retailer in status X"; `(status, order_date)` for "orders in status
/// X, oldest first". A missing index here is a silent performance defect —
/// reproduce both, not just one.
/// </summary>
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");

        builder.Property(o => o.OrderReference).HasColumnName("order_reference").HasMaxLength(20).IsRequired();
        builder.HasIndex(o => o.OrderReference).IsUnique();

        // observability_reliability, RI1/RI4, ledger L1: a FILTERED unique
        // index — EF Core's SQL Server provider emits `WHERE [request_id]
        // IS NOT NULL` by convention for a unique index over a nullable
        // column (verified against the generated migration; see
        // progress/impl_observability_reliability.md for the quoted
        // `filter:` argument). Unfiltered, MS-SQL treats two NULLs as equal
        // and admits only ONE order with no requestId — a total break of
        // RI4, since almost no order supplies one.
        builder.Property(o => o.RequestId).HasColumnName("request_id");
        builder.HasIndex(o => o.RequestId)
            .IsUnique()
            .HasDatabaseName("uq_orders_request_id");

        builder.Property(o => o.OrderDate).HasColumnName("order_date").HasColumnType("datetime2(3)");
        builder.Property(o => o.CompanyId).HasColumnName("company_id");
        builder.Property(o => o.RetailerId).HasColumnName("retailer_id");
        builder.Property(o => o.CurrencyId).HasColumnName("currency_id");

        builder.Property(o => o.InitialAmount).HasColumnName("initial_amount");
        builder.Property(o => o.InitialDiscount).HasColumnName("initial_discount");
        builder.Property(o => o.TotalAmount).HasColumnName("total_amount");

        builder.Property(o => o.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(o => o.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(100);
        builder.Property(o => o.Notes).HasColumnName("notes");

        builder.Property(o => o.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");

        // The relay's "orders of this retailer in status X" query.
        builder.HasIndex(o => new { o.RetailerId, o.Status });

        // The orchestrator's "orders in status X, oldest first" query.
        builder.HasIndex(o => new { o.Status, o.OrderDate });

        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // §4.2: "company_id | char(36) FK -> companies", "retailer_id | char(36)
        // FK -> retailers", "currency_id | char(36) FK -> currencies". No
        // navigation properties on the referenced POCOs — referential
        // integrity only (review D1). Restrict (NO ACTION), matching #7's
        // DDL: only order_items.order_id cascades.
        builder.HasOne<Company>()
            .WithMany()
            .HasForeignKey(o => o.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Retailer>()
            .WithMany()
            .HasForeignKey(o => o.RetailerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(o => o.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
