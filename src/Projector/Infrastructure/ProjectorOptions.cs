namespace OrderToCash.Projector.Infrastructure;

/// <summary>Kafka consumer settings for the fact stream (mirrors <c>NotificationsKafkaOptions</c>'s shape).</summary>
public sealed class ProjectorKafkaOptions
{
    /// <summary><c>kafka:29092</c> inside compose; <c>localhost:9092</c> for a host process.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Bounded <c>Consume(TimeSpan)</c> poll — returns <see langword="null"/> when nothing arrived, so the cancellation token is observed every cycle.</summary>
    public int PollTimeoutMs { get; set; } = 1_000;
}

/// <summary>
/// MongoDB connection settings — built EXACTLY as <c>SeedMongoConfig.Load()</c>
/// builds them (<c>Uri.EscapeDataString</c> on user and password), re-declared
/// here rather than referenced because the projector must not reference
/// <c>src/Seed</c> (which would drag EF Core in transitively via Orders/
/// Fulfillment/Billing — <c>PR21</c>).
/// </summary>
public sealed class ProjectorMongoOptions
{
    public string ConnectionUri { get; set; } = string.Empty;

    public string Database { get; set; } = "otc_read_model";

    /// <summary>
    /// Builds <see cref="ConnectionUri"/> from the same environment variable
    /// names and the same <see cref="Uri.EscapeDataString(string)"/> handling
    /// <c>SeedMongoConfig.Load()</c> uses — a credential containing <c>@</c>
    /// or <c>/</c> would otherwise silently produce a malformed URI.
    /// </summary>
    public static ProjectorMongoOptions FromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("MONGO_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("MONGO_HOST_PORT") ?? "27017";
        var user = Environment.GetEnvironmentVariable("MONGO_INITDB_ROOT_USERNAME") ?? "otc_mongo_root";
        var password = Environment.GetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD")
            ?? throw new InvalidOperationException(
                "MONGO_INITDB_ROOT_PASSWORD is not set. Export the value from .env before running the projector host, " +
                "e.g.: export $(grep -E '^MONGO_(INITDB_ROOT_PASSWORD|INITDB_ROOT_USERNAME|HOST_PORT|DB_READMODEL)=' .env | xargs)");
        var database = Environment.GetEnvironmentVariable("MONGO_DB_READMODEL") ?? "otc_read_model";

        var escapedUser = Uri.EscapeDataString(user);
        var escapedPassword = Uri.EscapeDataString(password);
        var connectionUri = $"mongodb://{escapedUser}:{escapedPassword}@{host}:{port}/?authSource=admin";

        return new ProjectorMongoOptions { ConnectionUri = connectionUri, Database = database };
    }
}

/// <summary>NATS connection settings — the shared singleton, publish-only.</summary>
public sealed class ProjectorNatsOptions
{
    public string Url { get; set; } = "nats://localhost:4222";
}

/// <summary>The configuration <c>ProjectorServiceCollectionExtensions</c> needs.</summary>
public sealed class ProjectorOptions
{
    public ProjectorKafkaOptions Kafka { get; } = new();

    public ProjectorMongoOptions Mongo { get; set; } = new();

    public ProjectorNatsOptions Nats { get; } = new();
}
