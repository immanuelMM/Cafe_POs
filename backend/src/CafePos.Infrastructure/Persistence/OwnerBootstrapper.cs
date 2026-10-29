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

        var db = sp.GetRequiredService<CafeDbContext>();
        if (await db.Users.AnyAsync(ct)) return; // the normal case on every start after the first

        var users = sp.GetRequiredService<UserManager<Identity.ApplicationUser>>();
        var displayName = string.IsNullOrWhiteSpace(options.OwnerName) ? DefaultOwnerName : options.OwnerName.Trim();
        var user = new Identity.ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = displayName, IsOwner = true
        };

        // Same password policy as every other account (Identity options), checked before touching the database.
        var problems = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var check = await validator.ValidateAsync(users, user, password!);
            if (!check.Succeeded) problems.AddRange(check.Errors.Select(e => e.Code));
        }
        if (problems.Count > 0)
        {
            logger.LogError("Owner bootstrap skipped for {Email}: the password does not meet the password policy ({Problems}). Fix Bootstrap:OwnerPassword and restart.",
                Mask(email!), string.Join(", ", problems));
            return;
        }

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [AdvisoryLockKey], ct);
            if (await db.Users.AnyAsync(ct)) return; // another instance finished first

            var created = await users.CreateAsync(user, password!);
            if (!created.Succeeded)
            {
                logger.LogError("Owner bootstrap failed for {Email}: {Errors}. Startup continues without creating the owner.",
                    Mask(email!), string.Join(", ", created.Errors.Select(e => e.Code)));
                return;
            }

            if (!await db.Locations.AnyAsync(ct))
            {
                var name = string.IsNullOrWhiteSpace(options.LocationName) ? DefaultLocationName : options.LocationName.Trim();
                db.Locations.Add(new Location { Id = Guid.NewGuid(), Name = name });
                await db.SaveChangesAsync(ct);
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
