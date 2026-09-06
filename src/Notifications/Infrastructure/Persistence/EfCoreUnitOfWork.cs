// COPY OF — src/Orders/Infrastructure/Persistence/EfCoreUnitOfWork.cs
using System.Data;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Notifications.Application.Ports;

namespace OrderToCash.Notifications.Infrastructure.Persistence;

/// <summary>
/// One <see cref="Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction"/>
/// over the scoped <see cref="NotificationsDbContext"/> — every collaborator
/// resolved from the same DI scope enlists automatically, which is what
/// makes the canonical <c>IdempotentConsumer</c>'s dedup insert transactional
/// with no <c>tx</c> parameter anywhere, exactly as it is in every other
/// write model.
/// </summary>
/// <remarks>
/// The delegate handed to <see cref="ExecuteAsync{T}"/> MUST be safe to
/// execute more than once. Always routed through
/// <c>CreateExecutionStrategy()</c>, even though retries are off today.
/// <c>IsolationLevel.ReadCommitted</c> stated explicitly, not inherited.
/// </remarks>
public sealed class EfCoreUnitOfWork(NotificationsDbContext db) : IUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var result = await work(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    public async Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken) =>
        await ExecuteAsync<object?>(
            async ct =>
            {
                await work(ct);
                return null;
            },
            cancellationToken);
}
