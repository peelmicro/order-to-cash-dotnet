namespace OrderToCash.Gateway.Domain.Problem;

/// <summary>The RPC error shape this classifier reads — mirrors the wire's <c>RpcError</c> schema (asyncapi.yaml) without depending on any service's own <c>RpcErrorPayload</c> record.</summary>
public interface IRpcErrorLike
{
    string Code { get; }

    string Message { get; }

    IReadOnlyDictionary<string, object?>? Details { get; }
}

/// <summary>The classification this file produces — an HTTP status and the <c>Problem.code</c>/<c>title</c> openapi.yaml's <c>Problem</c> schema carries.</summary>
public readonly record struct ClassifiedRpcError(int Status, string Code, string Title);

/// <summary>
/// The ONE place an <c>RpcError</c> (the twelve-value generic vocabulary
/// every RPC responder in this repository answers with — see
/// <c>OrdersCreateErrorMapper._contractRpcErrorCodes</c>,
/// <c>src/Orders/Presentation/Rpc/OrdersCreateErrorMapper.cs</c>) is
/// translated into an HTTP status and openapi.yaml's own stable
/// <c>Problem.code</c> vocabulary. Pure — no ASP.NET Core, no NATS — so it
/// is unit-testable on its own and the domain-purity architecture tests
/// leave it alone.
/// </summary>
/// <remarks>
/// Ported from #7's <c>apps/gateway/src/domain/problem/rpc-error-mapping.ts</c>
/// (<c>classifyRpcError</c>), which every RPC responder's own mapper keeps
/// the SPECIFIC vocabulary in <c>details.code</c> for, per its own class
/// summary — this file is the one translation step back from "generic RPC
/// code plus an opaque <c>details.code</c>" to the openapi contract's own
/// stable code, so a caller of THIS gateway never has to know which
/// upstream service raised the refusal or how that service's own mapper
/// happened to label it.
///
/// <b>Ported-idiom ledger row.</b> #7's <c>DETAIL_CODE_OVERRIDES</c> keys on
/// <c>details.code === 'PAYMENT_REFERENCE_CONFLICT'</c> to answer
/// <c>PAYMENT_REFERENCE_REUSED</c>. In #8, Billing's
/// <c>PaymentReferenceConflictError</c> (<c>src/Billing/Application/InvoiceApplicationErrors.cs</c>)
/// extends plain <see cref="Exception"/>, not <c>DomainError</c> — it carries
/// no <c>Code</c>, so <c>BillingErrorMapper</c>
/// (<c>src/Billing/Presentation/Rpc/BillingErrorMapper.cs:141-149</c>) emits
/// <c>PRECONDITION_FAILED</c> with <c>details.paymentReference</c> only, no
/// <c>details.code</c> at all — the property #7's override relied on does
/// not exist on this wire reply. Reaching into Billing to add one is out of
/// this feature's scope ("do not modify any other service"), and
/// openapi.yaml's <c>Problem.code</c> is a plain string, not a schema-
/// enforced enum, so the contract permits a coarser answer here: this
/// classifier keys the same override on the SHAPE of <c>details</c> instead
/// — <c>PRECONDITION_FAILED</c> carrying a <c>paymentReference</c> key and
/// no <c>code</c> key answers <c>PAYMENT_REFERENCE_REUSED</c> (409),
/// matching openapi.yaml's own documented outcome for that row of
/// <c>registerPayment</c>'s idempotency table even though the property #7
/// used to detect it does not survive the trip through #8's Billing
/// mapper.
/// </remarks>
public static class RpcErrorClassifier
{
    /// <summary><c>details.code</c> values that name a MORE SPECIFIC business outcome than their enclosing generic <c>RpcError.code</c> — each overrides both the status the generic code implies and the <c>Problem.code</c> the caller sees.</summary>
    private static readonly Dictionary<string, ClassifiedRpcError> _detailCodeOverrides = new(StringComparer.Ordinal)
    {
        ["INVOICE_ALREADY_PAID"] = new ClassifiedRpcError(409, "INVOICE_ALREADY_PAID", "Invoice already paid"),
        ["INVOICE_PAYMENT_AMOUNT_MISMATCH"] = new ClassifiedRpcError(422, "PAYMENT_MISMATCH", "Payment amount does not match the invoice total"),
        ["INVOICE_PAYMENT_CURRENCY_MISMATCH"] = new ClassifiedRpcError(422, "PAYMENT_MISMATCH", "Payment currency does not match the invoice total"),
        ["ORDER_NOT_CANCELLABLE"] = new ClassifiedRpcError(409, "ORDER_NOT_CANCELLABLE", "Order is not cancellable"),
    };

