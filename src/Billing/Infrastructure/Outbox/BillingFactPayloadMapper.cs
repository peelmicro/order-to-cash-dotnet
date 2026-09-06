using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Events;
using ContractsInvoiceLine = OrderToCash.Contracts.Facts.InvoiceLine;
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
/// <remarks>
/// Renamed from <c>CreditFactPayloadMapper</c> (`BI31`, design.md §4.1):
/// this mapper is no longer credit-specific — feature 21 adds the two
/// invoicing arms below. This is also the ONE place the nominal type
/// system's own hazard lives (design.md §16 ledger `L23`): a new
/// <c>FactEvent</c> subtype with no arm here compiles fine and throws at
/// RUN TIME, in a transaction, on a path with a live caller — #7's
/// structurally-typed mapper could not have this failure mode at all.
/// </remarks>
public sealed class BillingFactPayloadMapper : IFactPayloadMapper
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

        InvoiceIssued issued => new ContractsPayloads.InvoiceIssuedPayload(
            OrderReference: issued.OrderReference.Value,
            InvoiceReference: issued.InvoiceReference,
            InvoiceDate: issued.InvoiceDate,
            RetailerCode: issued.RetailerCode,
            CompanyCode: issued.CompanyCode,
            Currency: issued.Currency,
            Lines: [.. issued.Lines.Select(l => new ContractsInvoiceLine(l.ProductCode, l.Units.Value, l.UnitPrice.MinorUnits))],
            Amount: issued.Amount.MinorUnits,
            Discount: issued.Discount.MinorUnits,
            TotalAmount: issued.TotalAmount.MinorUnits),

        PaymentReceived received => new ContractsPayloads.PaymentReceivedPayload(
            OrderReference: received.OrderReference.Value,
            InvoiceReference: received.InvoiceReference,
            PaymentReference: received.PaymentReference,
            Currency: received.Amount.Currency,
            Amount: received.Amount.MinorUnits,
            ValueDate: received.ValueDate,
            Source: received.Source),

        _ => throw new InvalidOperationException($"BillingFactPayloadMapper has no mapping for event type '{domainEvent.GetType().FullName}' (eventType '{domainEvent.EventType}')."),
    };
}
