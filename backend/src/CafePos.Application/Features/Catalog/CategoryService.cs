using CafePos.Application.Common;
using CafePos.Domain;
using Microsoft.Extensions.Logging;

namespace CafePos.Application.Features.Catalog;

/// <summary>Read: any authenticated role. Write: Manager/Admin (enforced by the endpoint policy). Deletes deactivate (soft delete).</summary>
public sealed class CategoryService(ICurrentUser user, ICategoryRepository categories, IUnitOfWork uow, ILogger<CategoryService> logger)
{
    public async Task<Result<IReadOnlyList<CategoryDto>>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var rows = await categories.ListAsync(includeInactive && user.IsManagerUp(), ct);
        return rows.Select(c => c.ToDto()).ToList();
    }

    public async Task<Result<CategoryDto>> CreateAsync(UpsertCategoryRequest req, CancellationToken ct)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = req.Name.Trim(), SortOrder = req.SortOrder, Active = req.Active ?? true };
        categories.Add(category);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Category {CategoryId} created", category.Id);
        return category.ToDto();
    }

    public async Task<Result<CategoryDto>> UpdateAsync(Guid id, UpsertCategoryRequest req, CancellationToken ct)
    {
        var category = await categories.GetForUpdateAsync(id, ct);
        if (category is null) return Error.NotFound("Category");
        category.Name = req.Name.Trim();
        category.SortOrder = req.SortOrder;
        if (req.Active is not null) category.Active = req.Active.Value;
        await uow.SaveChangesAsync(ct);
        return category.ToDto();
    }

    public async Task<Result> DeactivateAsync(Guid id, CancellationToken ct)
    {
        var category = await categories.GetForUpdateAsync(id, ct);
        if (category is null) return Error.NotFound("Category");
        category.Active = false;
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
