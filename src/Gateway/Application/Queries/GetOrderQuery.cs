using OrderToCash.Cqrs;
using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Domain.Orders;
using OrderToCash.Gateway.Domain.Projection;

namespace OrderToCash.Gateway.Application.Queries;

public enum GetOrderResultKind
{
    Found,
    Pending,
    Unknown,
}

public sealed record GetOrderResult(GetOrderResultKind Kind, OrderDetailView? Detail);

/// <summary>
/// <c>GET /orders/{id}</c> (R54, R55). Three-way result — <c>Found</c>
/// (200), <c>Pending</c> (202 projection pending — R55's own clause,
/// scoped to "an order identifier the caller has just been given", so
/// answered ONLY when <see cref="IssuedOrderWindow"/> confirms THIS gateway
/// itself handed out the id recently), <c>Unknown</c> (404 — everything
/// else: a garbage id, a typo, an id nobody was ever given). Ported from
/// #7's <c>apps/gateway/src/application/queries/get-order.query.ts</c>,
/// including review finding F3: before it existed, EVERY read-model miss
/// answered pending, making openapi.yaml's own documented 404 unreachable.
/// </summary>
public sealed record GetOrderQuery(Guid OrderId) : IQuery<GetOrderResult>;

public sealed class GetOrderQueryHandler(IOrderReadModel readModel, IssuedOrderWindow issuedOrders) : IQueryHandler<GetOrderQuery, GetOrderResult>
{
    public async Task<GetOrderResult> HandleAsync(GetOrderQuery query, CancellationToken cancellationToken)
    {
        var doc = await readModel.FindByIdAsync(query.OrderId, cancellationToken).ConfigureAwait(false);
        if (doc is not null)
        {
            return new GetOrderResult(GetOrderResultKind.Found, OrderReadModelMapper.ToOrderDetail(doc));
        }

        if (issuedOrders.IsRecentlyIssued(query.OrderId))
        {
            return new GetOrderResult(GetOrderResultKind.Pending, null);
        }

        return new GetOrderResult(GetOrderResultKind.Unknown, null);
    }
}
