using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.UnitTests;

/// <summary>Runs the delegate INLINE, with no real transaction — exactly the shape a unit test needs to prove "the reply is built from the delegate's return value" without a database.</summary>
internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int ExecuteCount { get; private set; }

    public Func<Task>? BeforeWork { get; set; }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        ExecuteCount++;

        if (BeforeWork is not null)
        {
            await BeforeWork();
        }

        return await work(cancellationToken);
    }

    public async Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        ExecuteCount++;
        await work(cancellationToken);
    }
}

/// <summary>Invokes the delegate, THEN throws — simulating a commit that fails after the domain work has already produced its outcome. Proves the caller sees the failure, never the reply the delegate built.</summary>
internal sealed class ThrowingAfterWorkUnitOfWork : IUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        await work(cancellationToken);
        throw new InvalidOperationException("Simulated commit failure — the transaction rolled back.");
    }

    public async Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        await work(cancellationToken);
        throw new InvalidOperationException("Simulated commit failure — the transaction rolled back.");
    }
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
}

internal sealed class FakeBuyerCreditRepository : IBuyerCreditRepository
{
    public BuyerCredit? LockResult { get; set; }

    public int SaveChangesCallCount { get; private set; }

    public BuyerCredit? Saved { get; private set; }

    public Func<Task>? OnSaveChanges { get; set; }

    public Task<BuyerCredit?> LockForOrderAsync(string retailerCode, string companyCode, OrderNumber orderReference, CancellationToken cancellationToken) =>
        Task.FromResult(LockResult);

    public async Task SaveChangesAsync(BuyerCredit credit, CancellationToken cancellationToken)
    {
        SaveChangesCallCount++;
        Saved = credit;

        if (OnSaveChanges is not null)
        {
            await OnSaveChanges();
        }
    }
}

/// <summary>Records every call it receives — the recording fake `BC13`'s test needs to assert EXACTLY ZERO calls on the over-limit path.</summary>
internal sealed class FakeCreditDecisionPort : ICreditDecisionPort
{
    public CreditDecision Result { get; set; } = new CreditDecision.Approve();

    public int CallCount { get; private set; }

    public CreditDecisionRequest? LastRequest { get; private set; }

    public ValueTask<CreditDecision> DecideAsync(CreditDecisionRequest request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        return ValueTask.FromResult(Result);
    }
}
