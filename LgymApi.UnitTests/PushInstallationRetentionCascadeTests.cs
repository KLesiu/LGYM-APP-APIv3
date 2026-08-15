using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PushInstallationRetentionCascadeTests
{
    [Test]
    public async Task RemoveRange_WhenDeletingDisabledInstallation_CascadesToItsMessagesWithoutOrphans()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var installationId = Id<PushInstallation>.New();
        var messageId = Id<PushNotificationMessage>.New();

        await using (var database = new AppDbContext(options))
        {
            await database.Database.EnsureCreatedAsync();
            var user = new User
            {
                Id = Id<User>.New(),
                Name = "retention-cascade-user",
                Email = "retention-cascade-user@example.test",
                ProfileRank = "Junior 1"
            };
            database.Users.Add(user);
            database.PushInstallations.Add(new PushInstallation
            {
                Id = installationId,
                UserId = user.Id,
                InstallationId = "retention-cascade-installation",
                Platform = "android",
                FcmToken = "test-token",
                Environment = "test",
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-31),
                DisabledAt = DateTimeOffset.UtcNow.AddDays(-31),
                DisabledReason = "InactiveStale"
            });
            database.PushNotificationMessages.Add(new PushNotificationMessage
            {
                Id = messageId,
                UserId = user.Id,
                PushInstallationId = installationId,
                SchemaVersion = 1,
                Type = "retention.test",
                EventId = "retention-cascade-message",
                PayloadJson = "{}",
                Status = PushNotificationStatus.Sent
            });
            await database.SaveChangesAsync();

            var repository = new PushInstallationRepository(database);
            repository.RemoveRange([await database.PushInstallations.SingleAsync(installation => installation.Id == installationId)]);
            await new EfUnitOfWork(database).SaveChangesAsync();
        }

        await using var freshDatabase = new AppDbContext(options);
        (await freshDatabase.PushInstallations.AsNoTracking().AnyAsync(installation => installation.Id == installationId)).Should().BeFalse();
        (await freshDatabase.PushNotificationMessages.AsNoTracking().AnyAsync(message => message.Id == messageId)).Should().BeFalse();
    }

    [Test]
    public async Task RemoveForAccountAsync_WhenInstallationIsSoftDeleted_RemovesInstallationsAndMessagesForAccount()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var softDeletedInstallationId = Id<PushInstallation>.New();
        var softDeletedMessageId = Id<PushNotificationMessage>.New();

        await using (var database = new AppDbContext(options))
        {
            await database.Database.EnsureCreatedAsync();
            var user = new User
            {
                Id = Id<User>.New(),
                Name = "soft-deleted-push-user",
                Email = "soft-delete-push@example.test",
                ProfileRank = "Junior 1"
            };
            database.Users.Add(user);
            database.PushInstallations.Add(new PushInstallation
            {
                Id = softDeletedInstallationId,
                UserId = user.Id,
                InstallationId = "soft-deleted-installation",
                Platform = "android",
                FcmToken = "soft-deleted-token",
                Environment = "test",
                LastSeenAt = DateTimeOffset.UtcNow,
                IsDeleted = true
            });
            database.PushNotificationMessages.Add(new PushNotificationMessage
            {
                Id = softDeletedMessageId,
                UserId = user.Id,
                PushInstallationId = softDeletedInstallationId,
                SchemaVersion = 1,
                Type = "account.delete",
                EventId = "soft-deleted-message",
                PayloadJson = "{}",
                Status = PushNotificationStatus.Sent
            });
            await database.SaveChangesAsync();

            var repository = new PushInstallationRepository(database);
            await repository.RemoveForAccountAsync(user.Id);
            await new EfUnitOfWork(database).SaveChangesAsync();
        }

        await using var verificationDatabase = new AppDbContext(options);
        (await verificationDatabase.PushInstallations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(installation => installation.Id == softDeletedInstallationId))
            .Should().BeFalse();
        (await verificationDatabase.PushNotificationMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(message => message.Id == softDeletedMessageId))
            .Should().BeFalse();
    }
}
