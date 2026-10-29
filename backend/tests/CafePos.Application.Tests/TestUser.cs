using CafePos.Application.Common;
using CafePos.Domain;

namespace CafePos.Application.Tests;

/// <summary>A settable <see cref="ICurrentUser"/> for service tests.</summary>
public sealed class TestUser : ICurrentUser
{
    private readonly HashSet<string> _roles = new();

    public Guid? UserId { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = "Test User";

    public Guid? LocationId { get; init; }

    public bool IsOwner => _roles.Contains(Roles.Owner);

    public bool IsInRole(string role) => _roles.Contains(role);

    public static TestUser Owner(Guid? locationId = null) => new TestUser { LocationId = locationId }.With(Roles.Owner);

    public static TestUser Admin(Guid locationId) => new TestUser { LocationId = locationId }.With(Roles.Admin);

    public static TestUser Manager(Guid locationId) => new TestUser { LocationId = locationId }.With(Roles.Manager);

    public static TestUser Cashier(Guid locationId) => new TestUser { LocationId = locationId }.With(Roles.Cashier);

    private TestUser With(string role)
    {
        _roles.Add(role);
        return this;
    }
}
