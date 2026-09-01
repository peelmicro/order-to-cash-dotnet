using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;

namespace OrderToCash.Orders.Infrastructure.Persistence.Configurations;

/// <summary>`otc_orders.order_items` (Databases doc §4.2).</summary>
public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.OrderId).HasColumnName("order_id");
        builder.Property(i => i.ProductId).HasColumnName("product_id");
        builder.Property(i => i.Description).HasColumnName("description").HasMaxLength(255).IsRequired();
        builder.Property(i => i.Price).HasColumnName("price");
        builder.Property(i => i.Quantity).HasColumnName("quantity");
        builder.Property(i => i.Discount).HasColumnName("discount");

        builder.Property(i => i.CreatedAt).HasColumnName("created_at").HasColumnType("datetime2(3)");
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime2(3)");

        // §4.2: "product_id | char(36) FK -> products. Local referential
        // integrity only". No navigation property — review D1.
        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
