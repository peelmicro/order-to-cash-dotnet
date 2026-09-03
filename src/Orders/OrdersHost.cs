using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderToCash.Cqrs;
using OrderToCash.Orders.Infrastructure;

namespace OrderToCash.Orders;

/// <summary>
/// The Orders service's composition root — factored out of <c>Program.cs</c>
/// (review D6, round 2) so a test can drive the SAME method
/// <c>Program.cs</c> calls, rather than reconstructing its own copy of the
/// wiring. <c>OrdersDispatcherRegistrationTests.RealHostComposition_...</c>
/// (round 1) called <c>BuildServiceProvider</c> with its OWN
/// <see cref="ServiceProviderOptions"/>, which proved the container refuses
/// a broken graph when asked to validate — not whether <c>Program.cs</c>
/// asks. Factoring this method out closes that gap: the flags live in
/// exactly one place, the test calls <see cref="CreateBuilder"/> and then
/// the returned builder's own <c>Build()</c>, and reverting the flags here
/// is the only way to make that call stop validating — which is exactly
/// what round 2's Q1 mutation (<c>ValidateOnBuild = true,</c> →
/// <c>ValidateOnBuild = false,</c>) now fails a real test rather than
/// leaving the suite green.
/// </summary>
public static class OrdersHost
{
    public static HostApplicationBuilder CreateBuilder(
        string[] args,
        Action<OrdersOutboxOptions> configureOutbox,
        Action<OrdersAcceptanceOptions> configureAcceptance)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // review D3: Host.CreateApplicationBuilder only turns ValidateOnBuild /
        // ValidateScopes ON when the environment is Development
        // (HostingHostBuilderExtensions' own default), so the same missing-port
        // misconfiguration that is loud in a developer's shell is SILENT wherever
        // ASPNETCORE_ENVIRONMENT/DOTNET_ENVIRONMENT is unset — which is every
        // container this repository's compose files and CI run, since Production
        // is the environment's own default when neither variable is set. Forcing
        // both unconditionally makes a missing or duplicated port fail Build()
        // exactly like a missing or duplicated command handler already does
        // (CLAUDE.md: "every port is registered explicitly ... the startup
        // validation pass is what turns 'a handler is missing' from a runtime
        // surprise into a boot failure ... DI failures must be loud at boot").
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Services.AddOrdersOutbox(configureOutbox);
        builder.Services.AddOrdersAcceptance(configureAcceptance);

        // AddDispatcher MUST run after the two calls above so every port
        // PlaceOrderCommandHandler needs is already registered, and it
        // throws DispatcherValidationException SYNCHRONOUSLY if
        // PlaceOrderCommand has zero or more than one handler — a boot
        // failure, never a first-dispatch surprise.
        builder.Services.AddDispatcher(Assembly.GetExecutingAssembly());

        return builder;
    }
}
