using FluentAssertions;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
[Category("PostgreSql")]
public sealed class PostgreSqlRuntimeRoleSafetyTests
{
    [TestCase(RuntimeRoleProperty.Superuser)]
    [TestCase(RuntimeRoleProperty.BypassRls)]
    public async Task RuntimeValidation_WhenRuntimeRoleIsElevated_FailsClosed(RuntimeRoleProperty property)
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync(activate: false);
        var attribute = property == RuntimeRoleProperty.Superuser ? "SUPERUSER" : "BYPASSRLS";
        var resetAttribute = property == RuntimeRoleProperty.Superuser ? "NOSUPERUSER" : "NOBYPASSRLS";
        await environment.ExecuteAdminFormattedAsync("ALTER ROLE %I " + attribute, environment.RuntimeRole);

        try
        {
            var action = () => PostgreSqlRuntimeCatalogInspectionTests.ValidateAsync(
                environment,
                PostgreSqlRuntimeCatalogInspectionTests.CreateConfiguration(environment));

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*prohibited superuser, BYPASSRLS, or elevated role membership*");
        }
        finally
        {
            await environment.ExecuteAdminFormattedAsync("ALTER ROLE %I " + resetAttribute, environment.RuntimeRole);
        }
    }

    [Test]
    public async Task RuntimeValidation_WhenRuntimeRoleHasElevatedMembership_FailsClosed()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync(activate: false);
        var elevatedRole = $"lgym_elevated_it_{Id<PostgreSqlRuntimeRoleSafetyTests>.New():N}";
        await environment.ExecuteAdminFormattedAsync("CREATE ROLE %I NOLOGIN NOSUPERUSER BYPASSRLS", elevatedRole);
        await environment.ExecuteAdminFormattedAsync("GRANT %I TO %I", elevatedRole, environment.RuntimeRole);

        try
        {
            var action = () => PostgreSqlRuntimeCatalogInspectionTests.ValidateAsync(
                environment,
                PostgreSqlRuntimeCatalogInspectionTests.CreateConfiguration(environment));

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*prohibited superuser, BYPASSRLS, or elevated role membership*");
        }
        finally
        {
            await environment.ExecuteAdminFormattedAsync("REVOKE %I FROM %I", elevatedRole, environment.RuntimeRole);
            await environment.ExecuteAdminFormattedAsync("DROP ROLE %I", elevatedRole);
        }
    }

    [Test]
    public async Task RuntimeValidation_WhenRuntimeRoleOwnsProtectedTable_FailsClosed()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync(activate: false);
        await environment.ExecuteAdminFormattedAsync(
            "ALTER TABLE public.\"UserTutorialProgresses\" OWNER TO %I",
            environment.RuntimeRole);

        try
        {
            var action = () => PostgreSqlRuntimeCatalogInspectionTests.ValidateAsync(
                environment,
                PostgreSqlRuntimeCatalogInspectionTests.CreateConfiguration(environment));

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Runtime PostgreSQL role must not own protected tables.");
        }
        finally
        {
            await environment.ExecuteAdminFormattedAsync(
                "ALTER TABLE public.\"UserTutorialProgresses\" OWNER TO %I",
                environment.MaintenanceRole);
        }
    }

    [Test]
    public async Task RuntimeValidation_WhenRuntimeConnectionEnablesMultiplexing_FailsClosed()
    {
        await using var environment = await PostgreSqlTutorialRowSecurityTestEnvironment.CreateAsync(activate: false);
        var multiplexedConnectionString = new NpgsqlConnectionStringBuilder(environment.RuntimeConnectionString)
        {
            Multiplexing = true
        }.ConnectionString;
        await using var dbContext = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(multiplexedConnectionString).Options);
        var configuration = PostgreSqlRuntimeCatalogInspectionTests.CreateConfiguration(environment);

        var action = () => PostgreSqlRuntimeConnectionValidator.ValidateAsync(dbContext, configuration);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Runtime PostgreSQL connection must disable multiplexing for the RLS pilot.");
    }

    public enum RuntimeRoleProperty
    {
        Superuser,
        BypassRls
    }
}
