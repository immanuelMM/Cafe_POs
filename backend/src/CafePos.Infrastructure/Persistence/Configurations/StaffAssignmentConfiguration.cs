using CafePos.Domain;
using CafePos.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CafePos.Infrastructure.Persistence.Configurations;

internal sealed class StaffAssignmentConfiguration : IEntityTypeConfiguration<StaffAssignment>
{
    public void Configure(EntityTypeBuilder<StaffAssignment> builder)
    {
        builder.HasKey(x => new { x.UserId, x.LocationId });
        builder.Property(x => x.Role).HasMaxLength(50);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId);
        builder.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId);
    }
}
