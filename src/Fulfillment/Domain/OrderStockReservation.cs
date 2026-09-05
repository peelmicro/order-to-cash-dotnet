using OrderToCash.Contracts.Facts;
using OrderToCash.Fulfillment.Domain.Events;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain;

/// <summary>One requested line of a <c>stock.reserve</c> request, in domain types.</summary>
public sealed record ReserveOrderLine(string ProductCode, Quantity Units);

/// <summary>The order-scoped input to <see cref="OrderStockReservation.Reserve"/>. <see cref="CorrelationId"/> is the ORDER id (from <c>x-correlation-id</c>) — distinct from <see cref="OrderReference"/>, the human-readable business reference.</summary>
public sealed record ReserveOrderInput(OrderNumber OrderReference, string CompanyCode, string RetailerCode, IReadOnlyList<ReserveOrderLine> Lines, UniqueId CorrelationId);

/// <summary>The order-scoped input to <see cref="OrderStockReservation.Release"/>.</summary>
public sealed record ReleaseOrderInput(OrderNumber OrderReference, string Reason, UniqueId CorrelationId);

/// <summary>The time and causation this pure domain service stamps a fact with — supplied, never pulled (no <see cref="Application.Ports.IClock"/> reference here).</summary>
public sealed record StockContext(DateTimeOffset OccurredAt, UniqueId CausationId);

public enum ReserveOutcomeKind
{
    /// <summary>Every line was satisfiable — reservations were created and <see cref="StockReserved"/> is the fact.</summary>
    Reserved,

    /// <summary>At least one line was short — nothing was reserved (F3) and <see cref="StockRejected"/> is the fact.</summary>
    Rejected,

    /// <summary>No line resolved to a known stock item — there is no carrier aggregate for a fact (design.md §3.3). The application layer raises <c>NoKnownStockItemError</c>.</summary>
    NoCarrier,
}

/// <summary>The outcome of <see cref="OrderStockReservation.Reserve"/> — exactly one of the three <see cref="ReserveOutcomeKind"/> shapes.</summary>
public sealed class ReserveOrderOutcome
{
    private ReserveOrderOutcome(ReserveOutcomeKind kind, IReadOnlyList<Reservation> reservations, StockReserved? reservedFact, StockRejected? rejectedFact)
    {
        Kind = kind;
        Reservations = reservations;
        ReservedFact = reservedFact;
        RejectedFact = rejectedFact;
    }

    public ReserveOutcomeKind Kind { get; }

    public IReadOnlyList<Reservation> Reservations { get; }

    public StockReserved? ReservedFact { get; }

    public StockRejected? RejectedFact { get; }

    public static ReserveOrderOutcome Reserved(IReadOnlyList<Reservation> reservations, StockReserved fact) =>
        new(ReserveOutcomeKind.Reserved, reservations, fact, null);

    public static ReserveOrderOutcome Rejected(StockRejected fact) =>
        new(ReserveOutcomeKind.Rejected, [], null, fact);

    public static readonly ReserveOrderOutcome NoCarrier = new(ReserveOutcomeKind.NoCarrier, [], null, null);
}

public enum ReleaseOutcomeKind
{
    Released,
    AlreadyReleased,
}

/// <summary>The outcome of <see cref="OrderStockReservation.Release"/>.</summary>
public sealed class ReleaseOrderOutcome
{
    private ReleaseOrderOutcome(ReleaseOutcomeKind kind, IReadOnlyList<Reservation> released, StockReleased? fact)
    {
        Kind = kind;
        Released = released;
        Fact = fact;
    }

    public ReleaseOutcomeKind Kind { get; }

    public IReadOnlyList<Reservation> Released { get; }

    public StockReleased? Fact { get; }

    public static ReleaseOrderOutcome Create(IReadOnlyList<Reservation> released, StockReleased fact) =>
        new(ReleaseOutcomeKind.Released, released, fact);

    public static readonly ReleaseOrderOutcome AlreadyReleased = new(ReleaseOutcomeKind.AlreadyReleased, [], null);
}

/// <summary>
/// The pure domain service that makes reservation all-or-nothing ACROSS an
/// order's lines (<b>F3</b>) and builds the three facts — a home for an
/// invariant that spans multiple <see cref="StockItem"/> aggregates, which a
/// handler owning it would be the layering mistake repository-drains-
/// aggregate exists to prevent (design.md §3.3). No I/O, no clock, no ids
/// beyond those <c>newId</c> supplies — pure, like every domain method in
/// this repository.
/// </summary>
public static class OrderStockReservation
{
    /// <summary>
    /// Evaluates EVERY line against the supplied items BEFORE mutating any:
    /// a product with no item is short with <c>available: 0</c> and sets the
    /// reason to <c>unknown_product</c> (`FS8`); any other shortfall sets
    /// <c>insufficient_stock</c>; only when every line is satisfiable does it
    /// call <see cref="StockItem.Reserve"/> per line. Repeated lines naming
    /// the same product are summed in a <see langword="long"/> before being
    /// compared against <see cref="StockItem.AvailableUnits"/>, so an order
    /// with many large lines cannot wrap its own total — but each line still
    /// gets its own <see cref="Reservation"/> entity.
    /// </summary>
    public static ReserveOrderOutcome Reserve(
        IReadOnlyDictionary<string, StockItem> itemsByProductCode,
        ReserveOrderInput input,
        StockContext context,
        Func<UniqueId> newId)
    {
        // The carrier is the first REQUEST LINE (not the first product
        // group) that resolves to a known item (`FS13`).
        StockItem? carrier = null;
        foreach (var line in input.Lines)
        {
            if (itemsByProductCode.TryGetValue(line.ProductCode, out var resolvedItem))
            {
                carrier = resolvedItem;
                break;
            }
        }

        if (carrier is null)
        {
            return ReserveOrderOutcome.NoCarrier;
        }

        var requestedByProduct = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in input.Lines)
        {
            requestedByProduct[line.ProductCode] = requestedByProduct.GetValueOrDefault(line.ProductCode) + line.Units.Value;
        }

