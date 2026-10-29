using CafePos.Api.Http;
using CafePos.Application.Common;
using CafePos.Application.Features.Inventory;
using CafePos.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CafePos.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
public class InventoryController(IngredientService ingredients, RecipeService recipes) : ControllerBase
{
    [HttpGet("ingredients")]
    public async Task<ActionResult<IReadOnlyList<IngredientDto>>> List([FromQuery] bool includeInactive = false, [FromQuery] Guid? locationId = null, CancellationToken ct = default) =>
        (await ingredients.ListAsync(includeInactive, locationId, ct)).ToActionResult();

    /// <summary>Ingredients at or below their low-stock threshold at the active location (or `locationId`, Owner only).</summary>
    [HttpGet("ingredients/low-stock")]
    public async Task<ActionResult<IReadOnlyList<IngredientDto>>> LowStock([FromQuery] Guid? locationId, CancellationToken ct) =>
        (await ingredients.LowStockAsync(locationId, ct)).ToActionResult();

    [HttpPost("ingredients"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<IngredientDto>> Create(CreateIngredientRequest req, CancellationToken ct) =>
        (await ingredients.CreateAsync(req, ct)).ToCreated(i => $"/api/v1/ingredients/{i.Id}");

    [HttpPut("ingredients/{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<IngredientDto>> Update(Guid id, UpdateIngredientRequest req, CancellationToken ct) =>
        (await ingredients.UpdateAsync(id, req, ct)).ToActionResult();

    /// <summary>Ledger history for one ingredient at the active location (or `locationId`, Owner only), newest first.</summary>
    [HttpGet("ingredients/{id:guid}/movements"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<PagedResult<StockMovementDto>>> Movements(Guid id, [FromQuery] Guid? locationId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        (await ingredients.MovementsAsync(id, locationId, page, pageSize, ct)).ToActionResult();

    /// <summary>Record a restock (purchase) at the active location.</summary>
    [HttpPost("restocks"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<StockMovementDto>> Restock(RestockRequest req, CancellationToken ct) =>
        (await ingredients.RestockAsync(req, ct)).ToCreated(m => $"/api/v1/ingredients/{m.IngredientId}/movements");

    /// <summary>Manual stock correction or waste. Posts a signed ledger entry.</summary>
    [HttpPost("ingredients/{id:guid}/adjustments"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<StockMovementDto>> Adjust(Guid id, AdjustmentRequest req, CancellationToken ct) =>
        (await ingredients.AdjustAsync(id, req, ct)).ToCreated(m => $"/api/v1/ingredients/{id}/movements");

    // ---- recipes ----
    [HttpGet("variations/{id:guid}/recipe")]
    [Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<RecipeDto>> GetRecipe(Guid id, CancellationToken ct) =>
        (await recipes.GetAsync(id, ct)).ToActionResult();

    /// <summary>Replaces the whole recipe (global across locations). An empty list clears it.</summary>
    [HttpPut("variations/{id:guid}/recipe"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<RecipeDto>> SetRecipe(Guid id, SetRecipeRequest req, CancellationToken ct) =>
        (await recipes.SetAsync(id, req, ct)).ToActionResult();
}
