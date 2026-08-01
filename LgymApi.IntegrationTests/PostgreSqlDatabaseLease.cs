using Npgsql;

namespace LgymApi.IntegrationTests;

internal sealed class PostgreSqlDatabaseLease : IAsyncDisposable
{
    private const string EnvironmentVariableName = "LGYM_TEST_POSTGRES";
    private const string DatabaseNamePrefix = "lgym_it_";
    private static readonly IPostgreSqlDatabaseLeaseOperations DefaultOperations = new NpgsqlPostgreSqlDatabaseLeaseOperations();
    private readonly string _adminConnectionString;
    private readonly IPostgreSqlDatabaseLeaseOperations _operations;
    private readonly SemaphoreSlim _disposeGate = new(1, 1);
    private int _disposed;

    private PostgreSqlDatabaseLease(
        string adminConnectionString,
        string databaseName,
        string connectionString,
        IPostgreSqlDatabaseLeaseOperations operations)
    {
        _adminConnectionString = adminConnectionString;
        _operations = operations;
        DatabaseName = databaseName;
        ConnectionString = connectionString;
    }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    public static async Task<PostgreSqlDatabaseLease> CreateAsync(CancellationToken cancellationToken = default)
    {
        var adminConnectionString = ReadAdminConnectionString();
        return await CreateWithRedactedNpgsqlAsync(adminConnectionString, DefaultOperations, cancellationToken);
    }

    internal static async Task<PostgreSqlDatabaseLease> CreateWithRedactedNpgsqlAsync(
        string adminConnectionString,
        IPostgreSqlDatabaseLeaseOperations operations,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await CreateAsync(adminConnectionString, operations, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw new InvalidOperationException(
                "Could not create an isolated PostgreSQL test database from LGYM_TEST_POSTGRES. Verify the admin connection and CREATE DATABASE permission. The connection value is redacted.",
                exception);
        }
    }

    internal static async Task<PostgreSqlDatabaseLease> CreateAsync(
        string adminConnectionString,
        IPostgreSqlDatabaseLeaseOperations operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var databaseName = $"{DatabaseNamePrefix}{Guid.NewGuid():N}";

        try
        {
            await operations.CreateDatabaseAsync(adminConnectionString, databaseName, cancellationToken);
            var connectionString = operations.BuildDatabaseConnectionString(adminConnectionString, databaseName);
            return new PostgreSqlDatabaseLease(adminConnectionString, databaseName, connectionString, operations);
        }
        catch (Exception acquisitionException)
        {
            try
            {
                await operations.DropDatabaseAsync(adminConnectionString, databaseName, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                throw new InvalidOperationException(
                    $"Could not complete isolated PostgreSQL test database acquisition and compensating cleanup also failed for '{databaseName}'. The connection value is redacted.",
                    new AggregateException(acquisitionException, cleanupException));
            }

            throw;
        }
    }

    public static async Task<bool> DatabaseExistsAsync(string databaseName, CancellationToken cancellationToken = default)
    {
        var adminConnectionString = ReadAdminConnectionString();
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @databaseName)",
            connection);
        command.Parameters.AddWithValue("databaseName", databaseName);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        await _disposeGate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            try
            {
                await _operations.DropDatabaseAsync(_adminConnectionString, DatabaseName, CancellationToken.None);
            }
            catch (NpgsqlException exception)
            {
                throw new InvalidOperationException(
                    $"Could not remove isolated PostgreSQL test database '{DatabaseName}'. The connection value is redacted.",
                    exception);
            }

            Volatile.Write(ref _disposed, 1);
        }
        finally
        {
            _disposeGate.Release();
        }
    }

    private static string ReadAdminConnectionString()
    {
        var configuredConnectionString = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new IgnoreException(
                "PostgreSQL integration tests require LGYM_TEST_POSTGRES to contain an admin PostgreSQL connection.");
        }

        try
        {
            var connectionStringBuilder = new NpgsqlConnectionStringBuilder(configuredConnectionString)
            {
                Pooling = false,
                Timeout = 5,
                CommandTimeout = 30
            };

            return connectionStringBuilder.ConnectionString;
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                "LGYM_TEST_POSTGRES could not be parsed. Supply an admin PostgreSQL connection string. The connection value is redacted.");
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    private sealed class NpgsqlPostgreSqlDatabaseLeaseOperations : IPostgreSqlDatabaseLeaseOperations
    {
        public async Task CreateDatabaseAsync(
            string adminConnectionString,
            string databaseName,
            CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand($"CREATE DATABASE {QuoteIdentifier(databaseName)}", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task DropDatabaseAsync(
            string adminConnectionString,
            string databaseName,
            CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)} WITH (FORCE)",
                connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public string BuildDatabaseConnectionString(string adminConnectionString, string databaseName)
        {
            var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = databaseName
            };

            return builder.ConnectionString;
        }
    }
}

internal interface IPostgreSqlDatabaseLeaseOperations
{
    Task CreateDatabaseAsync(string adminConnectionString, string databaseName, CancellationToken cancellationToken);

    Task DropDatabaseAsync(string adminConnectionString, string databaseName, CancellationToken cancellationToken);

    string BuildDatabaseConnectionString(string adminConnectionString, string databaseName);
}
