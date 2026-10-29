using CafePos.Application.Common;
using CafePos.Application.Features.Inventory;
using CafePos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

internal sealed class StockRepository(CafeDbContext db) : IStockRepository
{
    public Task<decimal?> GetOnHandAsync(Guid ingredientId, CancellationToken ct) =>
        db.IngredientStocks.Where(s => s.IngredientId == ingredientId).Select(s => (decimal?)s.QuantityOnHand).FirstOrDefaultAsync(ct);

    public async Task EnsureStockRowAsync(Guid locationId, Guid ingredientId, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""IngredientStocks"" (""LocationId"", ""IngredientId"", ""QuantityOnHand"")
            VALUES ({locationId}, {ingredientId}, 0)
            ON CONFLICT (""LocationId"", ""IngredientId"") DO NOTHING", ct);

    public async Task<bool> TryApplyDeltaAsync(Guid locationId, Guid ingredientId, decimal delta, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE ""IngredientStocks""
            SET ""QuantityOnHand"" = ""QuantityOnHand"" + {delta}
            WHERE ""LocationId"" = {locationId} AND ""IngredientId"" = {ingredientId} AND ""QuantityOnHand"" + {delta} >= 0", ct) > 0;

    public async Task<bool> TryDecrementAsync(Guid locationId, Guid ingredientId, decimal quantity, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE ""IngredientStocks""
            SET ""QuantityOnHand"" = ""QuantityOnHand"" - {quantity}
            WHERE ""LocationId"" = {locationId} AND ""IngredientId"" = {ingredientId} AND ""QuantityOnHand"" >= {quantity}", ct) > 0;

    public void AddMovement(StockMovement movement) => db.StockMovements.Add(movement);

    public async Task<PagedResult<StockMovement>> ListMovementsAsync(Guid ingredientId, LocationScope scope, int page, int pageSize, CancellationToken ct)
    {
        var locationId = scope.LocationId;
        var q = (scope.CrossLocation
                ? db.StockMovements.IgnoreQueryFilters().Where(m => m.LocationId == locationId)
                : db.StockMovements).AsNoTracking().Where(m => m.IngredientId == ingredientId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(m => m.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<StockMovement>(rows, page, pageSize, total);
    }

    public async Task<IReadOnlyList<Guid>> GetUntrackedVariationIdsAsync(Guid locationId, IReadOnlyCollection<Guid> variationIds, CancellationToken ct)
    {
        var ids = variationIds.ToList();
        return await db.ItemVariationLocationOverrides.AsNoTracking()
            .Where(o => o.LocationId == locationId && o.TrackInventoryOverride == false && ids.Contains(o.ItemVariationId))
            .Select(o => o.ItemVariationId).ToListAsync(ct);
    }
}
