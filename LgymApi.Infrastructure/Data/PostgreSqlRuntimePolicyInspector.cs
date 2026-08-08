using Npgsql;

namespace LgymApi.Infrastructure.Data;

internal static class PostgreSqlRuntimePolicyInspector
{
    public static async Task<IReadOnlyList<PostgreSqlPolicyInspection>> QueryAsync(
        NpgsqlConnection connection,
        PostgreSqlProtectedTableOptions expectedTable,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT policy.polname,
                CASE policy.polcmd WHEN 'r' THEN 'SELECT' WHEN 'a' THEN 'INSERT' WHEN 'w' THEN 'UPDATE' WHEN 'd' THEN 'DELETE' ELSE 'ALL' END,
                ARRAY(
                    SELECT CASE WHEN policy_role.role_oid = 0 THEN 'PUBLIC' ELSE role.rolname END
                    FROM unnest(policy.polroles) AS policy_role(role_oid)
                    LEFT JOIN pg_roles role ON role.oid = policy_role.role_oid
                    ORDER BY 1),
                policy.polpermissive,
                pg_get_expr(policy.polqual, policy.polrelid),
                pg_get_expr(policy.polwithcheck, policy.polrelid)
            FROM pg_policy policy
            JOIN pg_class class ON class.oid = policy.polrelid
            JOIN pg_namespace namespace ON namespace.oid = class.relnamespace
            WHERE namespace.nspname = @schema AND class.relname = @table;
            """;
        command.Parameters.AddWithValue("schema", expectedTable.Schema);
        command.Parameters.AddWithValue("table", expectedTable.Name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var policies = new List<PostgreSqlPolicyInspection>();
        while (await reader.ReadAsync(cancellationToken))
        {
            policies.Add(new PostgreSqlPolicyInspection(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetFieldValue<string[]>(2),
                reader.GetBoolean(3),
                PostgreSqlTutorialPolicyExpressions.Classify(reader.IsDBNull(4) ? null : reader.GetString(4)),
                PostgreSqlTutorialPolicyExpressions.Classify(reader.IsDBNull(5) ? null : reader.GetString(5))));
        }

        return policies;
    }
}
