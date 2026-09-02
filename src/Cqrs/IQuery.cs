namespace OrderToCash.Cqrs;

/// <summary>
/// Marker for a query that produces a <typeparamref name="TResult"/>. Every
/// query DTO an application layer defines implements this interface, for
/// the same reason <see cref="ICommand{TResult}"/> exists on the command
/// side — it is what the startup validation pass enumerates to find a query
/// with zero handlers.
/// </summary>
/// <typeparam name="TResult">The type the query's execution returns.</typeparam>
public interface IQuery<TResult>
{
}
