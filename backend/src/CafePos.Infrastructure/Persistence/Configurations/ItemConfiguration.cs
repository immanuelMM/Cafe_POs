using CafePos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CafePos.Infrastructure.Persistence.Configurations;

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Variations).WithOne(v => v.Item).HasForeignKey(v => v.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.ModifierLists).WithOne().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
    }
}
