using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.TestUtils;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LgymApi.IntegrationTests;

internal sealed class PostgreSqlTutorialRowSecurityTestEnvironment : IAsyncDisposable
{
    private readonly PostgreSqlDatabaseLease _lease;
    private readonly string _maintenanceRole;
    private readonly string _runtimeRole;
    private PostgreSqlWebApplicationFactory? _factory;

    private PostgreSqlTutorialRowSecurityTestEnvironment(
        PostgreSqlDatabaseLease lease,
        string maintenanceRole,
        string runtimeRole,
        string maintenanceConnectionString,
        string runtimeConnectionString)
    {
        _lease = lease;
        _maintenanceRole = maintenanceRole;
        _runtimeRole = runtimeRole;
        MaintenanceConnectionString = maintenanceConnectionString;
        RuntimeConnectionString = runtimeConnectionString;
    }

    public PostgreSqlWebApplicationFactory Factory => _factory
        ?? throw new InvalidOperationException("The runtime test factory has not been initialized.");

    public string MaintenanceRole => _maintenanceRole;

    public string RuntimeConnectionString { get; }

    private string MaintenanceConnectionString { get; }

    public static async Task<PostgreSqlTutorialRowSecurityTestEnvironment> CreateAsync()
    {
        var lease = await PostgreSqlDatabaseLease.CreateAsync();
        var maintenanceRole = $"lgym_maintenance_it_{Id<PostgreSqlTutorialRowSecurityTestEnvironment>.New().ToString().Replace("-", "", StringComparison.Ordinal)}";
        var runtimeRole = $"lgym_runtime_it_{Id<PostgreSqlTutorialRowSecurityTestEnvironment>.New().ToString().Replace("-", "", StringComparison.Ordinal)}";
        var maintenancePassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        var runtimePassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        var maintenanceConnectionString = CreateRoleConnectionString(lease.ConnectionString, maintenanceRole, maintenancePassword, false);
        var runtimeConnectionString = CreateRoleConnectionString(lease.ConnectionString, runtimeRole, runtimePassword, true);
        var environment = new PostgreSqlTutorialRowSecurityTestEnvironment(
            lease,
            maintenanceRole,
            runtimeRole,
            maintenanceConnectionString,
            runtimeConnectionString);

        try
        {
            await environment.ProvisionAsync(maintenancePassword, runtimePassword);
            environment._factory = PostgreSqlWebApplicationFactory.CreateForPreparedDatabase(lease, runtimeConnectionString);
            return environment;
        }
        catch
        {
            await environment.DisposeAsync();
            throw;
        }
    }

    public async Task<Id<User>> SeedUserAsync(string name, string email)
    {
        await using var database = CreateDbContext(MaintenanceConnectionString);
        var user = await TestDataFactory.SeedUserAsync(database, name, email);
        await database.SaveChangesAsync();
        return user.Id;
    }

    public async Task<(Id<UserTutorialProgress> ProgressId, Id<UserTutorialStepProgress> StepId)> ReadTutorialIdsAsync(Id<User> userId)
    {
        await using var database = CreateDbContext(MaintenanceConnectionString);
        var progress = await database.UserTutorialProgresses
            .Include(candidate => candidate.CompletedSteps)
            .SingleAsync(candidate => candidate.UserId == userId);
        return (progress.Id, progress.CompletedSteps.Single().Id);
    }

