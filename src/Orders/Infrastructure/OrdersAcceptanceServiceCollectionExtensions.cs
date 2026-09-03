using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Presentation;

namespace OrderToCash.Orders.Infrastructure;

/// <summary>
/// <c>AddOrdersAcceptance(IServiceCollection, Action&lt;OrdersAcceptanceOptions&gt;)</c>
/// — one explicit registration line per port (CLAUDE.md: "every port is
/// registered explicitly"), the feature <c>orders_acceptance</c> half of
/// the Orders host: the RPC transport, the two new read ports
/// (<see cref="IOrderNumberAllocator"/>, <see cref="IOrderReferenceCatalog"/>),
/// and the <c>orders.create</c> responder. Deliberately separate from
/// <c>OrdersOutboxServiceCollectionExtensions.AddOrdersOutbox</c> — that
/// extension is feature <c>outbox_and_idempotency</c>'s own, unmodified by
/// this feature; <c>Program.cs</c> calls both.
/// </summary>
public static class OrdersAcceptanceServiceCollectionExtensions
{
    public static IServiceCollection AddOrdersAcceptance(this IServiceCollection services, Action<OrdersAcceptanceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OrdersAcceptanceOptions();
        configure(options);

        services.AddSingleton<IOptions<NatsOptions>>(Options.Create(options.Nats));

        // ONE INatsConnection per process — a NATS core connection is
        // multiplexed and safe to share across every publish/subscribe/
        // request call this service makes (the responder's inbound
        // subscription AND the stock-check client's outbound requests).
        // IAsyncDisposable — the container disposes it on shutdown.
        services.AddSingleton<INatsConnection>(_ => new NatsConnection(new NatsOpts { Url = options.Nats.Url }));

        services.AddScoped<IOrderNumberAllocator, EfCoreOrderNumberAllocator>();
        services.AddScoped<IOrderReferenceCatalog, EfCoreOrderReferenceCatalog>();
        services.AddScoped<IStockAvailabilityChecker, NatsStockAvailabilityChecker>();

        services.AddHostedService<OrdersCreateResponder>();

        return services;
    }
}
