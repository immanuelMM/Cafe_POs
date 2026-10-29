using CafePos.Application.Features.VariationOverrides;
using CafePos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

internal sealed class VariationOverrideRepository(CafeDbContext db) : IVariationOverrideRepository
{
    public Task<VariationPrice?> GetVariationAsync(Guid variationId, CancellationToken ct) =>
        db.ItemVariations.AsNoTracking().Where(v => v.Id == variationId).Select(v => new VariationPrice(v.Id, v.Price)).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ItemVariationLocationOverride>> ListAsync(Guid variationId, Guid? locationId, CancellationToken ct)
    {
        var q = db.ItemVariationLocationOverrides.AsNoTracking().Where(o => o.ItemVariationId == variationId);
        if (locationId is { } loc) q = q.Where(o => o.LocationId == loc);
        return await q.ToListAsync(ct);
    }

    public Task<ItemVariationLocationOverride?> GetAsync(Guid variationId, Guid locationId, CancellationToken ct) =>
        db.ItemVariationLocationOverrides.FirstOrDefaultAsync(o => o.ItemVariationId == variationId && o.LocationId == locationId, ct);

    public void Add(ItemVariationLocationOverride row) => db.ItemVariationLocationOverrides.Add(row);

    public void Remove(ItemVariationLocationOverride row) => db.ItemVariationLocationOverrides.Remove(row);
}