    public async Task<TutorialRowSnapshot> ReadTutorialSnapshotAsync(
        Id<UserTutorialProgress> progressId,
        Id<UserTutorialStepProgress> stepId)
    {
        await using var database = CreateDbContext(MaintenanceConnectionString);
        var progress = await database.UserTutorialProgresses.AsNoTracking().SingleAsync(candidate => candidate.Id == progressId);
        var step = await database.UserTutorialStepProgresses.AsNoTracking().SingleAsync(candidate => candidate.Id == stepId);
        return new TutorialRowSnapshot(progress.IsCompleted, progress.CompletedAt, step.CompletedAt);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_factory is not null)
            {
                await _factory.DisposeAsync();
            }
            else
            {
                await _lease.DisposeAsync();
            }
        }
        finally
        {
            await using var adminConnection = new NpgsqlConnection(_lease.AdminConnectionString);
            await adminConnection.OpenAsync();
            await ExecuteFormattedAsync(adminConnection, "DROP ROLE IF EXISTS %I", _runtimeRole);
            await ExecuteFormattedAsync(adminConnection, "DROP ROLE IF EXISTS %I", _maintenanceRole);
        }
    }

    private async Task ProvisionAsync(string maintenancePassword, string runtimePassword)
    {
        await using (var adminConnection = new NpgsqlConnection(_lease.AdminConnectionString))
        {
            await adminConnection.OpenAsync();
            await CreateRoleAsync(adminConnection, _maintenanceRole, maintenancePassword, true);
            await CreateRoleAsync(adminConnection, _runtimeRole, runtimePassword, false);
            await ExecuteFormattedAsync(adminConnection, "REVOKE %I FROM %I", _maintenanceRole, _runtimeRole);
            await ExecuteFormattedAsync(adminConnection, "ALTER DATABASE %I OWNER TO %I", _lease.DatabaseName, _maintenanceRole);
        }

        await using (var database = CreateDbContext(MaintenanceConnectionString))
        {
            await database.Database.MigrateAsync();
            await PrepareHangfireAsync();
            await GrantRuntimeAccessAsync();
            await TestDataFactory.SeedDefaultRolesAsync(database);
            await database.SaveChangesAsync();
        }

        await PostgreSqlTutorialRowSecurityActivation.RunAsync(
            MaintenanceConnectionString,
            _lease.DatabaseName,
            _maintenanceRole,
            _runtimeRole);
    }

    private static async Task CreateRoleAsync(NpgsqlConnection connection, string role, string password, bool maintenance)
    {
        var attributes = maintenance
            ? "NOSUPERUSER BYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION"
            : "NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT";
        await using var formatCommand = new NpgsqlCommand(
            $"SELECT format('CREATE ROLE %I LOGIN {attributes} PASSWORD %L', @role, @password);",
            connection);
        formatCommand.Parameters.AddWithValue("role", role);
        formatCommand.Parameters.AddWithValue("password", password);
        var commandText = (string)(await formatCommand.ExecuteScalarAsync())!;
        await using var command = new NpgsqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task PrepareHangfireAsync()
    {
        var options = new PostgreSqlStorageOptions
        {
            PrepareSchemaIfNecessary = true,
            StartupConnectionMaxRetries = 0,
            AllowDegradedModeWithoutStorage = false
        };
        _ = new PostgreSqlStorage(new NpgsqlConnectionFactory(MaintenanceConnectionString, options, null), options);
        await Task.CompletedTask;
    }

    private async Task GrantRuntimeAccessAsync()
    {
        await using var connection = new NpgsqlConnection(MaintenanceConnectionString);
        await connection.OpenAsync();
        await ExecuteFormattedAsync(connection, "GRANT CONNECT ON DATABASE %I TO %I", _lease.DatabaseName, _runtimeRole);
        await ExecuteFormattedAsync(connection, "GRANT USAGE ON SCHEMA public, hangfire TO %I", _runtimeRole);
        await ExecuteFormattedAsync(connection, "GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public, hangfire TO %I", _runtimeRole);
        await ExecuteFormattedAsync(connection, "GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public, hangfire TO %I", _runtimeRole);
        await ExecuteFormattedAsync(connection, "ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I", _maintenanceRole, _runtimeRole);
        await ExecuteFormattedAsync(connection, "ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO %I", _maintenanceRole, _runtimeRole);
        await ExecuteFormattedAsync(connection, "ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA hangfire GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I", _maintenanceRole, _runtimeRole);
        await ExecuteFormattedAsync(connection, "ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA hangfire GRANT USAGE, SELECT ON SEQUENCES TO %I", _maintenanceRole, _runtimeRole);
    }

    private static AppDbContext CreateDbContext(string connectionString)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

    private static string CreateRoleConnectionString(string baseConnectionString, string role, string password, bool pooling)
        => new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Username = role,
            Password = password,
            Pooling = pooling,
            MaxPoolSize = pooling ? 1 : 100,
            Timeout = 5,
            CommandTimeout = 30
        }.ConnectionString;

    private static async Task ExecuteFormattedAsync(NpgsqlConnection connection, string format, params string[] arguments)
    {
        await using var formatCommand = new NpgsqlCommand(
            $"SELECT format(@format, {string.Join(", ", arguments.Select((_, index) => $"@argument{index}"))});",
            connection);
        formatCommand.Parameters.AddWithValue("format", format);
        for (var index = 0; index < arguments.Length; index++)
        {
            formatCommand.Parameters.AddWithValue($"argument{index}", arguments[index]);
        }

        var commandText = (string)(await formatCommand.ExecuteScalarAsync())!;
        await using var command = new NpgsqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync();
    }

    public sealed record TutorialRowSnapshot(bool IsCompleted, DateTimeOffset? CompletedAt, DateTimeOffset StepCompletedAt);
}
