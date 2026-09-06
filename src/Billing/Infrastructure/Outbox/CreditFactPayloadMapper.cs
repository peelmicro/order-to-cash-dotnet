using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Events;
using ContractsPayloads = OrderToCash.Contracts.Facts.Payloads;

namespace OrderToCash.Billing.Infrastructure.Outbox;

/// <summary>
/// The ONE place a domain event becomes an
/// <c>OrderToCash.Contracts.Facts.Payloads.*</c> record — the
/// <c>OrderFactPayloadMapper</c>/<c>StockFactPayloadMapper</c> shape
/// (design.md §8.2.3). The domain carries domain types
/// (<see cref="OrderToCash.SharedKernel.Money"/>,
/// <see cref="OrderToCash.SharedKernel.OrderNumber"/>) and must never
/// reference <c>Contracts</c> for the ENVELOPE; this mapper lives in
/// <c>Infrastructure/Outbox/</c> and is where <c>Money.MinorUnits</c>
/// becomes <c>long</c>. No <c>decimal</c> appears anywhere on this path.
/// </summary>
public sealed class CreditFactPayloadMapper : IFactPayloadMapper
{
    public object ToPayload(FactEvent domainEvent) => domainEvent switch
    {
        CreditApproved approved => new ContractsPayloads.CreditApprovedPayload(
            OrderReference: approved.OrderReference.Value,
            RetailerCode: approved.RetailerCode,
            CompanyCode: approved.CompanyCode,
            CreditCode: approved.CreditCode,
            Currency: approved.HeldAmount.Currency,
            HeldAmount: approved.HeldAmount.MinorUnits,
            AvailableCreditAfter: approved.AvailableCreditAfter.MinorUnits),

        CreditRejected rejected => new ContractsPayloads.CreditRejectedPayload(
            OrderReference: rejected.OrderReference.Value,
            RetailerCode: rejected.RetailerCode,
            CompanyCode: rejected.CompanyCode,
            Currency: rejected.RequestedAmount.Currency,
            RequestedAmount: rejected.RequestedAmount.MinorUnits,
            AvailableCredit: rejected.AvailableCredit.MinorUnits,
            Reason: CreditRejectionReasons.ToToken(rejected.Reason),
            CreditCode: rejected.CreditCode),

        CreditReleased released => new ContractsPayloads.CreditReleasedPayload(
            OrderReference: released.OrderReference.Value,
            RetailerCode: released.RetailerCode,
            CompanyCode: released.CompanyCode,
            Currency: released.ReleasedAmount.Currency,
            ReleasedAmount: released.ReleasedAmount.MinorUnits,
            AvailableCreditAfter: released.AvailableCreditAfter.MinorUnits,
            Reason: CreditReleaseReasons.ToToken(released.Reason),
            CreditCode: released.CreditCode),

        _ => throw new InvalidOperationException($"CreditFactPayloadMapper has no mapping for event type '{domainEvent.GetType().FullName}' (eventType '{domainEvent.EventType}')."),
    };
}
