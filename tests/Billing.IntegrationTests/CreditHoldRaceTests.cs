using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Contracts.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.IntegrationTests;

/// <summary>`BC9` — the exclusive row lock on the credit line's own row, honestly (design.md §5.5).</summary>
[Collection(BillingCollection.Name)]
public sealed class CreditHoldRaceTests(MsSqlContainerFixture mssql, NatsContainerFixture nats, KafkaContainerFixture kafka)
{
    /// <summary>
    /// `Task.WhenAll` of two raw NATS requests for DIFFERENT orders against a
    /// line with room for exactly one, repeated on 10 fresh credit lines,
    /// asserting only on the replies, the final `Σ hold − Σ release ≤
    /// creditLimit`, and the outbox holding exactly one of each fact. No
    /// `SqlException` 1205 in any of the ten — exactly one row is ever
    /// locked, so no lock-ordering cycle can form.
    /// </summary>
    [Fact]
    public async Task BC9_TwoConcurrentHoldsAgainstOneNearlyExhaustedCreditLine_YieldExactlyOneCreditApprovedAndOneCreditRejected_WithCommittedHoldsNeverExceedingTheLimitAndNoDeadlock()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "race-bc9");
        using var _ = host;

        for (var i = 0; i < 10; i++)
        {
            var code = $"CR-RACE-{i:D4}";
            var creditId = await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, code, "CarrefourEs", $"RACE{i}", 5_000);

            var orderA = $"ORD-{800000 + (i * 2)}";
            var orderB = $"ORD-{800000 + (i * 2) + 1}";
            var correlationIdA = UniqueId.New();
            var correlationIdB = UniqueId.New();

            await using var connectionA = new NatsConnection(new NatsOpts { Url = nats.Url });
            await using var connectionB = new NatsConnection(new NatsOpts { Url = nats.Url });

            var requestA = RpcJson.Serialize(new CreditHoldRequestPayload(orderA, "CarrefourEs", $"RACE{i}", new CreditMoney(5_000, "EUR")));
            var requestB = RpcJson.Serialize(new CreditHoldRequestPayload(orderB, "CarrefourEs", $"RACE{i}", new CreditMoney(5_000, "EUR")));

            var taskA = BillingHostFixture.RequestBareAsync(connectionA, CreditSubjects.CreditHold, requestA, BuildHeaders(correlationIdA), TimeSpan.FromSeconds(15));
            var taskB = BillingHostFixture.RequestBareAsync(connectionB, CreditSubjects.CreditHold, requestB, BuildHeaders(correlationIdB), TimeSpan.FromSeconds(15));

            var results = await Task.WhenAll(taskA, taskB);

            var payloadA = RpcJson.Deserialize<CreditHoldReplyPayload>(results[0].Data!);
            var payloadB = RpcJson.Deserialize<CreditHoldReplyPayload>(results[1].Data!);

            var outcomes = new[] { payloadA.Outcome, payloadB.Outcome };
            Assert.Contains("approved", outcomes);
            Assert.Contains("rejected", outcomes);

            var committedExposure = await BillingHostFixture.CommittedExposureOfAsync(mssql, connectionString, creditId);
            Assert.True(committedExposure <= 5_000, $"committed exposure {committedExposure} exceeded the credit limit 5000 on line {code}.");

            var winningCorrelationId = payloadA.Outcome == "approved" ? correlationIdA : correlationIdB;
            var losingCorrelationId = payloadA.Outcome == "approved" ? correlationIdB : correlationIdA;

            var approvedRows = await BillingHostFixture.WaitForAsync(
                () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, winningCorrelationId.Value, "credit.approved.v1"),
                rows => rows.Count > 0,
                TimeSpan.FromSeconds(10));
            Assert.Single(approvedRows);

            var rejectedRows = await BillingHostFixture.WaitForAsync(
                () => BillingHostFixture.OutboxRowsForAsync(mssql, connectionString, losingCorrelationId.Value, "credit.rejected.v1"),
                rows => rows.Count > 0,
                TimeSpan.FromSeconds(10));
            Assert.Single(rejectedRows);
        }

        await host.StopAsync();
    }

    /// <summary>
    /// `D5`'s own arming target — TWO concurrent holds for the SAME order,
    /// against a line with room for both, so a duplicate write (rather than
    /// a limit rejection) is the only way this can go wrong: exactly ONE
    /// `credit_items` row for the order, and exactly one of the two replies
    /// carries <c>alreadyHeld: true</c>.
    /// </summary>
    [Fact]
    public async Task D5_TwoConcurrentHoldsForTheSameOrder_YieldExactlyOneLedgerEntryAndExactlyOneAlreadyHeldReply()
    {
        var (host, connectionString) = await BillingHostFixture.StartHostAsync(mssql, nats, kafka, "race-d5");
        using var _ = host;

        for (var i = 0; i < 10; i++)
        {
            var code = $"CR-RACED5-{i:D4}";
            await BillingHostFixture.SeedCreditLineAsync(mssql, connectionString, code, "CarrefourEs", $"RACED5{i}", 500_000);

            var order = $"ORD-{900000 + i}";
            var correlationIdA = UniqueId.New();
            var correlationIdB = UniqueId.New();

            await using var connectionA = new NatsConnection(new NatsOpts { Url = nats.Url });
            await using var connectionB = new NatsConnection(new NatsOpts { Url = nats.Url });

            var request = RpcJson.Serialize(new CreditHoldRequestPayload(order, "CarrefourEs", $"RACED5{i}", new CreditMoney(1_000, "EUR")));

            var taskA = BillingHostFixture.RequestBareAsync(connectionA, CreditSubjects.CreditHold, request, BuildHeaders(correlationIdA), TimeSpan.FromSeconds(15));
            var taskB = BillingHostFixture.RequestBareAsync(connectionB, CreditSubjects.CreditHold, request, BuildHeaders(correlationIdB), TimeSpan.FromSeconds(15));

            var results = await Task.WhenAll(taskA, taskB);

            var payloadA = RpcJson.Deserialize<CreditHoldReplyPayload>(results[0].Data!);
            var payloadB = RpcJson.Deserialize<CreditHoldReplyPayload>(results[1].Data!);

            var alreadyHeldFlags = new[] { payloadA.Outcome == "already_held", payloadB.Outcome == "already_held" };
            Assert.Single(alreadyHeldFlags, flag => flag);

            var ledger = await BillingHostFixture.LedgerOfAsync(mssql, connectionString, order);
            Assert.Single(ledger);
        }

        await host.StopAsync();
    }

    private static NatsHeaders BuildHeaders(UniqueId correlationId) => new()
    {
        { "x-correlation-id", correlationId.Value.ToString() },
        { "x-request-id", UniqueId.New().Value.ToString() },
    };
}
