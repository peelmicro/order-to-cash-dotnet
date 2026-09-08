using OrderToCash.Projector.Infrastructure.Messaging.Consumers;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary>
/// <c>PR1</c>/<c>PR21</c> — structural guards over <c>src/Projector/</c>
/// source text: exactly one Kafka <c>BackgroundService</c>, exactly three
/// topics, and zero NATS subscription of any kind. Arm by adding a fourth
/// topic and by adding a NATS subscription in a scratch file.
/// </summary>
public sealed class ProjectorConsumesOnlyTests
{
    [Fact]
    public void PR1_RegistersExactlyOneKafkaBackgroundServiceOverTheThreeFactTopics_AndNoNatsSubscriptionOfAnyKind()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var projectorDir = Path.Combine(root, "src", "Projector");

        var backgroundServiceFiles = Directory.EnumerateFiles(projectorDir, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains(": BackgroundService", StringComparison.Ordinal))
            .ToList();

        Assert.Single(backgroundServiceFiles);
        Assert.Contains("ProjectorFactsConsumer.cs", backgroundServiceFiles[0]);

        Assert.Equal(3, ProjectorFactTopics.All.Count);
        Assert.Equal(
            ["otc.orders.facts.v1", "otc.fulfillment.facts.v1", "otc.billing.facts.v1"],
            ProjectorFactTopics.All);

        var allSourceText = Directory.EnumerateFiles(projectorDir, "*.cs", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines)
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && !line.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .ToList();

        Assert.DoesNotContain(allSourceText, line => line.Contains("SubscribeAsync", StringComparison.Ordinal));
        Assert.DoesNotContain(allSourceText, line => line.Contains("SubscribeCoreAsync", StringComparison.Ordinal));
        Assert.DoesNotContain(allSourceText, line => line.Contains("RequestAsync", StringComparison.Ordinal));
        Assert.DoesNotContain(allSourceText, line => line.Contains("replyTo:", StringComparison.Ordinal));
    }

    [Fact]
    public void PR21_DeclaresNoWriteModelPackage_NoProcessedEventsConfiguration_NoHttpSurface_AndIssuesNoNatsRequest()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var csprojText = File.ReadAllText(Path.Combine(root, "src", "Projector", "Projector.csproj"));

        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", csprojText, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Data.SqlClient", csprojText, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.AspNetCore", csprojText, StringComparison.Ordinal);

        Assert.False(File.Exists(Path.Combine(root, "src", "Projector", "Infrastructure", "Persistence", "Configurations", "ProcessedEventConfiguration.cs")));
    }
}
