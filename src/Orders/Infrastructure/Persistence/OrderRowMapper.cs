using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using OrderToCash.SharedKernel;
using DomainOrder = OrderToCash.Orders.Domain.Order;
using RowOrder = OrderToCash.Orders.Infrastructure.Persistence.Entities.Order;

namespace OrderToCash.Orders.Infrastructure.Persistence;

/// <summary>
/// Rows &lt;-&gt; <see cref="DomainOrder"/>, exactly as
/// specs/orders_aggregate/design.md §8 fixes: resolving
/// <c>retailerCode</c>/<c>companyCode</c>/<c>currency</c>/<c>productCode</c>
/// against the four reference tables — inside this adapter, never in the
/// domain (§8.3); converting instants per §8.2 (<c>value.UtcDateTime</c> to
/// write, <c>new DateTimeOffset(value, TimeSpan.Zero)</c> to read); reading
/// lines ascending by <c>id</c> (§8.4); and calling <c>Rehydrate</c> with no
/// totals parameters (§8.3 — totals are re-derived, never accepted).
/// </summary>
public static class OrderRowMapper
{
    /// <summary>Rehydrates the aggregate from a persisted row, resolving the four reference tables by id. Never called from <c>Domain/</c> — the domain never sees a reference-table id.</summary>
    public static async Task<DomainOrder> ToDomainAsync(OrdersDbContext db, RowOrder row, CancellationToken cancellationToken)
    {
        var retailer = await db.Retailers.AsNoTracking().SingleAsync(r => r.Id == row.RetailerId, cancellationToken);
        var company = await db.Companies.AsNoTracking().SingleAsync(c => c.Id == row.CompanyId, cancellationToken);
        var currency = await db.Currencies.AsNoTracking().SingleAsync(c => c.Id == row.CurrencyId, cancellationToken);

        var productIds = row.Items.Select(i => i.ProductId).Distinct().ToArray();
        var productsById = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // §8.4: reload order is fixed as ascending `id` — deterministic,
        // stable across reloads, index-supported. `order_items` has no
        // ordering column of its own.
        var lines = row.Items
            .OrderBy(item => item.Id)
            .Select(item => new Domain.OrderLine(
                UniqueId.From(item.Id),
                productsById[item.ProductId].Code,
                item.Description,
                new Quantity(item.Quantity),
                new Money(item.Price, currency.Code),
                new Money(item.Discount, currency.Code)))
            .ToList();

        return DomainOrder.Rehydrate(
            UniqueId.From(row.Id),
            OrderNumber.Parse(row.OrderReference),
            new DateTimeOffset(row.OrderDate, TimeSpan.Zero),
            retailer.Code,
            new GLN(retailer.Gln),
            company.Code,
            new GLN(company.Gln),
            currency.Code,
            OrderStatuses.Parse(row.Status),
            row.CancellationReason is { } token ? CancellationReasons.Parse(token) : null,
            row.Notes,
            lines,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero));
    }

    /// <summary>Builds a brand-new, untracked row (plus its item rows) for an aggregate this adapter has never seen before — the id-resolution half of <see cref="EfCoreOrderRepository.AddAsync"/>.</summary>
    public static async Task<RowOrder> ToNewRowAsync(OrdersDbContext db, DomainOrder order, CancellationToken cancellationToken)
    {
        var retailerId = await db.Retailers.AsNoTracking().Where(r => r.Code == order.RetailerCode).Select(r => r.Id).SingleAsync(cancellationToken);
        var companyId = await db.Companies.AsNoTracking().Where(c => c.Code == order.CompanyCode).Select(c => c.Id).SingleAsync(cancellationToken);
        var currencyId = await db.Currencies.AsNoTracking().Where(c => c.Code == order.Currency).Select(c => c.Id).SingleAsync(cancellationToken);

        var row = new RowOrder
        {
            Id = order.Id.Value,
            OrderReference = order.OrderReference.Value,
            OrderDate = order.OrderDate.UtcDateTime,
            CompanyId = companyId,
            RetailerId = retailerId,
            CurrencyId = currencyId,
            Notes = order.Notes,
            CreatedAt = order.CreatedAt.UtcDateTime,
        };

        await SyncMutableFieldsAsync(db, order, row, cancellationToken);

        return row;
    }

    /// <summary>
    /// Copies every field the aggregate can change after creation — totals,
    /// status, cancellation reason, <c>updatedAt</c> and the line collection
    /// — onto an already-tracked (or brand-new) row. Reconciles
    /// <paramref name="row"/>'s <see cref="RowOrder.Items"/> collection
    /// against <paramref name="order"/>'s current lines by id: a domain line
    /// with no matching row item is a new <see cref="OrderItem"/>; a row
    /// item with no matching domain line has been removed (<c>RemoveLine</c>)
    /// and is deleted by EF's cascade-on-delete tracking of the navigation
    /// collection; a line whose id is present on both sides is updated in
    /// place (<c>ChangeLine</c> keeps the line's id — design.md §5.1).
    /// </summary>
    public static async Task SyncMutableFieldsAsync(OrdersDbContext db, DomainOrder order, RowOrder row, CancellationToken cancellationToken)
    {
        row.InitialAmount = order.InitialAmount.MinorUnits;
        row.InitialDiscount = order.InitialDiscount.MinorUnits;
        row.TotalAmount = order.TotalAmount.MinorUnits;
        row.Status = OrderStatuses.ToToken(order.Status);
        row.CancellationReason = order.CancellationReason is { } reason ? CancellationReasons.ToToken(reason) : null;
        row.UpdatedAt = order.UpdatedAt.UtcDateTime;

        var productCodes = order.Lines.Select(l => l.ProductCode).Distinct(StringComparer.Ordinal).ToArray();
        var productIdsByCode = await db.Products.AsNoTracking()
            .Where(p => productCodes.Contains(p.Code))
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, cancellationToken);

        var domainLineIds = order.Lines.Select(l => l.Id.Value).ToHashSet();
        row.Items.RemoveAll(item => !domainLineIds.Contains(item.Id));

        foreach (var line in order.Lines)
        {
            var productId = productIdsByCode[line.ProductCode];
            var existing = row.Items.Find(item => item.Id == line.Id.Value);

            if (existing is null)
            {
                row.Items.Add(new OrderItem
                {
                    Id = line.Id.Value,
                    OrderId = row.Id,
                    ProductId = productId,
                    Description = line.Description ?? string.Empty,
                    Price = line.UnitPrice.MinorUnits,
                    Quantity = line.Quantity.Value,
                    Discount = line.LineDiscount.MinorUnits,
                    CreatedAt = order.UpdatedAt.UtcDateTime,
                    UpdatedAt = order.UpdatedAt.UtcDateTime,
                });
            }
            else
            {
                existing.ProductId = productId;
                existing.Description = line.Description ?? string.Empty;
                existing.Price = line.UnitPrice.MinorUnits;
                existing.Quantity = line.Quantity.Value;
                existing.Discount = line.LineDiscount.MinorUnits;
                existing.UpdatedAt = order.UpdatedAt.UtcDateTime;
            }
        }
    }
}
