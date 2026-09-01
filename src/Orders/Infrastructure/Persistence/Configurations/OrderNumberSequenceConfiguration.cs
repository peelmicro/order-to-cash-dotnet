using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;

namespace OrderToCash.Orders.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_orders.order_number_sequences` (Databases doc §4.2, §3): a
/// single-row technical counter, `id = 1`, no audit columns. Allocation
/// under a row lock (`UPDLOCK`) is a repository concern, out of scope here.
/// </summary>
public sealed class OrderNumberSequenceConfiguration : IEntityTypeConfiguration<OrderNumberSequence>
{
    public void Configure(EntityTypeBuilder<OrderNumberSequence> builder)
    {
        builder.ToTable("order_number_sequences");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(s => s.NextValue).HasColumnName("next_value");
    }
}
