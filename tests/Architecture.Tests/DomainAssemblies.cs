using System.Reflection;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// The set of assemblies domain-purity and no-decimal architecture tests
/// scan: every service's Domain/ layer folder, <b>union</b>
/// <see cref="OrderToCash.SharedKernel"/> in full.
///
/// SharedKernel is included whole, not filtered by
/// <see cref="DomainNamespacePattern"/> the way a service assembly is,
/// because it has no layers to filter by design — it is
/// "copied per repository ... small, dependency-free shared kernel"
/// (specs/shared/domain-model.md §2) consisting entirely of value objects,
/// <c>Entity</c>/<c>AggregateRoot</c> and <c>DomainError</c>: the purest
/// domain code in the repository, and specifically the type
/// (<see cref="OrderToCash.SharedKernel.Money"/>) CLAUDE.md's no-decimal
/// rule exists for ("Money is long minor units ... decimal appears only at
/// presentation boundaries"). Filtering it by a ".Domain" namespace segment
/// would exclude 100% of it, since it correctly has no such segment — see
/// progress/impl_shared_kernel.md, defect raised by the leader after D2/D3:
/// two rule families were vacuous over SharedKernel because (a) its
/// namespace never matched <see cref="DomainNamespacePattern"/> and
/// (b) it was never in this list at all. Do not "fix" this by adding a
/// <c>.Domain</c> segment to SharedKernel's namespaces — that would impose a
/// layer marker on a project that deliberately has none, purely to satisfy
/// a regex.
/// </summary>
internal static class DomainAssemblies
{
    public static readonly Assembly[] All =
    [
        typeof(OrderToCash.Gateway.Domain.GatewayDomainPlaceholder).Assembly,
        typeof(OrderToCash.Orders.Domain.OrdersDomainPlaceholder).Assembly,
        typeof(OrderToCash.Fulfillment.Domain.FulfillmentDomainPlaceholder).Assembly,
        typeof(OrderToCash.Billing.Domain.BillingDomainPlaceholder).Assembly,
        typeof(OrderToCash.Notifications.Domain.NotificationsDomainPlaceholder).Assembly,
        typeof(OrderToCash.Projector.Domain.ProjectorDomainPlaceholder).Assembly,
        typeof(OrderToCash.Seed.Domain.SeedDomainPlaceholder).Assembly,
        // Whole-assembly-domain — see the class summary above. Every type in
        // this assembly is in scope, regardless of namespace, which is why
        // DomainNamespacePattern below also matches the SharedKernel root
        // namespace rather than requiring a ".Domain" segment on it.
        typeof(OrderToCash.SharedKernel.Money).Assembly,
    ];

    /// <summary>
    /// Two alternatives, both feeding the same rules so "the domain layer"
    /// stays one definition:
    /// (1) "Domain" as a namespace *segment* — matches OrderToCash.Orders.Domain,
    /// OrderToCash.Orders.Domain.ValueObjects, OrderToCash.Orders.Domain.Events,
    /// etc. Deliberately NOT a suffix-only match (that would miss every
    /// sub-namespace of Domain/, which is exactly where CLAUDE.md says
    /// aggregates, value objects, domain events and domain errors live) and
    /// deliberately NOT a substring match (that would also match a
    /// hypothetical ".DomainServices").
    /// (2) The SharedKernel root namespace and everything under it
    /// (OrderToCash.SharedKernel, OrderToCash.SharedKernel.Errors, ...) —
    /// SharedKernel has no ".Domain" segment by design (see the class
    /// summary), so it needs its own alternative rather than being folded
    /// into (1). Shared by every rule — in DomainPurityTests.cs via
    /// NetArchTest's ResideInNamespaceMatching, and in DomainDecimalTests.cs
    /// via its own GeneratedRegex.
    /// </summary>
    public const string DomainNamespacePattern = @"(^|\.)Domain(\.|$)|^OrderToCash\.SharedKernel(\.|$)";
}
