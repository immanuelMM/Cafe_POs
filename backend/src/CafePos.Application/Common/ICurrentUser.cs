namespace CafePos.Application.Common;

/// <summary>The authenticated caller. Implemented in the Api layer over the HTTP context; services never read HttpContext.</summary>
public interface ICurrentUser : ICurrentLocation
{
    /// <summary>Display name from the token (the name claim); empty when absent.</summary>
    string Name { get; }

    bool IsInRole(string role);
}
