using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderToCash.Cqrs;
using OrderToCash.Fulfillment;
using OrderToCash.Fulfillment.Application.Ports;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>
/// design.md §10.3 — <see cref="FulfillmentHost.CreateBuilder"/> succeeds
/// when every port is registered and fails <c>Build()</c> (never only the
/// first dispatch) when one is removed. <c>ValidateOnBuild</c>/<c>ValidateScopes</c>
/// forced on in every environment is what makes the negative case throw —
/// flipping either to <see langword="false"/> makes
/// <see cref="RealHostComposition_Build_SucceedsWhenEveryPortIsRegisteredAndFailsWhenOneIsRemoved"/>
/// FAIL (the E11 arming target).
/// </summary>
public sealed class FulfillmentDispatcherRegistrationTests
{
    [Fact]
    public void AddDispatcher_OverTheFulfillmentAssembly_RegistersEveryCommandAndQueryWithExactlyOneHandler()
    {
        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddDispatcher(typeof(FulfillmentHost).Assembly));

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
        var removed = negativeBuilder.Services.Single(d => d.ServiceType == typeof(IStockItemRepository));
        negativeBuilder.Services.Remove(removed);

        var negativeException = Record.Exception(() => negativeBuilder.Build());
        Assert.NotNull(negativeException);
        Assert.Contains(nameof(IStockItemRepository), negativeException!.ToString());
    }

    private static HostApplicationBuilder BuildRealHostBuilder() =>
        FulfillmentHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                // No real MS-SQL/NATS/Kafka needed — ValidateOnBuild checks
                // the DI GRAPH, it does not connect (Orders' own D3 probe,
                // inherited).
                options.ConnectionString = "Server=localhost;Database=otc_fulfillment_validate_on_build_probe;Trusted_Connection=True;";
                options.Nats.Url = "nats://127.0.0.1:1";
                options.Kafka.BootstrapServers = "127.0.0.1:1";
            });
}
