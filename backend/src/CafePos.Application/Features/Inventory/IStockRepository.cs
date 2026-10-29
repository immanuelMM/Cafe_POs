using CafePos.Application.Common;
using CafePos.Domain;

namespace CafePos.Application.Features.Inventory;

/// <summary>
/// Stock levels (IngredientStock, per location) and the append-only movement ledger. Level changes are conditional UPDATE
/// statements (never read-modify-write) so concurrent sales cannot oversell; callers run them inside one transaction.
/// </summary>
public interface IStockRepository
{
    /// <summary>On-hand quantity at the active location (query filter), or null when no stock row exists.</summary>
    Task<decimal?> GetOnHandAsync(Guid ingredientId, CancellationToken ct);

    /// <summary>Ensures a stock row (zero balance) exists for the location (immediate).</summary>
    Task EnsureStockRowAsync(Guid locationId, Guid ingredientId, CancellationToken ct);

    /// <summary>Atomically adds <paramref name="delta"/> unless the level would drop below zero. False when refused (immediate).</summary>
    Task<bool> TryApplyDeltaAsync(Guid locationId, Guid ingredientId, decimal delta, CancellationToken ct);

    /// <summary>Atomically subtracts <paramref name="quantity"/> only if enough is on hand. False when short (immediate).</summary>
    Task<bool> TryDecrementAsync(Guid locationId, Guid ingredientId, decimal quantity, CancellationToken ct);

    void AddMovement(StockMovement movement);

    /// <summary>Ledger entries of one ingredient at the target location, newest first.</summary>
    Task<PagedResult<StockMovement>> ListMovementsAsync(Guid ingredientId, LocationScope scope, int page, int pageSize, CancellationToken ct);

    /// <summary>Variations the location switched to "do not track inventory" among <paramref name="variationIds"/>.</summary>
    Task<IReadOnlyList<Guid>> GetUntrackedVariationIdsAsync(Guid locationId, IReadOnlyCollection<Guid> variationIds, CancellationToken ct);
}
