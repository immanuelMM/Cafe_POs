using CafePos.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CafePos.Infrastructure.Persistence;

/// <summary>
/// One-time Owner password recovery from "Bootstrap:ResetOwnerEmail" / "Bootstrap:ResetOwnerPassword", for hosts with no shell and
/// no reset flow. Acts only when BOTH are set and only on an EXISTING, active user whose email matches and who is an Owner; it never
/// creates users, reactivates anyone or touches non-owners. THREAT MODEL: whoever can set environment variables on the host already
/// controls the app and its database, so this grants no new privilege; restricting it to an existing Owner keeps it from becoming a
/// general account takeover or user-creation path. Effect: new password (validated by the Identity validators first), lockout and PIN
/// counters cleared, security stamp changed and every refresh token revoked so old sessions die. Idempotent: when the stored password
/// already matches and the account is not locked out nothing changes, so restarts do not keep revoking sessions. The password and
/// full email are never logged. SECURITY: remove both variables right after signing in.
/// </summary>
public static class OwnerPasswordReset
{
    private const long AdvisoryLockKey = 0x43AF_E905_B007_0002;

    public static async Task RunAsync(IServiceProvider sp, ILogger logger, CancellationToken ct = default)
    {
        var options = sp.GetRequiredService<IOptions<BootstrapOptions>>().Value;
        var email = options.ResetOwnerEmail?.Trim();
        var password = options.ResetOwnerPassword;
        var hasEmail = !string.IsNullOrEmpty(email);
        var hasPassword = !string.IsNullOrEmpty(password);

        if (!hasEmail && !hasPassword) return;
        if (hasEmail != hasPassword)
        {
            logger.LogWarning("Owner password reset skipped: Bootstrap:ResetOwnerEmail and Bootstrap:ResetOwnerPassword must both be set (only {Present} is). Nothing was changed.",
                hasEmail ? "ResetOwnerEmail" : "ResetOwnerPassword");
            return;
        }

        var masked = OwnerBootstrapper.Mask(email!);
        var db = sp.GetRequiredService<CafeDbContext>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var clock = sp.GetRequiredService<TimeProvider>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [AdvisoryLockKey], ct);

        var user = await users.FindByEmailAsync(email!);
        if (user is null)
        {
            logger.LogWarning("Owner password reset skipped: no user with email {Email}. Check Bootstrap__ResetOwnerEmail. Nothing was changed.", masked);
            return;
        }
        if (!user.IsOwner)
        {
            logger.LogWarning("Owner password reset skipped: {Email} is not an owner. Only an owner's password can be reset this way. Nothing was changed.", masked);
            return;
        }
        if (!user.Active)
        {
            logger.LogWarning("Owner password reset skipped: the owner {Email} is deactivated (it is not reactivated by this setting). Nothing was changed.", masked);
            return;
        }

        // Restart with the same config: already reset and usable, so do not revoke sessions again.
        if (await users.CheckPasswordAsync(user, password!) && !await users.IsLockedOutAsync(user))
        {
            logger.LogWarning("Bootstrap__ResetOwnerEmail and Bootstrap__ResetOwnerPassword are still set; remove them now (the owner password is already as configured, nothing was changed).");
            return;
        }

        var errors = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, password!);
            if (!result.Succeeded) errors.AddRange(result.Errors.Select(e => e.Code));
        }
        if (errors.Count > 0)
        {
            logger.LogError("Owner password reset skipped for {Email}: the new password was rejected ({Errors}). Fix Bootstrap__ResetOwnerPassword and restart. Nothing was changed.",
                masked, string.Join(", ", errors));
            return;
        }

        user.PasswordHash = users.PasswordHasher.HashPassword(user, password!);
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        user.ClockPinFailedCount = 0;
        user.ClockPinLockedUntil = null;
        var updated = await users.UpdateSecurityStampAsync(user); // changes the stamp and saves everything above
        if (!updated.Succeeded)
        {
            logger.LogError("Owner password reset failed for {Email}: {Errors}. Nothing was changed.", masked, string.Join(", ", updated.Errors.Select(e => e.Code)));
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var id = user.Id;
        await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        await tx.CommitAsync(ct);

        logger.LogWarning("Owner password reset for {Email}. Remove Bootstrap__ResetOwnerEmail and Bootstrap__ResetOwnerPassword now.", masked);
    }
}
