using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderToCash.Cqrs;
using OrderToCash.Projector.Infrastructure;

namespace OrderToCash.Projector;

/// <summary>
/// The Projector service's composition root — the <c>NotificationsHost</c>
/// shape, factored out of <c>Program.cs</c> so a test can drive the SAME
/// method <c>Program.cs</c> calls (<c>PR43</c>).
/// </summary>
public static class ProjectorHost
{
    public static HostApplicationBuilder CreateBuilder(string[] args, Action<ProjectorOptions> configure)
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

        builder.Services.AddProjector(configure);

        // AddDispatcher runs LAST, so a missing or duplicated command
        // handler is a boot failure, never a first-dispatch surprise.
        builder.Services.AddDispatcher(Assembly.GetExecutingAssembly());

        return builder;
    }
}
