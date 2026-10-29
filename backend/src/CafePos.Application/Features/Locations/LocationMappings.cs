using CafePos.Domain;

namespace CafePos.Application.Features.Locations;

public static class LocationMappings
{
    public static LocationDto ToDto(this Location l) =>
        new(l.Id, l.Name, l.Address, l.TimeZone, l.ContactPhone, l.VatRatePercent, l.Active, l.StripeTerminalLocationId);
}
