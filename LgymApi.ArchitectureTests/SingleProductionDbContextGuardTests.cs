using System.Diagnostics;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class SingleProductionDbContextGuardTests
{
    private const int PersistedEntityCount = 48;
    private const string MigrationRoot = PersistenceIdentityContract.MigrationRoot;

    [Test]
    public void Current_Production_Topology_Should_Have_One_Context_Model_And_Migration_Stream()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var sources = PersistenceTopologyGuardTestHelpers.LoadProductionSources(repoRoot);
        var topology = PersistenceTopologyGuardTestHelpers.Analyze(sources);
        var expectedEntities = PersistenceIdentityContract.DbSets
            .Select(dbSet => dbSet.EntityType)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        var configurationViolations = FindMultiplicityViolations(topology.Configurations.Select(item => item.EntityType), expectedEntities);
        var registrarViolations = FindMultiplicityViolations(topology.RegistrarEntries.Select(item => item.EntityType), expectedEntities);

        Assert.Multiple(() =>
        {
            Assert.That(sources.Select(source => source.Path), Does.Contain(PersistenceIdentityContract.DbContextSourcePath));
            Assert.That(sources.Select(source => source.Path), Does.Contain(PersistenceIdentityContract.SnapshotSourcePath));
            Assert.That(sources.Select(source => source.Path), Does.Contain(PersistenceIdentityContract.RegistrarSourcePath));
            Assert.That(PersistenceIdentityContract.DbSets, Has.Count.EqualTo(PersistedEntityCount));
            Assert.That(PersistedEntityOwnershipCatalog.Entries, Has.Count.EqualTo(PersistedEntityCount));
            Assert.That(
                PersistedEntityOwnershipCatalog.Entries.Select(entry => entry.EntityType.FullName),
                Is.EquivalentTo(expectedEntities));
            Assert.That(
                () => PersistenceTopologyGuardTestHelpers.EnsureSingleDbContext(
                    topology,
                    PersistenceIdentityContract.DbContextTypeName,
                    PersistenceIdentityContract.DbContextSourcePath),
                Throws.Nothing);
            Assert.That(
                () => PersistenceTopologyGuardTestHelpers.EnsureSingleDesignTimeFactory(
                    topology,
                    PersistenceIdentityContract.DesignTimeFactoryTypeName,
                    PersistenceIdentityContract.DesignTimeFactorySourcePath,
                    PersistenceIdentityContract.DbContextTypeName),
                Throws.Nothing);
            Assert.That(
                () => PersistenceTopologyGuardTestHelpers.EnsureExactDbSetIdentities(topology, PersistenceIdentityContract.DbSets),
                Throws.Nothing);
            Assert.That(
                topology.DbSets.Select(item => item.SourcePath).Distinct(StringComparer.Ordinal),
                Is.EqualTo(new[] { PersistenceIdentityContract.DbContextSourcePath }));
            Assert.That(topology.DbSets.Select(item => item.EntityType).Distinct(), Is.EquivalentTo(expectedEntities));
            Assert.That(configurationViolations, Is.Empty, string.Join(Environment.NewLine, configurationViolations));
            Assert.That(registrarViolations, Is.Empty, string.Join(Environment.NewLine, registrarViolations));
            Assert.That(
                () => PersistenceTopologyGuardTestHelpers.EnsureRegistrarOrder(
                    topology,
                    PersistenceIdentityContract.RegistrarConfigurationTypes,
                    PersistenceIdentityContract.RegistrarSourcePath),
                Throws.Nothing);
            Assert.That(
                () => PersistenceTopologyGuardTestHelpers.EnsureSingleMigrationRoot(
                    topology,
                    PersistenceIdentityContract.MigrationRoot),
                Throws.Nothing);
            Assert.That(
                () => PersistenceTopologyGuardTestHelpers.EnsureSingleSnapshot(
                    topology,
                    PersistenceIdentityContract.SnapshotTypeName,
                    PersistenceIdentityContract.SnapshotSourcePath,
                    PersistenceIdentityContract.DbContextTypeName),
                Throws.Nothing);
            Assert.That(topology.MigrationStreams.Single().ContextTypeNames, Is.EqualTo(new[] { PersistenceIdentityContract.DbContextTypeName }));
            Assert.That(topology.EnsureCreatedViolations, Is.Empty, Describe(topology.EnsureCreatedViolations));
            Assert.That(topology.SchemaSplitViolations, Is.Empty, Describe(topology.SchemaSplitViolations));
        });
    }

    [Test]
    public void Production_Migration_Worktree_Should_Remain_Unchanged()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var headChanges = RunGit(repoRoot, ["diff", "--name-only", "HEAD", "--", MigrationRoot]);
        var untrackedFiles = RunGit(repoRoot, ["ls-files", "--others", "--exclude-standard", "--", MigrationRoot]);

        AssertProductionMigrationWorktreeIsClean(headChanges, untrackedFiles);
    }

    [Test]
    public void Physical_Project_And_Persistence_Topology_Worktree_Should_Remain_Unchanged()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var headChanges = RunGit(repoRoot,
        [
            "diff",
            "--name-only",
            "HEAD",
            "--",
            "LgymApi.sln",
            ":(glob)**/*.csproj",
            MigrationRoot
        ]);
        var untrackedFiles = RunGit(repoRoot,
        [
            "ls-files",
            "--others",
            "--exclude-standard",
            "--",
            ":(glob)**/*.csproj",
            MigrationRoot
        ]);

        AssertPhysicalTopologyWorktreeIsClean(headChanges, untrackedFiles);
    }

    [Test]
    public void Production_Migration_Worktree_Fixture_Should_Reject_A_Staged_Migration_Source()
    {
        const string migrationPath = MigrationRoot + "/20990101000000_SyntheticMigration.cs";

        Assert.That(
            () => AssertProductionMigrationWorktreeIsClean([migrationPath], []),
            Throws.InstanceOf<AssertionException>().With.Message.Contains(migrationPath));
    }

    [Test]
    public void Production_Migration_Worktree_Fixture_Should_Reject_An_Untracked_Model_Snapshot()
    {
        const string snapshotPath = MigrationRoot + "/AppDbContextModelSnapshot.cs";

        Assert.That(
            () => AssertProductionMigrationWorktreeIsClean([], [snapshotPath]),
            Throws.InstanceOf<AssertionException>().With.Message.Contains(snapshotPath));
    }

    [Test]
    public void Npgsql_Runtime_Model_Should_Match_The_Compiled_Snapshot_Without_A_Database_Connection()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=topology_guard;Username=guard;Password=guard")
            .Options;

        using var context = new AppDbContext(options);

        Assert.That(context.Database.ProviderName, Is.EqualTo("Npgsql.EntityFrameworkCore.PostgreSQL"));
        Assert.That(
            () => PersistenceTopologyGuardTestHelpers.EnsureSingleDatabaseSchemaModel(
                context,
                PersistenceIdentityContract.DbSets),
            Throws.Nothing);
        PersistenceTopologyGuardTestHelpers.EnsureNoPendingModelChanges(context.Database.HasPendingModelChanges());
    }

    [Test]
    public void Semantic_Fixture_Should_Reject_A_Renamed_Persisted_Entity()
    {
        var topology = AnalyzeFixture(
            "LgymApi.Infrastructure/Data/AppDbContext.cs",
            "using Microsoft.EntityFrameworkCore; sealed class RenamedUser { } sealed class AppDbContext : DbContext { public DbSet<RenamedUser> Users => Set<RenamedUser>(); }");

        Assert.That(
            () => PersistenceTopologyGuardTestHelpers.EnsureExactDbSetIdentities(
                topology,
                [new PersistedDbSetIdentity("Users", "User")]),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Semantic_Fixture_Should_Reject_A_Renamed_DbSet()
    {
        var topology = AnalyzeFixture(
            "LgymApi.Infrastructure/Data/AppDbContext.cs",
            "using Microsoft.EntityFrameworkCore; sealed class User { } sealed class AppDbContext : DbContext { public DbSet<User> Accounts => Set<User>(); }");

        Assert.That(
            () => PersistenceTopologyGuardTestHelpers.EnsureExactDbSetIdentities(
                topology,
                [new PersistedDbSetIdentity("Users", "User")]),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Semantic_Fixture_Should_Reject_A_Second_Production_DbContext()
    {
        const string sourcePath = "LgymApi.Reporting/Data/ReportingDbContext.cs";
        var topology = AnalyzeFixture(
            sourcePath,
            """
            using Microsoft.EntityFrameworkCore;
            sealed class AppDbContext : DbContext { }
            sealed class ReportingDbContext : DbContext { }
            """);

        Assert.That(
            () => PersistenceTopologyGuardTestHelpers.EnsureSingleDbContext(topology, "AppDbContext", sourcePath),
            Throws.InvalidOperationException.With.Message.Contains("Expected one production DbContext"));
    }

    [Test]
    public void Semantic_Fixture_Should_Reject_A_Second_Production_DesignTimeFactory()
    {
        const string sourcePath = "LgymApi.Infrastructure/Data/AppDbContextFactory.cs";
        var topology = AnalyzeFixture(
            sourcePath,
            """
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Design;
            sealed class AppDbContext : DbContext { }
            sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext> { public AppDbContext CreateDbContext(string[] args) => null!; }
            sealed class ReportingDbContextFactory : IDesignTimeDbContextFactory<AppDbContext> { public AppDbContext CreateDbContext(string[] args) => null!; }
            """);

        Assert.That(
            () => PersistenceTopologyGuardTestHelpers.EnsureSingleDesignTimeFactory(
                topology,
                "AppDbContextFactory",
                sourcePath,
                "AppDbContext"),
            Throws.InvalidOperationException.With.Message.Contains("Expected one production design-time DbContext factory"));
    }

    [Test]
    public void Semantic_Fixtures_Should_Detect_Duplicate_And_Missing_Configurations()
    {
        var duplicate = AnalyzeFixture("LgymApi.Infrastructure/Data/Configurations/Duplicate.cs", ConfigurationFixture("UserConfiguration", "SecondUserConfiguration"));
        var missing = AnalyzeFixture("LgymApi.Infrastructure/Data/Configurations/Missing.cs", ConfigurationFixture());

        Assert.Multiple(() =>
        {
            Assert.That(duplicate.Configurations.Count(item => item.EntityType == "User"), Is.EqualTo(2));
            Assert.That(missing.Configurations.Where(item => item.EntityType == "User"), Is.Empty);
        });
    }

    [Test]
    public void Semantic_Fixtures_Should_Detect_Duplicate_And_Missing_Registrar_Entries()
    {
        var duplicate = AnalyzeFixture("LgymApi.Infrastructure/Data/Configurations/AppDbContextEntityTypeConfigurationRegistrar.cs", RegistrarFixture("Register(new UserConfiguration()); Register(new UserConfiguration());"));
        var missing = AnalyzeFixture("LgymApi.Infrastructure/Data/Configurations/AppDbContextEntityTypeConfigurationRegistrar.cs", RegistrarFixture(string.Empty));

        Assert.Multiple(() =>
        {
            Assert.That(duplicate.RegistrarEntries.Count(item => item.EntityType == "User"), Is.EqualTo(2));
            Assert.That(missing.RegistrarEntries.Where(item => item.EntityType == "User"), Is.Empty);
        });
    }

    [Test]
    public void Project_Path_Fixture_Should_Reject_A_Second_Migration_Root()
    {
        var topology = PersistenceTopologyGuardTestHelpers.Analyze(
        [
            new TopologySource("LgymApi.Infrastructure/Migrations/Initial.cs", MigrationFixture("Initial")),
            new TopologySource("LgymApi.Reporting/Migrations/Initial.cs", MigrationFixture("ReportingInitial"))
        ]);

        Assert.That(
            () => PersistenceTopologyGuardTestHelpers.EnsureSingleMigrationRoot(topology, PersistenceIdentityContract.MigrationRoot),
            Throws.InvalidOperationException.With.Message.Contains("Expected one migration root"));
    }

    [Test]
    public void Semantic_Fixture_Should_Reject_A_Second_Model_Snapshot()
    {
        var topology = PersistenceTopologyGuardTestHelpers.Analyze(
        [
            new TopologySource(
                "LgymApi.Infrastructure/Migrations/AppDbContextModelSnapshot.cs",
                SnapshotFixture("AppDbContextModelSnapshot")),
            new TopologySource(
                "LgymApi.Infrastructure/Migrations/ReportingDbContextModelSnapshot.cs",
                SnapshotFixture("ReportingDbContextModelSnapshot"))
        ]);

        Assert.That(
            () => PersistenceTopologyGuardTestHelpers.EnsureSingleSnapshot(
                topology,
                "AppDbContextModelSnapshot",
                "LgymApi.Infrastructure/Migrations/AppDbContextModelSnapshot.cs",
                "AppDbContext"),
            Throws.InvalidOperationException.With.Message.Contains("Expected one snapshot"));
    }

    [Test]
    public void Semantic_Fixture_Should_Detect_A_Production_Schema_Split()
    {
        var topology = AnalyzeFixture(
            "LgymApi.Infrastructure/Data/SchemaConfiguration.cs",
            "using Microsoft.EntityFrameworkCore; sealed class SchemaConfiguration { void Apply(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema(\"workouts\"); }");

        Assert.That(topology.SchemaSplitViolations, Has.Count.EqualTo(1));
    }

    [Test]
    public void Semantic_Fixtures_Should_Reject_Production_EnsureCreated_And_Preserve_NonRelational_Test_Setup()
    {
        var production = AnalyzeFixture(
            "LgymApi.Infrastructure/Data/Bootstrap.cs",
            "using Microsoft.EntityFrameworkCore; sealed class Bootstrap : DbContext { void Run() => Database.EnsureCreated(); }");
        var nonRelational = AnalyzeFixture(
            "LgymApi.DataSeeder/SeedOrchestrator.cs",
            "using Microsoft.EntityFrameworkCore; sealed class SeedContext : DbContext { void Run() { if (!Database.IsRelational()) { Database.EnsureCreated(); } } }");

        Assert.Multiple(() =>
        {
            Assert.That(production.EnsureCreatedViolations, Has.Count.EqualTo(1));
            Assert.That(nonRelational.EnsureCreatedViolations, Is.Empty);
        });
    }

    [Test]
    public void Snapshot_Drift_Fixture_Should_Be_Rejected()
    {
        Assert.That(
            () => PersistenceTopologyGuardTestHelpers.EnsureNoPendingModelChanges(true),
            Throws.InvalidOperationException.With.Message.Contains("AppDbContextModelSnapshot"));
    }

    [Test]
    public void Physical_Topology_Worktree_Fixture_Should_Reject_A_Project_Or_Migration_Change()
    {
        Assert.That(
            () => AssertPhysicalTopologyWorktreeIsClean(
                ["LgymApi.sln", "LgymApi.Infrastructure/Migrations/AppDbContextModelSnapshot.cs"],
                ["LgymApi.Reporting/LgymApi.Reporting.csproj"]),
            Throws.InstanceOf<AssertionException>().With.Message.Contains("LgymApi.sln"));
    }

    private static PersistenceTopologyAnalysis AnalyzeFixture(string path, string source)
    {
        return PersistenceTopologyGuardTestHelpers.Analyze([new TopologySource(path, source)]);
    }

    private static List<string> FindMultiplicityViolations(IEnumerable<string> actualEntities, IReadOnlyCollection<string> expectedEntities)
    {
        var counts = actualEntities.GroupBy(entity => entity, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return expectedEntities.Where(entity => !counts.TryGetValue(entity, out var count) || count != 1)
            .Concat(counts.Keys.Where(entity => !expectedEntities.Contains(entity, StringComparer.Ordinal)))
            .OrderBy(entity => entity, StringComparer.Ordinal)
            .Select(entity => $"{entity}: found {counts.GetValueOrDefault(entity, 0)} entries")
            .ToList();
    }

    private static string Describe<T>(IEnumerable<T> values) => string.Join(Environment.NewLine, values);

    private static void AssertProductionMigrationWorktreeIsClean(
        IEnumerable<string> headChanges,
        IEnumerable<string> untrackedFiles)
    {
        AssertPhysicalTopologyWorktreeIsClean(headChanges, untrackedFiles);
    }

    private static void AssertPhysicalTopologyWorktreeIsClean(
        IEnumerable<string> headChanges,
        IEnumerable<string> untrackedFiles)
    {
        var touchedFiles = headChanges
            .Concat(untrackedFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.That(touchedFiles, Is.Empty, string.Join(Environment.NewLine, touchedFiles));
    }

    private static IReadOnlyList<string> RunGit(string repoRoot, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"git {string.Join(' ', arguments)} did not finish within 10 seconds.");
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.That(process.ExitCode, Is.Zero, error);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ConfigurationFixture(params string[] configurationTypes)
    {
        var configurations = string.Join(Environment.NewLine, configurationTypes.Select(name =>
            $"sealed class {name} : IEntityTypeConfiguration<User> {{ public void Configure(EntityTypeBuilder<User> builder) {{ }} }}"));
        return $"using Microsoft.EntityFrameworkCore; using Microsoft.EntityFrameworkCore.Metadata.Builders; class User {{ }} sealed class AppDbContext : DbContext {{ public DbSet<User> Users => Set<User>(); }} {configurations}";
    }

    private static string RegistrarFixture(string registrations)
    {
        return $"using Microsoft.EntityFrameworkCore; using Microsoft.EntityFrameworkCore.Metadata.Builders; class User {{ }} sealed class UserConfiguration : IEntityTypeConfiguration<User> {{ public void Configure(EntityTypeBuilder<User> builder) {{ }} }} static class AppDbContextEntityTypeConfigurationRegistrar {{ static void Register<T>(IEntityTypeConfiguration<T> configuration) {{ }} static void Apply() {{ {registrations} }} }}";
    }

    private static string MigrationFixture(string typeName)
    {
        return $"using Microsoft.EntityFrameworkCore; using Microsoft.EntityFrameworkCore.Infrastructure; using Microsoft.EntityFrameworkCore.Migrations; sealed class AppDbContext : DbContext {{ }} [DbContext(typeof(AppDbContext))] sealed class {typeName} : Migration {{ protected override void Up(MigrationBuilder builder) {{ }} protected override void Down(MigrationBuilder builder) {{ }} }}";
    }

    private static string SnapshotFixture(string typeName)
    {
        return $"using Microsoft.EntityFrameworkCore; using Microsoft.EntityFrameworkCore.Infrastructure; sealed class AppDbContext : DbContext {{ }} [DbContext(typeof(AppDbContext))] sealed class {typeName} : ModelSnapshot {{ protected override void BuildModel(ModelBuilder modelBuilder) {{ }} }}";
    }
}
