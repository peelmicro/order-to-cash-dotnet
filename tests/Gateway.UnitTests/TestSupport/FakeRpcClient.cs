using OrderToCash.Gateway.Application.Ports;

namespace OrderToCash.Gateway.UnitTests.TestSupport;

/// <summary>A hand-rolled <see cref="IRpcClient"/> double — records every call it received and answers a pre-programmed reply or throws a pre-programmed <see cref="RpcCallError"/>, per subject.</summary>
public sealed class FakeRpcClient : IRpcClient
{
    public sealed record RecordedCall(string Subject, object Payload, RpcCallMeta Meta);

    public List<RecordedCall> Calls { get; } = [];

    private readonly Dictionary<string, Queue<object>> _replies = new();
    private readonly Dictionary<string, RpcCallError> _errors = new();

    public void EnqueueReply(string subject, object reply)
    {
        if (!_replies.TryGetValue(subject, out var queue))
        {
            queue = new Queue<object>();
            _replies[subject] = queue;
        }

        queue.Enqueue(reply);
    }

    public void FailNextCall(string subject, RpcCallError error) => _errors[subject] = error;

    public Task<TReply> CallAsync<TRequest, TReply>(string subject, TRequest payload, RpcCallMeta meta, CancellationToken cancellationToken)
    {
        Calls.Add(new RecordedCall(subject, payload!, meta));

        if (_errors.TryGetValue(subject, out var error))
        {
            _errors.Remove(subject);
            throw error;
        }

        if (_replies.TryGetValue(subject, out var queue) && queue.Count > 0)
        {
            return Task.FromResult((TReply)queue.Dequeue());
        }

        throw new InvalidOperationException($"FakeRpcClient has no reply queued for subject '{subject}'.");
    }
}
