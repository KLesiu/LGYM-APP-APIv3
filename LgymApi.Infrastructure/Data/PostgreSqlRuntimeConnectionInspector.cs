using System.Data.Common;
using Npgsql;

namespace LgymApi.Infrastructure.Data;

internal static class PostgreSqlRuntimeConnectionInspector
{
    public static async Task<PostgreSqlRuntimeInspection> InspectAsync(
        AppDbContext dbContext,
        NpgsqlConnection connection,
        PostgreSqlRuntimeValidationOptions options,
        CancellationToken cancellationToken)
    {
        var session = await InspectSessionAsync(connection, cancellationToken);
        var elevatedMemberships = await QueryStringListAsync(connection, """
            WITH RECURSIVE memberships(role_id) AS (
                SELECT roleid FROM pg_auth_members WHERE member = (SELECT oid FROM pg_roles WHERE rolname = current_user)
                UNION
                SELECT membership.roleid FROM pg_auth_members membership JOIN memberships ON membership.member = memberships.role_id
            )
            SELECT role.rolname
            FROM memberships
            JOIN pg_roles role ON role.oid = memberships.role_id
            WHERE role.rolsuper OR role.rolbypassrls;
            """, cancellationToken);
        var protectedTables = await InspectProtectedTablesAsync(connection, options.ProtectedTables, cancellationToken);
        var helperFunction = options.HelperFunction is null
            ? null
            : await InspectHelperFunctionAsync(connection, options.HelperFunction, cancellationToken);
        var privileges = await PostgreSqlRuntimePrivilegeInspector.InspectAsync(
            dbContext,
            connection,
            options.HangfireSchema,
            cancellationToken);

        return new PostgreSqlRuntimeInspection(
            session.DatabaseName,
            session.CurrentUser,
            session.IsSuperuser,
            session.BypassesRowSecurity,
            elevatedMemberships,
            new NpgsqlConnectionStringBuilder(connection.ConnectionString).Multiplexing,
            privileges.HangfireSchemaExists,
            privileges.HangfireSchemaUsageGranted,
            privileges.MissingTableGrants,
            privileges.MissingSequenceGrants,
            protectedTables,
            helperFunction);
    }

    private static async Task<SessionInspection> InspectSessionAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT current_database(), current_user, role.rolsuper, role.rolbypassrls
            FROM pg_roles role
            WHERE role.rolname = current_user;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Could not inspect the runtime PostgreSQL role.");
        }

        return new SessionInspection(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3));
    }

    private static async Task<IReadOnlyList<PostgreSqlProtectedTableInspection>> InspectProtectedTablesAsync(NpgsqlConnection connection, IEnumerable<PostgreSqlProtectedTableOptions> expectedTables, CancellationToken cancellationToken)
    {
        var inspections = new List<PostgreSqlProtectedTableInspection>();
        foreach (var table in expectedTables)
        {
            inspections.Add(await InspectProtectedTableAsync(connection, table, cancellationToken));
        }

        return inspections;
    }

    private static async Task<PostgreSqlProtectedTableInspection> InspectProtectedTableAsync(NpgsqlConnection connection, PostgreSqlProtectedTableOptions expectedTable, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT class.relrowsecurity, class.relforcerowsecurity, owner.rolname = current_user
            FROM pg_class class
            JOIN pg_namespace namespace ON namespace.oid = class.relnamespace
            JOIN pg_roles owner ON owner.oid = class.relowner
            WHERE namespace.nspname = @schema AND class.relname = @table;
            """;
        AddParameters(command, ("@schema", expectedTable.Schema), ("@table", expectedTable.Name));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new PostgreSqlProtectedTableInspection(expectedTable.Key, false, false, false, []);
        }

        var inspection = new PostgreSqlProtectedTableInspection(expectedTable.Key, reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2), []);
        await reader.CloseAsync();
        return inspection with
        {
            Policies = await PostgreSqlRuntimePolicyInspector.QueryAsync(connection, expectedTable, cancellationToken)
        };
    }

    private static async Task<PostgreSqlHelperFunctionInspection?> InspectHelperFunctionAsync(NpgsqlConnection connection, PostgreSqlHelperFunctionOptions helper, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT procedure.prosecdef,
                COALESCE(array_to_string(procedure.proconfig, ','), '') LIKE '%search_path=pg_catalog%'
                    AND COALESCE(array_to_string(procedure.proconfig, ','), '') NOT LIKE '%search_path=%public%',
                has_function_privilege(current_user, procedure.oid, 'EXECUTE')
            FROM pg_proc procedure
            JOIN pg_namespace namespace ON namespace.oid = procedure.pronamespace
            WHERE namespace.nspname = @schema AND procedure.proname = @name;
            """;
        AddParameters(command, ("@schema", helper.Schema), ("@name", helper.Name));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PostgreSqlHelperFunctionInspection(reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2))
            : null;
    }

    private static async Task<bool> ExecuteBooleanAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<IReadOnlyList<string>> QueryStringListAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameters(command, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static void AddParameters(DbCommand command, params (string Name, object Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }

    private sealed record SessionInspection(string DatabaseName, string CurrentUser, bool IsSuperuser, bool BypassesRowSecurity);
}
