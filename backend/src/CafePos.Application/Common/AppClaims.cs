namespace CafePos.Application.Common;

/// <summary>Names of the application-specific JWT claims.</summary>
public static class AppClaims
{
    /// <summary>The active location of the session (absent for an Owner's cross-location session).</summary>
    public const string LocationId = "location_id";
}
