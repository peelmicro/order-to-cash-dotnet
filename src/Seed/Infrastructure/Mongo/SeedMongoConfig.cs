namespace OrderToCash.Seed.Infrastructure.Mongo;

/// <summary>
/// Reads the MongoDB connection settings from the environment — the same
/// variable names <c>.env</c> already declares
/// (<c>MONGO_INITDB_ROOT_USERNAME</c>/<c>MONGO_INITDB_ROOT_PASSWORD</c>/
/// <c>MONGO_HOST_PORT</c>/<c>MONGO_DB_READMODEL</c>), against the composed
/// stack's <c>otcnet-mongodb</c> service (docker-compose.infra.yml), whose
/// root user authenticates against the <c>admin</c> database.
/// </summary>
public static class SeedMongoConfig
{
    public static (string ConnectionUri, string Database) Load()
    {
        var host = Environment.GetEnvironmentVariable("MONGO_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("MONGO_HOST_PORT") ?? "27017";
        var user = Environment.GetEnvironmentVariable("MONGO_INITDB_ROOT_USERNAME") ?? "otc_mongo_root";
        var password = Environment.GetEnvironmentVariable("MONGO_INITDB_ROOT_PASSWORD")
            ?? throw new InvalidOperationException(
                "MONGO_INITDB_ROOT_PASSWORD is not set. Export the value from .env before running the seed.");
        var database = Environment.GetEnvironmentVariable("MONGO_DB_READMODEL") ?? "otc_read_model";

        var escapedUser = Uri.EscapeDataString(user);
        var escapedPassword = Uri.EscapeDataString(password);
        var connectionUri = $"mongodb://{escapedUser}:{escapedPassword}@{host}:{port}/?authSource=admin";

        return (connectionUri, database);
    }
}
