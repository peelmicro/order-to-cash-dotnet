namespace OrderToCash.Billing.Application.Ports;

/// <summary>Allocates <c>INV-######</c> references — one row, one lock, never reassigned (`BI12`, `BI29`).</summary>
public interface IInvoiceNumberAllocator
{
    Task<string> AllocateNextAsync(CancellationToken cancellationToken);
}
