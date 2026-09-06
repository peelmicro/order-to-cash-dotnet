using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Errors;
using OrderToCash.Billing.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>`R45`/`R46` domain halves, `BI10`, `BI11`, `BI14`, `BI25` — the shared matrix's case names are reproduced VERBATIM (`specs/shared/test-matrix.md` §6).</summary>
public sealed class InvoiceTests
{
    private static readonly InvoiceContext _ctx = new(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero), UniqueId.New());

    private static IssueInvoiceInput BuildInput(
        IReadOnlyList<InvoiceLineInput>? lines = null,
        long discount = 0,
        string currency = "EUR",
        string orderReference = "ORD-000001")
    {
        lines ??= [new InvoiceLineInput("SKU-1", new Quantity(2), new Money(1_000, currency))];
        return new IssueInvoiceInput(
            UniqueId.New(),
            "INV-000001",
            OrderNumber.Parse(orderReference),
            "CarrefourEs",
            "IBERFOODS",
            lines,
            new Money(discount, currency),
            UniqueId.New());
    }

    [Fact]
    public void R45_CreatesExactlyOneIssuedInvoiceMirroringTheDespatchedLinesWithANonNegativeTotal_TheAggregateHalf()
    {
        var lines = new List<InvoiceLineInput>
        {
            new("SKU-1", new Quantity(3), new Money(4_000, "EUR")),
            new("SKU-2", new Quantity(1), new Money(2_500, "EUR")),
        };
        var input = BuildInput(lines, discount: 500);

        var invoice = Invoice.Issue(input, _ctx, UniqueId.New);

        Assert.Equal("issued", invoice.Status);
        Assert.Null(invoice.PaidAt);
        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal("SKU-1", invoice.Lines[0].ProductCode);
        Assert.Equal(3, invoice.Lines[0].Units.Value);
        Assert.Equal(4_000, invoice.Lines[0].UnitPrice.MinorUnits);
        Assert.Equal("SKU-2", invoice.Lines[1].ProductCode);
        Assert.Equal(1, invoice.Lines[1].Units.Value);
        Assert.Equal(2_500, invoice.Lines[1].UnitPrice.MinorUnits);

        // amount = 3*4000 + 1*2500 = 14500; total = 14500 - 500 = 14000.
        Assert.Equal(14_500, invoice.Amount.MinorUnits);
        Assert.Equal(500, invoice.Discount.MinorUnits);
        Assert.Equal(14_000, invoice.TotalAmount.MinorUnits);
        Assert.False(invoice.TotalAmount.IsNegative);

        // Exactly one raised fact — the count, not merely "at least one".
        var domainEvent = Assert.Single(invoice.DomainEvents);
        var issued = Assert.IsType<InvoiceIssued>(domainEvent);
        Assert.Equal(invoice.Id, issued.AggregateId);
    }

    [Fact]
    public void BI11_DerivesAmountAndTotalAmountFromTheLines_AndRefusesAnEmptyLineListAForeignLineCurrencyAndANegativeTotal()
    {
        Assert.Throws<EmptyInvoiceLinesError>(() => Invoice.Issue(BuildInput(lines: []), _ctx, UniqueId.New));

        var foreignCurrencyLines = new List<InvoiceLineInput> { new("SKU-1", new Quantity(1), new Money(1_000, "GBP")) };
        Assert.Throws<InvoiceLineCurrencyMismatchError>(() => Invoice.Issue(BuildInput(foreignCurrencyLines), _ctx, UniqueId.New));

        var negativeTotalInput = BuildInput(discount: 5_000); // amount = 2*1000 = 2000 < discount
        Assert.Throws<NegativeInvoiceTotalError>(() => Invoice.Issue(negativeTotalInput, _ctx, UniqueId.New));

        // No setter exists for Amount/Discount/TotalAmount — reflection guard.
        var invoiceType = typeof(Invoice);
        Assert.Null(invoiceType.GetProperty("Amount")!.SetMethod);
        Assert.Null(invoiceType.GetProperty("Discount")!.SetMethod);
        Assert.Null(invoiceType.GetProperty("TotalAmount")!.SetMethod);
        Assert.Null(invoiceType.GetProperty("Status")!.SetMethod);
        Assert.Null(invoiceType.GetProperty("PaidAt")!.SetMethod);
    }

    [Fact]
    public void BI10_CarriesPaidAtAndThePaidStatusAsOneIndivisibleValue_AndRefusesToReconstituteARowWhereTheTwoDisagree()
    {
        var invoice = Invoice.Issue(BuildInput(), _ctx, UniqueId.New);
        var goodSnapshot = invoice.ToSnapshot();

        // Direction 1: status 'paid' with a null instant.
        var paidWithNoInstant = goodSnapshot with { State = new InvoiceState.Paid(DateTimeOffset.MinValue) };
        Assert.Throws<InvalidInvoiceSnapshotError>(() => Invoice.Reconstitute(paidWithNoInstant));

        // Direction 2 is unrepresentable by construction: InvoiceState.Issued
        // carries no instant field at all, so there is no "issued with an
        // instant" value to construct — the property this hierarchy exists
        // to guarantee (design.md §3.2). Confirmed structurally instead.
        Assert.Null(new InvoiceState.Issued().PaidAtOrNull);
    }

    [Fact]
    public void R46_AllowsOnlyTheTransitionFromIssuedToPaid_SetsPaidAtExactlyThen_AndRaisesOnEveryOtherTransitionChangingAndEmittingNothing()
    {
        var invoice = Invoice.Issue(BuildInput(), _ctx, UniqueId.New);
        invoice.ClearDomainEvents();

        var paidInstant = new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);
        // Deliberately DISTINCT from paidInstant/OccurredAt, so a corruption of the
        // raise site's ValueDate/Source wiring cannot hide behind a value the fact
        // would also carry for an unrelated reason (CLAUDE.md's "a corruption probe
        // only bites on a field whose expected value the test supplied" — D1).
        var valueDate = new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        const string source = "bank-file-import";
        var paidCtx = new InvoiceContext(paidInstant, UniqueId.New());
        var markPaidInput = new MarkPaidInput("PMT-000001", invoice.TotalAmount, valueDate, source, UniqueId.New());

        invoice.MarkPaid(markPaidInput, paidCtx, UniqueId.New);

        Assert.Equal("paid", invoice.Status);
        Assert.Equal(paidInstant, invoice.PaidAt);
        var fact = Assert.IsType<PaymentReceived>(Assert.Single(invoice.DomainEvents));

        // Every field BI14 names — "the payment's own reference, amount, currency,
        // value date and source" — asserted against values THIS TEST supplied,
        // plus the two identifiers the raise site must carry from the aggregate
        // rather than the input (D1).
        Assert.Equal("PMT-000001", fact.PaymentReference);
        Assert.Equal(markPaidInput.Amount.MinorUnits, fact.Amount.MinorUnits);
        Assert.Equal(markPaidInput.Amount.Currency, fact.Amount.Currency);
        Assert.Equal(valueDate, fact.ValueDate);
        Assert.Equal(source, fact.Source);
        Assert.Equal(invoice.OrderReference, fact.OrderReference);
        Assert.Equal(invoice.InvoiceReference, fact.InvoiceReference);

        // Every other transition — a second payment — refuses, changing
        // nothing and emitting nothing.
        invoice.ClearDomainEvents();
        Assert.Throws<InvoiceAlreadyPaidError>(() => invoice.MarkPaid(markPaidInput, paidCtx, UniqueId.New));
        Assert.Equal("paid", invoice.Status);
        Assert.Equal(paidInstant, invoice.PaidAt);
        Assert.Empty(invoice.DomainEvents);
    }

    [Fact]
    public void BI14_MovesIssuedToPaidSettingPaidAtInTheSameStepAndAppendingExactlyOnePaymentReceived_AndRefusesASecondPaymentAMismatchedAmountAndAMismatchedCurrency()
    {
        var mismatchedAmountInvoice = Invoice.Issue(BuildInput(), _ctx, UniqueId.New);
        mismatchedAmountInvoice.ClearDomainEvents();
        var wrongAmount = new MarkPaidInput("PMT-A", new Money(mismatchedAmountInvoice.TotalAmount.MinorUnits + 1, "EUR"), DateTimeOffset.UtcNow, "robot", UniqueId.New());
        Assert.Throws<InvoicePaymentAmountMismatchError>(() => mismatchedAmountInvoice.MarkPaid(wrongAmount, _ctx, UniqueId.New));
        Assert.Equal("issued", mismatchedAmountInvoice.Status);
        Assert.Empty(mismatchedAmountInvoice.DomainEvents);

        var mismatchedCurrencyInvoice = Invoice.Issue(BuildInput(), _ctx, UniqueId.New);
        mismatchedCurrencyInvoice.ClearDomainEvents();
        var wrongCurrency = new MarkPaidInput("PMT-B", new Money(mismatchedCurrencyInvoice.TotalAmount.MinorUnits, "GBP"), DateTimeOffset.UtcNow, "robot", UniqueId.New());
        Assert.Throws<InvoicePaymentCurrencyMismatchError>(() => mismatchedCurrencyInvoice.MarkPaid(wrongCurrency, _ctx, UniqueId.New));
        Assert.Equal("issued", mismatchedCurrencyInvoice.Status);
        Assert.Empty(mismatchedCurrencyInvoice.DomainEvents);

        var alreadyPaidInvoice = Invoice.Issue(BuildInput(), _ctx, UniqueId.New);
        var goodPayment = new MarkPaidInput("PMT-C", alreadyPaidInvoice.TotalAmount, DateTimeOffset.UtcNow, "robot", UniqueId.New());
        alreadyPaidInvoice.MarkPaid(goodPayment, _ctx, UniqueId.New);
        alreadyPaidInvoice.ClearDomainEvents();
        Assert.Throws<InvoiceAlreadyPaidError>(() => alreadyPaidInvoice.MarkPaid(goodPayment, _ctx, UniqueId.New));
        Assert.Empty(alreadyPaidInvoice.DomainEvents);
    }

    /// <summary>ARM (`B8`): remove the `checked`/`catch` conversion in `Invoice.Issue` and confirm this fails with an `OverflowException` rather than the domain error.</summary>
    [Fact]
    public void BI25_RaisesInvoiceTotalOverflowErrorWithItsStableCodeRatherThanAnOverflowException_WhenTheLineTotalsExceedTheMoneyRange()
    {
        var huge = long.MaxValue / 2 + 1;
        var lines = new List<InvoiceLineInput>
        {
            new("SKU-1", new Quantity(1), new Money(huge, "EUR")),
            new("SKU-2", new Quantity(1), new Money(huge, "EUR")),
        };
        var input = BuildInput(lines);

        var error = Assert.Throws<InvoiceTotalOverflowError>(() => Invoice.Issue(input, _ctx, UniqueId.New));
        Assert.Equal("INVOICE_TOTAL_OVERFLOW", error.Code);
    }
}
