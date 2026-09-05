using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderToCash.Cqrs;
using OrderToCash.Fulfillment.Infrastructure;

namespace OrderToCash.Fulfillment;

/// <summary>
/// The Fulfillment service's composition root — the <c>OrdersHost</c> shape
/// (design.md §10.3), factored out of <c>Program.cs</c> so a test can drive
/// the SAME method <c>Program.cs</c> calls.
/// </summary>
public static class FulfillmentHost
{
    public static HostApplicationBuilder CreateBuilder(string[] args, Action<FulfillmentOptions> configure)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // ValidateOnBuild/ValidateScopes forced ON in EVERY environment —
        // Host.CreateApplicationBuilder only turns them on when the
        // environment is Development, which is nowhere this repository
        // actually runs (review D3 of feature 15, inherited unchanged).
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Services.AddFulfillment(configure);

        // AddDispatcher runs LAST, so a missing or duplicated command/query
        // handler is a boot failure, never a first-dispatch surprise.
        builder.Services.AddDispatcher(Assembly.GetExecutingAssembly());

        return builder;
    }
}
