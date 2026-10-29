namespace CafePos.Application.Common;

/// <summary>Commits the changes tracked by the repositories. Repositories never call SaveChanges themselves.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Starts an explicit transaction for a use case that spans several statements (order completion, manual stock moves).</summary>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>Drops every tracked entity (used after a conditional bulk update that bypassed the change tracker).</summary>
    void ClearTracking();
}
