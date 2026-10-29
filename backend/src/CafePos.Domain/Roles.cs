namespace CafePos.Domain;

public static class Roles
{
    public const string Cashier = "Cashier";
    public const string Manager = "Manager";
    public const string Admin = "Admin";
    /// <summary>Unrestricted across all locations. A user flag (ApplicationUser.IsOwner), not a StaffAssignment role.</summary>
    public const string Owner = "Owner";
    /// <summary>Comma list for [Authorize(Roles = ...)]: Manager, Admin and Owner.</summary>
    public const string ManagerUp = "Manager,Admin,Owner";
    /// <summary>Admin and Owner.</summary>
    public const string AdminUp = "Admin,Owner";
    /// <summary>Roles that can be stored in a StaffAssignment.</summary>
    public static readonly string[] Assignable = { Cashier, Manager, Admin };
    public static readonly string[] All = { Cashier, Manager, Admin, Owner };
}
