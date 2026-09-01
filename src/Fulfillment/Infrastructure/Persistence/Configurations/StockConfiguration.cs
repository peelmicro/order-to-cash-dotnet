using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

namespace OrderToCash.Fulfillment.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_fulfillment.stock` (Databases doc §5). Unique index
/// `(company_code, product_code)` — one row per (company, product); the
/// reservation flow locks these rows `FOR UPDATE` in a fixed order to make
/// concurrent reservations safe.
/// </summary>
public sealed class StockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> builder)
    {
        builder.ToTable("stock");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.CompanyCode).HasColumnName("company_code").HasMaxLength(20).IsRequired();
        builder.Property(s => s.ProductCode).HasColumnName("product_code").HasMaxLength(30).IsRequired();
        builder.HasIndex(s => new { s.CompanyCode, s.ProductCode }).IsUnique();

        builder.Property(s => s.Units).HasColumnName("units");
        builder.Property(s => s.ReservedUnits).HasColumnName("reserved_units");
        builder.Property(s => s.LowStockThreshold).HasColumnName("low_stock_threshold");

        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");
    }
}
