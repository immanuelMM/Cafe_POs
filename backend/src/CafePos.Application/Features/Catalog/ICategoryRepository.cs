using CafePos.Domain;

namespace CafePos.Application.Features.Catalog;

public interface ICategoryRepository
{
    /// <summary>Categories ordered by sort order then name.</summary>
    Task<IReadOnlyList<Category>> ListAsync(bool includeInactive, CancellationToken ct);

    Task<bool> ExistsAsync(Guid id, CancellationToken ct);

    /// <summary>Tracked lookup for modification.</summary>
    Task<Category?> GetForUpdateAsync(Guid id, CancellationToken ct);

    void Add(Category category);
}
