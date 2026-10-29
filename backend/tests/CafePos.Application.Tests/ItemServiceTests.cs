using CafePos.Application.Common;
using CafePos.Application.Common.Uploads;
using CafePos.Application.Features.Catalog;
using CafePos.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CafePos.Application.Tests;

public class ItemServiceTests
{
    private static readonly Guid Main = Guid.NewGuid();

    private readonly IItemRepository _items = Substitute.For<IItemRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IModifierListRepository _lists = Substitute.For<IModifierListRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public ItemServiceTests()
    {
        _categories.ExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _items.FindTakenSkuAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns((string?)null);
    }

    private ItemService Sut(ICurrentUser user) =>
        new(user, _items, _categories, _lists, _uow, TimeProvider.System, NullLogger<ItemService>.Instance);

    private static UpsertItemRequest NewItem(params string[] skus) => new()
    {
        CategoryId = Guid.NewGuid(), Name = " Latte ",
        Variations = skus.Select(s => new UpsertVariationRequest { Name = "V", Sku = s, Price = 100m }).ToList()
    };

    [Fact]
    public async Task An_item_needs_at_least_one_variation()
    {
        (await Sut(TestUser.Manager(Main)).CreateAsync(NewItem(), default)).ShouldFailWith(ErrorKind.Unprocessable, "Variation required");
    }

    [Fact]
    public async Task An_unknown_category_is_unprocessable()
    {
        _categories.ExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        (await Sut(TestUser.Manager(Main)).CreateAsync(NewItem("A"), default)).ShouldFailWith(ErrorKind.Unprocessable, "Invalid category");
    }

    [Fact]
    public async Task Duplicate_skus_in_the_request_are_a_conflict_case_insensitively()
    {
        (await Sut(TestUser.Manager(Main)).CreateAsync(NewItem("a-1", "A-1"), default)).ShouldFailWith(ErrorKind.Conflict, "Duplicate SKU");
    }

    [Fact]
    public async Task A_sku_already_in_use_is_a_conflict_naming_the_sku()
    {
        _items.FindTakenSkuAsync(Arg.Any<IReadOnlyCollection<string>>(), null, Arg.Any<CancellationToken>()).Returns("A-1");

        var error = (await Sut(TestUser.Manager(Main)).CreateAsync(NewItem("A-1"), default)).ShouldFailWith(ErrorKind.Conflict);

        Assert.Contains("A-1", error.Detail);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Creating_an_item_trims_names_and_saves()
    {
        var result = await Sut(TestUser.Manager(Main)).CreateAsync(NewItem(" A-1 "), default);

        var dto = result.ShouldSucceed();
        Assert.Equal("Latte", dto.Name);
        Assert.Equal("A-1", Assert.Single(dto.Variations).Sku);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_cashier_never_sees_inactive_variations_and_gets_the_location_price()
    {
        var item = new Item { Id = Guid.NewGuid(), Name = "Latte" };
        var active = new ItemVariation { Id = Guid.NewGuid(), ItemId = item.Id, Price = 150m, Active = true };
        item.Variations.Add(active);
        item.Variations.Add(new ItemVariation { Id = Guid.NewGuid(), ItemId = item.Id, Price = 90m, Active = false });
        _items.GetAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        _items.GetPriceOverridesAsync(Main, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, decimal> { [active.Id] = 120m });

        var dto = (await Sut(TestUser.Cashier(Main)).GetAsync(item.Id, default)).ShouldSucceed();

        var v = Assert.Single(dto.Variations);
        Assert.Equal(150m, v.Price);
        Assert.Equal(120m, v.EffectivePrice);
    }

    [Fact]
    public async Task An_owner_without_a_location_gets_no_price_overrides()
    {
        var item = new Item { Id = Guid.NewGuid() };
        item.Variations.Add(new ItemVariation { Id = Guid.NewGuid(), Price = 100m, Active = true });
        _items.GetAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);

        var dto = (await Sut(TestUser.Owner()).GetAsync(item.Id, default)).ShouldSucceed();

        Assert.Equal(100m, dto.Variations[0].EffectivePrice);
        await _items.DidNotReceiveWithAnyArgs().GetPriceOverridesAsync(default, default);
    }

    [Fact]
    public async Task A_photo_with_a_mismatching_signature_is_rejected()
    {
        _items.GetForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new Item { Id = Guid.NewGuid() });
        var notPng = new UploadedFile("image/png", 4, () => new MemoryStream([1, 2, 3, 4]));

        var result = await Sut(TestUser.Manager(Main)).PutImageAsync(Guid.NewGuid(), notPng, default);

        result.ShouldFailWith(ErrorKind.BadRequest, "Unsupported file type");
    }

    [Fact]
    public async Task A_valid_photo_is_stored_and_versions_the_url()
    {
        var item = new Item { Id = Guid.NewGuid() };
        _items.GetForUpdateAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0];

        var dto = (await Sut(TestUser.Manager(Main)).PutImageAsync(item.Id, new UploadedFile("image/png", png.Length, () => new MemoryStream(png)), default)).ShouldSucceed();

        Assert.StartsWith($"/api/v1/items/{item.Id}/image?v=", dto.ImageUrl);
        _items.Received(1).AddImage(Arg.Is<ItemImage>(i => i.ContentType == "image/png" && i.Data.Length == png.Length));
    }

    [Fact]
    public async Task A_missing_photo_file_is_a_bad_request()
    {
        _items.GetForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new Item());

        (await Sut(TestUser.Manager(Main)).PutImageAsync(Guid.NewGuid(), null, default)).ShouldFailWith(ErrorKind.BadRequest, "File required");
    }
}
