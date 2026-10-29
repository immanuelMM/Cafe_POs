using CafePos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CafePos.Infrastructure.Persistence.Configurations;

internal sealed class ItemImageConfiguration : IEntityTypeConfiguration<ItemImage>
{
    public void Configure(EntityTypeBuilder<ItemImage> builder)
    {
        builder.HasKey(x => x.ItemId);
        builder.Property(x => x.ItemId).ValueGeneratedNever();
        builder.Property(x => x.ContentType).HasMaxLength(50);
        builder.HasOne<Item>().WithOne().HasForeignKey<ItemImage>(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
    }
}
