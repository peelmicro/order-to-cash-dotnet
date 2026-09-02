namespace OrderToCash.Cqrs;

/// <summary>
/// The startup validation pass. Commands and queries are validated
/// identically — see the class remarks for why a query does not get a
/// looser rule than a command here, even though CLAUDE.md and the feature's
/// acceptance criteria state the rule only for commands.
/// </summary>
/// <remarks>
/// <b>Why a query follows the same rule as a command, not the asymmetry
/// events get.</b> A fact (<see cref="IEventHandler{TEvent}"/>) may
/// legitimately have zero listeners — it was published for whoever cares,
/// and "nobody cares yet" is a normal state of a system mid-build (#7's
/// <c>EventBus</c> behaves this way, and this repository publishes its own
/// facts long before every consumer exists). A query is not that: it is
/// answered synchronously, with exactly one <c>TResult</c> returned to the
/// caller. Zero handlers means the read side can never be served — the same
/// failure class as a command with no handler, not the command's
/// "no listener yet" cousin. And two handlers for a query is worse than two
/// for a command: a command handler's caller only needs the mutation to
/// happen once and correctly, so a duplicate is unambiguously a
/// misconfiguration either way, but a query with two candidate answers has
/// no principled way to pick one — returning the first one resolved by the
/// container would silently depend on registration order, which is exactly
/// the kind of DI failure CLAUDE.md says must be loud at boot, not
/// discovered from an inconsistent answer at runtime. So queries hold to
/// the same "exactly one" rule as commands, and the validator's logic (and
/// its tests) stays one rule applied twice rather than three separate rules
/// for three cases.
/// </remarks>
internal static class DispatcherRegistrationValidator
{
    public static void Validate(
        IReadOnlyCollection<string> declarationErrors,
        IReadOnlyCollection<Type> expectedCommandHandlerServiceTypes,
        IReadOnlyDictionary<Type, List<Type>> registeredCommandHandlers,
        IReadOnlyCollection<Type> expectedQueryHandlerServiceTypes,
        IReadOnlyDictionary<Type, List<Type>> registeredQueryHandlers)
    {
        // declarationErrors is malformed-declaration errors found while
        // computing the expected-handler universe itself (D6 — a command
        // type implementing both ICommand and ICommand<TResult>), seeded
        // first so a malformed command is reported for what it is rather
        // than surfacing only as a confusing "zero handlers" / "N handlers"
        // pair against two different expected service types.
        var errors = new List<string>(declarationErrors);

        CollectErrors("command", expectedCommandHandlerServiceTypes, registeredCommandHandlers, errors);
        CollectErrors("query", expectedQueryHandlerServiceTypes, registeredQueryHandlers, errors);

        if (errors.Count > 0)
        {
            throw new DispatcherValidationException(string.Join(Environment.NewLine, errors));
        }
    }

    private static void CollectErrors(
        string kind,
        IReadOnlyCollection<Type> expectedServiceTypes,
        IReadOnlyDictionary<Type, List<Type>> registered,
        List<string> errors)
    {
        foreach (var serviceType in expectedServiceTypes)
        {
            // registered.GetValueOrDefault returns null for a serviceType
            // that was never Record()-ed (the true zero-handlers case) — the
            // ?? [] folds that into the same empty, never-null List<Type>
            // implementations.Count == 0 checks below, so the two branches
            // are independent conditions on one guaranteed-non-null value
            // rather than a single compound condition coupling "key absent"
            // and "count == 0" together behind one nullable out var.
            var implementations = registered.GetValueOrDefault(serviceType) ?? [];

            if (implementations.Count == 0)
            {
                errors.Add($"No {kind} handler is registered for {serviceType}. Exactly one is required.");
            }
            else if (implementations.Count > 1)
            {
                var implementationNames = string.Join(", ", implementations.Select(t => t.FullName));
                errors.Add($"{implementations.Count} {kind} handlers are registered for {serviceType}: {implementationNames}. Exactly one is required.");
            }
        }
    }
}
