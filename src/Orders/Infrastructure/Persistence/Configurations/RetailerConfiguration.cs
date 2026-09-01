using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;

namespace OrderToCash.Orders.Infrastructure.Persistence.Configurations;

/// <summary>`otc_orders.retailers` (Databases doc §4.1).</summary>
public sealed class RetailerConfiguration : IEntityTypeConfiguration<Retailer>
{
    public void Configure(EntityTypeBuilder<Retailer> builder)
    {
        builder.ToTable("retailers");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
        builder.HasIndex(r => r.Code).IsUnique();

        builder.Property(r => r.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(r => r.Country).HasColumnName("country").HasMaxLength(2).IsRequired();
        builder.Property(r => r.Vat).HasColumnName("vat").HasMaxLength(15).IsRequired();
        builder.Property(r => r.Gln).HasColumnName("gln").HasMaxLength(13).IsRequired();
        builder.Property(r => r.CurrencyId).HasColumnName("currency_id");

        builder.Property(r => r.DisabledAt).HasColumnName("disabled_at").HasColumnType("datetime2(3)");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");

        // §4.1: "currency_id | char(36) FK -> currencies". No navigation
        // property — the FK is for referential integrity only (review D1).
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(r => r.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
