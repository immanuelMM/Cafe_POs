namespace CafePos.Domain;

/// <summary>A user role at a location (plan 4.8). One role per (user, location).</summary>
public class StaffAssignment
{
    public Guid UserId { get; set; }
    public Guid LocationId { get; set; }
    public string Role { get; set; } = Roles.Cashier;
    public Location? Location { get; set; }
}
