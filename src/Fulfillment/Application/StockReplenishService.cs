using OrderToCash.Cqrs;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Application;

/// <summary>The <c>fulfillment.stock.replenish</c> command. Not a saga command, not idempotent by design (`R61`, design.md §4.5) — a repeat applies again.</summary>
public sealed record ReplenishStockCommand(string CompanyCode, IReadOnlyList<StockReplenishRequestLine> Lines) : ICommand<StockReplenishReplyPayload>;

/// <summary>The replenish transactional unit (design.md §5.3) — all-or-nothing across lines (`FS14`), overflow-guarded (`FS20`), and writes NO outbox row: a top-up is an operational act, not a saga-visible fact (`R61`).</summary>
public sealed class StockReplenishService(IUnitOfWork unitOfWork, IStockItemRepository repository)
{
    public Task<StockReplenishReplyPayload> ReplenishAsync(ReplenishStockCommand command, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(
            async ct =>
            {
                var productCodes = command.Lines.Select(line => line.ProductCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var items = await repository.LockItemsAsync(command.CompanyCode, productCodes, ct).ConfigureAwait(false);

                // All-or-nothing (FS14): every line must resolve to a known
                // item BEFORE any Replenish() call mutates anything.
                foreach (var line in command.Lines)
                {
                    if (!items.ContainsKey(line.ProductCode))
                    {
                        throw new UnknownStockItemError(command.CompanyCode, line.ProductCode);
                    }
                }

                var affected = new List<Domain.StockItem>();
                foreach (var line in command.Lines)
                {
                    var item = items[line.ProductCode];
                    item.Replenish(new Quantity(line.Units));

                    if (!affected.Contains(item))
                    {
                        affected.Add(item);
                    }
                }

                await repository.SaveChangesAsync(ct).ConfigureAwait(false);

                return new StockReplenishReplyPayload([.. affected.Select(ToView)]);
            },
            cancellationToken);

    private static StockViewPayload ToView(Domain.StockItem item) =>
        new(item.CompanyCode, item.ProductCode, item.Units, item.ReservedUnits, item.AvailableUnits, item.LowStockThreshold);
}
