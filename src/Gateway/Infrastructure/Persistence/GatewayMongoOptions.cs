namespace OrderToCash.Gateway.Infrastructure.Persistence;

/// <summary>
/// MongoDB connection settings, built EXACTLY as <c>ProjectorMongoOptions.FromEnvironment</c>
/// builds them (<c>src/Projector/Infrastructure/ProjectorOptions.cs</c>) —
/// re-declared here, not referenced, for the same reason
/// <see cref="GatewayReadModelCollection"/> is its own copy: the Gateway
/// must not reference the Projector project.
/// </summary>
public sealed class GatewayMongoOptions
{
    public string ConnectionUri { get; set; } = string.Empty;

    public string Database { get; set; } = "otc_read_model";

    public static GatewayMongoOptions FromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("MONGO_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("MONGO_HOST_PORT") ?? "27017";
        var user = Environment.GetEnvironmentVariable("MONGO_INITDB_ROOT_USERNAME") ?? "otc_mongo_root";
        var password = Environment.GetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD")
            ?? throw new InvalidOperationException(
                "MONGO_INITDB_ROOT_PASSWORD is not set. Export the value from .env before running the gateway host, " +
                "e.g.: export $(grep -E '^MONGO_(INITDB_ROOT_PASSWORD|INITDB_ROOT_USERNAME|HOST_PORT|DB_READMODEL)=' .env | xargs)");
        var database = Environment.GetEnvironmentVariable("MONGO_DB_READMODEL") ?? "otc_read_model";

        var escapedUser = Uri.EscapeDataString(user);
        var escapedPassword = Uri.EscapeDataString(password);
        var connectionUri = $"mongodb://{escapedUser}:{escapedPassword}@{host}:{port}/?authSource=admin";

        return new GatewayMongoOptions { ConnectionUri = connectionUri, Database = database };
    }
}
