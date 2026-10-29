using CafePos.Domain;

namespace CafePos.Application.Features.Users;

public record AssignmentRequest
{
    public Guid LocationId { get; init; }
    /// <summary>Cashier, Manager or Admin.</summary>
    public string Role { get; init; } = Roles.Cashier;
}

public record CreateUserRequest
{
    public string Email { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Password { get; init; } = "";
    /// <summary>Owner callers only.</summary>
    public bool IsOwner { get; init; }
    public List<AssignmentRequest> Assignments { get; init; } = new();
}

public record UpdateUserRequest
{
    public string DisplayName { get; init; } = "";
    public bool? Active { get; init; }
    /// <summary>Owner callers only (403 for others when sent).</summary>
    public bool? IsOwner { get; init; }
}

public record SetPinRequest
{
    public string Pin { get; init; } = "";
}
