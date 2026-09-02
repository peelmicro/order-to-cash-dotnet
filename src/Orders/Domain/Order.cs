using OrderToCash.Orders.Domain.Errors;
using OrderToCash.Orders.Domain.Events;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain;

/// <summary>
/// The order lifecycle aggregate root — identity, the nine-state status
/// machine of Table T-1, its lines and their derived totals
/// (specs/shared/domain-model.md §3). This is also the saga's state: there
/// is no separate saga record (§3.1).
/// </summary>
/// <remarks>
/// Synchronous and pure: no I/O, no <c>async</c> method, no
/// <see cref="CancellationToken"/> — a domain method that awaited anything
/// would be a design error here, not compliance with CLAUDE.md's
/// "async all the way down" (design.md §0).
/// </remarks>
public sealed class Order : AggregateRoot
{
    private readonly List<OrderLine> _lines = [];

    private Order(
        UniqueId id,
        OrderNumber orderReference,
        DateTimeOffset orderDate,
        string retailerCode,
        GLN buyerGln,
        string companyCode,
        GLN supplierGln,
        string currency,
        string? notes,
        DateTimeOffset createdAt)
        : base(id)
    {
        OrderReference = orderReference;
        OrderDate = orderDate;
        RetailerCode = retailerCode;
        BuyerGln = buyerGln;
        CompanyCode = companyCode;
        SupplierGln = supplierGln;
        Currency = currency;
        Notes = notes;
        InitialAmount = Money.Zero(currency);
        InitialDiscount = Money.Zero(currency);
        TotalAmount = Money.Zero(currency);
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public OrderNumber OrderReference { get; }

    public DateTimeOffset OrderDate { get; }

    public string RetailerCode { get; }

    public GLN BuyerGln { get; }

    public string CompanyCode { get; }

    public GLN SupplierGln { get; }

    /// <summary>ISO 4217 code every line and every total shares — set once at construction and never changed (O2).</summary>
    public string Currency { get; }

    public string? Notes { get; }

    public OrderStatus Status { get; private set; }

    /// <summary>Present iff <see cref="Status"/> is <see cref="OrderStatus.Cancelled"/> (O6). Immutable once set — <see cref="OrderStatus.Cancelled"/> has no outbound edge, so a second <c>Cancel</c> is refused by the state machine before it could overwrite it.</summary>
    public CancellationReason? CancellationReason { get; private set; }

    /// <summary>Σ over lines of <c>unitPrice × quantity</c> — recomputed, never assigned (O3).</summary>
    public Money InitialAmount { get; private set; }

    /// <summary>Σ over lines of <c>lineDiscount</c> plus the order-level discount, which is always <c>Money.Zero</c> here — recomputed, never assigned (O3, design.md §4.4).</summary>
    public Money InitialDiscount { get; private set; }

    /// <summary><see cref="InitialAmount"/> minus <see cref="InitialDiscount"/> — recomputed, never assigned, never negative (O3).</summary>
    public Money TotalAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The order's lines, in the order this aggregate holds them. No caller can reach the backing store.</summary>
    public IReadOnlyList<OrderLine> Lines => _lines;

    /// <summary>
    /// T-1 row 1 — creation, not a transition: there is no <c>from</c> to
    /// look up, so this bypasses <see cref="TransitionTo"/> entirely and
    /// sets <see cref="Status"/> directly (design.md §3.2). Validates O1 (at
    /// least one line), O2 (every line's currency) and O3 (the resulting
    /// total is not negative), then raises <see cref="OrderPlaced"/>.
    /// </summary>
    public static Order Place(
        OrderNumber orderReference,
        DateTimeOffset orderDate,
        string retailerCode,
        GLN buyerGln,
        string companyCode,
        GLN supplierGln,
        string currency,
        IReadOnlyList<OrderLineRequest> lines,
        string? notes,
        DateTimeOffset occurredAt,
        UniqueId causationId)
    {
        if (lines.Count == 0)
        {
            throw new OrderMustHaveAtLeastOneLineError();
        }

        var orderLines = new List<OrderLine>(lines.Count);
        foreach (var line in lines)
        {
            EnsureLineCurrencyMatches(currency, line.UnitPrice, line.LineDiscount);
            orderLines.Add(new OrderLine(UniqueId.New(), line.ProductCode, line.Description, line.Quantity, line.UnitPrice, line.LineDiscount));
        }

        var (initialAmount, initialDiscount, totalAmount) = RecomputeTotals(orderLines, currency);
        if (totalAmount.IsNegative)
        {
            throw new OrderTotalMustNotBeNegativeError(totalAmount);
        }

        var order = new Order(UniqueId.New(), orderReference, orderDate, retailerCode, buyerGln, companyCode, supplierGln, currency, notes, createdAt: occurredAt)
        {
            InitialAmount = initialAmount,
            InitialDiscount = initialDiscount,
            TotalAmount = totalAmount,
            Status = OrderStatus.Placed,
        };
        order._lines.AddRange(orderLines);

        order.Raise(new OrderPlaced(
            EventId: UniqueId.New(),
            AggregateId: order.Id,
            CorrelationId: order.Id,
            CausationId: causationId,
            OccurredAt: occurredAt,
            OrderReference: orderReference,
            RetailerCode: retailerCode,
            CompanyCode: companyCode,
            BuyerGln: buyerGln,
            SupplierGln: supplierGln,
            Currency: currency,
            OrderDate: orderDate,
            Lines: orderLines.Select(line => new OrderPlacedLine(line.ProductCode, line.Description, line.Quantity, line.UnitPrice, line.LineDiscount)).ToList(),
            InitialAmount: initialAmount,
            InitialDiscount: initialDiscount,
            TotalAmount: totalAmount,
            Notes: notes));

        return order;
    }

    /// <summary>T-1 row 2 — silent: no fact is catalogued for this edge (O8, design.md §7.4).</summary>
    public void MarkStockReserved(DateTimeOffset occurredAt) =>
        TransitionTo(OrderStatus.StockReserved, occurredAt, buildEvent: null);

    /// <summary>T-1 row 3 — silent: no fact is catalogued for this edge (O8, design.md §7.4).</summary>
    public void ApproveCredit(DateTimeOffset occurredAt) =>
        TransitionTo(OrderStatus.CreditApproved, occurredAt, buildEvent: null);

    /// <summary>T-1 row 4 — the ORDRSP moment. Raises <see cref="OrderConfirmed"/>.</summary>
    public void Confirm(DateTimeOffset occurredAt, UniqueId causationId) =>
        TransitionTo(
            OrderStatus.Confirmed,
            occurredAt,
            buildEvent: () => new OrderConfirmed(
                UniqueId.New(),
                Id,
                Id,
                causationId,
                occurredAt,
                OrderReference,
                RetailerCode,
                CompanyCode,
                Currency,
                TotalAmount,
                ConfirmedAt: occurredAt));

    /// <summary>T-1 row 5 — silent: no fact is catalogued for this edge (O8, design.md §7.4).</summary>
    public void MarkDespatched(DateTimeOffset occurredAt) =>
        TransitionTo(OrderStatus.Despatched, occurredAt, buildEvent: null);

    /// <summary>T-1 row 6 — silent: no fact is catalogued for this edge (O8, design.md §7.4).</summary>
    public void MarkInvoiced(DateTimeOffset occurredAt) =>
        TransitionTo(OrderStatus.Invoiced, occurredAt, buildEvent: null);

    /// <summary>T-1 row 7 — silent: no fact is catalogued for this edge (O8, design.md §7.4).</summary>
    public void MarkPaid(DateTimeOffset occurredAt) =>
        TransitionTo(OrderStatus.Paid, occurredAt, buildEvent: null);

    /// <summary>T-1 row 8 — the saga closing successfully. Raises <see cref="OrderCompleted"/>.</summary>
    public void Complete(DateTimeOffset occurredAt, UniqueId causationId) =>
        TransitionTo(
            OrderStatus.Completed,
            occurredAt,
            buildEvent: () => new OrderCompleted(
                UniqueId.New(),
                Id,
                Id,
                causationId,
                occurredAt,
                OrderReference,
                RetailerCode,
                CompanyCode,
                Currency,
                TotalAmount,
                CompletedAt: occurredAt));

    /// <summary>
    /// T-1 rows 9–12, one method for all four cancel edges because they
    /// differ only in their source, never in their target or effect
    /// (design.md §3.2). Two checks precede the transition itself: the
    /// current status must have a cancel edge at all
    /// (<see cref="OrderNotCancellableError"/>), and the supplied
    /// <paramref name="reason"/> must pair with that source per Table T-1's
    /// <em>Trigger</em> column (<see cref="CancellationReasonNotApplicableError"/>,
    /// design.md §6.1). Raises <see cref="OrderCancelled"/> carrying the
    /// reason and the (defensively copied) compensation steps.
    /// </summary>
    public void Cancel(CancellationReason reason, IReadOnlyList<OrderCompensationStep> compensationSteps, DateTimeOffset occurredAt, UniqueId causationId)
    {
        if (!OrderStateMachine.IsLegal(Status, OrderStatus.Cancelled))
        {
            throw new OrderNotCancellableError(Status);
        }

        if (!IsReasonApplicable(Status, reason))
        {
            throw new CancellationReasonNotApplicableError(reason, Status);
        }

        var steps = new List<OrderCompensationStep>(compensationSteps);

        TransitionTo(
            OrderStatus.Cancelled,
            occurredAt,
            buildEvent: () => new OrderCancelled(
                UniqueId.New(),
                Id,
                Id,
                causationId,
                occurredAt,
                OrderReference,
                RetailerCode,
                CompanyCode,
                reason,
                CancelledAt: occurredAt,
                CompensationSteps: steps));

        CancellationReason = reason;
    }

    /// <summary>Appends a line. Candidate-then-commit: the freeze is checked first, then currency, then O1/O3 against a candidate list — a rejected call leaves every field untouched (design.md §4.3, §5.1).</summary>
    public UniqueId AddLine(string productCode, string? description, Quantity quantity, Money unitPrice, Money lineDiscount, DateTimeOffset occurredAt)
    {
        EnsureLinesMutable();
        EnsureLineCurrency(unitPrice, lineDiscount);

        var newLine = new OrderLine(UniqueId.New(), productCode, description, quantity, unitPrice, lineDiscount);
        var candidateLines = new List<OrderLine>(_lines) { newLine };

        CommitCandidateLines(candidateLines, occurredAt);

        return newLine.Id;
    }

    /// <summary>Removes a line, refusing to remove the last one (O1). Candidate-then-commit, freeze checked first (design.md §4.3, §5.1).</summary>
    public void RemoveLine(UniqueId lineId, DateTimeOffset occurredAt)
    {
        EnsureLinesMutable();

        var lineToRemove = _lines.FirstOrDefault(line => line.Id == lineId) ?? throw new OrderLineNotFoundError(lineId);

        var candidateLines = new List<OrderLine>(_lines);
        candidateLines.Remove(lineToRemove);

        if (candidateLines.Count == 0)
        {
            throw new OrderMustHaveAtLeastOneLineError();
        }

        CommitCandidateLines(candidateLines, occurredAt);
    }

    /// <summary>Replaces the three mutable fields of one line at once — one place where the freeze, the currency check and the recompute are applied (design.md §5.1).</summary>
    public void ChangeLine(UniqueId lineId, Quantity quantity, Money unitPrice, Money lineDiscount, DateTimeOffset occurredAt)
    {
        EnsureLinesMutable();
        EnsureLineCurrency(unitPrice, lineDiscount);

        var existingLine = _lines.FirstOrDefault(line => line.Id == lineId) ?? throw new OrderLineNotFoundError(lineId);

        var candidateLines = _lines
            .Select(line => line.Id == lineId
                ? new OrderLine(existingLine.Id, existingLine.ProductCode, existingLine.Description, quantity, unitPrice, lineDiscount)
                : line)
            .ToList();

        CommitCandidateLines(candidateLines, occurredAt);
    }

    /// <summary>
    /// Restores a state this aggregate previously produced — from persisted
    /// columns, never a legal walk. Bypasses the state machine and raises no
    /// domain event: the facts were published in the transaction that
    /// created them, and re-raising on load would republish the whole
    /// history (design.md §8.3). Totals are re-derived from
    /// <paramref name="lines"/>, never accepted as parameters, so a
    /// stored/derived drift is unrepresentable rather than merely detected
    /// (#7's OA3).
    /// </summary>
    public static Order Rehydrate(
        UniqueId id,
        OrderNumber orderReference,
        DateTimeOffset orderDate,
        string retailerCode,
        GLN buyerGln,
        string companyCode,
        GLN supplierGln,
        string currency,
        OrderStatus status,
        CancellationReason? cancellationReason,
        string? notes,
        IReadOnlyList<OrderLine> lines,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (!Enum.IsDefined(status))
        {
            throw new UnknownOrderStatusError(OrderStatuses.DescribeUndefinedValue(status));
        }

        if (lines.Count == 0)
        {
            throw new OrderMustHaveAtLeastOneLineError();
        }

        foreach (var line in lines)
        {
            EnsureLineCurrencyMatches(currency, line.UnitPrice, line.LineDiscount);
        }

        if (status == OrderStatus.Cancelled && cancellationReason is null)
        {
            throw new CancellationReasonRequiredError();
        }

        if (status != OrderStatus.Cancelled && cancellationReason is { } presentReason)
        {
            throw new CancellationReasonNotApplicableError(presentReason, status);
        }

        var orderedLines = lines.OrderBy(line => line.Id.Value).ToList();
        var (initialAmount, initialDiscount, totalAmount) = RecomputeTotals(orderedLines, currency);

        var order = new Order(id, orderReference, orderDate, retailerCode, buyerGln, companyCode, supplierGln, currency, notes, createdAt)
        {
            Status = status,
            CancellationReason = cancellationReason,
            InitialAmount = initialAmount,
            InitialDiscount = initialDiscount,
            TotalAmount = totalAmount,
            UpdatedAt = updatedAt,
        };
        order._lines.AddRange(orderedLines);

        return order;
    }

    /// <summary>
    /// The sole writer of <see cref="Status"/> for the eleven real T-1
    /// transitions (rows 2–12). Checks <see cref="OrderStateMachine.IsLegal"/>
    /// before assigning anything, before stamping <see cref="UpdatedAt"/> and
    /// before raising anything, so an illegal transition leaves the
    /// aggregate byte-identical (R9, design.md §3.1, §3.2 layer 3).
    /// </summary>
    private void TransitionTo(OrderStatus to, DateTimeOffset occurredAt, Func<OrderDomainEvent>? buildEvent)
    {
        if (!OrderStateMachine.IsLegal(Status, to))
        {
            throw new IllegalOrderTransitionError(Status, to);
        }

        Status = to;
        UpdatedAt = occurredAt;

        if (buildEvent is not null)
        {
            Raise(buildEvent());
        }
    }

    /// <summary>
    /// Table T-1's <em>Trigger</em> column, read as a reason-to-source
    /// pairing (design.md §6.1, #7's OA4). <c>operator_cancelled</c> is
    /// unrestricted; the other two each pair with exactly one source.
    /// The enum members are referenced through
    /// <see cref="global::OrderToCash.Orders.Domain.CancellationReason"/>'s
    /// full name because, inside a <see langword="static"/> member of this
    /// class, the bare identifier <c>CancellationReason</c> binds to the
    /// instance property of that name rather than the enum type (CS0120) —
    /// the same name is deliberately shared between the property and its
    /// type per the domain model's own vocabulary (domain-model.md §3.1).
    /// </summary>
    private static bool IsReasonApplicable(OrderStatus from, global::OrderToCash.Orders.Domain.CancellationReason reason) => (from, reason) switch
    {
        (OrderStatus.Placed, global::OrderToCash.Orders.Domain.CancellationReason.StockRejected) => true,
        (OrderStatus.StockReserved, global::OrderToCash.Orders.Domain.CancellationReason.CreditRejected) => true,
        (_, global::OrderToCash.Orders.Domain.CancellationReason.OperatorCancelled) => true,
        _ => false,
    };

    /// <summary>O4 — the freeze. First statement of every line-mutating method, before any structural check, so removing the last line of a <c>confirmed</c> order raises this and not the empty-order error (R7, design.md §5.2).</summary>
    private void EnsureLinesMutable()
    {
        if (Status is OrderStatus.Confirmed or OrderStatus.Despatched or OrderStatus.Invoiced or OrderStatus.Paid or OrderStatus.Completed or OrderStatus.Cancelled)
        {
            throw new OrderLinesAreFrozenError(Status);
        }
    }

    private void EnsureLineCurrency(Money unitPrice, Money lineDiscount) => EnsureLineCurrencyMatches(Currency, unitPrice, lineDiscount);

    /// <summary>O2, enforced before the candidate list is built — otherwise a mismatch would surface one step later as the shared kernel's <c>money.cross_currency</c> rather than this order invariant (R2; design.md §5.3).</summary>
    private static void EnsureLineCurrencyMatches(string currency, Money unitPrice, Money lineDiscount)
    {
        if (!string.Equals(unitPrice.Currency, currency, StringComparison.Ordinal))
        {
            throw new OrderLineCurrencyMismatchError(currency, unitPrice.Currency);
        }

        if (!string.Equals(lineDiscount.Currency, currency, StringComparison.Ordinal))
        {
            throw new OrderLineCurrencyMismatchError(currency, lineDiscount.Currency);
        }
    }

    /// <summary>Validates a candidate line list (O1, O3) and, only if it is accepted, commits it and the totals it implies, stamping <see cref="UpdatedAt"/> (design.md §4.3).</summary>
    private void CommitCandidateLines(List<OrderLine> candidateLines, DateTimeOffset occurredAt)
    {
        if (candidateLines.Count == 0)
        {
            throw new OrderMustHaveAtLeastOneLineError();
        }

        var (initialAmount, initialDiscount, totalAmount) = RecomputeTotals(candidateLines, Currency);
        if (totalAmount.IsNegative)
        {
            throw new OrderTotalMustNotBeNegativeError(totalAmount);
        }

        _lines.Clear();
        _lines.AddRange(candidateLines);
        InitialAmount = initialAmount;
        InitialDiscount = initialDiscount;
        TotalAmount = totalAmount;
        UpdatedAt = occurredAt;
    }

    /// <summary>
    /// O3's formula: <c>initialAmount = Σ(unitPrice × quantity)</c>,
    /// <c>initialDiscount = Σ(lineDiscount) + orderDiscount</c> with
    /// <c>orderDiscount</c> always <see cref="Money.Zero(string)"/> (design.md
    /// §4.4, #7's answer inherited), <c>totalAmount = initialAmount −
    /// initialDiscount</c>. Pure — takes a candidate line list rather than
    /// reading <see cref="_lines"/>, so the same routine serves both a
    /// committed recompute and a not-yet-committed candidate.
    /// </summary>
    private static (Money InitialAmount, Money InitialDiscount, Money TotalAmount) RecomputeTotals(IReadOnlyList<OrderLine> lines, string currency)
    {
        var initialAmount = Money.Zero(currency);
        var lineDiscountSum = Money.Zero(currency);

        foreach (var line in lines)
        {
            initialAmount = initialAmount.Add(line.UnitPrice.Multiply(line.Quantity));
            lineDiscountSum = lineDiscountSum.Add(line.LineDiscount);
        }

        var orderDiscount = Money.Zero(currency);
        var initialDiscount = lineDiscountSum.Add(orderDiscount);
        var totalAmount = initialAmount.Subtract(initialDiscount);

        return (initialAmount, initialDiscount, totalAmount);
    }
}
