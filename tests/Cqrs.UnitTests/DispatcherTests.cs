using Microsoft.Extensions.DependencyInjection;
using OrderToCash.Cqrs;
using OrderToCash.Cqrs.UnitTests.Fixtures;
using Xunit;

namespace OrderToCash.Cqrs.UnitTests;

/// <summary>
/// Proves acceptance items 1 and 2 together: every test resolves
/// <see cref="IDispatcher"/> from a provider built by a single, whole-
/// assembly <c>AddDispatcher(typeof(PingCommand).Assembly)</c> call in the
/// constructor — nothing here is registered by hand, and the four handler
/// shapes plus <c>IDispatcher</c> itself all come out of that one call.
/// </summary>
public sealed class DispatcherTests
{
    private readonly IDispatcher _dispatcher;

    public DispatcherTests()
    {
        var services = new ServiceCollection();
        services.AddDispatcher(typeof(PingCommand).Assembly);
        var provider = services.BuildServiceProvider();
        _dispatcher = provider.GetRequiredService<IDispatcher>();
    }

    [Fact]
    public async Task SendAsync_ReachesTheVoidCommandHandler()
    {
        PingCommandHandler.LastReceived = null;

        await _dispatcher.SendAsync(new PingCommand("hello"), CancellationToken.None);

        Assert.Equal("hello", PingCommandHandler.LastReceived?.Text);
    }

    [Fact]
    public async Task SendAsync_Void_ForwardsTheCancellationTokenToTheHandler()
    {
        PingCommandHandler.LastReceived = null;
        using var cts = new CancellationTokenSource();

        await _dispatcher.SendAsync(new PingCommand("token-check"), cts.Token);

        Assert.Equal(cts.Token, PingCommandHandler.LastReceived?.Token);
    }

    [Fact]
    public async Task SendAsync_ReachesTheResultCommandHandlerAndReturnsItsResult()
    {
        var result = await _dispatcher.SendAsync<CreateWidgetCommand, WidgetCreated>(
            new CreateWidgetCommand("widget-1"), CancellationToken.None);

        Assert.Equal("widget-1", result.Name);
    }

    [Fact]
    public async Task SendAsync_WithResult_ForwardsTheCancellationTokenToTheHandler()
    {
        CreateWidgetCommandHandler.LastToken = null;
        using var cts = new CancellationTokenSource();

        await _dispatcher.SendAsync<CreateWidgetCommand, WidgetCreated>(
            new CreateWidgetCommand("widget-2"), cts.Token);

        Assert.Equal(cts.Token, CreateWidgetCommandHandler.LastToken);
    }

    [Fact]
    public async Task QueryAsync_ReachesTheQueryHandlerAndReturnsItsResult()
    {
        var result = await _dispatcher.QueryAsync<GetWidgetCountQuery, int>(
            new GetWidgetCountQuery(), CancellationToken.None);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task QueryAsync_ForwardsTheCancellationTokenToTheHandler()
    {
        GetWidgetCountQueryHandler.LastToken = null;
        using var cts = new CancellationTokenSource();

        await _dispatcher.QueryAsync<GetWidgetCountQuery, int>(new GetWidgetCountQuery(), cts.Token);

        Assert.Equal(cts.Token, GetWidgetCountQueryHandler.LastToken);
    }

    [Fact]
    public async Task PublishAsync_ReachesEveryRegisteredEventHandler()
    {
        FirstWidgetCreatedFactHandler.LastReceived = null;
        SecondWidgetCreatedFactHandler.LastReceived = null;
        var factId = Guid.NewGuid();

        await _dispatcher.PublishAsync(new WidgetCreatedFact(factId), CancellationToken.None);

        Assert.Equal(factId, FirstWidgetCreatedFactHandler.LastReceived);
        Assert.Equal(factId, SecondWidgetCreatedFactHandler.LastReceived);
    }

    /// <summary>
    /// R (design decision, progress/impl_cqrs_dispatcher.md): a fact may
    /// have zero listeners — the asymmetry against commands and queries,
    /// which fail startup validation on zero. <see cref="UnlistenedFact"/>
    /// has no <see cref="IEventHandler{TEvent}"/> implementation anywhere in
    /// this assembly, and AddDispatcher(typeof(PingCommand).Assembly) in the
    /// constructor above did not throw despite that — this test proves
    /// publishing to it is also silently a no-op at dispatch time, not just
    /// silent at registration time.
    /// </summary>
    [Fact]
    public async Task PublishAsync_WithZeroRegisteredHandlers_CompletesWithoutError()
    {
        await _dispatcher.PublishAsync(new UnlistenedFact(Guid.NewGuid()), CancellationToken.None);
    }

    /// <summary>
    /// progress/review_cqrs_dispatcher.md, D3: PublishAsync must resolve
    /// handlers by the fact's RUNTIME type, not the static type of the
    /// variable at the call site. Publishing through an IUpstreamFact-typed
    /// variable — the shape a mixed IReadOnlyList&lt;IDomainEvent&gt; drain
    /// iterates and publishes through, one element at a time — must still
    /// reach ConcreteUpstreamFactHandler. Before the fix, this bound
    /// TEvent = IUpstreamFact at compile time, found zero handlers for that
    /// (unregistered) service type, and silently completed having called
    /// nothing — a lost fact, not a thrown error, because zero handlers is
    /// deliberately not an error (see the asymmetry tests above).
    /// </summary>
    [Fact]
    public async Task PublishAsync_ThroughABaseOrInterfaceTypedVariable_StillReachesTheHandlerForTheRuntimeType()
    {
        ConcreteUpstreamFactHandler.LastReceived = null;
        var factId = Guid.NewGuid();
        IUpstreamFact upstreamTypedFact = new ConcreteUpstreamFact(factId);

        await _dispatcher.PublishAsync(upstreamTypedFact, CancellationToken.None);

        Assert.Equal(factId, ConcreteUpstreamFactHandler.LastReceived);
    }

    /// <summary>
    /// progress/review_cqrs_dispatcher.md, D8: the D3 fix reaches the event
    /// handler through <see cref="System.Reflection.MethodInfo.Invoke(object?,object?[]?)"/>
    /// rather than a direct, compile-time-checked call, so CA2016 — an
    /// error in this repository — cannot see through it and can no longer
    /// prove the <see cref="CancellationToken"/> is forwarded on this path.
    /// The three SendAsync/QueryAsync forwarding tests above are covered by
    /// both the analyzer and a test; this is PublishAsync's replacement for
    /// the analyzer coverage the D3 rewrite removed.
    /// </summary>
    [Fact]
    public async Task PublishAsync_ForwardsTheCancellationTokenToTheHandler()
    {
        ConcreteUpstreamFactHandler.LastToken = null;
        using var cts = new CancellationTokenSource();
        IUpstreamFact upstreamTypedFact = new ConcreteUpstreamFact(Guid.NewGuid());

        await _dispatcher.PublishAsync(upstreamTypedFact, cts.Token);

        Assert.Equal(cts.Token, ConcreteUpstreamFactHandler.LastToken);
    }
}
