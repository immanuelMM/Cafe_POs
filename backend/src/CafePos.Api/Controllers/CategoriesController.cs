using CafePos.Api.Http;
using CafePos.Application.Features.Catalog;
using CafePos.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Controllers;

/// <summary>Read: any authenticated role. Write: Manager/Admin. Deletes deactivate (soft delete).</summary>
[ApiController]
[Route("api/v1/categories")]
[Authorize]
public class CategoriesController(CategoryService categories) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        (await categories.ListAsync(includeInactive, ct)).ToActionResult();

    [HttpPost, Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<CategoryDto>> Create(UpsertCategoryRequest req, CancellationToken ct) =>
        (await categories.CreateAsync(req, ct)).ToCreated(c => $"/api/v1/categories/{c.Id}");

    [HttpPut("{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<CategoryDto>> Update(Guid id, UpsertCategoryRequest req, CancellationToken ct) =>
        (await categories.UpdateAsync(id, req, ct)).ToActionResult();

    [HttpDelete("{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        (await categories.DeactivateAsync(id, ct)).ToNoContent();
}
