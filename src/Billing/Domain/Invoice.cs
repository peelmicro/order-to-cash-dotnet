using OrderToCash.Billing.Domain.Errors;
using OrderToCash.Billing.Domain.Events;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain;

/// <summary>The time and causation this aggregate stamps a fact with — supplied, never pulled (no <c>IClock</c> reference here, mirrors <c>CreditContext</c>).</summary>
public readonly record struct InvoiceContext(DateTimeOffset OccurredAt, UniqueId CausationId);

/// <summary>One requested line of an <c>invoice.issue</c> request — the input <see cref="Invoice.Issue"/> mirrors into an <see cref="InvoiceLine"/> exactly (`asyncapi.yaml`: "the invoice mirrors them exactly").</summary>
public sealed record InvoiceLineInput(string ProductCode, Quantity Units, Money UnitPrice);

/// <summary>The order-scoped input to <see cref="Invoice.Issue"/>. <see cref="CorrelationId"/> is the ORDER id (from <c>x-correlation-id</c>), distinct from <see cref="OrderReference"/>, the human-readable business reference — mirrors <c>HoldRequest</c>.</summary>
public sealed record IssueInvoiceInput(
    UniqueId Id,
    string InvoiceReference,
    OrderNumber OrderReference,
    string RetailerCode,
    string CompanyCode,
    IReadOnlyList<InvoiceLineInput> Lines,
    Money Discount,
    UniqueId CorrelationId);

/// <summary>The input to <see cref="Invoice.MarkPaid"/> — feature 22's seam. Delivered and unit-tested here; uncalled until `billing.payment.register` exists.</summary>
public sealed record MarkPaidInput(
    string PaymentReference,
    Money Amount,
    DateTimeOffset ValueDate,
    string Source,
    UniqueId CorrelationId);

/// <summary>
/// The aggregate root for one invoice — one row of `invoices` plus its
/// `invoice_items` lines (`domain-model.md` §5.2, §5.3). <see cref="Issue"/>
/// is the only way an <see cref="Invoice"/> comes into being: it derives the
/// totals, refuses every <b>B6</b> violation, and raises exactly one
/// <see cref="InvoiceIssued"/> BEFORE returning, so a caller can never
/// observe an invoice whose fact was not recorded (the
/// <c>OrderDespatch.Create</c> precedent). There is no <c>Cancel</c>, no
/// <c>Void</c>, no <c>CreditNote</c> — `domain-model.md` §5.3 gives the
/// invoice exactly one edge (<c>issued → paid</c>), and every other method a
/// reader might expect is deliberately absent.
/// </summary>
public sealed class Invoice : AggregateRoot
{
    private readonly List<InvoiceLine> _lines = [];

    // ONE field. Status and PaidAt below are read-only projections of it —
    // there is no code path that can write one without the other, because
    // there is nothing separate to write (design.md §3.2, ledger L4).
    private InvoiceState _state;

    private Invoice(
        UniqueId id,
        string invoiceReference,
        DateTimeOffset invoiceDate,
        OrderNumber orderReference,
        string retailerCode,
        string companyCode,
        string currency,
        Money amount,
        Money discount,
        Money totalAmount,
        InvoiceState state)
        : base(id)
    {
        InvoiceReference = invoiceReference;
        InvoiceDate = invoiceDate;
        OrderReference = orderReference;
        RetailerCode = retailerCode;
        CompanyCode = companyCode;
        Currency = currency;
        Amount = amount;
        Discount = discount;
        TotalAmount = totalAmount;
        _state = state;
    }

    public string InvoiceReference { get; }

    public DateTimeOffset InvoiceDate { get; }

    public OrderNumber OrderReference { get; }

    public string RetailerCode { get; }

    public string CompanyCode { get; }

