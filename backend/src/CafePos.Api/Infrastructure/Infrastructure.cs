using System.Security.Claims;
using CafePos.Api.Data;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Infrastructure;

/// <summary>Expected, client-facing failure; rendered as RFC 9457 ProblemDetails.</summary>
public class ApiException : Exception
{
    public int StatusCode { get; }
    public string Title { get; }
    public IDictionary<string, object?>? Extensions { get; }

    public ApiException(int statusCode, string title, string? detail = null, IDictionary<string, object?>? extensions = null)
        : base(detail ?? title)
    {
        StatusCode = statusCode;
        Title = title;
        Extensions = extensions;
    }

    public static ApiException NotFound(string what) => new(404, "Not found", $"{what} was not found.");
    public static ApiException Conflict(string title, string? detail = null, IDictionary<string, object?>? ext = null) => new(409, title, detail, ext);
    public static ApiException BadRequest(string title, string? detail = null) => new(400, title, detail);
    public static ApiException Forbidden(string detail) => new(403, "Forbidden", detail);
    public static ApiException LocationRequired() =>
        new(400, "Location required", "This request needs an active location. Owners: call POST /api/v1/auth/switch-location or pass locationId.");
    public static ApiException Locked(string title, string? detail = null) => new(423, title, detail);
    public static ApiException Unprocessable(string title, string? detail = null) => new(422, title, detail);
}

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IProblemDetailsService _problems;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problems)
    {
        _logger = logger;
        _problems = problems;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        ProblemDetails pd;
        if (ex is ApiException api)
        {
            _logger.LogInformation("Request rejected: {Status} {Title} ({Path})", api.StatusCode, api.Title, ctx.Request.Path);
            pd = new ProblemDetails { Status = api.StatusCode, Title = api.Title, Detail = api.Message };
            if (api.Extensions is not null)
                foreach (var kv in api.Extensions) pd.Extensions[kv.Key] = kv.Value;
        }
        else if (ex is BadHttpRequestException bad && bad.StatusCode is >= 400 and < 500)
        {
            pd = new ProblemDetails { Status = bad.StatusCode, Title = bad.StatusCode == 413 ? "Request too large" : "Bad request", Detail = bad.Message };
        }
        else
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            pd = new ProblemDetails { Status = 500, Title = "An unexpected error occurred." };
        }

        ctx.Response.StatusCode = pd.Status!.Value;
        return await _problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = pd, Exception = ex });
    }
}

public sealed class HttpCurrentLocation : ICurrentLocation
{
    private readonly IHttpContextAccessor _accessor;
    public HttpCurrentLocation(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public Guid? LocationId => Guid.TryParse(User?.FindFirstValue(AppClaims.LocationId), out var id) ? id : null;
    public Guid? UserId => Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public bool IsOwner => User?.IsInRole(CafePos.Domain.Roles.Owner) == true;
}

public static class AppClaims
{
    public const string LocationId = "location_id";
}

public static class ClaimsExtensions
{
    public static bool IsManagerUp(this ClaimsPrincipal user) =>
        user.IsInRole(CafePos.Domain.Roles.Manager) || user.IsInRole(CafePos.Domain.Roles.Admin) || user.IsOwner();

    public static bool IsOwner(this ClaimsPrincipal user) => user.IsInRole(CafePos.Domain.Roles.Owner);

    public static Guid? ActiveLocationId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(AppClaims.LocationId), out var id) ? id : null;

    public static Guid RequireUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new ApiException(401, "Unauthorized");

    public static Guid RequireLocationId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(AppClaims.LocationId), out var id)
            ? id
            : user.IsOwner()
                ? throw new ApiException(400, "Location required", "This Owner session has no active location. Call POST /api/v1/auth/switch-location first.")
                : throw new ApiException(401, "Unauthorized", "Token has no active location.");
}

/// <summary>Offset pagination envelope.</summary>
public record PagedResult<T>(IReadOnlyList<T> Data, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class Paging
{
    public const int MaxPageSize = 100;
    public static (int page, int pageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? 20, 1, MaxPageSize));
}
