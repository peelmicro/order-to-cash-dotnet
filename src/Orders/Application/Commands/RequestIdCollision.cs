using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace OrderToCash.Orders.Application.Commands;

/// <summary>
/// Feature <c>observability_reliability</c>, <c>RI3</c>, design.md §2.4,
/// ledger L3 — distinguishes a duplicate-key error on
/// <c>uq_orders_request_id</c> from one on <c>orders</c>'s OTHER unique
/// index (<c>order_reference</c>), which must propagate unchanged rather
/// than being mistaken for a benign request-id race.
/// </summary>
/// <remarks>
/// <c>Microsoft.Data.SqlClient</c> exposes no structured index name on a
/// duplicate-key error — the name appears only inside <see cref="SqlException.Message"/>
/// (captured verbatim from a real <c>mssql</c> container in
/// <c>progress/impl_observability_reliability.md</c>: <em>"Cannot insert
/// duplicate key row in object 'dbo.orders' with unique index
/// 'uq_orders_request_id'. The duplicate key value is (…)."</em>). Both SQL
/// error 2601 (duplicate key on a unique INDEX — what EF Core's
/// <c>HasIndex().IsUnique()</c> actually generates here) and 2627
/// (duplicate key on a unique CONSTRAINT) are matched, because a future
/// change from a unique index to a unique constraint would swap which one
/// is raised and this check must not silently stop working.
/// </remarks>
internal static class RequestIdCollision
{
    private const string IndexName = "uq_orders_request_id";

    public static bool Matches(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sql
        && sql.Message.Contains(IndexName, StringComparison.Ordinal);
}
