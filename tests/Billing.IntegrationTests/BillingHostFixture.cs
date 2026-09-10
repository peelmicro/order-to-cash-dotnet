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

    private static Task WaitUntilReachableAsync(NatsConnection connection) =>
        WaitUntilReachableAsync(connection, CreditSubjects.CreditList, RpcJson.Serialize(new CreditListRequestPayload(1, 1)), CancellationToken.None);

    /// <summary>
    /// Backlog id 63 — extracted to a generic, subject/probe-parameterised
    /// form (the exact shape <c>SagaIntegrationTestSupport.WaitUntilReachableAsync</c>
    /// already established in Orders.IntegrationTests, ported here because
    /// this fixture lives in a separate test assembly) so
    /// <c>BillingResponderReadinessRaceTests</c> can arm this loop's own
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

    public static async Task<Guid> SeedCreditLineAsync(MsSqlContainerFixture mssql, string connectionString, string code, string retailerCode, string companyCode, long creditLimit, string currencyCode = "EUR")
    {
        // BI17/A5 — deliberately broader than the hazard: the credit LIMIT
        // does not itself reach the credit-decision port, but a guard whose
        // scope a reader must reason about is a guard that gets bypassed
        // (design.md §10.1).
        CentsRuleFixtureGuard.AssertNotCentsRuleAmount(creditLimit, $"{nameof(SeedCreditLineAsync)}(code='{code}')");

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
        CentsRuleFixtureGuard.AssertNotCentsRuleAmount(amount, $"{nameof(SeedLedgerEntryAsync)}(orderReference='{orderReference}', type='{type}')");

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

    /// <summary>
    /// Hand-seeds an `invoices` row plus one `invoice_items` line directly
    /// in SQL — used by `BI9`'s `paid` repeat variant (feature 22 has no
    /// `billing.payment.register` responder yet, so this is the only way to
    /// seed a `paid` invoice) and by `BI24`'s round-trip. Deliberately
    /// bypasses <c>Invoice.Issue</c> so a caller can seed a row the domain
    /// would itself refuse (`BI10`'s store-side half).
    /// </summary>
    public static async Task<Guid> SeedInvoiceAsync(
        MsSqlContainerFixture mssql,
        string connectionString,
        string invoiceReference,
        string orderReference,
        string retailerCode,
        string companyCode,
        long amount,
        long discount,
        long totalAmount,
        string status,
        DateTime? paidAt,
        string currencyCode = "EUR",
        DateTime? invoiceDate = null)
    {
        CentsRuleFixtureGuard.AssertNotCentsRuleAmount(totalAmount, $"{nameof(SeedInvoiceAsync)}(orderReference='{orderReference}')");

        await using var db = mssql.CreateDbContext(connectionString);
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();

        db.Invoices.Add(new Invoice
        {
            Id = id,
            InvoiceReference = invoiceReference,
            InvoiceDate = invoiceDate ?? now,
            CompanyCode = companyCode,
            RetailerCode = retailerCode,
            OrderReference = orderReference,
            Amount = amount,
            Discount = discount,
            TotalAmount = totalAmount,
            CurrencyCode = currencyCode,
            Status = status,
            PaidAt = paidAt,
            CreatedAt = now,
            UpdatedAt = now,
        });

        db.InvoiceItems.Add(new InvoiceItem
        {
            Id = Guid.NewGuid(),
            InvoiceId = id,
            ProductCode = "SKU-SEED",
            Units = 1,
            Price = amount,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return id;
    }

    public static async Task<List<Invoice>> InvoicesOfAsync(MsSqlContainerFixture mssql, string connectionString, string orderReference)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.Invoices.AsNoTracking().Where(i => i.OrderReference == orderReference).ToListAsync();
    }

    public static async Task<List<InvoiceItem>> InvoiceItemsOfAsync(MsSqlContainerFixture mssql, string connectionString, Guid invoiceId)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.InvoiceItems.AsNoTracking().Where(i => i.InvoiceId == invoiceId).ToListAsync();
    }

    /// <summary>
    /// Builds a `billing.invoice.issue` request from simple line specs,
    /// guarding the COMPUTED total via <see cref="CentsRuleFixtureGuard"/>
    /// (`A3`/`A5`) — the hazard a scan of literals cannot see, since a
    /// three-line invoicing fixture's total is derived arithmetic, not a
    /// literal anywhere in this file.
    /// </summary>
    public static InvoiceIssueRequestPayload IssueRequest(
        string orderReference,
        string retailerCode,
        string companyCode,
        IReadOnlyList<(string ProductCode, int Units, long UnitPrice)> lines,
        long discount = 0,
        string currency = "EUR")
    {
        var computedTotal = lines.Sum(l => l.UnitPrice * (long)l.Units) - discount;
        CentsRuleFixtureGuard.AssertNotCentsRuleAmount(computedTotal, $"{nameof(IssueRequest)}(orderReference='{orderReference}')");

        return new InvoiceIssueRequestPayload(
            orderReference,
            retailerCode,
            companyCode,
            currency,
            [.. lines.Select(l => new Contracts.Facts.InvoiceLine(l.ProductCode, l.Units, l.UnitPrice))],
            discount == 0 ? null : discount);
    }

    // -- feature 22 (billing_remittance_intake) additions -------------------

    public static async Task<List<Payment>> PaymentsOfAsync(MsSqlContainerFixture mssql, string connectionString, Guid invoiceId)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.Payments.AsNoTracking().Where(p => p.InvoiceId == invoiceId).ToListAsync();
    }

    public static async Task<Payment?> FindPaymentByReferenceAsync(MsSqlContainerFixture mssql, string connectionString, string paymentReference)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.PaymentReference == paymentReference);
    }

    /// <summary>Builds a `billing.payment.register` request — the amount is guarded via <see cref="CentsRuleFixtureGuard"/> the SAME way <see cref="IssueRequest"/> already guards its own computed total.</summary>
    public static PaymentRegisterRequestPayload PaymentRequest(
        string paymentReference,
        long amount,
        DateTimeOffset valueDate,
        string? invoiceReference = null,
        Guid? invoiceId = null,
        string currency = "EUR",
        string source = "test")
    {
        CentsRuleFixtureGuard.AssertNotCentsRuleAmount(amount, $"{nameof(PaymentRequest)}(paymentReference='{paymentReference}')");

        return new PaymentRegisterRequestPayload(paymentReference, new CreditMoney(amount, currency), valueDate, source, invoiceId, invoiceReference);
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
