using CafePos.Application.Features.Orders;
using CafePos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

internal sealed class OrderCatalogRepository(CafeDbContext db) : IOrderCatalogRepository
{
    public Task<ItemVariation?> GetVariationWithItemAsync(Guid variationId, CancellationToken ct) =>
        db.ItemVariations.AsNoTracking().Include(v => v.Item).ThenInclude(i => i!.ModifierLists)
            .FirstOrDefaultAsync(v => v.Id == variationId, ct);

    public async Task<IReadOnlyList<Modifier>> GetModifiersAsync(IReadOnlyCollection<Guid> modifierIds, CancellationToken ct)
    {
        var ids = modifierIds.ToList();
        return await db.Modifiers.AsNoTracking().Where(m => ids.Contains(m.Id)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ModifierList>> GetActiveModifierListsAsync(IReadOnlyCollection<Guid> listIds, CancellationToken ct)
    {
        var ids = listIds.ToList();
        return await db.ModifierLists.AsNoTracking().Where(l => ids.Contains(l.Id) && l.Active).ToListAsync(ct);
    }

    public Task<decimal?> GetPriceOverrideAsync(Guid variationId, Guid locationId, CancellationToken ct) =>
        db.ItemVariationLocationOverrides.AsNoTracking()
            .Where(o => o.ItemVariationId == variationId && o.LocationId == locationId).Select(o => o.PriceOverride).FirstOrDefaultAsync(ct);
}
