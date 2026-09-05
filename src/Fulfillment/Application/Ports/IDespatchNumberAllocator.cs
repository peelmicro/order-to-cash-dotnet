namespace OrderToCash.Fulfillment.Application.Ports;

/// <summary>
/// Allocates the next <c>DES-######</c> business reference under a row lock
/// on <c>otc_fulfillment.despatch_number_sequences</c> — the sibling of
/// Orders' <c>IOrderNumberAllocator</c>. Called from inside the SAME
/// transaction <see cref="IUnitOfWork"/> opens for creating the despatch, so
/// a rollback returns the number rather than burns it.
/// </summary>
public interface IDespatchNumberAllocator
{
    Task<string> AllocateNextAsync(CancellationToken cancellationToken);
}
