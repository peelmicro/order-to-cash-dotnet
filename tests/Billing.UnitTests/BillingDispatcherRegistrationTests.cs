using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderToCash.Billing;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Cqrs;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// design.md §14.3 — <see cref="BillingHost.CreateBuilder"/> succeeds when
/// every port is registered and fails <c>Build()</c> (never only the first
/// dispatch) when one is removed. <c>ValidateOnBuild</c>/<c>ValidateScopes</c>
/// forced on in every environment is what makes the negative case throw.
/// </summary>
public sealed class BillingDispatcherRegistrationTests
{
    [Fact]
    public void AddDispatcher_OverTheBillingAssembly_RegistersEveryCommandAndQueryWithExactlyOneHandler()
    {
        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddDispatcher(typeof(BillingHost).Assembly));

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
        var removed = negativeBuilder.Services.Single(d => d.ServiceType == typeof(IBuyerCreditRepository));
        negativeBuilder.Services.Remove(removed);

        var negativeException = Record.Exception(() => negativeBuilder.Build());
        Assert.NotNull(negativeException);
        Assert.Contains(nameof(IBuyerCreditRepository), negativeException!.ToString());
    }

    /// <summary>
    /// `E7`/ledger `L20` — everything that must share the ambient
    /// transaction must share the SCOPE: <see cref="IInvoiceRepository"/>,
    /// <see cref="IInvoiceReadPort"/>, <see cref="IInvoiceNumberAllocator"/>
    /// and <see cref="OrderToCash.Billing.Application.InvoiceIssueService"/>
    /// each resolve and each have <see cref="ServiceLifetime.Scoped"/>.
    /// </summary>
    [Fact]
    public void InvoicingPorts_EachResolve_AndAreEachRegisteredScoped()
    {
        var builder = BuildRealHostBuilder();
        var scopedTypes = new[]
        {
            typeof(IInvoiceRepository),
            typeof(IInvoiceReadPort),
            typeof(IInvoiceNumberAllocator),
            typeof(OrderToCash.Billing.Application.InvoiceIssueService),
        };

        foreach (var serviceType in scopedTypes)
        {
            var descriptor = builder.Services.Single(d => d.ServiceType == serviceType);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        }

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();

        foreach (var serviceType in scopedTypes)
        {
            var resolved = scope.ServiceProvider.GetService(serviceType);
            Assert.NotNull(resolved);
        }
    }

    private static HostApplicationBuilder BuildRealHostBuilder() =>
        BillingHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                // No real MS-SQL/NATS/Kafka needed — ValidateOnBuild checks
                // the DI GRAPH, it does not connect (Orders' own D3 probe,
                // inherited).
                options.ConnectionString = "Server=localhost;Database=otc_billing_validate_on_build_probe;Trusted_Connection=True;";
                options.Nats.Url = "nats://127.0.0.1:1";
                options.Kafka.BootstrapServers = "127.0.0.1:1";
            });
}
