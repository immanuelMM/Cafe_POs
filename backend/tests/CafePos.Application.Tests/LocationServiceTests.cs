using CafePos.Application.Common;
using CafePos.Application.Common.Identity;
using CafePos.Application.Features.Locations;
using CafePos.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CafePos.Application.Tests;

public class LocationServiceTests
{
    private static readonly Guid Main = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private readonly ILocationRepository _locations = Substitute.For<ILocationRepository>();
    private readonly IStaffAssignmentRepository _assignments = Substitute.For<IStaffAssignmentRepository>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private LocationService Sut(ICurrentUser user) =>
        new(user, _locations, _assignments, _identity, _uow, NullLogger<LocationService>.Instance);

    private static UpdateLocationRequest Update(bool? active = null, string? stripe = null) =>
        new() { Name = " Branch ", TimeZone = "Asia/Manila", VatRatePercent = 12m, Active = active, StripeTerminalLocationId = stripe };

    [Fact]
    public async Task An_admin_cannot_update_another_location()
    {
        var result = await Sut(TestUser.Admin(Main)).UpdateAsync(Other, Update(), default);

        result.ShouldFailWith(ErrorKind.Forbidden);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_admin_cannot_change_active_or_the_stripe_location()
    {
        var sut = Sut(TestUser.Admin(Main));

        (await sut.UpdateAsync(Main, Update(active: false), default)).ShouldFailWith(ErrorKind.Forbidden);
        (await sut.UpdateAsync(Main, Update(stripe: "tml_1"), default)).ShouldFailWith(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task An_unknown_time_zone_is_unprocessable()
    {
        var result = await Sut(TestUser.Owner()).UpdateAsync(Main, Update() with { TimeZone = "Mars/Base" }, default);

        result.ShouldFailWith(ErrorKind.Unprocessable, "Invalid time zone");
    }

    [Fact]
    public async Task An_owner_update_trims_the_name_and_saves()
    {
        var location = new Location { Id = Main, Name = "Old" };
        _locations.GetForUpdateAsync(Main, Arg.Any<CancellationToken>()).Returns(location);

        var result = await Sut(TestUser.Owner()).UpdateAsync(Main, Update(active: false, stripe: "  "), default);

        var dto = result.ShouldSucceed();
        Assert.Equal("Branch", dto.Name);
        Assert.False(dto.Active);
        Assert.Null(dto.StripeTerminalLocationId); // empty clears
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_missing_location_is_not_found()
    {
        var result = await Sut(TestUser.Owner()).UpdateAsync(Main, Update(), default);

        result.ShouldFailWith(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Listing_for_a_non_owner_returns_only_the_assigned_active_locations()
    {
        var user = TestUser.Cashier(Main);
        _locations.ListActiveAssignedToAsync(user.UserId!.Value, Arg.Any<CancellationToken>()).Returns([new Location { Id = Main, Name = "Main" }]);

        var list = (await Sut(user).ListAsync(default)).ShouldSucceed();

        Assert.Single(list);
        await _locations.DidNotReceive().ListAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Current_in_an_owner_cross_location_session_asks_for_a_location()
    {
        var result = await Sut(TestUser.Owner()).CurrentAsync(default);

        result.ShouldFailWith(ErrorKind.BadRequest, "Location required");
    }

    [Fact]
    public async Task Assigning_staff_rejects_owners_inactive_users_and_oneself()
    {
        var admin = TestUser.Admin(Main);
        _locations.ActiveExistsAsync(Main, Arg.Any<CancellationToken>()).Returns(true);
        var sut = Sut(admin);
        var req = new AssignStaffByEmailRequest { Email = "x@y.z", Role = Roles.Cashier };

        _identity.FindByEmailAsync("x@y.z", Arg.Any<CancellationToken>()).Returns(new UserAccount { Id = Guid.NewGuid(), IsOwner = true, Active = true });
        (await sut.AssignStaffAsync(Main, req, default)).ShouldFailWith(ErrorKind.Unprocessable, "Owner needs no assignment");

        _identity.FindByEmailAsync("x@y.z", Arg.Any<CancellationToken>()).Returns(new UserAccount { Id = Guid.NewGuid(), Active = false });
        (await sut.AssignStaffAsync(Main, req, default)).ShouldFailWith(ErrorKind.Unprocessable, "User inactive");

        _identity.FindByEmailAsync("x@y.z", Arg.Any<CancellationToken>()).Returns(new UserAccount { Id = admin.UserId!.Value, Active = true });
        (await sut.AssignStaffAsync(Main, req, default)).ShouldFailWith(ErrorKind.Unprocessable, "Invalid change");
    }

    [Fact]
    public async Task Assigning_staff_adds_a_new_assignment_or_changes_the_role_of_an_existing_one()
    {
        var target = new UserAccount { Id = Guid.NewGuid(), Email = "x@y.z", DisplayName = "X", Active = true };
        _locations.ActiveExistsAsync(Main, Arg.Any<CancellationToken>()).Returns(true);
        _identity.FindByEmailAsync("x@y.z", Arg.Any<CancellationToken>()).Returns(target);
        var sut = Sut(TestUser.Admin(Main));

        var created = (await sut.AssignStaffAsync(Main, new() { Email = "x@y.z", Role = Roles.Manager }, default)).ShouldSucceed();
        Assert.Equal(Roles.Manager, created.Role);
        _assignments.Received(1).Add(Arg.Is<StaffAssignment>(a => a.UserId == target.Id && a.LocationId == Main && a.Role == Roles.Manager));

        var existing = new StaffAssignment { UserId = target.Id, LocationId = Main, Role = Roles.Cashier };
        _assignments.GetAsync(target.Id, Main, Arg.Any<CancellationToken>()).Returns(existing);
        await sut.AssignStaffAsync(Main, new() { Email = "x@y.z", Role = Roles.Admin }, default);
        Assert.Equal(Roles.Admin, existing.Role);
    }

    [Fact]
    public async Task Assigning_an_invalid_role_is_unprocessable_before_anything_is_loaded()
    {
        var result = await Sut(TestUser.Owner()).AssignStaffAsync(Main, new() { Email = "x@y.z", Role = "Boss" }, default);

        result.ShouldFailWith(ErrorKind.Unprocessable, "Invalid role");
        await _identity.DidNotReceive().FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
