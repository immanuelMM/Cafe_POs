using CafePos.Application.Common;
using CafePos.Application.Features.Locations;
using CafePos.Domain;

namespace CafePos.Application.Features.Inventory;

public sealed class IngredientService(
    ICurrentUser user,
    IIngredientRepository ingredients,
    IStockRepository stock,
    ILocationRepository locations,
    StockService stockService,
    IUnitOfWork uow)
{
    /// <summary>
    /// Resolves which location a read targets. Non-owners: only their active location. Owners: the requested one (an explicit
    /// cross-location read) or their active one.
    /// </summary>
    private async Task<Result<LocationScope>> ResolveTargetAsync(Guid? requested, CancellationToken ct)
    {
        var active = user.LocationId;
        if (requested is null || requested == active)
        {
            if (active is null) return Error.LocationRequired();
            return new LocationScope(active.Value, false);
        }
        var access = user.EnsureLocationAccess(requested.Value);
        if (access.IsFailure) return access.Error!;
        if (!await locations.ExistsAsync(requested.Value, ct)) return Error.NotFound("Location");
        return new LocationScope(requested.Value, true);
    }

    private async Task<Result<IReadOnlyList<IngredientDto>>> QueryAsync(bool lowOnly, bool includeInactive, Guid? locationId, CancellationToken ct)
    {
        if (!lowOnly && locationId is null && user.LocationId is null && user.IsOwner)
        {
            // Owner cross-location session: ingredients are global, so list the catalog without stock levels.
            var catalog = await ingredients.ListCatalogAsync(includeInactive, ct);
            return catalog.Select(i => i.ToDto(null)).ToList();
        }
        var target = await ResolveTargetAsync(locationId, ct);
        if (target.IsFailure) return target.Error!;
        var rows = await ingredients.ListWithStockAsync(includeInactive, target.Value, ct);
        return rows.Select(r => r.Ingredient.ToDto(r.OnHand)).Where(d => !lowOnly || d.IsLowStock == true).ToList();
    }

    public Task<Result<IReadOnlyList<IngredientDto>>> ListAsync(bool includeInactive, Guid? locationId, CancellationToken ct) =>
        QueryAsync(false, includeInactive && user.IsManagerUp(), locationId, ct);

    /// <summary>Ingredients at or below their low-stock threshold at the active location (or `locationId`, Owner only).</summary>
    public Task<Result<IReadOnlyList<IngredientDto>>> LowStockAsync(Guid? locationId, CancellationToken ct) =>
        QueryAsync(true, false, locationId, ct);

    public async Task<Result<IngredientDto>> CreateAsync(CreateIngredientRequest req, CancellationToken ct)
    {
        var name = req.Name.Trim();
        var hasLocation = user.LocationId is not null; // an Owner cross-location session can still create the global ingredient
        if (req.InitialQuantity > 0 && !hasLocation) return Error.LocationRequired();
        if (await ingredients.NameExistsAsync(name, null, ct)) return Error.Conflict("Duplicate ingredient", $"'{name}' already exists.");
        var ingredient = new Ingredient { Id = Guid.NewGuid(), Name = name, Unit = req.Unit.Trim(), LowStockThreshold = req.LowStockThreshold };
        ingredients.Add(ingredient);
        await uow.SaveChangesAsync(ct);
        if (hasLocation)
        {
            var ensured = await stockService.EnsureStockRowAsync(ingredient.Id, ct);
            if (ensured.IsFailure) return ensured.Error!;
        }
        if (req.InitialQuantity > 0)
        {
            var userId = user.RequireUser();
            if (userId.IsFailure) return userId.Error!;
            var opening = await stockService.ApplyManualAsync(ingredient.Id, req.InitialQuantity, StockMovementReason.Adjustment, userId.Value, "Opening balance", null, null, ct);
            if (opening.IsFailure) return opening.Error!;
        }
        decimal? onHand = hasLocation ? await stock.GetOnHandAsync(ingredient.Id, ct) ?? 0m : null;
        return ingredient.ToDto(onHand);
    }

    public async Task<Result<IngredientDto>> UpdateAsync(Guid id, UpdateIngredientRequest req, CancellationToken ct)
    {
        var ingredient = await ingredients.GetForUpdateAsync(id, ct);
        if (ingredient is null) return Error.NotFound("Ingredient");
        var name = req.Name.Trim();
        if (await ingredients.NameExistsAsync(name, id, ct)) return Error.Conflict("Duplicate ingredient", $"'{name}' already exists.");
        ingredient.Name = name;
        ingredient.Unit = req.Unit.Trim();
        ingredient.LowStockThreshold = req.LowStockThreshold;
        ingredient.Active = req.Active;
        await uow.SaveChangesAsync(ct);
        decimal? onHand = user.LocationId is null ? null : await stock.GetOnHandAsync(id, ct) ?? 0m;
        return ingredient.ToDto(onHand);
    }

    /// <summary>Ledger history for one ingredient at the active location (or `locationId`, Owner only), newest first.</summary>
    public async Task<Result<PagedResult<StockMovementDto>>> MovementsAsync(Guid id, Guid? locationId, int? page, int? pageSize, CancellationToken ct)
    {
        if (!await ingredients.ExistsAsync(id, ct)) return Error.NotFound("Ingredient");
        var target = await ResolveTargetAsync(locationId, ct);
        if (target.IsFailure) return target.Error!;
        var (p, ps) = Paging.Normalize(page, pageSize);
        var movements = await stock.ListMovementsAsync(id, target.Value, p, ps, ct);
        return new PagedResult<StockMovementDto>(movements.Data.Select(m => m.ToDto()).ToList(), p, ps, movements.TotalCount);
    }

    /// <summary>Record a restock (purchase) at the active location.</summary>
    public async Task<Result<StockMovementDto>> RestockAsync(RestockRequest req, CancellationToken ct)
    {
        if (!await ingredients.ActiveExistsAsync(req.IngredientId, ct)) return Error.Unprocessable("Invalid ingredient", "Ingredient does not exist or is inactive.");
        var userId = user.RequireUser();
        if (userId.IsFailure) return userId.Error!;
        var movement = await stockService.ApplyManualAsync(req.IngredientId, req.Quantity, StockMovementReason.Restock, userId.Value, req.Note, req.Supplier, req.UnitCost, ct);
        return movement.Map(m => m.ToDto());
    }

    /// <summary>Manual stock correction or waste. Posts a signed ledger entry.</summary>
    public async Task<Result<StockMovementDto>> AdjustAsync(Guid id, AdjustmentRequest req, CancellationToken ct)
    {
        if (!await ingredients.ExistsAsync(id, ct)) return Error.NotFound("Ingredient");
        if (req.QuantityDelta == 0) return Error.Unprocessable("Invalid adjustment", "quantityDelta must be non-zero.");
        if (!Enum.TryParse<StockMovementReason>(req.Reason, true, out var reason) || reason is not (StockMovementReason.Adjustment or StockMovementReason.Waste))
            return Error.Unprocessable("Invalid reason", "Reason must be Adjustment or Waste.");
        if (reason == StockMovementReason.Waste && req.QuantityDelta > 0) return Error.Unprocessable("Invalid adjustment", "Waste must be a negative quantity.");
        var userId = user.RequireUser();
        if (userId.IsFailure) return userId.Error!;
        var movement = await stockService.ApplyManualAsync(id, req.QuantityDelta, reason, userId.Value, req.Note, null, null, ct);
        return movement.Map(m => m.ToDto());
    }
}
