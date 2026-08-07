using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LgymApi.IntegrationTests;

[TestFixture]
[NonParallelizable]
[Category("PostgreSql")]
public sealed class PostgreSqlTutorialRowSecurityMigrationTests
{
    private const string PrePolicyMigrationId = "20260801222135_RepairRecurringReportAssignmentRequestIndex";
    private const string PolicyMigrationName = "AddTutorialRowSecurityPolicies";

    private static readonly TutorialPolicy[] ExpectedPolicies =
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
    public async Task ForwardMigration_CreatesDormantTutorialPoliciesAndPreservesData()
    {
        await using var lease = await PostgreSqlDatabaseLease.CreateAsync();
        await using (var baselineContext = CreateDbContext(lease))
        {
            await MigrateAsync(baselineContext, PrePolicyMigrationId);
            await SeedTutorialAsync(baselineContext);
        }

        await using (var migrationContext = CreateDbContext(lease))
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using var verificationContext = CreateDbContext(lease);
        var policyMigrationId = GetPolicyMigrationId(verificationContext.Database.GetMigrations());

        (await verificationContext.Database.GetAppliedMigrationsAsync()).Should().Contain(policyMigrationId);
        verificationContext.Database.HasPendingModelChanges().Should().BeFalse();
        (await ReadTableStatesAsync(verificationContext)).Values.Should().OnlyContain(state => !state.Enabled && !state.Forced);
        (await ReadPoliciesAsync(verificationContext)).Should().BeEquivalentTo(ExpectedPolicies);
        (await verificationContext.UserTutorialProgresses.AsNoTracking().CountAsync()).Should().Be(1);
        (await verificationContext.UserTutorialStepProgresses.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Test]
    public async Task DownMigration_DisablesTutorialRlsDropsPoliciesAndPreservesData()
    {
        await using var lease = await PostgreSqlDatabaseLease.CreateAsync();
        await using (var baselineContext = CreateDbContext(lease))
        {
            await MigrateAsync(baselineContext, PrePolicyMigrationId);
            await SeedTutorialAsync(baselineContext);
        }

        await using (var forwardContext = CreateDbContext(lease))
        {
            await forwardContext.Database.MigrateAsync();
        }

        await using (var downgradeContext = CreateDbContext(lease))
        {
            await MigrateAsync(downgradeContext, PrePolicyMigrationId);
        }

        await using var verificationContext = CreateDbContext(lease);
        (await ReadTableStatesAsync(verificationContext)).Values.Should().OnlyContain(state => !state.Enabled && !state.Forced);
        (await ReadPoliciesAsync(verificationContext)).Should().BeEmpty();
        (await verificationContext.UserTutorialProgresses.AsNoTracking().CountAsync()).Should().Be(1);
        (await verificationContext.UserTutorialStepProgresses.AsNoTracking().CountAsync()).Should().Be(1);
    }

    private static AppDbContext CreateDbContext(PostgreSqlDatabaseLease lease)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(lease.ConnectionString).Options);

    private static Task MigrateAsync(AppDbContext dbContext, string targetMigration)
        => dbContext.Database.GetService<IMigrator>().MigrateAsync(targetMigration);

    private static string GetPolicyMigrationId(IEnumerable<string> migrations)
    {
        var matches = migrations.Where(migration => migration.EndsWith(PolicyMigrationName, StringComparison.Ordinal)).ToArray();
        matches.Should().ContainSingle();
        return matches[0];
    }

    private static async Task SeedTutorialAsync(AppDbContext dbContext)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Id<User>.New(),
            Name = "tutorial-rls-migration-user",
            Email = "tutorial-rls-migration-user@example.test",
            ProfileRank = "Rookie",
            CreatedAt = now,
            UpdatedAt = now
        };
        var progress = new UserTutorialProgress
        {
            Id = Id<UserTutorialProgress>.New(),
            UserId = user.Id,
            TutorialType = TutorialType.OnboardingDemo,
            CreatedAt = now,
            UpdatedAt = now
        };
        var step = new UserTutorialStepProgress
        {
            Id = Id<UserTutorialStepProgress>.New(),
            UserTutorialProgressId = progress.Id,
            TutorialStep = TutorialStep.CreateArea,
            CompletedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.AddRange(user, progress, step);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<IReadOnlyDictionary<string, TutorialTableState>> ReadTableStatesAsync(AppDbContext dbContext)
    {
        await dbContext.Database.OpenConnectionAsync();
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT relation.relname, relation.relrowsecurity, relation.relforcerowsecurity
                FROM pg_class relation
                JOIN pg_namespace namespace ON namespace.oid = relation.relnamespace
                WHERE namespace.nspname = 'public'
                  AND relation.relname IN ('UserTutorialProgresses', 'UserTutorialStepProgresses');
                """;
            await using var reader = await command.ExecuteReaderAsync();
            var states = new Dictionary<string, TutorialTableState>(StringComparer.Ordinal);
            while (await reader.ReadAsync())
            {
                states.Add(reader.GetString(0), new TutorialTableState(reader.GetBoolean(1), reader.GetBoolean(2)));
            }

            states.Keys.Should().BeEquivalentTo(ExpectedPolicies.Select(policy => policy.Table).Distinct());
            return states;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task<IReadOnlyList<TutorialPolicy>> ReadPoliciesAsync(AppDbContext dbContext)
    {
        await dbContext.Database.OpenConnectionAsync();
        try
        {
            await using var command = dbContext.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT relation.relname, policy.polname,
                    CASE policy.polcmd WHEN 'r' THEN 'SELECT' WHEN 'a' THEN 'INSERT' WHEN 'w' THEN 'UPDATE' WHEN 'd' THEN 'DELETE' ELSE 'ALL' END
                FROM pg_policy policy
                JOIN pg_class relation ON relation.oid = policy.polrelid
                JOIN pg_namespace namespace ON namespace.oid = relation.relnamespace
                WHERE namespace.nspname = 'public'
                  AND relation.relname IN ('UserTutorialProgresses', 'UserTutorialStepProgresses');
                """;
            await using var reader = await command.ExecuteReaderAsync();
            var policies = new List<TutorialPolicy>();
            while (await reader.ReadAsync())
            {
                policies.Add(new TutorialPolicy(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }

            return policies;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private sealed record TutorialPolicy(string Table, string Name, string Command);

    private sealed record TutorialTableState(bool Enabled, bool Forced);
}
