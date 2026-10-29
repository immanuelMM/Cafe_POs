using CafePos.Domain;

namespace CafePos.Application.Features.VariationOverrides;

public interface IVariationOverrideRepository
{
    /// <summary>The variation's id and global price, or null when it does not exist.</summary>
    Task<VariationPrice?> GetVariationAsync(Guid variationId, CancellationToken ct);

    /// <summary>Read-only overrides of a variation; <paramref name="locationId"/> limits them to one location (null = all).</summary>
    Task<IReadOnlyList<ItemVariationLocationOverride>> ListAsync(Guid variationId, Guid? locationId, CancellationToken ct);

    /// <summary>Tracked override of a variation at a location.</summary>
    Task<ItemVariationLocationOverride?> GetAsync(Guid variationId, Guid locationId, CancellationToken ct);

    void Add(ItemVariationLocationOverride row);

    void Remove(ItemVariationLocationOverride row);
}

public sealed record VariationPrice(Guid Id, decimal Price);
