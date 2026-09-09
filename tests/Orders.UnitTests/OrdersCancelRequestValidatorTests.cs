using OrderToCash.Orders.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// <c>asyncapi.yaml</c> <c>OrdersCancelRequestPayload</c>: <c>orderId</c>
/// required (this responder's own choice, matching #7's identical
/// <c>orders-cancel.dto.ts</c> rationale — the wire schema's own
/// <c>required</c> list names only <c>reason</c>), <c>reason</c> must be
/// exactly the wire's <c>const</c>, <c>operator_cancelled</c>.
/// </summary>
public sealed class OrdersCancelRequestValidatorTests
{
    private static readonly Guid _validOrderId = Guid.NewGuid();

    [Fact]
    public void Validate_OrderIdAndOperatorCancelledReason_ThrowsNothing()
    {
        var request = new OrdersCancelRequestPayload(_validOrderId, "ORD-000001", "operator_cancelled", "please cancel");

        var exception = Record.Exception(() => OrdersCancelRequestValidator.Validate(request));
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_NoteOmitted_ThrowsNothing()
    {
        var request = new OrdersCancelRequestPayload(_validOrderId, OrderReference: null, "operator_cancelled", Note: null);

        var exception = Record.Exception(() => OrdersCancelRequestValidator.Validate(request));
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_OrderIdAbsent_Refuses()
    {
        var request = new OrdersCancelRequestPayload(OrderId: null, "ORD-000001", "operator_cancelled", Note: null);

        var error = Assert.Throws<InvalidOrdersCancelRequestError>(() => OrdersCancelRequestValidator.Validate(request));
        Assert.Contains("orderId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_OrderIdIsTheEmptyGuid_Refuses()
    {
        var request = new OrdersCancelRequestPayload(Guid.Empty, "ORD-000001", "operator_cancelled", Note: null);

        var error = Assert.Throws<InvalidOrdersCancelRequestError>(() => OrdersCancelRequestValidator.Validate(request));
        Assert.Contains("orderId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ReasonAbsent_Refuses()
    {
        var request = new OrdersCancelRequestPayload(_validOrderId, "ORD-000001", Reason: null, Note: null);

        var error = Assert.Throws<InvalidOrdersCancelRequestError>(() => OrdersCancelRequestValidator.Validate(request));
        Assert.Contains("reason", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// R8's own vocabulary boundary, at the wire: <c>stock_rejected</c> and
    /// <c>credit_rejected</c> are decided by the saga, never requested by a
    /// caller — a caller that sends one is refused exactly like any other
    /// wrong value, not silently accepted as if it meant
    /// <c>operator_cancelled</c>.
    /// </summary>
    [Theory]
    [InlineData("stock_rejected")]
    [InlineData("credit_rejected")]
    [InlineData("something_else")]
    public void Validate_ReasonIsAnythingOtherThanOperatorCancelled_Refuses(string wrongReason)
    {
        var request = new OrdersCancelRequestPayload(_validOrderId, "ORD-000001", wrongReason, Note: null);

        var error = Assert.Throws<InvalidOrdersCancelRequestError>(() => OrdersCancelRequestValidator.Validate(request));
        Assert.Contains("operator_cancelled", error.Message, StringComparison.Ordinal);
    }
}
