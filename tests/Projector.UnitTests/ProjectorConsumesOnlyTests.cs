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

    /// <summary>
    /// REWORDED — observability_reliability's group A4 (phase 14) gave
    /// Projector a deliberate, narrow HTTP surface: <c>HealthProbeService</c>
    /// (design.md §8.1) maps ONLY <c>GET /health/live</c>/<c>GET /health/ready</c>
    /// on <c>PROJECTOR_HEALTH_PORT</c>, via a bare
    /// <c>&lt;FrameworkReference Include="Microsoft.AspNetCore.App" /&gt;</c>
    /// — no NuGet package, no web-app SDK switch. The ORIGINAL claim ("no
    /// HTTP surface at all") is no longer true and this test now proves the
    /// NARROWER one that still matters: the only
    /// <c>Microsoft.AspNetCore</c>-mentioning line in the whole csproj is
    /// exactly that one FrameworkReference — never a
    /// <c>PackageReference</c>, never <c>Sdk="Microsoft.NET.Sdk.Web"</c>,
    /// never a second, wider web surface creeping in unnoticed.
    /// </summary>
    [Fact]
    public void PR21_DeclaresNoWriteModelPackage_NoProcessedEventsConfiguration_AndIssuesNoNatsRequest_TheOnlyAspNetCoreMentionIsTheHealthProbeFrameworkReference()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var csprojText = File.ReadAllText(Path.Combine(root, "src", "Projector", "Projector.csproj"));

        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", csprojText, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Data.SqlClient", csprojText, StringComparison.Ordinal);
        Assert.DoesNotContain("Sdk=\"Microsoft.NET.Sdk.Web\"", csprojText, StringComparison.Ordinal);

        var aspNetCoreLines = csprojText
            .Split('\n')
            .Where(line => line.Contains("Microsoft.AspNetCore", StringComparison.Ordinal))
            .ToList();

        var aspNetCoreLine = Assert.Single(aspNetCoreLines);
        Assert.Contains("<FrameworkReference", aspNetCoreLine, StringComparison.Ordinal);
        Assert.Contains("Microsoft.AspNetCore.App", aspNetCoreLine, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference", aspNetCoreLine, StringComparison.Ordinal);

        Assert.False(File.Exists(Path.Combine(root, "src", "Projector", "Infrastructure", "Persistence", "Configurations", "ProcessedEventConfiguration.cs")));
    }
}
