using OrderToCash.Gateway.Domain.Problem;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>Acceptance bullet 2 — "RPC timeouts mapped to HTTP status codes". Each named theory case guards ONE code, per the brief's own instruction ("guard the mapping per case, not that an error becomes an error").</summary>
public sealed class RpcErrorClassifierTests
{
    private sealed record FakeRpcError(string Code, string Message, IReadOnlyDictionary<string, object?>? Details = null) : IRpcErrorLike;

    [Theory]
    [InlineData("VALIDATION_FAILED", 400, "VALIDATION_FAILED")]
    [InlineData("NOT_FOUND", 404, "NOT_FOUND")]
    [InlineData("CONFLICT", 409, "CONFLICT")]
    [InlineData("PRECONDITION_FAILED", 409, "PRECONDITION_FAILED")]
    [InlineData("ORDER_NOT_CANCELLABLE", 409, "ORDER_NOT_CANCELLABLE")]
    [InlineData("STOCK_UNAVAILABLE", 409, "STOCK_UNAVAILABLE")]
    [InlineData("INVOICE_NOT_PAYABLE", 409, "INVOICE_NOT_PAYABLE")]
    [InlineData("PAYMENT_MISMATCH", 422, "PAYMENT_MISMATCH")]
    [InlineData("DOMAIN_ERROR", 422, "DOMAIN_ERROR")]
    [InlineData("INTERNAL_ERROR", 500, "INTERNAL_ERROR")]
    [InlineData("UNAVAILABLE", 503, "UPSTREAM_UNAVAILABLE")]
    [InlineData("TIMEOUT", 503, "UPSTREAM_TIMEOUT")]
    public void Classify_MapsEveryGenericRpcErrorCode_ToItsOwnDocumentedHttpStatusAndProblemCode(string rpcCode, int expectedStatus, string expectedProblemCode)
    {
        var classified = RpcErrorClassifier.Classify(new FakeRpcError(rpcCode, "irrelevant message"));

        Assert.Equal(expectedStatus, classified.Status);
        Assert.Equal(expectedProblemCode, classified.Code);
    }

    [Fact]
    public void Classify_TimeoutAndUnavailable_MapToTheSameStatusButDifferentProblemCodes()
    {
        // The two transient-failure codes both answer 503 (openapi.yaml
        // UpstreamUnavailable), but a caller must be able to tell "no
        // responder was even subscribed" from "one was, and it never
        // replied in time" — the exact distinction this feature's own
        // known-risk section names.
        var timeout = RpcErrorClassifier.Classify(new FakeRpcError("TIMEOUT", "timed out"));
        var unavailable = RpcErrorClassifier.Classify(new FakeRpcError("UNAVAILABLE", "no responder"));

        Assert.Equal(503, timeout.Status);
        Assert.Equal(503, unavailable.Status);
        Assert.NotEqual(timeout.Code, unavailable.Code);
        Assert.Equal("UPSTREAM_TIMEOUT", timeout.Code);
        Assert.Equal("UPSTREAM_UNAVAILABLE", unavailable.Code);
    }

    [Fact]
    public void Classify_UnrecognisedCode_FallsBackToInternalError500()
    {
        var classified = RpcErrorClassifier.Classify(new FakeRpcError("SOMETHING_NO_RESPONDER_HAS_EVER_SENT", "?"));

        Assert.Equal(500, classified.Status);
        Assert.Equal("INTERNAL_ERROR", classified.Code);
    }

    [Theory]
    [InlineData("INVOICE_ALREADY_PAID", 409, "INVOICE_ALREADY_PAID")]
    [InlineData("INVOICE_PAYMENT_AMOUNT_MISMATCH", 422, "PAYMENT_MISMATCH")]
    [InlineData("INVOICE_PAYMENT_CURRENCY_MISMATCH", 422, "PAYMENT_MISMATCH")]
    public void Classify_DetailCodeOverridesTakePrecedenceOverTheGenericPreconditionFailedCode(string detailCode, int expectedStatus, string expectedProblemCode)
    {
        var error = new FakeRpcError("PRECONDITION_FAILED", "the invoice refused the payment", new Dictionary<string, object?> { ["code"] = detailCode });

        var classified = RpcErrorClassifier.Classify(error);

        Assert.Equal(expectedStatus, classified.Status);
        Assert.Equal(expectedProblemCode, classified.Code);
    }

    /// <summary>
    /// The ported-idiom ledger row this class's own XML doc names:
    /// Billing's <c>PaymentReferenceConflictError</c> carries no
    /// <c>details.code</c> at all (it is not a <c>DomainError</c>) — only
    /// <c>details.paymentReference</c>. Proves the shape-based override
    /// still answers <c>PAYMENT_REFERENCE_REUSED</c> (409) from that
    /// narrower signal.
    /// </summary>
    [Fact]
    public void Classify_PreconditionFailedCarryingOnlyAPaymentReferenceDetail_AnswersPaymentReferenceReused()
    {
        var error = new FakeRpcError("PRECONDITION_FAILED", "paymentReference already used", new Dictionary<string, object?> { ["paymentReference"] = "PAY-1" });

        var classified = RpcErrorClassifier.Classify(error);

        Assert.Equal(409, classified.Status);
        Assert.Equal("PAYMENT_REFERENCE_REUSED", classified.Code);
    }

    /// <summary>Ported from #7's own rpc-error-mapping.spec.ts: "leaves an unrelated details.code untouched — only the known overrides apply."</summary>
    [Fact]
    public void Classify_AnUnrelatedDetailsCode_IsLeftUntouched_OnlyTheKnownOverridesApply()
    {
        var error = new FakeRpcError("PRECONDITION_FAILED", "some other precondition", new Dictionary<string, object?> { ["code"] = "SOME_OTHER_DOMAIN_CODE" });

        var classified = RpcErrorClassifier.Classify(error);

        Assert.Equal(409, classified.Status);
        Assert.Equal("PRECONDITION_FAILED", classified.Code);
    }

    [Fact]
    public void Classify_PreconditionFailedWithNeitherCodeNorPaymentReference_FallsBackToTheGenericPreconditionFailedClassification()
    {
        var error = new FakeRpcError("PRECONDITION_FAILED", "some other precondition", new Dictionary<string, object?> { ["orderReference"] = "ORD-000001" });

        var classified = RpcErrorClassifier.Classify(error);

        Assert.Equal(409, classified.Status);
        Assert.Equal("PRECONDITION_FAILED", classified.Code);
    }
}
