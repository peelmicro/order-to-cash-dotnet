using OrderToCash.Billing.Application.Commands;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Contracts.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Application;

/// <summary>
/// The release transactional unit (design.md §5.3/§5.6): the same lock
/// protocol as <see cref="CreditHoldService"/>, then
/// <c>Release(orderReference, order_cancelled, ...)</c>; <c>released: false</c>
/// and no write when the aggregate returns <see langword="null"/> (`BC11`).
/// </summary>
public sealed class CreditReleaseService(
    IUnitOfWork unitOfWork,
    IBuyerCreditRepository repository,
    IClock clock)
{
    public Task<CreditReleaseReplyPayload> ReleaseAsync(ReleaseCreditCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(
            async ct =>
            {
                var orderReference = OrderNumber.Parse(command.OrderReference);
                var credit = await repository.LockForOrderAsync(command.RetailerCode, command.CompanyCode, orderReference, ct).ConfigureAwait(false);

                if (credit is null)
                {
                    throw new CreditLineNotFoundError(command.RetailerCode, command.CompanyCode);
                }

                var ctx = new CreditContext(clock.UtcNow, command.RequestId);

                // BC25: reason is not a caller-supplied field on this subject — always order_cancelled.
                var released = credit.Release(orderReference, CreditReleaseReason.OrderCancelled, command.CorrelationId, ctx, UniqueId.New);

                if (released is null)
                {
                    // BC11: no outstanding exposure — a plain success no-op, no write, no fact.
                    return new CreditReleaseReplyPayload(
                        false,
                        command.OrderReference,
                        credit.AvailableCredit.MinorUnits,
                        credit.Code,
                        credit.CreditLimit.Currency);
                }

                await repository.SaveChangesAsync(credit, ct).ConfigureAwait(false);

                return new CreditReleaseReplyPayload(
                    true,
                    command.OrderReference,
                    credit.AvailableCredit.MinorUnits,
                    credit.Code,
                    credit.CreditLimit.Currency,
                    released.Amount.MinorUnits);
            },
            cancellationToken);
}
