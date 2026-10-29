using Microsoft.AspNetCore.Identity;

namespace CafePos.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public bool Active { get; set; } = true;
    /// <summary>Unrestricted: sees/acts on every location, no StaffAssignment rows needed.</summary>
    public bool IsOwner { get; set; }
    /// <summary>Time clock PIN, hashed with IPasswordHasher. Never returned by the API.</summary>
    public string? ClockPinHash { get; set; }
    public int ClockPinFailedCount { get; set; }
    public DateTime? ClockPinLockedUntil { get; set; }
}
