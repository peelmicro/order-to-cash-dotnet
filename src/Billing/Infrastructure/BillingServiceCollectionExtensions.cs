using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Billing.Application;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Infrastructure.CreditDecisions;
using OrderToCash.Billing.Infrastructure.Messaging;
using OrderToCash.Billing.Infrastructure.Outbox;
using OrderToCash.Billing.Infrastructure.Persistence;
using OrderToCash.Billing.Presentation;

namespace OrderToCash.Billing.Infrastructure;

/// <summary>
/// <c>AddBilling(IServiceCollection, Action&lt;BillingOptions&gt;)</c> —
/// one explicit registration line per port, no assembly scan (CLAUDE.md:
/// "every port is registered explicitly"). Everything this service needs
/// in one call, the <c>AddFulfillment</c> shape.
/// </summary>
public static class BillingServiceCollectionExtensions
{
    public static IServiceCollection AddBilling(this IServiceCollection services, Action<BillingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new BillingOptions();
        configure(options);

        services.AddDbContext<BillingDbContext>(db => db.UseSqlServer(options.ConnectionString));

        services.AddSingleton<IOptions<KafkaOptions>>(Options.Create(options.Kafka));
        services.AddSingleton<IOptions<OutboxRelayOptions>>(Options.Create(options.Relay));
        services.AddSingleton<IOptions<NatsOptions>>(Options.Create(options.Nats));
        services.AddSingleton<IOptions<BillingResponderOptions>>(Options.Create(options.Responder));

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.AddSingleton<IFactPayloadMapper, BillingFactPayloadMapper>();
        services.AddScoped<OutboxWriter>();
        services.AddScoped<IBuyerCreditRepository, EfCoreBuyerCreditRepository>();
        services.AddScoped<ICreditReadPort, EfCoreCreditReadRepository>();

        // Feature 21 — invoicing. All FOUR scoped: everything that must
        // share the ambient transaction must share the scope, and
        // ValidateScopes = true (BillingHost, forced on everywhere) turns a
        // singleton that captured the scoped BillingDbContext into a BOOT
        // FAILURE rather than a silent cross-transaction write (ledger `L20`).
        services.AddScoped<IInvoiceRepository, EfCoreInvoiceRepository>();
        services.AddScoped<IInvoiceReadPort, EfCoreInvoiceReadRepository>();
        services.AddScoped<IInvoiceNumberAllocator, EfCoreInvoiceNumberAllocator>();
        services.AddScoped<InvoiceIssueService>();

        // Feature 22 — billing.payment.register. Scoped, the SAME reason
        // InvoiceIssueService is: it holds IInvoiceRepository/IBuyerCreditRepository,
        // both scoped, and must share the ambient transaction.
        services.AddScoped<PaymentRegisterService>();

        // The credit-decision port — feature 20's ONE-line replacement of
        // the previously-bound always-approving adapter (design.md §6.3,
        // `BC15`). `AlwaysApproveCreditDecision` stays in the tree as the
        // port's reference implementation and a future harness's override
        // point; it is no longer the production binding.
        services.AddSingleton<ICreditDecisionPort>(_ => new SimulatorCreditDecision(options.CreditFailureRate));

        services.AddScoped<CreditHoldService>();
        services.AddScoped<CreditReleaseService>();

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
        services.AddHostedService<BillingRpcResponder>();

        return services;
    }
}
