using OrderToCash.Cqrs;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Fulfillment.Application.Commands;

/// <summary>Thin delegation to <see cref="DespatchCreationService.CreateAsync"/> — the split that keeps the transactional unit a plain class a unit test can <c>new</c> with fakes.</summary>
public sealed class CreateDespatchCommandHandler(DespatchCreationService service) : ICommandHandler<CreateDespatchCommand, DespatchCreateReplyPayload>
{
    public Task<DespatchCreateReplyPayload> HandleAsync(CreateDespatchCommand command, CancellationToken cancellationToken) =>
        service.CreateAsync(command, cancellationToken);
}
