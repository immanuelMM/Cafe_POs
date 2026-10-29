using CafePos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CafePos.Infrastructure.Persistence.Configurations;

internal sealed class ModifierListConfiguration : IEntityTypeConfiguration<ModifierList>
{
    public void Configure(EntityTypeBuilder<ModifierList> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.HasMany(x => x.Modifiers).WithOne().HasForeignKey(m => m.ModifierListId).OnDelete(DeleteBehavior.Cascade);
    }
}
