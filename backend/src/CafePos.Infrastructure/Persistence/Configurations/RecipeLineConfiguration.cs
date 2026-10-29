using CafePos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CafePos.Infrastructure.Persistence.Configurations;

internal sealed class RecipeLineConfiguration : IEntityTypeConfiguration<RecipeLine>
{
    public void Configure(EntityTypeBuilder<RecipeLine> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ItemVariationId, x.IngredientId }).IsUnique();
        builder.Property(x => x.Quantity).HasPrecision(18, 3);
        builder.HasOne(x => x.Ingredient).WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ItemVariation>().WithMany(v => v.Recipe).HasForeignKey(x => x.ItemVariationId).OnDelete(DeleteBehavior.Cascade);
    }
}
