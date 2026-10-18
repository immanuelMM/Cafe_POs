using CafePos.Api.Data;
using CafePos.Api.Domain;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;
using CafePos.Api.Services;
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
    private readonly ILogger<LocationsController> _logger;
    public LocationsController(CafeDbContext db, ILogger<LocationsController> logger) { _db = db; _logger = logger; }

    private static LocationDto Map(Location l) => new(l.Id, l.Name, l.Address, l.TimeZone, l.ContactPhone, l.VatRatePercent, l.Active);

    /// <summary>Locations the caller is assigned to.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LocationDto>>> List(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var rows = await _db.StaffAssignments.AsNoTracking().Where(a => a.UserId == userId).Select(a => a.Location!).OrderBy(l => l.Name).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>The caller's active location (name, address, VAT rate for the POS header and tax display).</summary>
    [HttpGet("current")]
    public async Task<ActionResult<LocationDto>> Current(CancellationToken ct) =>
        Map(await _db.Locations.AsNoTracking().FirstAsync(l => l.Id == User.RequireLocationId(), ct));

    /// <summary>Admin only, own active location. Use this to configure the VAT rate.</summary>
    [HttpPut("{id:guid}"), Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<LocationDto>> Update(Guid id, UpdateLocationRequest req, CancellationToken ct)
    {
        if (id != User.RequireLocationId()) throw ApiException.NotFound("Location"); // cannot touch other locations
        try { TimeZoneInfo.FindSystemTimeZoneById(req.TimeZone); }
        catch (TimeZoneNotFoundException) { throw ApiException.Unprocessable("Invalid time zone", $"'{req.TimeZone}' is not a known IANA time zone."); }

        var loc = await _db.Locations.FirstAsync(l => l.Id == id, ct);
        loc.Name = req.Name.Trim();
        loc.Address = req.Address;
        loc.TimeZone = req.TimeZone;
        loc.ContactPhone = req.ContactPhone;
        loc.VatRatePercent = req.VatRatePercent;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Location {LocationId} updated by {UserId}; VAT rate {Vat}", id, User.RequireUserId(), loc.VatRatePercent);
        return Map(loc);
    }
}

[ApiController]
[Route("api/v1")]
[Authorize]
public class InventoryController : ControllerBase
{
    private readonly CafeDbContext _db;
    private readonly StockService _stock;
    public InventoryController(CafeDbContext db, StockService stock) { _db = db; _stock = stock; }

    private static IngredientDto Map(Ingredient i, decimal onHand) =>
        new(i.Id, i.Name, i.Unit, i.LowStockThreshold, i.Active, onHand, onHand <= i.LowStockThreshold);

    private static StockMovementDto Map(StockMovement m) =>
        new(m.Id, m.IngredientId, m.QuantityDelta, m.Reason.ToString(), m.SourceDocumentRef, m.Note, m.Supplier, m.UnitCost, m.CreatedAt, m.UserId);

    private async Task<List<IngredientDto>> QueryAsync(bool lowOnly, bool includeInactive, CancellationToken ct)
    {
        // Left join onto the per-location stock rows (query-filtered to the active location).
        var rows = await (from i in _db.Ingredients.AsNoTracking()
                          where includeInactive || i.Active
                          join s in _db.IngredientStocks.AsNoTracking() on i.Id equals s.IngredientId into ss
                          from s in ss.DefaultIfEmpty()
                          orderby i.Name
                          select new { Ingredient = i, OnHand = s == null ? 0m : s.QuantityOnHand }).ToListAsync(ct);
        return rows.Select(r => Map(r.Ingredient, r.OnHand)).Where(d => !lowOnly || d.IsLowStock).ToList();
    }

    [HttpGet("ingredients")]
    public async Task<ActionResult<IReadOnlyList<IngredientDto>>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        await QueryAsync(false, includeInactive && User.IsManagerUp(), ct);

    /// <summary>Ingredients at or below their low-stock threshold at the active location.</summary>
    [HttpGet("ingredients/low-stock")]
    public async Task<ActionResult<IReadOnlyList<IngredientDto>>> LowStock(CancellationToken ct) =>
        await QueryAsync(true, false, ct);

