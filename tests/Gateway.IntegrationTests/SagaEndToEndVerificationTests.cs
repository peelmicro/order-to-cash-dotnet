using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;
using OrderToCash.Billing.Infrastructure.Persistence;
using OrderToCash.Billing.Infrastructure.Persistence.Entities;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Contracts.Wire;
using OrderToCash.Fulfillment.Presentation.Rpc;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.Orders.Infrastructure.Persistence;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// Feature <c>saga_e2e_verification</c> (id 28) — the whole saga proven
/// against REAL, SEPARATE processes: real MS-SQL (one disposable database
/// per write-model service), real Kafka, real NATS, real MongoDB, and five
/// real, unmodified service hosts (<see cref="OrderToCash.Orders.OrdersHost"/>,
/// <see cref="OrderToCash.Fulfillment.FulfillmentHost"/>,
/// <see cref="OrderToCash.Billing.BillingHost"/>,
/// <see cref="OrderToCash.Projector.ProjectorHost"/>, and the Gateway itself
/// via <see cref="GatewayTestHost"/>) — never a mock, never a stand-in for
/// any of the five.
/// </summary>
/// <remarks>
/// <para>
/// <b>#8 idiom, not #7's.</b> #7's own counterpart (`order-to-cash-nestjs`,
/// `progress/impl_saga_e2e_verification.md`) spawned six real OS
/// PROCESSES, because NestJS's DI graph could not otherwise be composed
/// from a test. #8 has no such limitation: every service already exposes
/// its own real composition root as a static
/// <c>XyzHost.CreateBuilder(...)</c> method, and EVERY existing
/// <c>Gateway.IntegrationTests</c> end-to-end suite
/// (<see cref="FulfillmentStockEndToEndTests"/>,
/// <see cref="StreamProjectorEndToEndTests"/>,
/// <see cref="OperatorNoteReachesTimelineEndToEndTests"/>) already boots
/// another service's REAL, unmodified host IN-PROCESS rather than as a
/// spawned child process. This suite is the same idiom, composed once for
/// all five services and five criteria rather than two or three. Ported
/// architecture, not ported mechanism — see <c>CLAUDE.md</c>'s own
/// instruction to translate #7's proven shape to .NET idioms rather than
/// copy its TypeScript specifics.
/// </para>
/// <para>
/// <b>No Gateway write-model DB-access guard exists in #8</b> (unlike #7's
/// <c>no-write-database-client.spec.ts</c>) — verified: no
/// <c>tests/Architecture.Tests</c> file forbids a SQL client dependency on
/// this TEST project, and CLAUDE.md's domain-purity rule only reaches
/// <c>Domain/</c> namespaces under <c>src/</c>, never a test project. So
/// this file reads Orders'/Fulfillment's/Billing's own write-model
/// databases directly via EF, exactly as
/// <see cref="OperatorNoteReachesTimelineEndToEndTests"/> already does for
/// Orders alone — no worker-process workaround (#7's
/// <c>mysql-worker-client.ts</c>) is needed.
/// </para>
/// <para>
/// <b>One shared fleet, built once, reused across all five criteria</b> —
/// the brief's own instruction, and #7's own proven approach. Each
/// <c>[Fact]</c> places its OWN order(s); no criterion depends on state a
/// sibling criterion left behind. The fleet is built lazily, exactly once,
/// by whichever <c>[Fact]</c> in this class happens to run first (guarded
/// by a static <see cref="SemaphoreSlim"/> — xUnit constructs a NEW
/// instance of this class per <c>[Fact]</c>, so the fleet itself is held in
/// a <see langword="static"/> field, and torn down by
/// <see cref="SagaFleetTeardown"/>, a collection fixture whose own
/// <c>DisposeAsync</c> xUnit guarantees runs only after every <c>[Fact]</c>
/// in <see cref="SagaE2ECollection"/> has finished).
/// </para>
/// </remarks>
[Collection(SagaE2ECollection.Name)]
public sealed class SagaEndToEndVerificationTests(
    KafkaContainerFixture kafka, MsSqlContainerFixture mssql, NatsContainerFixture nats, MongoContainerFixture mongo) : IAsyncLifetime
{
    private static readonly SemaphoreSlim _fleetGate = new(1, 1);
    private static SagaFleet? _fleet;

    public async Task InitializeAsync()
    {
        await _fleetGate.WaitAsync();
        try
        {
            _fleet ??= await SagaFleet.BuildAsync(kafka, mssql, nats, mongo);
        }
        finally
        {
            _fleetGate.Release();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Called by <see cref="SagaFleetTeardown"/> — never by an individual test.</summary>
    internal static async Task TearDownFleetIfBuiltAsync()
    {
        if (_fleet is { } fleet)
        {
            await fleet.DisposeAsync();
            _fleet = null;
        }
    }

    private SagaFleet Fleet => _fleet!;

    // ---------------------------------------------------------------
    // Criterion 1 — happy path reaches `completed`.
    // ---------------------------------------------------------------

    /// <summary>
    /// Places a real order through the real Gateway HTTP API, lets it flow
    /// through the whole saga (stock reserve -> credit hold -> confirm ->
    /// despatch -> invoice) entirely unattended, registers a real payment
    /// through the SAME HTTP API, and asserts BOTH the Projector's own read
    /// model (via <c>GET /orders/{id}</c>, which reads Mongo — R54) AND
    /// Orders' own authoritative write-model row reach <c>completed</c>.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public async Task Criterion1_HappyPath_ReachesCompleted()
    {
        var orderId = await PlaceOrderAsync(unitPrice: 1500, quantity: 2); // 3000 — not a `.99` total.

        await WaitForOrderStatusAsync(orderId, "invoiced", TimeSpan.FromSeconds(90));

        var orderReference = await GetOrderReferenceAsync(orderId);
        var invoiceId = await FindInvoiceIdAsync(orderReference, TimeSpan.FromSeconds(30));

        var paymentResponse = await Fleet.Gateway.Client.PostAsJsonAsync(
            $"/invoices/{invoiceId}/payments",
            new
            {
                paymentReference = $"PMT{Guid.NewGuid():N}"[..24], // <= 30 chars, per billing.payment.register's own validation.
                amount = new { amount = 3000L, currency = SagaFleet.Currency },
                valueDate = DateTimeOffset.UtcNow,
                source = "test", // Billing's closed set: operator | robot | test.
            });
        var paymentBody = await paymentResponse.Content.ReadAsStringAsync();
        Assert.True(paymentResponse.IsSuccessStatusCode, $"POST /invoices/{{id}}/payments failed: {(int)paymentResponse.StatusCode} {paymentResponse.StatusCode}: {paymentBody}");

        var final = await WaitForOrderStatusAsync(orderId, "completed", TimeSpan.FromSeconds(60));
        Assert.Equal("completed", final.GetProperty("status").GetString());

        // Cross-checked against Orders' own authoritative write-model row —
        // never merely trusting the read model's own eventual projection.
        await using var ordersDb = OpenOrdersDb();
        var row = await ordersDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal("completed", row.Status);
    }

    // ---------------------------------------------------------------
    // Criterion 2 — a `.99` order compensates visibly.
    // ---------------------------------------------------------------

    /// <summary>
    /// R42/`SimulatorCreditDecision` — a total whose minor units end in
    /// <c>99</c> is refused by the credit simulator UNCONDITIONALLY,
    /// regardless of available credit. Asserts three separate, precise
    /// facts — never merely the order's own terminal status — matching
    /// #7's own precedent for this criterion: (a) Orders' own
    /// <c>cancellationReason</c> column is <c>credit_rejected</c> (the
    /// DOMAIN's closed three-value set — <c>simulated_cents_rule</c> is
    /// Billing's own, more granular INTERNAL refusal reason and can never
    /// reach this column); (b) Billing's own <c>credit.rejected.v1</c>
    /// outbox row carries <c>reason: "simulated_cents_rule"</c> — the
    /// specific, precise proof the TRIGGER really was the <c>.99</c> rule;
    /// (c) Fulfillment's own reservation row is genuinely
    /// <c>released</c>, read directly, never inferred from the order's own
    /// status field.
    /// </summary>
    [Fact(Timeout = 240_000)]
    public async Task Criterion2_NinetyNineOrder_CompensatesVisibly()
    {
        var orderId = await PlaceOrderAsync(unitPrice: 1099, quantity: 1); // 1099 % 100 == 99.

        await WaitForOrderStatusAsync(orderId, "cancelled", TimeSpan.FromSeconds(90));

        await using var ordersDb = OpenOrdersDb();
        var orderRow = await ordersDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        Assert.Equal("cancelled", orderRow.Status);
        Assert.Equal("credit_rejected", orderRow.CancellationReason);

        await using var billingDb = OpenBillingDb();
        var rejectedRows = await billingDb.OutboxMessages.AsNoTracking()
            .Where(m => m.CorrelationId == orderId && m.EventType == "credit.rejected.v1")
            .ToListAsync();
        var rejectedRow = Assert.Single(rejectedRows);
        Assert.Contains(
            "\"reason\":\"simulated_cents_rule\"",
            rejectedRow.Payload,
            StringComparison.Ordinal);

        await using var fulfillmentDb = mssql.CreateDbContext(Fleet.FulfillmentConnectionString);
        var reservations = await fulfillmentDb.Reservations.AsNoTracking()
            .Where(r => r.OrderReference == orderRow.OrderReference)
            .ToListAsync();
        var reservation = Assert.Single(reservations);
        Assert.Equal("released", reservation.Status);
    }

    // ---------------------------------------------------------------
    // Criterion 3 — redelivery causes no corruption.
    // ---------------------------------------------------------------

    /// <summary>
    /// Reads back the REAL <c>order.placed.v1</c> envelope Orders' own
    /// outbox relay already published — byte-for-byte, from Orders' own
    /// outbox row, never fabricated — and republishes it, unchanged, to the
    /// SAME Kafka partition (same key = the order id). Immediately behind
    /// it, on the SAME partition, publishes a SECOND, well-formed but
    /// FRESH <c>order.placed.v1</c>-shaped fact for the SAME order (a new
    /// <c>eventId</c>, so it passes the eventId-dedup layer and exercises
    /// the SEPARATE precondition-status guard) — a "marker" whose own
    /// <c>saga_ignored_facts</c> row is the POSITIVE proof the partition
    /// was never blocked (never a bare sleep, per this repository's own
    /// rule that an absence-of-side-effect assertion proves nothing about
    /// whether anything was attempted). Then asserts exact row counts,
    /// before vs. after: Orders' own <c>saga_commands</c>
    /// (<c>stock.reserve</c>, stays 1) and Fulfillment's own
    /// <c>reservations</c> (stays at whatever count it held before the
    /// redelivery).
    /// </summary>
    [Fact(Timeout = 240_000)]
    public async Task Criterion3_Redelivery_CausesNoCorruption()
    {
        var orderId = await PlaceOrderAsync(unitPrice: 1200, quantity: 1);

        // "At least stock_reserved", never the exact string — a live-speed
        // saga can race past an intermediate status inside one poll tick
        // (#7's own documented finding, reproduced here structurally).
        var doc = await WaitForOrderStatusAtLeastAsync(orderId, "stock_reserved", TimeSpan.FromSeconds(60));
        var orderReference = doc.GetProperty("orderReference").GetString()!;

        await using var ordersDb = OpenOrdersDb();
        var originalRow = await ordersDb.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.CorrelationId == orderId && m.EventType == "order.placed.v1");

        var sagaCommandsBefore = await ordersDb.SagaCommands.AsNoTracking()
            .CountAsync(c => c.OrderId == orderId && c.Command == "stock.reserve");

        await using (var fulfillmentDbBefore = mssql.CreateDbContext(Fleet.FulfillmentConnectionString))
        {
            var reservationsBefore = await fulfillmentDbBefore.Reservations.AsNoTracking()
                .CountAsync(r => r.OrderReference == orderReference);

            // Republish the REAL envelope, byte-for-byte from the outbox row.
            var payloadElement = JsonDocument.Parse(originalRow.Payload).RootElement.Clone();
            var replayedEnvelope = new Envelope<JsonElement>(
                originalRow.EventId,
                originalRow.EventType,
                originalRow.AggregateId,
                originalRow.CorrelationId,
                originalRow.CausationId,
                new DateTimeOffset(DateTime.SpecifyKind(originalRow.OccurredAt, DateTimeKind.Utc)),
                payloadElement);
            var replayedBytes = JsonSerializer.SerializeToUtf8Bytes(replayedEnvelope, JsonWire.Options);

            // The marker — SAME order, a FRESH eventId, well-formed.
            var markerEventId = Guid.NewGuid();
            var markerEnvelope = new Envelope<JsonElement>(
                markerEventId,
                originalRow.EventType,
                originalRow.AggregateId,
                originalRow.CorrelationId,
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                payloadElement);
            var markerBytes = JsonSerializer.SerializeToUtf8Bytes(markerEnvelope, JsonWire.Options);

            using (var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
            {
                await producer.ProduceAsync(SagaFleet.OrdersFactsTopic, new Message<string, byte[]> { Key = orderId.ToString(), Value = replayedBytes });
                await producer.ProduceAsync(SagaFleet.OrdersFactsTopic, new Message<string, byte[]> { Key = orderId.ToString(), Value = markerBytes });
            }

            // Positive proof of continuation: the marker's OWN eventId
            // recorded as `precondition_unmet` (the order has long since
            // advanced past `placed`).
            await WaitForSagaIgnoredFactAsync(markerEventId, "precondition_unmet", TimeSpan.FromSeconds(60));

            var sagaCommandsAfter = await ordersDb.SagaCommands.AsNoTracking()
                .CountAsync(c => c.OrderId == orderId && c.Command == "stock.reserve");
            Assert.Equal(sagaCommandsBefore, sagaCommandsAfter);

            await using var fulfillmentDbAfter = mssql.CreateDbContext(Fleet.FulfillmentConnectionString);
            var reservationsAfter = await fulfillmentDbAfter.Reservations.AsNoTracking()
                .CountAsync(r => r.OrderReference == orderReference);
            Assert.Equal(reservationsBefore, reservationsAfter);
        }
    }

    // ---------------------------------------------------------------
    // Criterion 4 — a poisoned message reaches the DLQ.
    // ---------------------------------------------------------------

    /// <summary>
    /// OR1/R16's own poison shape (<c>SagaDeadLetterTests</c>'s proven,
    /// ARMED precedent, reused verbatim here one level up the pyramid,
    /// against the shared, real six-process fleet rather than a
    /// dedicated single-purpose Orders host): a <c>stock.reserved.v1</c>
    /// envelope whose PAYLOAD is a bare JSON string rather than an object
    /// — valid at the envelope level, poison only at deserialisation. A
    /// non-UUID <c>correlationId</c> (#7's own poison shape) is
    /// UNREACHABLE in #8, since the envelope's <c>correlationId</c> field
    /// is a real <see cref="Guid"/>, not a string — caught by the
    /// envelope-level guard, proving nothing about OR1.
    /// </summary>
    [Fact(Timeout = 180_000)]
    public async Task Criterion4_PoisonedMessage_ReachesTheDlq()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        var poisonEnvelope = new Envelope<string>(eventId, "stock.reserved.v1", aggregateId, correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow, "poison-payload-not-an-object");
        var poisonBytes = JsonSerializer.SerializeToUtf8Bytes(poisonEnvelope, JsonWire.Options);

        using (var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
        {
            await producer.ProduceAsync(SagaFleet.OrdersFactsTopic, new Message<string, byte[]> { Key = correlationId.ToString(), Value = poisonBytes });
        }

        var dlqRecord = await ConsumeOneFromDlqAsync(correlationId, "orders.saga", TimeSpan.FromSeconds(90));
        Assert.NotNull(dlqRecord);
        Assert.Equal(poisonBytes, dlqRecord!.Message.Value);

        var headers = dlqRecord.Message.Headers.ToDictionary(
            h => h.Key,
            h => System.Text.Encoding.UTF8.GetString(h.GetValueBytes()),
            StringComparer.Ordinal);
        Assert.Equal("orders.saga", headers["x-failed-consumer"]);
        Assert.Equal(SagaFleet.OrdersFactsTopic, headers["x-original-topic"]);
        Assert.Equal("stock.reserved.v1", headers["x-event-type"]);

        // Verify the Projector's own dead-lettering for the same poison reaches the container.
        var projectorDlqRecord = await ConsumeOneFromDlqAsync(correlationId, "projector", TimeSpan.FromSeconds(90));
        Assert.NotNull(projectorDlqRecord);
        var projectorHeaders = projectorDlqRecord.Message.Headers.ToDictionary(
            h => h.Key,
            h => System.Text.Encoding.UTF8.GetString(h.GetValueBytes()),
            StringComparer.Ordinal);
        Assert.Equal(SagaFleet.OrdersFactsTopic, projectorHeaders["x-original-topic"]);

        // THE property that actually failed live: the NEXT, DISTINCT,
        // WELL-FORMED fact on the SAME partition (same key) still
        // processes — the poison message must not block the partition.
        var secondEventId = Guid.NewGuid();
        var healthyPayload = new StockReservedPayload("ORD-E2E-DOES-NOT-EXIST", SagaFleet.CompanyCode, []);
        var healthyEnvelope = new Envelope<StockReservedPayload>(secondEventId, "stock.reserved.v1", aggregateId, correlationId, Guid.NewGuid(), DateTimeOffset.UtcNow, healthyPayload);
        var healthyBytes = JsonSerializer.SerializeToUtf8Bytes(healthyEnvelope, JsonWire.Options);

        using (var producer = new ProducerBuilder<string, byte[]>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
        {
            await producer.ProduceAsync(SagaFleet.OrdersFactsTopic, new Message<string, byte[]> { Key = correlationId.ToString(), Value = healthyBytes });
        }

        await WaitForSagaIgnoredFactAsync(secondEventId, "unknown_order", TimeSpan.FromSeconds(90));
    }

    // ---------------------------------------------------------------
    // Criterion 5 — R56's composed-stack trace observation.
    // ---------------------------------------------------------------

    /// <summary>
    /// R56/R57 — one trace id spanning the inbound Gateway HTTP request,
    /// the NATS RPC hop into Orders, and the write-model transactions of
    /// THREE real, separate processes (Orders, Fulfillment, Billing),
    /// proven by reading each service's own, independently and durably
    /// recorded <c>outbox.trace_parent</c> column (R57's already-armed
    /// mechanism — <c>TraceContextPropagationTests</c> proves each
    /// service's OWN extraction/injection independently; this criterion
    /// composes all three into ONE cross-process equality assertion for a
    /// SINGLE real order's real happy-path journey, which no existing test
    /// in this codebase does). Projector is not included: it owns no
    /// outbox (nothing durable to read a trace id back from) and no OTel
    /// collector is stood up in this suite (out of this feature's scope,
    /// same disclosed limitation #7's own Pass 3 recorded for the
    /// identical reason).
    /// </summary>
    /// <remarks>
    /// <b>Un-skipped — the composed-stack-only-observable production gap
    /// this criterion found is fixed.</b> Orders/Fulfillment/Billing used to
    /// carry a DIFFERENT trace id for the SAME order's happy path (observed
    /// run: orders=<c>432509b7…</c>, fulfillment=<c>949abcf3…</c>,
    /// billing=<c>81271d40…</c> for one order). Root cause, read from
    /// source rather than guessed: <c>SagaFactsConsumer.cs</c> DOES extract
    /// the fact's <c>traceparent</c> and starts a consumer
    /// <see cref="System.Diagnostics.Activity"/> under it
    /// (<c>TraceContext.ExtractKafka</c> + <c>OtcActivity.Source.StartActivity</c>)
    /// — but <c>SagaCommandDispatchWorker.ConsumeLoopAsync</c>
    /// (<c>src/Orders/Infrastructure/Saga/SagaCommandDispatchWorker.cs</c>,
    /// backlog id 80's own fast-path channel) dispatched the RESULTING
    /// outbound NATS command (<c>stock.reserve</c> etc.) on a SEPARATE
    /// background loop reading from a <see cref="System.Threading.Channels.Channel{T}"/>
    /// — no trace context of any kind was captured when the command was
    /// enqueued or restored when it was dispatched, so the outbound NATS
    /// call to Fulfillment/Billing started under whatever (unrelated, or
    /// no) <c>Activity.Current</c> the background loop happened to have,
    /// never the fact's own trace. R57's own tests stayed green throughout
    /// because they each prove ONE hop's inject/extract in isolation
    /// (NATS-in-isolation, Kafka-publish-in-isolation) — none of them
    /// crosses the async channel HANDOFF between "a fact was consumed" and
    /// "its resulting command was dispatched", which is exactly the seam
    /// this composed-stack criterion exists to catch and no lower-level
    /// test could substitute for (test-matrix.md's own R56 row, verbatim).
    /// Fixed (progress/impl_saga_trace_context_fast_path_fix.md) by adding
    /// <c>SagaCommandRef.TraceParent</c> — captured automatically from
    /// <c>Activity.Current</c> at every one of the record's construction
    /// sites, under the triggering fact's own span — and restoring it in
    /// <c>SagaCommandDispatchWorker.ConsumeLoopAsync</c> as a linked
    /// "dispatch" span's parent before calling <c>dispatcher.DispatchAsync</c>,
    /// the same "stored traceparent → linked child span" shape
    /// <c>OutboxRelay.BuildPublishableFact</c> already used for the
    /// identical kind of durable, async-boundary-crossing hand-off.
    /// </remarks>
    [Fact(Timeout = 180_000)]
    public async Task Criterion5_R56_OneTraceIdSpansTheComposedRealStack()
    {
        var orderId = await PlaceOrderAsync(unitPrice: 1234, quantity: 1);
        await WaitForOrderStatusAsync(orderId, "invoiced", TimeSpan.FromSeconds(90));

        await using var ordersDb = OpenOrdersDb();
        var ordersTraceParent = await ordersDb.OutboxMessages.AsNoTracking()
            .Where(m => m.CorrelationId == orderId && m.EventType == "order.placed.v1")
            .Select(m => m.TraceParent)
            .SingleAsync();

        await using var fulfillmentDb = mssql.CreateDbContext(Fleet.FulfillmentConnectionString);
        var fulfillmentTraceParent = await fulfillmentDb.OutboxMessages.AsNoTracking()
            .Where(m => m.CorrelationId == orderId && m.EventType == "stock.reserved.v1")
            .Select(m => m.TraceParent)
            .SingleAsync();

        await using var billingDb = OpenBillingDb();
        var billingTraceParent = await billingDb.OutboxMessages.AsNoTracking()
            .Where(m => m.CorrelationId == orderId && m.EventType == "credit.approved.v1")
            .Select(m => m.TraceParent)
            .SingleAsync();

        var traceIds = new[] { ordersTraceParent, fulfillmentTraceParent, billingTraceParent }
            .Select(tp =>
            {
                Assert.True(!string.IsNullOrEmpty(tp) && tp.Split('-') is { Length: 4 } parts && parts[1].Length == 32,
                    $"expected a real W3C traceparent, got: \"{tp}\"");
                return tp!.Split('-')[1];
            })
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(
            traceIds.Count == 1,
            $"expected ONE trace id across Orders/Fulfillment/Billing's own recorded trace_parent columns for order {orderId}, got {traceIds.Count}: [{string.Join(", ", traceIds)}] (orders={ordersTraceParent}, fulfillment={fulfillmentTraceParent}, billing={billingTraceParent}).");
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private async Task<Guid> PlaceOrderAsync(long unitPrice, int quantity)
    {
        var response = await Fleet.Gateway.Client.PostAsJsonAsync(
            "/orders",
            new
            {
                retailerCode = SagaFleet.RetailerCode,
                companyCode = SagaFleet.CompanyCode,
                currency = SagaFleet.Currency,
                lines = new[] { new { productCode = SagaFleet.ProductCode, quantity, unitPrice } },
            });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"POST /orders failed: {(int)response.StatusCode} {response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("orderId").GetGuid();
    }

    private async Task<string> GetOrderReferenceAsync(Guid orderId)
    {
        var response = await Fleet.Gateway.Client.GetAsync($"/orders/{orderId}");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("orderReference").GetString()!;
    }

    private async Task<Guid> FindInvoiceIdAsync(string orderReference, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var response = await Fleet.Gateway.Client.GetAsync($"/invoices?orderReference={orderReference}");
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var items = doc.RootElement.GetProperty("items");
            if (items.GetArrayLength() > 0)
            {
                return items[0].GetProperty("invoiceId").GetGuid();
            }

            await Task.Delay(300);
        }

        throw new TimeoutException($"no invoice ever appeared for order reference \"{orderReference}\" within {timeout}.");
    }

    /// <summary>Polls <c>GET /orders/{id}</c> — the REAL read model, through the REAL Gateway — for the EXACT expected status.</summary>
    private async Task<JsonElement> WaitForOrderStatusAsync(Guid orderId, string expectedStatus, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        string? lastStatus = null;
        while (DateTime.UtcNow < deadline)
        {
            var response = await Fleet.Gateway.Client.GetAsync($"/orders/{orderId}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                lastStatus = doc.RootElement.GetProperty("status").GetString();
                if (lastStatus == expectedStatus)
                {
                    return doc.RootElement.Clone();
                }
            }

            await Task.Delay(300);
        }

        throw new TimeoutException($"order {orderId} never reached status \"{expectedStatus}\" within {timeout} (last observed: \"{lastStatus ?? "none/pending"}\").");
    }

    private static readonly string[] _statusRank =
    [
        "placed", "stock_reserved", "credit_approved", "confirmed", "despatched", "invoiced", "paid", "completed",
    ];

    /// <summary>
    /// A live-speed saga can race past an EXACT intermediate status inside
    /// one poll tick — #7's own documented finding, reproduced here
    /// structurally rather than merely trusted: waits for the order's
    /// status to be <paramref name="minStatus"/> OR ANY LATER status in
    /// <see cref="_statusRank"/> (never <c>cancelled</c>, which this helper
    /// treats as a failure — a criterion using this helper never expects
    /// compensation).
    /// </summary>
    private async Task<JsonElement> WaitForOrderStatusAtLeastAsync(Guid orderId, string minStatus, TimeSpan timeout)
    {
        var minRank = Array.IndexOf(_statusRank, minStatus);
        var deadline = DateTime.UtcNow + timeout;
        string? lastStatus = null;
        while (DateTime.UtcNow < deadline)
        {
            var response = await Fleet.Gateway.Client.GetAsync($"/orders/{orderId}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                lastStatus = doc.RootElement.GetProperty("status").GetString();
                Assert.NotEqual("cancelled", lastStatus);
                var rank = Array.IndexOf(_statusRank, lastStatus);
                if (rank >= minRank)
                {
                    return doc.RootElement.Clone();
                }
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"order {orderId} never reached status \"{minStatus}\" or later within {timeout} (last observed: \"{lastStatus ?? "none/pending"}\").");
    }

    private async Task WaitForSagaIgnoredFactAsync(Guid eventId, string expectedMarker, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var db = OpenOrdersDb();
            var row = await db.SagaIgnoredFacts.AsNoTracking().SingleOrDefaultAsync(f => f.EventId == eventId);
            if (row is not null)
            {
                Assert.Equal(expectedMarker, row.Marker);
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"no saga_ignored_facts row ever appeared for eventId {eventId} within {timeout}.");
    }

    private async Task<ConsumeResult<string, byte[]>?> ConsumeOneFromDlqAsync(Guid correlationId, string expectedFailedConsumer, TimeSpan timeout)
    {
        const string dlqTopic = SagaFleet.OrdersFactsTopic + ".dlq";
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = $"e2e-dlq-probe-{Guid.NewGuid():N}",
            EnableAutoCommit = false,
        }).Build();

        consumer.Assign(Enumerable.Range(0, 6).Select(p => new TopicPartitionOffset(dlqTopic, new Partition(p), Offset.Beginning)).ToList());

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result is not null && !result.IsPartitionEOF && MatchesCorrelationId(result.Message.Value, correlationId))
            {
                var headers = result.Message.Headers.ToDictionary(
                    h => h.Key,
                    h => System.Text.Encoding.UTF8.GetString(h.GetValueBytes()),
                    StringComparer.Ordinal);
                if (headers.TryGetValue("x-failed-consumer", out var failedConsumer) && failedConsumer == expectedFailedConsumer)
                {
                    return result;
                }
                // Record did not match the expected consumer; skip and continue searching.
                continue;
            }
        }

        return null;
    }

    private static bool MatchesCorrelationId(byte[] messageValue, Guid correlationId)
    {
        try
        {
            using var document = JsonDocument.Parse(messageValue);
            return document.RootElement.TryGetProperty("correlationId", out var actual) && actual.GetGuid() == correlationId;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private OrdersDbContext OpenOrdersDb() =>
        new(new DbContextOptionsBuilder<OrdersDbContext>().UseSqlServer(Fleet.OrdersConnectionString).Options);

    private BillingDbContext OpenBillingDb() =>
        new(new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(Fleet.BillingConnectionString).Options);
}

/// <summary>
/// The shared fleet: three disposable MS-SQL databases (one per write-model
/// service), every fact topic and its <c>.dlq</c> companion, five real,
/// unmodified service hosts, and the seeded reference data every criterion
/// places an order against. Built ONCE (<see cref="BuildAsync"/>), reused —
/// never its DATA, only the fleet itself — by every criterion.
/// </summary>
internal sealed class SagaFleet : IAsyncDisposable
{
    public const string RetailerCode = "RETAILER-E2E";
    public const string CompanyCode = "COMPANY-E2E";
    public const string Currency = "EUR";
    public const string ProductCode = "PROD-E2E-1";
    private const string BuyerGln = "4006381333931";
    private const string SupplierGln = "5001234567890";

    public const string OrdersFactsTopic = "otc.orders.facts.v1";
    private const string FulfillmentFactsTopic = "otc.fulfillment.facts.v1";
    private const string BillingFactsTopic = "otc.billing.facts.v1";

    public required IHost OrdersHost { get; init; }

    public required string OrdersConnectionString { get; init; }

    public required IHost FulfillmentHost { get; init; }

    public required string FulfillmentConnectionString { get; init; }

    public required IHost BillingHost { get; init; }

    public required string BillingConnectionString { get; init; }

    public required IHost ProjectorHost { get; init; }

    public required GatewayTestHost Gateway { get; init; }

    public static async Task<SagaFleet> BuildAsync(KafkaContainerFixture kafka, MsSqlContainerFixture mssql, NatsContainerFixture nats, MongoContainerFixture mongo)
    {
        await EnsureTopicsAsync(kafka);

        var ordersConnectionString = await mssql.CreateFreshDatabaseAsync($"otc_orders_e2e_{Guid.NewGuid():N}");
        {
            var options = new DbContextOptionsBuilder<OrdersDbContext>().UseSqlServer(ordersConnectionString).Options;
            await using var db = new OrdersDbContext(options);
            await db.Database.MigrateAsync();
            await SeedOrdersReferenceDataAsync(db);
        }

        var fulfillmentConnectionString = await mssql.CreateFreshDatabaseAsync($"otc_fulfillment_e2e_{Guid.NewGuid():N}");
        await using (var seedDb = mssql.CreateDbContext(fulfillmentConnectionString))
        {
            await seedDb.Database.MigrateAsync();
            var now = DateTime.UtcNow;
            seedDb.Stocks.Add(new OrderToCash.Fulfillment.Infrastructure.Persistence.Entities.Stock
            {
                Id = Guid.NewGuid(),
                CompanyCode = CompanyCode,
                ProductCode = ProductCode,
                Units = 1_000_000,
                ReservedUnits = 0,
                LowStockThreshold = 5,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await seedDb.SaveChangesAsync();
        }

        var billingConnectionString = await mssql.CreateFreshDatabaseAsync($"otc_billing_e2e_{Guid.NewGuid():N}");
        {
            var options = new DbContextOptionsBuilder<BillingDbContext>().UseSqlServer(billingConnectionString).Options;
            await using var db = new BillingDbContext(options);
            await db.Database.MigrateAsync();
            var now = DateTime.UtcNow;
            db.Credits.Add(new Credit
            {
                Id = Guid.NewGuid(),
                Code = "CR-E2E-000001",
                RetailerCode = RetailerCode,
                CompanyCode = CompanyCode,
                CreditLimit = 100_000_000,
                CurrencyCode = Currency,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var mongoDatabase = $"otc_read_model_saga_e2e_{Guid.NewGuid():N}";

        // Fulfillment, Billing and Projector CONCURRENTLY first; Orders
        // spawned LAST once its downstream responders are already
        // listening (so the saga's first RPCs never race an unbooted
        // responder) — the brief's own ordering, unchanged from #7's.
        var fulfillmentHostTask = StartFulfillmentAsync(fulfillmentConnectionString, nats, kafka);
        var billingHostTask = StartBillingAsync(billingConnectionString, nats, kafka);
        var projectorHostTask = StartProjectorAsync(mongoDatabase, nats, kafka, mongo);

        await Task.WhenAll(fulfillmentHostTask, billingHostTask, projectorHostTask);
        var fulfillmentHost = await fulfillmentHostTask;
        var billingHost = await billingHostTask;
        var projectorHost = await projectorHostTask;

        var ordersHost = await StartOrdersAsync(ordersConnectionString, nats, kafka);

        var gateway = await GatewayTestHost.StartAsync(options =>
        {
            options.Nats.Url = nats.Url;
            options.Mongo.ConnectionUri = mongo.ConnectionString;
            options.Mongo.Database = mongoDatabase;
        });

        var loginResponse = await gateway.Client.PostAsJsonAsync("/auth/login", new { username = "operator", password = "otc_operator_dev_password_change_me" });
        loginResponse.EnsureSuccessStatusCode();
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginBody.GetProperty("accessToken").GetString();
        gateway.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return new SagaFleet
        {
            OrdersHost = ordersHost,
            OrdersConnectionString = ordersConnectionString,
            FulfillmentHost = fulfillmentHost,
            FulfillmentConnectionString = fulfillmentConnectionString,
            BillingHost = billingHost,
            BillingConnectionString = billingConnectionString,
            ProjectorHost = projectorHost,
            Gateway = gateway,
        };
    }

    private static async Task EnsureTopicsAsync(KafkaContainerFixture kafka)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        string[] topics =
        [
            OrdersFactsTopic, OrdersFactsTopic + ".dlq",
            FulfillmentFactsTopic, FulfillmentFactsTopic + ".dlq",
            BillingFactsTopic, BillingFactsTopic + ".dlq",
        ];

        // One topic per CreateTopicsAsync call — the established precedent
        // in this codebase (OperatorNoteReachesTimelineEndToEndTests'
        // CreateTopicIfMissingAsync, StreamProjectorEndToEndTests'
        // equivalent). A single multi-topic batch call was tried first and
        // found genuinely fragile against this broker: when one topic of
        // the six already exists (auto-created by an earlier metadata
        // probe against the same bootstrap) and five do not, the broker's
        // per-topic results come back MIXED, and `CreateTopicsException`
        // carries one `TopicAlreadyExists` entry alongside several
        // `NoError` entries — a catch filtered on "every result already
        // exists" does not match a MIXED result and the exception
        // propagates. One topic per call sidesteps the mixed-result shape
        // entirely, matching why no other fixture in this repository
        // creates its topics in one batch.
        foreach (var topic in topics)
        {
            try
            {
                await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 6, ReplicationFactor = 1 }]);
            }
            catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
            {
            }
        }
    }

    private static async Task SeedOrdersReferenceDataAsync(OrdersDbContext db)
    {
        var now = DateTime.UtcNow;
        var currencyId = Guid.NewGuid();

        db.Currencies.Add(new OrderToCash.Orders.Infrastructure.Persistence.Entities.Currency { Id = currencyId, Code = Currency, IsoNumber = "978", Symbol = "€", DecimalPoints = 2, CreatedAt = now, UpdatedAt = now });
        db.Retailers.Add(new OrderToCash.Orders.Infrastructure.Persistence.Entities.Retailer { Id = Guid.NewGuid(), Code = RetailerCode, Name = "E2E Retailer", Country = "FR", Vat = "FR00000000020", Gln = BuyerGln, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        db.Companies.Add(new OrderToCash.Orders.Infrastructure.Persistence.Entities.Company { Id = Guid.NewGuid(), Code = CompanyCode, Name = "E2E Company", Country = "FR", Vat = "FR00000000021", Gln = SupplierGln, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });
        db.Products.Add(new OrderToCash.Orders.Infrastructure.Persistence.Entities.Product { Id = Guid.NewGuid(), Code = ProductCode, Ean = "1000000000456", Name = "E2E Product", Description = "saga_e2e_verification fixture product", Price = 1_000, CurrencyId = currencyId, CreatedAt = now, UpdatedAt = now });

        await db.SaveChangesAsync();
    }

    private static async Task<IHost> StartOrdersAsync(string connectionString, NatsContainerFixture nats, KafkaContainerFixture kafka)
    {
        var builder = OrderToCash.Orders.OrdersHost.CreateBuilder(
            args: [],
            configureOutbox: o =>
            {
                o.ConnectionString = connectionString;
                o.Kafka.BootstrapServers = kafka.BootstrapServers;
                o.Relay.PollIntervalMs = 100;
            },
            configureAcceptance: o => o.Nats.Url = nats.Url,
            configureSaga: o =>
            {
                o.Kafka.BootstrapServers = kafka.BootstrapServers;
                o.Kafka.PollTimeoutMs = 200;
                // OrdersSagaOptions.DeadLetter is a SEPARATE, dedicated
                // producer connection (design.md §3.3). Left EXPLICIT here
                // even though AddOrdersSaga now falls back to
                // Kafka.BootstrapServers when this is unset (backlog id
                // 104) — found live before that fallback existed: leaving
                // it unset pointed the DLQ publisher at the default
                // "localhost:9092", a broker that does not exist at that
                // address (this fixture's Kafka container uses a
                // Docker-assigned ephemeral port), so criterion 4's poisoned
                // fact was retried and "dead-lettered" against a producer
                // that could never actually deliver it.
                o.DeadLetter.BootstrapServers = kafka.BootstrapServers;
            });

        var host = builder.Build();
        await host.StartAsync();

        await using var probe = new NatsConnection(new NatsOpts { Url = nats.Url });
        var probePayload = JsonSerializer.SerializeToUtf8Bytes(new OrdersCancelRequestPayload(Guid.NewGuid(), null, "operator_cancelled", null), JsonWire.Options);
        await StandInResponder.WaitUntilReachableAsync(probe, RpcSubjects.OrdersCancel, probePayload, CancellationToken.None);

        // The brief's own explicit instruction: "wait for the Orders Kafka
        // consumer group to report ready before running any criterion" —
        // the NATS readiness probe above proves the RPC responder is
        // listening, but says nothing about the SEPARATE `orders.saga`
        // Kafka consumer group's own join/rebalance, which runs on its own
        // BackgroundService and can still be mid-join when this method
        // returns. A fact produced before that join completes can be
        // missed by a `latest`-offset-reset consumer joining AFTER it was
        // written — found live: Criterion4 timed out waiting for a DLQ
        // record because its poison fact was produced before this group
        // had joined.
        await WaitForConsumerGroupStableAsync(kafka.BootstrapServers, OrdersSagaGroupId, TimeSpan.FromSeconds(60));

        return host;
    }

    private const string OrdersSagaGroupId = "orders.saga";

    private static async Task WaitForConsumerGroupStableAsync(string bootstrapServers, string groupId, TimeSpan timeout)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = await admin.DescribeConsumerGroupsAsync([groupId], new DescribeConsumerGroupsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
                var description = result.ConsumerGroupDescriptions.SingleOrDefault(g => g.GroupId == groupId);
                if (description is not null && description.State == ConsumerGroupState.Stable && description.Members.Count > 0)
                {
                    return;
                }
            }
            catch (KafkaException)
            {
                // Transient — the group may not exist yet at all on the
                // first few probes. Retry within the budget.
            }

            await Task.Delay(300);
        }

        throw new TimeoutException($"Consumer group '{groupId}' never reached Stable with at least one member within {timeout}.");
    }

    private static async Task<IHost> StartFulfillmentAsync(string connectionString, NatsContainerFixture nats, KafkaContainerFixture kafka)
    {
        var builder = OrderToCash.Fulfillment.FulfillmentHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.ConnectionString = connectionString;
                options.Nats.Url = nats.Url;
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Relay.PollIntervalMs = 100;
                options.Responder.MaxConcurrentRequests = 32;
            });

        var host = builder.Build();
        await host.StartAsync();

        await using var probe = new NatsConnection(new NatsOpts { Url = nats.Url });
        var probePayload = JsonSerializer.SerializeToUtf8Bytes(
            new StockCheckRequestPayload("E2E-READINESS-PROBE", [new StockCheckRequestLine("E2E-READINESS-PROBE", 1)]),
            JsonWire.Options);
        await StandInResponder.WaitUntilReachableAsync(probe, StockSubjects.StockCheck, probePayload, CancellationToken.None);

        return host;
    }

    private static async Task<IHost> StartBillingAsync(string connectionString, NatsContainerFixture nats, KafkaContainerFixture kafka)
    {
        var builder = OrderToCash.Billing.BillingHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.ConnectionString = connectionString;
                options.Nats.Url = nats.Url;
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Relay.PollIntervalMs = 100;
                options.Responder.MaxConcurrentRequests = 32;
            });

        var host = builder.Build();
        await host.StartAsync();

        await using var probe = new NatsConnection(new NatsOpts { Url = nats.Url });
        var probePayload = JsonSerializer.SerializeToUtf8Bytes(new CreditListRequestPayload(1, 1), JsonWire.Options);
        await StandInResponder.WaitUntilReachableAsync(probe, OrderToCash.Billing.Presentation.Rpc.CreditSubjects.CreditList, probePayload, CancellationToken.None);

        return host;
    }

    private static async Task<IHost> StartProjectorAsync(string mongoDatabase, NatsContainerFixture nats, KafkaContainerFixture kafka, MongoContainerFixture mongo)
    {
        var builder = OrderToCash.Projector.ProjectorHost.CreateBuilder(
            args: [],
            configure: options =>
            {
                options.Kafka.BootstrapServers = kafka.BootstrapServers;
                options.Kafka.PollTimeoutMs = 200;
                // ProjectorFacts' DeadLetter is a SEPARATE, dedicated
                // producer connection. Left EXPLICIT even though
                // AddProjector now falls back to Kafka.BootstrapServers when
                // this is unset (backlog id 104) — this is the exact call
                // site the phase-16 wrap-up fixed live (Criterion4 dead-
                // lettered to localhost:9092 with the developer's
                // infrastructure down); redundant with the fallback now,
                // kept deliberately as belt and braces.
                options.DeadLetter.BootstrapServers = kafka.BootstrapServers;
                options.Nats.Url = nats.Url;
                options.Mongo.ConnectionUri = mongo.ConnectionString;
                options.Mongo.Database = mongoDatabase;
            });

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        await Gateway.DisposeAsync();

        await OrdersHost.StopAsync();
        OrdersHost.Dispose();

        await FulfillmentHost.StopAsync();
        FulfillmentHost.Dispose();

        await BillingHost.StopAsync();
        BillingHost.Dispose();

        await ProjectorHost.StopAsync();
        ProjectorHost.Dispose();
    }
}

/// <summary>
/// A collection fixture whose SOLE job is teardown timing: xUnit guarantees
/// every <c>ICollectionFixture</c>'s own <c>DisposeAsync</c> runs only
/// after every <c>[Fact]</c> in <see cref="SagaE2ECollection"/> has
/// finished — the hook <see cref="SagaEndToEndVerificationTests"/> uses to
/// tear down its own lazily-built, statically-held
/// <see cref="SagaFleet"/> exactly once, regardless of which <c>[Fact]</c>
/// happened to build it.
/// </summary>
public sealed class SagaFleetTeardown : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => SagaEndToEndVerificationTests.TearDownFleetIfBuiltAsync();
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SagaE2ECollection :
    ICollectionFixture<KafkaContainerFixture>, ICollectionFixture<MsSqlContainerFixture>,
    ICollectionFixture<NatsContainerFixture>, ICollectionFixture<MongoContainerFixture>,
    ICollectionFixture<SagaFleetTeardown>
{
    public const string Name = "GatewaySagaE2E";
}
