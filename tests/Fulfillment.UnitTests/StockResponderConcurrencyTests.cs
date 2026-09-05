using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Cqrs;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Infrastructure;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;
using OrderToCash.Fulfillment.Presentation;
using OrderToCash.Fulfillment.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Fulfillment.UnitTests;

/// <summary>`FS18`'s scope-per-request half (design.md §6.2, ledger L6) — a real <see cref="IServiceProvider"/>, a fake <see cref="IDispatcher"/>, no NATS connection, no host.</summary>
public sealed class StockResponderConcurrencyTests
{
    [Fact]
    public async Task FS18_ResolvesADistinctDependencyInjectionScopePerRequest_NeverOnePerResponder()
    {
        var observedDispatchers = new List<object>();
        var services = new ServiceCollection();
        services.AddScoped<IDispatcher>(_ =>
        {
            var dispatcher = new SpyDispatcher();
            observedDispatchers.Add(dispatcher);
            return dispatcher;
        });
        services.AddScoped<IClock, FakeClock>();
        await using var provider = services.BuildServiceProvider();

        var responder = new StockRpcResponder(
            connection: null!,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new StockResponderOptions()),
            NullLogger<StockRpcResponder>.Instance);

        var payload = RpcJson.Serialize(new StockCheckRequestPayload("ACME", [new StockCheckRequestLine("P1", 1)]));

        await responder.ProcessRequestAsync(StockSubjects.StockCheck, BuildMessage(payload), CancellationToken.None);
        await responder.ProcessRequestAsync(StockSubjects.StockCheck, BuildMessage(payload), CancellationToken.None);

        Assert.Equal(2, observedDispatchers.Count);
        Assert.NotSame(observedDispatchers[0], observedDispatchers[1]);
    }

    private static NatsMsg<byte[]> BuildMessage(byte[] payload) =>
        new(StockSubjects.StockCheck, "reply", payload.Length, null!, payload, null!, default);

    private sealed class SpyDispatcher : IDispatcher
    {
        public Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken) where TCommand : ICommand => Task.CompletedTask;

        public Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken) where TCommand : ICommand<TResult> =>
            Task.FromResult<TResult>(default!);

        public Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken) where TQuery : IQuery<TResult> =>
            Task.FromResult<TResult>(default!);

        public Task PublishAsync(object @event, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
