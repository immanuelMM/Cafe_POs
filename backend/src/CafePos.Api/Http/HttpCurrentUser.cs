using System.Security.Claims;
using CafePos.Application.Common;
using CafePos.Domain;

namespace CafePos.Api.Http;

/// <summary>The authenticated caller, read from the JWT claims of the current request.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? LocationId => Guid.TryParse(Principal?.FindFirstValue(AppClaims.LocationId), out var id) ? id : null;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public bool IsOwner => Principal?.IsInRole(Roles.Owner) == true;

    public string Name => Principal?.Identity?.Name ?? "";

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
}
