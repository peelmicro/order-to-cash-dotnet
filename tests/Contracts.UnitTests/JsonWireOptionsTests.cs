using System.Text.Json;
using OrderToCash.Contracts.Envelopes;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Contracts.Wire;
using Xunit;

namespace OrderToCash.Contracts.UnitTests;

/// <summary>
/// Direct unit tests of the three properties feature 8 acceptance item 2
/// names explicitly: camelCase property names, nulls omitted, no
/// <c>$type</c> discriminator (and no PascalCase envelope). Each test is
/// written so that reverting the corresponding <see cref="JsonWire.Options"/>
/// setting makes it fail — see progress/impl_contracts_package.md's arming
/// table for the verbatim messages produced when each setting was reverted
/// and the assembly forcibly rebuilt.
/// </summary>
public sealed class JsonWireOptionsTests
{
    [Fact]
    public void PropertyNamesAreCamelCaseNotPascalCase()
    {
        var payload = new OrderCompletedPayload(
            OrderReference: "ORD-000001",
            RetailerCode: "CarrefourEs",
            CompanyCode: "IBERFOODS",
            Currency: "EUR",
            TotalAmount: 100,
            CompletedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var json = JsonSerializer.Serialize(payload, JsonWire.Options);

        Assert.Contains("\"orderReference\"", json, StringComparison.Ordinal);
        Assert.Contains("\"retailerCode\"", json, StringComparison.Ordinal);
        Assert.Contains("\"totalAmount\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"OrderReference\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"TotalAmount\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvelopeFieldsAreCamelCaseNeverPascalCase()
    {
        var envelope = new Envelope<OrderCompletedPayload>(
            EventId: Guid.NewGuid(),
            EventType: "order.completed.v1",
            AggregateId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            CausationId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            Payload: new OrderCompletedPayload("ORD-000001", "CarrefourEs", "IBERFOODS", "EUR", 100, DateTimeOffset.UtcNow));

        var json = JsonSerializer.Serialize(envelope, JsonWire.Options);

        Assert.Contains("\"eventId\"", json, StringComparison.Ordinal);
        Assert.Contains("\"correlationId\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"EventId\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"CorrelationId\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Payload\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalNullFieldIsOmittedNotWrittenAsNull()
    {
        var payload = new OrderPlacedPayload(
            OrderReference: "ORD-000001",
            RetailerCode: "CarrefourEs",
            CompanyCode: "IBERFOODS",
            BuyerGln: "8412345000013",
            SupplierGln: "8400000000017",
            Currency: "EUR",
            OrderDate: DateTimeOffset.UtcNow,
            Lines: [new OrderLine("PRD-0001", "Olive oil 1 L", 5, 2995, 0)],
            InitialAmount: 14975,
            InitialDiscount: 0,
            TotalAmount: 14975,
            Notes: null);

        var json = JsonSerializer.Serialize(payload, JsonWire.Options);

        Assert.DoesNotContain("notes", json, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalNonNullFieldIsWritten()
    {
        var payload = new OrderPlacedPayload(
            OrderReference: "ORD-000001",
            RetailerCode: "CarrefourEs",
            CompanyCode: "IBERFOODS",
            BuyerGln: "8412345000013",
            SupplierGln: "8400000000017",
            Currency: "EUR",
            OrderDate: DateTimeOffset.UtcNow,
            Lines: [new OrderLine("PRD-0001", "Olive oil 1 L", 5, 2995, 0)],
            InitialAmount: 14975,
            InitialDiscount: 0,
            TotalAmount: 14975,
            Notes: "urgent delivery");

        var json = JsonSerializer.Serialize(payload, JsonWire.Options);

        Assert.Contains("\"notes\":\"urgent delivery\"", json, StringComparison.Ordinal);
    }

    /// <summary>SA-2/feature <c>operator_note_reaches_the_timeline</c> bullet 2 — a saga-decided cancellation carries no note, and the wire must omit the key rather than write it as <c>null</c>.</summary>
    [Fact]
    public void SA2_OrderCancelledPayload_NoteOmittedWhenNull()
    {
        var payload = new OrderCancelledPayload("ORD-000001", "CarrefourEs", "IBERFOODS", "stock_rejected", DateTimeOffset.UtcNow, [], Note: null);

        var json = JsonSerializer.Serialize(payload, JsonWire.Options);

        Assert.DoesNotContain("note", json, StringComparison.Ordinal);
    }

    /// <summary>SA-2 bullet 1 — the exact supplied note is written, bracketed to the value under test rather than merely "a" value.</summary>
    [Fact]
    public void SA2_OrderCancelledPayload_NoteWrittenWhenPresent()
    {
        var payload = new OrderCancelledPayload("ORD-000001", "CarrefourEs", "IBERFOODS", "operator_cancelled", DateTimeOffset.UtcNow, [], Note: "Buyer cancelled by phone.");

        var json = JsonSerializer.Serialize(payload, JsonWire.Options);

        Assert.Contains("\"note\":\"Buyer cancelled by phone.\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// SA-2 bullet 3 — a real envelope captured/stored BEFORE this field
    /// existed has no <c>note</c> key at all (never <c>"note":null</c>);
    /// deserialising it must leave <see cref="OrderCancelledPayload.Note"/>
    /// at its default (<see langword="null"/>) rather than throwing, so a
    /// replay of an old envelope still projects. A hand-written raw string,
    /// not a payload built then stripped, so the key is GENUINELY absent
    /// rather than present-and-null.
    /// </summary>
    [Fact]
    public void SA2_OrderCancelledPayload_DeserialisesAnOldEnvelopeWithNoNoteKey_LeavingNoteNull()
    {
        const string oldPayloadJson = """
            {"orderReference":"ORD-000001","retailerCode":"CarrefourEs","companyCode":"IBERFOODS","cancellationReason":"stock_rejected","cancelledAt":"2026-01-01T00:00:00.000Z","compensationSteps":[]}
            """;

        var payload = JsonSerializer.Deserialize<OrderCancelledPayload>(oldPayloadJson, JsonWire.Options);

        Assert.NotNull(payload);
        Assert.Null(payload!.Note);
        Assert.Equal("ORD-000001", payload.OrderReference);
    }

    [Fact]
    public void SerialisedEnvelopeNeverCarriesATypeDiscriminator()
    {
        var envelope = new Envelope<OrderCompletedPayload>(
            EventId: Guid.NewGuid(),
            EventType: "order.completed.v1",
            AggregateId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            CausationId: Guid.NewGuid(),
            OccurredAt: DateTimeOffset.UtcNow,
            Payload: new OrderCompletedPayload("ORD-000001", "CarrefourEs", "IBERFOODS", "EUR", 100, DateTimeOffset.UtcNow));

        var json = JsonSerializer.Serialize(envelope, JsonWire.Options);

        Assert.DoesNotContain("$type", json, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryServiceMustUseTheSameSharedOptionsInstance()
    {
        // There is deliberately only one way to obtain wire-compatible
        // options: the static JsonWire.Options field. This test exists so a
        // future refactor that adds a second factory method (e.g. a
        // "JsonWire.CreateOptions()" that quietly diverges from this one) is
        // caught by a reader of this test even before any behavioural
        // difference is observable.
        var first = JsonWire.Options;
        var second = JsonWire.Options;

        Assert.Same(first, second);
    }
}
