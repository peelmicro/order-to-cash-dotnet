using OrderToCash.Gateway.Application.Ports;
using OrderToCash.Gateway.Domain.Projection;

namespace OrderToCash.Gateway.UnitTests.TestSupport;

/// <summary>A hand-rolled <see cref="IOrderReadModel"/> double, in-memory, no MongoDB.</summary>
public sealed class FakeOrderReadModel : IOrderReadModel
{
    private readonly List<OrderReadModelDocument> _documents = [];

    public void Seed(OrderReadModelDocument document) => _documents.Add(document);

    public Task<OrderReadModelDocument?> FindByIdAsync(Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult(_documents.FirstOrDefault(d => d.OrderId == orderId));

    public Task<OrderReadModelDocument?> FindByOrderReferenceAsync(string orderReference, CancellationToken cancellationToken) =>
        Task.FromResult(_documents.FirstOrDefault(d => d.OrderReference == orderReference));

    public Task<OrderListResult> ListAsync(OrderListFilter filter, CancellationToken cancellationToken) =>
        Task.FromResult(new OrderListResult(_documents, _documents.Count));
}
