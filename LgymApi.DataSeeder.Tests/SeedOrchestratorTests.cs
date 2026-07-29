using LgymApi.Application.Services;
using LgymApi.DataSeeder.Seeders;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Tests;

[TestFixture]
public sealed class SeedOrchestratorTests
{
    [Test]
    public async Task RunAsync_Should_Skip_Demo_Seeders_When_Disabled()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Id<User>.New().ToString())
            .Options;

        await using var context = new AppDbContext(options);
        var seedContext = new SeedContext();

        var seeders = new IEntitySeeder[]
        {
            new UserSeeder(new FakeLegacyPasswordService()),
            new EloRegistrySeeder(),
            new ExerciseSeeder()
        };

        var orchestrator = new SeedOrchestrator(seeders);

        var seedOptions = new SeedOptions
        {
            DropDatabase = false,
            UseMigrations = false,
            SeedDemoData = false
        };

        await orchestrator.RunAsync(context, seedContext, seedOptions, CancellationToken.None);

        (await context.Exercises.CountAsync()).Should().Be(0);
        seedContext.Exercises.Should().BeEmpty();
    }

    [Test]
    public async Task RunAsync_Should_Set_Admin_And_Tester_When_Present_In_Database()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Id<User>.New().ToString())
            .Options;

        await using var context = new AppDbContext(options);

        var admin = new User { Id = Id<User>.New(), Name = "Admin" };
        var tester = new User { Id = Id<User>.New(), Name = "Tester" };
        context.Users.AddRange(admin, tester);
        await context.SaveChangesAsync();

        var seedContext = new SeedContext();
        var seeders = new IEntitySeeder[]
        {
            new UserSeeder(new FakeLegacyPasswordService()),
            new EloRegistrySeeder()
        };

        var orchestrator = new SeedOrchestrator(seeders);
        var seedOptions = new SeedOptions
        {
            DropDatabase = false,
            UseMigrations = false,
            SeedDemoData = false
        };

        await orchestrator.RunAsync(context, seedContext, seedOptions, CancellationToken.None);

        seedContext.AdminUser?.Name.Should().Be("Admin");
        seedContext.TesterUser?.Name.Should().Be("Tester");
    }

    [Test]
    public async Task RunAsync_Should_Run_Seeders_In_Ascending_Order()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Id<User>.New().ToString())
            .Options;
        await using var context = new AppDbContext(options);
        var executionOrder = new List<int>();
        var orchestrator = new SeedOrchestrator(new IEntitySeeder[]
        {
            new RecordingSeeder(30, executionOrder),
            new RecordingSeeder(10, executionOrder),
            new RecordingSeeder(20, executionOrder)
        });

        await orchestrator.RunAsync(context, new SeedContext(), new SeedOptions { SeedDemoData = true }, CancellationToken.None);

        executionOrder.Should().Equal(10, 20, 30);
    }

    [Test]
    public async Task RunAsync_Should_Use_Migrations_For_Relational_Providers()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=lgym-seeder-test;Username=postgres;Password=secret;Timeout=1;Command Timeout=1")
            .Options;
        await using var context = new AppDbContext(options);
        var originalOut = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);

        try
        {
            var action = async () => await new SeedOrchestrator(Array.Empty<IEntitySeeder>())
                .RunAsync(context, new SeedContext(), new SeedOptions(), CancellationToken.None);

            await action.Should().ThrowAsync<Exception>();
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        output.ToString().Should().Contain("Applying migrations");
        output.ToString().Should().NotContain("Ensuring non-relational test database is created...");
    }

    [Test]
    public async Task RunAsync_Should_Remain_Idempotent_With_Stable_Role_And_Claim_Ids()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Id<User>.New().ToString())
            .Options;
        await using var context = new AppDbContext(options);
        var seeders = new IEntitySeeder[]
        {
            new UserSeeder(new FakeLegacyPasswordService()),
            new EloRegistrySeeder(),
            new RoleSeeder(),
            new RoleClaimSeeder()
        };
        var orchestrator = new SeedOrchestrator(seeders);
        var seedOptions = new SeedOptions { SeedDemoData = true };

        await orchestrator.RunAsync(context, new SeedContext(), seedOptions, CancellationToken.None);
        var firstUserCount = await context.Users.CountAsync();
        var firstRoleCount = await context.Roles.CountAsync();
        var firstRoleClaimCount = await context.RoleClaims.CountAsync();

        await orchestrator.RunAsync(context, new SeedContext(), seedOptions, CancellationToken.None);

        (await context.Users.CountAsync()).Should().Be(firstUserCount);
        (await context.Roles.CountAsync()).Should().Be(firstRoleCount);
        (await context.RoleClaims.CountAsync()).Should().Be(firstRoleClaimCount);
        (await context.Roles.SingleAsync(role => role.Name == "Admin")).Id.Should().Be(ParseSeedId<Role>(IdentitySeedIds.AdminRole));
        (await context.RoleClaims.SingleAsync(claim => claim.ClaimValue == AuthConstants.Permissions.AdminAccess)).Id.Should().Be(ParseSeedId<RoleClaim>(IdentitySeedIds.AdminAccessClaim));
    }

    private sealed class FakeLegacyPasswordService : ILegacyPasswordService
    {
        public bool Verify(string password, string hash, string salt, int? iterations, int? keyLength, string? digest)
        {
            return true;
        }

        public (string Hash, string Salt, int Iterations, int KeyLength, string Digest) Create(string password)
        {
            return ("hash", "salt", 1, 32, "sha");
        }
    }

    private sealed class RecordingSeeder : IEntitySeeder
    {
        private readonly List<int> _executionOrder;

        public RecordingSeeder(int order, List<int> executionOrder)
        {
            Order = order;
            _executionOrder = executionOrder;
        }

        public int Order { get; }

        public Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
        {
            _executionOrder.Add(Order);
            return Task.CompletedTask;
        }
    }

    private static Id<TEntity> ParseSeedId<TEntity>(string value)
    {
        return Id<TEntity>.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException($"Invalid Identity seed ID '{value}'.");
    }
}
