using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Notifications.Email;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LgymApi.IntegrationTests;

[TestFixture]
[Category("PostgreSql")]
internal sealed class PostgreSqlNotificationTransactionTests : PostgreSqlIntegrationTestBase
{
    [Test]
    public async Task EmailProcessAsync_ClaimsDurablyBeforeDeliveryAndPersistsSentStatus()
    {
        var notificationId = await SeedEmailNotificationAsync();
        var sender = new ObservingEmailSender(() => ReadEmailStatusAsync(notificationId));

        await using (var serviceScope = Factory.Services.CreateAsyncScope())
        {
            var database = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var handler = new EmailJobHandlerService(
                new EmailNotificationLogRepository(database),
                new TestEmailComposerFactory(),
                sender,
                new EfUnitOfWork(database),
                new NoOpEmailMetrics(),
                NullLogger<EmailJobHandlerService>.Instance);

            await handler.ProcessAsync(notificationId.ToString());
        }

        sender.SendCalls.Should().Be(1);
        sender.StatusObservedBeforeSend.Should().Be(EmailNotificationStatus.Sending);
        var persisted = await ReadEmailAsync(notificationId);
        persisted.Status.Should().Be(EmailNotificationStatus.Sent);
        persisted.Attempts.Should().Be(1);
        persisted.SentAt.Should().NotBeNull();
        persisted.DeliveredAt.Should().NotBeNull();
    }

    [Test]
    public async Task EmailProcessAsync_WhenDeliveryFails_RecoversStatusAfterTheDurableClaim()
    {
        var notificationId = await SeedEmailNotificationAsync();
        var senderFailure = new InvalidOperationException("Forced email provider failure.");
        var sender = new ObservingEmailSender(() => ReadEmailStatusAsync(notificationId), senderFailure);

        await using (var serviceScope = Factory.Services.CreateAsyncScope())
        {
            var database = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var handler = new EmailJobHandlerService(
                new EmailNotificationLogRepository(database),
                new TestEmailComposerFactory(),
                sender,
                new EfUnitOfWork(database),
                new NoOpEmailMetrics(),
                NullLogger<EmailJobHandlerService>.Instance);

            var action = () => handler.ProcessAsync(notificationId.ToString());

            await action.Should().ThrowAsync<InvalidOperationException>();
        }

        sender.StatusObservedBeforeSend.Should().Be(EmailNotificationStatus.Sending);
        var persisted = await ReadEmailAsync(notificationId);
        persisted.Status.Should().Be(EmailNotificationStatus.Failed);
        persisted.Attempts.Should().Be(1);
        persisted.LastError.Should().Contain(nameof(InvalidOperationException));
        persisted.SentAt.Should().BeNull();
        persisted.DeliveredAt.Should().BeNull();
    }

    [Test]
    public async Task PushProcessAsync_WhenProviderFails_ClaimsBeforeDeliveryAndPersistsRecoveryBeforeRetryDispatch()
    {
        var (messageId, installationId) = await SeedPushNotificationAsync();
        var providerFailure = new HttpRequestException("Forced push provider failure.");
        var sender = new ObservingPushSender(() => ReadPushStatusAsync(messageId), providerFailure);
        var scheduler = new ObservingPushScheduler(() => ReadPushStatus(messageId));

        await using (var serviceScope = Factory.Services.CreateAsyncScope())
        {
            var database = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var service = new PushNotificationDeliveryService(
                new PushNotificationMessageRepository(database),
                new PushInstallationRepository(database),
                sender,
                scheduler,
                new TestRetrySettings(),
                new EfUnitOfWork(database),
                NullLogger<PushNotificationDeliveryService>.Instance);

            var action = () => service.ProcessAsync(messageId.ToString());

            await action.Should().ThrowAsync<HttpRequestException>();
        }

        sender.SendCalls.Should().Be(1);
        sender.LastInstallationId.Should().Be(installationId);
        sender.StatusObservedBeforeSend.Should().Be(PushNotificationStatus.Sending);
        scheduler.ScheduleRetryCalls.Should().Be(1);
        scheduler.StatusObservedBeforeScheduling.Should().Be(PushNotificationStatus.Failed);
        var persisted = await ReadPushAsync(messageId);
        persisted.Status.Should().Be(PushNotificationStatus.Failed);
        persisted.FailureKind.Should().Be(PushNotificationFailureKind.Transient);
        persisted.Attempts.Should().Be(1);
        persisted.SchedulerJobId.Should().Be("task-36-retry-job");
        persisted.NextAttemptAt.Should().NotBeNull();
    }

