using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using Xunit;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// Backlog id 89 — <c>SagaCommandDispatchWorker</c>'s parallel fast path was
/// EXERCISED by every Orders integration test (the harness overrides no
/// <c>Dispatch</c> option, so all of them run the real eight-loop worker) and
/// ASSERTED only by <c>Orders.UnitTests</c>' <c>SagaCommandDispatchWorkerTests</c>
/// against a fake <c>ISagaCommandDispatcher</c>. Exercised is not asserted: no
/// integration test would have failed if the concurrency regressed, only the
/// unit tests would — a defect detected solely at the level furthest from
/// production.
///
/// <para>This closes that with the SAME claim id 80 proves at unit level, made
/// through a real host, real NATS, real Kafka and the real
/// <c>saga_commands</c> table: two orders, the first order's
/// <c>stock.reserve</c> RPC held open by a gated responder, the SECOND order's
/// <c>stock.reserve</c> row observed reaching <c>sent</c> while the first is
/// still gated.</para>
///
/// <para><b>Two things are deliberately taken out of the way, because leaving
/// them in would make this guard unable to fail.</b> The sweeper
/// (<c>Sweeper.Enabled = false</c>) is the durability BACKSTOP: it re-claims
/// any row left <c>pending</c> past its grace window and dispatches it
/// itself, so with it running a sequential worker would still get the second
/// order sent — eventually, by the other mechanism — and the test would pass
/// against exactly the regression it exists to catch. That is what the
/// pre-existing <c>OperatorCancelRacesSagaForwardProgressTests.Confirmed_ReleaseWins</c>
/// relies on, by its own comment, and why that test is not this guard. And
/// <c>Command.TimeoutMs</c> is raised well past this test's own bound so the
/// gated RPC cannot simply time out and free the loop inside the observation
/// window.</para>
/// </summary>
[Collection(SagaCollection.Name)]
public sealed class SagaCommandDispatchConcurrencyIntegrationTests(KafkaContainerFixture kafka, NatsContainerFixture nats, MsSqlContainerFixture mssql)
{
    /// <summary>How long the second order's row is given to reach <c>sent</c> while the first order's dispatch is gated. Generous for a real Kafka round trip (observed ~2 s), and far below the gated RPC's own 60 s budget, so a SEQUENTIAL worker cannot pass it by accident.</summary>
    private static readonly TimeSpan _concurrencyBound = TimeSpan.FromSeconds(20);

    /// <summary>The gated RPC's per-attempt budget. It must exceed <see cref="_concurrencyBound"/> by a wide margin: if it did not, a sequential worker would be freed by the timeout inside the window and this guard would pass against the regression it names.</summary>
    private const int GatedCommandTimeoutMs = 60_000;

