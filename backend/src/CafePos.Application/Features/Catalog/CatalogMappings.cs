using CafePos.Domain;

namespace CafePos.Application.Features.Catalog;

public static class CatalogMappings
{
    public static CategoryDto ToDto(this Category c) => new(c.Id, c.Name, c.SortOrder, c.Active);

    /// <summary>The variation with the price the caller's location charges (its override when present).</summary>
    public static VariationDto ToDto(this ItemVariation v, IReadOnlyDictionary<Guid, decimal> prices) =>
        new(v.Id, v.ItemId, v.Name, v.Sku, v.Price, v.Active, prices.TryGetValue(v.Id, out var p) ? p : v.Price);

    public static ItemDto ToDto(this Item i, bool includeInactiveVariations, IReadOnlyDictionary<Guid, decimal> prices) => new(
        i.Id, i.CategoryId, i.Name, i.Description, i.SortOrder, i.Active,
        i.Variations.Where(v => includeInactiveVariations || v.Active).OrderBy(v => v.Price).Select(v => v.ToDto(prices)).ToList(),
        i.ModifierLists.Select(m => m.ModifierListId).ToList(),
        i.ImageUpdatedAt is null ? null : $"/api/v1/items/{i.Id}/image?v={i.ImageUpdatedAt.Value.Ticks}");

    public static ModifierListDto ToDto(this ModifierList l, bool includeInactive) => new(
        l.Id, l.Name, l.MinSelections, l.MaxSelections, l.Active,
        l.Modifiers.Where(m => includeInactive || m.Active).OrderBy(m => m.Name).Select(m => new ModifierDto(m.Id, m.Name, m.PriceDelta, m.Active)).ToList());
}
