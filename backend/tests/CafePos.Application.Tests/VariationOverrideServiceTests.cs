using CafePos.Application.Common;
using CafePos.Application.Features.Locations;
using CafePos.Application.Features.VariationOverrides;
using CafePos.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CafePos.Application.Tests;

public class VariationOverrideServiceTests
{
    private static readonly Guid Main = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Variation = Guid.NewGuid();

    private readonly IVariationOverrideRepository _overrides = Substitute.For<IVariationOverrideRepository>();
    private readonly ILocationRepository _locations = Substitute.For<ILocationRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public VariationOverrideServiceTests()
    {
        _overrides.GetVariationAsync(Variation, Arg.Any<CancellationToken>()).Returns(new VariationPrice(Variation, 150m));
        _locations.GetAsync(Main, Arg.Any<CancellationToken>()).Returns(new Location { Id = Main, Name = "Main" });
        _locations.GetNamesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [Main] = "Main", [Other] = "Uptown" });
    }

    private VariationOverrideService Sut(ICurrentUser user) => new(user, _overrides, _locations, _uow, NullLogger<VariationOverrideService>.Instance);

    [Fact]
    public async Task A_manager_cannot_write_an_override_for_another_location()
    {
        var result = await Sut(TestUser.Manager(Main)).PutAsync(Variation, Other, new() { PriceOverride = 10m }, default);

        result.ShouldFailWith(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task An_unknown_variation_is_not_found()
    {
        var result = await Sut(TestUser.Owner()).PutAsync(Guid.NewGuid(), Main, new() { PriceOverride = 10m }, default);

        result.ShouldFailWith(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Sending_all_fields_null_removes_the_override()
    {
        var row = new ItemVariationLocationOverride { ItemVariationId = Variation, LocationId = Main, PriceOverride = 99m };
        _overrides.GetAsync(Variation, Main, Arg.Any<CancellationToken>()).Returns(row);

        var result = await Sut(TestUser.Manager(Main)).PutAsync(Variation, Main, new(), default);

        Assert.Null(result.ShouldSucceed());
        _overrides.Received(1).Remove(row);
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_new_override_is_added_and_reports_its_effective_price()
    {
        var result = await Sut(TestUser.Manager(Main)).PutAsync(Variation, Main, new() { PriceOverride = 120m, TrackInventoryOverride = false }, default);

        var dto = result.ShouldSucceed()!;
        Assert.Equal(120m, dto.EffectivePrice);
        Assert.Equal("Main", dto.LocationName);
        _overrides.Received(1).Add(Arg.Is<ItemVariationLocationOverride>(o => o.LocationId == Main && o.PriceOverride == 120m && o.TrackInventoryOverride == false));
    }

    [Fact]
    public async Task Without_a_price_override_the_effective_price_is_the_global_price()
    {
        var result = await Sut(TestUser.Manager(Main)).PutAsync(Variation, Main, new() { LowStockThresholdOverride = 5m }, default);

        Assert.Equal(150m, result.ShouldSucceed()!.EffectivePrice);
    }

    [Fact]
    public async Task A_non_owner_lists_only_the_override_at_their_active_location()
    {
        _overrides.ListAsync(Variation, Main, Arg.Any<CancellationToken>())
            .Returns([new ItemVariationLocationOverride { ItemVariationId = Variation, LocationId = Main, PriceOverride = 100m }]);

        var list = (await Sut(TestUser.Manager(Main)).ListAsync(Variation, default)).ShouldSucceed();

        Assert.Equal(100m, Assert.Single(list).EffectivePrice);
        await _overrides.DidNotReceive().ListAsync(Variation, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_owner_lists_every_location_ordered_by_location_name()
    {
        _overrides.ListAsync(Variation, null, Arg.Any<CancellationToken>()).Returns(
        [
            new ItemVariationLocationOverride { ItemVariationId = Variation, LocationId = Other },
            new ItemVariationLocationOverride { ItemVariationId = Variation, LocationId = Main }
        ]);

        var list = (await Sut(TestUser.Owner()).ListAsync(Variation, default)).ShouldSucceed();

        Assert.Equal(["Main", "Uptown"], list.Select(o => o.LocationName));
    }
}
