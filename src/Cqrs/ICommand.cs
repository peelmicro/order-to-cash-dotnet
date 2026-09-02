namespace OrderToCash.Cqrs;

/// <summary>
/// Marker for a command that produces no result. Every command DTO an
/// application layer defines implements either this interface or
/// <see cref="ICommand{TResult}"/> — never both, and never neither.
/// "Never both" is enforced, not just documented: a type implementing both
/// this interface and <see cref="ICommand{TResult}"/> fails the startup
/// validation pass with a dedicated message rather than being silently
/// treated as requiring two handlers (D6, progress/review_cqrs_dispatcher.md;
/// see <c>DispatcherServiceCollectionExtensions.ExpectedCommandHandlerServiceTypes</c>).
/// "Never neither" is not separately enforced: a type that implements
/// neither marker is simply not a command by this dispatcher's definition,
/// so nothing about it is ever checked — the same way an arbitrary class
/// that happens to implement neither <see cref="ICommand"/> nor
/// <see cref="IQuery{TResult}"/> is invisible to validation.
/// </summary>
/// <remarks>
/// The marker exists so the startup validation pass
/// (<see cref="DispatcherServiceCollectionExtensions"/>) can enumerate
/// "every command that must have exactly one handler" by scanning the same
/// assemblies handlers are scanned from, rather than by a hand-maintained
/// list that drifts (CLAUDE.md, "Registration is by assembly scan"). Without
/// it, the "zero handlers" half of the startup check has nothing to
/// enumerate: a handler that is never written leaves no trace for a scan
/// that only looks at handler classes to find.
/// </remarks>
public interface ICommand
{
}

/// <summary>
/// Marker for a command that produces a <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TResult">The type the command's execution returns.</typeparam>
public interface ICommand<TResult>
{
}
