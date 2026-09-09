namespace OrderToCash.Gateway.Domain.Projection;

/// <summary>openapi.yaml <c>PartyRef</c> — <c>gln</c> and <c>code</c> are required, so this shape can only exist once the projector has filled both in.</summary>
public sealed record OrderPartyRef(string Code, string? Name, string Gln);

public sealed record OrderTotalsView(long InitialAmount, long InitialDiscount, long TotalAmount);

/// <summary>openapi.yaml <c>OrderSummary</c> — <c>GET /orders</c>'s row shape.</summary>
public sealed record OrderSummaryView(
    Guid OrderId,
    string OrderReference,
    DateTimeOffset OrderDate,
    OrderPartyRef Retailer,
    OrderPartyRef Company,
    string Status,
    string? CancellationReason,
    string Currency,
    OrderTotalsView Totals,
    DateTimeOffset UpdatedAt);

/// <summary>openapi.yaml <c>OrderDetail</c> — <c>GET /orders/{id}</c>'s document shape, always returned once a document exists at all (placeholder or not — <see cref="HeaderComplete"/> tells the caller which).</summary>
public sealed record OrderDetailView(
    Guid OrderId,
    string? OrderReference,
    DateTimeOffset? OrderDate,
    OrderPartyRef? Retailer,
    OrderPartyRef? Company,
    string Status,
    string? CancellationReason,
    string? Currency,
    OrderTotalsView? Totals,
    IReadOnlyList<OrderReadModelItem> Items,
    OrderReadModelReferences References,
    IReadOnlyList<OrderReadModelEvent> Events,
    bool HeaderComplete,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Pure shaping of <see cref="OrderReadModelDocument"/> into the two
/// openapi.yaml wire shapes <c>OrderSummary</c> and <c>OrderDetail</c>.
/// Ported from #7's <c>apps/gateway/src/domain/projection/order-read-model-mapper.ts</c>
/// (<c>toOrderSummary</c>/<c>toOrderDetail</c>) — same rules, same field
/// shapes.
/// </summary>
public static class OrderReadModelMapper
{
    /// <summary>
    /// <c>GET /orders</c> row shape. Excludes documents whose header is
    /// still a placeholder (R53) rather than emitting a row with a null
    /// <c>orderReference</c> — the list endpoint's schema requires
    /// <c>orderReference</c> (openapi.yaml <c>OrderSummary</c>), so a
    /// placeholder simply is not list-ready yet; its full timeline is
    /// still reachable individually via <c>GET /orders/{id}</c>, which DOES
    /// surface <c>headerComplete: false</c> explicitly.
    /// </summary>
    public static OrderSummaryView? ToOrderSummary(OrderReadModelDocument doc)
    {
        var retailer = ToPartyRef(doc.Retailer);
        var company = ToPartyRef(doc.Company);

        if (doc.OrderReference is null || doc.OrderDate is null || doc.Currency is null
            || retailer is null || company is null || doc.Totals.TotalAmount is null)
        {
            return null;
        }

        return new OrderSummaryView(
            doc.OrderId,
            doc.OrderReference,
            doc.OrderDate.Value,
            retailer,
            company,
            doc.Status,
            doc.CancellationReason,
            doc.Currency,
            new OrderTotalsView(doc.Totals.InitialAmount ?? 0, doc.Totals.InitialDiscount ?? 0, doc.Totals.TotalAmount.Value),
            doc.UpdatedAt);
    }

    public static OrderDetailView ToOrderDetail(OrderReadModelDocument doc)
    {
        var totals = doc.Totals.TotalAmount is null
            ? null
            : new OrderTotalsView(doc.Totals.InitialAmount ?? 0, doc.Totals.InitialDiscount ?? 0, doc.Totals.TotalAmount.Value);

        return new OrderDetailView(
            doc.OrderId,
            doc.OrderReference,
            doc.OrderDate,
            ToPartyRef(doc.Retailer),
            ToPartyRef(doc.Company),
            doc.Status,
            doc.CancellationReason,
            doc.Currency,
            totals,
            doc.Items,
            doc.References,
            doc.Events,
            doc.HeaderComplete,
            doc.UpdatedAt);
    }

    private static OrderPartyRef? ToPartyRef(OrderReadModelParty party)
    {
        if (party.Code is null || party.Gln is null)
        {
            return null;
        }

        return new OrderPartyRef(party.Code, party.Name, party.Gln);
    }
}
