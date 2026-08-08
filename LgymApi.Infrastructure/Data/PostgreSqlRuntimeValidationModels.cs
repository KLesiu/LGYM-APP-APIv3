namespace LgymApi.Infrastructure.Data;

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
        if (string.IsNullOrWhiteSpace(Schema) || string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("PostgreSqlRuntime protected-table configuration is invalid.");
        }

        foreach (var policy in Policies)
        {
            policy.Validate();
        }
    }
}

public sealed class PostgreSqlPolicyOptions
{
    public string Name { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public List<string> Roles { get; init; } = [];
    public bool? IsPermissive { get; init; }
    public string? Using { get; init; }
    public string? WithCheck { get; init; }

    internal void Validate()
    {
        var commandIsKnown = Command is "SELECT" or "INSERT" or "UPDATE" or "DELETE";
        var rolesAreValid = Roles.Count != 0
            && Roles.All(role => !string.IsNullOrWhiteSpace(role))
            && Roles.Distinct(StringComparer.Ordinal).Count() == Roles.Count;
        var predicatesAreKnown = PostgreSqlTutorialPolicyExpressions.IsKnown(Using)
            && PostgreSqlTutorialPolicyExpressions.IsKnown(WithCheck);
        var commandShapeMatches = Command switch
        {
            "SELECT" or "DELETE" => Using is not null && WithCheck is null,
            "INSERT" => Using is null && WithCheck is not null,
            "UPDATE" => Using is not null && WithCheck is not null,
            _ => false
        };

        if (string.IsNullOrWhiteSpace(Name)
            || !commandIsKnown
            || !rolesAreValid
            || IsPermissive is null
            || !predicatesAreKnown
            || !commandShapeMatches)
        {
            throw new InvalidOperationException("PostgreSqlRuntime policy semantic contract is invalid.");
        }
    }
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
    bool HangfireSchemaUsageGranted,
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

public sealed record PostgreSqlPolicyInspection(
    string Name,
    string Command,
    IReadOnlyList<string> Roles,
    bool IsPermissive,
    string? Using,
    string? WithCheck);

public sealed record PostgreSqlHelperFunctionInspection(bool IsSecurityDefiner, bool HasSafeSearchPath, bool HasRequiredExecuteGrant);

internal static class PostgreSqlTutorialPolicyExpressions
{
    public const string ActorOwnsRow = "ActorOwnsRow";
    public const string ActorOwnsParent = "ActorOwnsParent";
    private const string Unrecognized = "Unrecognized";

    private const string ActorOwnsRowExpression = """
        ("UserId" =
        CASE
            WHEN (current_setting('lgym.account_id'::text, true) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text) THEN (current_setting('lgym.account_id'::text, true))::uuid
            ELSE NULL::uuid
        END)
        """;

    private const string ActorOwnsParentExpression = """
        (EXISTS ( SELECT 1
           FROM "UserTutorialProgresses" progress
          WHERE ((progress."Id" = "UserTutorialStepProgresses"."UserTutorialProgressId") AND (progress."UserId" =
                CASE
                    WHEN (current_setting('lgym.account_id'::text, true) ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'::text) THEN (current_setting('lgym.account_id'::text, true))::uuid
                    ELSE NULL::uuid
                END))))
        """;

    public static bool IsKnown(string? predicate)
        => predicate is null or ActorOwnsRow or ActorOwnsParent;

    public static string? Classify(string? expression)
    {
        if (expression is null)
        {
            return null;
        }

        var normalized = RemoveInsignificantWhitespace(expression);
        if (normalized == RemoveInsignificantWhitespace(ActorOwnsRowExpression))
        {
            return ActorOwnsRow;
        }

        return normalized == RemoveInsignificantWhitespace(ActorOwnsParentExpression)
            ? ActorOwnsParent
            : Unrecognized;
    }

    private static string RemoveInsignificantWhitespace(string expression)
        => string.Concat(expression.Where(character => !char.IsWhiteSpace(character)));
}
