using FluentAssertions;
using LgymApi.Application.Notifications;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class PushRetentionCleanupIntegrationTests
{
    [Test]
    public async Task CleanupAsync_WhenRowsExceedBatchSize_PhysicallyPurgesEligibleRowsAndLeavesProtectedRows()
    {
        using var baseFactory = new CustomWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PushNotifications:MessageHistoryDays", "30");
            builder.UseSetting("PushNotifications:DisabledInstallationDays", "30");
            builder.UseSetting("PushNotifications:RetentionPurgeBatchSize", "2");
        });
        var old = DateTimeOffset.UtcNow.AddDays(-31);
        Id<PushInstallation> activeInstallationId;
        Id<PushInstallation> disabledInstallationId;
        Id<PushInstallation> disassociatedInstallationId;
        Id<PushNotificationMessage> retainedMessageId;

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new User
            {
                Id = Id<User>.New(),
                Name = "retention-user",
                Email = "retention-user@example.test",
                ProfileRank = "Junior 1"
            };
            var active = CreateInstallation(user.Id, null);
            var disabled = CreateInstallation(user.Id, old);
            var disabledSecond = CreateInstallation(user.Id, old.AddMinutes(1));
            var disabledThird = CreateInstallation(user.Id, old.AddMinutes(2));
            var softDeletedDisabled = CreateInstallation(user.Id, old.AddMinutes(3), isDeleted: true);
            var disassociated = CreateInstallation(null, null);
            activeInstallationId = active.Id;
            disabledInstallationId = disabled.Id;
            disassociatedInstallationId = disassociated.Id;
            retainedMessageId = Id<PushNotificationMessage>.New();

            database.Users.Add(user);
            database.PushInstallations.AddRange(active, disabled, disabledSecond, disabledThird, softDeletedDisabled, disassociated);
            database.PushNotificationMessages.AddRange(
                CreateMessage(active.Id, old, "old-message-1"),
                CreateMessage(active.Id, old.AddMinutes(1), "old-message-2"),
                CreateMessage(active.Id, old.AddMinutes(2), "old-message-3"),
                CreateMessage(active.Id, old.AddMinutes(3), "soft-deleted-message", isDeleted: true),
                CreateMessage(disabled.Id, DateTimeOffset.UtcNow, "cascade-message"),
                CreateMessage(active.Id, DateTimeOffset.UtcNow, "retained-message", retainedMessageId));
            await database.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var messageCleanup = scope.ServiceProvider.GetRequiredService<IPushNotificationMessageRetentionCleanupService>();
            var installationCleanup = scope.ServiceProvider.GetRequiredService<IDisabledPushInstallationRetentionCleanupService>();

            (await messageCleanup.CleanupAsync()).Should().Be(4);
            (await messageCleanup.CleanupAsync()).Should().Be(0);
            (await installationCleanup.CleanupAsync()).Should().Be(4);
            (await installationCleanup.CleanupAsync()).Should().Be(0);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var installations = await database.PushInstallations.AsNoTracking().ToListAsync();
            var messages = await database.PushNotificationMessages.AsNoTracking().ToListAsync();

            installations.Select(installation => installation.Id).Should().Contain(activeInstallationId).And.Contain(disassociatedInstallationId);
            installations.Select(installation => installation.Id).Should().NotContain(disabledInstallationId);
            messages.Should().ContainSingle(message => message.Id == retainedMessageId);
        }
    }

    [Test]
    public async Task CleanupAsync_WhenInAppRowsExceedBatchSize_PhysicallyPurgesOnlyExpiredActiveRows()
    {
        using var baseFactory = new CustomWebApplicationFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("PushNotifications:InAppNotificationDays", "90");
            builder.UseSetting("PushNotifications:RetentionPurgeBatchSize", "2");
        });
        var expired = DateTimeOffset.UtcNow.AddDays(-91);
        Id<InAppNotification> retainedId;
        Id<InAppNotification> softDeletedId;

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            retainedId = Id<InAppNotification>.New();
            softDeletedId = Id<InAppNotification>.New();
            database.InAppNotifications.AddRange(
                CreateInAppNotification(expired, "expired-1"),
                CreateInAppNotification(expired.AddMinutes(1), "expired-2"),
                CreateInAppNotification(expired.AddMinutes(2), "expired-3"),
                CreateInAppNotification(DateTimeOffset.UtcNow.AddDays(-89), "retained", retainedId),
                CreateInAppNotification(expired, "soft-deleted", softDeletedId, true));
            await database.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var cleanup = scope.ServiceProvider.GetRequiredService<IInAppNotificationRetentionCleanupService>();

            (await cleanup.CleanupAsync()).Should().Be(4);
            (await cleanup.CleanupAsync()).Should().Be(0);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifications = await database.InAppNotifications.IgnoreQueryFilters().AsNoTracking().ToListAsync();

            notifications.Select(notification => notification.Id).Should().ContainSingle().Which.Should().Be(retainedId);
        }
    }

    private static PushInstallation CreateInstallation(Id<User>? userId, DateTimeOffset? disabledAt, bool isDeleted = false)
        => new()
        {
            Id = Id<PushInstallation>.New(),
            UserId = userId,
            InstallationId = Id<PushInstallation>.New().ToString(),
            Platform = "android",
            FcmToken = "test-token",
            Environment = "test",
            LastSeenAt = disabledAt ?? DateTimeOffset.UtcNow,
            DisabledAt = disabledAt,
            DisabledReason = disabledAt == null ? null : "InactiveStale",
            IsDeleted = isDeleted
        };

    private static PushNotificationMessage CreateMessage(
        Id<PushInstallation> installationId,
        DateTimeOffset createdAt,
        string eventId,
        Id<PushNotificationMessage>? id = null,
        bool isDeleted = false)
        => new()
        {
            Id = id ?? Id<PushNotificationMessage>.New(),
            UserId = Id<User>.New(),
            PushInstallationId = installationId,
            SchemaVersion = 1,
            Type = "retention.test",
            EventId = eventId,
            PayloadJson = "{}",
            Status = PushNotificationStatus.Sent,
            IsDeleted = isDeleted,
            CreatedAt = createdAt
        };

    private static InAppNotification CreateInAppNotification(
        DateTimeOffset createdAt,
        string deliveryKey,
        Id<InAppNotification>? id = null,
        bool isDeleted = false)
        => new()
        {
            Id = id ?? Id<InAppNotification>.New(),
            RecipientId = Id<User>.New(),
            DeliveryKey = deliveryKey,
            Message = "retention test",
            Type = InAppNotificationTypes.InvitationSent,
            IsDeleted = isDeleted,
            CreatedAt = createdAt
        };
}
