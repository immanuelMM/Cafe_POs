using CafePos.Application.Features.Locations;
using System.ComponentModel.DataAnnotations;
using CafePos.Domain;

namespace CafePos.Api.Dtos;

// ---- Auth ----
public record LoginRequest
{
    [Required, EmailAddress] public string Email { get; init; } = "";
    [Required] public string Password { get; init; } = "";
    /// <summary>Optional; required only when the user is assigned to several locations.</summary>
    public Guid? LocationId { get; init; }
}

/// <summary>Location is null for an Owner's cross-location session. Locations: assignments (all active locations for an Owner).</summary>
public record UserInfoDto(Guid Id, string Email, string DisplayName, string Role, bool IsOwner, LocationRef? Location, IReadOnlyList<LocationRoleDto> Locations);
public record AuthResponse(string AccessToken, DateTime ExpiresAt, UserInfoDto User);

public record SwitchLocationRequest
{
    /// <summary>Target location. Owners may send null for the cross-location session; everyone else must send a location.</summary>
    public Guid? LocationId { get; init; }
}

// ---- Branding ----

// ---- Time clock ----

// ---- Locations ----

// ---- Users ----

// ---- Variation location overrides ----

// ---- Catalog ----

// ---- Inventory ----

// ---- Orders ----

