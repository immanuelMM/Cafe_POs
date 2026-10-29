using CafePos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CafePos.Infrastructure.Persistence.Configurations;

internal sealed class ItemModifierListConfiguration : IEntityTypeConfiguration<ItemModifierList>
{
    public void Configure(EntityTypeBuilder<ItemModifierList> builder)
    {
        builder.HasKey(x => new { x.ItemId, x.ModifierListId });
        builder.HasOne(x => x.ModifierList).WithMany().HasForeignKey(x => x.ModifierListId).OnDelete(DeleteBehavior.Restrict);
    }
}
