using OrderToCash.Projector;
using OrderToCash.Projector.IntegrationTests.TestSupport;
using Xunit;

namespace OrderToCash.Projector.IntegrationTests;

/// <summary>
/// <c>PR45</c> (gate row 1, approved 2026-09-07) — an unreachable
/// <c>NATS_URL</c> fails the BOOT, rather than running with every update
/// signal silently swallowed by <c>PR19</c>.
/// </summary>
[Collection(ProjectorInfraCollection.Name)]
public sealed class ProjectorBootTests(MongoContainerFixture mongoFixture, KafkaContainerFixture kafkaFixture)
{
    [Fact]
    public async Task PR45_AnUnreachableNatsUrl_FailsTheHostStart_RatherThanRunningWithEverySignalSwallowed()
    {
        var builder = ProjectorHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.Kafka.BootstrapServers = kafkaFixture.BootstrapServers;
                options.Nats.Url = "nats://127.0.0.1:1"; // a closed port — nothing listens.
                options.Mongo.ConnectionUri = mongoFixture.ConnectionString;
                options.Mongo.Database = "otc_rm_boot_nats_unreachable";
            });

        var host = builder.Build();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync(cts.Token));

        host.Dispose();
    }
}
