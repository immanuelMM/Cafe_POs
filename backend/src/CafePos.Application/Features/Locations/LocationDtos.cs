namespace CafePos.Application.Features.Locations;

public record LocationRef(Guid Id, string Name);

public record LocationRoleDto(Guid LocationId, string LocationName, string Role);

public record LocationDto(Guid Id, string Name, string? Address, string TimeZone, string? ContactPhone, decimal VatRatePercent, bool Active,
    string? StripeTerminalLocationId);

public record StaffMemberDto(Guid UserId, string Email, string DisplayName, string Role, bool Active);
