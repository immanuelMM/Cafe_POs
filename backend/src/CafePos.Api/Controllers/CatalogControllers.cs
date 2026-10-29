using CafePos.Api.Data;
using CafePos.Domain;
using CafePos.Api.Dtos;
using CafePos.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CafePos.Api.Controllers;

/// <summary>Read: any authenticated role. Write: Manager/Admin. Deletes deactivate (soft delete).</summary>
[ApiController]
[Route("api/v1/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly CafeDbContext _db;
    private readonly ILogger<CategoriesController> _logger;
    public CategoriesController(CafeDbContext db, ILogger<CategoriesController> logger) { _db = db; _logger = logger; }

    private static CategoryDto Map(Category c) => new(c.Id, c.Name, c.SortOrder, c.Active);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var q = _db.Categories.AsNoTracking().AsQueryable();
        if (!(includeInactive && User.IsManagerUp())) q = q.Where(c => c.Active);
        return (await q.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct)).Select(Map).ToList();
    }

    [HttpPost, Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<CategoryDto>> Create(UpsertCategoryRequest req, CancellationToken ct)
    {
        var c = new Category { Id = Guid.NewGuid(), Name = req.Name.Trim(), SortOrder = req.SortOrder, Active = req.Active ?? true };
        _db.Categories.Add(c);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Category {CategoryId} created", c.Id);
        return Created($"/api/v1/categories/{c.Id}", Map(c));
    }

    [HttpPut("{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<CategoryDto>> Update(Guid id, UpsertCategoryRequest req, CancellationToken ct)
    {
        var c = await _db.Categories.FindAsync(new object[] { id }, ct) ?? throw ApiException.NotFound("Category");
        c.Name = req.Name.Trim();
        c.SortOrder = req.SortOrder;
        if (req.Active is not null) c.Active = req.Active.Value;
        await _db.SaveChangesAsync(ct);
        return Map(c);
    }

    [HttpDelete("{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var c = await _db.Categories.FindAsync(new object[] { id }, ct) ?? throw ApiException.NotFound("Category");
        c.Active = false;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1")]
[Authorize]
public class ItemsController : ControllerBase
{
    private readonly CafeDbContext _db;
    private readonly ILogger<ItemsController> _logger;
    public ItemsController(CafeDbContext db, ILogger<ItemsController> logger) { _db = db; _logger = logger; }

    /// <summary>Price overrides at the caller's active location, by variation id (empty for an Owner without an active location).</summary>
    private async Task<Dictionary<Guid, decimal>> PriceOverridesAsync(CancellationToken ct)
    {
        var loc = User.ActiveLocationId();
        if (loc is null) return new();
        return await _db.ItemVariationLocationOverrides.AsNoTracking()
            .Where(o => o.LocationId == loc && o.PriceOverride != null)
            .ToDictionaryAsync(o => o.ItemVariationId, o => o.PriceOverride!.Value, ct);
    }

    private static VariationDto Map(ItemVariation v, IReadOnlyDictionary<Guid, decimal> prices) =>
        new(v.Id, v.ItemId, v.Name, v.Sku, v.Price, v.Active, prices.TryGetValue(v.Id, out var p) ? p : v.Price);

    private static ItemDto Map(Item i, bool includeInactiveVariations, IReadOnlyDictionary<Guid, decimal> prices) => new(i.Id, i.CategoryId, i.Name, i.Description, i.SortOrder, i.Active,
        i.Variations.Where(v => includeInactiveVariations || v.Active).OrderBy(v => v.Price).Select(v => Map(v, prices)).ToList(),
        i.ModifierLists.Select(m => m.ModifierListId).ToList(),
        i.ImageUpdatedAt is null ? null : $"/api/v1/items/{i.Id}/image?v={i.ImageUpdatedAt.Value.Ticks}");

    private IQueryable<Item> Query() => _db.Items.Include(i => i.Variations).Include(i => i.ModifierLists);

    [HttpGet("items")]
    public async Task<ActionResult<PagedResult<ItemDto>>> List([FromQuery] Guid? categoryId, [FromQuery] string? search,
        [FromQuery] bool includeInactive = false, [FromQuery] int? page = null, [FromQuery] int? pageSize = null, CancellationToken ct = default)
    {
        var inactive = includeInactive && User.IsManagerUp();
        var (p, ps) = Paging.Normalize(page, pageSize);
        var q = Query().AsNoTracking().AsSplitQuery().AsQueryable();
        if (!inactive) q = q.Where(i => i.Active);
        if (categoryId is not null) q = q.Where(i => i.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(i => EF.Functions.ILike(i.Name, $"%{search.Trim()}%"));
        var total = await q.CountAsync(ct);
        var prices = await PriceOverridesAsync(ct);
        var rows = await q.OrderBy(i => i.SortOrder).ThenBy(i => i.Name).Skip((p - 1) * ps).Take(ps).ToListAsync(ct);
        return new PagedResult<ItemDto>(rows.Select(i => Map(i, inactive, prices)).ToList(), p, ps, total);
    }

    [HttpGet("items/{id:guid}")]
    public async Task<ActionResult<ItemDto>> Get(Guid id, CancellationToken ct)
    {
        var item = await Query().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw ApiException.NotFound("Item");
        return Map(item, User.IsManagerUp(), await PriceOverridesAsync(ct));
    }

    [HttpPost("items"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<ItemDto>> Create(UpsertItemRequest req, CancellationToken ct)
    {
        if (req.Variations is null || req.Variations.Count == 0)
            throw ApiException.Unprocessable("Variation required", "An item needs at least one variation (name, sku, price).");
        await EnsureCategoryAsync(req.CategoryId, ct);
        await EnsureModifierListsAsync(req.ModifierListIds, ct);
        var skus = req.Variations.Select(v => v.Sku.Trim()).ToList();
        if (skus.Distinct(StringComparer.OrdinalIgnoreCase).Count() != skus.Count) throw ApiException.Conflict("Duplicate SKU", "SKUs in the request must be unique.");
        await EnsureSkusFreeAsync(skus, null, ct);

        var item = new Item
        {
            Id = Guid.NewGuid(), CategoryId = req.CategoryId, Name = req.Name.Trim(), Description = req.Description,
            SortOrder = req.SortOrder, Active = req.Active ?? true
        };
        foreach (var v in req.Variations)
            item.Variations.Add(new ItemVariation { Id = Guid.NewGuid(), ItemId = item.Id, Name = v.Name.Trim(), Sku = v.Sku.Trim(), Price = v.Price, Active = v.Active ?? true });
        foreach (var mid in (req.ModifierListIds ?? new()).Distinct())
            item.ModifierLists.Add(new ItemModifierList { ItemId = item.Id, ModifierListId = mid });
        _db.Items.Add(item);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Item {ItemId} created with {Count} variations", item.Id, item.Variations.Count);
        return Created($"/api/v1/items/{item.Id}", Map(item, true, await PriceOverridesAsync(ct)));
    }

    [HttpPut("items/{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<ItemDto>> Update(Guid id, UpsertItemRequest req, CancellationToken ct)
    {
        var item = await Query().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw ApiException.NotFound("Item");
        await EnsureCategoryAsync(req.CategoryId, ct);
        await EnsureModifierListsAsync(req.ModifierListIds, ct);
        item.CategoryId = req.CategoryId;
        item.Name = req.Name.Trim();
        item.Description = req.Description;
        item.SortOrder = req.SortOrder;
        if (req.Active is not null) item.Active = req.Active.Value;
        if (req.ModifierListIds is not null)
        {
            var wanted = req.ModifierListIds.Distinct().ToHashSet();
            foreach (var link in item.ModifierLists.Where(l => !wanted.Contains(l.ModifierListId)).ToList())
            {
                item.ModifierLists.Remove(link);
                _db.ItemModifierLists.Remove(link);
            }
            foreach (var mid in wanted.Where(w => item.ModifierLists.All(l => l.ModifierListId != w)))
                item.ModifierLists.Add(new ItemModifierList { ItemId = item.Id, ModifierListId = mid });
        }
        await _db.SaveChangesAsync(ct);
        return Map(item, true, await PriceOverridesAsync(ct));
    }

    [HttpDelete("items/{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var item = await _db.Items.FindAsync(new object[] { id }, ct) ?? throw ApiException.NotFound("Item");
        item.Active = false;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- photo ----
    private const int ImageMaxBytes = 2 * 1024 * 1024;
    // Headroom above the file limit so an oversize file is answered with a 400, not a transport-level 413.
    private const long ImageRequestLimit = 4L * 1024 * 1024;

    /// <summary>Item photo bytes. Anonymous like the brand logo; the URL is versioned so it is cached immutably.</summary>
    [HttpGet("items/{id:guid}/image"), AllowAnonymous]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken ct)
    {
        var img = await _db.ItemImages.AsNoTracking().FirstOrDefaultAsync(x => x.ItemId == id, ct);
        return Uploads.Serve(this, img?.Data, img?.ContentType);
    }

    [HttpPut("items/{id:guid}/image"), Authorize(Roles = Roles.ManagerUp)]
    [RequestSizeLimit(ImageRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImageRequestLimit)]
    public async Task<ActionResult<ItemDto>> PutImage(Guid id, [FromForm] UploadFileRequest req, CancellationToken ct)
    {
        var item = await Query().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw ApiException.NotFound("Item");
        var (data, type) = await Uploads.ReadAsync(req.File, Uploads.ImageTypes, ImageMaxBytes, "Item photo", ct);
        var img = await _db.ItemImages.FirstOrDefaultAsync(x => x.ItemId == id, ct);
        if (img is null) _db.ItemImages.Add(img = new ItemImage { ItemId = id });
        img.Data = data;
        img.ContentType = type;
        item.ImageUpdatedAt = Uploads.VersionStamp();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Item {ItemId} photo uploaded by {UserId} ({Type}, {Bytes} bytes)", id, User.RequireUserId(), type, data.Length);
        return Map(item, true, await PriceOverridesAsync(ct));
    }

    [HttpDelete("items/{id:guid}/image"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<ItemDto>> DeleteImage(Guid id, CancellationToken ct)
    {
        var item = await Query().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw ApiException.NotFound("Item");
        await _db.ItemImages.Where(x => x.ItemId == id).ExecuteDeleteAsync(ct);
        item.ImageUpdatedAt = null;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Item {ItemId} photo removed by {UserId}", id, User.RequireUserId());
        return Map(item, true, await PriceOverridesAsync(ct));
    }

    // ---- variations ----
    [HttpGet("items/{id:guid}/variations")]
    public async Task<ActionResult<IReadOnlyList<VariationDto>>> Variations(Guid id, CancellationToken ct)
    {
        if (!await _db.Items.AnyAsync(i => i.Id == id, ct)) throw ApiException.NotFound("Item");
        var inactive = User.IsManagerUp();
        var prices = await PriceOverridesAsync(ct);
        return (await _db.ItemVariations.AsNoTracking().Where(v => v.ItemId == id && (inactive || v.Active)).OrderBy(v => v.Price).ToListAsync(ct))
            .Select(v => Map(v, prices)).ToList();
    }

    [HttpPost("items/{id:guid}/variations"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<VariationDto>> AddVariation(Guid id, UpsertVariationRequest req, CancellationToken ct)
    {
        if (!await _db.Items.AnyAsync(i => i.Id == id, ct)) throw ApiException.NotFound("Item");
        await EnsureSkusFreeAsync(new[] { req.Sku.Trim() }, null, ct);
        var v = new ItemVariation { Id = Guid.NewGuid(), ItemId = id, Name = req.Name.Trim(), Sku = req.Sku.Trim(), Price = req.Price, Active = req.Active ?? true };
        _db.ItemVariations.Add(v);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/v1/variations/{v.Id}", Map(v, await PriceOverridesAsync(ct)));
    }

    [HttpPut("variations/{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<VariationDto>> UpdateVariation(Guid id, UpsertVariationRequest req, CancellationToken ct)
    {
        var v = await _db.ItemVariations.FindAsync(new object[] { id }, ct) ?? throw ApiException.NotFound("Variation");
        await EnsureSkusFreeAsync(new[] { req.Sku.Trim() }, id, ct);
        v.Name = req.Name.Trim();
        v.Sku = req.Sku.Trim();
        v.Price = req.Price;
        if (req.Active is not null) v.Active = req.Active.Value;
        await _db.SaveChangesAsync(ct);
        return Map(v, await PriceOverridesAsync(ct));
    }

    [HttpDelete("variations/{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<IActionResult> DeactivateVariation(Guid id, CancellationToken ct)
    {
        var v = await _db.ItemVariations.FindAsync(new object[] { id }, ct) ?? throw ApiException.NotFound("Variation");
        v.Active = false;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task EnsureCategoryAsync(Guid id, CancellationToken ct)
    {
        if (!await _db.Categories.AnyAsync(c => c.Id == id, ct)) throw ApiException.Unprocessable("Invalid category", "Category does not exist.");
    }

    private async Task EnsureModifierListsAsync(List<Guid>? ids, CancellationToken ct)
    {
        if (ids is null || ids.Count == 0) return;
        var distinct = ids.Distinct().ToList();
        if (await _db.ModifierLists.CountAsync(m => distinct.Contains(m.Id), ct) != distinct.Count)
            throw ApiException.Unprocessable("Invalid modifier list", "One or more modifier lists do not exist.");
    }

    private async Task EnsureSkusFreeAsync(IEnumerable<string> skus, Guid? exceptVariationId, CancellationToken ct)
    {
        var list = skus.ToList();
        var taken = await _db.ItemVariations.AsNoTracking().Where(v => list.Contains(v.Sku) && v.Id != exceptVariationId).Select(v => v.Sku).FirstOrDefaultAsync(ct);
        if (taken is not null) throw ApiException.Conflict("Duplicate SKU", $"SKU '{taken}' is already in use.");
    }
}

[ApiController]
[Route("api/v1/modifier-lists")]
[Authorize]
public class ModifierListsController : ControllerBase
{
    private readonly CafeDbContext _db;
    public ModifierListsController(CafeDbContext db) => _db = db;

    private static ModifierListDto Map(ModifierList l, bool inactive) => new(l.Id, l.Name, l.MinSelections, l.MaxSelections, l.Active,
        l.Modifiers.Where(m => inactive || m.Active).OrderBy(m => m.Name).Select(m => new ModifierDto(m.Id, m.Name, m.PriceDelta, m.Active)).ToList());

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ModifierListDto>>> List(CancellationToken ct)
    {
        var inactive = User.IsManagerUp();
        var rows = await _db.ModifierLists.AsNoTracking().Include(l => l.Modifiers).Where(l => inactive || l.Active).OrderBy(l => l.Name).ToListAsync(ct);
        return rows.Select(l => Map(l, inactive)).ToList();
    }

    [HttpPost, Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<ModifierListDto>> Create(UpsertModifierListRequest req, CancellationToken ct)
    {
        Validate(req);
        var list = new ModifierList { Id = Guid.NewGuid(), Name = req.Name.Trim(), MinSelections = req.MinSelections, MaxSelections = req.MaxSelections };
        foreach (var m in req.Modifiers)
            list.Modifiers.Add(new Modifier { Id = Guid.NewGuid(), ModifierListId = list.Id, Name = m.Name.Trim(), PriceDelta = m.PriceDelta });
        _db.ModifierLists.Add(list);
        await _db.SaveChangesAsync(ct);
        return Created($"/api/v1/modifier-lists/{list.Id}", Map(list, true));
    }

    /// <summary>Replaces the modifier set: entries with a known id are updated, new ones created, omitted ones deactivated.</summary>
    [HttpPut("{id:guid}"), Authorize(Roles = Roles.ManagerUp)]
    public async Task<ActionResult<ModifierListDto>> Update(Guid id, UpsertModifierListRequest req, CancellationToken ct)
    {
        Validate(req);
        var list = await _db.ModifierLists.Include(l => l.Modifiers).FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw ApiException.NotFound("Modifier list");
        list.Name = req.Name.Trim();
        list.MinSelections = req.MinSelections;
        list.MaxSelections = req.MaxSelections;
        var keep = new HashSet<Guid>();
        foreach (var m in req.Modifiers)
        {
            var existing = m.Id is null ? null : list.Modifiers.FirstOrDefault(x => x.Id == m.Id);
            if (m.Id is not null && existing is null) throw ApiException.Unprocessable("Invalid modifier", $"Modifier {m.Id} is not part of this list.");
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
        await _db.SaveChangesAsync(ct);
        return Map(list, true);
    }

    private static void Validate(UpsertModifierListRequest req)
    {
        if (req.MinSelections > req.MaxSelections)
            throw ApiException.Unprocessable("Invalid selection rules", "minSelections cannot exceed maxSelections.");
    }
}
