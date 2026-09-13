using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// Feature <c>design_time_dbcontext_factory_env_reads_are_unguarded</c> (id
/// 67) — every test class that mutates process-wide environment variables
/// through <see cref="Environment.SetEnvironmentVariable(string, string?)"/>
/// shares this collection, so xUnit runs them SEQUENTIALLY relative to each
/// other (xUnit parallelises across collections, never within one). Without
/// this, <see cref="OrdersDbContextFactoryTests"/> — which drives the SAME
/// <c>MSSQL_HOST</c>/<c>MSSQL_HOST_PORT</c>/<c>MSSQL_DB_ORDERS</c>/
/// <c>MSSQL_APP_USER</c>/<c>MSSQL_APP_PASSWORD</c> variable names
/// <see cref="OrdersProgramConfigurationTests"/> already mutates — would
/// race against it on a shared process resource, on every parallel test run
/// (the shape <c>GatewayEnvironmentVariableTestCollection</c> was written to
/// close, feature <c>composition_root_env_reads_are_unguarded</c>, id 56).
/// </summary>
[CollectionDefinition(Name)]
public sealed class OrdersEnvironmentVariableTestCollection
{
    public const string Name = "Orders environment variable tests";
}
