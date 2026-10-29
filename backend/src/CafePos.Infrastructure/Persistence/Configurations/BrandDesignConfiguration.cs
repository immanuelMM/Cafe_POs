using CafePos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CafePos.Infrastructure.Persistence.Configurations;

internal sealed class BrandDesignConfiguration : IEntityTypeConfiguration<BrandDesign>
{
    public void Configure(EntityTypeBuilder<BrandDesign> builder)
    {
        builder.HasKey(x => x.Kind);
        builder.Property(x => x.Kind).HasMaxLength(20);
        builder.Property(x => x.Document).HasColumnType("jsonb").Metadata.SetMaxLength(null); // undo the 500-char string convention
    }
}
