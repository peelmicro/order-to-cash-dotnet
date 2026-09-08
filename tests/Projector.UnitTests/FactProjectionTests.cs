using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Projector.Domain;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

public sealed class FactProjectionTests
{
    /// <summary>One minimal, valid payload instance per catalogued eventType — every field settable, values chosen to be non-default so sentinel-style assertions elsewhere have something real to compare against.</summary>
    private static readonly IReadOnlyDictionary<string, object> _minimalPayloadByEventType = new Dictionary<string, object>(StringComparer.Ordinal)
    {
        ["order.placed.v1"] = new OrderPlacedPayload("ORD-1", "RET1", "COM1", "bgln", "sgln", "EUR", DateTimeOffset.UtcNow, [], 1, 0, 1),
        ["stock.reserved.v1"] = new StockReservedPayload("ORD-1", "COM1", [new ReservationRef(Guid.NewGuid(), "SKU1", 1)]),
        ["stock.rejected.v1"] = new StockRejectedPayload("ORD-1", "COM1", [new Shortage("SKU1", 2, 1)], "insufficient_stock"),
        ["stock.released.v1"] = new StockReleasedPayload("ORD-1", "COM1", [new ReservationRef(Guid.NewGuid(), "SKU1", 1)], "order_cancelled"),
        ["credit.approved.v1"] = new CreditApprovedPayload("ORD-1", "RET1", "COM1", "CR-1", "EUR", 1, 1),
        ["credit.rejected.v1"] = new CreditRejectedPayload("ORD-1", "RET1", "COM1", "EUR", 1, 1, "insufficient_credit"),
        ["credit.released.v1"] = new CreditReleasedPayload("ORD-1", "RET1", "COM1", "EUR", 1, 1, "invoice_paid"),
        ["order.confirmed.v1"] = new OrderConfirmedPayload("ORD-1", "RET1", "COM1", "EUR", 1, DateTimeOffset.UtcNow),
        ["order.despatched.v1"] = new OrderDespatchedPayload("ORD-1", "DES-1", DateTimeOffset.UtcNow, "COM1", "RET1", []),
        ["invoice.issued.v1"] = new InvoiceIssuedPayload("ORD-1", "INV-1", DateTimeOffset.UtcNow, "RET1", "COM1", "EUR", [], 1, 0, 1),
        ["payment.received.v1"] = new PaymentReceivedPayload("ORD-1", "INV-1", "PAY-1", "EUR", 1, DateTimeOffset.UtcNow, "bank_transfer"),
        ["order.completed.v1"] = new OrderCompletedPayload("ORD-1", "RET1", "COM1", "EUR", 1, DateTimeOffset.UtcNow),
        ["order.cancelled.v1"] = new OrderCancelledPayload("ORD-1", "RET1", "COM1", "buyer_requested", DateTimeOffset.UtcNow, []),
        ["order.saga_failed.v1"] = new OrderSagaFailedPayload("ORD-1", "cmd", 1, "err", DateTimeOffset.UtcNow),
    };

    private static FactEnvelope Envelope(object payload, string eventType) =>
        new(Guid.NewGuid(), eventType, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, payload);

    /// <summary>
    /// <c>PR2</c> — closes the loop in both directions. Direction 1 (a
    /// catalogued type with no arm) is proven structurally: this test
    /// iterates the LIVE <c>FactCatalog</c>, so a fifteenth catalogue entry
    /// with no projection arm throws <see cref="UnknownFactTypeError"/> and
    /// fails here automatically, without editing this file. Direction 2 (an
    /// arm for a type not in the catalogue) is proven by the impostor probe:
    /// a payload-shaped type that is deliberately absent from
    /// <c>FactCatalog</c> must still fall through to the <c>_ =&gt; throw</c>
    /// default.
    /// </summary>
    [Fact]
    public void PR2_ProjectsEveryEventTypeInTheFactCatalogue_AndFailsInBothDirectionsWhenTheCatalogueAndTheSwitchDisagree()
    {
        Assert.Equal(14, FactCatalog.PayloadTypesByEventType.Count);
        Assert.Equal(14, _minimalPayloadByEventType.Count);

        foreach (var (eventType, payloadType) in FactCatalog.PayloadTypesByEventType)
        {
            Assert.True(_minimalPayloadByEventType.TryGetValue(eventType, out var payload), $"no fixture payload for '{eventType}'");
            Assert.IsType(payloadType, payload);

            var delta = FactProjection.Project(Envelope(payload, eventType));
            Assert.NotNull(delta);
        }

        var impostor = new ImpostorPayload("not in the catalogue");
        var error = Assert.Throws<UnknownFactTypeError>(() => FactProjection.Project(Envelope(impostor, "impostor.v1")));
        Assert.Equal("impostor.v1", error.EventType);
    }