    public string Currency { get; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;

    public Money Amount { get; }

    public Money Discount { get; }

    public Money TotalAmount { get; }

    /// <summary>Projection of <see cref="_state"/> — never independently assigned.</summary>
    public string Status => InvoiceStatuses.ToToken(_state);

    /// <summary>Projection of <see cref="_state"/> — <see langword="null"/> iff <see cref="Status"/> is `issued` (invariant <b>B9</b>).</summary>
    public DateTimeOffset? PaidAt => _state.PaidAtOrNull;

    /// <summary>
    /// The only construction site for a brand-new invoice (`R45`, `BI11`).
    /// Derives <see cref="Amount"/>/<see cref="TotalAmount"/> inside an
    /// explicit `checked` region, converting an overflow to
    /// <see cref="InvoiceTotalOverflowError"/> rather than letting an
    /// unhandled <see cref="OverflowException"/> reach the responder
    /// (`BI25`) — the exact shape <c>CreditExposure.Summarise</c> already
    /// ships. Refuses an empty line list, a foreign line currency and a
    /// negative total (<b>B6</b>). Raises EXACTLY ONE <see cref="InvoiceIssued"/>
    /// before returning.
    /// </summary>
    public static Invoice Issue(IssueInvoiceInput input, InvoiceContext ctx, Func<UniqueId> newId)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(newId);

        if (input.Lines.Count == 0)
        {
            throw new EmptyInvoiceLinesError(input.OrderReference.Value);
        }

        var currency = input.Discount.Currency;

        var lines = new List<InvoiceLine>(input.Lines.Count);
        foreach (var lineInput in input.Lines)
        {
            if (!string.Equals(lineInput.UnitPrice.Currency, currency, StringComparison.Ordinal))
            {
                throw new InvoiceLineCurrencyMismatchError(currency, lineInput.UnitPrice.Currency);
            }

            lines.Add(InvoiceLine.Create(newId(), lineInput.ProductCode, lineInput.Units, lineInput.UnitPrice));
        }

        Money amount;
        Money totalAmount;
        try
        {
            checked
            {
                var amountMinorUnits = 0L;
                foreach (var line in lines)
                {
                    amountMinorUnits = checked(amountMinorUnits + line.LineTotal.MinorUnits);
                }

                amount = new Money(amountMinorUnits, currency);
                totalAmount = amount.Subtract(input.Discount);
            }
        }
        catch (OverflowException ex)
        {
            throw new InvoiceTotalOverflowError(ex);
        }

        if (totalAmount.IsNegative)
        {
            throw new NegativeInvoiceTotalError(amount.MinorUnits, input.Discount.MinorUnits);
        }

        var invoice = new Invoice(
            input.Id,
            input.InvoiceReference,
            ctx.OccurredAt,
            input.OrderReference,
            input.RetailerCode,
            input.CompanyCode,
            currency,
            amount,
            input.Discount,
            totalAmount,
            new InvoiceState.Issued());
        invoice._lines.AddRange(lines);

        invoice.Raise(new InvoiceIssued(
            EventId: newId(),
            AggregateId: invoice.Id,
            CorrelationId: input.CorrelationId,
            CausationId: ctx.CausationId,
            OccurredAt: ctx.OccurredAt,
            OrderReference: invoice.OrderReference,
            InvoiceReference: invoice.InvoiceReference,
            InvoiceDate: invoice.InvoiceDate,
            RetailerCode: invoice.RetailerCode,
            CompanyCode: invoice.CompanyCode,
            Currency: invoice.Currency,
            Lines: invoice.Lines,
            Amount: invoice.Amount,
            Discount: invoice.Discount,
            TotalAmount: invoice.TotalAmount));

        return invoice;
    }

    /// <summary>
    /// Restores a persisted invoice. Refuses a `status`/`paid_at`
    /// disagreement, disagreeing totals and a foreign line currency —
    /// <see cref="InvalidInvoiceSnapshotError"/> (<b>B6</b>, <b>B9</b>,
    /// `BI10`). Does not re-derive `PaidAt`/`Status` from anything other
    /// than <paramref name="snapshot"/>'s own already-parsed
    /// <see cref="InvoiceState"/> — the row mapper is the only place a raw
    /// `status`/`paid_at` pair becomes one.
    /// </summary>
    public static Invoice Reconstitute(InvoiceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // B9: a stored 'paid' row with a null paid_at parses to
        // DateTimeOffset.MinValue (InvoiceStatuses.Parse) — detect that
        // disagreement here, the aggregate's own invariant, rather than in
        // the token mapper.
        if (snapshot.State is InvoiceState.Paid paid && paid.PaidAt == DateTimeOffset.MinValue)
        {
            throw new InvalidInvoiceSnapshotError(
                $"invoice '{snapshot.InvoiceReference}': status is 'paid' but paid_at is NULL — invariant B9.");
        }

        if (snapshot.Lines.Count == 0)
        {
            throw new InvalidInvoiceSnapshotError($"invoice '{snapshot.InvoiceReference}': has no lines — invariant B6.");
        }

        foreach (var lineSnapshot in snapshot.Lines)
        {
            if (!string.Equals(lineSnapshot.UnitPrice.Currency, snapshot.Currency, StringComparison.Ordinal))
            {
                throw new InvalidInvoiceSnapshotError(
                    $"invoice '{snapshot.InvoiceReference}': line '{lineSnapshot.Id}' currency '{lineSnapshot.UnitPrice.Currency}' differs from the invoice's currency '{snapshot.Currency}' — invariant B6.");
            }
        }

        var recomputedAmount = 0L;
        try
        {
            checked
            {
                foreach (var lineSnapshot in snapshot.Lines)
                {
                    var lineTotal = lineSnapshot.UnitPrice.Multiply(lineSnapshot.Units);
                    recomputedAmount = checked(recomputedAmount + lineTotal.MinorUnits);
                }
            }
        }
        catch (OverflowException ex)
        {
            throw new InvoiceTotalOverflowError(ex);
        }

        if (recomputedAmount != snapshot.Amount.MinorUnits)
        {
            throw new InvalidInvoiceSnapshotError(
                $"invoice '{snapshot.InvoiceReference}': stored amount {snapshot.Amount.MinorUnits} disagrees with the lines' own total {recomputedAmount} — invariant B6.");
        }

        var recomputedTotal = snapshot.Amount.Subtract(snapshot.Discount);
        if (recomputedTotal.MinorUnits != snapshot.TotalAmount.MinorUnits)
        {
            throw new InvalidInvoiceSnapshotError(
                $"invoice '{snapshot.InvoiceReference}': stored totalAmount {snapshot.TotalAmount.MinorUnits} disagrees with amount − discount {recomputedTotal.MinorUnits} — invariant B6.");
        }

        if (recomputedTotal.IsNegative)
        {
            throw new InvalidInvoiceSnapshotError(
                $"invoice '{snapshot.InvoiceReference}': totalAmount {recomputedTotal.MinorUnits} is negative — invariant B6.");
        }

        var invoice = new Invoice(
            snapshot.Id,
            snapshot.InvoiceReference,
            snapshot.InvoiceDate,
            snapshot.OrderReference,
            snapshot.RetailerCode,
            snapshot.CompanyCode,
            snapshot.Currency,
            snapshot.Amount,
            snapshot.Discount,
            snapshot.TotalAmount,
            snapshot.State);

        foreach (var lineSnapshot in snapshot.Lines)
        {
            invoice._lines.Add(InvoiceLine.Reconstitute(lineSnapshot));
        }

        return invoice;
    }

