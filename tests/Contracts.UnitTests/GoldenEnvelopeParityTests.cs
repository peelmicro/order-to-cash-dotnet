using System.Text.Json;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using Xunit;

namespace OrderToCash.Contracts.UnitTests;

/// <summary>
/// The parity oracle: 12 real #7 wire envelopes captured from its retained
/// Kafka topics, committed under <c>GoldenEnvelopes/</c>. Every test in this
/// class deserialises a golden file into the matching
/// <see cref="Envelope{TPayload}"/>, re-serialises it with
/// <see cref="JsonWire.Options"/> — the same options instance every service
/// uses — and checks three separate things against the golden bytes:
/// <list type="number">
/// <item>the envelope's SEVEN top-level fields appear, and appear in the
/// order <c>asyncapi.yaml</c> declares (feature 8 acceptance item 3);</item>
/// <item>the six scalar envelope fields (everything except <c>payload</c>)
/// reproduce the golden bytes exactly — this is what proves the
/// <see cref="InstantJsonConverter"/>'s format and the default GUID
/// formatting match #7's wire, not merely that SOME ISO-8601 string came
/// out;</item>
/// <item>the <c>payload</c> object is semantically equal to the golden
/// payload — same keys, values, types and casing, key ORDER unasserted
/// (feature 8 acceptance item 4; CLAUDE.md's MySQL-json-column-normalisation
/// note explains why order is deliberately not a parity claim).</item>
/// </list>
/// Doing all three from one deserialise-then-reserialise round trip (rather
/// than hand-typing a second, independent construction of each object) is a
/// deliberate choice: hand-transcribing 12 golden files into C# object
/// initialisers would introduce a second, fallible copy of the oracle's data
/// instead of exercising the real deserialise/serialise path this project
/// exists to prove — and the round-trip acceptance item (5) needs exactly
/// this path anyway.
/// </summary>
public sealed class GoldenEnvelopeParityTests
{
    private static readonly string[] _expectedEnvelopeFieldOrder =
    [
        "eventId", "eventType", "aggregateId", "correlationId", "causationId", "occurredAt", "payload",
    ];

    [Fact]
    public void OrderPlacedV1_IsByteExactSemanticallyEqualAndRoundTrips() =>
        AssertGoldenEnvelopeParity<OrderPlacedPayload>("order_placed_v1.json");

    [Fact]
    public void StockReservedV1_IsByteExactSemanticallyEqualAndRoundTrips() =>
        AssertGoldenEnvelopeParity<StockReservedPayload>("stock_reserved_v1.json");

    [Fact]
    public void StockReleasedV1_IsByteExactSemanticallyEqualAndRoundTrips() =>
        AssertGoldenEnvelopeParity<StockReleasedPayload>("stock_released_v1.json");

    [Fact]
    public void CreditApprovedV1_IsByteExactSemanticallyEqualAndRoundTrips() =>
        AssertGoldenEnvelopeParity<CreditApprovedPayload>("credit_approved_v1.json");

    [Fact]
    public void CreditRejectedV1_IsByteExactSemanticallyEqualAndRoundTrips() =>
        AssertGoldenEnvelopeParity<CreditRejectedPayload>("credit_rejected_v1.json");

    [Fact]
    public void CreditReleasedV1_IsByteExactSemanticallyEqualAndRoundTrips() =>
        AssertGoldenEnvelopeParity<CreditReleasedPayload>("credit_released_v1.json");

    [Fact]
    public void OrderConfirmedV1_IsByteExactSemanticallyEqualAndRoundTrips() =>
        AssertGoldenEnvelopeParity<OrderConfirmedPayload>("order_confirmed_v1.json");

    [Fact]
    public void OrderDespatchedV1_IsByteExactSemanticallyEqualAndRoundTrips() =>
        AssertGoldenEnvelopeParity<OrderDespatchedPayload>("order_despatched_v1.json");

    [Fact]
    public void InvoiceIssuedV1_IsByteExactSemanticallyEqualAndRoundTrips() =>
        AssertGoldenEnvelopeParity<InvoiceIssuedPayload>("invoice_issued_v1.json");

    [Fact]
    public void PaymentReceivedV1_IsByteExactSemanticallyEqualAndRoundTrips() =>
        AssertGoldenEnvelopeParity<PaymentReceivedPayload>("payment_received_v1.json");

    [Fact]
    public void OrderCompletedV1_IsByteExactSemanticallyEqualAndRoundTrips() =>
        AssertGoldenEnvelopeParity<OrderCompletedPayload>("order_completed_v1.json");

