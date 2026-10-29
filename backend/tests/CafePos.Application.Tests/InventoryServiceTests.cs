using CafePos.Application.Common;
using CafePos.Application.Features.Inventory;
using CafePos.Application.Features.Locations;
using CafePos.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CafePos.Application.Tests;

public class InventoryServiceTests
{
    private static readonly Guid Main = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private readonly IIngredientRepository _ingredients = Substitute.For<IIngredientRepository>();
    private readonly IStockRepository _stock = Substitute.For<IStockRepository>();
    private readonly IRecipeRepository _recipes = Substitute.For<IRecipeRepository>();
    private readonly ILocationRepository _locations = Substitute.For<ILocationRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IUnitOfWorkTransaction _tx = Substitute.For<IUnitOfWorkTransaction>();

    public InventoryServiceTests()
    {
        _uow.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(_tx);
    }

    private StockService Stock(ICurrentUser user) =>
        new(user, _stock, _recipes, _ingredients, _uow, TimeProvider.System, NullLogger<StockService>.Instance);

    private IngredientService Sut(ICurrentUser user) => new(user, _ingredients, _stock, _locations, Stock(user), _uow);

    private static Order OrderWith(Guid variationId, int quantity) => new()
    {
        Id = Guid.NewGuid(), LocationId = Main,
        Items = [new OrderItem { ItemVariationId = variationId, Quantity = quantity }]
    };

    [Fact]
    public async Task Consuming_stock_sums_recipe_quantities_and_posts_one_sale_movement_per_ingredient()
    {
        var variation = Guid.NewGuid();
        var beans = Guid.NewGuid();
        _stock.GetUntrackedVariationIdsAsync(Main, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        _recipes.ListLinesForVariationsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new RecipeLine { ItemVariationId = variation, IngredientId = beans, Quantity = 18m }]);
        _stock.TryDecrementAsync(Main, beans, 36m, Arg.Any<CancellationToken>()).Returns(true);

        var result = await Stock(TestUser.Cashier(Main)).ConsumeForOrderAsync(OrderWith(variation, 2), Guid.NewGuid(), default);

