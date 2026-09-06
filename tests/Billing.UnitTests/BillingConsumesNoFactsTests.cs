using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderToCash.Billing;
using OrderToCash.Billing.Infrastructure.Outbox;
using OrderToCash.Billing.Presentation;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BI1` (design.md §9) — Billing issues an invoice ONLY in response to a
/// `billing.invoice.issue` NATS RPC request; it registers NO Kafka consumer,
/// NO fact-stream <see cref="BackgroundService"/> and NO idempotent-consumer
/// runner of any kind. Built from the REAL host (`BillingHost.CreateBuilder`)
/// rather than a text scan for a decorator — #7's own review recorded that
/// limitation as its `N8`.
/// </summary>
public sealed class BillingConsumesNoFactsTests
{
    [Fact]
    public void BI1_TheBuiltHostRegistersExactlyTheOutboxRelayAndTheRpcResponder_AndNoKafkaConsumerTypeIsReferencedAnywhereUnderSrcBilling()
    {
        var builder = BillingHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.ConnectionString = "Server=localhost;Database=otc_billing_consumes_no_facts_probe;Trusted_Connection=True;";
                options.Nats.Url = "nats://127.0.0.1:1";
                options.Kafka.BootstrapServers = "127.0.0.1:1";
            });

        using var host = builder.Build();

        var hostedServiceTypes = host.Services.GetServices<IHostedService>().Select(s => s.GetType()).ToHashSet();

        Assert.Equal(new HashSet<Type> { typeof(OutboxRelayBackgroundService), typeof(BillingRpcResponder) }, hostedServiceTypes);

        var (hits, scannedFiles) = ScanForKafkaConsumerTypes();
        Assert.Empty(hits);

        // Non-vacuity: the scan must be proved capable of finding a hit —
        // never merely asserted empty and believed (CLAUDE.md: a negative
        // claim about the repository is a search result, not a reading).
        Assert.True(scannedFiles > 0, "the scan found no .cs files under src/Billing at all — it cannot be trusted to have looked.");
    }

    /// <summary>ARM: register a trivial <see cref="BackgroundService"/> in <c>AddBilling</c> — done by hand, temporarily, and this test must then fail on the hosted-service-set assertion.</summary>
    private static (List<string> Hits, int ScannedFiles) ScanForKafkaConsumerTypes()
    {
        var billingRoot = Path.Combine(RepositoryPaths.Find("src"), "Billing");
        var files = Directory.GetFiles(billingRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        var hits = new List<string>();
        var forbidden = new[] { "IConsumer<", "ConsumerConfig", "ConsumerBuilder" };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var needle in forbidden)
            {
                if (text.Contains(needle, StringComparison.Ordinal))
                {
                    hits.Add($"{file}: contains '{needle}'");
                }
            }
        }

        return (hits, files.Count);
    }

    /// <summary>Proves the scan itself is capable of finding a hit — a fixture string containing the forbidden markers, written to a scratch file inside `src/Billing` momentarily, never left behind.</summary>
    [Fact]
    public void TheSourceScanIsProvenNonVacuous_AgainstAFixtureFileContainingTheForbiddenMarkers()
    {
        var billingRoot = Path.Combine(RepositoryPaths.Find("src"), "Billing");
        var scratchFile = Path.Combine(billingRoot, $"__scratch_kafka_consumer_probe_{Guid.NewGuid():N}.cs");

        try
        {
            File.WriteAllText(scratchFile, "// probe: IConsumer<string, byte[]> x; ConsumerConfig y; ConsumerBuilder<string, byte[]> z;");

            var (hits, _) = ScanForKafkaConsumerTypes();

            Assert.Equal(3, hits.Count(h => h.Contains(Path.GetFileName(scratchFile), StringComparison.Ordinal)));
        }
        finally
        {
            File.Delete(scratchFile);
        }
    }
}
