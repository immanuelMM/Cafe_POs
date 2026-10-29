using CafePos.Domain;

namespace CafePos.Application.Features.Locations;

public record CreateLocationRequest
{
    public string Name { get; init; } = "";
    public string? Address { get; init; }
    public string TimeZone { get; init; } = "Asia/Manila";
    public string? ContactPhone { get; init; }
    public decimal VatRatePercent { get; init; } = 12m;
    public string? StripeTerminalLocationId { get; init; }
}

public record UpdateLocationRequest
{
    public string Name { get; init; } = "";
    public string? Address { get; init; }
    public string TimeZone { get; init; } = "Asia/Manila";
    public string? ContactPhone { get; init; }
    public decimal VatRatePercent { get; init; } = 12m;
    /// <summary>Owner only (403 for others when sent). Omit to leave unchanged.</summary>
    public bool? Active { get; init; }
    /// <summary>Owner only (403 for others when sent). Omit to leave unchanged; empty string clears.</summary>
    public string? StripeTerminalLocationId { get; init; }
}

public record AssignStaffByEmailRequest
{
    public string Email { get; init; } = "";
    /// <summary>Cashier, Manager or Admin.</summary>
    public string Role { get; init; } = Roles.Cashier;
}
