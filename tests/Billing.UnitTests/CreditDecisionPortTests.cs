using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>`BC14` type half — <see cref="AdapterRejectionReason"/> types the port so <c>over_limit</c> is not a reason an adapter can return (design.md §6.1, §15 ledger `L24`).</summary>
public sealed class CreditDecisionPortTests
{
    [Fact]
    public void BC14_TypesThePortSoThatOverLimitIsNotAReasonAnAdapterCanReturn()
    {
        var members = Enum.GetValues<AdapterRejectionReason>();

        Assert.Equal(2, members.Length);
        Assert.Contains(AdapterRejectionReason.SimulatedCentsRule, members);
        Assert.Contains(AdapterRejectionReason.SimulatedFailureRate, members);
        Assert.DoesNotContain(members, m => string.Equals(m.ToString(), "OverLimit", StringComparison.OrdinalIgnoreCase));

        // The mapping is total: every AdapterRejectionReason member maps to
        // a CreditRejectionReason, and none of them is OverLimit.
        foreach (var member in members)
        {
            var mapped = AdapterRejectionReasons.ToDomainReason(member);
            Assert.NotEqual(CreditRejectionReason.OverLimit, mapped);
        }
    }
}