        result.ShouldSucceed();
        _stock.Received(1).AddMovement(Arg.Is<StockMovement>(m => m.IngredientId == beans && m.QuantityDelta == -36m && m.Reason == StockMovementReason.Sale));
    }

    [Fact]
    public async Task A_shortage_returns_a_409_with_the_ingredient_details()
    {
        var variation = Guid.NewGuid();
        var milk = Guid.NewGuid();
        _stock.GetUntrackedVariationIdsAsync(Main, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        _recipes.ListLinesForVariationsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new RecipeLine { ItemVariationId = variation, IngredientId = milk, Quantity = 200m }]);
        _stock.TryDecrementAsync(Main, milk, 200m, Arg.Any<CancellationToken>()).Returns(false);
        _stock.GetOnHandAsync(milk, Arg.Any<CancellationToken>()).Returns(50m);
        _ingredients.GetNameAsync(milk, Arg.Any<CancellationToken>()).Returns("Whole Milk");

        var error = (await Stock(TestUser.Cashier(Main)).ConsumeForOrderAsync(OrderWith(variation, 1), null, default)).ShouldFailWith(ErrorKind.Conflict, "Insufficient stock");

        Assert.Equal("Whole Milk", error.Extensions!["ingredient"]);
        Assert.Equal(200m, error.Extensions["required"]);
        Assert.Equal(50m, error.Extensions["available"]);
        _stock.DidNotReceiveWithAnyArgs().AddMovement(default!);
    }

    [Fact]
    public async Task Untracked_variations_consume_nothing()
    {
        var variation = Guid.NewGuid();
        _stock.GetUntrackedVariationIdsAsync(Main, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([variation]);
        _recipes.ListLinesForVariationsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);

        (await Stock(TestUser.Cashier(Main)).ConsumeForOrderAsync(OrderWith(variation, 3), null, default)).ShouldSucceed();

        await _stock.DidNotReceiveWithAnyArgs().TryDecrementAsync(default, default, default, default);
    }

    [Fact]
    public async Task A_manual_change_below_zero_is_refused_and_not_committed()
    {
        var id = Guid.NewGuid();
        _stock.TryApplyDeltaAsync(Main, id, -5m, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Stock(TestUser.Manager(Main)).ApplyManualAsync(id, -5m, StockMovementReason.Adjustment, null, "n", null, null, default);

        result.ShouldFailWith(ErrorKind.Conflict, "Insufficient stock");
        await _tx.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_manual_change_posts_the_movement_in_one_committed_transaction()
    {
        var id = Guid.NewGuid();
        _stock.TryApplyDeltaAsync(Main, id, 10m, Arg.Any<CancellationToken>()).Returns(true);

        (await Stock(TestUser.Manager(Main)).ApplyManualAsync(id, 10m, StockMovementReason.Restock, null, null, "Acme", 2m, default)).ShouldSucceed();

        _stock.Received(1).AddMovement(Arg.Is<StockMovement>(m => m.Supplier == "Acme" && m.UnitCost == 2m && m.Reason == StockMovementReason.Restock));
        await _tx.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Manual_changes_need_an_active_location()
    {
        (await Stock(TestUser.Owner()).ApplyManualAsync(Guid.NewGuid(), 1m, StockMovementReason.Restock, null, null, null, null, default))
            .ShouldFailWith(ErrorKind.BadRequest, "Location required");
    }

    [Fact]
    public async Task A_non_owner_cannot_read_another_locations_stock()
    {
        (await Sut(TestUser.Manager(Main)).ListAsync(false, Other, default)).ShouldFailWith(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task An_owner_cross_location_session_lists_the_catalog_without_stock_levels()
    {
        _ingredients.ListCatalogAsync(false, Arg.Any<CancellationToken>()).Returns([new Ingredient { Id = Guid.NewGuid(), Name = "Beans" }]);

        var list = (await Sut(TestUser.Owner()).ListAsync(false, null, default)).ShouldSucceed();

        Assert.Null(Assert.Single(list).QuantityOnHand);
    }

    [Fact]
    public async Task An_owner_reading_another_location_must_pick_an_existing_one()
    {
        _locations.ExistsAsync(Other, Arg.Any<CancellationToken>()).Returns(false);

        (await Sut(TestUser.Owner(Main)).ListAsync(false, Other, default)).ShouldFailWith(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Low_stock_keeps_only_ingredients_at_or_below_their_threshold()
    {
        var low = new Ingredient { Id = Guid.NewGuid(), Name = "Tea", LowStockThreshold = 10m };
        var fine = new Ingredient { Id = Guid.NewGuid(), Name = "Milk", LowStockThreshold = 10m };
        _ingredients.ListWithStockAsync(false, new LocationScope(Main, false), Arg.Any<CancellationToken>())
            .Returns([new IngredientStockRow(low, 10m), new IngredientStockRow(fine, 11m)]);

        var list = (await Sut(TestUser.Manager(Main)).LowStockAsync(null, default)).ShouldSucceed();

        Assert.Equal("Tea", Assert.Single(list).Name);
    }

    [Fact]
    public async Task A_duplicate_ingredient_name_is_a_conflict()
    {
        _ingredients.NameExistsAsync("Beans", null, Arg.Any<CancellationToken>()).Returns(true);

        (await Sut(TestUser.Manager(Main)).CreateAsync(new CreateIngredientRequest { Name = " Beans ", Unit = "g" }, default))
            .ShouldFailWith(ErrorKind.Conflict, "Duplicate ingredient");
    }

    [Fact]
    public async Task An_opening_balance_needs_an_active_location()
    {
        (await Sut(TestUser.Owner()).CreateAsync(new CreateIngredientRequest { Name = "x", Unit = "g", InitialQuantity = 5m }, default))
            .ShouldFailWith(ErrorKind.BadRequest, "Location required");
    }

    [Theory]
    [InlineData(0, "Adjustment", "Invalid adjustment")]
    [InlineData(5, "Sale", "Invalid reason")]
    [InlineData(5, "Waste", "Invalid adjustment")]
    public async Task Invalid_adjustments_are_unprocessable(int delta, string reason, string title)
    {
        _ingredients.ExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        (await Sut(TestUser.Manager(Main)).AdjustAsync(Guid.NewGuid(), new AdjustmentRequest { QuantityDelta = delta, Reason = reason, Note = "n" }, default))
            .ShouldFailWith(ErrorKind.Unprocessable, title);
    }

    [Fact]
    public async Task A_recipe_may_not_repeat_an_ingredient()
    {
        var variation = Guid.NewGuid();
        var beans = Guid.NewGuid();
        _recipes.VariationExistsAsync(variation, Arg.Any<CancellationToken>()).Returns(true);
        var sut = new RecipeService(_recipes, _ingredients, _uow);

        (await sut.SetAsync(variation, new SetRecipeRequest { Lines = [new() { IngredientId = beans, Quantity = 1 }, new() { IngredientId = beans, Quantity = 2 }] }, default))
            .ShouldFailWith(ErrorKind.Unprocessable, "Duplicate ingredient");
    }

    [Fact]
    public async Task Replacing_a_recipe_flushes_the_deletes_before_adding_the_new_lines()
    {
        var variation = Guid.NewGuid();
        var beans = Guid.NewGuid();
        _recipes.VariationExistsAsync(variation, Arg.Any<CancellationToken>()).Returns(true);
        _ingredients.CountExistingAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(1);
        _recipes.GetLinesForUpdateAsync(variation, Arg.Any<CancellationToken>()).Returns([new RecipeLine()]);
        _recipes.ListLinesAsync(variation, Arg.Any<CancellationToken>()).Returns([]);

        var sut = new RecipeService(_recipes, _ingredients, _uow);
        (await sut.SetAsync(variation, new SetRecipeRequest { Lines = [new() { IngredientId = beans, Quantity = 1 }] }, default)).ShouldSucceed();

        Received.InOrder(() =>
        {
            _recipes.RemoveLines(Arg.Any<IEnumerable<RecipeLine>>());
            _uow.SaveChangesAsync(Arg.Any<CancellationToken>());
            _recipes.AddLine(Arg.Any<RecipeLine>());
            _uow.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }
}
