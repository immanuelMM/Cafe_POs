using CafePos.Application.Common;
using CafePos.Domain;

namespace CafePos.Application.Features.Catalog;

public sealed class ModifierListService(ICurrentUser user, IModifierListRepository lists, IUnitOfWork uow)
{
    public async Task<Result<IReadOnlyList<ModifierListDto>>> ListAsync(CancellationToken ct)
    {
        var inactive = user.IsManagerUp();
        var rows = await lists.ListAsync(inactive, ct);
        return rows.Select(l => l.ToDto(inactive)).ToList();
    }

    public async Task<Result<ModifierListDto>> CreateAsync(UpsertModifierListRequest req, CancellationToken ct)
    {
        var rules = CheckSelectionRules(req);
        if (rules.IsFailure) return rules.Error!;
        var list = new ModifierList { Id = Guid.NewGuid(), Name = req.Name.Trim(), MinSelections = req.MinSelections, MaxSelections = req.MaxSelections };
        foreach (var m in req.Modifiers)
            list.Modifiers.Add(new Modifier { Id = Guid.NewGuid(), ModifierListId = list.Id, Name = m.Name.Trim(), PriceDelta = m.PriceDelta });
        lists.Add(list);
        await uow.SaveChangesAsync(ct);
        return list.ToDto(true);
    }

    /// <summary>Replaces the modifier set: entries with a known id are updated, new ones created, omitted ones deactivated.</summary>
    public async Task<Result<ModifierListDto>> UpdateAsync(Guid id, UpsertModifierListRequest req, CancellationToken ct)
    {
        var rules = CheckSelectionRules(req);
        if (rules.IsFailure) return rules.Error!;
        var list = await lists.GetForUpdateAsync(id, ct);
        if (list is null) return Error.NotFound("Modifier list");

        list.Name = req.Name.Trim();
        list.MinSelections = req.MinSelections;
        list.MaxSelections = req.MaxSelections;
        var keep = new HashSet<Guid>();
        foreach (var m in req.Modifiers)
        {
            var existing = m.Id is null ? null : list.Modifiers.FirstOrDefault(x => x.Id == m.Id);
            if (m.Id is not null && existing is null) return Error.Unprocessable("Invalid modifier", $"Modifier {m.Id} is not part of this list.");
            if (existing is null)
            {
                existing = new Modifier { Id = Guid.NewGuid(), ModifierListId = list.Id };
                list.Modifiers.Add(existing);
            }
            existing.Name = m.Name.Trim();
            existing.PriceDelta = m.PriceDelta;
            existing.Active = true;
            keep.Add(existing.Id);
        }
        foreach (var gone in list.Modifiers.Where(x => !keep.Contains(x.Id))) gone.Active = false;
        await uow.SaveChangesAsync(ct);
        return list.ToDto(true);
    }

    private static Result CheckSelectionRules(UpsertModifierListRequest req) =>
        req.MinSelections > req.MaxSelections
            ? Error.Unprocessable("Invalid selection rules", "minSelections cannot exceed maxSelections.")
            : Result.Success();
}
