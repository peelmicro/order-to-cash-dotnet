using OrderToCash.Billing.Application.Ports;

namespace OrderToCash.Billing.Infrastructure.CreditDecisions;

/// <summary>
/// The credit-decision port's REFERENCE implementation — approves every
/// request it is asked about, pure, dependency-free, no I/O — and the
/// provider a future fixture may bind. It is NOT the production binding:
/// feature 20 replaced its DI registration with
/// <c>SimulatorCreditDecision</c> (`BillingServiceCollectionExtensions.cs`
/// records this correctly). Feature 21 (billing_invoicing) considered
/// binding this class in the integration fixture instead of building the
/// `.99` cents-rule guard, and declined — `design.md` §10.1 records why
/// (`BillingHostFixture` deliberately builds the real host with no
/// overrides, which is what makes an integration test evidence about what
/// `Program.cs` boots).
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
