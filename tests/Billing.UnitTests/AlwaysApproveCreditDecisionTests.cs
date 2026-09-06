using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Infrastructure.CreditDecisions;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>`BC15` — the adapter bound before feature 20 landed; retained as the port's reference implementation, approving every request it is asked about.</summary>
public sealed class AlwaysApproveCreditDecisionTests
{
    [Fact]
    public async Task BC15_ApprovesEveryRequest_AndWasTheOnlyRegistrationFeature20Replaced()
    {
        var port = new AlwaysApproveCreditDecision();
        var request = new CreditDecisionRequest("ORD-000001", "CarrefourEs", "IBERFOODS", "CR-000001", 999_999_999, "EUR", 0);

        var decision = await port.DecideAsync(request, CancellationToken.None);

        Assert.IsType<CreditDecision.Approve>(decision);
    }
}
