using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Application;
using OrderToCash.Fulfillment.Domain.Errors;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Presentation.Rpc;

/// <summary>
/// A pure function, the shape of Orders' <c>OrdersCreateErrorMapper</c> but
/// with this service's own cases (design.md §6.5). <b><c>CONFLICT</c> is
/// banned from this service's mapper</b> — not a style preference:
/// <c>NatsSagaCommandsAdapter.IsTerminalRpcErrorCode</c> classifies
/// <c>CONFLICT</c> as a TERMINAL business rejection, so a deadlock victim
/// answered <c>CONFLICT</c> would mark the <c>saga_commands</c> row
/// <c>rejected</c> — permanently ending the order's saga over a failure that
/// was only ever transient (`FS21`, ledger L7).
/// </summary>
public static class StockErrorMapper
{
    // SQL Server error numbers — 1205: deadlock victim; 1222: lock request
    // timeout period exceeded.
    private const int DeadlockVictim = 1205;
    private const int LockRequestTimeout = 1222;

    public static RpcErrorPayload Map(Exception error, DateTimeOffset occurredAt) => error switch
    {
        InvalidStockRequestError e => new RpcErrorPayload("VALIDATION_FAILED", e.Message, OccurredAt: occurredAt),

        NoKnownStockItemError e => new RpcErrorPayload(
            "NOT_FOUND",
            e.Message,
            new Dictionary<string, object?> { ["companyCode"] = e.CompanyCode },
            OccurredAt: occurredAt),

        UnknownStockItemError e => new RpcErrorPayload(
            "NOT_FOUND",
            e.Message,
            new Dictionary<string, object?> { ["companyCode"] = e.CompanyCode, ["productCode"] = e.ProductCode },
            OccurredAt: occurredAt),

        ReservationTerminalError e => new RpcErrorPayload("PRECONDITION_FAILED", e.Message, OccurredAt: occurredAt),

        // TRANSIENT — never CONFLICT (see the class summary).
        ConcurrentReservationChangeError e => new RpcErrorPayload("UNAVAILABLE", e.Message, OccurredAt: occurredAt),
        DbUpdateConcurrencyException e => new RpcErrorPayload("UNAVAILABLE", e.Message, OccurredAt: occurredAt),
        SqlException e when e.Number is DeadlockVictim or LockRequestTimeout => new RpcErrorPayload("UNAVAILABLE", e.Message, OccurredAt: occurredAt),
        SqlException e => new RpcErrorPayload("UNAVAILABLE", e.Message, OccurredAt: occurredAt),

        // Any other aggregate refusal — DOMAIN_ERROR, terminal.
        DomainError e => new RpcErrorPayload(
            "DOMAIN_ERROR",
            e.Message,
            new Dictionary<string, object?> { ["code"] = e.Code },
            OccurredAt: occurredAt),

        RequestDeadlineElapsedError e => new RpcErrorPayload("TIMEOUT", e.Message, OccurredAt: occurredAt),

        _ => new RpcErrorPayload("INTERNAL_ERROR", error.Message, OccurredAt: occurredAt),
    };
}

/// <summary>The request's own deadline elapsing (design.md §6.5) — feature 27's <c>x-deadline-ms</c> enforcement is not wired yet; this type exists so the mapping table is complete and future-proof.</summary>
public sealed class RequestDeadlineElapsedError(string message) : Exception(message);
