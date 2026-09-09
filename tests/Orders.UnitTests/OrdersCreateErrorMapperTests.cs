using System.Reflection;
using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Domain.Errors;
using OrderToCash.Orders.Infrastructure.Messaging;
using OrderToCash.Orders.Presentation.Rpc;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// design.md §9.2's mapping table, reproduced from #7's
/// <c>rpc-error-mapper.ts</c> — every failure the <c>orders.create</c>
/// responder can observe, mapped to <c>asyncapi.yaml</c>'s closed
/// <c>RpcError.code</c> enum.
/// </summary>
public sealed class OrdersCreateErrorMapperTests
{
    private static readonly DateTimeOffset _occurredAt = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The stock-check rejection path (this feature's acceptance item 3) — a business outcome, not a transport error, carrying the short lines in `details.shortages`.</summary>
    [Fact]
    public void Map_StockUnavailableError_MapsToStockUnavailableWithTheShortLinesInDetails()
    {
        var shortage = new StockAvailabilityLineResult("PROD-001", 2, 1, false);
        var error = new StockUnavailableError([shortage]);

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("STOCK_UNAVAILABLE", payload.Code);
        Assert.Equal(error.Message, payload.Message);
        Assert.Same(shortage, Assert.Single((IReadOnlyList<StockAvailabilityLineResult>)payload.Details!["shortages"]!));
        Assert.Equal(_occurredAt, payload.OccurredAt);
    }

    [Fact]
    public void Map_StockCheckTimeoutError_MapsToTimeoutNotStockUnavailable()
    {
        var error = new StockCheckTimeoutError("fulfillment.stock.check", 5_000);

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("TIMEOUT", payload.Code);
        Assert.Equal("fulfillment.stock.check", payload.Details!["subject"]);
        Assert.Equal(5_000, payload.Details!["timeoutMs"]);
    }

    [Fact]
    public void Map_StockCheckTransportError_MapsToUnavailableNotStockUnavailable()
    {
        var error = new StockCheckTransportError("fulfillment.stock.check", "no responder subscribed");

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("UNAVAILABLE", payload.Code);
        Assert.Equal("fulfillment.stock.check", payload.Details!["subject"]);
    }

    /// <summary>
    /// Feature 46 (`R46`): the responder's OWN code and message pass through
    /// to the wire unchanged — this is what "a meaningful RPC error rather
    /// than INTERNAL_ERROR" means for <c>OrdersCreateErrorMapper</c>. Two
    /// distinct codes on purpose: a mapper that ignored
    /// <see cref="StockCheckBusinessError.RpcErrorCode"/> and hardcoded a
    /// single sentinel would still pass a single-code test.
    /// </summary>
    [Theory]
    [InlineData("NOT_FOUND", "product ZZZ is not known to Fulfillment")]
    [InlineData("PRECONDITION_FAILED", "stock item is locked for replenishment")]
    public void Map_StockCheckBusinessError_MapsToTheRespondersOwnCodeAndMessageNotInternalError(string responderCode, string responderMessage)
    {
        var error = new StockCheckBusinessError("fulfillment.stock.check", responderCode, responderMessage);

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal(responderCode, payload.Code);
        Assert.NotEqual("INTERNAL_ERROR", payload.Code);
        Assert.Equal(responderMessage, payload.Message);
        Assert.Equal("fulfillment.stock.check", payload.Details!["subject"]);
    }

    /// <summary>
    /// Review D2 (round 2), required change 1: a <c>[Theory]</c> pinning the
    /// closed set — including at least one code outside
    /// <c>asyncapi.yaml</c>'s twelve-value <c>RpcError.code</c> enum — and
    /// asserting the mapper NEVER writes anything else to the wire.
    /// </summary>
    private static readonly HashSet<string> _contractRpcErrorCodes =
    [
        "VALIDATION_FAILED", "NOT_FOUND", "CONFLICT", "PRECONDITION_FAILED",
        "ORDER_NOT_CANCELLABLE", "STOCK_UNAVAILABLE", "INVOICE_NOT_PAYABLE",
        "PAYMENT_MISMATCH", "DOMAIN_ERROR", "INTERNAL_ERROR", "UNAVAILABLE", "TIMEOUT",
    ];

    [Theory]
    [InlineData("NOT_FOUND")]
    [InlineData("PRECONDITION_FAILED")]
    [InlineData("UNAVAILABLE")]
    [InlineData("NOT_A_CONTRACT_CODE")]
    [InlineData("stock.check.timeout")]
    public void Map_StockCheckBusinessError_NeverEmitsACodeOutsideAsyncApisClosedRpcErrorEnum(string responderCode)
    {
        var error = new StockCheckBusinessError("fulfillment.stock.check", responderCode, "some message");

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Contains(payload.Code, _contractRpcErrorCodes);
    }

    /// <summary>
    /// Backlog id 51, `BC23` (design.md §10.2) — THREE hand-retyped copies of
    /// the twelve-value <c>RpcError.code</c> enum now exist:
    /// <see cref="OrdersCreateErrorMapper"/>'s own <c>_contractRpcErrorCodes</c>
    /// (production), this test file's <see cref="_contractRpcErrorCodes"/>
    /// (copy 2, added to prove the mapper's clamp), and
    /// <see cref="NatsSagaCommandsAdapter"/>'s terminal/transient split
    /// (copy 3 — a SUBSET of the twelve, added by a later feature). All
    /// three are asserted here against the set parsed from
    /// <c>specs/shared/asyncapi.yaml</c> as text, named individually so a
    /// failure message says WHICH of the three drifted.
    /// </summary>
    [Fact]
    public void BC23_TheThreeRetypedRpcErrorCodeSetsAgreeWithTheEnumParsedFromAsyncApi()
    {
        var parsed = AsyncApiSchema.EnumValuesOf("RpcError.code").ToHashSet(StringComparer.Ordinal);
        Assert.Equal(12, parsed.Count);

        var mapperField = typeof(OrdersCreateErrorMapper).GetField("_contractRpcErrorCodes", BindingFlags.NonPublic | BindingFlags.Static)!;
        var mapperCodes = (HashSet<string>)mapperField.GetValue(null)!;
        Assert.True(parsed.SetEquals(mapperCodes), $"OrdersCreateErrorMapper._contractRpcErrorCodes disagrees with the parsed RpcError.code enum. Parsed: [{string.Join(", ", parsed)}]. Mapper: [{string.Join(", ", mapperCodes)}].");

        Assert.True(parsed.SetEquals(_contractRpcErrorCodes), $"OrdersCreateErrorMapperTests._contractRpcErrorCodes (this file's own copy) disagrees with the parsed RpcError.code enum. Parsed: [{string.Join(", ", parsed)}]. Test copy: [{string.Join(", ", _contractRpcErrorCodes)}].");

        // NatsSagaCommandsAdapter's terminal set is a SUBSET, read via its
        // own IsTerminalRpcErrorCode classification (reflection — same
        // assembly, no text-parsing needed here) rather than retyped.
        var isTerminal = typeof(NatsSagaCommandsAdapter).GetMethod("IsTerminalRpcErrorCode", BindingFlags.NonPublic | BindingFlags.Static)!;
        var terminalCodes = parsed.Where(code => (bool)isTerminal.Invoke(null, [code])!).ToHashSet(StringComparer.Ordinal);
        Assert.True(terminalCodes.IsSubsetOf(parsed), $"NatsSagaCommandsAdapter.IsTerminalRpcErrorCode names a code outside the parsed RpcError.code enum. Terminal: [{string.Join(", ", terminalCodes)}]. Parsed: [{string.Join(", ", parsed)}].");
        Assert.NotEmpty(terminalCodes);
    }

    /// <summary>
    /// `G5` — the arming that proves the guard has teeth. A SCRATCH copy of
    /// the real spec (never the real, read-only
    /// <c>specs/shared/asyncapi.yaml</c>) with one value deleted from
    /// <c>RpcError.code.enum</c>: the parsed set no longer has twelve
    /// members and no longer agrees with the retyped copies.
    /// </summary>
    [Fact]
    public void G5_TheGuardFailsAgainstAScratchCopyWithOneRpcErrorCodeEnumValueDeleted()
    {
        var realSpecPath = RepositoryPaths.Find(Path.Combine("specs", "shared", "asyncapi.yaml"));
        var scratchPath = Path.Combine(Path.GetTempPath(), $"asyncapi-g5-scratch-enum-{Guid.NewGuid():N}.yaml");

        try
        {
            var corrupted = File.ReadAllText(realSpecPath)
                .Replace("          enum:\n            - VALIDATION_FAILED\n            - NOT_FOUND\n            - CONFLICT\n",
                          "          enum:\n            - VALIDATION_FAILED\n            - NOT_FOUND\n", StringComparison.Ordinal);
            File.WriteAllText(scratchPath, corrupted);

            var scratchText = File.ReadAllText(scratchPath);
            var parsedFromScratch = AsyncApiSchema.EnumValuesOf(scratchText, "RpcError.code").ToHashSet(StringComparer.Ordinal);

            Assert.NotEqual(12, parsedFromScratch.Count);
            Assert.DoesNotContain("CONFLICT", parsedFromScratch);
            Assert.False(parsedFromScratch.SetEquals(_contractRpcErrorCodes));
        }
        finally
        {
            File.Delete(scratchPath);
        }
    }

    /// <summary>
    /// Review D2 (round 2): the specific fallback the clamp chooses, and the
    /// original code preserved — visibly — for a caller that wants it. Kills
    /// BOTH mutation families in one assertion set: deleting the clamp makes
    /// <c>payload.Code</c> the raw, out-of-enum string; corrupting the
    /// fallback to anything other than <c>UNAVAILABLE</c> (whether or not the
    /// corrupted value is itself in the closed set) fails the first
    /// assertion below.
    /// </summary>
    [Fact]
    public void Map_StockCheckBusinessError_WithACodeOutsideTheContractsClosedEnum_ClampsToUnavailableAndPreservesTheOriginalCodeInDetails()
    {
        var error = new StockCheckBusinessError("fulfillment.stock.check", "NOT_A_CONTRACT_CODE", "some odd responder message");

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("UNAVAILABLE", payload.Code);
        Assert.Equal("some odd responder message", payload.Message);
        Assert.Equal("NOT_A_CONTRACT_CODE", payload.Details!["responderCode"]);
        Assert.Equal("fulfillment.stock.check", payload.Details!["subject"]);
    }

    [Fact]
    public void Map_ReferenceDataNotFoundError_MapsToNotFoundWithFieldAndValue()
    {
        var error = new ReferenceDataNotFoundError("productCode", "UNKNOWN-PRODUCT");

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("NOT_FOUND", payload.Code);
        Assert.Equal("productCode", payload.Details!["field"]);
        Assert.Equal("UNKNOWN-PRODUCT", payload.Details!["value"]);
    }

    [Fact]
    public void Map_OrderDiscountNotSupportedError_MapsToValidationFailedWithNoDetails()
    {
        var error = new OrderDiscountNotSupportedError(150);

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("VALIDATION_FAILED", payload.Code);
        Assert.Null(payload.Details);
    }

    /// <summary>Every aggregate refusal collapses to VALIDATION_FAILED, but the specific domain Code survives in `details.code` — design.md §9.2: "The details key is code, not domainCode".</summary>
    [Fact]
    public void Map_ADomainError_MapsToValidationFailedAndPreservesTheDomainCodeInDetails()
    {
        DomainError error = new OrderMustHaveAtLeastOneLineError();

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("VALIDATION_FAILED", payload.Code);
        Assert.Equal("order.must_have_at_least_one_line", payload.Details!["code"]);
    }

    [Fact]
    public void Map_AnUnrecognisedException_MapsToInternalError()
    {
        var payload = OrdersCreateErrorMapper.Map(new InvalidOperationException("boom"), _occurredAt);

        Assert.Equal("INTERNAL_ERROR", payload.Code);
        Assert.Equal("boom", payload.Message);
    }

    /// <summary>
    /// review D4 (round 2) — A2's whole point, unguarded until now: a
    /// wire-shape refusal (a required field missing) is client-caused and
    /// must map to <c>VALIDATION_FAILED</c>, never fall through to the
    /// catch-all's <c>INTERNAL_ERROR</c> — the exact symptom A2 was raised
    /// to fix, re-created verbatim by reverting this one arm.
    /// </summary>
    [Fact]
    public void Map_AnInvalidOrdersCreateRequestError_MapsToValidationFailedNotInternalError()
    {
        var error = new InvalidOrdersCreateRequestError("orders.create request is missing or has an empty required field: lines.");

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("VALIDATION_FAILED", payload.Code);
        Assert.NotEqual("INTERNAL_ERROR", payload.Code);
        Assert.Equal(error.Message, payload.Message);
    }

    /// <summary>Feature <c>orders_catalog_responder</c> — <c>catalog.reference.list</c>'s own wire-shape refusal, mapped the same way A2's <c>InvalidOrdersCreateRequestError</c> already is.</summary>
    [Fact]
    public void Map_AnInvalidCatalogReferenceListRequestError_MapsToValidationFailedNotInternalError()
    {
        var error = new InvalidCatalogReferenceListRequestError("catalog.reference.list request's kinds carries a value outside the declared enum: widgets.");

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("VALIDATION_FAILED", payload.Code);
        Assert.NotEqual("INTERNAL_ERROR", payload.Code);
        Assert.Equal(error.Message, payload.Message);
    }

    /// <summary>Feature <c>orders_cancel_responder</c> — <c>orders.cancel</c>'s own wire-shape refusal, mapped the same way as the two validators above.</summary>
    [Fact]
    public void Map_AnInvalidOrdersCancelRequestError_MapsToValidationFailedNotInternalError()
    {
        var error = new InvalidOrdersCancelRequestError("orders.cancel request is missing or has an empty required field: orderId.");

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("VALIDATION_FAILED", payload.Code);
        Assert.NotEqual("INTERNAL_ERROR", payload.Code);
        Assert.Equal(error.Message, payload.Message);
    }

    /// <summary>Feature <c>orders_cancel_responder</c> — an unknown <c>orderId</c> maps to NOT_FOUND, distinct from <see cref="ReferenceDataNotFoundError"/> above but the same wire code.</summary>
    [Fact]
    public void Map_AnOrderNotFoundError_MapsToNotFound()
    {
        var orderId = Guid.NewGuid();
        var error = new OrderNotFoundError(orderId);

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("NOT_FOUND", payload.Code);
        Assert.Equal(error.Message, payload.Message);
    }

    /// <summary>
    /// Feature <c>orders_cancel_responder</c>, acceptance bullet 3: a
    /// terminal-status cancel refusal maps to the wire-documented
    /// <c>ORDER_NOT_CANCELLABLE</c> code — checked BEFORE the generic
    /// <c>DomainError</c> case, since <see cref="OrderNotCancellableError"/>
    /// IS a <c>DomainError</c> and would otherwise collapse to the less
    /// specific <c>VALIDATION_FAILED</c> this test would then wrongly pass
    /// against.
    /// </summary>
    [Fact]
    public void Map_AnOrderNotCancellableError_MapsToOrderNotCancellableNotValidationFailedNotUnavailable()
    {
        var error = new OrderNotCancellableError(OrderStatus.Invoiced);

        var payload = OrdersCreateErrorMapper.Map(error, _occurredAt);

        Assert.Equal("ORDER_NOT_CANCELLABLE", payload.Code);
        Assert.NotEqual("VALIDATION_FAILED", payload.Code);
        Assert.NotEqual("UNAVAILABLE", payload.Code);
        Assert.Equal("invoiced", payload.Details!["status"]);
    }
}
