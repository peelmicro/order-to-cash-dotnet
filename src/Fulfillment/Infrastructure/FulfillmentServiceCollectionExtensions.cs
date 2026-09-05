using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Fulfillment.Application;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Infrastructure.Messaging;
using OrderToCash.Fulfillment.Infrastructure.Outbox;
using OrderToCash.Fulfillment.Infrastructure.Persistence;
using OrderToCash.Fulfillment.Presentation;

namespace OrderToCash.Fulfillment.Infrastructure;

/// <summary>
/// <c>AddFulfillment(IServiceCollection, Action&lt;FulfillmentOptions&gt;)</c> —
/// one explicit registration line per port, no assembly scan (CLAUDE.md:
/// "every port is registered explicitly"). Everything this service needs in
/// one call, unlike Orders' three-extension split — this is one cohesive
/// feature rather than three landed separately over time.
/// </summary>
public static class FulfillmentServiceCollectionExtensions
{
    public static IServiceCollection AddFulfillment(this IServiceCollection services, Action<FulfillmentOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new FulfillmentOptions();
        configure(options);

        services.AddDbContext<FulfillmentDbContext>(db => db.UseSqlServer(options.ConnectionString));

        services.AddSingleton<IOptions<KafkaOptions>>(Options.Create(options.Kafka));
        services.AddSingleton<IOptions<OutboxRelayOptions>>(Options.Create(options.Relay));
        services.AddSingleton<IOptions<NatsOptions>>(Options.Create(options.Nats));
        services.AddSingleton<IOptions<StockResponderOptions>>(Options.Create(options.Responder));

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.AddScoped<OutboxWriter>();
        services.AddScoped<IStockItemRepository, EfCoreStockItemRepository>();
        services.AddScoped<IStockReadPort, EfCoreStockReadRepository>();

        services.AddScoped<StockReservationService>();
        services.AddScoped<StockReplenishService>();

        // KafkaFactPublisher: singleton, disposed by the container — one
        // producer, IDisposable (CA2213 is an error here).
        services.AddSingleton<IFactPublisher, KafkaFactPublisher>();

        services.AddScoped<OutboxRelay>();
        services.AddScoped<IOutboxRelay>(sp => sp.GetRequiredService<OutboxRelay>());
        services.AddHostedService<OutboxRelayBackgroundService>();

        // ONE INatsConnection per process — multiplexed, safe to share
        // across every subscribe/reply this responder makes. IAsyncDisposable
        // — the container disposes it on shutdown.
        services.AddSingleton<INatsConnection>(_ => new NatsConnection(new NatsOpts { Url = options.Nats.Url }));
        services.AddHostedService<StockRpcResponder>();

        return services;
    }
}
