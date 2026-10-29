using CafePos.Api.Http;
using CafePos.Application.Features.Catalog;
using CafePos.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Controllers;

[ApiController]
[Route("api/v1/modifier-lists")]
[Authorize]
public class ModifierListsController(ModifierListService modifierLists) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ModifierListDto>>> List(CancellationToken ct) =>
        (await modifierLists.ListAsync(ct)).ToActionResult();

    [HttpPost, Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<ModifierListDto>> Create(UpsertModifierListRequest req, CancellationToken ct) =>
        (await modifierLists.CreateAsync(req, ct)).ToCreated(l => $"/api/v1/modifier-lists/{l.Id}");

    /// <summary>Replaces the modifier set: entries with a known id are updated, new ones created, omitted ones deactivated.</summary>
    [HttpPut("{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<ModifierListDto>> Update(Guid id, UpsertModifierListRequest req, CancellationToken ct) =>
        (await modifierLists.UpdateAsync(id, req, ct)).ToActionResult();
}
