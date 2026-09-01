using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

namespace OrderToCash.Fulfillment.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_fulfillment.despatches` (Databases doc §5). `DespatchReference` and
/// `OrderReference` are both unique — at most one despatch per order, per
/// #7's `apps/fulfillment/drizzle/0002_despatch_number_sequence_and_order_reference_unique.sql`.
/// </summary>
public sealed class DespatchConfiguration : IEntityTypeConfiguration<Despatch>
{
    public void Configure(EntityTypeBuilder<Despatch> builder)
    {
        builder.ToTable("despatches");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");

        builder.Property(d => d.DespatchReference).HasColumnName("despatch_reference").HasMaxLength(20).IsRequired();
        builder.HasIndex(d => d.DespatchReference).IsUnique();

        builder.Property(d => d.DespatchDate).HasColumnName("despatch_date").HasColumnType("datetime2(3)");
        builder.Property(d => d.CompanyCode).HasColumnName("company_code").HasMaxLength(20).IsRequired();
        builder.Property(d => d.RetailerCode).HasColumnName("retailer_code").HasMaxLength(20).IsRequired();

        builder.Property(d => d.OrderReference).HasColumnName("order_reference").HasMaxLength(20).IsRequired();
        builder.HasIndex(d => d.OrderReference).IsUnique();

        builder.Property(d => d.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");
    }
}
