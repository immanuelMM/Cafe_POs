using CafePos.Application.Features.Catalog;
using CafePos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

internal sealed class CategoryRepository(CafeDbContext db) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = db.Categories.AsNoTracking().AsQueryable();
        if (!includeInactive) q = q.Where(c => c.Active);
        return await q.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) => db.Categories.AnyAsync(c => c.Id == id, ct);

    public Task<Category?> GetForUpdateAsync(Guid id, CancellationToken ct) => db.Categories.FindAsync([id], ct).AsTask();

    public void Add(Category category) => db.Categories.Add(category);
}
