using Npgsql;

namespace LgymApi.IntegrationTests;

internal sealed partial class PostgreSqlTutorialRowSecurityTestEnvironment
{
    public async Task ExecuteAdminFormattedAsync(string format, params string[] arguments)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_lease.AdminConnectionString)
        {
            Database = _lease.DatabaseName
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteFormattedAsync(connection, format, arguments);
    }
}
