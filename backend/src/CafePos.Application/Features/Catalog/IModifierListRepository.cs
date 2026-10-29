using CafePos.Domain;

namespace CafePos.Application.Features.Catalog;

public interface IModifierListRepository
{
    /// <summary>Modifier lists with their modifiers (read-only), by name; inactive lists only when requested.</summary>
    Task<IReadOnlyList<ModifierList>> ListAsync(bool includeInactive, CancellationToken ct);

    /// <summary>List with modifiers, tracked for modification.</summary>
    Task<ModifierList?> GetForUpdateAsync(Guid id, CancellationToken ct);

    /// <summary>How many of the given ids exist.</summary>
    Task<int> CountExistingAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    void Add(ModifierList list);
}
