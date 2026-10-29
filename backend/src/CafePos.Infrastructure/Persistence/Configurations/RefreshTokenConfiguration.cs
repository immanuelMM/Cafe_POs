using CafePos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CafePos.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.Property(x => x.TokenHash).HasMaxLength(100);
        builder.Property(x => x.ReplacedByTokenHash).HasMaxLength(100);
        builder.Property(x => x.CreatedByIp).HasMaxLength(45);
    }
}
