using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PushRetentionRepositoryTests
{
    [Test]
    public async Task GetRetentionCandidatesCreatedBeforeAsync_WhenRowsAreRetentionEligibleIncludingSoftDeleted_ReturnsOnlyOldestBoundedRowsAndStagesPhysicalDeletion()
    {
        var databaseName = $"push-message-retention-{Id<PushRetentionRepositoryTests>.New():N}";
        var cutoff = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero).AddDays(-30);
        var firstId = new Id<PushNotificationMessage>(new Guid("00000000-0000-0000-0000-000000000001"));
        var secondId = new Id<PushNotificationMessage>(new Guid("00000000-0000-0000-0000-000000000002"));
        var thirdId = new Id<PushNotificationMessage>(new Guid("00000000-0000-0000-0000-000000000003"));
        var atCutoffId = new Id<PushNotificationMessage>(new Guid("00000000-0000-0000-0000-000000000004"));
        var recentId = new Id<PushNotificationMessage>(new Guid("00000000-0000-0000-0000-000000000005"));
        var softDeletedId = new Id<PushNotificationMessage>(new Guid("00000000-0000-0000-0000-000000000000"));

        await using (var database = CreateDatabase(databaseName))
        {
            database.PushNotificationMessages.AddRange(
                CreateMessage(softDeletedId, cutoff.AddDays(-2), isDeleted: true),
                CreateMessage(firstId, cutoff.AddDays(-1)),
                CreateMessage(secondId, cutoff.AddDays(-1)),
                CreateMessage(thirdId, cutoff.AddDays(-1)),
                CreateMessage(atCutoffId, cutoff),
                CreateMessage(recentId, cutoff.AddDays(1)));
            await database.SaveChangesAsync();

            var repository = new PushNotificationMessageRepository(database);
            var candidates = await repository.GetRetentionCandidatesCreatedBeforeAsync(cutoff, 2);

            candidates.Select(message => message.Id).Should().Equal(softDeletedId, firstId);
            repository.RemoveRange(candidates);
            database.Entry(candidates[0]).State.Should().Be(EntityState.Deleted);

            await using var uncommittedDatabase = CreateDatabase(databaseName);
            (await uncommittedDatabase.PushNotificationMessages.Select(message => message.Id).ToListAsync())
                .Should().Contain(firstId).And.Contain(secondId);

            await database.SaveChangesAsync();
        }

        await using var freshDatabase = CreateDatabase(databaseName);
        var remainingIds = await freshDatabase.PushNotificationMessages
            .OrderBy(message => message.CreatedAt)
            .Select(message => message.Id)
            .ToListAsync();
        remainingIds.Should().Equal(secondId, thirdId, atCutoffId, recentId);
        remainingIds.Should().NotContain(softDeletedId).And.NotContain(firstId);
    }

    [Test]
    public async Task GetRetentionCandidatesDisabledBeforeAsync_WhenRowsAreRetentionEligibleIncludingSoftDeleted_ReturnsOnlyDisabledOldestBoundedRowsAndStagesPhysicalDeletion()
    {
        var databaseName = $"push-installation-retention-{Id<PushRetentionRepositoryTests>.New():N}";
        var cutoff = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero).AddDays(-30);
        var firstId = new Id<PushInstallation>(new Guid("00000000-0000-0000-0000-000000000011"));
        var secondId = new Id<PushInstallation>(new Guid("00000000-0000-0000-0000-000000000012"));
        var thirdId = new Id<PushInstallation>(new Guid("00000000-0000-0000-0000-000000000013"));
        var softDeletedId = new Id<PushInstallation>(new Guid("00000000-0000-0000-0000-000000000010"));

        await using (var database = CreateDatabase(databaseName))
        {
            database.PushInstallations.AddRange(
                CreateInstallation(softDeletedId, cutoff.AddDays(-2), isDeleted: true),
                CreateInstallation(firstId, cutoff.AddDays(-1)),
                CreateInstallation(secondId, cutoff.AddDays(-1)),
                CreateInstallation(thirdId, cutoff.AddDays(-1)),
                CreateInstallation(Id<PushInstallation>.New(), cutoff),
                CreateInstallation(Id<PushInstallation>.New(), null),
                CreateInstallation(Id<PushInstallation>.New(), null, isDisassociated: true));
            await database.SaveChangesAsync();

            var repository = new PushInstallationRepository(database);
            var candidates = await repository.GetRetentionCandidatesDisabledBeforeAsync(cutoff, 2);

            candidates.Select(installation => installation.Id).Should().Equal(softDeletedId, firstId);
            repository.RemoveRange(candidates);
            database.Entry(candidates[0]).State.Should().Be(EntityState.Deleted);

            await using var uncommittedDatabase = CreateDatabase(databaseName);
            (await uncommittedDatabase.PushInstallations.Select(installation => installation.Id).ToListAsync())
                .Should().Contain(firstId).And.Contain(secondId);

            await database.SaveChangesAsync();
        }

        await using var freshDatabase = CreateDatabase(databaseName);
        var remaining = await freshDatabase.PushInstallations.ToListAsync();
        remaining.Select(installation => installation.Id).Should().Contain(secondId).And.Contain(thirdId);
        remaining.Select(installation => installation.Id).Should().NotContain(softDeletedId).And.NotContain(firstId);
        remaining.Count(installation => installation.DisabledAt == null).Should().Be(2);
    }

    private static AppDbContext CreateDatabase(string databaseName)
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static PushNotificationMessage CreateMessage(Id<PushNotificationMessage> id, DateTimeOffset createdAt, bool isDeleted = false)
        => new()
        {
            Id = id,
            UserId = Id<User>.New(),
            PushInstallationId = Id<PushInstallation>.New(),
            SchemaVersion = 1,
            Type = "retention.test",
            EventId = id.ToString(),
            PayloadJson = "{}",
            Status = PushNotificationStatus.Sent,
            IsDeleted = isDeleted,
            CreatedAt = createdAt
        };

    private static PushInstallation CreateInstallation(
        Id<PushInstallation> id,
        DateTimeOffset? disabledAt,
        bool isDisassociated = false,
        bool isDeleted = false)
        => new()
        {
            Id = id,
            UserId = isDisassociated ? null : Id<User>.New(),
            InstallationId = id.ToString(),
            Platform = "android",
            FcmToken = "test-token",
            Environment = "test",
            LastSeenAt = disabledAt ?? DateTimeOffset.UtcNow,
            DisabledAt = disabledAt,
            DisabledReason = disabledAt == null ? null : "InactiveStale",
            IsDeleted = isDeleted
        };
}
