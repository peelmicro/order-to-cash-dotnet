using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Infrastructure.CreditDecisions;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Infrastructure.Persistence;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;
using OrderToCash.Billing.Presentation.Rpc;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>
/// Boots the REAL <see cref="OrderToCash.Billing.BillingHost.CreateBuilder"/>
/// graph against real MS-SQL, real NATS and real Kafka, and provides the
/// helpers the integration suites need — callers are RAW
/// <see cref="NatsConnection"/> clients, never a hand-wired graph, exactly
/// as the production caller (Orders' <c>NatsSagaCommandsAdapter</c>)
/// behaves (design.md §13). Takes <see cref="ICreditDecisionPort"/> as a
/// construction parameter defaulting to the always-approve adapter —
/// design.md §13's own note that #7's harness bound the adapter
/// UNCONDITIONALLY in every integration file, which is why its
/// port-refusal defect (`D1`) was unreachable at that level.
/// </summary>
internal static class BillingHostFixture
{
    public static async Task<(IHost Host, string ConnectionString)> StartHostAsync(
        MsSqlContainerFixture mssql,
        NatsContainerFixture nats,
        KafkaContainerFixture kafka,
        string databaseNameSuffix,
        ICreditDecisionPort? decisionPort = null,
        Action<Infrastructure.BillingOptions>? configure = null)
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_it_{databaseNameSuffix}_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
        }

        var builder = OrderToCash.Billing.BillingHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.ConnectionString = connectionString;
                options.Nats.Url = nats.Url;
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Relay.PollIntervalMs = 100;
                options.Responder.MaxConcurrentRequests = 32;
                configure?.Invoke(options);
            });

        if (decisionPort is not null)
        {
            builder.Services.Replace(ServiceDescriptor.Singleton(decisionPort));
        }

        var host = builder.Build();
        await host.StartAsync();

        // BackgroundService.StartAsync returns as soon as ExecuteAsync is
        // SCHEDULED, not once the three NATS subscriptions inside it have
        // actually landed server-side — the same subscribe-side race
        // Fulfillment's own FulfillmentHostFixture closes. A cheap,
        // side-effect-free credit.list probe is retried until SOME reply
        // arrives.
        await using (var probeConnection = new NatsConnection(new NatsOpts { Url = nats.Url }))
        {
            await WaitUntilReachableAsync(probeConnection);
        }

        return (host, connectionString);
    }

    private static async Task WaitUntilReachableAsync(NatsConnection connection)
    {
        var probe = RpcJson.Serialize(new CreditListRequestPayload(1, 1));

        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                var reply = await connection.RequestAsync<byte[], byte[]>(
                    CreditSubjects.CreditList,
                    probe,
                    replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromMilliseconds(200) });

                if (reply.Data is not null)
                {
                    return;
                }
            }
            catch (NatsNoReplyException)
            {
            }
            catch (NatsNoRespondersException)
            {
            }
        }

        throw new TimeoutException("The Billing responder never became reachable.");
    }

    /// <summary>A raw request/reply over NATS — the production caller's own shape, never a hand-wired dispatcher call.</summary>
    public static async Task<NatsMsg<byte[]>> RequestBareAsync(NatsConnection connection, string subject, byte[] payload, NatsHeaders? headers = null, TimeSpan? timeout = null)
    {
        var opts = new NatsSubOpts { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
        return await connection.RequestAsync<byte[], byte[]>(subject, payload, headers: headers, replyOpts: opts);
    }

    public static async Task<Guid> SeedCreditLineAsync(MsSqlContainerFixture mssql, string connectionString, string code, string retailerCode, string companyCode, long creditLimit, string currencyCode = "EUR")
    {
        await using var db = mssql.CreateDbContext(connectionString);
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        db.Credits.Add(new Credit
        {
            Id = id,
            Code = code,
            RetailerCode = retailerCode,
            CompanyCode = companyCode,
            CreditLimit = creditLimit,
            CurrencyCode = currencyCode,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    public static async Task<Guid> SeedLedgerEntryAsync(MsSqlContainerFixture mssql, string connectionString, Guid creditId, string orderReference, long amount, string type)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        db.CreditItems.Add(new CreditItem
        {
            Id = id,
            CreditId = creditId,
            OrderReference = orderReference,
            Amount = amount,
            Type = type,
            CreditDate = now,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    public static async Task<Credit?> FindCreditAsync(MsSqlContainerFixture mssql, string connectionString, string retailerCode, string companyCode)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.Credits.AsNoTracking().SingleOrDefaultAsync(c => c.RetailerCode == retailerCode && c.CompanyCode == companyCode);
    }

    public static async Task<List<CreditItem>> LedgerOfAsync(MsSqlContainerFixture mssql, string connectionString, string orderReference)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.CreditItems.AsNoTracking().Where(i => i.OrderReference == orderReference).ToListAsync();
    }

    public static async Task<long> CommittedExposureOfAsync(MsSqlContainerFixture mssql, string connectionString, Guid creditId)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        var rows = await db.CreditItems.AsNoTracking().Where(i => i.CreditId == creditId).ToListAsync();
        return rows.Sum(r => r.Type == "hold" ? r.Amount : r.Type == "release" ? -r.Amount : 0);
    }

    public static async Task<List<OutboxMessage>> OutboxRowsForAsync(MsSqlContainerFixture mssql, string connectionString, Guid correlationId)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.OutboxMessages.AsNoTracking().Where(m => m.CorrelationId == correlationId).ToListAsync();
    }

    public static async Task<List<OutboxMessage>> OutboxRowsForAsync(MsSqlContainerFixture mssql, string connectionString, Guid correlationId, string eventType)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.OutboxMessages.AsNoTracking().Where(m => m.CorrelationId == correlationId && m.EventType == eventType).ToListAsync();
    }

    /// <summary>Waits for a terminal/monotonic condition — never polls a mid-flight counter or `availableCredit` (the reviewer's binding synchronisation rule since feature 16, design.md §13).</summary>
    public static async Task<T> WaitForAsync<T>(Func<Task<T>> probe, Func<T, bool> isDone, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = await probe();

        while (DateTime.UtcNow < deadline)
        {
            last = await probe();
            if (isDone(last))
            {
                return last;
            }

            await Task.Delay(100);
        }

        return last;
    }
}
