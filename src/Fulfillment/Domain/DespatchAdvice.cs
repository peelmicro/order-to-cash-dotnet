using OrderToCash.Contracts.Facts;
using OrderToCash.Fulfillment.Domain.Errors;
using OrderToCash.Fulfillment.Domain.Events;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain;

/// <summary>One despatched line, in domain types — 1:1 with the consumed <see cref="Reservation"/> it traces (F7), never merged even when two lines of the order named the same product.</summary>
public sealed record DespatchLineEntry(string ProductCode, Quantity Units);

/// <summary>
/// The aggregate root for one DESADV (specs/shared/domain-model.md §4.3).
/// <see cref="Create"/> is the only constructor: refuses an empty line list
/// (<b>F6</b>, <see cref="EmptyDespatchLinesError"/>) and appends exactly one
/// <c>order.despatched.v1</c> fact to its own event collection before
/// returning, so a caller can never observe an aggregate whose fact was not
/// recorded. A <see cref="DespatchAdvice"/> is created once and never
/// mutated again — there is no <c>Reconstitute</c>: the F8 idempotent-repeat
/// read path only ever needs a lightweight snapshot
/// (<c>Application.Ports.DespatchSnapshot</c>), never a live aggregate.
/// </summary>
public sealed class DespatchAdvice : AggregateRoot
{
    private readonly List<DespatchLineEntry> _lines;

    private DespatchAdvice(
        UniqueId id,
        string despatchReference,
        DateTimeOffset despatchDate,
        OrderNumber orderReference,
        string companyCode,
        string retailerCode,
        IReadOnlyList<DespatchLineEntry> lines)
        : base(id)
    {
        DespatchReference = despatchReference;
        DespatchDate = despatchDate;
        OrderReference = orderReference;
        CompanyCode = companyCode;
        RetailerCode = retailerCode;
        _lines = [.. lines];
    }

    public string DespatchReference { get; }

    public DateTimeOffset DespatchDate { get; }

    public OrderNumber OrderReference { get; }

    public string CompanyCode { get; }

    public string RetailerCode { get; }

    public IReadOnlyList<DespatchLineEntry> Lines => _lines;

    /// <summary>
    /// The only way a <see cref="DespatchAdvice"/> is born. Refuses
    /// (<see cref="EmptyDespatchLinesError"/>, F6) an empty
    /// <paramref name="lines"/> list BEFORE the aggregate exists — there is
    /// no half-built aggregate to observe on that path. <paramref name="eventId"/>
    /// and <paramref name="id"/> are supplied by the caller's <c>newId</c>
    /// delegate (<see cref="OrderDespatch"/>), never minted here — the same
    /// "no ids beyond those <c>newId</c> supplies" discipline
    /// <see cref="OrderStockReservation"/> keeps (backlog 49).
    /// </summary>
    public static DespatchAdvice Create(
        UniqueId id,
        string despatchReference,
        DateTimeOffset despatchDate,
        OrderNumber orderReference,
        string companyCode,
        string retailerCode,
        IReadOnlyList<DespatchLineEntry> lines,
        UniqueId correlationId,
        UniqueId causationId,
        UniqueId eventId)
    {
        if (lines.Count == 0)
        {
            throw new EmptyDespatchLinesError(orderReference.Value);
        }

        var advice = new DespatchAdvice(id, despatchReference, despatchDate, orderReference, companyCode, retailerCode, lines);

        var fact = new OrderDespatched(
            EventId: eventId,
            AggregateId: id,
            CorrelationId: correlationId,
            CausationId: causationId,
            OccurredAt: despatchDate,
            OrderReference: orderReference,
            DespatchReference: despatchReference,
            DespatchDate: despatchDate,
            CompanyCode: companyCode,
            RetailerCode: retailerCode,
            Lines: [.. lines.Select(l => new DespatchLine(l.ProductCode, l.Units.Value))]);

        advice.Raise(fact);

        return advice;
    }
}
