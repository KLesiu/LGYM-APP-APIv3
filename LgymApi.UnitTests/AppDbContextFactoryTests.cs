using FluentAssertions;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class AppDbContextFactoryTests
{
    private string? _originalMigrationConnection;
    private string? _originalRuntimeConnection;
    private string? _originalEnvironment;

    [SetUp]
    public void SetUp()
    {
        _originalMigrationConnection = Environment.GetEnvironmentVariable("LGYM_MIGRATION_POSTGRES");
        _originalRuntimeConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
        _originalEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("LGYM_MIGRATION_POSTGRES", _originalMigrationConnection);
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", _originalRuntimeConnection);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", _originalEnvironment);
    }

    [Test]
    public void CreateDbContext_WhenDevelopmentConnectionVariablesAreMissing_UsesLocalFallback()
    {
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("LGYM_MIGRATION_POSTGRES", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null);
        var factory = new AppDbContextFactory();

        using var context = factory.CreateDbContext([]);

        context.Database.GetDbConnection().Database.Should().Be("LGYM-APP");
        context.Database.GetDbConnection().DataSource.Should().Contain("localhost");
    }

    [Test]
    public void CreateDbContext_WhenMigrationEnvironmentVariableProvided_PrefersMaintenanceConnection()
    {
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("LGYM_MIGRATION_POSTGRES", "Host=maintenance;Port=5432;Database=design_time;Username=maintenance;Password=test-only");
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", "Host=runtime;Port=5432;Database=runtime;Username=runtime;Password=test-only");
        var factory = new AppDbContextFactory();

        using var context = factory.CreateDbContext([]);

        context.Database.GetDbConnection().Database.Should().Be("design_time");
        context.Database.GetDbConnection().DataSource.Should().Contain("maintenance");
    }

    [Test]
    public void CreateDbContext_OutsideDevelopmentWithoutMaintenanceConnection_Throws()
    {
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("LGYM_MIGRATION_POSTGRES", null);

        var action = () => new AppDbContextFactory().CreateDbContext([]);

        action.Should().Throw<InvalidOperationException>().WithMessage("*LGYM_MIGRATION_POSTGRES*");
    }

    [Test]
    public void CreateDbContext_BuildsCanonicalNpgsqlModelAndMigrationStream()
    {
        const string connectionString = "Host=127.0.0.1;Port=1;Database=design_time_guard;Username=guard;Password=guard";
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("LGYM_MIGRATION_POSTGRES", connectionString);
        var factory = new AppDbContextFactory();

        using var context = factory.CreateDbContext([]);
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();

        context.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
        context.Model.GetEntityTypes().Should().HaveCount(48);
        var userType = context.Model.FindEntityType(typeof(LgymApi.Domain.Entities.User));
        userType!.FindProperty(nameof(LgymApi.Domain.Entities.User.AdultConfirmedAt))!.IsNullable.Should().BeTrue();
        userType.FindProperty(nameof(LgymApi.Domain.Entities.User.AdultConfirmationVersion))!.IsNullable.Should().BeTrue();
        context.Database.HasPendingModelChanges().Should().BeFalse();
        migrationsAssembly.Assembly.Should().BeSameAs(typeof(AppDbContext).Assembly);
        migrationsAssembly.ModelSnapshot.Should().NotBeNull();
        migrationsAssembly.ModelSnapshot!.GetType().Name.Should().Be("AppDbContextModelSnapshot");
    }
}
