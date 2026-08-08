using FluentAssertions;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
[Category("PostgreSql")]
public sealed class PostgreSqlRuntimeCatalogInspectionTests
{
    [Test]
    public async Task InspectAsync_WhenDormantRuntimeCatalogMatches_ReportsExactPolicySemanticsAndPrivileges()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync(activate: false);
        var configuration = CreateConfiguration(environment);
        var options = configuration.GetSection("PostgreSqlRuntime").Get<PostgreSqlRuntimeValidationOptions>()!;
        await using var dbContext = CreateDbContext(environment.RuntimeConnectionString);
        await using var connection = new NpgsqlConnection(environment.RuntimeConnectionString);
        await connection.OpenAsync();

        var inspection = await PostgreSqlRuntimeConnectionInspector.InspectAsync(
            dbContext,
            connection,
            options,
            CancellationToken.None);

        inspection.DatabaseName.Should().Be(environment.DatabaseName);
        inspection.CurrentUser.Should().Be(environment.RuntimeRole);
        inspection.IsSuperuser.Should().BeFalse();
        inspection.BypassesRowSecurity.Should().BeFalse();
        inspection.ElevatedMemberships.Should().BeEmpty();
        inspection.MultiplexingEnabled.Should().BeFalse();
        inspection.HangfireSchemaExists.Should().BeTrue();
        inspection.HangfireSchemaUsageGranted.Should().BeTrue();
        inspection.MissingTableGrants.Should().BeEmpty();
        inspection.MissingSequenceGrants.Should().BeEmpty();
        inspection.ProtectedTables.Should().OnlyContain(table =>
            !table.RowSecurityEnabled && !table.RowSecurityForced && !table.IsOwnedByRuntimeRole);

