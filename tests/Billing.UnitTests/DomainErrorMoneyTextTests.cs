using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Errors;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// Backlog id 102 (<c>problem_detail_money_reads_as_minor_units</c>) — every
/// <see cref="DomainError"/> message enumerated as reaching a Gateway
/// problem document's <c>detail</c> (review `review_timeline_money_and_stock_names.md`
/// D6, and this feature's own class enumeration) renders its amount(s)
/// with the shared money-text formatter (id 100, <see cref="MoneyText"/>),
/// never as a raw minor-units integer. Whole-string assertions only, the
/// same discipline id 100's own tests use.
/// </summary>
public sealed class DomainErrorMoneyTextTests
{
    // -- Constructor-formatted errors (the class builds its own message) ----

    [Fact]
    public void InvoicePaymentAmountMismatchError_RendersBothAmountsScaledByTheCurrencysExponent()
    {
        var error = new InvoicePaymentAmountMismatchError(expectedMinorUnits: 12000, receivedMinorUnits: 9245, currency: "EUR");

        Assert.Equal("Payment amount 92.45 EUR does not equal the invoice's totalAmount 120.00 EUR.", error.Message);
        Assert.Equal(12000, error.ExpectedMinorUnits);
        Assert.Equal(9245, error.ReceivedMinorUnits);
    }

    [Fact]
    public void NegativeInvoiceTotalError_RendersBothAmountsScaledByTheCurrencysExponent()
    {
        var error = new NegativeInvoiceTotalError(amountMinorUnits: 2000, discountMinorUnits: 5000, currency: "EUR");

        Assert.Equal("totalAmount would be negative: amount 20.00 EUR minus discount 50.00 EUR.", error.Message);
    }

    [Fact]
    public void CreditReleaseUnderflowError_RendersBothAmountsScaledByTheCurrencysExponent()
    {
        var error = new CreditReleaseUnderflowError("ORD-000001", outstandingMinorUnits: 500, requestedMinorUnits: 600, currency: "GBP");

        Assert.Equal("Order 'ORD-000001': releasing 6.00 GBP would drive exposure below zero (outstanding 5.00 GBP).", error.Message);
    }

    [Fact]
    public void CreditRefusalMismatchError_RendersBothAmountsScaledByTheCurrencysExponent()
    {
        var error = new CreditRefusalMismatchError(requestedMinorUnits: 1000, availableMinorUnits: 2000, currency: "EUR");

        Assert.Equal("Refusal reason 'over_limit' claimed for a request (10.00 EUR) that fits within the available credit (20.00 EUR).", error.Message);
    }

    [Fact]
    public void CreditLimitExceededError_RendersBothAmountsScaledByTheCurrencysExponent()
    {
        var error = new CreditLimitExceededError(requestedMinorUnits: 12345, availableMinorUnits: 500, currency: "BHD");

        Assert.Equal("Requested amount 12.345 BHD exceeds available credit 0.500 BHD.", error.Message);
    }

    // -- Reason-string errors (the CALL SITE builds the message) -----------

    [Fact]
    public void CreditLedgerEntry_Create_RefusesANonPositiveAmount_RenderedScaledByTheCurrencysExponent()
    {
        var error = Assert.Throws<InvalidBuyerCreditSnapshotError>(
            () => CreditLedgerEntry.Create(UniqueId.New(), OrderNumber.Parse("ORD-000001"), new Money(0, "JPY"), CreditEntryType.Hold, DateTimeOffset.UtcNow));

        Assert.Equal("a hold entry's amount must be strictly positive; got 0 JPY.", error.Message);
    }

    [Fact]
    public void BuyerCredit_Reconstitute_RefusesAnOverLimitSnapshot_RenderedScaledByTheCurrencysExponent()
    {
        var overLimitSnapshot = new BuyerCreditSnapshot(UniqueId.New(), "CR-000001", "CarrefourEs", "IBERFOODS", new Money(1_000, "EUR"), 1_001, []);

        var error = Assert.Throws<InvalidBuyerCreditSnapshotError>(() => BuyerCredit.Reconstitute(overLimitSnapshot));

        Assert.Equal("credit line 'CR-000001': committed exposure (10.01 EUR) already exceeds the credit limit (10.00 EUR) — invariant B1.", error.Message);
    }

    [Fact]
    public void Invoice_Reconstitute_RefusesAStoredAmountDisagreeingWithTheLinesOwnTotal_RenderedScaledByTheCurrencysExponent()
    {
        var lineSnapshot = new InvoiceLineSnapshot(UniqueId.New(), "SKU-1", new Quantity(1), new Money(1_000, "EUR"));
        var snapshot = new InvoiceSnapshot(
            UniqueId.New(),
            "INV-000001",
            DateTimeOffset.UtcNow,
            OrderNumber.Parse("ORD-000001"),
            "CarrefourEs",
            "IBERFOODS",
            "EUR",
            new Money(9999, "EUR"), // disagrees with the line total (1_000)
            new Money(0, "EUR"),
            new Money(9999, "EUR"),
            new InvoiceState.Issued(),
            [lineSnapshot]);

        var error = Assert.Throws<InvalidInvoiceSnapshotError>(() => Invoice.Reconstitute(snapshot));

        Assert.Equal("invoice 'INV-000001': stored amount 99.99 EUR disagrees with the lines' own total 10.00 EUR — invariant B6.", error.Message);
    }
}
