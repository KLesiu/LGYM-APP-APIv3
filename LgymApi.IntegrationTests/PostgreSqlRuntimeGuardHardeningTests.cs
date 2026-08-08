using FluentAssertions;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
[Category("PostgreSql")]
public sealed class PostgreSqlRuntimeGuardHardeningTests
{
    [Test]
    public async Task RuntimeValidation_WhenPolicyContractMatches_Succeeds()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync(activate: false);

        var action = () => ValidateRuntimeAsync(environment, includePolicies: true);

        await action.Should().NotThrowAsync();
    }

    [Test]
    public async Task RuntimeValidation_WhenHangfireSchemaUsageIsRevoked_FailsClosed()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync();
        await environment.ExecuteMaintenanceFormattedAsync(
            "REVOKE USAGE ON SCHEMA hangfire FROM %I",
            environment.RuntimeRole);

        var action = () => ValidateRuntimeAsync(environment, includePolicies: false);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task RuntimeValidation_WhenRequiredHangfireTableGrantIsRevoked_FailsClosed()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync();
        await environment.ExecuteMaintenanceFormattedAsync(
            "REVOKE SELECT ON TABLE hangfire.job FROM %I",
            environment.RuntimeRole);

        var action = () => ValidateRuntimeAsync(environment, includePolicies: false);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task RuntimeValidation_WhenPolicyUsingPredicateIsPermissive_FailsClosed()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync(activate: false);
        await ReplaceParentSelectPolicyWithTruePredicateAsync(environment);

        var action = () => ValidateRuntimeAsync(environment, includePolicies: true);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task Activation_WhenPolicyUsingPredicateIsPermissive_RejectsWithoutEnablingRls()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync(activate: false);
        await ReplaceParentSelectPolicyWithTruePredicateAsync(environment);

        var action = () => PostgreSqlTutorialRowSecurityActivation.RunAsync(
            environment.MaintenanceConnectionString,
            environment.DatabaseName,
            environment.MaintenanceRole,
            environment.RuntimeRole);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Tutorial RLS activation command failed with exit code *");
        (await ReadRlsStatesAsync(environment.MaintenanceConnectionString)).Should()
            .OnlyContain(state => !state.Enabled && !state.Forced);
    }

    [Test]
    public async Task Activation_WhenDatabaseIsMarkedProductionAndCallerClaimsStaging_RejectsWithoutEnablingRls()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync(
            databaseEnvironment: "Production",
            activate: false);

        var action = () => PostgreSqlTutorialRowSecurityActivation.RunAsync(
            environment.MaintenanceConnectionString,
            environment.DatabaseName,
            environment.MaintenanceRole,
            environment.RuntimeRole,
            targetEnvironment: "Staging");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Tutorial RLS activation command failed with exit code *");
        (await ReadRlsStatesAsync(environment.MaintenanceConnectionString)).Should()
            .OnlyContain(state => !state.Enabled && !state.Forced);
    }

    private static async Task ValidateRuntimeAsync(
        PostgreSqlTutorialRowSecurityTestEnvironment environment,
        bool includePolicies)
    {
        var configuration = CreateRuntimeConfiguration(environment, includePolicies);
        await using var dbContext = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(environment.RuntimeConnectionString)
                .Options);
        await PostgreSqlRuntimeConnectionValidator.ValidateAsync(dbContext, configuration);
    }

    private static IConfiguration CreateRuntimeConfiguration(
        PostgreSqlTutorialRowSecurityTestEnvironment environment,
        bool includePolicies)
    {
        var builder = new ConfigurationBuilder();
        if (includePolicies)
        {
            builder
                .SetBasePath(FindRepositoryRoot())
                .AddJsonFile("appsettings.container.example.json", optional: false);
        }

        return builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PostgreSqlRuntime:ExpectedDatabase"] = environment.DatabaseName,
            ["PostgreSqlRuntime:ExpectedRole"] = environment.RuntimeRole,
            ["PostgreSqlRuntime:HangfireSchema"] = "hangfire"
        }).Build();
    }

    private static async Task ReplaceParentSelectPolicyWithTruePredicateAsync(
        PostgreSqlTutorialRowSecurityTestEnvironment environment)
    {
        await environment.ExecuteMaintenanceFormattedAsync(
            "DROP POLICY user_tutorial_progresses_actor_select ON public.\"UserTutorialProgresses\"; " +
            "CREATE POLICY user_tutorial_progresses_actor_select ON public.\"UserTutorialProgresses\" " +
            "FOR SELECT TO PUBLIC USING (true)",
            "unused");
    }

    private static async Task<IReadOnlyList<RlsState>> ReadRlsStatesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT relation.relrowsecurity, relation.relforcerowsecurity
            FROM pg_class relation
            JOIN pg_namespace namespace ON namespace.oid = relation.relnamespace
            WHERE namespace.nspname = 'public'
              AND relation.relname IN ('UserTutorialProgresses', 'UserTutorialStepProgresses')
            ORDER BY relation.relname;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var states = new List<RlsState>();
        while (await reader.ReadAsync())
        {
            states.Add(new RlsState(reader.GetBoolean(0), reader.GetBoolean(1)));
        }

        states.Should().HaveCount(2);
        return states;
    }

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

    private sealed record RlsState(bool Enabled, bool Forced);
}
