using System.Text.Json;
using CafePos.Application.Common;
using CafePos.Application.Features.Inventory;
using CafePos.Application.Features.Locations;
using CafePos.Application.Features.Orders;
using CafePos.Application.Payments;
using CafePos.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CafePos.Application.Tests;

public class OrderServiceTests
{
    private static readonly Guid Main = Guid.NewGuid();

    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IOrderCatalogRepository _catalog = Substitute.For<IOrderCatalogRepository>();
    private readonly ILocationRepository _locations = Substitute.For<ILocationRepository>();
    private readonly IStockRepository _stockRepo = Substitute.For<IStockRepository>();
    private readonly IRecipeRepository _recipes = Substitute.For<IRecipeRepository>();
    private readonly IIngredientRepository _ingredients = Substitute.For<IIngredientRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IUnitOfWorkTransaction _tx = Substitute.For<IUnitOfWorkTransaction>();
    private readonly TestUser _cashier = TestUser.Cashier(Main);

    public OrderServiceTests()
    {
        _uow.BeginTransactionAsync(Arg.Any<CancellationToken>()).Returns(_tx);
        _locations.GetAsync(Main, Arg.Any<CancellationToken>()).Returns(new Location { Id = Main, Name = "Main", VatRatePercent = 12m });
        _stockRepo.GetUntrackedVariationIdsAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        _recipes.ListLinesForVariationsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        _orders.NextReceiptNumberAsync(Main, Arg.Any<CancellationToken>()).Returns(7);
    }

    private OrderService Sut(ICurrentUser user)
    {
        var stock = new StockService(user, _stockRepo, _recipes, _ingredients, _uow, TimeProvider.System, NullLogger<StockService>.Instance);
        return new OrderService(user, _orders, _catalog, _locations, stock, new PaymentProviderResolver([new CashPaymentProvider()]),
            _uow, TimeProvider.System, NullLogger<OrderService>.Instance);
    }

    private Order OpenOrder(Guid? cashier = null) => new()
    {
        Id = Guid.NewGuid(), LocationId = Main, CashierId = cashier ?? _cashier.UserId!.Value, Status = OrderStatus.Open,
        Items = [new OrderItem { Id = Guid.NewGuid(), ItemVariationId = Guid.NewGuid(), LineTotal = 150m, Quantity = 1, UnitPrice = 150m }]
    };

