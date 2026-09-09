using OrderToCash.Gateway.Domain.Projection;

namespace OrderToCash.Gateway.Application.Ports;

/// <summary>A page of <see cref="OrderReadModelDocument"/>s, one-indexed to match openapi.yaml's <c>Page</c>/<c>PageSize</c> parameters.</summary>
public sealed record OrderListFilter(
    IReadOnlyList<string>? Status,
    string? RetailerCode,
    string? CompanyCode,
    string? OrderReference,
    int Page,
    int PageSize);

public sealed record OrderListResult(IReadOnlyList<OrderReadModelDocument> Items, long Total);

/// <summary>
/// Read-only access to the projector's <c>order_timeline</c> collection —
/// R54's "served exclusively from the projected read model". Never a
/// write-model client of any kind; <c>Infrastructure/Persistence/MongoOrderReadModel.cs</c>
/// is the ONE implementation, over a direct MongoDB query with no RPC hop
/// (the projector answers no query subject by design).
/// </summary>
public interface IOrderReadModel
{
    Task<OrderReadModelDocument?> FindByIdAsync(Guid orderId, CancellationToken cancellationToken);

    Task<OrderReadModelDocument?> FindByOrderReferenceAsync(string orderReference, CancellationToken cancellationToken);

    Task<OrderListResult> ListAsync(OrderListFilter filter, CancellationToken cancellationToken);
}
