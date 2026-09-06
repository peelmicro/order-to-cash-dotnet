using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Application;
using OrderToCash.Billing.Domain.Errors;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Presentation.Rpc;

/// <summary>
/// A pure function, the shape of <c>StockErrorMapper</c>/
/// <c>OrdersCreateErrorMapper</c> but with this service's own cases
/// (design.md §4.5). <b><c>CONFLICT</c> is banned from this service's
/// mapper</b> (`BC27`) — not a style preference:
/// <c>NatsSagaCommandsAdapter.IsTerminalRpcErrorCode</c> classifies
/// <c>CONFLICT</c> as a TERMINAL business rejection, so a deadlock victim
/// answered <c>CONFLICT</c> would mark the <c>saga_commands</c> row
/// <c>rejected</c> — permanently ending the order's saga over a failure
/// that was only ever transient.
/// </summary>
/// <remarks>
/// Renamed from <c>CreditErrorMapper</c> (`BI31`, design.md §4.1): this
/// mapper is no longer credit-specific — feature 21 (invoicing) adds its own
/// cases ahead of the generic <see cref="DomainError"/> fallback, and
/// <c>NoActiveHoldError</c>/<c>CreditLineNotFoundError</c> are REUSED
/// UNCHANGED (already mapped correctly, not touched here).
/// </remarks>
public static class BillingErrorMapper
{
    // SQL Server error numbers — 1205: deadlock victim; 1222: lock request
    // timeout period exceeded.
    private const int DeadlockVictim = 1205;
    private const int LockRequestTimeout = 1222;

    public static RpcErrorPayload Map(Exception error, DateTimeOffset occurredAt) => error switch
    {
        InvalidCreditRequestError e => new RpcErrorPayload("VALIDATION_FAILED", e.Message, OccurredAt: occurredAt),

        InvalidInvoiceRequestError e => new RpcErrorPayload("VALIDATION_FAILED", e.Message, OccurredAt: occurredAt),

        CreditLineNotFoundError e => new RpcErrorPayload(
            "NOT_FOUND",
            e.Message,
            new Dictionary<string, object?> { ["retailerCode"] = e.RetailerCode, ["companyCode"] = e.CompanyCode },
            OccurredAt: occurredAt),

        CreditCurrencyMismatchError e => new RpcErrorPayload(
            "VALIDATION_FAILED",
            e.Message,
            new Dictionary<string, object?> { ["expected"] = e.Expected, ["received"] = e.Received },
            OccurredAt: occurredAt),

        InvoiceCurrencyMismatchError e => new RpcErrorPayload(
            "VALIDATION_FAILED",
            e.Message,
            new Dictionary<string, object?> { ["expected"] = e.Expected, ["received"] = e.Received },
            OccurredAt: occurredAt),

        // BI11: statements about the REQUEST's lines, not about an
        // invoice's state — VALIDATION_FAILED, not a domain-state refusal.
        NegativeInvoiceTotalError e => new RpcErrorPayload(
            "VALIDATION_FAILED",
            e.Message,
            new Dictionary<string, object?> { ["code"] = e.Code },
            OccurredAt: occurredAt),

        EmptyInvoiceLinesError e => new RpcErrorPayload(
            "VALIDATION_FAILED",
            e.Message,
            new Dictionary<string, object?> { ["code"] = e.Code },
            OccurredAt: occurredAt),

        InvoiceLineCurrencyMismatchError e => new RpcErrorPayload(
            "VALIDATION_FAILED",
            e.Message,
            new Dictionary<string, object?> { ["code"] = e.Code },
            OccurredAt: occurredAt),

        // BI25: TERMINAL on purpose — a permanently-overflowing payload
        // retried on every sweep is the failure this mapping prevents.
        InvoiceTotalOverflowError e => new RpcErrorPayload(
            "DOMAIN_ERROR",
            e.Message,
            new Dictionary<string, object?> { ["code"] = e.Code },
            OccurredAt: occurredAt),

        // Feature 22's own subject maps these; declared here so the
        // vocabulary is complete on delivery (design.md §4.5).
        InvoiceAlreadyPaidError e => new RpcErrorPayload(
            "PRECONDITION_FAILED",
            e.Message,
            new Dictionary<string, object?> { ["code"] = e.Code },
            OccurredAt: occurredAt),

        InvoicePaymentAmountMismatchError e => new RpcErrorPayload(
            "PRECONDITION_FAILED",
            e.Message,
            new Dictionary<string, object?> { ["code"] = e.Code },
            OccurredAt: occurredAt),

        InvoicePaymentCurrencyMismatchError e => new RpcErrorPayload(
            "PRECONDITION_FAILED",
            e.Message,
            new Dictionary<string, object?> { ["code"] = e.Code },
            OccurredAt: occurredAt),

        // BC30: a ledger whose sums overflow will overflow again on every
        // retry — TERMINAL, deliberately, not folded into "any other
        // DomainError" only because its own row in the mapping table names
        // it, matching design.md §4.5.
        CreditLedgerOverflowError e => new RpcErrorPayload(
            "DOMAIN_ERROR",
            e.Message,
            new Dictionary<string, object?> { ["code"] = e.Code },
            OccurredAt: occurredAt),

        CreditReleaseUnderflowError e => new RpcErrorPayload(
            "PRECONDITION_FAILED",
            e.Message,
            new Dictionary<string, object?> { ["code"] = e.Code },
            OccurredAt: occurredAt),

        NoActiveHoldError e => new RpcErrorPayload(
            "PRECONDITION_FAILED",
            e.Message,
            new Dictionary<string, object?> { ["code"] = e.Code },
            OccurredAt: occurredAt),

        // Any other aggregate refusal — DOMAIN_ERROR, terminal. Includes
        // InvalidInvoiceSnapshotError — a programming-error guard rather
        // than client input (design.md §4.5).
        DomainError e => new RpcErrorPayload(
            "DOMAIN_ERROR",
            e.Message,
            new Dictionary<string, object?> { ["code"] = e.Code },
            OccurredAt: occurredAt),

        // TRANSIENT — never CONFLICT (see the class summary).
        DbUpdateConcurrencyException e => new RpcErrorPayload("UNAVAILABLE", e.Message, OccurredAt: occurredAt),
        SqlException e when e.Number is DeadlockVictim or LockRequestTimeout => new RpcErrorPayload("UNAVAILABLE", e.Message, OccurredAt: occurredAt),
        SqlException e => new RpcErrorPayload("UNAVAILABLE", e.Message, OccurredAt: occurredAt),

        RequestDeadlineElapsedError e => new RpcErrorPayload("TIMEOUT", e.Message, OccurredAt: occurredAt),

        _ => new RpcErrorPayload("INTERNAL_ERROR", error.Message, OccurredAt: occurredAt),
    };
}

/// <summary>The request's own deadline elapsing (design.md §4.5) — feature 27's <c>x-deadline-ms</c> enforcement is not wired yet; this type exists so the mapping table is complete and future-proof.</summary>
public sealed class RequestDeadlineElapsedError(string message) : Exception(message);
