using System.Text.Json;
using CafePos.Application.Common;
using CafePos.Application.Common.Identity;
using CafePos.Application.Common.Uploads;
using CafePos.Application.Features.Branding;
using CafePos.Application.Features.TimeClock;
using CafePos.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace CafePos.Application.Tests;

public class BrandingServiceTests
{
    private readonly IBrandingRepository _repo = Substitute.For<IBrandingRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private BrandingService Sut() => new(TestUser.Admin(Guid.NewGuid()), _repo, _uow, TimeProvider.System, NullLogger<BrandingService>.Instance);

    private static UpdateBrandingRequest Update(string? secondary = null, string? tertiary = null, bool gradient = false) =>
        new() { BrandName = " Cafe ", PrimaryColor = "#AABBCC", SecondaryColor = secondary, TertiaryColor = tertiary, Gradient = gradient };

    [Fact]
    public async Task A_tertiary_color_needs_a_secondary_color_and_a_gradient_needs_two_colors()
    {
        (await Sut().UpdateAsync(Update(tertiary: "#112233"), default)).ShouldFailWith(ErrorKind.BadRequest, "Invalid colors");
        (await Sut().UpdateAsync(Update(gradient: true), default)).ShouldFailWith(ErrorKind.BadRequest, "Invalid colors");
    }

