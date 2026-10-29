using CafePos.Application.Common;
using CafePos.Application.Common.Identity;
using CafePos.Application.Features.Auth;
using CafePos.Application.Features.Locations;
using CafePos.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CafePos.Application.Tests;

public class AuthServiceTests
{
    private static readonly Guid Main = Guid.NewGuid();
    private static readonly Guid Uptown = Guid.NewGuid();

    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly ILocationRepository _locations = Substitute.For<ILocationRepository>();
    private readonly IStaffAssignmentRepository _assignments = Substitute.For<IStaffAssignmentRepository>();
    private readonly IRefreshTokenRepository _refresh = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly UserAccount _cashier = new() { Id = Guid.NewGuid(), Email = "c@cafe.local", DisplayName = "Casey", Active = true };
    private int _counter;

    public AuthServiceTests()
    {
        _tokens.CreateAccessToken(Arg.Any<UserAccount>(), Arg.Any<Guid?>(), Arg.Any<string>()).Returns(new AccessToken("access", DateTime.UtcNow.AddMinutes(30)));
        _tokens.CreateRefreshToken().Returns(_ => { var n = ++_counter; return new NewRefreshToken("raw" + n, "hash" + n, DateTime.UtcNow.AddDays(7)); });
        _tokens.Hash(Arg.Any<string>()).Returns(ci => "hash-of-" + ci.Arg<string>());
        _identity.FindByEmailAsync(_cashier.Email, Arg.Any<CancellationToken>()).Returns(_cashier);
        _identity.FindByIdAsync(_cashier.Id, Arg.Any<CancellationToken>()).Returns(_cashier);
        _identity.CheckPasswordAsync(_cashier, "pw", Arg.Any<CancellationToken>()).Returns(true);
        AssignedTo(("Main", Main, Roles.Cashier));
    }

    private void AssignedTo(params (string name, Guid id, string role)[] rows) =>
        _assignments.ListForActiveLocationsAsync(_cashier.Id, Arg.Any<CancellationToken>())
            .Returns(rows.Select(r => new StaffAssignment { UserId = _cashier.Id, LocationId = r.id, Role = r.role, Location = new Location { Id = r.id, Name = r.name } }).ToList());

    private AuthService Sut(ICurrentUser? user = null) =>
        new(user ?? new TestUser { UserId = _cashier.Id }, _identity, _locations, _assignments, _refresh, _tokens, _uow, TimeProvider.System, NullLogger<AuthService>.Instance);

    private static LoginRequest Login(Guid? location = null) => new() { Email = "c@cafe.local", Password = "pw", LocationId = location };