    private void Existing(Order order) => _orders.GetAsync(order.Id, Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(order);

    [Fact]
    public async Task Completion_claims_numbers_consumes_stock_pays_and_records_idempotency_in_one_transaction()
    {
        var order = OpenOrder();
        Existing(order);
        _orders.TryClaimForCompletionAsync(order.Id, Arg.Any<CancellationToken>()).Returns(1);

        var result = await Sut(_cashier).CompleteAsync(order.Id, " key-1 ", new CompleteOrderRequest { PaymentMethod = "cash", AmountTendered = 200m }, default);

        var outcome = result.ShouldSucceed();
        Assert.False(outcome.Replayed);
        Assert.Equal("Completed", outcome.Order.Status);
        Assert.Equal(7, outcome.Order.OrderNumber);
        Assert.Equal(16.07m, outcome.Order.VatAmount);
        Assert.Equal(50m, outcome.Order.Payments.Single().ChangeGiven);
        _orders.Received(1).AddIdempotencyRecord(Arg.Is<IdempotencyRecord>(r => r.Key == "key-1" && r.OrderId == order.Id));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _tx.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Insufficient_stock_returns_409_and_never_commits()
    {
        var order = OpenOrder();
        Existing(order);
        _orders.TryClaimForCompletionAsync(order.Id, Arg.Any<CancellationToken>()).Returns(1);
        var milk = Guid.NewGuid();
        _recipes.ListLinesForVariationsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new RecipeLine { ItemVariationId = order.Items[0].ItemVariationId, IngredientId = milk, Quantity = 5m }]);
        _stockRepo.TryDecrementAsync(Main, milk, 5m, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Sut(_cashier).CompleteAsync(order.Id, "k", new CompleteOrderRequest { AmountTendered = 200m }, default);

        result.ShouldFailWith(ErrorKind.Conflict, "Insufficient stock");
        await _tx.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _orders.DidNotReceiveWithAnyArgs().AddIdempotencyRecord(default!);
    }

    [Fact]
    public async Task A_failed_payment_is_422_before_any_transaction_starts()
    {
        var order = OpenOrder();
        Existing(order);

        var result = await Sut(_cashier).CompleteAsync(order.Id, "k", new CompleteOrderRequest { AmountTendered = 10m }, default);

        result.ShouldFailWith(ErrorKind.Unprocessable, "Payment failed");
        await _uow.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_order_completed_or_canceled_meanwhile_is_a_conflict()
    {
        var order = OpenOrder();
        Existing(order);
        _orders.TryClaimForCompletionAsync(order.Id, Arg.Any<CancellationToken>()).Returns(0);

        var result = await Sut(_cashier).CompleteAsync(order.Id, "k", new CompleteOrderRequest { AmountTendered = 200m }, default);

        result.ShouldFailWith(ErrorKind.Conflict, "Order is no longer open");
        await _orders.DidNotReceiveWithAnyArgs().NextReceiptNumberAsync(default, default);
    }

    [Fact]
    public async Task Repeating_an_idempotency_key_for_the_same_order_replays_the_stored_response_without_charging()
    {
        var order = OpenOrder();
        Existing(order);
        var stored = new OrderDto(order.Id, 3, "Completed", Main, order.CashierId, "c", null, 12m, 150m, 133.93m, 16.07m, DateTime.UtcNow, DateTime.UtcNow, null, [], []);
        _orders.GetIdempotencyRecordAsync("k", Arg.Any<CancellationToken>())
            .Returns(new IdempotencyRecord { Key = "k", OrderId = order.Id, ResponseJson = JsonSerializer.Serialize(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });

        var outcome = (await Sut(_cashier).CompleteAsync(order.Id, "k", new CompleteOrderRequest { AmountTendered = 200m }, default)).ShouldSucceed();

        Assert.True(outcome.Replayed);
        Assert.Equal(3, outcome.Order.OrderNumber);
        await _uow.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_idempotency_key_used_for_another_order_is_unprocessable()
    {
        var order = OpenOrder();
        Existing(order);
        _orders.GetIdempotencyRecordAsync("k", Arg.Any<CancellationToken>()).Returns(new IdempotencyRecord { Key = "k", OrderId = Guid.NewGuid(), ResponseJson = "{}" });

        (await Sut(_cashier).CompleteAsync(order.Id, "k", new CompleteOrderRequest { AmountTendered = 200m }, default))
            .ShouldFailWith(ErrorKind.Unprocessable, "Idempotency-Key reused");
    }

    [Fact]
    public async Task A_concurrent_completion_with_the_same_key_is_reported_as_in_progress()
    {
        var order = OpenOrder();
        Existing(order);
        _orders.TryClaimForCompletionAsync(order.Id, Arg.Any<CancellationToken>()).Returns(1);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<Task<int>>(_ => throw new UniqueConstraintViolationException(new Exception()));

        (await Sut(_cashier).CompleteAsync(order.Id, "k", new CompleteOrderRequest { AmountTendered = 200m }, default))
            .ShouldFailWith(ErrorKind.Conflict, "Request in progress");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task The_idempotency_key_is_required(string? key)
    {
        (await Sut(_cashier).CompleteAsync(Guid.NewGuid(), key, new CompleteOrderRequest(), default)).ShouldFailWith(ErrorKind.BadRequest, "Idempotency-Key required");
    }

    [Fact]
    public async Task A_cashier_cannot_see_a_colleagues_order()
    {
        var order = OpenOrder(cashier: Guid.NewGuid());
        Existing(order);

        (await Sut(_cashier).GetAsync(order.Id, default)).ShouldFailWith(ErrorKind.NotFound);
        (await Sut(TestUser.Manager(Main)).GetAsync(order.Id, default)).ShouldSucceed();
    }

    [Fact]
    public async Task Mutating_an_order_in_an_owner_cross_location_session_asks_for_a_location()
    {
        (await Sut(TestUser.Owner()).CancelAsync(Guid.NewGuid(), default)).ShouldFailWith(ErrorKind.BadRequest, "Location required");
    }

    [Fact]
    public async Task Only_the_owner_may_list_another_locations_orders_and_the_location_must_exist()
    {
        var other = Guid.NewGuid();
        (await Sut(TestUser.Manager(Main)).ListAsync(null, null, null, other, null, null, default)).ShouldFailWith(ErrorKind.Forbidden);

        _locations.ExistsAsync(other, Arg.Any<CancellationToken>()).Returns(false);
        (await Sut(TestUser.Owner(Main)).ListAsync(null, null, null, other, null, null, default)).ShouldFailWith(ErrorKind.NotFound);
    }

    [Fact]
    public async Task A_cashier_list_is_limited_to_their_own_orders_and_an_invalid_status_is_rejected()
    {
        _orders.ListAsync(Arg.Any<OrderListQuery>(), Arg.Any<CancellationToken>()).Returns(new OrderPage([], 0));

        (await Sut(_cashier).ListAsync("Completed", null, null, null, null, null, default)).ShouldSucceed();
        await _orders.Received(1).ListAsync(
            Arg.Is<OrderListQuery>(q => q.CashierId == _cashier.UserId && q.Status == OrderStatus.Completed && q.Scope == OrderScopeKind.ActiveLocation),
            Arg.Any<CancellationToken>());

        (await Sut(_cashier).ListAsync("Nope", null, null, null, null, null, default)).ShouldFailWith(ErrorKind.BadRequest, "Invalid status");
    }

    [Fact]
    public async Task Creating_an_order_with_an_existing_id_of_another_cashier_is_a_conflict_and_the_same_cashier_gets_it_back()
    {
        var id = Guid.NewGuid();
        _orders.FindOwnerAsync(id, Arg.Any<CancellationToken>()).Returns(new OrderOwner(Main, Guid.NewGuid()));
        (await Sut(_cashier).CreateAsync(new CreateOrderRequest { Id = id, Items = [new()] }, default)).ShouldFailWith(ErrorKind.Conflict, "Order id already in use");

        var order = OpenOrder();
        Existing(order);
        _orders.FindOwnerAsync(order.Id, Arg.Any<CancellationToken>()).Returns(new OrderOwner(Main, _cashier.UserId!.Value));
        var again = (await Sut(_cashier).CreateAsync(new CreateOrderRequest { Id = order.Id, Items = [new()] }, default)).ShouldSucceed();
        Assert.False(again.Created);
    }

    [Fact]
    public async Task Creating_an_order_prices_lines_with_the_location_override_modifiers_and_vat_inclusive_totals()
    {
        var item = new Item { Id = Guid.NewGuid(), Name = "Latte", Active = true };
        var list = new ModifierList { Id = Guid.NewGuid(), Name = "Milk", MinSelections = 0, MaxSelections = 1, Active = true };
        item.ModifierLists.Add(new ItemModifierList { ItemId = item.Id, ModifierListId = list.Id });
        var variation = new ItemVariation { Id = Guid.NewGuid(), Name = "M", Sku = "LAT-M", Price = 150m, Active = true, Item = item };
        var oat = new Modifier { Id = Guid.NewGuid(), ModifierListId = list.Id, Name = "Oat", PriceDelta = 30m, Active = true };
        _catalog.GetVariationWithItemAsync(variation.Id, Arg.Any<CancellationToken>()).Returns(variation);
        _catalog.GetModifiersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([oat]);
        _catalog.GetActiveModifierListsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([list]);
        _catalog.GetPriceOverrideAsync(variation.Id, Main, Arg.Any<CancellationToken>()).Returns(160m);

        var outcome = (await Sut(_cashier).CreateAsync(new CreateOrderRequest
        {
            Id = Guid.NewGuid(),
            Items = [new OrderLineRequest { ItemVariationId = variation.Id, Quantity = 2, ModifierIds = [oat.Id] }]
        }, default)).ShouldSucceed();

        Assert.True(outcome.Created);
        var line = Assert.Single(outcome.Order.Items);
        Assert.Equal(190m, line.UnitPrice);   // override 160 + modifier 30
        Assert.Equal(380m, outcome.Order.Total);
        Assert.Equal(40.71m, outcome.Order.VatAmount); // 380 * 12 / 112
    }

    [Fact]
    public async Task A_modifier_list_selection_outside_its_min_and_max_is_unprocessable()
    {
        var item = new Item { Id = Guid.NewGuid(), Name = "Latte", Active = true };
        var list = new ModifierList { Id = Guid.NewGuid(), Name = "Milk", MinSelections = 1, MaxSelections = 1, Active = true };
        item.ModifierLists.Add(new ItemModifierList { ItemId = item.Id, ModifierListId = list.Id });
        var variation = new ItemVariation { Id = Guid.NewGuid(), Price = 150m, Active = true, Item = item };
        _catalog.GetVariationWithItemAsync(variation.Id, Arg.Any<CancellationToken>()).Returns(variation);
        _catalog.GetActiveModifierListsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([list]);

        var result = await Sut(_cashier).CreateAsync(new CreateOrderRequest { Id = Guid.NewGuid(), Items = [new OrderLineRequest { ItemVariationId = variation.Id }] }, default);

        result.ShouldFailWith(ErrorKind.Unprocessable, "Invalid modifier selection");
    }

    [Fact]
    public async Task Removing_the_last_line_is_refused_and_a_non_open_order_cannot_be_edited()
    {
        var order = OpenOrder();
        Existing(order);
        (await Sut(_cashier).RemoveItemAsync(order.Id, order.Items[0].Id, default)).ShouldFailWith(ErrorKind.Unprocessable, "Empty order");

        order.Status = OrderStatus.Completed;
        (await Sut(_cashier).RemoveItemAsync(order.Id, order.Items[0].Id, default)).ShouldFailWith(ErrorKind.Conflict, "Order is not open");
    }

    [Fact]
    public async Task A_receipt_exists_only_for_completed_orders()
    {
        var order = OpenOrder();
        Existing(order);

        (await Sut(_cashier).GetReceiptAsync(order.Id, default)).ShouldFailWith(ErrorKind.Conflict, "Receipt unavailable");
    }
}
