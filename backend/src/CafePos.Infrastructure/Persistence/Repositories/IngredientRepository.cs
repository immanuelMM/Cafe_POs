using CafePos.Application.Features.Inventory;
using CafePos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

internal sealed class IngredientRepository(CafeDbContext db) : IIngredientRepository
{
    public async Task<IReadOnlyList<Ingredient>> ListCatalogAsync(bool includeInactive, CancellationToken ct) =>
        await db.Ingredients.AsNoTracking().Where(i => includeInactive || i.Active).OrderBy(i => i.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<IngredientStockRow>> ListWithStockAsync(bool includeInactive, LocationScope scope, CancellationToken ct)
    {
        // Left join onto the per-location stock rows. Active location: the global query filter applies. Another location
        // (Owner only, resolved by the service): explicit IgnoreQueryFilters restricted to that one location.
        var locationId = scope.LocationId;
        var stocks = scope.CrossLocation
            ? db.IngredientStocks.IgnoreQueryFilters().AsNoTracking().Where(s => s.LocationId == locationId)
            : db.IngredientStocks.AsNoTracking();
        var rows = await (from i in db.Ingredients.AsNoTracking()
                          where includeInactive || i.Active
                          join s in stocks on i.Id equals s.IngredientId into ss
                          from s in ss.DefaultIfEmpty()
                          orderby i.Name
                          select new { Ingredient = i, OnHand = s == null ? 0m : s.QuantityOnHand }).ToListAsync(ct);
        return rows.Select(r => new IngredientStockRow(r.Ingredient, r.OnHand)).ToList();
    }

    public Task<Ingredient?> GetForUpdateAsync(Guid id, CancellationToken ct) => db.Ingredients.FindAsync([id], ct).AsTask();

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) => db.Ingredients.AnyAsync(i => i.Id == id, ct);

    public Task<bool> ActiveExistsAsync(Guid id, CancellationToken ct) => db.Ingredients.AnyAsync(i => i.Id == id && i.Active, ct);

    public Task<bool> NameExistsAsync(string name, Guid? exceptId, CancellationToken ct) =>
        db.Ingredients.AnyAsync(i => i.Name == name && i.Id != exceptId, ct);

    public Task<int> CountExistingAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        return db.Ingredients.CountAsync(i => list.Contains(i.Id), ct);
    }

    public Task<string?> GetNameAsync(Guid id, CancellationToken ct) =>
        db.Ingredients.Where(i => i.Id == id).Select(i => i.Name).FirstOrDefaultAsync(ct);

    public void Add(Ingredient ingredient) => db.Ingredients.Add(ingredient);
}
