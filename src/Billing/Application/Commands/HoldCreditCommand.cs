using OrderToCash.Contracts.Rpc;
using OrderToCash.Cqrs;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Application.Commands;

/// <summary>The <c>billing.credit.hold</c> command — carries <see cref="CorrelationId"/>/<see cref="RequestId"/> as <see cref="UniqueId"/> (`BC1`), extracted from the request's headers by the responder, never from the payload.</summary>
public sealed record HoldCreditCommand(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    long AmountMinorUnits,
    string Currency,
    UniqueId CorrelationId,
    UniqueId RequestId) : ICommand<CreditHoldReplyPayload>;

/// <summary>Thin delegation to <see cref="CreditHoldService.HoldAsync"/> (design.md §5.1) — the split that keeps the transactional unit a plain class a unit test can <c>new</c> with fakes.</summary>
public sealed class HoldCreditCommandHandler(CreditHoldService service) : ICommandHandler<HoldCreditCommand, CreditHoldReplyPayload>
{
    public Task<CreditHoldReplyPayload> HandleAsync(HoldCreditCommand command, CancellationToken cancellationToken) =>
        service.HoldAsync(command, cancellationToken);
}