    private async Task<Id<NotificationMessage>> SeedEmailNotificationAsync()
    {
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Channel = NotificationChannel.Email,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = new Email("task-36@example.com"),
            PayloadJson = "{}",
            Status = EmailNotificationStatus.Pending
        };
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.NotificationMessages.Add(notification);
        await database.SaveChangesAsync();
        return notification.Id;
    }

    private async Task<(Id<PushNotificationMessage> MessageId, Id<PushInstallation> InstallationId)> SeedPushNotificationAsync()
    {
        var user = await SeedUserAsync($"push-recovery-{Id<User>.New():N}", $"push-recovery-{Id<User>.New():N}@example.com");
        var installation = new PushInstallation
        {
            Id = Id<PushInstallation>.New(),
            UserId = user.Id,
            InstallationId = $"task-36-{Id<PushInstallation>.New():N}",
            Platform = "android",
            FcmToken = "test-token",
            Environment = "test",
            PermissionStatus = "authorized",
            LastSeenAt = DateTimeOffset.UtcNow
        };
        var payload = new PushEventPayload(1, "task.36", "event-36", null, null, null);
        var message = new PushNotificationMessage
        {
            Id = Id<PushNotificationMessage>.New(),
            UserId = user.Id,
            PushInstallationId = installation.Id,
            SchemaVersion = payload.SchemaVersion,
            Type = payload.Type,
            EventId = payload.EventId,
            PayloadJson = JsonSerializer.Serialize(payload, SharedSerializationOptions.Current),
            Status = PushNotificationStatus.Pending
        };

        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        database.PushInstallations.Add(installation);
        database.PushNotificationMessages.Add(message);
        await database.SaveChangesAsync();
        return (message.Id, installation.Id);
    }

    private async Task<EmailNotificationStatus> ReadEmailStatusAsync(Id<NotificationMessage> notificationId)
        => (await ReadEmailAsync(notificationId)).Status;

    private async Task<NotificationMessage> ReadEmailAsync(Id<NotificationMessage> notificationId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.NotificationMessages.AsNoTracking()
            .SingleAsync(notification => notification.Id == notificationId);
    }

    private async Task<PushNotificationStatus> ReadPushStatusAsync(Id<PushNotificationMessage> messageId)
        => (await ReadPushAsync(messageId)).Status;

    private PushNotificationStatus ReadPushStatus(Id<PushNotificationMessage> messageId)
    {
        using var scope = Factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return database.PushNotificationMessages.AsNoTracking()
            .Where(message => message.Id == messageId)
            .Select(message => message.Status)
            .Single();
    }

    private async Task<PushNotificationMessage> ReadPushAsync(Id<PushNotificationMessage> messageId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await database.PushNotificationMessages.AsNoTracking()
            .SingleAsync(message => message.Id == messageId);
    }

    private sealed class TestEmailComposerFactory : IEmailTemplateComposerFactory
    {
        public EmailMessage ComposeMessage(EmailNotificationType notificationType, string payloadJson) => new()
        {
            To = "task-36@example.com",
            Subject = "Task 36",
            Body = "Transaction proof"
        };
    }

    private sealed class NoOpEmailMetrics : IEmailMetrics
    {
        public void RecordEnqueued(EmailNotificationType notificationType) { }
        public void RecordSent(EmailNotificationType notificationType) { }
        public void RecordFailed(EmailNotificationType notificationType) { }
        public void RecordRetried(EmailNotificationType notificationType) { }
    }

    private sealed class ObservingEmailSender(
        Func<Task<EmailNotificationStatus>> readStatus,
        Exception? exception = null) : IEmailSender
    {
        public int SendCalls { get; private set; }
        public EmailNotificationStatus? StatusObservedBeforeSend { get; private set; }

        public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            SendCalls++;
            StatusObservedBeforeSend = await readStatus();
            if (exception is not null)
            {
                throw exception;
            }

            return true;
        }
    }

    private sealed class ObservingPushSender(
        Func<Task<PushNotificationStatus>> readStatus,
        Exception exception) : IPushProviderSender
    {
        public int SendCalls { get; private set; }
        public Id<PushInstallation>? LastInstallationId { get; private set; }
        public PushNotificationStatus? StatusObservedBeforeSend { get; private set; }

        public async Task<PushSendAttemptResult> SendAsync(
            Id<PushInstallation> installationId,
            PushEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            SendCalls++;
            LastInstallationId = installationId;
            StatusObservedBeforeSend = await readStatus();
            throw exception;
        }
    }

    private sealed class ObservingPushScheduler(Func<PushNotificationStatus> readStatus) : IPushBackgroundScheduler
    {
        public int ScheduleRetryCalls { get; private set; }
        public PushNotificationStatus? StatusObservedBeforeScheduling { get; private set; }

        public string? Enqueue(string notificationId)
            => throw new InvalidOperationException("Unexpected initial enqueue.");

        public string? ScheduleRetry(string notificationId, TimeSpan delay)
        {
            ScheduleRetryCalls++;
            StatusObservedBeforeScheduling = readStatus();
            return "task-36-retry-job";
        }
    }

    private sealed class TestRetrySettings : IPushNotificationDeliveryRetrySettings
    {
        public IReadOnlyList<int> RetryDelaysSeconds { get; } = [5];
    }
}
