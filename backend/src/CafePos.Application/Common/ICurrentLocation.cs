namespace CafePos.Application.Common;

/// <summary>Resolves the active location for the current request (from the JWT location_id claim).</summary>
public interface ICurrentLocation
{
    Guid? LocationId { get; }

    Guid? UserId { get; }

    /// <summary>True for the unrestricted Owner role (may use explicit cross-location queries).</summary>
    bool IsOwner { get; }
}
