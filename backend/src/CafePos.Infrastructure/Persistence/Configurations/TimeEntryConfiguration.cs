using CafePos.Domain;
using CafePos.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CafePos.Infrastructure.Persistence.Configurations;

internal sealed class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.LocationId, x.UserId, x.ClockInAt });
        builder.HasIndex(x => new { x.UserId, x.LocationId }).IsUnique().HasFilter("\"ClockOutAt\" IS NULL").HasDatabaseName("IX_TimeEntries_OneOpenPerUserLocation");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
    }
}
