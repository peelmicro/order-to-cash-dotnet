using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace OrderToCash.Cqrs;

/// <summary>
/// <see cref="IDispatcher"/>, resolving handlers from an
/// <see cref="IServiceProvider"/>. Durability never depends on this class —
/// the outbox and saga_commands tables remain the guarantee (CLAUDE.md); this
/// is only the in-process fast path from a Presentation/ endpoint or a saga
/// step down into Application/.
/// </summary>
/// <remarks>
/// Registered <b>scoped</b> by <see cref="DispatcherServiceCollectionExtensions.AddDispatcherFromTypes"/>,
/// never singleton — a singleton would be constructed once, from the DI
/// container's ROOT scope, and would then permanently store the root
/// <see cref="IServiceProvider"/> here, so every handler resolved through it
/// — including one depending on a scoped service such as an EF Core
/// <c>DbContext</c> — would resolve from root rather than the caller's
/// scope. Scoped registration is what makes the <see cref="IServiceProvider"/>
/// injected below the CALLER's scoped provider: a Presentation/ endpoint, a
/// Kafka consumer or a NATS responder resolves <see cref="IDispatcher"/>
/// from a scope it owns (an ASP.NET Core request scope, or one it opens
/// itself per message in a background service), and every handler this
/// class resolves inherits that same scope. See
/// <c>DispatcherScopeTests.SendAsync_ResolvesTheHandlerAndItsDependenciesFromTheCallersScope_NotTheRootProvider</c>
/// (progress/impl_cqrs_dispatcher.md, defect D1) — the singleton
/// registration this feature originally shipped with passed every other
/// test and still let two separate request scopes silently share one
/// captive dependency instance.
/// </remarks>
public sealed class Dispatcher : IDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public Dispatcher(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    public Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand
    {
        ArgumentNullException.ThrowIfNull(command);
        var handler = _serviceProvider.GetRequiredService<ICommandHandler<TCommand>>();
        return handler.HandleAsync(command, cancellationToken);
    }

    public Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        ArgumentNullException.ThrowIfNull(command);
        var handler = _serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>();
        return handler.HandleAsync(command, cancellationToken);
    }

    public Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken)
        where TQuery : IQuery<TResult>
    {
        ArgumentNullException.ThrowIfNull(query);
        var handler = _serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>();
        return handler.HandleAsync(query, cancellationToken);
    }

    public async Task PublishAsync(object @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // Resolved by the RUNTIME type of @event, not a compile-time
        // generic parameter — see IDispatcher.PublishAsync's remarks
        // (progress/review_cqrs_dispatcher.md, D3). This is the one place
        // in this file that pays for it: MakeGenericType + a cached
        // MethodInfo.Invoke per publish, instead of a direct generically-
        // typed call. Facts are published per outbox row / per consumed
        // message, not in a hot per-request loop, so the cost is a handful
        // of reflection calls per fact rather than per request — judged
        // worth it here for correctness on the call shape (publishing
        // through a base/interface-typed variable) feature 14's outbox
        // drain and feature 15's aggregate drain both use.
        var eventType = @event.GetType();
        var handlerServiceType = typeof(IEventHandler<>).MakeGenericType(eventType);
        var handleAsyncMethod = _handleAsyncMethodsByEventType.GetOrAdd(eventType, static (_, serviceType) =>
            serviceType.GetMethod(nameof(IEventHandler<object>.HandleAsync))
                ?? throw new InvalidOperationException(
                    $"{serviceType} does not declare a HandleAsync method — this should be unreachable, IEventHandler<T> always declares one."),
            handlerServiceType);

        foreach (var handler in _serviceProvider.GetServices(handlerServiceType))
        {
            var task = (Task)handleAsyncMethod.Invoke(handler, [@event, cancellationToken])!;
            await task.ConfigureAwait(false);
        }
    }

    // Keyed by the event's runtime Type rather than the closed
    // IEventHandler<> service type: one lookup per distinct fact type for
    // the whole process lifetime, populated lazily on first publish of that
    // type.
    private static readonly ConcurrentDictionary<Type, MethodInfo> _handleAsyncMethodsByEventType = new();
}
