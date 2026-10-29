using CafePos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CafePos.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.LocationId, x.Key }).IsUnique();
        builder.Property(x => x.Key).HasMaxLength(200);
        builder.Property(x => x.ResponseJson).HasMaxLength(100000);
    }
}
