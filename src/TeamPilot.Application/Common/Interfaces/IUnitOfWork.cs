namespace TeamPilot.Application.Common.Interfaces;

/// <summary>
/// Abstraction over persistence commit/transaction concerns, keeping EF Core types
/// (e.g. <c>IDbContextTransaction</c>) out of the Application layer.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="action"/> inside a database transaction, committing on success
    /// and rolling back if it throws.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default);
}
