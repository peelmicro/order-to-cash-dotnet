namespace OrderToCash.Orders.Presentation.Rpc;

/// <summary>
/// A wire-shape refusal for <c>orders.cancel</c> — mirrors
/// <c>InvalidOrdersCreateRequestError</c>/<c>InvalidCatalogReferenceListRequestError</c>:
/// distinct from every domain/application refusal, mapped to
/// <c>VALIDATION_FAILED</c> by <see cref="OrdersCreateErrorMapper"/>.
/// </summary>
public sealed class InvalidOrdersCancelRequestError(string message) : Exception(message);

/// <summary>
/// <c>asyncapi.yaml</c> <c>OrdersCancelRequestPayload</c>: <c>orderId</c> is
/// required HERE even though the wire schema's own <c>required</c> list
/// names only <c>reason</c> — the Gateway, the only caller, always sends
/// <c>orderId</c>, and this responder has no other way to locate the order
/// (matching #7's identical <c>orders-cancel.dto.ts</c> rationale). Run
/// BEFORE the request reaches the command, the same discipline
/// <c>OrdersCreateRequestValidator</c>/<c>CatalogReferenceListRequestValidator</c>
/// already follow.
/// </summary>
public static class OrdersCancelRequestValidator
{
    private const string RequiredReason = "operator_cancelled";

    public static void Validate(OrdersCancelRequestPayload request)
    {
        if (request.OrderId is null || request.OrderId == Guid.Empty)
        {
            throw new InvalidOrdersCancelRequestError(
                "orders.cancel request is missing or has an empty required field: orderId.");
        }

        if (!string.Equals(request.Reason, RequiredReason, StringComparison.Ordinal))
        {
            throw new InvalidOrdersCancelRequestError(
                $"orders.cancel request's reason must be \"{RequiredReason}\" — the only value an external caller " +
                $"may ever request (stock_rejected/credit_rejected are decided by the saga, never asked for). " +
                $"Received {(request.Reason is null ? "<absent>" : $"\"{request.Reason}\"")}.");
        }
    }
}
