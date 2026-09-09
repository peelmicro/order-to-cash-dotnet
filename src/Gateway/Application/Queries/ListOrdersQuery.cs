using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Domain.Projection;

namespace OrderToCash.Gateway.Application.Queries;

public sealed record PageResult(int Page, int PageSize, long Total);

public sealed record OrderSummaryPageResult(IReadOnlyList<OrderSummaryView> Items, PageResult Page);

/// <summary><c>GET /orders</c> (R54) — served EXCLUSIVELY from the projected read model. No RPC call, no write-model client anywhere in this handler.</summary>
public sealed record ListOrdersQuery(OrderListFilter Filter) : IQuery<OrderSummaryPageResult>;

public sealed class ListOrdersQueryHandler(IOrderReadModel readModel) : IQueryHandler<ListOrdersQuery, OrderSummaryPageResult>
{
    public async Task<OrderSummaryPageResult> HandleAsync(ListOrdersQuery query, CancellationToken cancellationToken)
    {
        var result = await readModel.ListAsync(query.Filter, cancellationToken).ConfigureAwait(false);

        // R53 — a placeholder document (no orderReference yet) is filtered
        // out of the list rather than emitted half-filled; it stays fully
        // reachable via GET /orders/{id}.
        var items = result.Items
            .Select(OrderReadModelMapper.ToOrderSummary)
            .Where(summary => summary is not null)
            .Select(summary => summary!)
            .ToList();

        return new OrderSummaryPageResult(items, new PageResult(query.Filter.Page, query.Filter.PageSize, result.Total));
    }
}
