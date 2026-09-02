namespace OrderToCash.Cqrs;

/// <summary>
/// Handles a command that produces no result.
/// </summary>
/// <remarks>
/// A genuinely separate interface from <see cref="ICommandHandler{TCommand,TResult}"/>,
/// not the same shape unified behind a <c>Unit</c>-like marker return type.
/// Two reasons, in order of weight. First, the acceptance criteria for this
/// feature name both shapes explicitly ("ICommandHandler&lt;T&gt;,
/// ICommandHandler&lt;T,R&gt;"), so the decision was already made at the
/// point this feature was scoped. Second, a <c>Unit</c> marker exists only
/// to make a void-returning method fit a signature built for a value — every
/// caller of the void form would receive a value it must ignore, and every
/// handler author would return a token that carries no information. <c>Task</c>
/// already expresses "no result" in .NET without an invented type standing
/// in for it, so the two-interface split costs one extra interface
/// declaration and buys handler authors a signature that says exactly what
/// it means.
/// </remarks>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Handles a command that produces a <typeparamref name="TResult"/>.
/// </summary>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}
