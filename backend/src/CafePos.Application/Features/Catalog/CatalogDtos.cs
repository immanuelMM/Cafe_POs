namespace CafePos.Application.Features.Catalog;

public record CategoryDto(Guid Id, string Name, int SortOrder, bool Active);

/// <summary>Price is the global price; EffectivePrice is what the caller's active location charges (override when present).</summary>
public record VariationDto(Guid Id, Guid ItemId, string Name, string Sku, decimal Price, bool Active, decimal EffectivePrice);

/// <summary>ImageUrl: versioned path relative to the API origin, or null when the item has no photo.</summary>
public record ItemDto(Guid Id, Guid CategoryId, string Name, string? Description, int SortOrder, bool Active,
    IReadOnlyList<VariationDto> Variations, IReadOnlyList<Guid> ModifierListIds, string? ImageUrl);

public record ModifierDto(Guid Id, string Name, decimal PriceDelta, bool Active);

public record ModifierListDto(Guid Id, string Name, int MinSelections, int MaxSelections, bool Active, IReadOnlyList<ModifierDto> Modifiers);
