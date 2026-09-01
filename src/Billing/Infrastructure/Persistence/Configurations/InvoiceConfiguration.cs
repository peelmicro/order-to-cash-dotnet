using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;

namespace OrderToCash.Billing.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_billing.invoices` (Databases doc §6). `InvoiceReference` and
/// `OrderReference` are both unique — one invoice per order, per #7's
/// `apps/billing/drizzle/0002_invoice_sequences_and_order_uniqueness.sql`.
/// Index `(status, invoice_date)` is the n8n bank robot's "issued invoices
/// older than X" query.
/// </summary>
public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.InvoiceReference).HasColumnName("invoice_reference").HasMaxLength(20).IsRequired();
        builder.HasIndex(i => i.InvoiceReference).IsUnique();

        builder.Property(i => i.InvoiceDate).HasColumnName("invoice_date").HasColumnType("datetime2(3)");
        builder.Property(i => i.CompanyCode).HasColumnName("company_code").HasMaxLength(20).IsRequired();
        builder.Property(i => i.RetailerCode).HasColumnName("retailer_code").HasMaxLength(20).IsRequired();

        builder.Property(i => i.OrderReference).HasColumnName("order_reference").HasMaxLength(20).IsRequired();
        builder.HasIndex(i => i.OrderReference).IsUnique();

        builder.Property(i => i.Amount).HasColumnName("amount");
        builder.Property(i => i.Discount).HasColumnName("discount");
        builder.Property(i => i.TotalAmount).HasColumnName("total_amount");
        builder.Property(i => i.CurrencyCode).HasColumnName("currency_code").HasColumnType("char(3)").IsRequired();
        builder.Property(i => i.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(i => i.PaidAt).HasColumnName("paid_at").HasColumnType("datetime2(3)");

        builder.Property(i => i.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");

        builder.HasIndex(i => new { i.Status, i.InvoiceDate });
    }
}
