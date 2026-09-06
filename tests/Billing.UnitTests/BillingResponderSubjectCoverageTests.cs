using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderToCash.Billing;
using OrderToCash.Billing.Infrastructure.Outbox;
using OrderToCash.Billing.Presentation;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BI31` — all six `billing.*` RPC subjects (feature 22 adds
/// `billing.payment.register`, to the SAME class, per its own design
/// choice) are answered from ONE <see cref="BackgroundService"/>, so the
/// service's concurrency bound, its one-scope-per-request rule and its
/// individually-awaited drain exist in exactly one implementation. A
/// second responder class would fork those four separately-armed
/// behaviours into an unguarded copy (design.md §4.1, gate-approved
/// 2026-09-06).
/// </summary>
public sealed class BillingResponderSubjectCoverageTests
{
    [Fact]
    public void BI31_OneBackgroundServiceSubscribesAllSixBillingSubjects_AndTheHostRegistersNoSecondRpcResponder()
    {
        var builder = BillingHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.ConnectionString = "Server=localhost;Database=otc_billing_responder_coverage_probe;Trusted_Connection=True;";
                options.Nats.Url = "nats://127.0.0.1:1";
                options.Kafka.BootstrapServers = "127.0.0.1:1";
            });

        using var host = builder.Build();
        var hostedServiceTypes = host.Services.GetServices<IHostedService>().Select(s => s.GetType()).ToHashSet();

        // Exactly one RPC responder — not "at least one", so a second,
        // narrower responder cannot slip in unnoticed.
        Assert.Equal(new HashSet<Type> { typeof(OutboxRelayBackgroundService), typeof(BillingRpcResponder) }, hostedServiceTypes);

        var responderSource = File.ReadAllText(RepositoryPaths.Find(Path.Combine("src", "Billing", "Presentation", "BillingRpcResponder.cs")));
        var executeAsyncBody = ExtractExecuteAsyncBody(responderSource);

        foreach (var subject in new[]
        {
            "CreditSubjects.CreditHold",
            "CreditSubjects.CreditRelease",
            "CreditSubjects.CreditList",
            "InvoiceSubjects.InvoiceIssue",
            "InvoiceSubjects.InvoiceList",
            "InvoiceSubjects.PaymentRegister",
        })
        {
            Assert.Contains(subject, executeAsyncBody, StringComparison.Ordinal);
        }
    }

    private static string ExtractExecuteAsyncBody(string source)
    {
        const string marker = "protected override async Task ExecuteAsync(CancellationToken stoppingToken)";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "could not locate ExecuteAsync in BillingRpcResponder.cs");

        var end = source.IndexOf("public override async Task StopAsync", start, StringComparison.Ordinal);
        Assert.True(end > start, "could not locate the end of ExecuteAsync in BillingRpcResponder.cs");

        return source[start..end];
    }
}
