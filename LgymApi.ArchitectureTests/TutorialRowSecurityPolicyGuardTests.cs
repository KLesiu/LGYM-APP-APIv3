using System.Text.Json;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class TutorialRowSecurityPolicyGuardTests
{
    private const string MigrationPath = "LgymApi.Infrastructure/Migrations/20260807160000_AddTutorialRowSecurityPolicies.cs";
    private const string ActivationScriptPath = "deploy/postgres/activate-tutorial-row-security.sql";
    private const string DeactivationScriptPath = "deploy/postgres/deactivate-tutorial-row-security.sql";
    private const string ProvisionScriptPath = "deploy/postgres/provision-rls-pilot-roles.sql";
    private const string RuntimeConfigurationPath = "appsettings.container.example.json";
    private const string ReadmePath = "README.md";

    private static readonly PolicyContract[] Policies =
    [
        new("UserTutorialProgresses", "user_tutorial_progresses_actor_select", "SELECT"),
        new("UserTutorialProgresses", "user_tutorial_progresses_actor_insert", "INSERT"),
        new("UserTutorialProgresses", "user_tutorial_progresses_actor_update", "UPDATE"),
        new("UserTutorialProgresses", "user_tutorial_progresses_actor_delete", "DELETE"),
        new("UserTutorialStepProgresses", "user_tutorial_step_progresses_actor_select", "SELECT"),
        new("UserTutorialStepProgresses", "user_tutorial_step_progresses_actor_insert", "INSERT"),
        new("UserTutorialStepProgresses", "user_tutorial_step_progresses_actor_update", "UPDATE"),
        new("UserTutorialStepProgresses", "user_tutorial_step_progresses_actor_delete", "DELETE")
    ];

    [Test]
    public void DormantMigration_DefinesCompleteNullSafeParentAndChildPolicyContract()
    {
        var source = Read(MigrationPath);
        var up = source[..source.IndexOf("protected override void Down", StringComparison.Ordinal)];
        var down = source[source.IndexOf("protected override void Down", StringComparison.Ordinal)..];

        Assert.That(up, Does.Not.Contain("ENABLE ROW LEVEL SECURITY"));
        Assert.That(up, Does.Contain("DISABLE ROW LEVEL SECURITY"));
        Assert.That(up, Does.Not.Contain("CREATE FUNCTION"));
        Assert.That(Count(up, "CREATE POLICY"), Is.EqualTo(Policies.Length));

        foreach (var policy in Policies)
        {
            var definition = ExtractPolicy(up, policy.Name);
            Assert.That(definition, Does.Contain($"ON public.\"{policy.Table}\""));
            Assert.That(definition, Does.Contain($"FOR {policy.Command} TO PUBLIC"));
            AssertNullSafeActorExpression(definition);
            AssertCommandCoverage(definition, policy.Command);
        }

        foreach (var policy in Policies.Where(policy => policy.Table == "UserTutorialProgresses"))
        {
            Assert.That(ExtractPolicy(up, policy.Name), Does.Contain("\"UserId\" = CASE"));
        }

        foreach (var policy in Policies.Where(policy => policy.Table == "UserTutorialStepProgresses"))
        {
            var definition = ExtractPolicy(up, policy.Name);
            Assert.That(definition, Does.Contain("EXISTS ("));
            Assert.That(definition, Does.Contain("FROM public.\"UserTutorialProgresses\" AS progress"));
            Assert.That(definition, Does.Contain("progress.\"Id\" = \"UserTutorialProgressId\""));
            Assert.That(definition, Does.Contain("progress.\"UserId\" = CASE"));
        }

        var firstDropPolicy = down.IndexOf("DROP POLICY", StringComparison.Ordinal);
        Assert.That(firstDropPolicy, Is.GreaterThan(0));
        Assert.That(down.IndexOf("NO FORCE ROW LEVEL SECURITY", StringComparison.Ordinal), Is.LessThan(firstDropPolicy));
        Assert.That(down.IndexOf("DISABLE ROW LEVEL SECURITY", StringComparison.Ordinal), Is.LessThan(firstDropPolicy));
        Assert.That(Count(down, "DROP POLICY IF EXISTS"), Is.EqualTo(Policies.Length));
        Assert.That(down, Does.Not.Contain("DROP TABLE"));
    }

    [Test]
    public void OperatorScripts_UseTheSharedLockValidateTargetsAndKeepProductionActivationBlocked()
    {
        var activation = Read(ActivationScriptPath);
        var deactivation = Read(DeactivationScriptPath);

        AssertScriptSafety(activation);
        AssertScriptSafety(deactivation);
        Assert.That(activation, Does.Contain("lower(:'target_environment') = 'staging'"));
        Assert.That(activation, Does.Contain("Task 18 production go/no-go"));
        Assert.That(activation, Does.Not.Contain("CREATE POLICY"));
        Assert.That(activation, Does.Contain("ENABLE ROW LEVEL SECURITY"));
        Assert.That(activation, Does.Contain("FORCE ROW LEVEL SECURITY"));
        Assert.That(activation, Does.Contain("policy_contract_matches"));
        Assert.That(activation, Does.Contain("policy.polcmd::text"));
        Assert.That(activation, Does.Contain("policy.polroles"));
        Assert.That(activation, Does.Contain("policy.polpermissive"));
        Assert.That(activation, Does.Contain("pg_get_expr(policy.polqual"));
        Assert.That(activation, Does.Contain("pg_get_expr(policy.polwithcheck"));
        AssertDatabaseEnvironmentIdentity(activation);
        AssertDatabaseEnvironmentIdentity(deactivation);
        Assert.That(deactivation, Does.Contain("IN ('development', 'staging', 'production')"));
        Assert.That(deactivation, Does.Not.Contain("DROP POLICY"));
        Assert.That(deactivation.IndexOf("NO FORCE ROW LEVEL SECURITY", StringComparison.Ordinal),
            Is.LessThan(deactivation.IndexOf("DISABLE ROW LEVEL SECURITY", StringComparison.Ordinal)));
    }

    [Test]
    public void Provisioning_StoresDatabaseEnvironmentAndReadmeUsesFailClosedPsqlFlags()
    {
        var provisioning = Read(ProvisionScriptPath);
        var readme = Read(ReadmePath);

        Assert.That(provisioning, Does.Contain("database_environment is required"));
        Assert.That(provisioning, Does.Contain("ALTER DATABASE"));
        Assert.That(provisioning, Does.Contain("lgym.deployment_environment"));
        Assert.That(readme, Does.Contain(
            "psql -X -v ON_ERROR_STOP=1 -v database_name=LGYM-APP -v database_environment=Staging"));
    }

    [Test]
    public void RuntimeTemplate_RequiresTheDormantPolicyContractWithRlsDisabled()
    {
        using var document = JsonDocument.Parse(Read(RuntimeConfigurationPath));
        var protectedTables = document.RootElement
            .GetProperty("PostgreSqlRuntime")
            .GetProperty("ProtectedTables")
            .EnumerateArray()
            .ToArray();

        Assert.That(protectedTables, Has.Length.EqualTo(2));
        foreach (var table in protectedTables)
        {
            Assert.That(table.GetProperty("RowSecurityEnabled").GetBoolean(), Is.False);
            Assert.That(table.GetProperty("RowSecurityForced").GetBoolean(), Is.False);
            foreach (var policy in table.GetProperty("Policies").EnumerateArray())
            {
                Assert.That(
                    policy.GetProperty("Roles").EnumerateArray().Select(role => role.GetString()),
                    Is.EqualTo(new[] { "PUBLIC" }));
                Assert.That(policy.GetProperty("IsPermissive").GetBoolean(), Is.True);
                Assert.That(policy.TryGetProperty("Using", out _), Is.True);
                Assert.That(policy.TryGetProperty("WithCheck", out _), Is.True);
            }
        }

        var actualPolicies = protectedTables
            .SelectMany(table => table.GetProperty("Policies").EnumerateArray().Select(policy => new PolicyContract(
                table.GetProperty("Name").GetString()!,
                policy.GetProperty("Name").GetString()!,
                policy.GetProperty("Command").GetString()!)));

        Assert.That(actualPolicies, Is.EquivalentTo(Policies));
    }

    private static void AssertDatabaseEnvironmentIdentity(string source)
    {
        Assert.That(source, Does.Contain("pg_db_role_setting"));
        Assert.That(source, Does.Contain("lgym.deployment_environment"));
        Assert.That(source, Does.Contain("setrole = 0"));
        Assert.That(source, Does.Contain("database_environment_matches"));
    }

    private static void AssertNullSafeActorExpression(string definition)
    {
        Assert.That(definition, Does.Contain("current_setting('lgym.account_id', true) ~*"));
        Assert.That(definition, Does.Contain("THEN current_setting('lgym.account_id', true)::uuid"));
        Assert.That(definition, Does.Contain("ELSE NULL"));
    }

    private static void AssertCommandCoverage(string definition, string command)
    {
        if (command is "SELECT" or "DELETE")
        {
            Assert.That(definition, Does.Contain("USING ("));
            Assert.That(definition, Does.Not.Contain("WITH CHECK"));
            return;
        }

        if (command == "INSERT")
        {
            Assert.That(definition, Does.Not.Contain("USING ("));
            Assert.That(definition, Does.Contain("WITH CHECK ("));
            return;
        }

        Assert.That(definition, Does.Contain("USING ("));
        Assert.That(definition, Does.Contain("WITH CHECK ("));
    }

    private static void AssertScriptSafety(string source)
    {
        Assert.That(source, Does.StartWith("\\set ON_ERROR_STOP on"));
        Assert.That(source, Does.Contain("current_database() = :'database_name'"));
        Assert.That(source, Does.Contain("current_user = :'maintenance_role'"));
        Assert.That(source, Does.Contain("pg_advisory_xact_lock(hashtextextended('lgym.tutorial-row-security.rollout', 0))"));
        Assert.That(source.IndexOf("BEGIN;", StringComparison.Ordinal),
            Is.LessThan(source.IndexOf("pg_advisory_xact_lock", StringComparison.Ordinal)));
        Assert.That(source.IndexOf("pg_advisory_xact_lock", StringComparison.Ordinal),
            Is.LessThan(source.IndexOf("COMMIT;", StringComparison.Ordinal)));
    }

    private static string ExtractPolicy(string source, string policyName)
    {
        var start = source.IndexOf($"CREATE POLICY \"{policyName}\"", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing policy {policyName}.");
        var end = source.IndexOf(';', start);
        Assert.That(end, Is.GreaterThan(start), $"Policy {policyName} is not terminated.");
        return source[start..end];
    }

    private static int Count(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(ArchitectureTestHelpers.ResolveRepositoryRoot(), relativePath));

    private sealed record PolicyContract(string Table, string Name, string Command);
}
