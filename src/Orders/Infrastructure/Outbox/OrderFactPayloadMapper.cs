using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Domain.Events;
using ContractsPayloads = OrderToCash.Contracts.Facts.Payloads;

namespace OrderToCash.Orders.Infrastructure.Outbox;

/// <summary>
/// The ONE place a domain event becomes an
/// <c>OrderToCash.Contracts.Facts.Payloads.*</c> record — design.md §4.4.
/// The domain carries domain types (<see cref="OrderToCash.SharedKernel.Money"/>,
/// <see cref="OrderToCash.SharedKernel.OrderNumber"/>, <see cref="OrderToCash.SharedKernel.GLN"/>,
/// <see cref="OrderToCash.SharedKernel.Quantity"/>) and must never reference
/// <c>Contracts</c> (<c>OrdersDomainMustNotDependOnContracts</c>); this
/// mapper lives in <c>Infrastructure/Outbox/</c> and is where
/// <c>Money.MinorUnits</c> becomes <c>long</c>, <c>OrderNumber</c> and
/// <c>GLN</c> become <c>string</c> and <c>Quantity</c> becomes <c>int</c>.
/// No <c>decimal</c> appears anywhere on this path, in either direction.
/// </summary>
public static class OrderFactPayloadMapper
{
    /// <summary>One method per fact type, dispatched by the event's CLR type. An unmapped event type throws, naming it — this is the writer's second guard, alongside <see cref="OrderToCash.SharedKernel.DomainEventEnvelope.Validate"/>.</summary>
    public static object ToPayload(OrderDomainEvent domainEvent) => domainEvent switch
    {
        OrderPlaced placed => ToOrderPlacedPayload(placed),
        OrderConfirmed confirmed => ToOrderConfirmedPayload(confirmed),
        OrderCompleted completed => ToOrderCompletedPayload(completed),
        OrderCancelled cancelled => ToOrderCancelledPayload(cancelled),
        _ => throw new InvalidOperationException($"OrderFactPayloadMapper has no mapping for event type '{domainEvent.GetType().FullName}' (eventType '{domainEvent.EventType}')."),
    };

    private static ContractsPayloads.OrderPlacedPayload ToOrderPlacedPayload(OrderPlaced placed) => new(
        OrderReference: placed.OrderReference.Value,
        RetailerCode: placed.RetailerCode,
        CompanyCode: placed.CompanyCode,
        BuyerGln: placed.BuyerGln.Value,
        SupplierGln: placed.SupplierGln.Value,
        Currency: placed.Currency,
        OrderDate: placed.OrderDate,
        Lines: [.. placed.Lines.Select(ToOrderLine)],
        InitialAmount: placed.InitialAmount.MinorUnits,
        InitialDiscount: placed.InitialDiscount.MinorUnits,
        TotalAmount: placed.TotalAmount.MinorUnits,
        Notes: placed.Notes);

    private static ContractsPayloads.OrderConfirmedPayload ToOrderConfirmedPayload(OrderConfirmed confirmed) => new(
        OrderReference: confirmed.OrderReference.Value,
        RetailerCode: confirmed.RetailerCode,
        CompanyCode: confirmed.CompanyCode,
        Currency: confirmed.Currency,
        TotalAmount: confirmed.TotalAmount.MinorUnits,
        ConfirmedAt: confirmed.ConfirmedAt);

    private static ContractsPayloads.OrderCompletedPayload ToOrderCompletedPayload(OrderCompleted completed) => new(
        OrderReference: completed.OrderReference.Value,
        RetailerCode: completed.RetailerCode,
        CompanyCode: completed.CompanyCode,
        Currency: completed.Currency,
        TotalAmount: completed.TotalAmount.MinorUnits,
        CompletedAt: completed.CompletedAt);

    private static ContractsPayloads.OrderCancelledPayload ToOrderCancelledPayload(OrderCancelled cancelled) => new(
        OrderReference: cancelled.OrderReference.Value,
        RetailerCode: cancelled.RetailerCode,
        CompanyCode: cancelled.CompanyCode,
        CancellationReason: CancellationReasons.ToToken(cancelled.CancellationReason),
        CancelledAt: cancelled.CancelledAt,
        CompensationSteps: [.. cancelled.CompensationSteps.Select(ToCompensationStep)]);

    private static Contracts.Facts.OrderLine ToOrderLine(OrderPlacedLine line) => new(
        ProductCode: line.ProductCode,
        Description: line.Description,
        Quantity: line.Quantity.Value,
        UnitPrice: line.UnitPrice.MinorUnits,
        LineDiscount: line.LineDiscount.MinorUnits);

    private static Contracts.Facts.CompensationStep ToCompensationStep(OrderCompensationStep step) => new(
        Step: CompensationStepKinds.ToToken(step.Step),
        EventType: step.EventType,
        OccurredAt: step.OccurredAt,
        EventId: step.EventId?.Value,
        Summary: step.Summary);
}
