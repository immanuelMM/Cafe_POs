using CafePos.Application.Features.Inventory;
using CafePos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

internal sealed class RecipeRepository(CafeDbContext db) : IRecipeRepository
{
    public Task<bool> VariationExistsAsync(Guid variationId, CancellationToken ct) => db.ItemVariations.AnyAsync(v => v.Id == variationId, ct);

    public async Task<IReadOnlyList<RecipeLine>> ListLinesAsync(Guid variationId, CancellationToken ct) =>
        await db.RecipeLines.AsNoTracking().Include(r => r.Ingredient)
            .Where(r => r.ItemVariationId == variationId).OrderBy(r => r.Ingredient!.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<RecipeLine>> ListLinesForVariationsAsync(IReadOnlyCollection<Guid> variationIds, CancellationToken ct)
    {
        var ids = variationIds.ToList();
        return await db.RecipeLines.AsNoTracking().Where(r => ids.Contains(r.ItemVariationId)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RecipeLine>> GetLinesForUpdateAsync(Guid variationId, CancellationToken ct) =>
        await db.RecipeLines.Where(r => r.ItemVariationId == variationId).ToListAsync(ct);

    public void AddLine(RecipeLine line) => db.RecipeLines.Add(line);

    public void RemoveLines(IEnumerable<RecipeLine> lines) => db.RecipeLines.RemoveRange(lines);
}
