using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Saga;

/// <summary>
/// The <see cref="ISagaCommandSignal"/> implementation — a bounded
/// <see cref="Channel{T}"/> (capacity 1 024, <see cref="BoundedChannelFullMode.DropWrite"/>),
/// singleton (it owns the channel), design.md §5.5. <see cref="Signal"/> is
/// synchronous and never blocks (SO10): <see cref="ChannelWriter{T}.TryWrite"/>
/// either succeeds immediately or drops immediately — there is no awaited
/// path here at all.
/// </summary>
/// <remarks>
/// <see cref="Reader"/> is consumed by <see cref="SagaCommandDispatchWorker"/>'s
/// <c>OrdersSagaDispatchOptions.DegreeOfParallelism</c> PARALLEL consumer
/// loops (backlog id 80), so <c>SingleReader</c> is deliberately
/// <see langword="false"/> — it was <see langword="true"/> before this fix,
/// which is exactly what made the single-worker predecessor's assumption
/// (one reader draining strictly in order) a correctness requirement rather
/// than an accident: the fix could not simply add more reader loops without
/// this flag also changing, or it would have been undefined behaviour
/// rather than a supported concurrent-read configuration
/// (<c>System.Threading.Channels</c>' own contract).
/// </remarks>
public sealed class ChannelSagaCommandSignal : ISagaCommandSignal
{
    private const int Capacity = 1_024;

    private readonly Channel<SagaCommandRef> _channel = Channel.CreateBounded<SagaCommandRef>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = false, SingleWriter = false });

    private readonly ILogger<ChannelSagaCommandSignal> _logger;

    public ChannelSagaCommandSignal(ILogger<ChannelSagaCommandSignal> logger) => _logger = logger;

    public ChannelReader<SagaCommandRef> Reader => _channel.Reader;

    public void Signal(SagaCommandRef commandRef)
    {
        if (!_channel.Writer.TryWrite(commandRef))
        {
            // Safe to drop: the row is already committed `pending` (SO3),
            // and SagaCommandSweeper re-issues any pending row older than
            // PendingGraceMs — the recovery path, not data loss.
            _logger.LogWarning(
                "Saga command signal channel is full; dropped the signal for order {OrderId}, command {Command}. " +
                "The durable saga_commands row will be picked up by the next sweep.",
                commandRef.OrderId,
                commandRef.Command);
        }
    }
}
