using CafePos.Application.Common.Uploads;
using CafePos.Domain;

namespace CafePos.Application.Features.Catalog;

public interface IItemRepository
{
    /// <summary>A page of items with their variations and modifier-list links (read-only), by sort order then name.</summary>
    Task<ItemPage> ListAsync(ItemListQuery query, CancellationToken ct);

    /// <summary>Item with variations and modifier-list links, read-only.</summary>
    Task<Item?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Item with variations and modifier-list links, tracked for modification.</summary>
    Task<Item?> GetForUpdateAsync(Guid id, CancellationToken ct);

    /// <summary>Item without its children, tracked (deactivation).</summary>
    Task<Item?> GetBasicForUpdateAsync(Guid id, CancellationToken ct);

    Task<bool> ExistsAsync(Guid id, CancellationToken ct);

    /// <summary>Price overrides at a location by variation id (only variations that have one).</summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetPriceOverridesAsync(Guid locationId, CancellationToken ct);

    /// <summary>The first of <paramref name="skus"/> already used by a variation other than <paramref name="exceptVariationId"/>, or null.</summary>
    Task<string?> FindTakenSkuAsync(IReadOnlyCollection<string> skus, Guid? exceptVariationId, CancellationToken ct);

    Task<IReadOnlyList<ItemVariation>> ListVariationsAsync(Guid itemId, bool includeInactive, CancellationToken ct);

    Task<ItemVariation?> GetVariationForUpdateAsync(Guid id, CancellationToken ct);

    Task<StoredImage?> GetImageAsync(Guid itemId, CancellationToken ct);

    Task<ItemImage?> GetImageForUpdateAsync(Guid itemId, CancellationToken ct);

    void Add(Item item);

    void AddVariation(ItemVariation variation);

    void AddImage(ItemImage image);

    void RemoveModifierLink(ItemModifierList link);

    /// <summary>Deletes the stored photo immediately (not via the unit of work).</summary>
    Task DeleteImageAsync(Guid itemId, CancellationToken ct);
}

public sealed record ItemListQuery(Guid? CategoryId, string? Search, bool IncludeInactive, int Page, int PageSize);

public sealed record ItemPage(IReadOnlyList<Item> Items, int TotalCount);
