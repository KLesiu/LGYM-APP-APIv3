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
        options.Validate();

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

        if (!inspection.HangfireSchemaUsageGranted)
        {
            throw new InvalidOperationException("Runtime PostgreSQL role is missing USAGE on the Hangfire schema.");
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
                .OrderBy(policy => policy.Name, StringComparer.Ordinal)
                .ThenBy(policy => policy.Command, StringComparer.Ordinal)
                .ToArray();
            var actualPolicies = table.Policies
                .OrderBy(policy => policy.Name, StringComparer.Ordinal)
                .ThenBy(policy => policy.Command, StringComparer.Ordinal)
                .ToArray();
            if (expectedPolicies.Length != actualPolicies.Length
                || !expectedPolicies.Zip(actualPolicies).All(pair => PolicyMatches(pair.First, pair.Second)))
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

    private static bool PolicyMatches(PostgreSqlPolicyOptions expected, PostgreSqlPolicyInspection actual)
        => string.Equals(expected.Name, actual.Name, StringComparison.Ordinal)
            && string.Equals(expected.Command, actual.Command, StringComparison.Ordinal)
            && expected.Roles
                .OrderBy(role => role, StringComparer.Ordinal)
                .SequenceEqual(actual.Roles.OrderBy(role => role, StringComparer.Ordinal), StringComparer.Ordinal)
            && expected.IsPermissive!.Value == actual.IsPermissive
            && string.Equals(expected.Using, actual.Using, StringComparison.Ordinal)
            && string.Equals(expected.WithCheck, actual.WithCheck, StringComparison.Ordinal);

}
