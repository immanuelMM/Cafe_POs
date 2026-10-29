using CafePos.Domain;

namespace CafePos.Application.Features.Inventory;

public interface IIngredientRepository
{
    /// <summary>Ingredients by name without stock levels (the global catalog view).</summary>
    Task<IReadOnlyList<Ingredient>> ListCatalogAsync(bool includeInactive, CancellationToken ct);

    /// <summary>Ingredients by name with the on-hand quantity at the target location (0 when no stock row exists).</summary>
    Task<IReadOnlyList<IngredientStockRow>> ListWithStockAsync(bool includeInactive, LocationScope scope, CancellationToken ct);

    Task<Ingredient?> GetForUpdateAsync(Guid id, CancellationToken ct);

    Task<bool> ExistsAsync(Guid id, CancellationToken ct);

    Task<bool> ActiveExistsAsync(Guid id, CancellationToken ct);

    Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken ct);

    /// <summary>How many of the given ids exist.</summary>
    Task<int> CountExistingAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    Task<string?> GetNameAsync(Guid id, CancellationToken ct);

    void Add(Ingredient ingredient);
}

public sealed record IngredientStockRow(Ingredient Ingredient, decimal OnHand);
