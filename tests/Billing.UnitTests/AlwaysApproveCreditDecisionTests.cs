using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Infrastructure.CreditDecisions;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>`BC15` — the adapter bound until feature 20 approves every request it is asked about.</summary>
public sealed class AlwaysApproveCreditDecisionTests
{
    [Fact]
    public async Task BC15_ApprovesEveryRequest_AndIsTheOnlyRegistrationFeature20Replaces()
    {
        var port = new AlwaysApproveCreditDecision();
        var request = new CreditDecisionRequest("ORD-000001", "CarrefourEs", "IBERFOODS", "CR-000001", 999_999_999, "EUR", 0);

        var decision = await port.DecideAsync(request, CancellationToken.None);

        Assert.IsType<CreditDecision.Approve>(decision);
    }
}
