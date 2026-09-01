using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;

namespace OrderToCash.Billing.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_billing.payments` (Databases doc §6). `PaymentReference` is unique —
/// the remittance endpoint's idempotency key (R47/R48). No `UpdatedAt`
/// column (§3 — append-only ledger).
/// </summary>
public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.PaymentReference).HasColumnName("payment_reference").HasMaxLength(30).IsRequired();
        builder.HasIndex(p => p.PaymentReference).IsUnique();

        builder.Property(p => p.InvoiceId).HasColumnName("invoice_id");
        builder.Property(p => p.Amount).HasColumnName("amount");
        builder.Property(p => p.CurrencyCode).HasColumnName("currency_code").HasColumnType("char(3)").IsRequired();
        builder.Property(p => p.ValueDate).HasColumnName("value_date").HasColumnType("datetime2(3)");
        builder.Property(p => p.Source).HasColumnName("source").HasMaxLength(20).IsRequired();

        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");

        // §6: "invoice_id | FK -> invoices". #7's committed DDL
        // (`apps/billing/drizzle/0000_brown_hammerhead.sql`) has this FK as
        // `ON DELETE no action`.
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(p => p.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
