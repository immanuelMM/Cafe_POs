using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CafePos.Api.Data;
using CafePos.Api.Domain;
using CafePos.Api.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CafePos.Api.Tests;

[Collection("api")]
public class BrandingTests
{
    private readonly ApiFactory _f;
    public BrandingTests(ApiFactory f) => _f = f;

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

    private static MultipartFormDataContent Form(byte[] bytes, string contentType, string fileName = "f.bin")
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { part, "file", fileName } };
    }

    private static byte[] Text(string s) => Encoding.ASCII.GetBytes(s);

    [Fact]
    public async Task Update_branding_roundtrips_and_validates_color()
    {
        var admin = await _f.AdminAsync();
        var res = await admin.PutAsJsonAsync("/api/v1/branding", new { brandName = "M brew & Bean", tagline = "Fresh", primaryColor = "#AABBCC" });
        res.EnsureSuccessStatusCode();
        var dto = await res.ReadAsync<BrandingDto>();
        dto.BrandName.ShouldBe("M brew & Bean");
        dto.Tagline.ShouldBe("Fresh");
        dto.LogoUrl.ShouldBe(null);

        var anon = await _f.CreateClient().GetFromJsonAsync<BrandingDto>("/api/v1/branding", ApiFactory.Json);
        anon!.PrimaryColor.ShouldBe("#aabbcc");

        (await admin.PutAsJsonAsync("/api/v1/branding", new { brandName = "X", primaryColor = "red" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync("/api/v1/branding", new { brandName = "X", primaryColor = "#12345" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync("/api/v1/branding", new { brandName = "", primaryColor = "#123456" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync("/api/v1/branding", new { brandName = "X", tagline = new string('t', 201), primaryColor = "#123456" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var cleared = await (await admin.PutAsJsonAsync("/api/v1/branding", new { brandName = "M brew & Bean", tagline = "", primaryColor = "#3b2417" })).ReadAsync<BrandingDto>();
        cleared.Tagline.ShouldBe(null);

        var cashier = await _f.CashierAsync();
        (await cashier.PutAsJsonAsync("/api/v1/branding", new { brandName = "Y", primaryColor = "#123456" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Logo_upload_rejects_bad_type_signature_and_size_and_serves_anonymously()
    {
        var admin = await _f.AdminAsync();

        (await admin.PutAsync("/api/v1/branding/logo", Form(Png, "image/svg+xml"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsync("/api/v1/branding/logo", Form(Text("<svg onload=alert(1)/>"), "image/png"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsync("/api/v1/branding/logo", Form(Png, "image/jpeg"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsync("/api/v1/branding/logo", Form(Text("%PDF-1.4"), "application/pdf"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var big = new byte[1024 * 1024 + 1];
        Png.CopyTo(big, 0);
        (await admin.PutAsync("/api/v1/branding/logo", Form(big, "image/png"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsync("/api/v1/branding/logo", new MultipartFormDataContent())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var cashier = await _f.CashierAsync();
        (await cashier.PutAsync("/api/v1/branding/logo", Form(Png, "image/png"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var ok = await admin.PutAsync("/api/v1/branding/logo", Form(Png, "image/png"));
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await ok.ReadAsync<BrandingDto>();
        Assert.StartsWith("/api/v1/branding/logo?v=", dto.LogoUrl);

        var anon = _f.CreateClient();
        var img = await anon.GetAsync("/api/v1/branding/logo");
        img.StatusCode.ShouldBe(HttpStatusCode.OK);
        img.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        Assert.Equal(Png, await img.Content.ReadAsByteArrayAsync());
        Assert.Contains("immutable", img.Headers.CacheControl!.ToString());
        Assert.Contains("max-age=31536000", img.Headers.CacheControl!.ToString());
        img.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");

        (await admin.DeleteAsync("/api/v1/branding/logo")).EnsureSuccessStatusCode();
        (await anon.GetAsync("/api/v1/branding/logo")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tarpaulin_accepts_pdf_and_rejects_over_15mb()
    {
        var admin = await _f.AdminAsync();
        var pdf = await admin.PutAsync("/api/v1/branding/tarpaulin", Form(Text("%PDF-1.4 hello"), "application/pdf"));
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await pdf.ReadAsync<BrandingDto>()).TarpaulinContentType.ShouldBe("application/pdf");
        (await _f.CreateClient().GetAsync("/api/v1/branding/tarpaulin")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var big = new byte[15 * 1024 * 1024 + 1];
        Png.CopyTo(big, 0);
        (await admin.PutAsync("/api/v1/branding/tarpaulin", Form(big, "image/png"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var fits = new byte[15 * 1024 * 1024];
        Png.CopyTo(fits, 0);
        (await admin.PutAsync("/api/v1/branding/tarpaulin", Form(fits, "image/png"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.DeleteAsync("/api/v1/branding/tarpaulin")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Swagger_document_generates()
    {
        (await _f.CreateClient().GetAsync("/swagger/v1/swagger.json")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

[Collection("api")]
public class TimeClockTests
{
    private readonly ApiFactory _f;
    public TimeClockTests(ApiFactory f) => _f = f;

    /// <summary>A fresh Cashier at Main so tests never share PIN/lock state.</summary>
    private async Task<Guid> NewStaffAsync(string name = "Clock Tester")
    {
        using var scope = _f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CafeDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"clock-{Guid.NewGuid():N}@cafe.local";
        var u = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, DisplayName = name };
        (await users.CreateAsync(u, "Clock#Test12345")).Succeeded.ShouldBe(true);
        db.StaffAssignments.Add(new StaffAssignment { UserId = u.Id, LocationId = DevDataSeeder.MainLocationId, Role = Roles.Cashier });
        await db.SaveChangesAsync();
        return u.Id;
    }

    private static Task<HttpResponseMessage> Punch(HttpClient c, Guid id, string pin) =>
        c.PostAsJsonAsync("/api/v1/timeclock/punch", new { userId = id, pin });

    private static async Task SetPin(HttpClient owner, Guid id, string pin) =>
        (await owner.PutAsJsonAsync($"/api/v1/users/{id}/pin", new { pin })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

    [Fact]
    public async Task Punch_toggles_in_and_out_and_staff_list_reflects_it()
    {
        var owner = await _f.AdminAsync();
        var kiosk = await _f.CashierAsync();
        var id = await NewStaffAsync("Zed Toggle");
        await SetPin(owner, id, "1234");

        var staff = await kiosk.GetFromJsonAsync<List<TimeClockStaffDto>>("/api/v1/timeclock/staff", ApiFactory.Json);
        var me = staff!.Single(s => s.UserId == id);
        me.HasPin.ShouldBe(true);
        me.ClockedIn.ShouldBe(false);

        var inRes = await (await Punch(kiosk, id, "1234")).ReadAsync<PunchResultDto>();
        inRes.Action.ShouldBe("In");
        inRes.Entry.ClockOutAt.ShouldBe(null);
        inRes.Entry.Hours.ShouldBe(null);

        staff = await kiosk.GetFromJsonAsync<List<TimeClockStaffDto>>("/api/v1/timeclock/staff", ApiFactory.Json);
        staff!.Single(s => s.UserId == id).ClockedIn.ShouldBe(true);

        var outRes = await (await Punch(kiosk, id, "1234")).ReadAsync<PunchResultDto>();
        outRes.Action.ShouldBe("Out");
        outRes.Entry.Id.ShouldBe(inRes.Entry.Id);
        Assert.NotNull(outRes.Entry.ClockOutAt);
        Assert.NotNull(outRes.Entry.Hours);

        (await Punch(kiosk, Guid.NewGuid(), "1234")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Staff_list_excludes_owners()
    {
        var kiosk = await _f.CashierAsync();
        var staff = await kiosk.GetFromJsonAsync<List<TimeClockStaffDto>>("/api/v1/timeclock/staff", ApiFactory.Json);
        using var db = _f.NewDb(null);
        var ownerId = (await db.Users.AsNoTracking().SingleAsync(x => x.Email == DevDataSeeder.AdminEmail)).Id;
        Assert.DoesNotContain(staff!, s => s.UserId == ownerId);
        Assert.NotEmpty(staff!);
    }

    [Fact]
    public async Task Pin_not_set_is_422_wrong_pin_is_403_and_bad_format_is_400()
    {
        var owner = await _f.AdminAsync();
        var kiosk = await _f.CashierAsync();
        var id = await NewStaffAsync();
        (await Punch(kiosk, id, "1234")).StatusCode.ShouldBe((HttpStatusCode)422);
        await SetPin(owner, id, "4321");
        (await Punch(kiosk, id, "0000")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        foreach (var bad in new[] { "123", "1234567", "12a4", "" })
            (await owner.PutAsJsonAsync($"/api/v1/users/{id}/pin", new { pin = bad })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await owner.DeleteAsync($"/api/v1/users/{id}/pin")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Punch(kiosk, id, "4321")).StatusCode.ShouldBe((HttpStatusCode)422);
    }

    [Fact]
    public async Task Five_wrong_pins_lock_for_five_minutes_with_423()
    {
        var owner = await _f.AdminAsync();
        var kiosk = await _f.CashierAsync();
        var id = await NewStaffAsync();
        await SetPin(owner, id, "1111");
        for (var i = 0; i < 5; i++) (await Punch(kiosk, id, "9999")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Punch(kiosk, id, "9999")).StatusCode.ShouldBe((HttpStatusCode)423);
        (await Punch(kiosk, id, "1111")).StatusCode.ShouldBe((HttpStatusCode)423); // even the right PIN while locked

        using var db = _f.NewDb(null);
        var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.InRange(u.ClockPinLockedUntil!.Value, DateTime.UtcNow.AddMinutes(4), DateTime.UtcNow.AddMinutes(5.1));

        // Setting a new PIN clears the lock.
        await SetPin(owner, id, "2222");
        (await Punch(kiosk, id, "2222")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Successful_punch_resets_the_failed_counter()
    {
        var owner = await _f.AdminAsync();
        var kiosk = await _f.CashierAsync();
        var id = await NewStaffAsync();
        await SetPin(owner, id, "1111");
        for (var i = 0; i < 4; i++) (await Punch(kiosk, id, "0000")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Punch(kiosk, id, "1111")).StatusCode.ShouldBe(HttpStatusCode.OK);
        for (var i = 0; i < 4; i++) (await Punch(kiosk, id, "0000")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_at_another_location_cannot_set_pin_on_a_main_user()
    {
        var (_, email, pw) = await _f.CreateOtherLocationAsync(Roles.Admin);
        var otherAdmin = await _f.LoginAsync(email, pw);
        var id = await NewStaffAsync();
        (await otherAdmin.PutAsJsonAsync($"/api/v1/users/{id}/pin", new { pin = "1234" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await otherAdmin.DeleteAsync($"/api/v1/users/{id}/pin")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var db = _f.NewDb(null);
        (await db.Users.AsNoTracking().SingleAsync(x => x.Id == id)).ClockPinHash.ShouldBe(null);

        // An Admin may not touch an Owner's PIN.
        var ownerId = (await db.Users.AsNoTracking().SingleAsync(x => x.Email == DevDataSeeder.AdminEmail)).Id;
        (await otherAdmin.PutAsJsonAsync($"/api/v1/users/{ownerId}/pin", new { pin = "1234" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // Cashiers cannot use the admin endpoint at all.
        var cashier = await _f.CashierAsync();
        (await cashier.PutAsJsonAsync($"/api/v1/users/{id}/pin", new { pin = "1234" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cashier_sees_only_own_entries_while_manager_sees_all()
    {
        var owner = await _f.AdminAsync();
        var kiosk = await _f.CashierAsync();
        var other = await NewStaffAsync("Other Person");
        await SetPin(owner, other, "5555");
        (await kiosk.PutAsJsonAsync("/api/v1/timeclock/my-pin", new { pin = "123456" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await kiosk.PutAsJsonAsync("/api/v1/timeclock/my-pin", new { pin = "12" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var db = _f.NewDb(null);
        var cashierId = (await db.Users.AsNoTracking().SingleAsync(x => x.Email == DevDataSeeder.CashierEmail)).Id;
        (await Punch(kiosk, other, "5555")).EnsureSuccessStatusCode();
        (await Punch(kiosk, cashierId, "123456")).EnsureSuccessStatusCode();

        var mine = await kiosk.GetFromJsonAsync<List<TimeEntryDto>>($"/api/v1/timeclock/entries?userId={other}", ApiFactory.Json);
        Assert.NotEmpty(mine!);
        Assert.All(mine!, e => e.UserId.ShouldBe(cashierId));

        var manager = await _f.ManagerAsync(DevDataSeeder.MainLocationId);
        var all = await manager.GetFromJsonAsync<List<TimeEntryDto>>("/api/v1/timeclock/entries", ApiFactory.Json);
        Assert.Contains(all!, e => e.UserId == other);
        Assert.Contains(all!, e => e.UserId == cashierId);
        var one = await manager.GetFromJsonAsync<List<TimeEntryDto>>($"/api/v1/timeclock/entries?userId={other}", ApiFactory.Json);
        Assert.All(one!, e => e.UserId.ShouldBe(other));

        var future = Uri.EscapeDataString(DateTime.UtcNow.AddDays(1).ToString("O"));
        (await manager.GetFromJsonAsync<List<TimeEntryDto>>($"/api/v1/timeclock/entries?from={future}", ApiFactory.Json))!.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Owner_all_locations_session_gets_400_on_timeclock_but_can_set_own_pin()
    {
        var owner = await _f.OwnerAsync();
        (await owner.GetAsync("/api/v1/timeclock/staff")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.GetAsync("/api/v1/timeclock/entries")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync("/api/v1/timeclock/punch", new { userId = Guid.NewGuid(), pin = "1234" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await owner.PutAsJsonAsync("/api/v1/timeclock/my-pin", new { pin = "1234" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _f.CreateClient().GetAsync("/api/v1/timeclock/staff")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
