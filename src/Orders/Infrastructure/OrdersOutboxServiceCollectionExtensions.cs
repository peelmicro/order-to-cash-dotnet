using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence;

namespace OrderToCash.Orders.Infrastructure;

/// <summary>
/// <c>AddOrdersOutbox(IServiceCollection, Action&lt;OrdersOutboxOptions&gt;)</c>
/// — design.md §2.3, §8: one explicit registration line per port, no
/// assembly scan. Feature 15 calls this from <c>Program.cs</c> when it
/// builds the Orders host for the <c>orders.create</c> responder; until
/// then the relay runs only in tests (design.md §2.3's consequence,
/// inherited rather than discovered by feature 15).
/// </summary>
public static class OrdersOutboxServiceCollectionExtensions
{
    public static IServiceCollection AddOrdersOutbox(this IServiceCollection services, Action<OrdersOutboxOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OrdersOutboxOptions();
        configure(options);

        services.AddDbContext<OrdersDbContext>(db => db.UseSqlServer(options.ConnectionString));
        // ProcessedEventLedger and IdempotentConsumer (the CANONICAL copy,
        // design.md §6.3) take DbContext, never OrdersDbContext — this is
        // the one line that makes the scoped OrdersDbContext resolvable
        // that way.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<OrdersDbContext>());

        services.AddSingleton<IOptions<KafkaOptions>>(Options.Create(options.Kafka));
        services.AddSingleton<IOptions<OutboxRelayOptions>>(Options.Create(options.Relay));

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.AddScoped<OutboxWriter>();
        services.AddScoped<IOrderRepository, EfCoreOrderRepository>();

        services.AddScoped<ProcessedEventLedger>();
        services.AddScoped<IdempotentConsumer>();

        // KafkaFactPublisher: singleton, disposed by the container
        // (design.md §5.3 — one producer, IDisposable, CA2213 is an error
        // here so a forgotten dispose fails the build).
        services.AddSingleton<IFactPublisher, KafkaFactPublisher>();

        // OutboxRelay resolves as both its concrete type and IOutboxRelay,
        // the SAME scoped instance within one scope — the BackgroundService
        // depends on the interface (OI6's fake seam); nothing else needs
        // the concrete type, but it stays resolvable for parity with the
        // design.md §5.1 shape.
        services.AddScoped<OutboxRelay>();
        services.AddScoped<IOutboxRelay>(sp => sp.GetRequiredService<OutboxRelay>());

        // ILogger<T> is deliberately NOT registered here: this extension
        // owns ports, not cross-cutting infrastructure. Feature 15's
        // Program.cs calls services.AddLogging() (or the ASP.NET Core host
        // default, which does it implicitly) before or after this call —
        // registering a fallback here risks silently shadowing the host's
        // real logging if AddOrdersOutbox runs first.
        services.AddHostedService<OutboxRelayBackgroundService>();

        return services;
    }
}
