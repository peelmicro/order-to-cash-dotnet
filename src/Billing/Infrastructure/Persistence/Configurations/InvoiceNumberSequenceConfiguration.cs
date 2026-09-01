using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;

namespace OrderToCash.Billing.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_billing.invoice_number_sequences` (Databases doc §6, §3): a
/// single-row technical counter, `id = 1`, no audit columns. Allocation
/// under a row lock (`UPDLOCK`) is a repository concern, out of scope here.
/// </summary>
public sealed class InvoiceNumberSequenceConfiguration : IEntityTypeConfiguration<InvoiceNumberSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceNumberSequence> builder)
    {
        builder.ToTable("invoice_number_sequences");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(s => s.NextValue).HasColumnName("next_value");
    }
}
