using System.Reflection;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// R60/OR6, design.md §8.3, ledger L26 — "a stalled-but-open socket is the
/// case that matters, and it is not the same as a closed one." #7 shipped
/// its Gateway NATS probe without a timeout wrapper — found and disclosed
/// by #7's own IMPLEMENTER
/// (<c>order-to-cash-nestjs/progress/impl_observability_reliability.md:912</c>,
/// the mechanism at <c>:927</c>), and only CONFIRMED by #7's reviewer
/// (<c>order-to-cash-nestjs/progress/review_observability_reliability.md:70-72</c>)
/// (review round 1, R2). Every one of this repository's <c>IHealthCheck</c> implementations
/// must name an explicit timeout, and the ENUMERATION that finds them is a
/// search result, never a hand-typed list (CLAUDE.md's own "a negative
/// claim about the repository is a search result, not a reading").
///
/// The predicate matches by the interface's FULLY-QUALIFIED name
/// (<c>OrderToCash.&lt;Service&gt;.Application.Ports.IHealthCheck</c>),
/// never the bare <c>IHealthCheck</c> name — every service's
/// <c>&lt;FrameworkReference Include="Microsoft.AspNetCore.App" /&gt;</c>
/// (design.md §8.1) makes
/// <c>Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck</c>
/// resolvable in the SAME compiled assemblies, and the two share a bare
/// name — a scan on the bare name would be exactly the self-selecting
/// sweep CLAUDE.md's ledger-enumeration rule warns about.
/// </summary>
public sealed class HealthProbeTimeoutTests
{
    /// <summary>Every service assembly this feature adds a health surface to — the same six <c>DomainAssemblies</c>-adjacent projects, but the WHOLE assembly (Infrastructure/Health included), not just the Domain/ slice.</summary>
    private static readonly Assembly[] _serviceAssemblies =
    [
        typeof(OrderToCash.Gateway.GatewayHost).Assembly,
        typeof(OrderToCash.Orders.OrdersHost).Assembly,
        typeof(OrderToCash.Fulfillment.FulfillmentHost).Assembly,
        typeof(OrderToCash.Billing.BillingHost).Assembly,
        typeof(OrderToCash.Notifications.NotificationsHost).Assembly,
        typeof(OrderToCash.Projector.ProjectorHost).Assembly,
    ];

    /// <summary>Matches ONLY a per-service <c>IHealthCheck</c> port — never the ASP.NET Core shared framework's own <c>Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck</c>, which the FrameworkReference above makes resolvable too.</summary>
    private static bool IsServiceHealthCheckInterface(Type type) =>
        type.IsInterface && type.FullName is not null && type.FullName.EndsWith(".Application.Ports.IHealthCheck", StringComparison.Ordinal);

    /// <summary>
    /// design.md §8.2's fourteen checks (Gateway 2 + Orders 3 + Fulfillment
    /// 2 + Billing 2 + Notifications 2 + Projector 3), enumerated as a
    /// LITERAL expected set — the rest is derived by subtraction, never the
    /// other way round (CLAUDE.md: "make the expected set a literal and
    /// derive the rest by subtraction").
    /// </summary>
    private static readonly string[] _expectedImplementations =
    [
        "OrderToCash.Gateway.Infrastructure.Health.NatsHealthCheck",
        "OrderToCash.Gateway.Infrastructure.Health.MongoHealthCheck",
        "OrderToCash.Orders.Infrastructure.Health.MsSqlHealthCheck",
        "OrderToCash.Orders.Infrastructure.Health.KafkaHealthCheck",
        "OrderToCash.Orders.Infrastructure.Health.NatsHealthCheck",
        "OrderToCash.Fulfillment.Infrastructure.Health.MsSqlHealthCheck",
        "OrderToCash.Fulfillment.Infrastructure.Health.NatsHealthCheck",
        "OrderToCash.Billing.Infrastructure.Health.MsSqlHealthCheck",
        "OrderToCash.Billing.Infrastructure.Health.NatsHealthCheck",
        "OrderToCash.Notifications.Infrastructure.Health.MsSqlHealthCheck",
        "OrderToCash.Notifications.Infrastructure.Health.KafkaHealthCheck",
        "OrderToCash.Projector.Infrastructure.Health.KafkaHealthCheck",
        "OrderToCash.Projector.Infrastructure.Health.NatsHealthCheck",
        "OrderToCash.Projector.Infrastructure.Health.MongoHealthCheck",
    ];

    private static List<Type> DiscoverImplementations() =>
        _serviceAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetInterfaces().Any(IsServiceHealthCheckInterface))
            .ToList();

    [Fact]
    public void OR6_EveryReadinessProbeInEveryServiceIsBoundedByAnExplicitTimeout()
    {
        var implementations = DiscoverImplementations();

        var actualNames = implementations.Select(t => t.FullName!).ToHashSet(StringComparer.Ordinal);
        var expectedNames = _expectedImplementations.ToHashSet(StringComparer.Ordinal);

        var missing = expectedNames.Except(actualNames).ToList();
        var unexpected = actualNames.Except(expectedNames).ToList();

        Assert.True(
            missing.Count == 0 && unexpected.Count == 0,
            $"design.md §8.2's fourteen IHealthCheck implementations drifted.{Environment.NewLine}" +
            $"Declared in the literal but not found: {string.Join(", ", missing)}{Environment.NewLine}" +
            $"Found but not in the literal: {string.Join(", ", unexpected)}");

        var untimed = implementations
            .Where(type => !type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Any(field => field.FieldType == typeof(TimeSpan)))
            .Select(type => type.FullName)
            .ToList();

        Assert.True(
            untimed.Count == 0,
            $"Every IHealthCheck implementation must name an explicit TimeSpan timeout (design.md §8.3, ledger L26). " +
            $"Missing one: {string.Join(", ", untimed)}");
    }
}
