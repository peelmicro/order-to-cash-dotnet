using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain;

/// <summary>The order-scoped input to <see cref="OrderDespatch.Create"/>. <see cref="CorrelationId"/> is the ORDER id (from <c>x-correlation-id</c>) — distinct from <see cref="OrderReference"/>, the human-readable business reference (mirrors <see cref="ReserveOrderInput"/>).</summary>
public sealed record DespatchOrderInput(OrderNumber OrderReference, UniqueId CorrelationId);

public enum DespatchOutcomeKind
{
    /// <summary>At least one reservation was consumed — a <see cref="DespatchAdvice"/> was created and <see cref="DespatchAdvice"/>'s own fact is on its event list.</summary>
    Created,

    /// <summary>Nothing was consumed. A DEFENSIVE, expected-unreachable branch — the caller (<c>Application.DespatchCreationService</c>) decides the real R36 refusal BEFORE calling this, from the state of the order's reservations under lock, which is something this pure function — given only the items it was handed — cannot itself tell apart from an F8 idempotent repeat.</summary>
    NoReservations,
}

/// <summary>The outcome of <see cref="OrderDespatch.Create"/>.</summary>
public sealed class DespatchOrderOutcome
{
    private DespatchOrderOutcome(DespatchOutcomeKind kind, DespatchAdvice? advice)
    {
        Kind = kind;
        Advice = advice;
    }

    public DespatchOutcomeKind Kind { get; }

    public DespatchAdvice? Advice { get; }

    public static DespatchOrderOutcome Created(DespatchAdvice advice) => new(DespatchOutcomeKind.Created, advice);

    public static readonly DespatchOrderOutcome NoReservations = new(DespatchOutcomeKind.NoReservations, null);
}

/// <summary>
/// The pure domain service that consumes an order's reservations across its
/// items and creates the one <see cref="DespatchAdvice"/> that results
/// (`R36`, F6/F7/F8's creation half) — the sibling
/// <see cref="OrderStockReservation"/> never got in feature 17, deliberately
/// left for this feature (specs/fulfillment_stock/design.md §16). No I/O, no
/// clock, no ids beyond those <paramref name="newId"/> supplies — pure, like
/// every domain method in this repository.
/// </summary>
public static class OrderDespatch
{
    /// <summary>
    /// Consumes every <c>reserved</c> reservation of the order across the
    /// supplied items (<see cref="StockItem.Consume"/> — moves each to
    /// <c>consumed</c>, subtracting its units from BOTH counters), collects
    /// one <see cref="DespatchLineEntry"/> per consumed reservation (F7 —
    /// 1:1, never merged, mirroring <c>stockReservedEvent</c>'s
    /// <c>reservations[]</c> shape), and — if anything was consumed — creates
    /// exactly one <see cref="DespatchAdvice"/> (F6, F8). Both
    /// <see cref="DespatchAdvice.CompanyCode"/> and
    /// <see cref="DespatchAdvice.RetailerCode"/> are read from the SAME
    /// consumed reservation (the first one) — deliberately, so the invariant
    /// "the fact's parties come from the reservations being consumed" is
    /// self-evident rather than requiring the reader to reconstruct it
    /// (inherits #7's review finding N2 as prevention, rather than
    /// reproducing the asymmetry that finding flagged).
    /// </summary>
    public static DespatchOrderOutcome Create(
        IReadOnlyList<StockItem> items,
        DespatchOrderInput input,
        string despatchReference,
        StockContext context,
        Func<UniqueId> newId)
    {
        var consumed = new List<Reservation>();
        foreach (var item in items)
        {
            consumed.AddRange(item.Consume(input.OrderReference));
        }

        if (consumed.Count == 0)
        {
            return DespatchOrderOutcome.NoReservations;
        }

        var lines = consumed.Select(r => new DespatchLineEntry(r.ProductCode, r.Units)).ToList();
        var first = consumed[0];

        var advice = DespatchAdvice.Create(
            newId(),
            despatchReference,
            context.OccurredAt,
            input.OrderReference,
            first.CompanyCode,
            first.RetailerCode,
            lines,
            input.CorrelationId,
            context.CausationId,
            newId());

        return DespatchOrderOutcome.Created(advice);
    }
}
