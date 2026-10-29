using CafePos.Application.Common;
using CafePos.Application.Common.Identity;
using CafePos.Domain;
using Microsoft.Extensions.Logging;

namespace CafePos.Application.Features.Locations;

public sealed class LocationService(
    ICurrentUser user,
    ILocationRepository locations,
    IStaffAssignmentRepository assignments,
    IIdentityService identity,
    IUnitOfWork uow,
    ILogger<LocationService> logger)
{
    /// <summary>Owner: every location (including inactive). Everyone else: the active locations they are assigned to.</summary>
    public async Task<Result<IReadOnlyList<LocationDto>>> ListAsync(CancellationToken ct)
    {
        if (user.IsOwner)
            return (await locations.ListAllAsync(ct)).Select(l => l.ToDto()).ToList();
        var userId = user.RequireUser();
        if (userId.IsFailure) return userId.Error!;
        return (await locations.ListActiveAssignedToAsync(userId.Value, ct)).Select(l => l.ToDto()).ToList();
    }

    /// <summary>The caller's active location (name, address, VAT rate for the POS header and tax display).</summary>
    public async Task<Result<LocationDto>> CurrentAsync(CancellationToken ct)
    {
        var id = user.RequireLocation();
        if (id.IsFailure) return id.Error!;
        var loc = await locations.GetAsync(id.Value, ct);
        return loc is null ? Error.NotFound("Location") : loc.ToDto();
    }

    public async Task<Result<LocationDto>> CreateAsync(CreateLocationRequest req, CancellationToken ct)
    {
        if (!IsKnownTimeZone(req.TimeZone)) return InvalidTimeZone(req.TimeZone);
        var loc = new Location
        {
            Id = Guid.NewGuid(), Name = req.Name.Trim(), Address = req.Address, TimeZone = req.TimeZone, ContactPhone = req.ContactPhone,
            VatRatePercent = req.VatRatePercent,
            StripeTerminalLocationId = string.IsNullOrWhiteSpace(req.StripeTerminalLocationId) ? null : req.StripeTerminalLocationId.Trim()
        };
        locations.Add(loc);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Location {LocationId} created by {UserId}", loc.Id, user.UserId);
        return loc.ToDto();
    }

    /// <summary>Owner: any location. Admin: own active location only (403 otherwise). Active/StripeTerminalLocationId are Owner only.</summary>
    public async Task<Result<LocationDto>> UpdateAsync(Guid id, UpdateLocationRequest req, CancellationToken ct)
    {
        var access = user.EnsureLocationAccess(id);
        if (access.IsFailure) return access.Error!;
        if (!user.IsOwner && (req.Active is not null || req.StripeTerminalLocationId is not null))
            return Error.Forbidden("Only an Owner can change active or stripeTerminalLocationId.");
        if (!IsKnownTimeZone(req.TimeZone)) return InvalidTimeZone(req.TimeZone);

        var loc = await locations.GetForUpdateAsync(id, ct);
        if (loc is null) return Error.NotFound("Location");
        loc.Name = req.Name.Trim();
        loc.Address = req.Address;
        loc.TimeZone = req.TimeZone;
        loc.ContactPhone = req.ContactPhone;
        loc.VatRatePercent = req.VatRatePercent;
        if (req.Active is not null) loc.Active = req.Active.Value;
        if (req.StripeTerminalLocationId is not null)
            loc.StripeTerminalLocationId = string.IsNullOrWhiteSpace(req.StripeTerminalLocationId) ? null : req.StripeTerminalLocationId.Trim();
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Location {LocationId} updated by {UserId}; VAT rate {Vat}", id, user.UserId, loc.VatRatePercent);
        return loc.ToDto();
    }

    /// <summary>Staff assigned to a location. Owner: any location. Admin: own active location.</summary>
    public async Task<Result<IReadOnlyList<StaffMemberDto>>> StaffAsync(Guid id, CancellationToken ct)
    {
        var access = user.EnsureLocationAccess(id);
        if (access.IsFailure) return access.Error!;
        if (!await locations.ExistsAsync(id, ct)) return Error.NotFound("Location");
        return Result.Success(await locations.ListStaffAsync(id, ct));
    }

    /// <summary>
    /// Add an existing user (found by exact email) to this location with a role, or change their role here. This is how an Admin
    /// brings in a user from another branch without ever seeing that user's other assignments. Owner: any location; Admin: own location.
    /// </summary>
    public async Task<Result<StaffMemberDto>> AssignStaffAsync(Guid id, AssignStaffByEmailRequest req, CancellationToken ct)
    {
        var access = user.EnsureLocationAccess(id);
        if (access.IsFailure) return access.Error!;
        if (!Roles.Assignable.Contains(req.Role)) return Error.Unprocessable("Invalid role", "Role must be Cashier, Manager or Admin.");
        if (!await locations.ActiveExistsAsync(id, ct)) return Error.NotFound("Location");
        var target = await identity.FindByEmailAsync(req.Email.Trim(), ct);
        if (target is null) return Error.NotFound("User");
        if (target.IsOwner) return Error.Unprocessable("Owner needs no assignment", "Owners have access to every location.");
        if (!target.Active) return Error.Unprocessable("User inactive", "Reactivate the user first.");
        var caller = user.RequireUser();
        if (caller.IsFailure) return caller.Error!;
        if (target.Id == caller.Value) return Error.Unprocessable("Invalid change", "You cannot change your own assignment.");

        var row = await assignments.GetAsync(target.Id, id, ct);
        if (row is null) assignments.Add(new StaffAssignment { UserId = target.Id, LocationId = id, Role = req.Role });
        else row.Role = req.Role;
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("User {TargetId} assigned to {LocationId} as {Role} by {UserId}", target.Id, id, req.Role, caller.Value);
        return new StaffMemberDto(target.Id, target.Email, target.DisplayName, req.Role, target.Active);
    }

    private static bool IsKnownTimeZone(string tz)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(tz);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
    }

    private static Error InvalidTimeZone(string tz) => Error.Unprocessable("Invalid time zone", $"'{tz}' is not a known IANA time zone.");
}
