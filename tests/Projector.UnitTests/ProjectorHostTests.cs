using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderToCash.Projector;
using OrderToCash.Projector.Infrastructure.Persistence;
using OrderToCash.Projector.Presentation;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary>
/// <c>PR43</c> — the registration ORDER of the two hosted services in the
/// real <see cref="IServiceCollection"/>, and <c>ServicesStartConcurrently</c>
/// staying <see langword="false"/>. Arm by swapping the two registrations
/// and by setting the flag <see langword="true"/>, and confirm this test
/// fails BOTH times.
/// </summary>
public sealed class ProjectorHostTests
{
    [Fact]
    public void PR43_ReadModelBootstrapIsRegisteredBeforeTheConsumer_AndServicesStartSequentially()
    {
        var builder = ProjectorHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.Kafka.BootstrapServers = "localhost:9092";
                options.Nats.Url = "nats://localhost:4222";
                options.Mongo.ConnectionUri = "mongodb://user:pass@localhost:27017/?authSource=admin";
                options.Mongo.Database = "otc_read_model_test";
            });

        var hostedServiceDescriptors = builder.Services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .ToList();

        var bootstrapIndex = hostedServiceDescriptors.IndexOf(typeof(ReadModelBootstrap));
        var consumerIndex = hostedServiceDescriptors.IndexOf(typeof(ProjectorFactsConsumer));

        Assert.True(bootstrapIndex >= 0, "ReadModelBootstrap was not registered as an IHostedService.");
        Assert.True(consumerIndex >= 0, "ProjectorFactsConsumer was not registered as an IHostedService.");
        Assert.True(bootstrapIndex < consumerIndex, "ReadModelBootstrap must be registered BEFORE ProjectorFactsConsumer.");

        // HostOptions.ServicesStartConcurrently defaults to false and is
        // never overridden by this feature — asserted against the real
        // built host's options, not merely "not set".
        using var host = builder.Build();
        var hostOptions = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<HostOptions>>().Value;
        Assert.False(hostOptions.ServicesStartConcurrently);
    }

    [Fact]
    public void ReadModelBootstrapIsAnIHostedServiceDirectly_NotABackgroundService()
    {
        // A BackgroundService's StartAsync returns at the first await inside
        // ExecuteAsync — the appearance of ordering with none of the
        // substance. ReadModelBootstrap must implement IHostedService
        // directly.
        Assert.True(typeof(IHostedService).IsAssignableFrom(typeof(ReadModelBootstrap)));
        Assert.False(typeof(Microsoft.Extensions.Hosting.BackgroundService).IsAssignableFrom(typeof(ReadModelBootstrap)));
    }
}
