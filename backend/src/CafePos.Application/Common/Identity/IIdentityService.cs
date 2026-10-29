namespace CafePos.Application.Common.Identity;

/// <summary>
/// Port over ASP.NET Core Identity (UserManager) so Application code never references it. Identity operations commit through
/// the user store themselves (UserManager saves), unlike repositories, which are committed by <see cref="IUnitOfWork"/>.
/// </summary>
public interface IIdentityService
{
    Task<UserAccount?> FindByIdAsync(Guid id, CancellationToken ct);

    Task<UserAccount?> FindByEmailAsync(string email, CancellationToken ct);

    /// <summary>Creates the account. Identity rule failures (weak password, duplicate name) come back as a validation error.</summary>
    Task<Result<UserAccount>> CreateAsync(string email, string displayName, string password, bool isOwner, CancellationToken ct);

    /// <summary>Persists DisplayName, Active, IsOwner and the clock PIN fields of the account (validated and saved by UserManager).</summary>
    Task UpdateAsync(UserAccount account, CancellationToken ct);

    /// <summary>
    /// Copies the same fields onto the tracked identity entity without saving; they are committed together with the next
    /// <see cref="IUnitOfWork.SaveChangesAsync"/> (used when a user change must be atomic with other repository changes).
    /// </summary>
    void ApplyChanges(UserAccount account);

    Task<bool> CheckPasswordAsync(UserAccount account, string password, CancellationToken ct);

    Task<bool> IsLockedOutAsync(UserAccount account, CancellationToken ct);

    Task RecordAccessFailedAsync(UserAccount account, CancellationToken ct);

    Task ResetAccessFailedAsync(UserAccount account, CancellationToken ct);
}
