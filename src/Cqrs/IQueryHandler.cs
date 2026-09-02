namespace OrderToCash.Cqrs;

/// <summary>
/// Handles a query and produces a <typeparamref name="TResult"/>.
/// </summary>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