    [Fact]
    public async Task TheFastPathDispatchesTwoOrdersConcurrently_TheSecondOrdersStockReserveReachesSentWhileTheFirstsIsStillGated()
    {
        var (host, connectionString) = await SagaIntegrationTestSupport.StartHostAsync(
            mssql, kafka, nats, "dispatchConcurrency",
            configureSaga: options =>
            {
                options.Command.TimeoutMs = GatedCommandTimeoutMs;

                // One attempt only: a retry would issue a SECOND stock.reserve
                // for the gated order, which the responder would also hold —
                // more in-flight requests, no extra information, and a longer
                // teardown.
                options.Command.MaxAttempts = 1;

                // See the class remarks: the sweeper is the backstop that
                // would deliver the second order's command even under a
                // sequential worker, so with it running this test cannot fail
                // for the reason it is named after.
                options.Sweeper.Enabled = false;
            });
        try
        {
            await using var stockCheck = await StandInFulfillmentStockCheckResponder.StartAvailableAsync(nats.Url, CancellationToken.None);
            await using var stockReserve = await GatedStockReserveResponder.StartAsync(nats.Url, CancellationToken.None);

            // Order A — placed first, and its stock.reserve RPC is then held
            // open, occupying one of the worker's dispatch loops.
            var first = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var firstReference = first.OrderReference.Value;

            var firstArrived = await stockReserve.WaitUntilRequestArrivedAsync(firstReference, _concurrencyBound);
            Assert.True(
                firstArrived,
                $"order {firstReference}'s stock.reserve RPC never reached the gated responder within {_concurrencyBound.TotalSeconds:F0}s, so no dispatch loop was " +
                $"ever occupied and the concurrency claim below could not be tested at all. Requests observed: [{string.Join(", ", stockReserve.ObservedOrderReferences)}].");

            // Order B — placed only once A's dispatch is provably in flight
            // and blocked. Under a SEQUENTIAL worker its channel signal now
            // sits behind A's blocked DispatchAsync; under the real parallel
            // worker another loop picks it up immediately.
            var second = await SagaIntegrationTestSupport.PlaceOrderAsync(host);
            var secondReference = second.OrderReference.Value;
            stockReserve.Release(secondReference);

            var secondSent = await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(
                connectionString, mssql, second.OrderId.Value, "stock.reserve", "sent", _concurrencyBound);

            var firstStatus = await ReadSagaCommandStatusAsync(connectionString, first.OrderId.Value, "stock.reserve");

            Assert.True(
                secondSent > 0,
                $"order {secondReference}'s stock.reserve never reached 'sent' within {_concurrencyBound.TotalSeconds:F0}s while order {firstReference}'s " +
                $"stock.reserve RPC was held open by the gated responder (its own per-attempt budget is {GatedCommandTimeoutMs / 1000}s, and the sweeper backstop is " +
                $"disabled for this test). SagaCommandDispatchWorker is therefore dispatching the fast path SEQUENTIALLY: one blocked responder is stalling every " +
                $"other order's saga command behind it — backlog id 80's defect. Order {firstReference}'s own row is '{firstStatus}'; requests observed by the " +
                $"responder: [{string.Join(", ", stockReserve.ObservedOrderReferences)}].");

            Assert.True(
                firstStatus is not null && !string.Equals(firstStatus, "sent", StringComparison.Ordinal),
                $"order {firstReference}'s stock.reserve row is '{firstStatus}' at the moment order {secondReference}'s reached 'sent'. The gate was supposed to be " +
                "holding that RPC open, so the two dispatches were never actually overlapping and this run proves nothing about concurrency — it would pass just as " +
                "well against a sequential worker.");

            // Let the gated order finish, so teardown is not racing a live RPC.
            stockReserve.Release(firstReference);
            await SagaIntegrationTestSupport.WaitForSagaCommandCountAsync(
                connectionString, mssql, first.OrderId.Value, "stock.reserve", "sent", _concurrencyBound);
        }
        finally
        {
            await SagaIntegrationTestSupport.StopHostAndWaitForGroupToClearAsync(host, kafka);
        }
    }

    private async Task<string?> ReadSagaCommandStatusAsync(string connectionString, Guid orderId, string command)
    {
        await using var db = mssql.CreateDbContext(connectionString);
        return await db.SagaCommands
            .AsNoTracking()
            .Where(c => c.OrderId == orderId && c.Command == command)
            .Select(c => c.Status)
            .SingleOrDefaultAsync();
    }

    /// <summary>
    /// A real NATS responder on <c>fulfillment.stock.reserve</c> that holds
    /// EVERY request until the test releases that order's reference by name.
    /// Two differences from <c>StandInRpcResponder</c>, both required here:
    ///
    /// <list type="bullet">
    /// <item>each request is answered on its OWN task, so holding one order's
    /// reply does not block the subscription loop — a sequential responder
    /// would stall the second order's request too and the test would fail
    /// against a CORRECT worker;</item>
    /// <item>holding is the DEFAULT and releasing is explicit, so no gate has
    /// to be armed in the window between placing an order and its command
    /// being dispatched. Nothing here depends on timing.</item>
    /// </list>
    /// </summary>
    private sealed class GatedStockReserveResponder : IAsyncDisposable
    {
        /// <summary>The same readiness marker <c>StandInRpcResponder</c> uses, so this responder can be probed by the shared, already-armed <c>SagaIntegrationTestSupport.WaitUntilReachableAsync</c>.</summary>
        private static readonly byte[] _probeMarker = "\"__otc_saga_probe__\""u8.ToArray();

