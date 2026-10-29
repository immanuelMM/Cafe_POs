namespace CafePos.Application.Features.Catalog;

public record UpsertCategoryRequest
{
    public string Name { get; init; } = "";
    public int SortOrder { get; init; }
    public bool? Active { get; init; }
}

public record UpsertVariationRequest
{
    public string Name { get; init; } = "";
    public string Sku { get; init; } = "";
    public decimal Price { get; init; }
    public bool? Active { get; init; }
}

public record UpsertItemRequest
{
    public Guid CategoryId { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public int SortOrder { get; init; }
    public bool? Active { get; init; }
    public List<Guid>? ModifierListIds { get; init; }
    /// <summary>Required (at least one) on create; ignored on update (use the variation endpoints).</summary>
    public List<UpsertVariationRequest>? Variations { get; init; }
}

public record UpsertModifierRequest
{
    /// <summary>Existing modifier id to update; omit to create.</summary>
    public Guid? Id { get; init; }
    public string Name { get; init; } = "";
    public decimal PriceDelta { get; init; }
}

public record UpsertModifierListRequest
{
    public string Name { get; init; } = "";
    public int MinSelections { get; init; }
    public int MaxSelections { get; init; } = 1;
    public List<UpsertModifierRequest> Modifiers { get; init; } = new();
}