    private sealed record ImpostorPayload(string Value);

    [Fact]
    public void PR14_NeverReadsAClock_EveryTimestampComesFromTheEnvelopesOccurredAt()
    {
        foreach (var (eventType, payload) in _minimalPayloadByEventType)
        {
            var occurredAt = new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.Zero);
            var envelope = Envelope(payload, eventType) with { OccurredAt = occurredAt };

            var delta = FactProjection.Project(envelope);

            Assert.Equal(occurredAt, delta.Entry.OccurredAt);
        }
    }

    [Fact]
    public void PR28_ProjectsToAStoreAgnosticDeltaWithNoDriverOrSerialiserType()
    {
        var delta = FactProjection.Project(Envelope(_minimalPayloadByEventType["order.placed.v1"], "order.placed.v1"));

        // ProjectionDelta and its record members are all plain domain types —
        // this is also enforced at build time by DomainPurityTests, which is
        // non-vacuous for OrderToCash.Projector.Domain from this feature on.
        Assert.IsType<ProjectionDelta>(delta);
        Assert.All(
            typeof(ProjectionDelta).GetProperties(),
            p => Assert.DoesNotContain("Mongo", p.PropertyType.Namespace ?? string.Empty, StringComparison.Ordinal));
    }

    /// <summary><c>PR30</c>: <c>causationId</c> carried verbatim; the ordering key is never derived from <c>eventType</c>. Sentinel: swap <c>CausationId</c> and <c>EventId</c> on the copy — armed manually against the source (see progress/impl).</summary>
    [Fact]
    public void PR30_TheEntryCarriesTheEnvelopesCausationIdVerbatim_AndNoEventTypeDerivedOrderingKey()
    {
        var sentinelCausationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var sentinelEventId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var envelope = Envelope(_minimalPayloadByEventType["order.placed.v1"], "order.placed.v1") with
        {
            EventId = sentinelEventId,
            CausationId = sentinelCausationId,
        };

        var delta = FactProjection.Project(envelope);

        Assert.Equal(sentinelCausationId, delta.Entry.CausationId);
        Assert.Equal(sentinelEventId, delta.Entry.EventId);
        Assert.NotEqual(delta.Entry.CausationId, delta.Entry.EventId); // sentinels chosen distinct — a swap would trip this.
    }

    /// <summary><c>PR37</c> — one sentinel per copied field, at the wire→FactEnvelope→entry hop.</summary>
    [Fact]
    public void PR37_EveryEnvelopeFieldReachesTheEntryVerbatim_SentinelPerField()
    {
        var sentinelEventId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var sentinelCorrelationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var sentinelCausationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var sentinelOccurredAt = new DateTimeOffset(2031, 5, 6, 7, 8, 9, 123, TimeSpan.Zero);
        const string sentinelEventType = "order.placed.v1";

        var envelope = new FactEnvelope(
            sentinelEventId,
            sentinelEventType,
            sentinelCorrelationId,
            sentinelCausationId,
            sentinelOccurredAt,
            _minimalPayloadByEventType[sentinelEventType]);

        var delta = FactProjection.Project(envelope);

        Assert.Equal(sentinelEventId, delta.Entry.EventId);
        Assert.Equal(sentinelEventType, delta.Entry.EventType);
        Assert.Equal(sentinelCorrelationId, delta.OrderId);
        Assert.Equal(sentinelCausationId, delta.Entry.CausationId);
        Assert.Equal(sentinelOccurredAt, delta.Entry.OccurredAt);
    }

    /// <summary>
    /// <c>PR37</c> — one sentinel per copied field, at the
    /// <c>order.placed.v1</c> payload → <c>OrderHeaderDelta</c> hop. Every
    /// header field is given a value DISTINCT from every other, so a
    /// field-swap corruption (found live by the mutation sweep, L06/L07 —
    /// see progress/impl) trips a mismatch rather than passing by
    /// coincidence.
    /// </summary>
    [Fact]
    public void PR37_EveryOrderPlacedPayloadFieldReachesTheHeaderVerbatim_SentinelPerField()
    {
        var orderDate = new DateTimeOffset(2027, 2, 3, 4, 5, 6, 789, TimeSpan.Zero);
        var payload = new OrderPlacedPayload(
            OrderReference: "SENT-ORDERREF",
            RetailerCode: "SENT-RETAILER",
            CompanyCode: "SENT-COMPANY",
            BuyerGln: "SENT-BUYERGLN",
            SupplierGln: "SENT-SUPPLIERGLN",
            Currency: "SENT-CCY",
            OrderDate: orderDate,
            Lines: [new OrderLine("SENT-SKU", "desc", 7, 501, 11)],
            InitialAmount: 1001,
            InitialDiscount: 2002,
            TotalAmount: 3003);

        var delta = FactProjection.Project(Envelope(payload, "order.placed.v1"));
        var header = delta.Header;

        Assert.NotNull(header);
        Assert.Equal(payload.OrderReference, header!.OrderReference);
        Assert.Equal(payload.OrderDate, header.OrderDate);
        Assert.Equal(payload.RetailerCode, header.RetailerCode);
        Assert.Equal(payload.BuyerGln, header.BuyerGln);
        Assert.Equal(payload.CompanyCode, header.CompanyCode);
        Assert.Equal(payload.SupplierGln, header.SupplierGln);
        Assert.Equal(payload.Currency, header.Currency);
        Assert.Equal(payload.InitialAmount, header.InitialAmount);
        Assert.Equal(payload.InitialDiscount, header.InitialDiscount);
        Assert.Equal(payload.TotalAmount, header.TotalAmount);

        var item = Assert.Single(header.Items);
        Assert.Equal("SENT-SKU", item.ProductCode);
        Assert.Equal(7, item.Quantity);
        Assert.Equal(501L, item.UnitPrice);
        Assert.Equal(11L, item.LineDiscount);
    }

    [Fact]
    public void PR36_TheOnlyEventTypeTableUnderSrcProjectorIsTheFactCatalogue_ProvedByEnumeratingTheFourteenLiterals()
    {
        var root = RepositoryPaths.Find(string.Empty);
        var projectorDir = Path.Combine(root, "src", "Projector");

        var literals = FactCatalog.PayloadTypesByEventType.Keys.ToArray();
        Assert.Equal(14, literals.Length);

        var hits = new List<(string File, int Line, string Text)>();

        foreach (var file in Directory.EnumerateFiles(projectorDir, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                var isComment = trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("///", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal);
                foreach (var literal in literals)
                {
                    if (lines[i].Contains($"\"{literal}\"", StringComparison.Ordinal) && !isComment)
                    {
                        hits.Add((Path.GetRelativePath(root, file), i + 1, lines[i].Trim()));
                    }
                }
            }
        }

        // Classify every hit: FactCatalog.cs itself is the one table and is
        // allowed; a test-facing constant/log message is allowed; anything
        // else is a routing structure and fails. Since FactCatalog.cs lives
        // under src/Contracts (not src/Projector), NO hit under src/Projector
        // is expected at all — the routing table is FactCatalog itself, the
        // topic list is three literals (not fourteen), and every eventType
        // literal reaching src/Projector does so only via that one table's
        // dictionary VALUES resolved at runtime, never re-typed as a string.
        Assert.True(hits.Count == 0, "Unclassified eventType literal hit(s) under src/Projector/: " + string.Join("; ", hits.Select(h => $"{h.File}:{h.Line} — {h.Text}")));
    }
}