    /// <summary>Generic <c>RpcError.code</c> → HTTP status + fallback <c>Problem.code</c>/<c>title</c>, used whenever <c>details.code</c> (or the shape heuristic below) names nothing more specific.</summary>
    private static readonly Dictionary<string, ClassifiedRpcError> _genericClassification = new(StringComparer.Ordinal)
    {
        ["VALIDATION_FAILED"] = new ClassifiedRpcError(400, "VALIDATION_FAILED", "The request was malformed or failed validation"),
        ["NOT_FOUND"] = new ClassifiedRpcError(404, "NOT_FOUND", "No such resource"),
        ["CONFLICT"] = new ClassifiedRpcError(409, "CONFLICT", "The request conflicts with the current state"),
        ["PRECONDITION_FAILED"] = new ClassifiedRpcError(409, "PRECONDITION_FAILED", "A precondition of this operation was not met"),
        ["ORDER_NOT_CANCELLABLE"] = new ClassifiedRpcError(409, "ORDER_NOT_CANCELLABLE", "Order is not cancellable"),
        ["STOCK_UNAVAILABLE"] = new ClassifiedRpcError(409, "STOCK_UNAVAILABLE", "Insufficient stock at acceptance"),
        ["INVOICE_NOT_PAYABLE"] = new ClassifiedRpcError(409, "INVOICE_NOT_PAYABLE", "Invoice is not payable"),
        ["PAYMENT_MISMATCH"] = new ClassifiedRpcError(422, "PAYMENT_MISMATCH", "Payment amount or currency does not match the invoice"),
        ["DOMAIN_ERROR"] = new ClassifiedRpcError(422, "DOMAIN_ERROR", "A domain rule refused the request"),
        ["INTERNAL_ERROR"] = new ClassifiedRpcError(500, "INTERNAL_ERROR", "An unexpected error occurred"),
        ["UNAVAILABLE"] = new ClassifiedRpcError(503, "UPSTREAM_UNAVAILABLE", "The owning context is unreachable"),
        ["TIMEOUT"] = new ClassifiedRpcError(503, "UPSTREAM_TIMEOUT", "The owning context did not answer within the deadline"),
    };

    private static readonly ClassifiedRpcError _default = new(500, "INTERNAL_ERROR", "An unexpected error occurred");

    public static ClassifiedRpcError Classify(IRpcErrorLike error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var detailCode = error.Details is not null && error.Details.TryGetValue("code", out var codeValue) && codeValue is string s ? s : null;
        if (detailCode is not null && _detailCodeOverrides.TryGetValue(detailCode, out var overridden))
        {
            return overridden;
        }

        // The Billing-mapper translation gap this class's own remarks
        // record: PRECONDITION_FAILED carrying `paymentReference` (and no
        // `code`) is the reused-payment-reference refusal.
        if (string.Equals(error.Code, "PRECONDITION_FAILED", StringComparison.Ordinal)
            && detailCode is null
            && error.Details is not null
            && error.Details.ContainsKey("paymentReference"))
        {
            return new ClassifiedRpcError(409, "PAYMENT_REFERENCE_REUSED", "Payment reference reused for a different invoice or amount");
        }

        return _genericClassification.GetValueOrDefault(error.Code, _default);
    }
}
