using OrderToCash.Billing.Application.Commands;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Contracts.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Application;

/// <summary>
/// The hold transactional unit as a plain class the command handler
/// delegates to (design.md §5.3): <c>LockForOrderAsync → null ⇒
/// CreditLineNotFoundError → EvaluateHold → short-circuits → OverLimit ⇒
/// Refuse(over_limit) → Fits ⇒ DecideAsync ⇒ Approve | Refuse(reason) →
/// SaveChangesAsync → map to reply</c>. The reply is built inside the
/// delegate but returned only after <see cref="IUnitOfWork.ExecuteAsync{T}"/>
/// resolves, so a rollback can never have produced a success reply.
/// </summary>
public sealed class CreditHoldService(
    IUnitOfWork unitOfWork,
    IBuyerCreditRepository repository,
    ICreditDecisionPort decisionPort,
    IClock clock)
{
    public Task<CreditHoldReplyPayload> HoldAsync(HoldCreditCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(
            async ct =>
            {
                var orderReference = OrderNumber.Parse(command.OrderReference);
                var credit = await repository.LockForOrderAsync(command.RetailerCode, command.CompanyCode, orderReference, ct).ConfigureAwait(false);

                if (credit is null)
                {
                    throw new CreditLineNotFoundError(command.RetailerCode, command.CompanyCode);
                }

                // BC1/R12: CausationId is the request's own id, never a
                // freshly minted one — the same formula every responder in
                // this repository uses.
                var ctx = new CreditContext(clock.UtcNow, command.RequestId);
                var request = new HoldRequest(orderReference, new Money(command.AmountMinorUnits, command.Currency), command.CorrelationId);

                var evaluation = credit.EvaluateHold(request);

                switch (evaluation)
                {
                    case HoldEvaluation.AlreadyHeld alreadyHeld:
                        // BC7/BC13: no port call, nothing written, no fact.
                        return new CreditHoldReplyPayload(
                            "already_held",
                            command.OrderReference,
                            credit.CreditLimit.Currency,
                            credit.AvailableCredit.MinorUnits,
                            credit.Code,
                            alreadyHeld.HeldAmount.MinorUnits);

                    case HoldEvaluation.CurrencyMismatch mismatch:
                        // BC4: a contract violation, not a credit decision — no ledger entry, no fact.
                        throw new CreditCurrencyMismatchError(mismatch.Expected, command.Currency);

                    case HoldEvaluation.OverLimit overLimit:
                        // BC13: the port is NEVER consulted for an over-limit request.
                        credit.Refuse(request, CreditRejectionReason.OverLimit, ctx, UniqueId.New);
                        await repository.SaveChangesAsync(credit, ct).ConfigureAwait(false);
                        return new CreditHoldReplyPayload(
                            "rejected",
                            command.OrderReference,
                            credit.CreditLimit.Currency,
                            overLimit.AvailableCredit.MinorUnits,
                            credit.Code,
                            Reason: CreditRejectionReasons.ToToken(CreditRejectionReason.OverLimit));

                    case HoldEvaluation.Fits:
                        var decisionRequest = new CreditDecisionRequest(
                            command.OrderReference,
                            command.RetailerCode,
                            command.CompanyCode,
                            credit.Code,
                            command.AmountMinorUnits,
                            command.Currency,
                            credit.AvailableCredit.MinorUnits);

                        var decision = await decisionPort.DecideAsync(decisionRequest, ct).ConfigureAwait(false);

                        switch (decision)
                        {
                            case CreditDecision.Approve:
                                var approvedEntry = credit.Approve(request, ctx, UniqueId.New);
                                await repository.SaveChangesAsync(credit, ct).ConfigureAwait(false);
                                return new CreditHoldReplyPayload(
                                    "approved",
                                    command.OrderReference,
                                    credit.CreditLimit.Currency,
                                    credit.AvailableCredit.MinorUnits,
                                    credit.Code,
                                    approvedEntry.Amount.MinorUnits);

                            case CreditDecision.Refuse refuse:
                                // BC14: this is the SAME Refuse(...) call the OverLimit
                                // branch above uses — one code path for every refusal,
                                // differing only in `reason`.
                                var adapterReason = AdapterRejectionReasons.ToDomainReason(refuse.Reason);
                                credit.Refuse(request, adapterReason, ctx, UniqueId.New);
                                await repository.SaveChangesAsync(credit, ct).ConfigureAwait(false);
                                return new CreditHoldReplyPayload(
                                    "rejected",
                                    command.OrderReference,
                                    credit.CreditLimit.Currency,
                                    credit.AvailableCredit.MinorUnits,
                                    credit.Code,
                                    Reason: CreditRejectionReasons.ToToken(adapterReason));

                            default:
                                throw new InvalidOperationException($"Unrecognised CreditDecision '{decision.GetType().FullName}'.");
                        }

                    default:
                        throw new InvalidOperationException($"Unrecognised HoldEvaluation '{evaluation.GetType().FullName}'.");
                }
            },
            cancellationToken);
}
