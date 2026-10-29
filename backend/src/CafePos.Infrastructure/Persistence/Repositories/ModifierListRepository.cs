using CafePos.Application.Features.Catalog;
using CafePos.Domain;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Infrastructure.Persistence.Repositories;

internal sealed class ModifierListRepository(CafeDbContext db) : IModifierListRepository
{
    public async Task<IReadOnlyList<ModifierList>> ListAsync(bool includeInactive, CancellationToken ct) =>
        await db.ModifierLists.AsNoTracking().Include(l => l.Modifiers)
            .Where(l => includeInactive || l.Active).OrderBy(l => l.Name).ToListAsync(ct);

    public Task<ModifierList?> GetForUpdateAsync(Guid id, CancellationToken ct) =>
        db.ModifierLists.Include(l => l.Modifiers).FirstOrDefaultAsync(l => l.Id == id, ct);

    public Task<int> CountExistingAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var list = ids.ToList();
        return db.ModifierLists.CountAsync(m => list.Contains(m.Id), ct);
    }

    public void Add(ModifierList list) => db.ModifierLists.Add(list);
}