        private readonly NatsConnection _connection;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _gates = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _observed = new();
        private readonly Task _loop;

        private GatedStockReserveResponder(NatsConnection connection)
        {
            _connection = connection;
            _loop = RunAsync(_cts.Token);
        }

        public IReadOnlyList<string> ObservedOrderReferences => [.. _observed];

        public static async Task<GatedStockReserveResponder> StartAsync(string natsUrl, CancellationToken cancellationToken)
        {
            var connection = new NatsConnection(new NatsOpts { Url = natsUrl });
            var responder = new GatedStockReserveResponder(connection);

            try
            {
                await SagaIntegrationTestSupport.WaitUntilReachableAsync(connection, RpcSubjects.StockReserve, _probeMarker, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await responder.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            return responder;
        }

        /// <summary>Lets the held reply for one order's <c>stock.reserve</c> go out.</summary>
        public void Release(string orderReference) => GateFor(orderReference).TrySetResult();

        /// <summary>Blocks until this responder has actually RECEIVED a request for <paramref name="orderReference"/> — proof that a dispatch loop is occupied, never an assumption that it must be by now.</summary>
        public async Task<bool> WaitUntilRequestArrivedAsync(string orderReference, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                if (_observed.Contains(orderReference))
                {
                    return true;
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            return _observed.Contains(orderReference);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var gate in _gates.Values)
            {
                gate.TrySetResult();
            }

            await _cts.CancelAsync().ConfigureAwait(false);
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected — exactly what cancelling the loop above causes.
            }
            finally
            {
                try
                {
                    await _connection.PingAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort UNSUB fence only, as StandInRpcResponder's own teardown does.
                }

                await _connection.DisposeAsync().ConfigureAwait(false);
                _cts.Dispose();
            }
        }

        private TaskCompletionSource GateFor(string orderReference) =>
            _gates.GetOrAdd(orderReference, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            await foreach (var message in _connection.SubscribeAsync<byte[]>(RpcSubjects.StockReserve, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                if (message.Data is null)
                {
                    continue;
                }

                if (message.Data.AsSpan().SequenceEqual(_probeMarker))
                {
                    await message.ReplyAsync(_probeMarker, cancellationToken: cancellationToken).ConfigureAwait(false);
                    continue;
                }

                StockReserveRequestPayload request;
                try
                {
                    request = RpcJson.Deserialize<StockReserveRequestPayload>(message.Data);
                }
                catch (System.Text.Json.JsonException)
                {
                    continue;
                }

                _observed.Enqueue(request.OrderReference);

                // Answered on its own task — the whole point: a held reply
                // must not block this subscription loop, or every order would
                // queue behind the gated one HERE instead of in the worker,
                // and the test would be measuring the responder rather than
                // SagaCommandDispatchWorker.
                _ = AnswerWhenReleasedAsync(message, request, cancellationToken);
            }
        }

        private async Task AnswerWhenReleasedAsync(NatsMsg<byte[]> message, StockReserveRequestPayload request, CancellationToken cancellationToken)
        {
            try
            {
                await GateFor(request.OrderReference).Task.WaitAsync(cancellationToken).ConfigureAwait(false);

                var reply = new StockReserveReplyPayload("accepted", request.OrderReference, Reservations: []);
                await message.ReplyAsync(RpcJson.Serialize(reply), cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Teardown — the caller's own RPC budget covers it.
            }
        }
    }
}
