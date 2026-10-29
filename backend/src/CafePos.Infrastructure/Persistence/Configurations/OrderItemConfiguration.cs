using CafePos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CafePos.Infrastructure.Persistence.Configurations;

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ItemName).HasMaxLength(200);
        builder.HasMany(x => x.Modifiers).WithOne().HasForeignKey(m => m.OrderItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.LocationId);
    }
}
