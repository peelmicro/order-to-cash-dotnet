using Microsoft.Extensions.DependencyInjection;
using OrderToCash.Cqrs;
using OrderToCash.Projector;
using OrderToCash.Projector.Application.Commands;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary><c>PR27</c> — the REAL <see cref="ProjectorHost"/> container resolves exactly one handler for <see cref="ProjectFactCommand"/>. Arm by registering a second handler and confirming the boot fails.</summary>
public sealed class ProjectorDispatcherRegistrationTests
{
    [Fact]
    public void PR27_TheRealHostResolvesExactlyOneHandlerForProjectFactCommand()
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

        using var host = builder.Build();

        using var scope = host.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ProjectFactCommand>>();

        Assert.IsType<ProjectFactCommandHandler>(handler);
    }
}
