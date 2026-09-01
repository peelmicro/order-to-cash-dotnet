using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

namespace OrderToCash.Fulfillment.Infrastructure.Persistence.Configurations;

/// <summary>`otc_fulfillment.despatch_items` (Databases doc §5).</summary>
public sealed class DespatchItemConfiguration : IEntityTypeConfiguration<DespatchItem>
{
    public void Configure(EntityTypeBuilder<DespatchItem> builder)
    {
        builder.ToTable("despatch_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.DespatchId).HasColumnName("despatch_id");
        builder.Property(i => i.ProductCode).HasColumnName("product_code").HasMaxLength(30).IsRequired();
        builder.Property(i => i.Units).HasColumnName("units");

        builder.Property(i => i.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");

        // §5: "despatch_id (FK -> despatches, cascade)". #7's committed DDL
        // (`apps/fulfillment/drizzle/0000_nappy_mad_thinker.sql:76`) has this
        // FK as `ON DELETE cascade`.
        builder.HasOne<Despatch>()
            .WithMany()
            .HasForeignKey(i => i.DespatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
