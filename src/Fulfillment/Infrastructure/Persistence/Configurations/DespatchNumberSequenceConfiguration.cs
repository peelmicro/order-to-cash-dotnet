using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

namespace OrderToCash.Fulfillment.Infrastructure.Persistence.Configurations;

/// <summary>
/// `otc_fulfillment.despatch_number_sequences` (Databases doc §5, §3): a
/// single-row technical counter, `id = 1`, no audit columns. Allocation
/// under a row lock (`UPDLOCK`) is a repository concern, out of scope here.
/// </summary>
public sealed class DespatchNumberSequenceConfiguration : IEntityTypeConfiguration<DespatchNumberSequence>
{
    public void Configure(EntityTypeBuilder<DespatchNumberSequence> builder)
    {
        builder.ToTable("despatch_number_sequences");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(s => s.NextValue).HasColumnName("next_value");
    }
}
