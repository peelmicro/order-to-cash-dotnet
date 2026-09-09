using Microsoft.Extensions.DependencyInjection;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Queries;
using OrderToCash.Orders.Infrastructure;
using OrderToCash.Orders.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// The ported-idiom ledger's own row (<c>progress/impl_orders_catalog_responder.md</c>
/// — corrected after review round 1's D2): "#7 relied on X; in #8 that
/// property is supplied by Y." #7 satisfied "reuses the same reference-data
/// lookup <c>PlaceOrderHandler</c> already calls" via an EXPLICIT
/// <c>useExisting</c> alias between two distinct DI tokens
/// (<c>apps/orders/src/app.module.ts:123-137</c>) — a deliberate
/// configuration decision carrying its own documented risk, NOT something
/// NestJS's DI gave for free (its own comment there explains why it is an
/// alias, not a second factory). #7 also kept TWO separate ports on one
/// adapter; #8 instead widened ONE port, so there is no second token to
/// alias at all — in .NET that reuse is a pure REGISTRATION DECISION —
/// <c>OrdersAcceptanceServiceCollectionExtensions.AddOrdersAcceptance</c>'s
/// ONE <c>AddScoped&lt;IOrderReferenceCatalog, EfCoreOrderReferenceCatalog&gt;</c>
/// line — and nothing checks it unless a test does. This file is that test:
/// it fails the moment a second, parallel <c>IOrderReferenceCatalog</c>
/// registration (or a query handler that bypasses the port entirely) is
/// introduced.
/// </summary>
public sealed class CatalogReferenceListPortReuseTests
{
    /// <summary>
    /// The registration-count half of the guard. Building the full
    /// <see cref="ServiceProvider"/> is deliberately NOT needed —
    /// <c>AddOrdersAcceptance</c>'s <c>INatsConnection</c> registration is a
    /// lazy factory (never dials on registration), so inspecting the
    /// <see cref="IServiceCollection"/>'s own descriptors is enough, and
    /// keeps this a fast, no-transport unit test.
    /// </summary>
    [Fact]
    public void AddOrdersAcceptance_RegistersExactlyOneIOrderReferenceCatalogImplementation()
    {
        var services = new ServiceCollection();
        services.AddOrdersAcceptance(options => options.Nats.Url = "nats://localhost:4222");

        var registrations = services.Where(d => d.ServiceType == typeof(IOrderReferenceCatalog)).ToList();
        var registration = Assert.Single(registrations);

        Assert.Equal(typeof(EfCoreOrderReferenceCatalog), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }

    /// <summary>
    /// The dependency-shape half of the guard: both the place-order path
    /// and the catalog-listing path declare a constructor parameter typed
    /// <see cref="IOrderReferenceCatalog"/> — never a direct
    /// <c>OrdersDbContext</c> dependency in
    /// <see cref="ListCatalogReferenceQueryHandler"/>, which would be the
    /// "second query path that could drift" this feature's brief calls out.
    /// Review advisory A1: asserts the EXACT parameter-type list, not merely
    /// that <see cref="IOrderReferenceCatalog"/> is somewhere among the
    /// parameters — <c>Assert.Contains</c> could not fail if a SECOND
    /// data-access dependency (e.g. <c>OrdersDbContext</c> itself) were added
    /// beside the port, which is precisely the bypass this guard exists to
    /// catch.
    /// </summary>
    [Fact]
    public void PlaceOrderCommandHandlerAndListCatalogReferenceQueryHandler_BothDeclareAConstructorDependencyOnIOrderReferenceCatalog()
    {
        AssertConstructorParameterTypesEqual<PlaceOrderCommandHandler>(
            typeof(IUnitOfWork),
            typeof(IOrderRepository),
            typeof(IOrderNumberAllocator),
            typeof(IOrderReferenceCatalog),
            typeof(IStockAvailabilityChecker),
            typeof(IClock));

        AssertConstructorParameterTypesEqual<ListCatalogReferenceQueryHandler>(
            typeof(IOrderReferenceCatalog));
    }

    private static void AssertConstructorParameterTypesEqual<T>(params Type[] expectedParameterTypes)
    {
        var constructor = Assert.Single(typeof(T).GetConstructors());
        Assert.Equal(expectedParameterTypes, constructor.GetParameters().Select(p => p.ParameterType));
    }
}
