using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Infrastructure.Persistence;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;
using OrderToCash.Fulfillment.Presentation.Rpc;

namespace OrderToCash.Fulfillment.IntegrationTests;

/// <summary>
/// Boots the REAL <see cref="FulfillmentHost.CreateBuilder"/> graph against
/// real MS-SQL, real NATS and real Kafka, and provides the helpers the
/// integration suites need — callers are RAW <see cref="NatsConnection"/>
/// clients, never a hand-wired graph, exactly as the production caller
/// (Orders' <c>NatsSagaCommandsAdapter</c>) behaves (design.md §14).
/// </summary>
internal static class FulfillmentHostFixture
{
    public static async Task<(IHost Host, string ConnectionString)> StartHostAsync(
        MsSqlContainerFixture mssql,
        NatsContainerFixture nats,
        KafkaContainerFixture kafka,
        string databaseNameSuffix,
        Action<Infrastructure.FulfillmentOptions>? configure = null)
    {
        var connectionString = await mssql.CreateFreshDatabaseAsync($"otc_fulfillment_it_{databaseNameSuffix}_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(connectionString))
        {
            await seedDb.Database.MigrateAsync();
        }

        var builder = OrderToCash.Fulfillment.FulfillmentHost.CreateBuilder(
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

        var host = builder.Build();
        await host.StartAsync();

        // BackgroundService.StartAsync returns as soon as ExecuteAsync is
        // SCHEDULED, not once the five NATS subscriptions inside it have
        // actually landed server-side — the same subscribe-side race
        // Orders' own OrdersCreateAcceptanceTests closes for orders.create.
        // A cheap, side-effect-free stock.check probe on a nonexistent
        // company is retried until SOME reply arrives.
        await using (var probeConnection = new NatsConnection(new NatsOpts { Url = nats.Url }))
        {
            await WaitUntilReachableAsync(probeConnection);
        }

        return (host, connectionString);
    }

    private static Task WaitUntilReachableAsync(NatsConnection connection) =>
        WaitUntilReachableAsync(
            connection,
            StockSubjects.StockCheck,
            RpcJson.Serialize(new StockCheckRequestPayload("NATS-PROBE-CONNECTIVITY", [new StockCheckRequestLine("NATS-PROBE-CONNECTIVITY", 1)])),
            CancellationToken.None);

    /// <summary>
    /// Backlog id 63 — extracted to a generic, subject/probe-parameterised
    /// form (the exact shape <c>SagaIntegrationTestSupport.WaitUntilReachableAsync</c>
    /// already established in Orders.IntegrationTests, ported here because
    /// this fixture lives in a separate test assembly) so
    /// <c>FulfillmentResponderReadinessRaceTests</c> can arm this loop's own
    /// pacing deterministically, against a synthetic delayed subscriber, the
    /// same way <c>OrdersCancelResponderReadinessRaceTests</c> proves the
    /// Orders precedent. Previously this loop retried 100 times with NO
    /// delay between attempts, paced only by the per-attempt request
    /// timeout — <see cref="NatsNoRespondersException"/> is the server's
    /// IMMEDIATE "definitely nobody subscribed" sentinel, so it does not
    /// wait out that timeout, and the whole budget could expire in about a
    /// millisecond.
    /// </summary>
    internal static async Task WaitUntilReachableAsync(NatsConnection connection, string subject, byte[] probe, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var reply = await connection.RequestAsync<byte[], byte[]>(
                    subject,
                    probe,
                    replyOpts: new NatsSubOpts { Timeout = TimeSpan.FromMilliseconds(200) },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

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

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"'{subject}' never became reachable.");
    }

    /// <summary>A raw request/reply over NATS — the production caller's own shape, never a hand-wired dispatcher call.</summary>
    public static async Task<NatsMsg<byte[]>> RequestBareAsync(NatsConnection connection, string subject, byte[] payload, NatsHeaders? headers = null, TimeSpan? timeout = null)
    {
        var opts = new NatsSubOpts { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
        return await connection.RequestAsync<byte[], byte[]>(subject, payload, headers: headers, replyOpts: opts);
    }

    public static async Task SeedStockAsync(MsSqlContainerFixture mssql, string connectionString, Guid id, string companyCode, string productCode, int units, int reservedUnits = 0, int lowStockThreshold = 5)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        var now = DateTime.UtcNow;
        db.Stocks.Add(new Stock
        {
            Id = id,
            CompanyCode = companyCode,
            ProductCode = productCode,
            Units = units,
            ReservedUnits = reservedUnits,
            LowStockThreshold = lowStockThreshold,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    public static async Task<Guid> SeedReservationAsync(MsSqlContainerFixture mssql, string connectionString, Guid stockId, string companyCode, string retailerCode, string productCode, string orderReference, int units, string status)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        db.Reservations.Add(new Reservation
        {
            Id = id,
            StockId = stockId,
            CompanyCode = companyCode,
            RetailerCode = retailerCode,
            ProductCode = productCode,
            OrderReference = orderReference,
            Units = units,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    public static async Task<Stock?> FindStockAsync(MsSqlContainerFixture mssql, string connectionString, string companyCode, string productCode)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.Stocks.AsNoTracking().SingleOrDefaultAsync(s => s.CompanyCode == companyCode && s.ProductCode == productCode);
    }

    public static async Task<List<Reservation>> ReservationsOfAsync(MsSqlContainerFixture mssql, string connectionString, string orderReference)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.Reservations.AsNoTracking().Where(r => r.OrderReference == orderReference).ToListAsync();
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

    public static async Task<Despatch?> FindDespatchAsync(MsSqlContainerFixture mssql, string connectionString, string orderReference)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.Despatches.AsNoTracking().SingleOrDefaultAsync(d => d.OrderReference == orderReference);
    }

    public static async Task<List<DespatchItem>> DespatchItemsOfAsync(MsSqlContainerFixture mssql, string connectionString, Guid despatchId)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.DespatchItems.AsNoTracking().Where(i => i.DespatchId == despatchId).ToListAsync();
    }

    /// <summary>Waits for a terminal/monotonic condition — never polls a mid-flight counter (the reviewer's binding synchronisation rule since feature 16).</summary>
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
