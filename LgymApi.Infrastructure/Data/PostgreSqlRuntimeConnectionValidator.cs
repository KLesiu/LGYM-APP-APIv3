using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace LgymApi.Infrastructure.Data;

public static class PostgreSqlRuntimeConnectionValidator
{
    public static async Task ValidateAsync(AppDbContext dbContext, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection("PostgreSqlRuntime").Get<PostgreSqlRuntimeValidationOptions>()
            ?? new PostgreSqlRuntimeValidationOptions();
        options.Validate();

        if (dbContext.Database.GetDbConnection() is not NpgsqlConnection connection)
        {
            throw new InvalidOperationException("Staging and Production require an Npgsql runtime connection.");
        }

        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var inspection = await PostgreSqlRuntimeConnectionInspector.InspectAsync(dbContext, connection, options, cancellationToken);
            ValidateInspection(inspection, options);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    public static void ValidateInspection(PostgreSqlRuntimeInspection inspection, PostgreSqlRuntimeValidationOptions options)
    {
        if (!string.Equals(inspection.DatabaseName, options.ExpectedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Runtime PostgreSQL connection targets an unexpected database.");
        }

        if (!string.Equals(inspection.CurrentUser, options.ExpectedRole, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Runtime PostgreSQL connection uses an unexpected role.");
        }

        if (inspection.IsSuperuser || inspection.BypassesRowSecurity || inspection.ElevatedMemberships.Count != 0)
        {
            throw new InvalidOperationException("Runtime PostgreSQL role has prohibited superuser, BYPASSRLS, or elevated role membership.");
        }

        if (inspection.MultiplexingEnabled)
        {
            throw new InvalidOperationException("Runtime PostgreSQL connection must disable multiplexing for the RLS pilot.");
        }

        if (!inspection.HangfireSchemaExists)
        {
            throw new InvalidOperationException("Hangfire schema is missing. Run the offline DataSeeder with --prepare-hangfire before API startup.");
        }

        if (inspection.MissingTableGrants.Count != 0 || inspection.MissingSequenceGrants.Count != 0)
        {
            throw new InvalidOperationException("Runtime PostgreSQL role is missing required DML or sequence privileges.");
        }

        foreach (var expectedTable in options.ProtectedTables)
        {
            var table = inspection.ProtectedTables.SingleOrDefault(candidate => candidate.Key == expectedTable.Key);
            if (table is null)
            {
                throw new InvalidOperationException("A configured protected table is missing from the runtime database.");
            }

            if (table.IsOwnedByRuntimeRole)
            {
                throw new InvalidOperationException("Runtime PostgreSQL role must not own protected tables.");
            }

            if (table.RowSecurityEnabled != expectedTable.RowSecurityEnabled || table.RowSecurityForced != expectedTable.RowSecurityForced)
            {
                throw new InvalidOperationException("Protected-table RLS state does not match the configured runtime expectation.");
            }

            var expectedPolicies = expectedTable.Policies
                .Select(policy => new PostgreSqlPolicyInspection(policy.Name, policy.Command))
                .OrderBy(policy => policy.Name, StringComparer.Ordinal)
                .ThenBy(policy => policy.Command, StringComparer.Ordinal)
                .ToArray();
            var actualPolicies = table.Policies
                .OrderBy(policy => policy.Name, StringComparer.Ordinal)
                .ThenBy(policy => policy.Command, StringComparer.Ordinal)
                .ToArray();
            if (!expectedPolicies.SequenceEqual(actualPolicies))
            {
                throw new InvalidOperationException("Protected-table policies do not match the configured runtime expectation.");
            }
        }

        if (options.HelperFunction is not null &&
            (inspection.HelperFunction is null ||
             inspection.HelperFunction.IsSecurityDefiner ||
             !inspection.HelperFunction.HasSafeSearchPath ||
             !inspection.HelperFunction.HasRequiredExecuteGrant))
        {
            throw new InvalidOperationException("Configured RLS helper function has unsafe security mode, search path, or grants.");
        }
    }

}

public sealed class PostgreSqlRuntimeValidationOptions
{
    public string ExpectedDatabase { get; init; } = string.Empty;
    public string ExpectedRole { get; init; } = string.Empty;
    public string HangfireSchema { get; init; } = "hangfire";
    public List<PostgreSqlProtectedTableOptions> ProtectedTables { get; init; } = [];
    public PostgreSqlHelperFunctionOptions? HelperFunction { get; init; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExpectedDatabase) || string.IsNullOrWhiteSpace(ExpectedRole) || string.IsNullOrWhiteSpace(HangfireSchema))
        {
            throw new InvalidOperationException("PostgreSqlRuntime must configure ExpectedDatabase, ExpectedRole, and HangfireSchema.");
        }

        foreach (var table in ProtectedTables)
        {
            table.Validate();
        }
    }
}

public sealed class PostgreSqlProtectedTableOptions
{
    public string Schema { get; init; } = "public";
    public string Name { get; init; } = string.Empty;
    public bool RowSecurityEnabled { get; init; }
    public bool RowSecurityForced { get; init; }
    public List<PostgreSqlPolicyOptions> Policies { get; init; } = [];
    internal string Key => $"{Schema}.{Name}";

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Schema) || string.IsNullOrWhiteSpace(Name) || Policies.Any(policy => string.IsNullOrWhiteSpace(policy.Name) || string.IsNullOrWhiteSpace(policy.Command)))
        {
            throw new InvalidOperationException("PostgreSqlRuntime protected-table configuration is invalid.");
        }
    }
}

public sealed class PostgreSqlPolicyOptions
{
    public string Name { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
}

public sealed class PostgreSqlHelperFunctionOptions
{
    public string Schema { get; init; } = "public";
    public string Name { get; init; } = string.Empty;
}

public sealed record PostgreSqlRuntimeInspection(
    string DatabaseName,
    string CurrentUser,
    bool IsSuperuser,
    bool BypassesRowSecurity,
    IReadOnlyList<string> ElevatedMemberships,
    bool MultiplexingEnabled,
    bool HangfireSchemaExists,
    IReadOnlyList<string> MissingTableGrants,
    IReadOnlyList<string> MissingSequenceGrants,
    IReadOnlyList<PostgreSqlProtectedTableInspection> ProtectedTables,
    PostgreSqlHelperFunctionInspection? HelperFunction);

public sealed record PostgreSqlProtectedTableInspection(
    string Key,
    bool RowSecurityEnabled,
    bool RowSecurityForced,
    bool IsOwnedByRuntimeRole,
    IReadOnlyList<PostgreSqlPolicyInspection> Policies);

public sealed record PostgreSqlPolicyInspection(string Name, string Command);

public sealed record PostgreSqlHelperFunctionInspection(bool IsSecurityDefiner, bool HasSafeSearchPath, bool HasRequiredExecuteGrant);
