// COPY OF — src/Orders/Infrastructure/Persistence/EfCoreUnitOfWork.cs
using System.Data;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Infrastructure.Observability;

namespace OrderToCash.Billing.Infrastructure.Persistence;

/// <summary>
/// One <see cref="Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction"/>
/// over the scoped <see cref="BillingDbContext"/> — every collaborator
/// resolved from the same DI scope enlists automatically, which is what
/// makes "the credit line row, the ledger entry rows and the outbox row in
/// one transaction" true with no <c>tx</c> parameter anywhere.
/// </summary>
/// <remarks>
/// The delegate handed to <see cref="ExecuteAsync{T}"/> MUST be safe to
/// execute more than once. Always routed through
/// <c>CreateExecutionStrategy()</c>, even though retries are off today.
/// <c>IsolationLevel.ReadCommitted</c> stated explicitly, not inherited.
/// </remarks>
public sealed class EfCoreUnitOfWork(BillingDbContext db) : IUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // OR4/design.md §5.4, ledger L23 — the write-database hop of
            // R56's trace: one hand-started Activity for the whole
            // transaction, nested under whatever span is Activity.Current
            // at this point (the RPC responder's own span).
            using var activity = OtcActivity.Source.StartActivity("writemodel.transaction", ActivityKind.Internal);
            activity?.SetTag("db.system", "mssql");
            activity?.SetTag("db.name", db.Database.GetDbConnection().Database);

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
