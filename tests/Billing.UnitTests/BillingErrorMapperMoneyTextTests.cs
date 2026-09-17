using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Errors;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// Backlog id 102, fix round 1 (review `review_timeline_money_and_stock_names.md`,
/// defect E1). <see cref="DomainErrorMoneyTextTests"/> proves each fixed
/// site's own <c>.Message</c> is rendered with <see cref="MoneyText"/>; it
/// never calls <see cref="BillingErrorMapper"/>. The reviewer's own arm
/// (Q2) showed that gap is real: reverting
/// <c>BillingErrorMapper.cs</c>'s <see cref="InvoicePaymentAmountMismatchError"/>
/// case to rebuild a raw-minor-units message left <c>Billing.UnitTests</c>
/// fully green, because nothing drove a REAL domain error through the REAL
/// mapper and asserted the whole wire <c>message</c>. These 8 cases — one
/// per fixed Billing site (id 102's own enumeration table) — close that:
/// each constructs the real error and calls
/// <see cref="BillingErrorMapper.Map"/> directly, asserting the exact wire
/// string.
/// </summary>
public sealed class BillingErrorMapperMoneyTextTests
{
    private static readonly DateTimeOffset _occurredAt = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InvoicePaymentAmountMismatchError_ReachesTheWireMessage_ScaledByTheCurrencysExponent()
    {
        var error = new InvoicePaymentAmountMismatchError(expectedMinorUnits: 12000, receivedMinorUnits: 9245, currency: "EUR");

        var reply = BillingErrorMapper.Map(error, _occurredAt);

        Assert.Equal("Payment amount 92.45 EUR does not equal the invoice's totalAmount 120.00 EUR.", reply.Message);
    }

    [Fact]
    public void NegativeInvoiceTotalError_ReachesTheWireMessage_ScaledByTheCurrencysExponent()
    {
        var error = new NegativeInvoiceTotalError(amountMinorUnits: 2000, discountMinorUnits: 5000, currency: "EUR");

        var reply = BillingErrorMapper.Map(error, _occurredAt);

        Assert.Equal("totalAmount would be negative: amount 20.00 EUR minus discount 50.00 EUR.", reply.Message);
    }

    [Fact]
    public void CreditReleaseUnderflowError_ReachesTheWireMessage_ScaledByTheCurrencysExponent()
    {
        var error = new CreditReleaseUnderflowError("ORD-000001", outstandingMinorUnits: 500, requestedMinorUnits: 600, currency: "GBP");

        var reply = BillingErrorMapper.Map(error, _occurredAt);

        Assert.Equal("Order 'ORD-000001': releasing 6.00 GBP would drive exposure below zero (outstanding 5.00 GBP).", reply.Message);
    }

    [Fact]
    public void CreditRefusalMismatchError_ReachesTheWireMessage_ScaledByTheCurrencysExponent()
    {
        var error = new CreditRefusalMismatchError(requestedMinorUnits: 1000, availableMinorUnits: 2000, currency: "EUR");

        var reply = BillingErrorMapper.Map(error, _occurredAt);

        Assert.Equal("Refusal reason 'over_limit' claimed for a request (10.00 EUR) that fits within the available credit (20.00 EUR).", reply.Message);
    }

    [Fact]
    public void CreditLimitExceededError_ReachesTheWireMessage_ScaledByTheCurrencysExponent()
    {
        var error = new CreditLimitExceededError(requestedMinorUnits: 12345, availableMinorUnits: 500, currency: "BHD");

        var reply = BillingErrorMapper.Map(error, _occurredAt);

        Assert.Equal("Requested amount 12.345 BHD exceeds available credit 0.500 BHD.", reply.Message);
    }

    [Fact]
    public void CreditLedgerEntry_Create_NonPositiveAmount_ReachesTheWireMessage_ScaledByTheCurrencysExponent()
    {
        var error = Assert.Throws<InvalidBuyerCreditSnapshotError>(
            () => CreditLedgerEntry.Create(UniqueId.New(), OrderNumber.Parse("ORD-000001"), new Money(0, "JPY"), CreditEntryType.Hold, DateTimeOffset.UtcNow));

        var reply = BillingErrorMapper.Map(error, _occurredAt);

        Assert.Equal("a hold entry's amount must be strictly positive; got 0 JPY.", reply.Message);
    }

    [Fact]
    public void BuyerCredit_Reconstitute_OverLimitSnapshot_ReachesTheWireMessage_ScaledByTheCurrencysExponent()
    {
        var overLimitSnapshot = new BuyerCreditSnapshot(UniqueId.New(), "CR-000001", "CarrefourEs", "IBERFOODS", new Money(1_000, "EUR"), 1_001, []);

        var error = Assert.Throws<InvalidBuyerCreditSnapshotError>(() => BuyerCredit.Reconstitute(overLimitSnapshot));

        var reply = BillingErrorMapper.Map(error, _occurredAt);

        Assert.Equal("credit line 'CR-000001': committed exposure (10.01 EUR) already exceeds the credit limit (10.00 EUR) — invariant B1.", reply.Message);
    }

    [Fact]
    public void Invoice_Reconstitute_StoredAmountDisagreement_ReachesTheWireMessage_ScaledByTheCurrencysExponent()
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

        var reply = BillingErrorMapper.Map(error, _occurredAt);

        Assert.Equal("invoice 'INV-000001': stored amount 99.99 EUR disagrees with the lines' own total 10.00 EUR — invariant B6.", reply.Message);
    }
}
