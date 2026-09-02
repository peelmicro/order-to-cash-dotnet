using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace OrderToCash.Cqrs;

/// <summary>
/// The one call a service's <c>Program.cs</c> makes to wire up the
/// dispatcher: registers <see cref="IDispatcher"/>, discovers every
/// <see cref="ICommandHandler{TCommand}"/> / <see cref="ICommandHandler{TCommand,TResult}"/> /
/// <see cref="IQueryHandler{TQuery,TResult}"/> / <see cref="IEventHandler{TEvent}"/>
/// implementation in the supplied assemblies by scanning rather than a
/// hand-maintained list (CLAUDE.md), and then runs the startup validation
/// pass before returning.
/// </summary>
public static class DispatcherServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IDispatcher"/>, scans <paramref name="assemblies"/>
    /// for handler implementations, and validates that every command and
    /// every query type discovered in those same assemblies has exactly one
    /// handler.
    /// </summary>
    /// <exception cref="DispatcherValidationException">
    /// A command or query type has zero registered handlers, or more than
    /// one. Thrown here — during registration, before the host is built or
    /// run — so the failure is loud at boot rather than on first dispatch.
    /// </exception>
    public static IServiceCollection AddDispatcher(this IServiceCollection services, params Assembly[] assemblies)
    {
        if (assemblies is null || assemblies.Length == 0)
        {
            throw new ArgumentException("At least one assembly must be supplied to scan for handlers.", nameof(assemblies));
        }

        return services.AddDispatcherFromTypes(assemblies.SelectMany(SafeGetTypes));
    }

    /// <summary>
    /// The scanning-and-validation core, taking the candidate type universe
    /// directly rather than the assemblies it came from.
    /// </summary>
    /// <remarks>
    /// <see langword="internal"/>, visible to <c>OrderToCash.Cqrs.UnitTests</c>
    /// only (see <c>InternalsVisibleTo.cs</c>). The production entry point is
    /// always <see cref="AddDispatcher"/> — assembly scan is the acceptance
    /// criterion. This seam exists so the zero-handler and multiple-handler
    /// validation tests can hand the validator a small, hand-picked type
    /// universe instead of this test assembly's own full reflection surface,
    /// which also has to carry the well-formed fixtures every other test in
    /// this project dispatches through. Scanning the whole test assembly for
    /// every scenario would make the well-formed fixtures and the
    /// deliberately-broken ones fight over the same command/query types.
    /// </remarks>
    internal static IServiceCollection AddDispatcherFromTypes(this IServiceCollection services, IEnumerable<Type> candidateTypes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(candidateTypes);

        // D5 (progress/review_cqrs_dispatcher.md): AddDispatcherFromTypes
        // validates only the type universe of the call it is in. A second
        // call on the same IServiceCollection would (a) validate a second,
        // DISJOINT universe — a command declared in the first call's
        // assemblies with its handler only in the second call's assemblies
        // would be reported as having zero handlers even though one exists
        // — and (b) register a second IDispatcher. Rather than attempt to
        // merge two partial scans after the fact, AddDispatcher is exactly-
        // once per IServiceCollection by contract: refuse the second call
        // outright, with a message naming the fix (pass every relevant
        // assembly to one call).
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IDispatcher)))
        {
            throw new InvalidOperationException(
                "AddDispatcher was already called on this IServiceCollection. Call it exactly once, " +
                "passing every assembly that contains commands, queries, events or their handlers " +
                "together (AddDispatcher(assemblyA, assemblyB, ...)) — two separate calls each " +
                "validate only their own assemblies' type universe, so a command declared in one " +
                "assembly with its handler registered from another would be reported as having zero " +
                "handlers even though one exists.");
        }

        // Scoped, not singleton — see the remarks on Dispatcher for why. A
        // singleton Dispatcher would capture the DI container's root
        // IServiceProvider permanently, and every handler resolved through
        // it (including one with a scoped dependency such as an EF Core
        // DbContext) would resolve from root instead of the caller's scope.
        services.AddScoped<IDispatcher, Dispatcher>();

        // IsGenericTypeDefinition excluded: a real command/query/handler DTO
        // is never generic in this repository, and Assembly.GetTypes() only
        // ever yields the OPEN definition of a generic type declared in that
        // assembly (never a closed instantiation someone built elsewhere via
        // MakeGenericType) — so this filter is also what keeps a whole-
        // assembly scan from ever seeing a generic type as if it were a
        // concrete command.
        var concreteTypes = candidateTypes
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .ToArray();

        var commandHandlers = new Dictionary<Type, List<Type>>();
        var queryHandlers = new Dictionary<Type, List<Type>>();

        foreach (var concreteType in concreteTypes)
        {
            foreach (var implementedInterface in concreteType.GetInterfaces().Where(i => i.IsGenericType))
            {
                var definition = implementedInterface.GetGenericTypeDefinition();

                if (definition == typeof(ICommandHandler<>) || definition == typeof(ICommandHandler<,>))
                {
                    services.AddTransient(implementedInterface, concreteType);
                    Record(commandHandlers, implementedInterface, concreteType);
                }
                else if (definition == typeof(IQueryHandler<,>))
                {
                    services.AddTransient(implementedInterface, concreteType);
                    Record(queryHandlers, implementedInterface, concreteType);
                }
                else if (definition == typeof(IEventHandler<>))
                {
                    // Multiple handlers for the same fact type are
                    // legitimate (IDispatcher.PublishAsync fans out to all
                    // of them), so every implementation is added, never
                    // replaced.
                    services.AddTransient(implementedInterface, concreteType);
                }
            }
        }

        var declarationErrors = new List<string>();

        DispatcherRegistrationValidator.Validate(
            declarationErrors,
            ExpectedCommandHandlerServiceTypes(concreteTypes, declarationErrors),
            commandHandlers,
            ExpectedQueryHandlerServiceTypes(concreteTypes),
            queryHandlers);

        return services;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static void Record(Dictionary<Type, List<Type>> counts, Type serviceType, Type implementationType)
    {
        if (!counts.TryGetValue(serviceType, out var implementations))
        {
            implementations = [];
            counts[serviceType] = implementations;
        }

        implementations.Add(implementationType);
    }

    /// <summary>
    /// Every <see cref="ICommandHandler{TCommand}"/> / <see cref="ICommandHandler{TCommand,TResult}"/>
    /// closed service type a command type discovered among
    /// <paramref name="concreteTypes"/> requires — the universe the
    /// "zero handlers" half of validation checks registrations against.
    /// </summary>
    /// <param name="concreteTypes">The candidate type universe to scan for command types.</param>
    /// <param name="declarationErrors">
    /// Receives one entry per command type that implements BOTH
    /// <see cref="ICommand"/> and <see cref="ICommand{TResult}"/> (D6,
    /// progress/review_cqrs_dispatcher.md) — <see cref="ICommand"/>'s own
    /// remarks say "either ... or ... never both, and never neither", and
    /// nothing enforced that until now. Such a type contributes no expected
    /// service type at all: it is not a "missing handler" or a "duplicate
    /// handler", it is a malformed declaration, and reporting it as one of
    /// those two would be misleading.
    /// </param>
    private static IReadOnlyCollection<Type> ExpectedCommandHandlerServiceTypes(
        IEnumerable<Type> concreteTypes,
        List<string> declarationErrors)
    {
        var expected = new List<Type>();

        foreach (var commandType in concreteTypes)
        {
            var implementsVoidCommand = false;
            Type? resultCommandInterface = null;

            foreach (var implementedInterface in commandType.GetInterfaces())
            {
                if (implementedInterface == typeof(ICommand))
                {
                    implementsVoidCommand = true;
                }
                else if (implementedInterface.IsGenericType && implementedInterface.GetGenericTypeDefinition() == typeof(ICommand<>))
                {
                    resultCommandInterface = implementedInterface;
                }
            }

            if (implementsVoidCommand && resultCommandInterface is not null)
            {
                declarationErrors.Add(
                    $"{commandType} implements both {typeof(ICommand)} and {resultCommandInterface} — " +
                    "a command must implement exactly one of ICommand (no result) or ICommand<TResult> " +
                    "(a result), never both.");
                continue;
            }

            if (implementsVoidCommand)
            {
                expected.Add(typeof(ICommandHandler<>).MakeGenericType(commandType));
            }
            else if (resultCommandInterface is not null)
            {
                var resultType = resultCommandInterface.GetGenericArguments()[0];
                expected.Add(typeof(ICommandHandler<,>).MakeGenericType(commandType, resultType));
            }
        }

        return expected;
    }

    /// <summary>
    /// Every <see cref="IQueryHandler{TQuery,TResult}"/> closed service type
    /// a query type discovered among <paramref name="concreteTypes"/> requires.
    /// </summary>
    private static IReadOnlyCollection<Type> ExpectedQueryHandlerServiceTypes(IEnumerable<Type> concreteTypes)
    {
        var expected = new List<Type>();

        foreach (var queryType in concreteTypes)
        {
            foreach (var implementedInterface in queryType.GetInterfaces())
            {
                if (implementedInterface.IsGenericType && implementedInterface.GetGenericTypeDefinition() == typeof(IQuery<>))
                {
                    var resultType = implementedInterface.GetGenericArguments()[0];
                    expected.Add(typeof(IQueryHandler<,>).MakeGenericType(queryType, resultType));
                }
            }
        }

        return expected;
    }
}
