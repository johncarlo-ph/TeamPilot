using System.Data;
using Microsoft.EntityFrameworkCore.Storage;
using TeamPilot.Application.Common.Interfaces;

namespace TeamPilot.Infrastructure.Persistence;

public class UnitOfWork(TeamPilotDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteInTransactionAsync(
            operation: _ => action(),
            verifySucceeded: _ => Task.FromResult(true),
            isolationLevel: IsolationLevel.Unspecified,
            cancellationToken: cancellationToken);
    }
}
