using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LgymApi.Infrastructure.Data;

internal static class PostgreSqlRuntimePrivilegeInspector
{
    private static readonly string[] RequiredHangfireTables =
    [
        "aggregatedcounter", "counter", "hash", "job", "jobparameter", "jobqueue",
        "list", "lock", "schema", "server", "set", "state"
    ];

    private static readonly string[] RequiredHangfireSequences =
    [
        "aggregatedcounter_id_seq", "counter_id_seq", "hash_id_seq", "job_id_seq",
        "jobparameter_id_seq", "jobqueue_id_seq", "list_id_seq", "set_id_seq", "state_id_seq"
    ];

    public static async Task<PostgreSqlRuntimePrivilegeInspection> InspectAsync(
        AppDbContext dbContext,
        NpgsqlConnection connection,
        string hangfireSchema,
        CancellationToken cancellationToken)
    {
        var schemaExists = await SchemaExistsAsync(connection, hangfireSchema, cancellationToken);
        var schemaUsageGranted = schemaExists
            && await SchemaUsageGrantedAsync(connection, hangfireSchema, cancellationToken);
        var missingTableGrants = await FindMissingTableGrantsAsync(
            dbContext,
            connection,
            hangfireSchema,
            cancellationToken);
        var missingSequenceGrants = await FindMissingSequenceGrantsAsync(
            connection,
            hangfireSchema,
            cancellationToken);

        return new PostgreSqlRuntimePrivilegeInspection(
            schemaExists,
            schemaUsageGranted,
            missingTableGrants,
            missingSequenceGrants);
    }

    private static Task<bool> SchemaExistsAsync(
        NpgsqlConnection connection,
        string schema,
        CancellationToken cancellationToken)
        => ExecuteBooleanAsync(
            connection,
            "SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = @schema);",
            cancellationToken,
            ("schema", schema));

    private static Task<bool> SchemaUsageGrantedAsync(
        NpgsqlConnection connection,
        string schema,
        CancellationToken cancellationToken)
        => ExecuteBooleanAsync(
            connection,
            """
            SELECT COALESCE(has_schema_privilege(current_user, namespace.oid, 'USAGE'), false)
            FROM pg_namespace namespace
            WHERE namespace.nspname = @schema;
            """,
            cancellationToken,
            ("schema", schema));

    private static async Task<IReadOnlyList<string>> FindMissingTableGrantsAsync(
        AppDbContext dbContext,
        NpgsqlConnection connection,
        string hangfireSchema,
        CancellationToken cancellationToken)
    {
        var expectedTables = dbContext.Model.GetEntityTypes()
            .Where(entityType => !entityType.IsOwned() && entityType.GetTableName() is not null)
            .Select(entityType => (Schema: entityType.GetSchema() ?? "public", Name: entityType.GetTableName()!))
            .Concat(RequiredHangfireTables.Select(name => (Schema: hangfireSchema, Name: name)))
            .Distinct()
            .ToArray();
        var missing = new List<string>();
        foreach (var table in expectedTables)
        {
            if (!await RelationPrivilegeGrantedAsync(
                    connection,
                    table.Schema,
                    table.Name,
                    "r",
                    cancellationToken))
            {
                missing.Add($"{table.Schema}.{table.Name}");
            }
        }

        return missing;
    }

    private static async Task<IReadOnlyList<string>> FindMissingSequenceGrantsAsync(
        NpgsqlConnection connection,
        string hangfireSchema,
        CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        foreach (var sequence in RequiredHangfireSequences)
        {
            if (!await RelationPrivilegeGrantedAsync(
                    connection,
                    hangfireSchema,
                    sequence,
                    "S",
                    cancellationToken))
            {
                missing.Add($"{hangfireSchema}.{sequence}");
            }
        }

        var missingPublicSequences = await QueryStringListAsync(connection, """
            SELECT format('%I.%I', namespace.nspname, class.relname)
            FROM pg_class class
            JOIN pg_namespace namespace ON namespace.oid = class.relnamespace
            WHERE class.relkind = 'S'
              AND namespace.nspname = 'public'
              AND NOT (
                  has_sequence_privilege(current_user, class.oid, 'USAGE')
                  AND has_sequence_privilege(current_user, class.oid, 'SELECT'));
            """, cancellationToken);
        missing.AddRange(missingPublicSequences);
        return missing;
    }

    private static Task<bool> RelationPrivilegeGrantedAsync(
        NpgsqlConnection connection,
        string schema,
        string relation,
        string relationKind,
        CancellationToken cancellationToken)
        => ExecuteBooleanAsync(connection, """
            SELECT COALESCE(bool_and(
                CASE WHEN @kind = 'S'
                    THEN has_sequence_privilege(current_user, class.oid, 'USAGE')
                         AND has_sequence_privilege(current_user, class.oid, 'SELECT')
                    ELSE has_table_privilege(current_user, class.oid, 'SELECT')
                         AND has_table_privilege(current_user, class.oid, 'INSERT')
                         AND has_table_privilege(current_user, class.oid, 'UPDATE')
                         AND has_table_privilege(current_user, class.oid, 'DELETE')
                END), false)
            FROM pg_class class
            JOIN pg_namespace namespace ON namespace.oid = class.relnamespace
            WHERE namespace.nspname = @schema
              AND class.relname = @relation
              AND class.relkind = @kind;
            """, cancellationToken,
            ("schema", schema),
            ("relation", relation),
            ("kind", relationKind));

    private static async Task<bool> ExecuteBooleanAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<IReadOnlyList<string>> QueryStringListAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}

internal sealed record PostgreSqlRuntimePrivilegeInspection(
    bool HangfireSchemaExists,
    bool HangfireSchemaUsageGranted,
    IReadOnlyList<string> MissingTableGrants,
    IReadOnlyList<string> MissingSequenceGrants);
