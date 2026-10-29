using CafePos.Application.Common.Uploads;
using CafePos.Application.Features.Catalog;
using CafePos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

internal sealed class ItemRepository(CafeDbContext db) : IItemRepository
{
    private IQueryable<Item> WithDetails => db.Items.Include(i => i.Variations).Include(i => i.ModifierLists);

    public async Task<ItemPage> ListAsync(ItemListQuery query, CancellationToken ct)
    {
        var categoryId = query.CategoryId;
        var q = WithDetails.AsNoTracking().AsSplitQuery().AsQueryable();
        if (!query.IncludeInactive) q = q.Where(i => i.Active);
        if (categoryId is not null) q = q.Where(i => i.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = $"%{query.Search.Trim()}%";
            q = q.Where(i => EF.Functions.ILike(i.Name, like));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(i => i.SortOrder).ThenBy(i => i.Name).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new ItemPage(rows, total);
    }

    public Task<Item?> GetAsync(Guid id, CancellationToken ct) => WithDetails.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<Item?> GetForUpdateAsync(Guid id, CancellationToken ct) => WithDetails.FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<Item?> GetBasicForUpdateAsync(Guid id, CancellationToken ct) => db.Items.FindAsync([id], ct).AsTask();

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) => db.Items.AnyAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetPriceOverridesAsync(Guid locationId, CancellationToken ct) =>
        await db.ItemVariationLocationOverrides.AsNoTracking()
            .Where(o => o.LocationId == locationId && o.PriceOverride != null)
            .ToDictionaryAsync(o => o.ItemVariationId, o => o.PriceOverride!.Value, ct);

    public Task<string?> FindTakenSkuAsync(IReadOnlyCollection<string> skus, Guid? exceptVariationId, CancellationToken ct)
    {
        var list = skus.ToList();
        return db.ItemVariations.AsNoTracking().Where(v => list.Contains(v.Sku) && v.Id != exceptVariationId).Select(v => v.Sku).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<ItemVariation>> ListVariationsAsync(Guid itemId, bool includeInactive, CancellationToken ct) =>
        await db.ItemVariations.AsNoTracking().Where(v => v.ItemId == itemId && (includeInactive || v.Active)).OrderBy(v => v.Price).ToListAsync(ct);

    public Task<ItemVariation?> GetVariationForUpdateAsync(Guid id, CancellationToken ct) => db.ItemVariations.FindAsync([id], ct).AsTask();

    public Task<StoredImage?> GetImageAsync(Guid itemId, CancellationToken ct) =>
        db.ItemImages.AsNoTracking().Where(x => x.ItemId == itemId).Select(x => new StoredImage(x.Data, x.ContentType)).FirstOrDefaultAsync(ct);

    public Task<ItemImage?> GetImageForUpdateAsync(Guid itemId, CancellationToken ct) => db.ItemImages.FirstOrDefaultAsync(x => x.ItemId == itemId, ct);

    public void Add(Item item) => db.Items.Add(item);

    public void AddVariation(ItemVariation variation) => db.ItemVariations.Add(variation);

    public void AddImage(ItemImage image) => db.ItemImages.Add(image);

    public void RemoveModifierLink(ItemModifierList link) => db.ItemModifierLists.Remove(link);

    public Task DeleteImageAsync(Guid itemId, CancellationToken ct) => db.ItemImages.Where(x => x.ItemId == itemId).ExecuteDeleteAsync(ct);
}
