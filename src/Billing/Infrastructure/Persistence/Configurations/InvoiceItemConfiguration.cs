using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;

namespace OrderToCash.Billing.Infrastructure.Persistence.Configurations;

/// <summary>`otc_billing.invoice_items` (Databases doc §6).</summary>
public sealed class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        builder.ToTable("invoice_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.InvoiceId).HasColumnName("invoice_id");
        builder.Property(i => i.ProductCode).HasColumnName("product_code").HasMaxLength(30).IsRequired();
        builder.Property(i => i.Units).HasColumnName("units");
        builder.Property(i => i.Price).HasColumnName("price");

        builder.Property(i => i.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");

        // §6: "invoice_id (FK -> invoices, cascade)". #7's committed DDL
        // (`apps/billing/drizzle/0000_brown_hammerhead.sql`) has this FK as
        // `ON DELETE cascade`.
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(i => i.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
