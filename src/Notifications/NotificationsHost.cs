using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderToCash.Cqrs;
using OrderToCash.Notifications.Infrastructure;

namespace OrderToCash.Notifications;

/// <summary>
/// The Notifications service's composition root — the <c>FulfillmentHost</c>/
/// <c>BillingHost</c> shape, factored out of <c>Program.cs</c> so a test can
/// drive the SAME method <c>Program.cs</c> calls.
/// </summary>
public static class NotificationsHost
{
    public static HostApplicationBuilder CreateBuilder(string[] args, Action<NotificationsOptions> configure)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // ValidateOnBuild/ValidateScopes forced ON in EVERY environment —
        // Host.CreateApplicationBuilder only turns them on when the
        // environment is Development, which is nowhere this repository
        // actually runs.
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Services.AddNotifications(configure);

        // AddDispatcher runs LAST, so a missing or duplicated command
        // handler is a boot failure, never a first-dispatch surprise.
        builder.Services.AddDispatcher(Assembly.GetExecutingAssembly());

        return builder;
    }
}
