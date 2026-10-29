using CafePos.Domain;

namespace CafePos.Application.Features.Orders;

/// <summary>Read-only catalog lookups used to price and validate order lines.</summary>
public interface IOrderCatalogRepository
{
    /// <summary>Variation with its item and the item's modifier-list links, or null.</summary>
    Task<ItemVariation?> GetVariationWithItemAsync(Guid variationId, CancellationToken ct);

    Task<IReadOnlyList<Modifier>> GetModifiersAsync(IReadOnlyCollection<Guid> modifierIds, CancellationToken ct);

    /// <summary>Active modifier lists among <paramref name="listIds"/>.</summary>
    Task<IReadOnlyList<ModifierList>> GetActiveModifierListsAsync(IReadOnlyCollection<Guid> listIds, CancellationToken ct);

    /// <summary>The location's price override for the variation, or null when it uses the global price.</summary>
    Task<decimal?> GetPriceOverrideAsync(Guid variationId, Guid locationId, CancellationToken ct);
}
