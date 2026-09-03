namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// The transaction boundary sits above the repository (design.md §2.1, §4.1):
/// <c>R17</c> requires a dedup record, an aggregate change and outbox rows
/// from two collaborators inside one transaction, which a repository that
/// opened its own transaction could not guarantee. Every collaborator
/// resolved from the same scoped <c>OrdersDbContext</c> enlists in the
/// transaction <see cref="ExecuteAsync{T}"/> opens automatically — there is
/// no opaque transaction-context parameter to thread through, unlike #7's
/// Drizzle-shaped port (design.md §2.1).
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="work"/> inside ONE write-model transaction.
    /// Commits if it completes, rolls back if it throws, and never swallows
    /// the exception. The delegate MUST be safe to execute more than once —
    /// <see cref="EfCoreUnitOfWork"/> routes through
    /// <c>Database.CreateExecutionStrategy()</c> even with retries off today,
    /// so enabling retries later is a configuration change, not a rewrite of
    /// every transactional path (design.md §4.1 point 1). See §4.5 for how
    /// the repository makes the "safe to retry" contract true rather than
    /// merely assumed.
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);

    /// <summary>The no-result overload of <see cref="ExecuteAsync{T}"/>.</summary>
    Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}
