using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.IntegrationTests;

/// <summary>
/// A controllable <see cref="IFactPublisher"/> — records every call it
/// receives and, if <see cref="OnPublish"/> is set, delegates to it so a
/// test can make ONE call throw, delay forever, or simply observe order
/// (design.md §9.1's "fake publisher — the point is the claim, not the
/// broker").
/// </summary>
internal sealed class FakeFactPublisher : IFactPublisher
{
    private readonly List<IReadOnlyList<PublishableFact>> _calls = [];

    public IReadOnlyList<IReadOnlyList<PublishableFact>> Calls => _calls;

    public Func<IReadOnlyList<PublishableFact>, CancellationToken, Task>? OnPublish { get; set; }

    public async Task PublishAsync(IReadOnlyList<PublishableFact> facts, CancellationToken cancellationToken)
    {
        _calls.Add(facts);

        if (OnPublish is not null)
        {
            await OnPublish(facts, cancellationToken);
        }
    }
}
