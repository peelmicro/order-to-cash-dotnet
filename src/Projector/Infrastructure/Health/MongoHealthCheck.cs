using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using OrderToCash.Projector.Application.Ports;

namespace OrderToCash.Projector.Infrastructure.Health;

/// <summary>design.md §8.2's <c>readModel</c> check — a real Mongo <c>ping</c> command on the SAME singleton <see cref="IMongoClient"/> the read-model writer shares, bounded by an explicit timeout (design.md §8.3).</summary>
public sealed class MongoHealthCheck(IMongoClient client, IOptions<HealthOptions> options) : IHealthCheck
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);

    public string Name => "readModel";

    public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            var database = client.GetDatabase(options.Value.MongoDatabase);
            await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cts.Token).ConfigureAwait(false);
            return HealthCheckResult.Up();
        }
        catch (Exception ex) when (ex is OperationCanceledException or MongoException or TimeoutException)
        {
            return HealthCheckResult.Down(ex.Message);
        }
    }
}
