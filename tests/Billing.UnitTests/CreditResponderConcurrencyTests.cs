using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Infrastructure;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.Billing.Presentation;
using OrderToCash.Billing.Presentation.Rpc;
using OrderToCash.Contracts.Rpc;
using OrderToCash.Cqrs;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>`BC21`'s scope-per-request half (design.md §4.1, ledger `L21`) — a real <see cref="IServiceProvider"/>, a fake <see cref="IDispatcher"/>, no NATS connection, no host.</summary>
public sealed class CreditResponderConcurrencyTests
{
    [Fact]
    public async Task BC21_ResolvesADistinctDependencyInjectionScopePerRequest_NeverOnePerResponder()
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

        var responder = new BillingRpcResponder(
            connection: null!,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new BillingResponderOptions()),
            NullLogger<BillingRpcResponder>.Instance);

        var payload = RpcJson.Serialize(new CreditListRequestPayload(null, null));

        await responder.ProcessRequestAsync(CreditSubjects.CreditList, BuildMessage(payload), CancellationToken.None);
        await responder.ProcessRequestAsync(CreditSubjects.CreditList, BuildMessage(payload), CancellationToken.None);

        Assert.Equal(2, observedDispatchers.Count);
        Assert.NotSame(observedDispatchers[0], observedDispatchers[1]);
    }

    private static NatsMsg<byte[]> BuildMessage(byte[] payload) =>
        new(CreditSubjects.CreditList, "reply", payload.Length, null!, payload, null!, default);

    private sealed class SpyDispatcher : IDispatcher
    {
        public Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken) where TCommand : ICommand => Task.CompletedTask;

        public Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken) where TCommand : ICommand<TResult> =>
            Task.FromResult<TResult>(default!);

        public Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken) where TQuery : IQuery<TResult>
        {
            if (typeof(TResult) == typeof(CreditListReplyPayload))
            {
                return Task.FromResult((TResult)(object)new CreditListReplyPayload([], new CreditPageInfo(1, 25, 0)));
            }

            return Task.FromResult<TResult>(default!);
        }

        public Task PublishAsync(object @event, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
