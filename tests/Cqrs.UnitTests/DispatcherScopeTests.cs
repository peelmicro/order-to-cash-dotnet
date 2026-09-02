using Microsoft.Extensions.DependencyInjection;
using OrderToCash.Cqrs;
using OrderToCash.Cqrs.UnitTests.Fixtures;
using Xunit;

namespace OrderToCash.Cqrs.UnitTests;

/// <summary>
/// Guards the defect the reviewer found (progress/review_cqrs_dispatcher.md,
/// D1): registering <see cref="IDispatcher"/> as a singleton means
/// <c>Dispatcher</c> captures the DI container's ROOT
/// <see cref="IServiceProvider"/> at construction, permanently — so every
/// handler it resolves, including one depending on a scoped service (a real
/// EF Core <c>DbContext</c> from feature 15 onward), resolves from root
/// rather than the caller's scope. Two dispatches from two different scopes
/// then silently see the SAME captive instance instead of two independent
/// ones — a defect this project's own startup validation pass cannot see,
/// because <c>Dispatcher</c> is a service locator and hides its resolutions
/// from the built-in call-site validator.
/// </summary>
/// <remarks>
/// Deliberately built without <see cref="ServiceProviderOptions.ValidateScopes"/>
/// set — a bare <c>BuildServiceProvider()</c> defaults it to
/// <see langword="false"/>, matching a Production host rather than a
/// Development one. That is the more dangerous of the two failure modes the
/// review recorded: with scope validation off, the defect does not throw at
/// all, it just silently hands two different request scopes the same
/// dependency instance — exactly what this test asserts against.
/// </remarks>
public sealed class DispatcherScopeTests
{
    [Fact]
    public async Task SendAsync_ResolvesTheHandlerAndItsDependenciesFromTheCallersScope_NotTheRootProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedDependency>();
        services.AddDispatcher(typeof(ScopedProbeCommand).Assembly);

        await using var provider = services.BuildServiceProvider();

        Guid firstScopeInstanceId;
        Guid secondScopeInstanceId;

        await using (var firstScope = provider.CreateAsyncScope())
        {
            var dispatcher = firstScope.ServiceProvider.GetRequiredService<IDispatcher>();
            firstScopeInstanceId = await dispatcher.SendAsync<ScopedProbeCommand, Guid>(
                new ScopedProbeCommand(), CancellationToken.None);
        }

        await using (var secondScope = provider.CreateAsyncScope())
        {
            var dispatcher = secondScope.ServiceProvider.GetRequiredService<IDispatcher>();
            secondScopeInstanceId = await dispatcher.SendAsync<ScopedProbeCommand, Guid>(
                new ScopedProbeCommand(), CancellationToken.None);
        }

        // Two separate scopes must see two separate ScopedDependency
        // instances. Under the AddSingleton<IDispatcher, Dispatcher>
        // registration this defect started from, both dispatches resolve
        // ICommandHandler<ScopedProbeCommand, Guid> — and therefore its
        // ScopedDependency — from the same root provider, so this assertion
        // fails: the two ids come back equal (a captive instance), not the
        // exception a scoped-service-from-root misuse would throw under
        // ValidateScopes: true. That is the point — this is the silent
        // failure mode, not the loud one.
        Assert.NotEqual(firstScopeInstanceId, secondScopeInstanceId);
    }
}
