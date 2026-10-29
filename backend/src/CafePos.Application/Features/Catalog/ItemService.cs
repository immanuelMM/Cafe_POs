using CafePos.Application.Common;
using CafePos.Application.Common.Uploads;
using CafePos.Domain;
using Microsoft.Extensions.Logging;

namespace CafePos.Application.Features.Catalog;

/// <summary>Items, their variations and photos. Read: any authenticated role; inactive rows only for Manager and above.</summary>
public sealed class ItemService(
    ICurrentUser user,
    IItemRepository items,
    ICategoryRepository categories,
    IModifierListRepository modifierLists,
    IUnitOfWork uow,
    TimeProvider clock,
    ILogger<ItemService> logger)
{
    public const int ImageMaxBytes = 2 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<Guid, decimal> NoPrices = new Dictionary<Guid, decimal>();

    /// <summary>Price overrides at the caller's active location (empty for an Owner without an active location).</summary>
    private async Task<IReadOnlyDictionary<Guid, decimal>> PricesAsync(CancellationToken ct) =>
        user.LocationId is { } location ? await items.GetPriceOverridesAsync(location, ct) : NoPrices;

    public async Task<Result<PagedResult<ItemDto>>> ListAsync(Guid? categoryId, string? search, bool includeInactive, int? page, int? pageSize, CancellationToken ct)
    {
        var inactive = includeInactive && user.IsManagerUp();
        var (p, ps) = Paging.Normalize(page, pageSize);
        var result = await items.ListAsync(new ItemListQuery(categoryId, search, inactive, p, ps), ct);
        var prices = await PricesAsync(ct);
        return new PagedResult<ItemDto>(result.Items.Select(i => i.ToDto(inactive, prices)).ToList(), p, ps, result.TotalCount);
    }

    public async Task<Result<ItemDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var item = await items.GetAsync(id, ct);
        if (item is null) return Error.NotFound("Item");
        return item.ToDto(user.IsManagerUp(), await PricesAsync(ct));
    }

    public async Task<Result<ItemDto>> CreateAsync(UpsertItemRequest req, CancellationToken ct)
    {
        if (req.Variations is null || req.Variations.Count == 0)
            return Error.Unprocessable("Variation required", "An item needs at least one variation (name, sku, price).");
        var references = await EnsureReferencesAsync(req, ct);
        if (references.IsFailure) return references.Error!;
        var skus = req.Variations.Select(v => v.Sku.Trim()).ToList();
        if (skus.Distinct(StringComparer.OrdinalIgnoreCase).Count() != skus.Count)
            return Error.Conflict("Duplicate SKU", "SKUs in the request must be unique.");
        var free = await EnsureSkusFreeAsync(skus, null, ct);
        if (free.IsFailure) return free.Error!;

        var item = new Item
        {
            Id = Guid.NewGuid(), CategoryId = req.CategoryId, Name = req.Name.Trim(), Description = req.Description,
            SortOrder = req.SortOrder, Active = req.Active ?? true
        };
        foreach (var v in req.Variations)
            item.Variations.Add(new ItemVariation { Id = Guid.NewGuid(), ItemId = item.Id, Name = v.Name.Trim(), Sku = v.Sku.Trim(), Price = v.Price, Active = v.Active ?? true });
        foreach (var mid in (req.ModifierListIds ?? new()).Distinct())
            item.ModifierLists.Add(new ItemModifierList { ItemId = item.Id, ModifierListId = mid });
        items.Add(item);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Item {ItemId} created with {Count} variations", item.Id, item.Variations.Count);
        return item.ToDto(true, await PricesAsync(ct));
    }

    public async Task<Result<ItemDto>> UpdateAsync(Guid id, UpsertItemRequest req, CancellationToken ct)
    {
        var item = await items.GetForUpdateAsync(id, ct);
        if (item is null) return Error.NotFound("Item");
        var references = await EnsureReferencesAsync(req, ct);
        if (references.IsFailure) return references.Error!;

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
                items.RemoveModifierLink(link);
            }
            foreach (var mid in wanted.Where(w => item.ModifierLists.All(l => l.ModifierListId != w)))
                item.ModifierLists.Add(new ItemModifierList { ItemId = item.Id, ModifierListId = mid });
        }
        await uow.SaveChangesAsync(ct);
        return item.ToDto(true, await PricesAsync(ct));
    }

    public async Task<Result> DeactivateAsync(Guid id, CancellationToken ct)
    {
        var item = await items.GetBasicForUpdateAsync(id, ct);
        if (item is null) return Error.NotFound("Item");
        item.Active = false;
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---- photo ----

    /// <summary>Item photo bytes, or null when the item has none.</summary>
    public Task<StoredImage?> GetImageAsync(Guid id, CancellationToken ct) => items.GetImageAsync(id, ct);

    public async Task<Result<ItemDto>> PutImageAsync(Guid id, UploadedFile? file, CancellationToken ct)
    {
        var item = await items.GetForUpdateAsync(id, ct);
        if (item is null) return Error.NotFound("Item");
        var upload = await UploadRules.ValidateAsync(file, UploadRules.ImageTypes, ImageMaxBytes, "Item photo", ct);
        if (upload.IsFailure) return upload.Error!;
        var (data, type) = (upload.Value.Data, upload.Value.ContentType);

        var image = await items.GetImageForUpdateAsync(id, ct);
        if (image is null) items.AddImage(image = new ItemImage { ItemId = id });
        image.Data = data;
        image.ContentType = type;
        item.ImageUpdatedAt = UploadRules.VersionStamp(clock);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Item {ItemId} photo uploaded by {UserId} ({Type}, {Bytes} bytes)", id, user.UserId, type, data.Length);
        return item.ToDto(true, await PricesAsync(ct));
    }

    public async Task<Result<ItemDto>> DeleteImageAsync(Guid id, CancellationToken ct)
    {
        var item = await items.GetForUpdateAsync(id, ct);
        if (item is null) return Error.NotFound("Item");
        await items.DeleteImageAsync(id, ct);
        item.ImageUpdatedAt = null;
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Item {ItemId} photo removed by {UserId}", id, user.UserId);
        return item.ToDto(true, await PricesAsync(ct));
    }

    // ---- variations ----

    public async Task<Result<IReadOnlyList<VariationDto>>> VariationsAsync(Guid id, CancellationToken ct)
    {
        if (!await items.ExistsAsync(id, ct)) return Error.NotFound("Item");
        var prices = await PricesAsync(ct);
        return (await items.ListVariationsAsync(id, user.IsManagerUp(), ct)).Select(v => v.ToDto(prices)).ToList();
    }

    public async Task<Result<VariationDto>> AddVariationAsync(Guid id, UpsertVariationRequest req, CancellationToken ct)
    {
        if (!await items.ExistsAsync(id, ct)) return Error.NotFound("Item");
        var free = await EnsureSkusFreeAsync([req.Sku.Trim()], null, ct);
        if (free.IsFailure) return free.Error!;
        var variation = new ItemVariation { Id = Guid.NewGuid(), ItemId = id, Name = req.Name.Trim(), Sku = req.Sku.Trim(), Price = req.Price, Active = req.Active ?? true };
        items.AddVariation(variation);
        await uow.SaveChangesAsync(ct);
        return variation.ToDto(await PricesAsync(ct));
    }

    public async Task<Result<VariationDto>> UpdateVariationAsync(Guid id, UpsertVariationRequest req, CancellationToken ct)
    {
        var variation = await items.GetVariationForUpdateAsync(id, ct);
        if (variation is null) return Error.NotFound("Variation");
        var free = await EnsureSkusFreeAsync([req.Sku.Trim()], id, ct);
        if (free.IsFailure) return free.Error!;
        variation.Name = req.Name.Trim();
        variation.Sku = req.Sku.Trim();
        variation.Price = req.Price;
        if (req.Active is not null) variation.Active = req.Active.Value;
        await uow.SaveChangesAsync(ct);
        return variation.ToDto(await PricesAsync(ct));
    }

    public async Task<Result> DeactivateVariationAsync(Guid id, CancellationToken ct)
    {
        var variation = await items.GetVariationForUpdateAsync(id, ct);
        if (variation is null) return Error.NotFound("Variation");
        variation.Active = false;
        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---- helpers ----

    private async Task<Result> EnsureReferencesAsync(UpsertItemRequest req, CancellationToken ct)
    {
        if (!await categories.ExistsAsync(req.CategoryId, ct)) return Error.Unprocessable("Invalid category", "Category does not exist.");
        if (req.ModifierListIds is { Count: > 0 } ids)
        {
            var distinct = ids.Distinct().ToList();
            if (await modifierLists.CountExistingAsync(distinct, ct) != distinct.Count)
                return Error.Unprocessable("Invalid modifier list", "One or more modifier lists do not exist.");
        }
        return Result.Success();
    }

    private async Task<Result> EnsureSkusFreeAsync(IReadOnlyCollection<string> skus, Guid? exceptVariationId, CancellationToken ct)
    {
        var taken = await items.FindTakenSkuAsync(skus, exceptVariationId, ct);
        return taken is null ? Result.Success() : Error.Conflict("Duplicate SKU", $"SKU '{taken}' is already in use.");
    }
}
