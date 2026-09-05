// COPY OF — src/Orders/Application/Ports/IUnitOfWork.cs
namespace OrderToCash.Fulfillment.Application.Ports;

/// <summary>
/// The transaction boundary sits above the repository. Every collaborator
/// resolved from the same scoped <c>FulfillmentDbContext</c> enlists in the
/// transaction <see cref="ExecuteAsync{T}"/> opens automatically — there is
/// no opaque transaction-context parameter to thread through.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="work"/> inside ONE write-model transaction.
    /// Commits if it completes, rolls back if it throws, and never swallows
    /// the exception. The delegate MUST be safe to execute more than once —
    /// <c>EfCoreUnitOfWork</c> routes through
    /// <c>Database.CreateExecutionStrategy()</c> even with retries off today.
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);

    /// <summary>The no-result overload of <see cref="ExecuteAsync{T}"/>.</summary>
    Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken);
}
