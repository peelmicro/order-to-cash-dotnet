using OrderToCash.Billing.Application.Ports;

namespace OrderToCash.Billing.Infrastructure.CreditDecisions;

/// <summary>
/// The credit-decision port bound today (design.md §6.3): approves every
/// request it is asked about. Pure, dependency-free, no I/O — feature 20's
/// <c>SimulatorCreditDecision</c> is the ONLY thing that replaces its DI
/// registration; no <c>Domain/</c>, <c>Application/</c> or
/// <c>Presentation/</c> file changes when it lands.
/// </summary>
/// <remarks>
/// Namespace/folder deliberately <c>CreditDecisions</c>, not the design's
/// literal <c>Infrastructure/Credit/</c>: a sibling namespace literally
/// named <c>Credit</c> under <c>OrderToCash.Billing.Infrastructure</c> is
/// reachable from <c>Infrastructure.Persistence</c>'s enclosing-namespace
/// search and collides with the phase-6 <c>Entities.Credit</c> class
/// (<c>CS0118</c>, "'Credit' is a namespace but is used like a type") —
/// exactly the schema this feature's task list forbids touching. Recorded
/// as a disclosed, necessary deviation from the design's literal path.
/// </remarks>
public sealed class AlwaysApproveCreditDecision : ICreditDecisionPort
{
    public ValueTask<CreditDecision> DecideAsync(CreditDecisionRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<CreditDecision>(new CreditDecision.Approve());
}
