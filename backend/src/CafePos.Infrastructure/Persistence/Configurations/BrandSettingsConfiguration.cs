using CafePos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CafePos.Infrastructure.Persistence.Configurations;

internal sealed class BrandSettingsConfiguration : IEntityTypeConfiguration<BrandSettings>
{
    public void Configure(EntityTypeBuilder<BrandSettings> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.BrandName).HasMaxLength(100);
        builder.Property(x => x.Tagline).HasMaxLength(200);
        builder.Property(x => x.PrimaryColor).HasMaxLength(7);
        builder.Property(x => x.SecondaryColor).HasMaxLength(7);
        builder.Property(x => x.TertiaryColor).HasMaxLength(7);
        builder.Property(x => x.LogoContentType).HasMaxLength(50);
        builder.Property(x => x.TarpaulinContentType).HasMaxLength(50);
        builder.HasData(new BrandSettings());
    }
}
