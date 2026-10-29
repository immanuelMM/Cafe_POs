using CafePos.Application.Common;
using CafePos.Application.Common.Identity;
using CafePos.Application.Features.Auth;
using CafePos.Application.Features.Locations;
using CafePos.Application.Features.Users;
using CafePos.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CafePos.Application.Tests;

public class UserServiceTests
{
    private static readonly Guid Main = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IStaffAssignmentRepository _assignments = Substitute.For<IStaffAssignmentRepository>();
    private readonly ILocationRepository _locations = Substitute.For<ILocationRepository>();
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IPinHasher _pins = Substitute.For<IPinHasher>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public UserServiceTests()
    {
        _locations.ActiveExistsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        _users.ToDtoAsync(Arg.Any<UserAccount>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new UserDto(ci.Arg<UserAccount>().Id, "", "", true, false, []));
    }

    private UserService Sut(ICurrentUser user) =>
        new(user, _identity, _users, _assignments, _locations, _tokens, _pins, _uow, TimeProvider.System, NullLogger<UserService>.Instance);

    private static CreateUserRequest NewUser(bool isOwner = false, params Guid[] locations) => new()
    {
        Email = " new@cafe.local ", DisplayName = " New ", Password = "Password#12345", IsOwner = isOwner,
        Assignments = locations.Select(l => new AssignmentRequest { LocationId = l, Role = Roles.Cashier }).ToList()
    };

