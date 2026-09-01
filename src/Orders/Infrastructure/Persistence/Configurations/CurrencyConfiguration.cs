using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;

namespace OrderToCash.Orders.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_orders.currencies` (Databases doc §4.1). Column names are mapped
/// explicitly to `snake_case`, never relying on a convention that happens to
/// match, per CLAUDE.md.
/// </summary>
public sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("currencies");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.Code).HasColumnName("code").HasMaxLength(3).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();

        builder.Property(c => c.IsoNumber).HasColumnName("iso_number").HasMaxLength(3).IsRequired();
        builder.Property(c => c.Symbol).HasColumnName("symbol").HasMaxLength(5).IsRequired();
        builder.Property(c => c.DecimalPoints).HasColumnName("decimal_points");

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");
    }
}
