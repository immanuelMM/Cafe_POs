using CafePos.Application.Common;
using CafePos.Domain;
using Microsoft.Extensions.Logging;

namespace CafePos.Application.Features.Inventory;

/// <summary>
/// Inventory writes. Stock levels live in IngredientStock (per location); every change also appends a StockMovement.
/// Decrements use a conditional UPDATE so concurrent sales can never oversell; multi-step changes run in one transaction.
/// </summary>
public sealed class StockService(
    ICurrentUser user,
    IStockRepository stock,
    IRecipeRepository recipes,
    IIngredientRepository ingredients,
    IUnitOfWork uow,
    TimeProvider clock,
    ILogger<StockService> logger)
{
    /// <summary>
    /// Consumes recipe ingredients for a completed order: conditional decrement + ledger row per ingredient. A shortage returns a
    /// 409 with ingredient/required/available extensions; the caller's transaction then rolls back.
    /// </summary>
    public async Task<Result> ConsumeForOrderAsync(Order order, Guid? userId, CancellationToken ct)
    {
        var variationIds = order.Items.Select(i => i.ItemVariationId).Distinct().ToList();
        // Variations this location has switched to "do not track inventory" consume nothing.
        var untracked = await stock.GetUntrackedVariationIdsAsync(order.LocationId, variationIds, ct);
        variationIds.RemoveAll(untracked.Contains);
        var lines = await recipes.ListLinesForVariationsAsync(variationIds, ct);
        var soldByVariation = order.Items.Where(i => variationIds.Contains(i.ItemVariationId))
            .GroupBy(i => i.ItemVariationId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var required = lines
            .GroupBy(r => r.IngredientId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity * soldByVariation[r.ItemVariationId]));

        var locationId = user.LocationId;
        if (locationId is null) return Error.LocationRequired();
        foreach (var (ingredientId, qty) in required.OrderBy(k => k.Key)) // stable lock order
        {
            if (!await stock.TryDecrementAsync(locationId.Value, ingredientId, qty, ct))
            {
                var name = await ingredients.GetNameAsync(ingredientId, ct) ?? ingredientId.ToString();
                var onHand = await stock.GetOnHandAsync(ingredientId, ct) ?? 0m;
                logger.LogWarning("Order {OrderId} blocked: insufficient stock of {Ingredient} (need {Needed}, have {OnHand})", order.Id, name, qty, onHand);
                return Error.Conflict("Insufficient stock",
                    $"Not enough {name} to complete this order (needed {qty:0.###}, available {onHand:0.###}).",
                    new Dictionary<string, object?> { ["ingredientId"] = ingredientId, ["ingredient"] = name, ["required"] = qty, ["available"] = onHand });
            }

            stock.AddMovement(new StockMovement
            {
                Id = Guid.NewGuid(),
                LocationId = locationId.Value,
                IngredientId = ingredientId,
                QuantityDelta = -qty,
                Reason = StockMovementReason.Sale,
                SourceDocumentRef = $"order:{order.Id}",
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                UserId = userId
            });
        }
        return Result.Success();
    }

    /// <summary>Ensures a stock row exists for the active location (zero balance).</summary>
    public async Task<Result> EnsureStockRowAsync(Guid ingredientId, CancellationToken ct)
    {
        if (user.LocationId is not { } locationId) return Error.LocationRequired();
        await stock.EnsureStockRowAsync(locationId, ingredientId, ct);
        return Result.Success();
    }

    /// <summary>Signed manual change (restock, adjustment, waste). Refuses to take stock below zero.</summary>
    public async Task<Result<StockMovement>> ApplyManualAsync(Guid ingredientId, decimal delta, StockMovementReason reason, Guid? userId,
        string? note, string? supplier, decimal? unitCost, CancellationToken ct)
    {
        if (user.LocationId is not { } locationId) return Error.LocationRequired();
        await using var tx = await uow.BeginTransactionAsync(ct);
        await stock.EnsureStockRowAsync(locationId, ingredientId, ct);

        if (!await stock.TryApplyDeltaAsync(locationId, ingredientId, delta, ct))
            return Error.Conflict("Insufficient stock", "This adjustment would take the stock level below zero.");

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
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UserId = userId
        };
        stock.AddMovement(movement);
        await uow.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        logger.LogInformation("Stock {Reason} of {Delta} for ingredient {IngredientId} at location {LocationId} by {UserId}",
            reason, delta, ingredientId, locationId, userId);
        return movement;
    }
}
