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
    private const string EnvironmentVariableName = "ConnectionStrings__Postgres";
    private string? _originalValue;

    [SetUp]
    public void SetUp()
    {
        _originalValue = Environment.GetEnvironmentVariable(EnvironmentVariableName);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(EnvironmentVariableName, _originalValue);
    }

    [Test]
    public void CreateDbContext_WhenEnvironmentVariableMissing_UsesDefaultConnectionString()
    {
        Environment.SetEnvironmentVariable(EnvironmentVariableName, null);
        var factory = new AppDbContextFactory();

        using var context = factory.CreateDbContext([]);

        context.Database.GetConnectionString().Should().Contain("Host=localhost;Port=5433;Database=LGYM-APP");
        context.Database.GetConnectionString().Should().Contain("Password=REPLACE_ME");
        context.Database.GetConnectionString().Should().NotContain("sasasa");
    }

    [Test]
    public void CreateDbContext_WhenEnvironmentVariableProvided_UsesOverride()
    {
        const string connectionString = "Host=prod;Port=5432;Database=LGYM;Username=test;Password=REPLACE_ME_IN_TEST";
        Environment.SetEnvironmentVariable(EnvironmentVariableName, connectionString);
        var factory = new AppDbContextFactory();

        using var context = factory.CreateDbContext([]);

        context.Database.GetConnectionString().Should().Be(connectionString);
    }

    [Test]
    public void CreateDbContext_BuildsCanonicalNpgsqlModelAndMigrationStream()
    {
        const string connectionString = "Host=127.0.0.1;Port=1;Database=design_time_guard;Username=guard;Password=guard";
        Environment.SetEnvironmentVariable(EnvironmentVariableName, connectionString);
        var factory = new AppDbContextFactory();

        using var context = factory.CreateDbContext([]);
        var migrationsAssembly = context.GetService<IMigrationsAssembly>();

        context.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
        context.Model.GetEntityTypes().Should().HaveCount(48);
        context.Database.HasPendingModelChanges().Should().BeFalse();
        migrationsAssembly.Assembly.Should().BeSameAs(typeof(AppDbContext).Assembly);
        migrationsAssembly.ModelSnapshot.Should().NotBeNull();
        migrationsAssembly.ModelSnapshot!.GetType().Name.Should().Be("AppDbContextModelSnapshot");
    }
}
