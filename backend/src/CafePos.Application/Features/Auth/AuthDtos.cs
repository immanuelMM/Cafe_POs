using CafePos.Application.Features.Locations;

namespace CafePos.Application.Features.Auth;

/// <summary>Location is null for an Owner's cross-location session. Locations: assignments (all active locations for an Owner).</summary>
public record UserInfoDto(Guid Id, string Email, string DisplayName, string Role, bool IsOwner, LocationRef? Location, IReadOnlyList<LocationRoleDto> Locations);

public record AuthResponse(string AccessToken, DateTime ExpiresAt, UserInfoDto User);