    /// <summary>
    /// Moves `issued` to `paid`, sets `paidAt` in the SAME indivisible
    /// assignment, and raises exactly one <see cref="PaymentReceived"/>
    /// (<b>B8</b>, <b>B9</b>, <b>B10</b>). Refuses a second payment
    /// (<see cref="InvoiceAlreadyPaidError"/>), a mismatched amount
    /// (<see cref="InvoicePaymentAmountMismatchError"/>) and a mismatched
    /// currency (<see cref="InvoicePaymentCurrencyMismatchError"/>) —
    /// every refusal leaves <see cref="_state"/> and the event list
    /// untouched.
    /// </summary>
    /// <returns>
    /// The <c>eventId</c> of the <see cref="PaymentReceived"/> fact just
    /// appended — backlog id 57 (ported from #7's commit <c>bf59af9</c>):
    /// <c>PaymentRegisterService</c> uses this value as the
    /// <c>causationId</c> of the <c>credit.released.v1</c> fact it triggers
    /// next, so the release's cause is recorded as the payment that
    /// produced it (`R47`'s own "in that order" wording) rather than the
    /// two facts sharing the REQUEST's id as siblings with no causal edge
    /// between them.
    /// </returns>
    public UniqueId MarkPaid(MarkPaidInput input, InvoiceContext ctx, Func<UniqueId> newId)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(newId);

        if (_state is InvoiceState.Paid)
        {
            throw new InvoiceAlreadyPaidError(InvoiceReference);
        }

        if (!string.Equals(input.Amount.Currency, Currency, StringComparison.Ordinal))
        {
            throw new InvoicePaymentCurrencyMismatchError(Currency, input.Amount.Currency);
        }

        if (input.Amount.MinorUnits != TotalAmount.MinorUnits)
        {
            throw new InvoicePaymentAmountMismatchError(TotalAmount.MinorUnits, input.Amount.MinorUnits);
        }

        _state = new InvoiceState.Paid(ctx.OccurredAt);

        var fact = new PaymentReceived(
            EventId: newId(),
            AggregateId: Id,
            CorrelationId: input.CorrelationId,
            CausationId: ctx.CausationId,
            OccurredAt: ctx.OccurredAt,
            OrderReference: OrderReference,
            InvoiceReference: InvoiceReference,
            PaymentReference: input.PaymentReference,
            Amount: input.Amount,
            ValueDate: input.ValueDate,
            Source: input.Source);

        Raise(fact);

        return fact.EventId;
    }

    public InvoiceSnapshot ToSnapshot() => new(
        Id,
        InvoiceReference,
        InvoiceDate,
        OrderReference,
        RetailerCode,
        CompanyCode,
        Currency,
        Amount,
        Discount,
        TotalAmount,
        _state,
        [.. _lines.Select(l => l.ToSnapshot())]);
}
