using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging;

namespace OrderToCash.Orders.Infrastructure.Saga;

/// <summary>
/// The one implementation of <see cref="IIdempotentSagaRunner"/> — composes
/// the EXISTING, UNMODIFIED <see cref="IdempotentConsumer"/> with
/// <see cref="ConsumerName.OrdersSaga"/> fixed, translating
/// <see cref="ConsumptionOutcome"/> into <see cref="IdempotentSagaRunOutcome"/>
/// (design.md §5.1). Nothing about <see cref="IdempotentConsumer"/> itself
/// changes — this class only wraps it.
/// </summary>
public sealed class IdempotentConsumerSagaRunner(IdempotentConsumer idempotentConsumer) : IIdempotentSagaRunner
{
    public async Task<IdempotentSagaRunOutcome> RunOnceAsync(Guid eventId, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        var outcome = await idempotentConsumer.RunOnceAsync(eventId, ConsumerName.OrdersSaga, work, cancellationToken).ConfigureAwait(false);

        return outcome == ConsumptionOutcome.Duplicate ? IdempotentSagaRunOutcome.Duplicate : IdempotentSagaRunOutcome.Processed;
    }
}
