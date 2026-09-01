namespace OrderToCash.Orders.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_orders.retailers` — the buyers (Databases doc
/// §4.1). Same shape as <see cref="Company"/>, kept as a separate table
/// because the two roles never share identity.
/// </summary>
public sealed class Retailer
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string Vat { get; set; } = string.Empty;

    public string Gln { get; set; } = string.Empty;

    public Guid CurrencyId { get; set; }

    public DateTime? DisabledAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
