using CafePos.Api.Data;
using CafePos.Api.Domain;
using CafePos.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Api.Services;

/// <summary>
/// Inventory writes. Stock levels live in IngredientStock (per location); every change also appends a StockMovement.
/// Decrements use a conditional UPDATE (WHERE QuantityOnHand &gt;= n) so concurrent sales can never oversell;
/// callers must run these inside a single transaction so a multi-ingredient sale is all-or-nothing.
/// </summary>
public class StockService
{
    private readonly CafeDbContext _db;
    private readonly ICurrentLocation _current;
    private readonly ILogger<StockService> _logger;

    public StockService(CafeDbContext db, ICurrentLocation current, ILogger<StockService> logger)
    {
        _db = db;
        _current = current;
        _logger = logger;
    }

    private Guid LocationId => _current.LocationId ?? throw new ApiException(401, "Unauthorized", "No active location.");

    /// <summary>Consumes recipe ingredients for a completed order. Throws 409 (and the caller's transaction rolls back) if short.</summary>
    public async Task ConsumeForOrderAsync(Order order, Guid? userId, CancellationToken ct)
    {
        var variationIds = order.Items.Select(i => i.ItemVariationId).Distinct().ToList();
        var recipes = await _db.RecipeLines.AsNoTracking().Where(r => variationIds.Contains(r.ItemVariationId)).ToListAsync(ct);
        var soldByVariation = order.Items.GroupBy(i => i.ItemVariationId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var required = recipes
            .GroupBy(r => r.IngredientId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity * soldByVariation[r.ItemVariationId]));

        var locationId = LocationId;
        foreach (var (ingredientId, qty) in required.OrderBy(k => k.Key)) // stable lock order
        {
            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE ""IngredientStocks""
                SET ""QuantityOnHand"" = ""QuantityOnHand"" - {qty}
                WHERE ""LocationId"" = {locationId} AND ""IngredientId"" = {ingredientId} AND ""QuantityOnHand"" >= {qty}", ct);

            if (rows == 0)
            {
                var name = await _db.Ingredients.Where(i => i.Id == ingredientId).Select(i => i.Name).FirstOrDefaultAsync(ct) ?? ingredientId.ToString();
                var onHand = await _db.IngredientStocks.Where(s => s.IngredientId == ingredientId).Select(s => (decimal?)s.QuantityOnHand).FirstOrDefaultAsync(ct) ?? 0m;
                _logger.LogWarning("Order {OrderId} blocked: insufficient stock of {Ingredient} (need {Needed}, have {OnHand})", order.Id, name, qty, onHand);
                throw ApiException.Conflict("Insufficient stock",
                    $"Not enough {name} to complete this order (needed {qty:0.###}, available {onHand:0.###}).",
                    new Dictionary<string, object?> { ["ingredientId"] = ingredientId, ["ingredient"] = name, ["required"] = qty, ["available"] = onHand });
            }

            _db.StockMovements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                LocationId = locationId,
                IngredientId = ingredientId,
                QuantityDelta = -qty,
                Reason = StockMovementReason.Sale,
                SourceDocumentRef = $"order:{order.Id}",
                CreatedAt = DateTime.UtcNow,
                UserId = userId
            });
        }
    }

    /// <summary>Ensures a stock row exists for this location (zero balance).</summary>
    public async Task EnsureStockRowAsync(Guid ingredientId, CancellationToken ct)
    {
        var locationId = LocationId;
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""IngredientStocks"" (""LocationId"", ""IngredientId"", ""QuantityOnHand"")
            VALUES ({locationId}, {ingredientId}, 0)
            ON CONFLICT (""LocationId"", ""IngredientId"") DO NOTHING", ct);
    }

    /// <summary>Signed manual change (restock, adjustment, waste). Refuses to take stock below zero.</summary>
    public async Task<StockMovement> ApplyManualAsync(Guid ingredientId, decimal delta, StockMovementReason reason, Guid? userId,
        string? note, string? supplier, decimal? unitCost, CancellationToken ct)
    {
        var locationId = LocationId;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await EnsureStockRowAsync(ingredientId, ct);

        var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE ""IngredientStocks""
            SET ""QuantityOnHand"" = ""QuantityOnHand"" + {delta}
            WHERE ""LocationId"" = {locationId} AND ""IngredientId"" = {ingredientId} AND ""QuantityOnHand"" + {delta} >= 0", ct);
        if (rows == 0)
            throw ApiException.Conflict("Insufficient stock", "This adjustment would take the stock level below zero.");

        var movement = new StockMovement
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            IngredientId = ingredientId,
            QuantityDelta = delta,
            Reason = reason,
            Note = note,
            Supplier = supplier,
            UnitCost = unitCost,
            CreatedAt = DateTime.UtcNow,
            UserId = userId
        };
        _db.StockMovements.Add(movement);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _logger.LogInformation("Stock {Reason} of {Delta} for ingredient {IngredientId} at location {LocationId} by {UserId}",
            reason, delta, ingredientId, locationId, userId);
        return movement;
    }
}
