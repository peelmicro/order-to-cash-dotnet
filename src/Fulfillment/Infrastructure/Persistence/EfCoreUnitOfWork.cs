// COPY OF — src/Orders/Infrastructure/Persistence/EfCoreUnitOfWork.cs
using System.Data;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Application.Ports;

namespace OrderToCash.Fulfillment.Infrastructure.Persistence;

/// <summary>
/// One <see cref="Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction"/>
/// over the scoped <see cref="FulfillmentDbContext"/> — every collaborator
/// resolved from the same DI scope enlists automatically, which is what
/// makes "the stock rows, the reservation rows and the outbox row in one
/// transaction" true with no <c>tx</c> parameter anywhere.
/// </summary>
/// <remarks>
/// The delegate handed to <see cref="ExecuteAsync{T}"/> MUST be safe to
/// execute more than once. Always routed through
/// <c>CreateExecutionStrategy()</c>, even though retries are off today.
/// <c>IsolationLevel.ReadCommitted</c> stated explicitly, not inherited —
/// design.md §4.1 point 3.
/// </remarks>
public sealed class EfCoreUnitOfWork(FulfillmentDbContext db) : IUnitOfWork
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
