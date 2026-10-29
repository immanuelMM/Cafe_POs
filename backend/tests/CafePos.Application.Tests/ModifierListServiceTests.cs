using CafePos.Application.Common;
using CafePos.Application.Features.Catalog;
using CafePos.Domain;

namespace CafePos.Application.Tests;

public class ModifierListServiceTests
{
    private readonly IModifierListRepository _lists = Substitute.For<IModifierListRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private ModifierListService Sut() => new(TestUser.Manager(Guid.NewGuid()), _lists, _uow);

    [Fact]
    public async Task Min_selections_cannot_exceed_max_selections()
    {
        var result = await Sut().CreateAsync(new UpsertModifierListRequest { Name = "x", MinSelections = 3, MaxSelections = 1 }, default);

        result.ShouldFailWith(ErrorKind.Unprocessable, "Invalid selection rules");
    }

    [Fact]
    public async Task Update_changes_known_modifiers_adds_new_ones_and_deactivates_omitted_ones()
    {
        var keep = new Modifier { Id = Guid.NewGuid(), Name = "Keep", Active = true };
        var gone = new Modifier { Id = Guid.NewGuid(), Name = "Gone", Active = true };
        var list = new ModifierList { Id = Guid.NewGuid(), Modifiers = [keep, gone] };
        _lists.GetForUpdateAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await Sut().UpdateAsync(list.Id, new UpsertModifierListRequest
        {
            Name = "L",
            Modifiers = [new() { Id = keep.Id, Name = " Renamed ", PriceDelta = 5m }, new() { Name = "New", PriceDelta = 1m }]
        }, default);

        result.ShouldSucceed();
        Assert.Equal("Renamed", keep.Name);
        Assert.False(gone.Active);
        Assert.Equal(3, list.Modifiers.Count);
    }

    [Fact]
    public async Task A_modifier_id_from_another_list_is_unprocessable()
    {
        var list = new ModifierList { Id = Guid.NewGuid() };
        _lists.GetForUpdateAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await Sut().UpdateAsync(list.Id, new UpsertModifierListRequest { Name = "L", Modifiers = [new() { Id = Guid.NewGuid(), Name = "x" }] }, default);

        result.ShouldFailWith(ErrorKind.Unprocessable, "Invalid modifier");
    }
}
