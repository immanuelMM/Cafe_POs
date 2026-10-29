using CafePos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CafePos.Infrastructure.Persistence.Configurations;

internal sealed class ItemVariationConfiguration : IEntityTypeConfiguration<ItemVariation>
{
    public void Configure(EntityTypeBuilder<ItemVariation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Sku).IsUnique();
        builder.Property(x => x.Sku).HasMaxLength(64);
        builder.Property(x => x.Name).HasMaxLength(200);
    }
}
