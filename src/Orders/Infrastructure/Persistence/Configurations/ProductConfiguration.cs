using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;

namespace OrderToCash.Orders.Infrastructure.Persistence.Configurations;

/// <summary>`otc_orders.products` (Databases doc §4.1).</summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
        builder.HasIndex(p => p.Code).IsUnique();

        builder.Property(p => p.Ean).HasColumnName("ean").HasMaxLength(13).IsRequired();
        builder.HasIndex(p => p.Ean).IsUnique();

        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(255).IsRequired();
        builder.Property(p => p.Price).HasColumnName("price");
        builder.Property(p => p.CurrencyId).HasColumnName("currency_id");

        builder.Property(p => p.DisabledAt).HasColumnName("disabled_at").HasColumnType("datetime2(3)");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");

        // §4.1: "currency_id | char(36) FK -> currencies". No navigation
        // property on either POCO — the FK exists for referential integrity
        // only, not to give EF a graph to traverse (review D1).
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(p => p.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
