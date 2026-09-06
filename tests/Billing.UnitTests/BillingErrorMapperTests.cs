using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Application;
using OrderToCash.Billing.Domain.Errors;
using OrderToCash.Billing.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BC27`, ledger `L19` — every TRANSIENT store failure this service can
/// map produces a code the saga adapter treats as retryable, and
/// `CONFLICT` is produced by NOTHING. The terminal set is read from
/// <c>NatsSagaCommandsAdapter</c>'s OWN classification (as TEXT — the two
/// services share no assembly reference) rather than retyped, the
/// <c>StockRpcErrorMapperTests</c> instrument.
/// </summary>
public sealed partial class BillingErrorMapperTests
{
    [Theory]
    [MemberData(nameof(TransientExceptions))]
    public void BC27_MapsEveryTransientStoreFailureToACodeTheSagaAdapterTreatsAsRetryable_NeverToATerminalBusinessCode(Exception exception)
    {
        var terminalSet = ReadTerminalCodeSet();

        var reply = BillingErrorMapper.Map(exception, DateTimeOffset.UtcNow);

        Assert.DoesNotContain(reply.Code, terminalSet);
    }

    [Fact]
    public void ConflictIsProducedByNoInputAtAll()
    {
        var candidates = new Exception[]
        {
            new InvalidCreditRequestError("bad request"),
            new CreditLineNotFoundError("CarrefourEs", "IBERFOODS"),
            new CreditCurrencyMismatchError("EUR", "GBP"),
            new CreditLimitExceededError(1_000, 500),
            new CreditRefusalMismatchError(1_000, 2_000),
            new CreditReleaseUnderflowError("ORD-000001", 500, 600),
            new NoActiveHoldError("ORD-000001"),
            new CreditLedgerOverflowError(new OverflowException()),
            BuildDeadlockException(),
            new DbUpdateConcurrencyException("optimistic concurrency conflict"),
            new InvalidOperationException("anything else"),
            new InvoiceNotFoundError(Guid.NewGuid(), "INV-000001"),
            new PaymentReferenceConflictError("PAY-000001"),
        };

        Assert.All(candidates, exception => Assert.NotEqual("CONFLICT", BillingErrorMapper.Map(exception, DateTimeOffset.UtcNow).Code));
    }

    [Fact]
    public void CreditLedgerOverflowError_MapsToDomainErrorTerminal_NeverRetryable()
    {
        var reply = BillingErrorMapper.Map(new CreditLedgerOverflowError(new OverflowException()), DateTimeOffset.UtcNow);

        Assert.Equal("DOMAIN_ERROR", reply.Code);
        var terminalSet = ReadTerminalCodeSet();
        Assert.Contains(reply.Code, terminalSet);
    }

    // -- Feature 21 (billing_invoicing) additions ---------------------------

    [Fact]
    public void InvalidInvoiceRequestError_MapsToValidationFailed()
    {
        var reply = BillingErrorMapper.Map(new InvalidInvoiceRequestError("bad payload"), DateTimeOffset.UtcNow);
        Assert.Equal("VALIDATION_FAILED", reply.Code);
    }

    [Fact]
    public void InvoiceCurrencyMismatchError_MapsToValidationFailed_CarryingExpectedAndReceived()
    {
        var reply = BillingErrorMapper.Map(new InvoiceCurrencyMismatchError("EUR", "GBP"), DateTimeOffset.UtcNow);

        Assert.Equal("VALIDATION_FAILED", reply.Code);
        Assert.Equal("EUR", reply.Details?["expected"]);
        Assert.Equal("GBP", reply.Details?["received"]);
    }

    [Fact]
    public void EmptyInvoiceLinesError_MapsToValidationFailed_CarryingItsCode()
    {
        var error = new EmptyInvoiceLinesError("ORD-000001");
        var reply = BillingErrorMapper.Map(error, DateTimeOffset.UtcNow);

        Assert.Equal("VALIDATION_FAILED", reply.Code);
        Assert.Equal(error.Code, reply.Details?["code"]);
    }

    [Fact]
    public void InvoiceLineCurrencyMismatchError_MapsToValidationFailed_CarryingItsCode()
    {
        var error = new InvoiceLineCurrencyMismatchError("EUR", "GBP");
        var reply = BillingErrorMapper.Map(error, DateTimeOffset.UtcNow);

        Assert.Equal("VALIDATION_FAILED", reply.Code);
        Assert.Equal(error.Code, reply.Details?["code"]);
    }

    [Fact]
    public void NegativeInvoiceTotalError_MapsToValidationFailed_CarryingItsCode()
    {
        var error = new NegativeInvoiceTotalError(1_000, 2_000);
        var reply = BillingErrorMapper.Map(error, DateTimeOffset.UtcNow);

        Assert.Equal("VALIDATION_FAILED", reply.Code);
        Assert.Equal(error.Code, reply.Details?["code"]);
    }

    [Fact]
    public void InvoiceAlreadyPaidError_MapsToPreconditionFailed_CarryingItsCode()
    {
        var error = new InvoiceAlreadyPaidError("INV-000001");
        var reply = BillingErrorMapper.Map(error, DateTimeOffset.UtcNow);

        Assert.Equal("PRECONDITION_FAILED", reply.Code);
        Assert.Equal(error.Code, reply.Details?["code"]);
    }

    [Fact]
    public void InvoicePaymentAmountMismatchError_MapsToPreconditionFailed_CarryingItsCode()
    {
        var error = new InvoicePaymentAmountMismatchError(1_000, 2_000);
        var reply = BillingErrorMapper.Map(error, DateTimeOffset.UtcNow);

        Assert.Equal("PRECONDITION_FAILED", reply.Code);
        Assert.Equal(error.Code, reply.Details?["code"]);
    }

    [Fact]
    public void InvoicePaymentCurrencyMismatchError_MapsToPreconditionFailed_CarryingItsCode()
    {
        var error = new InvoicePaymentCurrencyMismatchError("EUR", "GBP");
        var reply = BillingErrorMapper.Map(error, DateTimeOffset.UtcNow);

        Assert.Equal("PRECONDITION_FAILED", reply.Code);
        Assert.Equal(error.Code, reply.Details?["code"]);
    }

    // -- Feature 22 (billing_remittance_intake) additions --------------------

    [Fact]
    public void InvoiceNotFoundError_MapsToNotFound_CarryingTheRequestedIdentifiers()
    {
        var invoiceId = Guid.NewGuid();
        var error = new InvoiceNotFoundError(invoiceId, "INV-999999");
        var reply = BillingErrorMapper.Map(error, DateTimeOffset.UtcNow);

        Assert.Equal("NOT_FOUND", reply.Code);
        Assert.Equal(invoiceId, reply.Details?["invoiceId"]);
        Assert.Equal("INV-999999", reply.Details?["invoiceReference"]);
    }

    /// <summary>`BC27` extended to feature 22's own conflict: PRECONDITION_FAILED, DELIBERATELY never CONFLICT — the same reason `CreditLineNotFoundError` et al. are banned from that code (this mapper's class summary).</summary>
    [Fact]
    public void PaymentReferenceConflictError_MapsToPreconditionFailed_NeverConflict_CarryingThePaymentReference()
    {
        var error = new PaymentReferenceConflictError("PAY-000001");
        var reply = BillingErrorMapper.Map(error, DateTimeOffset.UtcNow);

        Assert.Equal("PRECONDITION_FAILED", reply.Code);
        Assert.NotEqual("CONFLICT", reply.Code);
        Assert.Equal("PAY-000001", reply.Details?["paymentReference"]);

        var terminalSet = ReadTerminalCodeSet();
        Assert.Contains(reply.Code, terminalSet);
    }

    /// <summary>`BI25` — TERMINAL on purpose: an overflow retried on every sweep is the failure this mapping prevents (design.md §4.5). ARM: map <c>InvoiceTotalOverflowError</c> to <c>INTERNAL_ERROR</c> and confirm this fails.</summary>
    [Fact]
    public void BI25_MapsInvoiceTotalOverflowErrorToDomainError()
    {
        var error = new InvoiceTotalOverflowError(new OverflowException());
        var reply = BillingErrorMapper.Map(error, DateTimeOffset.UtcNow);

        Assert.Equal("DOMAIN_ERROR", reply.Code);
        Assert.Equal(error.Code, reply.Details?["code"]);

        var terminalSet = ReadTerminalCodeSet();
        Assert.Contains(reply.Code, terminalSet);
    }

    /// <summary>
    /// `BI26` — reads the terminal classification from
    /// <c>NatsSagaCommandsAdapter</c>'s OWN closed set rather than restating
    /// it (`BC27`'s shape), so a `BI5` `NO_ACTIVE_HOLD` refusal stops the
    /// `invoice.issue` command row immediately rather than being retried to
    /// exhaustion. ARM: temporarily map <see cref="NoActiveHoldError"/> to
    /// <c>UNAVAILABLE</c> in <see cref="BillingErrorMapper"/> and confirm
    /// this case fails.
    /// </summary>
    [Fact]
    public void BI26_PreconditionFailedIsInTheSagaDispatchersOwnTerminalSet_SoANoActiveHoldRefusalStopsTheCommandRowRatherThanRetrying()
    {
        var reply = BillingErrorMapper.Map(new NoActiveHoldError("ORD-000001"), DateTimeOffset.UtcNow);

        Assert.Equal("PRECONDITION_FAILED", reply.Code);

        var terminalSet = ReadTerminalCodeSet();
        Assert.Contains(reply.Code, terminalSet);
    }

    public static TheoryData<Exception> TransientExceptions() => new()
    {
        SqlExceptionFactory.WithNumber(1205, "deadlock victim"),
        SqlExceptionFactory.WithNumber(1222, "lock request timeout period exceeded"),
        SqlExceptionFactory.WithNumber(4060, "cannot open database (any other SqlException number)"),
        new DbUpdateConcurrencyException("optimistic concurrency conflict"),
        new InvalidOperationException("something unexpected"),
    };

    private static Exception BuildDeadlockException() => SqlExceptionFactory.WithNumber(1205, "deadlock victim");

    private static HashSet<string> ReadTerminalCodeSet()
    {
        var adapterPath = RepositoryPaths.Find(Path.Combine("src", "Orders", "Infrastructure", "Messaging", "NatsSagaCommandsAdapter.cs"));
        var source = File.ReadAllText(adapterPath);

        var methodMatch = IsTerminalMethodRegex().Match(source);
        Assert.True(methodMatch.Success, "could not locate IsTerminalRpcErrorCode's switch body in NatsSagaCommandsAdapter.cs");

        var codes = CodeLiteralRegex().Matches(methodMatch.Value).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(codes);

        return codes;
    }

    [GeneratedRegex(@"IsTerminalRpcErrorCode\(string code\) => code switch\s*\{(.*?)\};", RegexOptions.Singleline)]
    private static partial Regex IsTerminalMethodRegex();

    [GeneratedRegex("\"([A-Z_]+)\"")]
    private static partial Regex CodeLiteralRegex();
}
