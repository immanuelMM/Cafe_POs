namespace CafePos.Application.Features.Auth;

public record LoginRequest
{
    public string Email { get; init; } = "";
    public string Password { get; init; } = "";
    /// <summary>Optional; required only when the user is assigned to several locations.</summary>
    public Guid? LocationId { get; init; }
}

public record SwitchLocationRequest
{
    /// <summary>Target location. Owners may send null for the cross-location session; everyone else must send a location.</summary>
    public Guid? LocationId { get; init; }
}