        var shortages = new List<Shortage>();
        var hasUnknownProduct = false;

        foreach (var (productCode, totalRequested) in requestedByProduct)
        {
            if (!itemsByProductCode.TryGetValue(productCode, out var item))
            {
                hasUnknownProduct = true;
                shortages.Add(new Shortage(productCode, ClampToInt(totalRequested), 0));
                continue;
            }

            if (totalRequested > item.AvailableUnits)
            {
                shortages.Add(new Shortage(productCode, ClampToInt(totalRequested), item.AvailableUnits));
            }
        }

        if (shortages.Count > 0)
        {
            var reason = hasUnknownProduct ? "unknown_product" : "insufficient_stock";

            var rejectedFact = new StockRejected(
                EventId: newId(),
                AggregateId: carrier.Id,
                CorrelationId: input.CorrelationId,
                CausationId: context.CausationId,
                OccurredAt: context.OccurredAt,
                OrderReference: input.OrderReference,
                CompanyCode: input.CompanyCode,
                RetailerCode: input.RetailerCode,
                Shortages: shortages,
                Reason: reason);

            carrier.RecordOrderFact(rejectedFact);

            return ReserveOrderOutcome.Rejected(rejectedFact);
        }

        var reservations = new List<Reservation>(input.Lines.Count);
        foreach (var line in input.Lines)
        {
            var item = itemsByProductCode[line.ProductCode];
            reservations.Add(item.Reserve(newId(), input.OrderReference, input.RetailerCode, line.Units));
        }

        var reservedFact = new StockReserved(
            EventId: newId(),
            AggregateId: carrier.Id,
            CorrelationId: input.CorrelationId,
            CausationId: context.CausationId,
            OccurredAt: context.OccurredAt,
            OrderReference: input.OrderReference,
            CompanyCode: input.CompanyCode,
            RetailerCode: input.RetailerCode,
            Reservations: [.. reservations.Select(r => new ReservationRef(r.Id.Value, r.ProductCode, r.Units.Value))]);

        carrier.RecordOrderFact(reservedFact);

        return ReserveOrderOutcome.Reserved(reservations, reservedFact);
    }

    /// <summary>
    /// Calls <see cref="StockItem.Release"/> on each item and, if the union
    /// of released reservations is non-empty, appends exactly ONE
    /// <see cref="StockReleased"/> to the carrier (the first item whose call
    /// actually released something, `FS13`); otherwise returns
    /// <see cref="ReleaseOutcomeKind.AlreadyReleased"/> and appends nothing
    /// (F5, `R34`, `FS9`). Takes <paramref name="newId"/> the same way
    /// <see cref="Reserve"/> does — backlog 49: this method used to mint the
    /// fact's <c>EventId</c> with <c>UniqueId.New()</c> directly, the one id
    /// this feature's "no ids beyond those <c>newId</c> supplies" promise
    /// did not actually keep.
    /// </summary>
    public static ReleaseOrderOutcome Release(IReadOnlyList<StockItem> items, ReleaseOrderInput input, StockContext context, Func<UniqueId> newId)
    {
        var allReleased = new List<Reservation>();
        StockItem? carrier = null;

        foreach (var item in items)
        {
            var released = item.Release(input.OrderReference);

            if (released.Count > 0)
            {
                allReleased.AddRange(released);
                carrier ??= item;
            }
        }

        if (allReleased.Count == 0 || carrier is null)
        {
            return ReleaseOrderOutcome.AlreadyReleased;
        }

        var fact = new StockReleased(
            EventId: newId(),
            AggregateId: carrier.Id,
            CorrelationId: input.CorrelationId,
            CausationId: context.CausationId,
            OccurredAt: context.OccurredAt,
            OrderReference: input.OrderReference,
            CompanyCode: carrier.CompanyCode,
            RetailerCode: allReleased[0].RetailerCode,
            Released: [.. allReleased.Select(r => new ReservationRef(r.Id.Value, r.ProductCode, r.Units.Value))],
            Reason: input.Reason);

        carrier.RecordOrderFact(fact);

        return ReleaseOrderOutcome.Create(allReleased, fact);
    }

    private static int ClampToInt(long value) => value > int.MaxValue ? int.MaxValue : (int)value;
}
