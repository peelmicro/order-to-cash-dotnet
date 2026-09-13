using OrderToCash.Contracts.Rpc;
using OrderToCash.Cqrs;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Application.Commands;

/// <summary>The <c>billing.credit.release</c> command. Carries no <c>reason</c> — this subject always releases with reason <c>order_cancelled</c> (`BC25`).</summary>
public sealed record ReleaseCreditCommand(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    UniqueId CorrelationId,
    UniqueId RequestId) : ICommand<CreditReleaseReplyPayload>;

/// <summary>Thin delegation to <see cref="CreditReleaseService.ReleaseAsync"/>.</summary>
public sealed class ReleaseCreditCommandHandler(CreditReleaseService service) : ICommandHandler<ReleaseCreditCommand, CreditReleaseReplyPayload>
{
    public Task<CreditReleaseReplyPayload> HandleAsync(ReleaseCreditCommand command, CancellationToken cancellationToken) =>
        service.ReleaseAsync(command, cancellationToken);
}
