namespace CafePos.Application.Common.Identity;

/// <summary>
/// The Application's view of a user account. The Identity-backed entity (ApplicationUser) lives in Infrastructure; services
/// read and change accounts through this type and <see cref="IIdentityService"/>.
/// </summary>
public sealed class UserAccount
{
    public Guid Id { get; init; }

    public string Email { get; init; } = "";

    public string DisplayName { get; set; } = "";

    public bool Active { get; set; }

    /// <summary>Unrestricted: sees/acts on every location, no StaffAssignment rows needed.</summary>
    public bool IsOwner { get; set; }

    /// <summary>Time clock PIN hash (see <see cref="IPinHasher"/>). Never returned by the API.</summary>
    public string? ClockPinHash { get; set; }

    public int ClockPinFailedCount { get; set; }

    public DateTime? ClockPinLockedUntil { get; set; }
}
