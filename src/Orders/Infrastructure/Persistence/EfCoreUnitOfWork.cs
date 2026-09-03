using System.Data;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Persistence;

/// <summary>
/// design.md §4.1, exactly. One <see cref="IDbContextTransaction"/> over the
/// scoped <see cref="OrdersDbContext"/> — every collaborator resolved from
/// the same DI scope enlists automatically (design.md §2.1), which is what
/// makes <c>R17</c>'s "dedup record, aggregate change and outbox rows in one
/// transaction" true without a <c>tx</c> parameter anywhere.
/// </summary>
/// <remarks>
/// The delegate handed to <see cref="ExecuteAsync{T}"/> MUST be safe to
/// execute more than once. Always routed through
/// <c>CreateExecutionStrategy()</c>, even though retries are off today: EF
/// Core throws the moment <c>EnableRetryOnFailure</c> is enabled on a
/// context whose code calls <c>BeginTransactionAsync</c> directly, so paying
/// the (here, single-pass) cost now means enabling retries later is a
/// configuration change rather than a rewrite of every transactional path.
/// Retries are deliberately NOT enabled by this feature — a retrying
/// strategy re-executes the delegate, and a delegate that mutated an
/// aggregate whose events were already drained would commit aggregate rows
/// with no outbox rows (design.md §4.5's OI9 hazard). The repository makes
/// the "safe to retry" contract true by invalidating the scope on failure
/// rather than by hoping the caller notices.
/// </remarks>
public sealed class EfCoreUnitOfWork(OrdersDbContext db) : IUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // IsolationLevel.ReadCommitted stated explicitly, not inherited —
            // design.md §4.1 point 3: the relay's claim (§5.2) depends on it,
            // and an ambient TransactionScope opened by a future caller could
            // otherwise change the level under it.
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