        var policies = inspection.ProtectedTables.SelectMany(table => table.Policies).ToArray();
        policies.Should().HaveCount(8);
        policies.Should().OnlyContain(policy => policy.Roles.Count == 1 && policy.Roles[0] == "PUBLIC");
        policies.Should().OnlyContain(policy => policy.IsPermissive);
        AssertPolicy(policies, "user_tutorial_progresses_actor_select", "SELECT", "ActorOwnsRow", null);
        AssertPolicy(policies, "user_tutorial_progresses_actor_insert", "INSERT", null, "ActorOwnsRow");
        AssertPolicy(policies, "user_tutorial_progresses_actor_update", "UPDATE", "ActorOwnsRow", "ActorOwnsRow");
        AssertPolicy(policies, "user_tutorial_progresses_actor_delete", "DELETE", "ActorOwnsRow", null);
        AssertPolicy(policies, "user_tutorial_step_progresses_actor_select", "SELECT", "ActorOwnsParent", null);
        AssertPolicy(policies, "user_tutorial_step_progresses_actor_insert", "INSERT", null, "ActorOwnsParent");
        AssertPolicy(policies, "user_tutorial_step_progresses_actor_update", "UPDATE", "ActorOwnsParent", "ActorOwnsParent");
        AssertPolicy(policies, "user_tutorial_step_progresses_actor_delete", "DELETE", "ActorOwnsParent", null);

        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateInspection(inspection, options);
        action.Should().NotThrow();
    }

    [TestCase(RuntimePrivilege.SchemaUsage, "*USAGE on the Hangfire schema*")]
    [TestCase(RuntimePrivilege.TableSelect, "*DML or sequence privileges*")]
    [TestCase(RuntimePrivilege.SequenceUsage, "*DML or sequence privileges*")]
    public async Task RuntimeValidation_WhenRequiredRuntimePrivilegeIsRevoked_FailsClosed(
        RuntimePrivilege privilege,
        string expectedMessage)
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync(activate: false);
        var (format, target) = privilege switch
        {
            RuntimePrivilege.SchemaUsage => ("REVOKE USAGE ON SCHEMA hangfire FROM %I", environment.RuntimeRole),
            RuntimePrivilege.TableSelect => ("REVOKE SELECT ON TABLE hangfire.job FROM %I", environment.RuntimeRole),
            RuntimePrivilege.SequenceUsage => ("REVOKE USAGE ON SEQUENCE hangfire.job_id_seq FROM %I", environment.RuntimeRole),
            _ => throw new ArgumentOutOfRangeException(nameof(privilege), privilege, null)
        };
        await environment.ExecuteMaintenanceFormattedAsync(format, target);

        var action = () => ValidateAsync(environment, CreateConfiguration(environment));

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage(expectedMessage);
    }

    [Test]
    public async Task RuntimeValidation_WhenHangfireSchemaIsMissing_FailsClosed()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync(activate: false);
        await environment.ExecuteMaintenanceFormattedAsync("DROP SCHEMA hangfire CASCADE", "unused");

        var action = () => ValidateAsync(environment, CreateConfiguration(environment));

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("Hangfire schema is missing*");
    }

    [Test]
    public async Task RuntimeValidation_WhenConfiguredProtectedTableIsAbsent_FailsClosed()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync(activate: false);
        var configuration = CreateConfiguration(environment, new Dictionary<string, string?>
        {
            ["PostgreSqlRuntime:ProtectedTables:0:Name"] = "MissingTutorialProgresses"
        });

        var action = () => ValidateAsync(environment, configuration);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*policies do not match*");
    }

    [Test]
    public async Task RuntimeValidation_WhenProtectedTableRlsStateDiffers_FailsClosed()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync(activate: false);
        await environment.ExecuteMaintenanceFormattedAsync(
            "ALTER TABLE public.\"UserTutorialProgresses\" ENABLE ROW LEVEL SECURITY",
            "unused");

        var action = () => ValidateAsync(environment, CreateConfiguration(environment));

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("Protected-table RLS state does not match*");
    }

    [Test]
    public async Task InspectAsync_WhenConfiguredHelperIsSafe_ReportsItsSecurityContract()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync(activate: false);
        await environment.ExecuteMaintenanceFormattedAsync(
            "CREATE FUNCTION public.runtime_inspection_helper() RETURNS boolean LANGUAGE sql SECURITY INVOKER SET search_path = pg_catalog AS 'SELECT true'; " +
            "REVOKE ALL ON FUNCTION public.runtime_inspection_helper() FROM PUBLIC; " +
            "GRANT EXECUTE ON FUNCTION public.runtime_inspection_helper() TO %I",
            environment.RuntimeRole);
        var configuration = CreateConfiguration(environment, new Dictionary<string, string?>
        {
            ["PostgreSqlRuntime:HelperFunction:Schema"] = "public",
            ["PostgreSqlRuntime:HelperFunction:Name"] = "runtime_inspection_helper"
        });
        var options = configuration.GetSection("PostgreSqlRuntime").Get<PostgreSqlRuntimeValidationOptions>()!;
        await using var dbContext = CreateDbContext(environment.RuntimeConnectionString);
        await using var connection = new NpgsqlConnection(environment.RuntimeConnectionString);
        await connection.OpenAsync();

        var inspection = await PostgreSqlRuntimeConnectionInspector.InspectAsync(
            dbContext,
            connection,
            options,
            CancellationToken.None);

        inspection.HelperFunction.Should().Be(new PostgreSqlHelperFunctionInspection(false, true, true));
        PostgreSqlRuntimeConnectionValidator.ValidateInspection(inspection, options);
    }

    private static void AssertPolicy(
        IEnumerable<PostgreSqlPolicyInspection> policies,
        string name,
        string command,
        string? usingExpression,
        string? withCheckExpression)
        => policies.Should().ContainSingle(policy =>
            policy.Name == name
            && policy.Command == command
            && policy.Using == usingExpression
            && policy.WithCheck == withCheckExpression);

    internal static async Task ValidateAsync(PostgreSqlTutorialRowSecurityTestEnvironment environment, IConfiguration configuration)
    {
        await using var dbContext = CreateDbContext(environment.RuntimeConnectionString);
        await PostgreSqlRuntimeConnectionValidator.ValidateAsync(dbContext, configuration);
    }

    internal static IConfiguration CreateConfiguration(
        PostgreSqlTutorialRowSecurityTestEnvironment environment,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["PostgreSqlRuntime:ExpectedDatabase"] = environment.DatabaseName,
            ["PostgreSqlRuntime:ExpectedRole"] = environment.RuntimeRole,
            ["PostgreSqlRuntime:HangfireSchema"] = "hangfire"
        };
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }

        return new ConfigurationBuilder()
            .SetBasePath(FindRepositoryRoot())
            .AddJsonFile("appsettings.container.example.json", optional: false)
            .AddInMemoryCollection(values)
            .Build();
    }

    private static AppDbContext CreateDbContext(string connectionString)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LgymApi.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    public enum RuntimePrivilege
    {
        SchemaUsage,
        TableSelect,
        SequenceUsage
    }
}
