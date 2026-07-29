using LgymApi.Application.Services;
using LgymApi.DataSeeder.Seeders;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace LgymApi.DataSeeder.Tests;

[TestFixture]
public sealed class DataSeederProgramTests
{
    [Test]
    public void MaskConnectionString_Should_Mask_Password_And_Keep_Other_Parts()
    {
        var masked = DataSeederProgram.MaskConnectionString("Host=localhost;Password=secret;Username=user");

        masked.Should().Contain("Host=localhost");
        masked.Should().Contain("Username=user");
        masked.Should().Contain("Password=***");
        masked.Should().NotContain("secret");
    }

    [Test]
    public void MaskConnectionString_Should_Return_Empty_For_Whitespace()
    {
        var masked = DataSeederProgram.MaskConnectionString(" ");

        masked.Should().Be("<empty>");
    }

    [Test]
    public void BuildConfiguration_Should_Load_Base_And_Optional_Appsettings()
    {
        var repoRoot = CreateTempRepo();
        try
        {
            var config = DataSeederProgram.BuildConfiguration(repoRoot);

            config.GetConnectionString("Postgres").Should().Be("Host=localhost");
            config["FeatureFlag"].Should().Be("true");
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    [Test]
    public void BuildServiceProvider_Should_Resolve_Identity_Password_Service_And_All_Seeders()
    {
        var configuration = new ConfigurationBuilder().Build();

        using var provider = BuildServiceProvider(configuration);
        using var scope = provider.CreateScope();

        var passwordService = scope.ServiceProvider.GetRequiredService<ILegacyPasswordService>();
        var password = passwordService.Create("seed-password");
        var seeders = scope.ServiceProvider.GetRequiredService<IEnumerable<IEntitySeeder>>().ToList();

        passwordService.Verify("seed-password", password.Hash, password.Salt, password.Iterations, password.KeyLength, password.Digest)
            .Should().BeTrue();
        seeders.Should().HaveCount(39);
        seeders.Should().ContainSingle(seeder => seeder is RecurringReportAssignmentSeeder);
    }

    [Test]
    public async Task Composed_Seeders_Should_Run_In_Deterministic_Order_And_Remain_Idempotent()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var provider = BuildServiceProvider(configuration);
        using var scope = provider.CreateScope();
        var seeders = scope.ServiceProvider.GetRequiredService<IEnumerable<IEntitySeeder>>().ToList();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Id<User>.New().ToString())
            .Options;
        await using var context = new AppDbContext(options);
        var orchestrator = new SeedOrchestrator(seeders);
        var seedOptions = new SeedOptions { SeedDemoData = true };

        await orchestrator.RunAsync(context, new SeedContext(), seedOptions, CancellationToken.None);
        var firstUserCount = await context.Users.CountAsync();
        var firstRoleCount = await context.Roles.CountAsync();
        var firstRoleClaimCount = await context.RoleClaims.CountAsync();

        await orchestrator.RunAsync(context, new SeedContext(), seedOptions, CancellationToken.None);

        seeders.Should().HaveCount(39);
        (await context.Users.CountAsync()).Should().Be(firstUserCount);
        (await context.Roles.CountAsync()).Should().Be(firstRoleCount);
        (await context.RoleClaims.CountAsync()).Should().Be(firstRoleClaimCount);
    }

    private static string CreateTempRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "lgym-seeder-tests", Id<DataSeederProgramTests>.New().ToString());
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "LgymApi.sln"), string.Empty);

        var apiRoot = Path.Combine(root, "LgymApi.Api");
        Directory.CreateDirectory(apiRoot);

        var baseSettings = "{" +
                           "\"ConnectionStrings\": { \"Postgres\": \"Host=localhost\" }" +
                           "}";
        var optionalSettings = "{" +
                               "\"FeatureFlag\": \"true\"" +
                               "}";

        File.WriteAllText(Path.Combine(apiRoot, "appsettings.json"), baseSettings);
        File.WriteAllText(Path.Combine(apiRoot, "appsettings.Development.json"), optionalSettings);

        return root;
    }

    private static ServiceProvider BuildServiceProvider(IConfiguration configuration)
    {
        var method = typeof(LgymApi.DataSeeder.Program).GetMethod(
            "BuildServiceProvider",
            BindingFlags.Static | BindingFlags.NonPublic);

        return (ServiceProvider)method!
            .Invoke(null, [configuration, "Host=localhost;Database=lgym-seeder-test"])!;
    }
}
