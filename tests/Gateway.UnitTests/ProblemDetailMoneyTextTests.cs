using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Presentation.Problem;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>
/// Backlog id 102 (<c>problem_detail_money_reads_as_minor_units</c>) — a
/// problem document's <c>detail</c> can carry money, and
/// <see cref="ProblemJsonMiddleware.Classify"/> forwards an upstream RPC
/// error's <see cref="Exception.Message"/> to <c>detail</c> VERBATIM
/// (<see cref="ProblemJsonMiddlewareClassificationTests.Classify_NeverRewritesTheOriginalMessage"/>
/// proves the no-rewrite half of this claim for every case). Gateway has
/// no project reference to either service's domain, so this file cannot
/// construct their error TYPES directly; it reconstructs their MESSAGE
/// TEXT the same way, from the same shared <see cref="MoneyText"/>
/// formatter, and drives it through an <see cref="RpcBusinessError"/> the
/// way the real wire reply would.
/// </summary>
/// <remarks>
/// Fix round 1 correction (review defect E1): this file proves ONLY the
/// Gateway pass-through step (RPC error -&gt; problem document
/// <c>detail</c>), NOT "the whole path" — that overstated claim is what
/// let the reviewer's Q2/Q4 arms (a service mapper rebuilding a raw
/// message) survive with this suite fully green, because this file never
/// calls the real <c>*ErrorMapper</c>. The mapper hop itself is now
/// proven separately, by <c>Billing.UnitTests.BillingErrorMapperMoneyTextTests</c>
/// and the money-text cases added to
/// <c>Orders.UnitTests.OrdersCreateErrorMapperTests</c>, each of which
/// constructs a real domain error and calls the real mapper. The full
/// chain is therefore: domain message
/// (<c>Billing.UnitTests.DomainErrorMoneyTextTests</c>,
/// <c>Orders.UnitTests.OrderTotalsTests</c>/<c>PlaceOrderCommandHandlerTests</c>)
/// -&gt; service mapper (<c>BillingErrorMapperMoneyTextTests</c>,
/// <c>OrdersCreateErrorMapperTests</c>) -&gt; Gateway pass-through (this
/// file) — three separate proofs, no single one of which spans the whole
/// path alone.
/// </remarks>
public sealed class ProblemDetailMoneyTextTests
{
    /// <summary>
    /// The exact path the review traced: `src/Billing/Domain/Errors/InvoicePaymentAmountMismatchError.cs`
    /// -&gt; `src/Billing/Presentation/Rpc/BillingErrorMapper.cs:95-98`
    /// (PRECONDITION_FAILED, details.code = INVOICE_PAYMENT_AMOUNT_MISMATCH)
    /// -&gt; `src/Gateway/Presentation/Problem/ProblemJsonMiddleware.cs`'s
    /// `detail`.
    /// </summary>
    [Fact]
    public void Classify_InvoicePaymentAmountMismatch_RendersBothAmountsWithTheSharedMoneyTextFormatter_NeverRawMinorUnits()
    {
        var message = $"Payment amount {MoneyText.Format(9245, "EUR")} does not equal the invoice's totalAmount {MoneyText.Format(12000, "EUR")}.";
        var error = new RpcBusinessError(
            "billing.payment.register",
            "PRECONDITION_FAILED",
            message,
            new Dictionary<string, object?> { ["code"] = "INVOICE_PAYMENT_AMOUNT_MISMATCH" });

        var classified = ProblemJsonMiddleware.Classify(error);

        Assert.Equal(422, classified.Status);
        Assert.Equal("PAYMENT_MISMATCH", classified.Code);
        Assert.Equal("Payment amount 92.45 EUR does not equal the invoice's totalAmount 120.00 EUR.", classified.Detail);
        Assert.DoesNotContain("9245", classified.Detail);
        Assert.DoesNotContain("12000", classified.Detail);
    }

    /// <summary>
    /// The Orders-side twin: `src/Orders/Domain/Errors/OrderTotalMustNotBeNegativeError.cs`
    /// -&gt; `src/Orders/Presentation/Rpc/OrdersCreateErrorMapper.cs`'s generic
    /// `DomainError` case (VALIDATION_FAILED) -&gt; Gateway `detail`. Proves the
    /// same path for the OTHER service whose domain errors were fixed by
    /// this backlog entry, and for a 0-exponent currency (JPY), so grouping
    /// with no decimal point survives the trip too.
    /// </summary>
    [Fact]
    public void Classify_OrderTotalMustNotBeNegative_RendersTheAmountWithTheSharedMoneyTextFormatter_ForAZeroExponentCurrency()
    {
        var message = $"The resulting total amount would be negative: {MoneyText.Format(-500000, "JPY")}.";
        var error = new RpcBusinessError(
            "orders.create",
            "VALIDATION_FAILED",
            message,
            new Dictionary<string, object?> { ["code"] = "order.total_must_not_be_negative" });

        var classified = ProblemJsonMiddleware.Classify(error);

        Assert.Equal(400, classified.Status);
        Assert.Equal("VALIDATION_FAILED", classified.Code);
        Assert.Equal("The resulting total amount would be negative: -500 000 JPY.", classified.Detail);
        Assert.DoesNotContain("-500000", classified.Detail);
    }

    /// <summary>Machine field `code` stays the raw domain code — never reformatted (acceptance bullet 3: "machine-readable fields are unchanged").</summary>
    [Fact]
    public void Classify_InvoicePaymentAmountMismatch_TheCodeFieldIsUnaffectedByTheDetailTextChange()
    {
        var error = new RpcBusinessError(
            "billing.payment.register",
            "PRECONDITION_FAILED",
            $"Payment amount {MoneyText.Format(9245, "EUR")} does not equal the invoice's totalAmount {MoneyText.Format(12000, "EUR")}.",
            new Dictionary<string, object?> { ["code"] = "INVOICE_PAYMENT_AMOUNT_MISMATCH" });

        var classified = ProblemJsonMiddleware.Classify(error);

        Assert.Equal("PAYMENT_MISMATCH", classified.Code);
    }
}
