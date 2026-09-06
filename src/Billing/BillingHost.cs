using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderToCash.Billing.Infrastructure;
using OrderToCash.Cqrs;

namespace OrderToCash.Billing;

/// <summary>
/// The Billing service's composition root — the <c>FulfillmentHost</c>
/// shape (design.md §14.3), factored out of <c>Program.cs</c> so a test can
/// drive the SAME method <c>Program.cs</c> calls.
/// </summary>
public static class BillingHost
{
    public static HostApplicationBuilder CreateBuilder(string[] args, Action<BillingOptions> configure)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // ValidateOnBuild/ValidateScopes forced ON in EVERY environment —
        // Host.CreateApplicationBuilder only turns them on when the
        // environment is Development, which is nowhere this repository
        // actually runs (feature 15's review D3, inherited unchanged).
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Services.AddBilling(configure);

        // AddDispatcher runs LAST, so a missing or duplicated command/query
        // handler is a boot failure, never a first-dispatch surprise.
        builder.Services.AddDispatcher(Assembly.GetExecutingAssembly());

        return builder;
    }
}