    [Fact]
    public async Task Login_issues_an_access_token_and_a_hashed_refresh_token_bound_to_the_location_and_ip()
    {
        var outcome = await Sut().LoginAsync(Login(), "10.0.0.1", default);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("access", outcome.Session!.Response.AccessToken);
        Assert.Equal("raw1", outcome.Session.RefreshToken);
        Assert.Equal(Main, outcome.Session.Response.User.Location!.Id);
        _refresh.Received(1).Add(Arg.Is<RefreshToken>(t => t.TokenHash == "hash1" && t.LocationId == Main && t.UserId == _cashier.Id && t.CreatedByIp == "10.0.0.1"));
        await _identity.Received(1).ResetAccessFailedAsync(_cashier, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_and_inactive_accounts_and_bad_passwords_are_401_without_revealing_which()
    {
        _identity.FindByEmailAsync("nobody@cafe.local", Arg.Any<CancellationToken>()).Returns((UserAccount?)null);
        var unknown = await Sut().LoginAsync(new LoginRequest { Email = "nobody@cafe.local", Password = "pw" }, null, default);
        _cashier.Active = false;
        var inactive = await Sut().LoginAsync(Login(), null, default);
        _cashier.Active = true;
        var wrong = await Sut().LoginAsync(new LoginRequest { Email = "c@cafe.local", Password = "nope" }, null, default);

        foreach (var o in new[] { unknown, inactive, wrong })
            Assert.Equal(("Unauthorized", "Invalid credentials or session."), (o.Error!.Title, o.Error.Detail));
        await _identity.Received(1).RecordAccessFailedAsync(_cashier, Arg.Any<CancellationToken>());
        _refresh.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task A_locked_out_account_gets_423_before_the_password_is_checked()
    {
        _identity.IsLockedOutAsync(_cashier, Arg.Any<CancellationToken>()).Returns(true);

        var outcome = await Sut().LoginAsync(Login(), null, default);

        outcome.Error!.ShouldFailWith(ErrorKind.Locked, "Account locked");
        await _identity.DidNotReceiveWithAnyArgs().CheckPasswordAsync(default!, default!, default);
    }

    [Fact]
    public async Task A_user_with_several_locations_must_choose_one_and_is_told_the_choices()
    {
        AssignedTo(("Main", Main, Roles.Cashier), ("Uptown", Uptown, Roles.Manager));

        var needs = await Sut().LoginAsync(Login(), null, default);
        var chosen = await Sut().LoginAsync(Login(Uptown), null, default);
        var foreign = await Sut().LoginAsync(Login(Guid.NewGuid()), null, default);

        needs.Error!.ShouldFailWith(ErrorKind.BadRequest, "Location required");
        Assert.IsType<List<LocationRef>>(needs.Error!.Extensions!["locations"]);
        Assert.Equal(Roles.Manager, chosen.Session!.Response.User.Role);
        foreign.Error!.ShouldFailWith(ErrorKind.Forbidden, "No location access");
    }

    [Fact]
    public async Task A_user_without_an_active_location_cannot_sign_in()
    {
        AssignedTo();

        (await Sut().LoginAsync(Login(), null, default)).Error!.ShouldFailWith(ErrorKind.Forbidden, "No location access");
    }

    [Fact]
    public async Task An_owner_signs_in_without_a_location_and_sees_every_active_location()
    {
        var owner = new UserAccount { Id = Guid.NewGuid(), Email = "o@cafe.local", Active = true, IsOwner = true };
        _identity.FindByEmailAsync("o@cafe.local", Arg.Any<CancellationToken>()).Returns(owner);
        _identity.CheckPasswordAsync(owner, "pw", Arg.Any<CancellationToken>()).Returns(true);
        _locations.ListActiveAsync(Arg.Any<CancellationToken>()).Returns([new Location { Id = Main, Name = "Main" }, new Location { Id = Uptown, Name = "Uptown" }]);

        var outcome = await Sut().LoginAsync(new LoginRequest { Email = "o@cafe.local", Password = "pw" }, null, default);

        Assert.Null(outcome.Session!.Response.User.Location);
        Assert.Equal(2, outcome.Session.Response.User.Locations.Count);
        _refresh.Received(1).Add(Arg.Is<RefreshToken>(t => t.LocationId == null));
    }

    // ---- refresh ----

    private RefreshToken StoredToken(DateTime? revokedAt = null, DateTime? expiresAt = null, Guid? location = null)
    {
        var token = new RefreshToken { Id = Guid.NewGuid(), UserId = _cashier.Id, LocationId = location ?? Main, TokenHash = "hash-of-raw", ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(1), RevokedAt = revokedAt };
        _refresh.GetByHashAsync("hash-of-raw", Arg.Any<CancellationToken>()).Returns(token);
        return token;
    }

    [Fact]
    public async Task Refresh_rotates_the_token_revoking_the_old_one_and_linking_it_to_the_new()
    {
        var old = StoredToken();

        var outcome = await Sut().RefreshAsync("raw", "10.0.0.2", default);

        Assert.True(outcome.IsSuccess);
        Assert.NotNull(old.RevokedAt);
        Assert.Equal("hash1", old.ReplacedByTokenHash);
        _refresh.Received(1).Add(Arg.Is<RefreshToken>(t => t.TokenHash == "hash1" && t.LocationId == Main && t.CreatedByIp == "10.0.0.2"));
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replaying_a_revoked_token_revokes_every_session_of_the_user_and_clears_the_cookie()
    {
        StoredToken(revokedAt: DateTime.UtcNow.AddMinutes(-1));

        var outcome = await Sut().RefreshAsync("raw", null, default);

        Assert.True(outcome.ClearRefreshCookie);
        outcome.Error!.ShouldFailWith(ErrorKind.Unauthorized);
        await _refresh.Received(1).RevokeAllForUserAsync(_cashier.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_expired_token_or_a_deactivated_user_clears_the_cookie()
    {
        StoredToken(expiresAt: DateTime.UtcNow.AddMinutes(-1));
        var expired = await Sut().RefreshAsync("raw", null, default);

        StoredToken();
        _cashier.Active = false;
        var inactive = await Sut().RefreshAsync("raw", null, default);

        Assert.True(expired.ClearRefreshCookie);
        Assert.True(inactive.ClearRefreshCookie);
        await _refresh.DidNotReceiveWithAnyArgs().RevokeAllForUserAsync(default, default, default);
    }

    [Fact]
    public async Task A_missing_cookie_or_unknown_token_is_401_and_leaves_the_cookie_alone()
    {
        var none = await Sut().RefreshAsync(null, null, default);
        var unknown = await Sut().RefreshAsync("never-issued", null, default);

        Assert.False(none.ClearRefreshCookie);
        Assert.False(unknown.ClearRefreshCookie);
        none.Error!.ShouldFailWith(ErrorKind.Unauthorized);
    }

    [Fact]
    public async Task A_token_whose_assignment_was_removed_is_rejected_and_the_cookie_cleared()
    {
        StoredToken();
        AssignedTo();

        var outcome = await Sut().RefreshAsync("raw", null, default);

        Assert.True(outcome.ClearRefreshCookie);
        outcome.Error!.ShouldFailWith(ErrorKind.Unauthorized);
    }

    [Fact]
    public async Task A_location_less_token_is_only_valid_for_an_owner()
    {
        var token = StoredToken();
        token.LocationId = null;

        var outcome = await Sut().RefreshAsync("raw", null, default);

        outcome.Error!.ShouldFailWith(ErrorKind.Unauthorized);
        Assert.False(outcome.ClearRefreshCookie);
    }

    // ---- switch / logout / me ----

    [Fact]
    public async Task Switching_location_revokes_the_old_cookie_so_it_cannot_jump_back()
    {
        AssignedTo(("Main", Main, Roles.Manager), ("Uptown", Uptown, Roles.Manager));

        var outcome = await Sut().SwitchLocationAsync(new SwitchLocationRequest { LocationId = Uptown }, "old-raw", null, default);

        Assert.Equal(Uptown, outcome.Session!.Response.User.Location!.Id);
        await _refresh.Received(1).RevokeByHashAsync("hash-of-old-raw", _cashier.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_an_owner_may_switch_to_the_cross_location_session()
    {
        var outcome = await Sut().SwitchLocationAsync(new SwitchLocationRequest(), null, null, default);

        outcome.Error!.ShouldFailWith(ErrorKind.BadRequest, "Location required");
    }

    [Fact]
    public async Task Logout_revokes_the_presented_token_and_tolerates_a_missing_one()
    {
        await Sut().LogoutAsync("raw", default);
        await Sut().LogoutAsync(null, default);

        await _refresh.Received(1).RevokeByHashAsync("hash-of-raw", null, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Me_reports_the_session_and_rejects_deactivated_users_or_unknown_locations()
    {
        var me = (await Sut(new TestUser { UserId = _cashier.Id, LocationId = Main }).MeAsync(default)).ShouldSucceed();
        Assert.Equal(Roles.Cashier, me.Role);

        var stale = await Sut(new TestUser { UserId = _cashier.Id, LocationId = Uptown }).MeAsync(default);
        stale.ShouldFailWith(ErrorKind.Unauthorized);

        _cashier.Active = false;
        (await Sut(new TestUser { UserId = _cashier.Id, LocationId = Main }).MeAsync(default)).ShouldFailWith(ErrorKind.Unauthorized);
    }
}