    [HttpPost("ingredients"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<IngredientDto>> Create(CreateIngredientRequest req, CancellationToken ct)
    {
        var name = req.Name.Trim();
        if (await _db.Ingredients.AnyAsync(i => i.Name == name, ct)) throw ApiException.Conflict("Duplicate ingredient", $"'{name}' already exists.");
        var ing = new Ingredient { Id = Guid.NewGuid(), Name = name, Unit = req.Unit.Trim(), LowStockThreshold = req.LowStockThreshold };
        _db.Ingredients.Add(ing);
        await _db.SaveChangesAsync(ct);
        await _stock.EnsureStockRowAsync(ing.Id, ct);
        if (req.InitialQuantity > 0)
            await _stock.ApplyManualAsync(ing.Id, req.InitialQuantity, StockMovementReason.Adjustment, User.RequireUserId(), "Opening balance", null, null, ct);
        var onHand = await _db.IngredientStocks.Where(s => s.IngredientId == ing.Id).Select(s => s.QuantityOnHand).FirstOrDefaultAsync(ct);
        return Created($"/api/v1/ingredients/{ing.Id}", Map(ing, onHand));
    }

    [HttpPut("ingredients/{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<IngredientDto>> Update(Guid id, UpdateIngredientRequest req, CancellationToken ct)
    {
        var ing = await _db.Ingredients.FindAsync(new object[] { id }, ct) ?? throw ApiException.NotFound("Ingredient");
        var name = req.Name.Trim();
        if (await _db.Ingredients.AnyAsync(i => i.Name == name && i.Id != id, ct)) throw ApiException.Conflict("Duplicate ingredient", $"'{name}' already exists.");
        ing.Name = name;
        ing.Unit = req.Unit.Trim();
        ing.LowStockThreshold = req.LowStockThreshold;
        ing.Active = req.Active;
        await _db.SaveChangesAsync(ct);
        var onHand = await _db.IngredientStocks.Where(s => s.IngredientId == id).Select(s => s.QuantityOnHand).FirstOrDefaultAsync(ct);
        return Map(ing, onHand);
    }

    /// <summary>Ledger history for one ingredient at the active location, newest first.</summary>
    [HttpGet("ingredients/{id:guid}/movements"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<PagedResult<StockMovementDto>>> Movements(Guid id, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        if (!await _db.Ingredients.AnyAsync(i => i.Id == id, ct)) throw ApiException.NotFound("Ingredient");
        var (p, ps) = Paging.Normalize(page, pageSize);
        var q = _db.StockMovements.AsNoTracking().Where(m => m.IngredientId == id);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(m => m.CreatedAt).Skip((p - 1) * ps).Take(ps).ToListAsync(ct);
        return new PagedResult<StockMovementDto>(rows.Select(Map).ToList(), p, ps, total);
    }

    /// <summary>Record a restock (purchase) at the active location.</summary>
    [HttpPost("restocks"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<StockMovementDto>> Restock(RestockRequest req, CancellationToken ct)
    {
        if (!await _db.Ingredients.AnyAsync(i => i.Id == req.IngredientId && i.Active, ct)) throw ApiException.Unprocessable("Invalid ingredient", "Ingredient does not exist or is inactive.");
        var m = await _stock.ApplyManualAsync(req.IngredientId, req.Quantity, StockMovementReason.Restock, User.RequireUserId(), req.Note, req.Supplier, req.UnitCost, ct);
        return Created($"/api/v1/ingredients/{req.IngredientId}/movements", Map(m));
    }

    /// <summary>Manual stock correction or waste. Posts a signed ledger entry.</summary>
    [HttpPost("ingredients/{id:guid}/adjustments"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<StockMovementDto>> Adjust(Guid id, AdjustmentRequest req, CancellationToken ct)
    {
        if (!await _db.Ingredients.AnyAsync(i => i.Id == id, ct)) throw ApiException.NotFound("Ingredient");
        if (req.QuantityDelta == 0) throw ApiException.Unprocessable("Invalid adjustment", "quantityDelta must be non-zero.");
        if (!Enum.TryParse<StockMovementReason>(req.Reason, true, out var reason) || reason is not (StockMovementReason.Adjustment or StockMovementReason.Waste))
            throw ApiException.Unprocessable("Invalid reason", "Reason must be Adjustment or Waste.");
        if (reason == StockMovementReason.Waste && req.QuantityDelta > 0) throw ApiException.Unprocessable("Invalid adjustment", "Waste must be a negative quantity.");
        var m = await _stock.ApplyManualAsync(id, req.QuantityDelta, reason, User.RequireUserId(), req.Note, null, null, ct);
        return Created($"/api/v1/ingredients/{id}/movements", Map(m));
    }

    // ---- recipes ----
    [HttpGet("variations/{id:guid}/recipe")]
    [Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<RecipeDto>> GetRecipe(Guid id, CancellationToken ct)
    {
        if (!await _db.ItemVariations.AnyAsync(v => v.Id == id, ct)) throw ApiException.NotFound("Variation");
        var lines = await _db.RecipeLines.AsNoTracking().Include(r => r.Ingredient).Where(r => r.ItemVariationId == id).OrderBy(r => r.Ingredient!.Name).ToListAsync(ct);
        return new RecipeDto(id, lines.Select(r => new RecipeLineDto(r.IngredientId, r.Ingredient!.Name, r.Ingredient.Unit, r.Quantity)).ToList());
    }

    /// <summary>Replaces the whole recipe (global across locations). An empty list clears it.</summary>
    [HttpPut("variations/{id:guid}/recipe"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<RecipeDto>> SetRecipe(Guid id, SetRecipeRequest req, CancellationToken ct)
    {
        if (!await _db.ItemVariations.AnyAsync(v => v.Id == id, ct)) throw ApiException.NotFound("Variation");
        var ids = req.Lines.Select(l => l.IngredientId).ToList();
        if (ids.Distinct().Count() != ids.Count) throw ApiException.Unprocessable("Duplicate ingredient", "Each ingredient may appear once per recipe.");
        var found = await _db.Ingredients.Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        if (found.Count != ids.Count) throw ApiException.Unprocessable("Invalid ingredient", "One or more ingredients do not exist.");

        var existing = await _db.RecipeLines.Where(r => r.ItemVariationId == id).ToListAsync(ct);
        _db.RecipeLines.RemoveRange(existing);
        await _db.SaveChangesAsync(ct); // flush deletes first: unique (variation, ingredient)
        foreach (var l in req.Lines)
            _db.RecipeLines.Add(new RecipeLine { Id = Guid.NewGuid(), ItemVariationId = id, IngredientId = l.IngredientId, Quantity = l.Quantity });
        await _db.SaveChangesAsync(ct);
        return await GetRecipe(id, ct);
    }
}
