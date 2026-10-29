using CafePos.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CafePos.Infrastructure.Persistence;

/// <summary>
/// Creates the first Owner (and a first location when none exists) from the "Bootstrap" configuration section, for hosts with no
/// shell where production is never seeded. It acts only when BOTH OwnerEmail and OwnerPassword are set AND the users table is
/// completely empty, so it can never re-create or overwrite an owner. The check-and-create runs in one transaction guarded by a
/// PostgreSQL advisory lock, so restarts and two instances racing at first boot produce exactly one owner and one location; a
/// unique-violation that still slips through is treated as "someone else won". An invalid password is logged and skipped
/// (startup continues). Neither the password nor the full email is ever logged.
/// SECURITY: remove Bootstrap__OwnerEmail / Bootstrap__OwnerPassword from the environment after the first sign-in.
/// </summary>
public static class OwnerBootstrapper
{
    public const string DefaultOwnerName = "Owner";
    public const string DefaultLocationName = "Main";
    private const long AdvisoryLockKey = 0x43AF_E905_B007_0001; // arbitrary constant shared by all instances

    public static async Task RunAsync(IServiceProvider sp, ILogger logger, CancellationToken ct = default)
    {
        var options = sp.GetRequiredService<IOptions<BootstrapOptions>>().Value;
        var email = options.OwnerEmail?.Trim();
        var password = options.OwnerPassword;
        var hasEmail = !string.IsNullOrEmpty(email);
        var hasPassword = !string.IsNullOrEmpty(password);

        if (!hasEmail && !hasPassword) return;
        if (hasEmail != hasPassword)
        {
            logger.LogWarning("Owner bootstrap skipped: Bootstrap:OwnerEmail and Bootstrap:OwnerPassword must both be set (only {Present} is).",
                hasEmail ? "OwnerEmail" : "OwnerPassword");
            return;
        }

        // Configuration problems are found before any database work: nothing is created, startup continues.
        var defaults = new Location();
        var timeZone = defaults.TimeZone;
        if (!string.IsNullOrWhiteSpace(options.LocationTimeZone))
        {
            timeZone = options.LocationTimeZone.Trim();
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _))
            {
                logger.LogError("Owner bootstrap skipped: Bootstrap:LocationTimeZone is not a known time zone id. Fix it and restart.");
                return;
            }
        }
        var vat = options.LocationVatRatePercent ?? defaults.VatRatePercent;
        if (vat < 0m || vat > 100m)
        {
            logger.LogError("Owner bootstrap skipped: Bootstrap:LocationVatRatePercent must be between 0 and 100. Fix it and restart.");
            return;
        }

        var db = sp.GetRequiredService<CafeDbContext>();
        if (await db.Users.AnyAsync(ct)) // the normal case on every start after the first
        {
            if (options.IsConfigured)
                logger.LogWarning("Bootstrap__OwnerPassword is still set; remove it - if the database is ever recreated (e.g. free-plan expiry) it would recreate an Owner with this password.");
            return;
        }

        var users = sp.GetRequiredService<UserManager<Identity.ApplicationUser>>();
        var displayName = string.IsNullOrWhiteSpace(options.OwnerName) ? DefaultOwnerName : options.OwnerName.Trim();
        var user = new Identity.ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = displayName, IsOwner = true
        };

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [AdvisoryLockKey], ct);
            if (await db.Users.AnyAsync(ct)) return; // another instance finished first

            // CreateAsync runs the user (email) and password validators exactly once; a rejection rolls the transaction back.
            var created = await users.CreateAsync(user, password!);
            if (!created.Succeeded)
            {
                logger.LogError("Owner bootstrap failed for {Email}: {Errors}. Fix Bootstrap:OwnerEmail / Bootstrap:OwnerPassword and restart; startup continues without an owner.",
                    Mask(email!), string.Join(", ", created.Errors.Select(e => e.Code)));
                return;
            }

            if (!await db.Locations.AnyAsync(ct))
            {
                var name = string.IsNullOrWhiteSpace(options.LocationName) ? DefaultLocationName : options.LocationName.Trim();
                db.Locations.Add(new Location { Id = Guid.NewGuid(), Name = name, TimeZone = timeZone, VatRatePercent = vat });
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Owner bootstrap created location {LocationName} with time zone {TimeZone} and VAT {Vat}%. Change these in the app if they are not right.", name, timeZone, vat);
            }
            await tx.CommitAsync(ct);
            logger.LogWarning("Owner bootstrap created the first owner {Email}. Remove Bootstrap__OwnerEmail and Bootstrap__OwnerPassword from the environment now.", Mask(email!));
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            logger.LogInformation("Owner bootstrap lost a race with another instance; nothing to do.");
        }
    }

    /// <summary>"alice@example.com" becomes "a***@e***.com": enough to recognise, not enough to disclose.</summary>
    public static string Mask(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return email.Length == 0 ? "***" : email[0] + "***";
        var domain = email[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        var maskedDomain = domain.Length == 0 ? "" : dot > 0 ? domain[0] + "***" + domain[dot..] : domain[0] + "***";
        return email[0] + "***@" + maskedDomain;
    }
}
