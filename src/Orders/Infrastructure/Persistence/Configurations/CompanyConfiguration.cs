using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;

namespace OrderToCash.Orders.Infrastructure.Persistence.Configurations;

/// <summary>`otc_orders.companies` (Databases doc §4.1).</summary>
public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();

        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(c => c.Country).HasColumnName("country").HasMaxLength(2).IsRequired();
        builder.Property(c => c.Vat).HasColumnName("vat").HasMaxLength(15).IsRequired();
        builder.Property(c => c.Gln).HasColumnName("gln").HasMaxLength(13).IsRequired();
        builder.Property(c => c.CurrencyId).HasColumnName("currency_id");

        builder.Property(c => c.DisabledAt).HasColumnName("disabled_at").HasColumnType("datetime2(3)");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");

        // §4.1: "currency_id | char(36) FK -> currencies". No navigation
        // property — the FK is for referential integrity only (review D1).
        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(c => c.CurrencyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
