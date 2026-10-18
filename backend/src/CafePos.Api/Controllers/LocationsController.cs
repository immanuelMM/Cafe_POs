using CafePos.Api.Data;
using CafePos.Api.Domain;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Api.Controllers;

[ApiController]
[Route("api/v1/locations")]
[Authorize]
public class LocationsController : ControllerBase
{
    private readonly CafeDbContext _db;
    private readonly ILocationAccess _access;
    private readonly ILogger<LocationsController> _logger;
    public LocationsController(CafeDbContext db, ILocationAccess access, ILogger<LocationsController> logger) { _db = db; _access = access; _logger = logger; }

    private static LocationDto Map(Location l) =>
        new(l.Id, l.Name, l.Address, l.TimeZone, l.ContactPhone, l.VatRatePercent, l.Active, l.StripeTerminalLocationId);

    private static void ValidateTimeZone(string tz)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(tz); }
        catch (TimeZoneNotFoundException) { throw ApiException.Unprocessable("Invalid time zone", $"'{tz}' is not a known IANA time zone."); }
    }

    /// <summary>Owner: every location (including inactive). Everyone else: the active locations they are assigned to.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LocationDto>>> List(CancellationToken ct)
    {
        if (User.IsOwner())
            return (await _db.Locations.AsNoTracking().OrderBy(l => l.Name).ToListAsync(ct)).Select(Map).ToList();
        var userId = User.RequireUserId();
        var rows = await _db.StaffAssignments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.UserId == userId && a.Location!.Active).Select(a => a.Location!).OrderBy(l => l.Name).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>The caller's active location (name, address, VAT rate for the POS header and tax display).</summary>
    [HttpGet("current")]
    public async Task<ActionResult<LocationDto>> Current(CancellationToken ct)
    {
        var id = User.RequireLocationId();
        return Map(await _db.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw ApiException.NotFound("Location"));
    }

    [HttpPost, Authorize(Policy = Policies.OwnerOnly)]
    [ProducesResponseType(typeof(LocationDto), 201)]
    public async Task<ActionResult<LocationDto>> Create(CreateLocationRequest req, CancellationToken ct)
    {
        ValidateTimeZone(req.TimeZone);
        var loc = new Location
        {
            Id = Guid.NewGuid(), Name = req.Name.Trim(), Address = req.Address, TimeZone = req.TimeZone, ContactPhone = req.ContactPhone,
            VatRatePercent = req.VatRatePercent, StripeTerminalLocationId = string.IsNullOrWhiteSpace(req.StripeTerminalLocationId) ? null : req.StripeTerminalLocationId.Trim()
        };
        _db.Locations.Add(loc);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Location {LocationId} created by {UserId}", loc.Id, User.RequireUserId());
        return Created($"/api/v1/locations/{loc.Id}", Map(loc));
    }

    /// <summary>Owner: any location. Admin: own active location only (403 otherwise). Active/StripeTerminalLocationId are Owner only.</summary>
    [HttpPut("{id:guid}"), Authorize(Policy = Policies.AdminUp)]
    public async Task<ActionResult<LocationDto>> Update(Guid id, UpdateLocationRequest req, CancellationToken ct)
    {
        await _access.EnsureAsync(User, id);
        if (!User.IsOwner() && (req.Active is not null || req.StripeTerminalLocationId is not null))
            throw ApiException.Forbidden("Only an Owner can change active or stripeTerminalLocationId.");
        ValidateTimeZone(req.TimeZone);

        var loc = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw ApiException.NotFound("Location");
        loc.Name = req.Name.Trim();
        loc.Address = req.Address;
        loc.TimeZone = req.TimeZone;
        loc.ContactPhone = req.ContactPhone;
        loc.VatRatePercent = req.VatRatePercent;
        if (req.Active is not null) loc.Active = req.Active.Value;
        if (req.StripeTerminalLocationId is not null)
            loc.StripeTerminalLocationId = string.IsNullOrWhiteSpace(req.StripeTerminalLocationId) ? null : req.StripeTerminalLocationId.Trim();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Location {LocationId} updated by {UserId}; VAT rate {Vat}", id, User.RequireUserId(), loc.VatRatePercent);
        return Map(loc);
    }

    /// <summary>Staff assigned to a location. Owner: any location. Admin: own active location.</summary>
    [HttpGet("{id:guid}/staff"), Authorize(Policy = Policies.AdminUp)]
    public async Task<ActionResult<IReadOnlyList<StaffMemberDto>>> Staff(Guid id, CancellationToken ct)
    {
        await _access.EnsureAsync(User, id);
        if (!await _db.Locations.AnyAsync(l => l.Id == id, ct)) throw ApiException.NotFound("Location");
        // Cross-location read for an Owner who is not currently at this location: explicit, Owner/Admin-gated IgnoreQueryFilters.
        return await _db.StaffAssignments.IgnoreQueryFilters().AsNoTracking().Where(a => a.LocationId == id)
            .OrderBy(a => a.User!.DisplayName)
            .Select(a => new StaffMemberDto(a.UserId, a.User!.Email ?? "", a.User.DisplayName, a.Role, a.User.Active)).ToListAsync(ct);
    }
}
