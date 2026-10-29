using CafePos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CafePos.Infrastructure.Persistence.Configurations;

internal sealed class ItemVariationLocationOverrideConfiguration : IEntityTypeConfiguration<ItemVariationLocationOverride>
{
    public void Configure(EntityTypeBuilder<ItemVariationLocationOverride> builder)
    {
        builder.HasKey(x => new { x.ItemVariationId, x.LocationId });
        builder.HasIndex(x => x.LocationId);
        builder.Property(x => x.PriceOverride).HasPrecision(18, 2);
        builder.Property(x => x.LowStockThresholdOverride).HasPrecision(18, 3);
        builder.HasOne<ItemVariation>().WithMany().HasForeignKey(x => x.ItemVariationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
    }
}
