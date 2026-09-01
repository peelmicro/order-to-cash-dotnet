using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;

namespace OrderToCash.Billing.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_billing.credits` (Databases doc §6). `Code` is unique; unique index
/// `(retailer_code, company_code)` — one credit line per (retailer,
/// company) pair.
/// </summary>
public sealed class CreditConfiguration : IEntityTypeConfiguration<Credit>
{
    public void Configure(EntityTypeBuilder<Credit> builder)
    {
        builder.ToTable("credits");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();

        builder.Property(c => c.RetailerCode).HasColumnName("retailer_code").HasMaxLength(20).IsRequired();
        builder.Property(c => c.CompanyCode).HasColumnName("company_code").HasMaxLength(20).IsRequired();
        builder.HasIndex(c => new { c.RetailerCode, c.CompanyCode }).IsUnique();

        builder.Property(c => c.CreditLimit).HasColumnName("credit_limit");
        builder.Property(c => c.CurrencyCode).HasColumnName("currency_code").HasColumnType("char(3)").IsRequired();

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");
    }
}
