using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Messaging;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// The boot-loudness rule CLAUDE.md keeps, one feature before there is a
/// boot (design.md §2.3): every port <c>AddOrdersOutbox</c> registers must
/// resolve from the container it builds, and none may be registered twice.
/// </summary>
public sealed class OrdersOutboxRegistrationTests
{
    private static readonly Type[] _expectedSingleRegistrations =
    [
        typeof(IClock),
        typeof(IUnitOfWork),
        typeof(OutboxWriter),
        typeof(IOrderRepository),
        typeof(ProcessedEventLedger),
        typeof(IdempotentConsumer),
        typeof(IFactPublisher),
        typeof(OutboxRelay),
        typeof(IOutboxRelay),
        typeof(DbContext),
        typeof(OrdersDbContext),
    ];

    [Fact]
    public void EveryPortTheOutboxNeedsResolvesFromAContainerBuiltByAddOrdersOutbox()
    {
        var services = new ServiceCollection();
        // A host's own AddLogging() (or the ASP.NET Core default) — not
        // AddOrdersOutbox's job (its own comment says so); provided here
        // the way feature 15's Program.cs is expected to provide it.
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddOrdersOutbox(options =>
        {
            options.ConnectionString = "Server=unused;Database=unused;TrustServerCertificate=True;";
            options.Kafka.BootstrapServers = "localhost:9092";
            options.Kafka.ClientId = "otc-orders-test";
        });

        foreach (var serviceType in _expectedSingleRegistrations)
        {
            var registrationCount = services.Count(d => d.ServiceType == serviceType);
            Assert.True(registrationCount == 1, $"{serviceType.FullName} is registered {registrationCount} time(s), expected exactly 1.");
        }

        Assert.Contains(services, d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) && d.ImplementationType == typeof(OutboxRelayBackgroundService));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        foreach (var serviceType in _expectedSingleRegistrations)
        {
            var resolved = scope.ServiceProvider.GetService(serviceType);
            Assert.True(resolved is not null, $"{serviceType.FullName} did not resolve from the container AddOrdersOutbox built.");
        }

        var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToList();
        Assert.Contains(hostedServices, s => s is OutboxRelayBackgroundService);
    }
}
