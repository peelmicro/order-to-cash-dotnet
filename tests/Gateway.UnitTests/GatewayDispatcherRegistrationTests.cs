using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using OrderToCash.Cqrs;
using OrderToCash.Gateway;
using OrderToCash.Gateway.Application.Ports;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// <see cref="GatewayHost.CreateBuilder"/> succeeds when every port is
/// registered and fails <c>Build()</c> — never only on first dispatch —
/// when one is removed. <c>ValidateOnBuild</c>/<c>ValidateScopes</c> forced
/// on in every environment is what makes the negative case throw, mirroring
/// every other service's own <c>*DispatcherRegistrationTests</c>
/// (e.g. <c>FulfillmentDispatcherRegistrationTests</c>). No real MS-SQL /
/// NATS / Mongo needed — <c>ValidateOnBuild</c> checks the DI GRAPH, it
/// does not connect (probed directly here too: green with garbage
/// connection strings/URLs).
/// </summary>
public sealed class GatewayDispatcherRegistrationTests
{
    [Fact]
    public void AddDispatcher_OverTheGatewayAssembly_RegistersEveryCommandAndQueryWithExactlyOneHandler()
    {
        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddDispatcher(typeof(GatewayHost).Assembly));

        Assert.Null(exception);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IDispatcher));
    }

    [Fact]
    public void RealHostComposition_Build_SucceedsWhenEveryPortIsRegisteredAndFailsWhenOneIsRemoved()
    {
        var positiveBuilder = BuildRealHostBuilder();
        var positiveException = Record.Exception(() => positiveBuilder.Build());
        Assert.Null(positiveException);

        var negativeBuilder = BuildRealHostBuilder();
        var removed = negativeBuilder.Services.Single(d => d.ServiceType == typeof(IOrderReadModel));
        negativeBuilder.Services.Remove(removed);

        var negativeException = Record.Exception(() => negativeBuilder.Build());
        Assert.NotNull(negativeException);
        Assert.Contains(nameof(IOrderReadModel), negativeException!.ToString());
    }

    private static Microsoft.AspNetCore.Builder.WebApplicationBuilder BuildRealHostBuilder() =>
        GatewayHost.CreateBuilder(
            args: ["--urls", "http://127.0.0.1:0"],
            configure: options =>
            {
                options.Nats.Url = "nats://127.0.0.1:1";
                options.Mongo.ConnectionUri = "mongodb://127.0.0.1:1/?connectTimeoutMS=1";
                options.Mongo.Database = "otc_read_model_validate_on_build_probe";
            });
}
