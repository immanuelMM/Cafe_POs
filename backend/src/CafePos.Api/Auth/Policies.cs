using CafePos.Domain;
using Microsoft.AspNetCore.Authorization;

namespace CafePos.Api.Auth;

/// <summary>Named authorization policies. The fallback policy requires an authenticated user (secure by default).</summary>
public static class Policies
{
    public const string OwnerOnly = "OwnerOnly";
    public const string AdminUp = "AdminUp";
    public const string ManagerUp = "ManagerUp";

    public static void Register(AuthorizationOptions o)
    {
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(); // secure by default
        o.AddPolicy(OwnerOnly, p => p.RequireRole(Roles.Owner));
        o.AddPolicy(AdminUp, p => p.RequireRole(Roles.Admin, Roles.Owner));
        o.AddPolicy(ManagerUp, p => p.RequireRole(Roles.Manager, Roles.Admin, Roles.Owner));
    }
}