    [Fact]
    public void OrderCancelledV1_IsByteExactSemanticallyEqualAndRoundTrips() =>
        AssertGoldenEnvelopeParity<OrderCancelledPayload>("order_cancelled_v1.json");

    /// <summary>
    /// `stock.rejected.v1` has NO golden file — #7's retained Kafka topics
    /// held no instance of the rare race fact at capture time (see
    /// progress/impl_contracts_package.md). This test cannot check against a
    /// golden oracle, so it proves the weaker thing that IS provable without
    /// one: a hand-built envelope for this fact still produces the correct
    /// field set and order, and still omits a payload field that was never
    /// set — i.e. the type is wired into <see cref="JsonWire"/> the same way
    /// as every other fact, even though its wire-parity claim against real
    /// #7 bytes is unproven and stays unproven until a real instance is
    /// captured.
    /// </summary>
    [Fact]
    public void StockRejectedV1_HasNoGoldenFile_ButStillProducesTheCorrectEnvelopeShape()
    {
        var envelope = new Envelope<StockRejectedPayload>(
            EventId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            EventType: "stock.rejected.v1",
            AggregateId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CorrelationId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            CausationId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            OccurredAt: new DateTimeOffset(2026, 8, 30, 12, 0, 0, 0, TimeSpan.Zero),
            Payload: new StockRejectedPayload(
                OrderReference: "ORD-000099",
                CompanyCode: "PORTOTOOLS",
                Shortages: [new Shortage("PRD-0001", Requested: 10, Available: 3)],
                Reason: "insufficient_stock"));

        var json = JsonSerializer.Serialize(envelope, JsonWire.Options);
        using var document = JsonDocument.Parse(json);

        AssertEnvelopeFieldOrder(document.RootElement);

        // retailerCode was never set on the payload (optional, left null) —
        // it must be OMITTED, not present as `"retailerCode":null`.
        var payload = document.RootElement.GetProperty("payload");
        Assert.False(payload.TryGetProperty("retailerCode", out _));
    }

    private static void AssertGoldenEnvelopeParity<TPayload>(string goldenFileName)
    {
        var path = RepositoryPaths.Find(Path.Combine("tests", "Contracts.UnitTests", "GoldenEnvelopes", goldenFileName));
        var goldenJson = File.ReadAllText(path);

        var envelope = JsonSerializer.Deserialize<Envelope<TPayload>>(goldenJson, JsonWire.Options);
        Assert.NotNull(envelope);

        var reserialised = JsonSerializer.Serialize(envelope, JsonWire.Options);

        using var goldenDocument = JsonDocument.Parse(goldenJson);
        using var reserialisedDocument = JsonDocument.Parse(reserialised);

        // (1) Envelope field SET and ORDER — byte-exact.
        AssertEnvelopeFieldOrder(reserialisedDocument.RootElement);

        // (2) The six scalar envelope fields reproduce the golden bytes
        // exactly (raw JSON text, including quoting), proving GUID and
        // Instant formatting match #7's wire, not merely "an" ISO string.
        foreach (var scalarField in new[] { "eventId", "eventType", "aggregateId", "correlationId", "causationId", "occurredAt" })
        {
            var expectedRaw = goldenDocument.RootElement.GetProperty(scalarField).GetRawText();
            var actualRaw = reserialisedDocument.RootElement.GetProperty(scalarField).GetRawText();

            Assert.True(
                expectedRaw == actualRaw,
                $"{goldenFileName}: envelope field '{scalarField}' is not byte-exact — golden {expectedRaw}, produced {actualRaw}");
        }

        // (3) Payload — semantically equal (keys, values, types, casing),
        // key order deliberately unasserted (CLAUDE.md's MySQL-normalisation
        // note). This also completes acceptance item 5's round trip: the
        // envelope above was DESERIALISED from goldenJson, not hand-built.
        JsonEquivalence.AssertSemanticallyEqual(
            goldenDocument.RootElement.GetProperty("payload"),
            reserialisedDocument.RootElement.GetProperty("payload"));
    }

    private static void AssertEnvelopeFieldOrder(JsonElement envelope)
    {
        var actualOrder = envelope.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.True(
            _expectedEnvelopeFieldOrder.SequenceEqual(actualOrder, StringComparer.Ordinal),
            $"Envelope field order must be [{string.Join(", ", _expectedEnvelopeFieldOrder)}], " +
            $"found [{string.Join(", ", actualOrder)}]");
    }
}