    [Fact]
    public async Task Update_normalizes_names_and_colors_and_creates_the_row_when_missing()
    {
        var dto = (await Sut().UpdateAsync(Update(secondary: " #DDEEFF ", gradient: true), default)).ShouldSucceed();

        Assert.Equal("Cafe", dto.BrandName);
        Assert.Equal("#aabbcc", dto.PrimaryColor);
        Assert.Equal("#ddeeff", dto.SecondaryColor);
        _repo.Received(1).Add(Arg.Any<BrandSettings>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_a_stored_row_the_public_settings_are_the_defaults()
    {
        var dto = (await Sut().GetAsync(default)).ShouldSucceed();

        Assert.Equal(BrandSettings.DefaultBrandName, dto.BrandName);
        Assert.Null(dto.LogoUrl);
    }

    [Fact]
    public async Task Designs_must_be_a_known_kind_and_a_json_object()
    {
        (await Sut().SaveDesignAsync("poster", new SaveBrandDesignRequest { Document = JsonDocument.Parse("{}").RootElement }, default)).ShouldFailWith(ErrorKind.NotFound);
        (await Sut().SaveDesignAsync("logo", new SaveBrandDesignRequest { Document = JsonDocument.Parse("[1]").RootElement }, default)).ShouldFailWith(ErrorKind.BadRequest, "Invalid design");
    }

    [Fact]
    public async Task A_logo_with_the_wrong_signature_is_rejected_before_the_row_is_loaded()
    {
        var bad = new UploadedFile("image/jpeg", 3, () => new MemoryStream([1, 2, 3]));

        (await Sut().PutLogoAsync(bad, default)).ShouldFailWith(ErrorKind.BadRequest, "Unsupported file type");
        await _repo.DidNotReceive().GetForWriteAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removing_the_logo_clears_it_and_bumps_the_version()
    {
        var row = new BrandSettings { LogoData = [1], LogoContentType = "image/png" };
        _repo.GetForWriteAsync(Arg.Any<CancellationToken>()).Returns(row);

        var dto = (await Sut().DeleteLogoAsync(default)).ShouldSucceed();

        Assert.Null(dto.LogoUrl);
        Assert.Null(row.LogoData);
        Assert.NotEqual(default, row.UpdatedAt);
    }
}

public class TimeClockServiceTests
{
    private static readonly Guid Main = Guid.NewGuid();

    private readonly ITimeClockRepository _repo = Substitute.For<ITimeClockRepository>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IPinHasher _pins = Substitute.For<IPinHasher>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly UserAccount _staff = new() { Id = Guid.NewGuid(), DisplayName = "Casey", ClockPinHash = "hash", Active = true };

    public TimeClockServiceTests()
    {
        _repo.IsEligibleAsync(_staff.Id, Main, Arg.Any<CancellationToken>()).Returns(true);
        _identity.FindByIdAsync(_staff.Id, Arg.Any<CancellationToken>()).Returns(_staff);
    }

    private TimeClockService Sut(ICurrentUser? user = null) =>
        new(user ?? TestUser.Cashier(Main), _repo, _identity, _pins, _uow, TimeProvider.System, NullLogger<TimeClockService>.Instance);

    private PunchRequest Punch(string pin = "1234") => new() { UserId = _staff.Id, Pin = pin };

    [Fact]
    public async Task Punching_needs_an_active_location()
    {
        (await Sut(TestUser.Owner()).PunchAsync(Punch(), default)).ShouldFailWith(ErrorKind.BadRequest, "Location required");
    }

    [Fact]
    public async Task An_ineligible_staff_member_is_not_found_and_a_missing_pin_is_unprocessable()
    {
        _repo.IsEligibleAsync(_staff.Id, Main, Arg.Any<CancellationToken>()).Returns(false);
        (await Sut().PunchAsync(Punch(), default)).ShouldFailWith(ErrorKind.NotFound);

        _repo.IsEligibleAsync(_staff.Id, Main, Arg.Any<CancellationToken>()).Returns(true);
        _staff.ClockPinHash = null;
        (await Sut().PunchAsync(Punch(), default)).ShouldFailWith(ErrorKind.Unprocessable, "PIN not set");
    }

    [Fact]
    public async Task A_wrong_pin_is_403_counts_the_failure_and_locks_after_five()
    {
        _pins.Verify("hash", "0000").Returns(false);
        var sut = Sut();

        for (var i = 1; i <= 4; i++)
        {
            (await sut.PunchAsync(Punch("0000"), default)).ShouldFailWith(ErrorKind.Forbidden, "Forbidden");
            Assert.Equal(i, _staff.ClockPinFailedCount);
        }
        (await sut.PunchAsync(Punch("0000"), default)).ShouldFailWith(ErrorKind.Forbidden);

        Assert.Equal(0, _staff.ClockPinFailedCount);
        Assert.NotNull(_staff.ClockPinLockedUntil);
        (await sut.PunchAsync(Punch("1234"), default)).ShouldFailWith(ErrorKind.Locked, "PIN locked");
    }

    [Fact]
    public async Task A_correct_pin_clocks_in_then_out_and_resets_the_failure_count()
    {
        _pins.Verify("hash", "1234").Returns(true);
        _staff.ClockPinFailedCount = 3;
        var sut = Sut();

        var inResult = (await sut.PunchAsync(Punch(), default)).ShouldSucceed();
        Assert.Equal("In", inResult.Action);
        Assert.Equal(0, _staff.ClockPinFailedCount);
        _repo.Received(1).Add(Arg.Is<TimeEntry>(e => e.UserId == _staff.Id && e.LocationId == Main && e.ClockOutAt == null));

        var open = new TimeEntry { Id = Guid.NewGuid(), UserId = _staff.Id, LocationId = Main, ClockInAt = DateTime.UtcNow.AddHours(-2) };
        _repo.GetOpenEntryForUpdateAsync(_staff.Id, Arg.Any<CancellationToken>()).Returns(open);
        var outResult = (await sut.PunchAsync(Punch(), default)).ShouldSucceed();
        Assert.Equal("Out", outResult.Action);
        Assert.InRange(outResult.Entry.Hours!.Value, 1.99m, 2.01m);
    }

    [Fact]
    public async Task Two_simultaneous_clock_ins_conflict()
    {
        _pins.Verify("hash", "1234").Returns(true);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<Task<int>>(_ => throw new UniqueConstraintViolationException(new Exception()));

        (await Sut().PunchAsync(Punch(), default)).ShouldFailWith(ErrorKind.Conflict, "Punch conflict");
    }

    [Fact]
    public async Task A_cashier_only_sees_their_own_entries_while_a_manager_can_filter_by_user()
    {
        var cashier = TestUser.Cashier(Main);
        _repo.ListEntriesAsync(Main, Arg.Any<DateTime>(), null, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns([]);

        (await Sut(cashier).EntriesAsync(null, null, Guid.NewGuid(), default)).ShouldSucceed();
        await _repo.Received(1).ListEntriesAsync(Main, Arg.Any<DateTime>(), null, cashier.UserId, Arg.Any<CancellationToken>());

        var other = Guid.NewGuid();
        (await Sut(TestUser.Manager(Main)).EntriesAsync(null, null, other, default)).ShouldSucceed();
        await _repo.Received(1).ListEntriesAsync(Main, Arg.Any<DateTime>(), null, other, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Setting_your_own_pin_hashes_it_and_clears_the_lockout()
    {
        var me = TestUser.Cashier(Main);
        var account = new UserAccount { Id = me.UserId!.Value, ClockPinFailedCount = 2 };
        _identity.FindByIdAsync(account.Id, Arg.Any<CancellationToken>()).Returns(account);
        _pins.Hash("4321").Returns("h2");

        (await Sut(me).SetMyPinAsync("4321", default)).ShouldSucceed();

        Assert.Equal("h2", account.ClockPinHash);
        Assert.Equal(0, account.ClockPinFailedCount);
        _identity.Received(1).ApplyChanges(account);
    }
}