    [Fact]
    public async Task An_admin_cannot_create_an_owner()
    {
        (await Sut(TestUser.Admin(Main)).CreateAsync(NewUser(isOwner: true), default)).ShouldFailWith(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task A_non_owner_user_needs_at_least_one_assignment()
    {
        (await Sut(TestUser.Owner()).CreateAsync(NewUser(), default)).ShouldFailWith(ErrorKind.BadRequest, "Assignment required");
    }

    [Fact]
    public async Task An_admin_can_only_assign_to_the_active_location()
    {
        (await Sut(TestUser.Admin(Main)).CreateAsync(NewUser(false, Other), default)).ShouldFailWith(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Duplicate_locations_in_one_request_are_unprocessable()
    {
        (await Sut(TestUser.Owner()).CreateAsync(NewUser(false, Main, Main), default)).ShouldFailWith(ErrorKind.Unprocessable, "Duplicate location");
    }

    [Fact]
    public async Task An_inactive_location_is_an_invalid_assignment()
    {
        _locations.ActiveExistsAsync(Main, Arg.Any<CancellationToken>()).Returns(false);

        (await Sut(TestUser.Owner()).CreateAsync(NewUser(false, Main), default)).ShouldFailWith(ErrorKind.Unprocessable, "Invalid location");
    }

    [Fact]
    public async Task A_duplicate_email_is_a_conflict()
    {
        _identity.FindByEmailAsync("new@cafe.local", Arg.Any<CancellationToken>()).Returns(new UserAccount());

        (await Sut(TestUser.Owner()).CreateAsync(NewUser(false, Main), default)).ShouldFailWith(ErrorKind.Conflict, "Duplicate email");
    }

    [Fact]
    public async Task Creating_a_user_trims_input_adds_the_assignments_and_saves_once()
    {
        var account = new UserAccount { Id = Guid.NewGuid(), Email = "new@cafe.local", DisplayName = "New", Active = true };
        _identity.CreateAsync("new@cafe.local", "New", "Password#12345", false, Arg.Any<CancellationToken>()).Returns(account);

        (await Sut(TestUser.Owner()).CreateAsync(NewUser(false, Main), default)).ShouldSucceed();

        _assignments.Received(1).Add(Arg.Is<StaffAssignment>(a => a.UserId == account.Id && a.LocationId == Main && a.Role == Roles.Cashier));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Identity_rule_failures_are_returned_as_a_validation_error_and_nothing_is_assigned()
    {
        var error = Error.Validation(new Dictionary<string, string[]> { ["password"] = ["Passwords must have at least one uppercase."] });
        _identity.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(error);

        var result = await Sut(TestUser.Owner()).CreateAsync(NewUser(false, Main), default);

        result.ShouldFailWith(ErrorKind.Validation);
        _assignments.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task An_admin_cannot_see_a_user_who_is_not_assigned_to_their_location()
    {
        var target = new UserAccount { Id = Guid.NewGuid() };
        _identity.FindByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _assignments.ExistsAsync(target.Id, Main, Arg.Any<CancellationToken>()).Returns(false);

        (await Sut(TestUser.Admin(Main)).GetAsync(target.Id, default)).ShouldFailWith(ErrorKind.NotFound);
    }

    [Fact]
    public async Task An_admin_cannot_see_an_owner_even_when_listed_at_their_location()
    {
        var owner = new UserAccount { Id = Guid.NewGuid(), IsOwner = true };
        _identity.FindByIdAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);
        _assignments.ExistsAsync(owner.Id, Main, Arg.Any<CancellationToken>()).Returns(true);

        (await Sut(TestUser.Admin(Main)).GetAsync(owner.Id, default)).ShouldFailWith(ErrorKind.NotFound);
    }

    [Fact]
    public async Task An_admin_may_only_list_their_active_location()
    {
        (await Sut(TestUser.Admin(Main)).ListAsync(Other, null, null, null, default)).ShouldFailWith(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task An_admin_list_hides_owners_and_scopes_assignments_to_the_active_location()
    {
        _users.ListAsync(Arg.Any<UserListQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<UserDto>([], 1, 20, 0));

        (await Sut(TestUser.Admin(Main)).ListAsync(null, " bob ", null, null, default)).ShouldSucceed();

        await _users.Received(1).ListAsync(
            Arg.Is<UserListQuery>(q => q.LocationId == Main && q.ExcludeOwners && q.ScopeAssignmentsToLocation && q.Page == 1 && q.PageSize == 20),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nobody_can_deactivate_or_demote_themselves()
    {
        var admin = TestUser.Owner();
        var self = new UserAccount { Id = admin.UserId!.Value, IsOwner = true };
        _identity.FindByIdAsync(self.Id, Arg.Any<CancellationToken>()).Returns(self);
        var sut = Sut(admin);

        (await sut.DeactivateAsync(self.Id, default)).ShouldFailWith(ErrorKind.Unprocessable, "Invalid change");
        (await sut.UpdateAsync(self.Id, new UpdateUserRequest { DisplayName = "Me", Active = false }, default)).ShouldFailWith(ErrorKind.Unprocessable, "Invalid change");
        (await sut.UpdateAsync(self.Id, new UpdateUserRequest { DisplayName = "Me", IsOwner = false }, default)).ShouldFailWith(ErrorKind.Unprocessable, "Invalid change");
    }

    [Fact]
    public async Task An_owner_deactivation_is_global_and_revokes_every_session()
    {
        var target = new UserAccount { Id = Guid.NewGuid(), Active = true };
        _identity.FindByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);

        (await Sut(TestUser.Owner()).DeactivateAsync(target.Id, default)).ShouldSucceed();

        Assert.False(target.Active);
        await _identity.Received(1).UpdateAsync(target, Arg.Any<CancellationToken>());
        await _tokens.Received(1).RevokeAllForUserAsync(target.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_admin_deactivation_only_unassigns_at_their_location_while_the_user_works_elsewhere()
    {
        var target = new UserAccount { Id = Guid.NewGuid(), Active = true };
        _identity.FindByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _assignments.ExistsAsync(target.Id, Main, Arg.Any<CancellationToken>()).Returns(true);
        _assignments.ListAtLocationAsync(target.Id, Main, Arg.Any<CancellationToken>()).Returns([new StaffAssignment { UserId = target.Id, LocationId = Main }]);
        _assignments.AnyAsync(target.Id, Arg.Any<CancellationToken>()).Returns(true); // still assigned elsewhere

        (await Sut(TestUser.Admin(Main)).DeactivateAsync(target.Id, default)).ShouldSucceed();

        Assert.True(target.Active);
        await _tokens.Received(1).RevokeForUserAtLocationAsync(target.Id, Main, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _tokens.DidNotReceive().RevokeAllForUserAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_admin_deactivation_is_global_when_no_assignment_remains()
    {
        var target = new UserAccount { Id = Guid.NewGuid(), Active = true };
        _identity.FindByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _assignments.ExistsAsync(target.Id, Main, Arg.Any<CancellationToken>()).Returns(true);
        _assignments.AnyAsync(target.Id, Arg.Any<CancellationToken>()).Returns(false);

        (await Sut(TestUser.Admin(Main)).DeactivateAsync(target.Id, default)).ShouldSucceed();

        Assert.False(target.Active);
        await _tokens.Received(1).RevokeAllForUserAsync(target.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_admin_cannot_change_an_owners_pin()
    {
        var owner = new UserAccount { Id = Guid.NewGuid(), IsOwner = true };
        _identity.FindByIdAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);

        (await Sut(TestUser.Admin(Main)).SetPinAsync(owner.Id, new SetPinRequest { Pin = "1234" }, default)).ShouldFailWith(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Setting_a_pin_stores_the_hash_and_resets_the_lockout()
    {
        var target = new UserAccount { Id = Guid.NewGuid(), ClockPinFailedCount = 3, ClockPinLockedUntil = DateTime.UtcNow.AddMinutes(5) };
        _identity.FindByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
        _pins.Hash("1234").Returns("hash");

        (await Sut(TestUser.Owner()).SetPinAsync(target.Id, new SetPinRequest { Pin = "1234" }, default)).ShouldSucceed();

        Assert.Equal("hash", target.ClockPinHash);
        Assert.Equal(0, target.ClockPinFailedCount);
        Assert.Null(target.ClockPinLockedUntil);
        await _identity.Received(1).UpdateAsync(target, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Assigning_the_owner_to_a_location_is_unprocessable()
    {
        var owner = new UserAccount { Id = Guid.NewGuid(), IsOwner = true };
        _identity.FindByIdAsync(owner.Id, Arg.Any<CancellationToken>()).Returns(owner);

        (await Sut(TestUser.Owner()).AssignAsync(owner.Id, new AssignmentRequest { LocationId = Main }, default))
            .ShouldFailWith(ErrorKind.Unprocessable, "Owner needs no assignment");
    }

    [Fact]
    public async Task Removing_your_own_assignment_is_refused_for_non_owners()
    {
        var admin = TestUser.Admin(Main);
        _assignments.GetAsync(admin.UserId!.Value, Main, Arg.Any<CancellationToken>()).Returns(new StaffAssignment());

        (await Sut(admin).UnassignAsync(admin.UserId!.Value, Main, default)).ShouldFailWith(ErrorKind.Unprocessable, "Invalid change");
    }

    [Fact]
    public async Task Unassigning_at_another_location_is_forbidden_for_an_admin()
    {
        (await Sut(TestUser.Admin(Main)).UnassignAsync(Guid.NewGuid(), Other, default)).ShouldFailWith(ErrorKind.Forbidden);
    }
}
