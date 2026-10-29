using System.Runtime.CompilerServices;
using CafePos.Application.Common;
using CafePos.Application.Common.Identity;
using Microsoft.AspNetCore.Identity;

namespace CafePos.Infrastructure.Identity;

/// <summary>UserManager-backed implementation of <see cref="IIdentityService"/>. Accounts stay linked to their tracked entity.</summary>
internal sealed class IdentityService(UserManager<ApplicationUser> users) : IIdentityService
{
    private readonly ConditionalWeakTable<UserAccount, ApplicationUser> _entities = new();

    public async Task<UserAccount?> FindByIdAsync(Guid id, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        return user is null ? null : Wrap(user);
    }

    public async Task<UserAccount?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        return user is null ? null : Wrap(user);
    }

    public async Task<Result<UserAccount>> CreateAsync(string email, string displayName, string password, bool isOwner, CancellationToken ct)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = displayName, IsOwner = isOwner
        };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var fields = result.Errors
                .GroupBy(e => e.Code.Contains("Password", StringComparison.Ordinal) ? "password" : "email")
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
            return Error.Validation(fields);
        }
        return Wrap(user);
    }

    public async Task UpdateAsync(UserAccount account, CancellationToken ct)
    {
        var entity = await EntityAsync(account);
        Copy(account, entity);
        await users.UpdateAsync(entity);
    }

    public void ApplyChanges(UserAccount account)
    {
        if (!_entities.TryGetValue(account, out var entity))
            throw new InvalidOperationException("The account was not loaded through this identity service.");
        Copy(account, entity);
    }

    public async Task<bool> CheckPasswordAsync(UserAccount account, string password, CancellationToken ct) =>
        await users.CheckPasswordAsync(await EntityAsync(account), password);

    public async Task<bool> IsLockedOutAsync(UserAccount account, CancellationToken ct) =>
        await users.IsLockedOutAsync(await EntityAsync(account));

    public async Task RecordAccessFailedAsync(UserAccount account, CancellationToken ct) =>
        await users.AccessFailedAsync(await EntityAsync(account));

    public async Task ResetAccessFailedAsync(UserAccount account, CancellationToken ct) =>
        await users.ResetAccessFailedCountAsync(await EntityAsync(account));

    private UserAccount Wrap(ApplicationUser user)
    {
        var account = new UserAccount
        {
            Id = user.Id,
            Email = user.Email ?? "",
            DisplayName = user.DisplayName,
            Active = user.Active,
            IsOwner = user.IsOwner,
            ClockPinHash = user.ClockPinHash,
            ClockPinFailedCount = user.ClockPinFailedCount,
            ClockPinLockedUntil = user.ClockPinLockedUntil
        };
        _entities.AddOrUpdate(account, user);
        return account;
    }

    private async Task<ApplicationUser> EntityAsync(UserAccount account)
    {
        if (_entities.TryGetValue(account, out var entity)) return entity;
        entity = await users.FindByIdAsync(account.Id.ToString())
                 ?? throw new InvalidOperationException($"User {account.Id} no longer exists.");
        _entities.AddOrUpdate(account, entity);
        return entity;
    }

    private static void Copy(UserAccount account, ApplicationUser entity)
    {
        entity.DisplayName = account.DisplayName;
        entity.Active = account.Active;
        entity.IsOwner = account.IsOwner;
        entity.ClockPinHash = account.ClockPinHash;
        entity.ClockPinFailedCount = account.ClockPinFailedCount;
        entity.ClockPinLockedUntil = account.ClockPinLockedUntil;
    }
}
