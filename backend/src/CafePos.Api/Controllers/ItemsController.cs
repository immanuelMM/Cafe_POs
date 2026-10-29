using CafePos.Api.Http;
using CafePos.Api.Auth;
using CafePos.Application.Common;
using CafePos.Application.Features.Catalog;
using CafePos.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
public class ItemsController(ItemService items) : ControllerBase
{
    [HttpGet("items")]
    public async Task<ActionResult<PagedResult<ItemDto>>> List([FromQuery] Guid? categoryId, [FromQuery] string? search,
        [FromQuery] bool includeInactive = false, [FromQuery] int? page = null, [FromQuery] int? pageSize = null, CancellationToken ct = default) =>
        (await items.ListAsync(categoryId, search, includeInactive, page, pageSize, ct)).ToActionResult();

    [HttpGet("items/{id:guid}")]
    public async Task<ActionResult<ItemDto>> Get(Guid id, CancellationToken ct) =>
        (await items.GetAsync(id, ct)).ToActionResult();

    [HttpPost("items"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<ItemDto>> Create(UpsertItemRequest req, CancellationToken ct) =>
        (await items.CreateAsync(req, ct)).ToCreated(i => $"/api/v1/items/{i.Id}");

    [HttpPut("items/{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<ItemDto>> Update(Guid id, UpsertItemRequest req, CancellationToken ct) =>
        (await items.UpdateAsync(id, req, ct)).ToActionResult();

    [HttpDelete("items/{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        (await items.DeactivateAsync(id, ct)).ToNoContent();

    // ---- photo ----
    // Headroom above the file limit so an oversize file is answered with a 400, not a transport-level 413.
    private const long ImageRequestLimit = 4L * 1024 * 1024;

    /// <summary>Item photo bytes. Anonymous like the brand logo; the URL is versioned so it is cached immutably.</summary>
    [HttpGet("items/{id:guid}/image"), AllowAnonymous]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken ct)
    {
        var image = await items.GetImageAsync(id, ct);
        return Uploads.Serve(this, image?.Data, image?.ContentType);
    }

    [HttpPut("items/{id:guid}/image"), Authorize(Roles = Roles.ManagerUp)]
    [RequestSizeLimit(ImageRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImageRequestLimit)]
    public async Task<ActionResult<ItemDto>> PutImage(Guid id, [FromForm] UploadFileRequest req, CancellationToken ct) =>
        (await items.PutImageAsync(id, Uploads.ToUploadedFile(req.File), ct)).ToActionResult();

    [HttpDelete("items/{id:guid}/image"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<ItemDto>> DeleteImage(Guid id, CancellationToken ct) =>
        (await items.DeleteImageAsync(id, ct)).ToActionResult();

    // ---- variations ----
    [HttpGet("items/{id:guid}/variations")]
    public async Task<ActionResult<IReadOnlyList<VariationDto>>> Variations(Guid id, CancellationToken ct) =>
        (await items.VariationsAsync(id, ct)).ToActionResult();

    [HttpPost("items/{id:guid}/variations"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<VariationDto>> AddVariation(Guid id, UpsertVariationRequest req, CancellationToken ct) =>
        (await items.AddVariationAsync(id, req, ct)).ToCreated(v => $"/api/v1/variations/{v.Id}");

    [HttpPut("variations/{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<VariationDto>> UpdateVariation(Guid id, UpsertVariationRequest req, CancellationToken ct) =>
        (await items.UpdateVariationAsync(id, req, ct)).ToActionResult();

    [HttpDelete("variations/{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<IActionResult> DeactivateVariation(Guid id, CancellationToken ct) =>
        (await items.DeactivateVariationAsync(id, ct)).ToNoContent();
}
