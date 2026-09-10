using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Facts.Payloads;
using OrderToCash.Projector.Domain;
using Xunit;

namespace OrderToCash.Projector.UnitTests;

/// <summary>
/// <c>PR16</c> — fourteen builders, each asserting the WHOLE string (never a
/// <c>Contains</c>). Expected strings are #7's <c>summaries.ts</c> table as
/// carried into design.md §4. Arm by corrupting one payload field the test
/// supplied (e.g. <c>credit.rejected.v1</c>'s <c>reason</c>) and by deleting
/// one builder's arm.
/// </summary>
public sealed class SummariesTests
{
    [Fact]
    public void PR16_OrderPlaced()
    {
        var payload = new OrderPlacedPayload("ORD-000001", "RET01", "COM01", "buyer-gln", "supplier-gln", "EUR", DateTimeOffset.UtcNow, [], 0, 0, 0);
        var result = Summaries.OrderPlaced(payload);
        Assert.Equal("Order ORD-000001 placed for RET01", result.Summary);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void PR16_StockReserved()
    {
        var payload = new StockReservedPayload("ORD-000001", "COM01", [new ReservationRef(Guid.NewGuid(), "SKU1", 2), new ReservationRef(Guid.NewGuid(), "SKU2", 1)]);
        var result = Summaries.StockReserved(payload);
        Assert.Equal("Stock reserved for 2 line(s)", result.Summary);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void PR16_StockRejected()
    {
        var payload = new StockRejectedPayload("ORD-000001", "COM01", [new Shortage("SKU1", 10, 4), new Shortage("SKU2", 5, 5)], "insufficient_stock");
        var result = Summaries.StockRejected(payload);
        Assert.Equal("Stock rejected: 6 unit(s) short on SKU1", result.Summary);
        Assert.NotNull(result.Detail);
        Assert.Equal(payload.Shortages, result.Detail!["shortages"]);
        Assert.Equal("insufficient_stock", result.Detail["reason"]);
    }

    [Fact]
    public void PR16_StockReleased_NotACompensation()
    {
        var payload = new StockReleasedPayload("ORD-000001", "COM01", [new ReservationRef(Guid.NewGuid(), "SKU1", 3)], "order_cancelled");
        var result = Summaries.StockReleased(payload);
        Assert.Equal("3 unit(s) released back to stock", result.Summary);
        Assert.Equal("order_cancelled", result.Detail!["reason"]);
    }

    [Fact]
    public void PR16_StockReleased_CompensationSuffixOnlyWhenReasonIsCreditRejected()
    {
        var payload = new StockReleasedPayload("ORD-000001", "COM01", [new ReservationRef(Guid.NewGuid(), "SKU1", 3)], "credit_rejected");
        var result = Summaries.StockReleased(payload);
        Assert.Equal("3 unit(s) released back to stock (compensation)", result.Summary);
    }

    [Fact]
    public void PR16_CreditApproved()
    {
        var payload = new CreditApprovedPayload("ORD-000001", "RET01", "COM01", "CR-000001", "EUR", 16130, 50000);
        var result = Summaries.CreditApproved(payload);
        Assert.Equal("Credit hold of 16 130 EUR approved", result.Summary);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void PR16_CreditRejected()
    {
        var payload = new CreditRejectedPayload("ORD-000001", "RET01", "COM01", "EUR", 16130, 5000, "insufficient_credit");
        var result = Summaries.CreditRejected(payload);
        Assert.Equal("Credit hold of 16 130 EUR rejected (insufficient_credit)", result.Summary);
        Assert.Equal("insufficient_credit", result.Detail!["reason"]);
        Assert.Equal(16130L, result.Detail["requestedAmount"]);
    }

    [Fact]
    public void PR16_CreditReleased_InvoicePaid()
    {
        var payload = new CreditReleasedPayload("ORD-000001", "RET01", "COM01", "EUR", 16130, 50000, "invoice_paid");
        var result = Summaries.CreditReleased(payload);
        Assert.Equal("Credit exposure released — invoice paid", result.Summary);
    }

    [Fact]
    public void PR16_CreditReleased_OrderCancelled()
    {
        var payload = new CreditReleasedPayload("ORD-000001", "RET01", "COM01", "EUR", 16130, 50000, "order_cancelled");
        var result = Summaries.CreditReleased(payload);
        Assert.Equal("Credit exposure released — order cancelled", result.Summary);
    }

    [Fact]
    public void PR16_OrderConfirmed()
    {
        var payload = new OrderConfirmedPayload("ORD-000001", "RET01", "COM01", "EUR", 16130, DateTimeOffset.UtcNow);
        var result = Summaries.OrderConfirmed(payload);
        Assert.Equal("Order confirmed (ORDRSP)", result.Summary);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void PR16_OrderDespatched()
    {
        var payload = new OrderDespatchedPayload("ORD-000001", "DES-000001", DateTimeOffset.UtcNow, "COM01", "RET01", []);
        var result = Summaries.OrderDespatched(payload);
        Assert.Equal("Despatch DES-000001 created", result.Summary);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void PR16_InvoiceIssued()
    {
        var payload = new InvoiceIssuedPayload("ORD-000001", "INV-000001", DateTimeOffset.UtcNow, "RET01", "COM01", "EUR", [], 16130, 0, 16130);
        var result = Summaries.InvoiceIssued(payload);
        Assert.Equal("Invoice INV-000001 issued", result.Summary);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void PR16_PaymentReceived()
    {
        var payload = new PaymentReceivedPayload("ORD-000001", "INV-000001", "PAY-000001", "EUR", 16130, DateTimeOffset.UtcNow, "bank_transfer");
        var result = Summaries.PaymentReceived(payload);
        Assert.Equal("Payment PAY-000001 received", result.Summary);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void PR16_OrderCompleted()
    {
        var payload = new OrderCompletedPayload("ORD-000001", "RET01", "COM01", "EUR", 16130, DateTimeOffset.UtcNow);
        var result = Summaries.OrderCompleted(payload);
        Assert.Equal("Order ORD-000001 completed", result.Summary);
        Assert.Null(result.Detail);
    }

    [Fact]
    public void PR16_OrderCancelled()
    {
        var payload = new OrderCancelledPayload("ORD-000001", "RET01", "COM01", "buyer_requested", DateTimeOffset.UtcNow, [new CompensationStep("stock_release", "stock.released.v1", DateTimeOffset.UtcNow)]);
        var result = Summaries.OrderCancelled(payload);
        Assert.Equal("Order ORD-000001 cancelled (buyer_requested)", result.Summary);
        Assert.Equal("buyer_requested", result.Detail!["cancellationReason"]);
        Assert.Equal(payload.CompensationSteps, result.Detail["compensationSteps"]);
        Assert.False(result.Detail.ContainsKey("note")); // SA-2: no key at all when the fact carries none.
    }

    /// <summary>
    /// SA-2/feature <c>operator_note_reaches_the_timeline</c> bullet 1's
    /// summary-builder half: a fact carrying a note populates the
    /// <c>note</c> detail key with the EXACT text — bracketed to the value
    /// the test itself supplies, not merely asserted present (CLAUDE.md's
    /// provenance rule; also the corruption half of bullet 4's arming, since
    /// a wrong value here fails this same assertion).
    /// </summary>
    [Fact]
    public void SA2_OrderCancelled_WithANote_PopulatesTheNoteDetailKeyWithTheExactText()
    {
        const string note = "Buyer changed their mind before despatch.";
        var payload = new OrderCancelledPayload("ORD-000001", "RET01", "COM01", "operator_cancelled", DateTimeOffset.UtcNow, [], note);

        var result = Summaries.OrderCancelled(payload);

        Assert.Equal(note, result.Detail!["note"]);
    }

    [Fact]
    public void PR16_OrderSagaFailed()
    {
        var payload = new OrderSagaFailedPayload("ORD-000001", "credit.hold", 5, "timeout", DateTimeOffset.UtcNow);
        var result = Summaries.OrderSagaFailed(payload);
        Assert.Equal("Saga command \"credit.hold\" dead-lettered after 5 attempt(s)", result.Summary);
        Assert.Equal("credit.hold", result.Detail!["command"]);
        Assert.Equal(5, result.Detail["attempts"]);
        Assert.Equal("timeout", result.Detail["lastError"]);
    }

    /// <summary><c>PR16</c>: <c>detail</c> populated only for these five facts, and no other.</summary>
    [Fact]
    public void PR16_DetailIsPopulatedOnlyForTheFiveNamedFacts()
    {
        Assert.NotNull(Summaries.StockRejected(new StockRejectedPayload("O", "C", [new Shortage("S", 1, 0)], "r")).Detail);
        Assert.NotNull(Summaries.StockReleased(new StockReleasedPayload("O", "C", [], "r")).Detail);
        Assert.NotNull(Summaries.CreditRejected(new CreditRejectedPayload("O", "R", "C", "EUR", 1, 1, "r")).Detail);
        Assert.NotNull(Summaries.OrderCancelled(new OrderCancelledPayload("O", "R", "C", "r", DateTimeOffset.UtcNow, [])).Detail);
        Assert.NotNull(Summaries.OrderSagaFailed(new OrderSagaFailedPayload("O", "cmd", 1, "e", DateTimeOffset.UtcNow)).Detail);

        Assert.Null(Summaries.OrderPlaced(new OrderPlacedPayload("O", "R", "C", "b", "s", "EUR", DateTimeOffset.UtcNow, [], 0, 0, 0)).Detail);
        Assert.Null(Summaries.StockReserved(new StockReservedPayload("O", "C", [])).Detail);
        Assert.Null(Summaries.CreditApproved(new CreditApprovedPayload("O", "R", "C", "CR", "EUR", 1, 1)).Detail);
        Assert.Null(Summaries.CreditReleased(new CreditReleasedPayload("O", "R", "C", "EUR", 1, 1, "invoice_paid")).Detail);
        Assert.Null(Summaries.OrderConfirmed(new OrderConfirmedPayload("O", "R", "C", "EUR", 1, DateTimeOffset.UtcNow)).Detail);
        Assert.Null(Summaries.OrderDespatched(new OrderDespatchedPayload("O", "D", DateTimeOffset.UtcNow, "C", "R", [])).Detail);
        Assert.Null(Summaries.InvoiceIssued(new InvoiceIssuedPayload("O", "I", DateTimeOffset.UtcNow, "R", "C", "EUR", [], 1, 0, 1)).Detail);
        Assert.Null(Summaries.PaymentReceived(new PaymentReceivedPayload("O", "I", "P", "EUR", 1, DateTimeOffset.UtcNow, "s")).Detail);
        Assert.Null(Summaries.OrderCompleted(new OrderCompletedPayload("O", "R", "C", "EUR", 1, DateTimeOffset.UtcNow)).Detail);
    }
}
