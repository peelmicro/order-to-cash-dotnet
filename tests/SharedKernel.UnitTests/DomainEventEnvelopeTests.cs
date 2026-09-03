using OrderToCash.SharedKernel;
using OrderToCash.SharedKernel.Errors;
using Xunit;

namespace OrderToCash.SharedKernel.UnitTests;

/// <summary>
/// R11 (the matrix's <c>shared-kernel/domain/event-envelope.spec</c> row) —
/// pure, no framework, no store, no mock. A minimal <see cref="IDomainEventEnvelope"/>
/// test double stands in for a real domain event, since the guard is a pure
/// function of the interface and this project must not reference any
/// service's <c>Domain/</c> assembly.
/// </summary>
public sealed class DomainEventEnvelopeTests
{
    private static readonly UniqueId _validId = UniqueId.New();
    private static readonly DateTimeOffset _validOccurredAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string ValidEventType = "order.placed.v1";

    private static FakeEnvelope Valid() => new(_validId, ValidEventType, _validId, _validId, _validId, _validOccurredAt);

    [Fact]
    public void R11_DomainEventEnvelope_AcceptsACompleteEnvelope()
    {
        var exception = Record.Exception(() => DomainEventEnvelope.Validate(Valid()));
        Assert.Null(exception);
    }

    [Fact]
    public void R11_DomainEventEnvelope_RefusesAnEnvelopeWithAnAbsentNullOrEmptyFieldAndAnEventTypeThatDoesNotMatchThePattern_EventIdEmpty()
    {
        var envelope = Valid() with { EventId = default };
        var error = Assert.Throws<IncompleteDomainEventEnvelopeError>(() => DomainEventEnvelope.Validate(envelope));
        Assert.Equal("domain_event_envelope.incomplete", error.Code);
        Assert.Contains("EventId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void R11_DomainEventEnvelope_RefusesAnEnvelopeWithAnAbsentNullOrEmptyFieldAndAnEventTypeThatDoesNotMatchThePattern_AggregateIdEmpty()
    {
        var envelope = Valid() with { AggregateId = default };
        var error = Assert.Throws<IncompleteDomainEventEnvelopeError>(() => DomainEventEnvelope.Validate(envelope));
        Assert.Contains("AggregateId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void R11_DomainEventEnvelope_RefusesAnEnvelopeWithAnAbsentNullOrEmptyFieldAndAnEventTypeThatDoesNotMatchThePattern_CorrelationIdEmpty()
    {
        var envelope = Valid() with { CorrelationId = default };
        var error = Assert.Throws<IncompleteDomainEventEnvelopeError>(() => DomainEventEnvelope.Validate(envelope));
        Assert.Contains("CorrelationId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void R11_DomainEventEnvelope_RefusesAnEnvelopeWithAnAbsentNullOrEmptyFieldAndAnEventTypeThatDoesNotMatchThePattern_CausationIdEmpty()
    {
        var envelope = Valid() with { CausationId = default };
        var error = Assert.Throws<IncompleteDomainEventEnvelopeError>(() => DomainEventEnvelope.Validate(envelope));
        Assert.Contains("CausationId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void R11_DomainEventEnvelope_RefusesAnEnvelopeWithAnAbsentNullOrEmptyFieldAndAnEventTypeThatDoesNotMatchThePattern_OccurredAtDefault()
    {
        var envelope = Valid() with { OccurredAt = default };
        var error = Assert.Throws<IncompleteDomainEventEnvelopeError>(() => DomainEventEnvelope.Validate(envelope));
        Assert.Contains("OccurredAt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void R11_DomainEventEnvelope_RefusesAnEnvelopeWithAnAbsentNullOrEmptyFieldAndAnEventTypeThatDoesNotMatchThePattern_EventTypeNull()
    {
        var envelope = Valid() with { EventType = null! };
        var error = Assert.Throws<IncompleteDomainEventEnvelopeError>(() => DomainEventEnvelope.Validate(envelope));
        Assert.Contains("EventType", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void R11_DomainEventEnvelope_RefusesAnEnvelopeWithAnAbsentNullOrEmptyFieldAndAnEventTypeThatDoesNotMatchThePattern_EventTypeEmpty()
    {
        var envelope = Valid() with { EventType = string.Empty };
        var error = Assert.Throws<IncompleteDomainEventEnvelopeError>(() => DomainEventEnvelope.Validate(envelope));
        Assert.Contains("EventType", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Order.Placed.v1")]
    [InlineData("order.placed")]
    [InlineData("order.placed.v")]
    [InlineData("orderplaced.v1")]
    public void R11_DomainEventEnvelope_RefusesAnEnvelopeWithAnAbsentNullOrEmptyFieldAndAnEventTypeThatDoesNotMatchThePattern_BadEventType(string badEventType)
    {
        var envelope = Valid() with { EventType = badEventType };
        var error = Assert.Throws<IncompleteDomainEventEnvelopeError>(() => DomainEventEnvelope.Validate(envelope));
        Assert.Contains("EventType", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("order.placed.v1")]
    [InlineData("stock.reserved.v1")]
    [InlineData("credit.released.v1")]
    public void R11_DomainEventEnvelope_AcceptsEveryEventTypeMatchingThePattern(string goodEventType)
    {
        var envelope = Valid() with { EventType = goodEventType };
        var exception = Record.Exception(() => DomainEventEnvelope.Validate(envelope));
        Assert.Null(exception);
    }

    private sealed record FakeEnvelope(
        UniqueId EventId,
        string EventType,
        UniqueId AggregateId,
        UniqueId CorrelationId,
        UniqueId CausationId,
        DateTimeOffset OccurredAt) : IDomainEventEnvelope;
}
