using FluentAssertions;
using LgymApi.Domain.ValueObjects;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.BackgroundWorker.Notifications;
using LgymApi.Application.Repositories;
using LgymApi.BackgroundWorker.Common;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using EmailNotificationType = LgymApi.Domain.Notifications.EmailNotificationType;
using EmailNotificationTypes = LgymApi.Domain.Notifications.EmailNotificationTypes;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class WelcomeEmailServicesTests
{
    [Test]
    public async Task Scheduler_WhenNoExistingNotification_AddsAndEnqueues()
    {
        var repository = new FakeNotificationRepository();
        var scheduler = new FakeBackgroundScheduler();
        var unitOfWork = new FakeUnitOfWork();
        var metrics = new FakeEmailMetrics();

        var service = new EmailSchedulerService<WelcomeEmailPayload>(
            repository,
            unitOfWork,
            new EnabledFeature(),
            metrics,
            NullLogger<EmailSchedulerService<WelcomeEmailPayload>>.Instance);

        await service.ScheduleAsync(new WelcomeEmailPayload
        {
            UserId = Id<LgymApi.Domain.Entities.User>.New(),
            UserName = "Alex",
            RecipientEmail = "alex@example.com",
            CultureName = "en-US"
        });

        repository.Added.Should().HaveCount(1);
        unitOfWork.SaveChangesCalls.Should().Be(1);
        scheduler.EnqueuedNotificationIds.Should().BeEmpty();
        metrics.Enqueued.Should().Be(1);
        metrics.Retried.Should().Be(0);
    }

    [Test]
    public async Task Scheduler_WhenExistingFailedAtLimit_DoesNotEnqueue()
    {
        var existing = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Failed,
            Attempts = 5,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "alex@example.com",
            PayloadJson = "{}"
        };

        var repository = new FakeNotificationRepository { ExistingByCorrelation = existing };
        var scheduler = new FakeBackgroundScheduler();
        var unitOfWork = new FakeUnitOfWork();
        var metrics = new FakeEmailMetrics();

        var service = new EmailSchedulerService<WelcomeEmailPayload>(
            repository,
            unitOfWork,
            new EnabledFeature(),
            metrics,
            NullLogger<EmailSchedulerService<WelcomeEmailPayload>>.Instance);

        await service.ScheduleAsync(new WelcomeEmailPayload
        {
            UserId = existing.CorrelationId.Rebind<LgymApi.Domain.Entities.User>(),
            UserName = "Alex",
            RecipientEmail = existing.Recipient,
            CultureName = "en-US"
        });

        scheduler.EnqueuedNotificationIds.Should().BeEmpty();
        repository.Added.Should().BeEmpty();
        metrics.Enqueued.Should().Be(0);
        metrics.Retried.Should().Be(0);
    }

    [Test]
    public async Task Scheduler_WhenEmailNotificationsDisabled_DoesNotCreateOrEnqueue()
    {
        var repository = new FakeNotificationRepository();
        var scheduler = new FakeBackgroundScheduler();
        var unitOfWork = new FakeUnitOfWork();
        var metrics = new FakeEmailMetrics();

        var service = new EmailSchedulerService<WelcomeEmailPayload>(
            repository,
            unitOfWork,
            new DisabledFeature(),
            metrics,
            NullLogger<EmailSchedulerService<WelcomeEmailPayload>>.Instance);

        await service.ScheduleAsync(new WelcomeEmailPayload
        {
            UserId = Id<LgymApi.Domain.Entities.User>.New(),
            UserName = "Alex",
            RecipientEmail = "alex@example.com",
            CultureName = "en-US"
        });

        repository.Added.Should().BeEmpty();
        unitOfWork.SaveChangesCalls.Should().Be(0);
        scheduler.EnqueuedNotificationIds.Should().BeEmpty();
        metrics.Enqueued.Should().Be(0);
        metrics.Retried.Should().Be(0);
    }

    [Test]
    public async Task Scheduler_WhenConcurrentInsertDetected_EnqueuesExistingNotification()
    {
        var existing = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Pending,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "alex@example.com",
            PayloadJson = "{}"
        };

        var repository = new FakeNotificationRepository
        {
            ExistingByCorrelationOnSecondLookup = existing
        };
        var scheduler = new FakeBackgroundScheduler();
        var unitOfWork = new FakeUnitOfWork { ThrowOnSave = true };
        var metrics = new FakeEmailMetrics();

        var service = new EmailSchedulerService<WelcomeEmailPayload>(
            repository,
            unitOfWork,
            new EnabledFeature(),
            metrics,
            NullLogger<EmailSchedulerService<WelcomeEmailPayload>>.Instance);

        await service.ScheduleAsync(new WelcomeEmailPayload
        {
            UserId = existing.CorrelationId.Rebind<LgymApi.Domain.Entities.User>(),
            UserName = "Alex",
            RecipientEmail = existing.Recipient,
            CultureName = "en-US"
        });

        scheduler.EnqueuedNotificationIds.Should().BeEmpty();
        metrics.Enqueued.Should().Be(1);
        metrics.Retried.Should().Be(0);
    }

    [Test]
    public async Task JobHandler_WhenTemplateComposerThrows_MarksFailedAndSaves()
    {
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Pending,
            Attempts = 0,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "alex@example.com",
            PayloadJson = "{\"userId\":\"d75e53b9-2701-4cb0-b2d1-c02f0dbf8aa0\",\"userName\":\"Alex\",\"recipientEmail\":\"alex@example.com\",\"cultureName\":\"en-US\"}"
        };

        var repository = new FakeNotificationRepository { ExistingById = notification };
        var unitOfWork = new FakeUnitOfWork();
        var metrics = new FakeEmailMetrics();
        var handler = new EmailJobHandlerService(
            repository,
            new FakeTemplateComposerFactory(new ThrowingComposer()),
            new FakeEmailSender(),
            unitOfWork,
            metrics,
             NullLogger<EmailJobHandlerService>.Instance);

        await FluentActions.Invoking(() => handler.ProcessAsync(notification.Id)).Should().ThrowAsync<InvalidOperationException>();
        notification.Status.Should().Be(EmailNotificationStatus.Failed);
        notification.LastError.Should().StartWith("InvalidOperationException");
        unitOfWork.SaveChangesCalls.Should().Be(1);
        metrics.Failed.Should().Be(1);
    }

    [Test]
    public async Task JobHandler_WhenAlreadySent_DoesNothing()
    {
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Sent,
            Attempts = 2,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "alex@example.com",
            PayloadJson = "{}"
        };

        var repository = new FakeNotificationRepository { ExistingById = notification };
        var unitOfWork = new FakeUnitOfWork();
        var sender = new FakeEmailSender();
        var metrics = new FakeEmailMetrics();
        var handler = new EmailJobHandlerService(
            repository,
            new FakeTemplateComposerFactory(new PassThroughComposer()),
            sender,
            unitOfWork,
            metrics,
            NullLogger<EmailJobHandlerService>.Instance);

        await handler.ProcessAsync(notification.Id);

        unitOfWork.SaveChangesCalls.Should().Be(0);
        sender.SendCalls.Should().Be(0);
        notification.Attempts.Should().Be(2);
        metrics.Sent.Should().Be(0);
        metrics.Failed.Should().Be(0);
        metrics.Retried.Should().Be(0);
    }

    private sealed class FakeNotificationRepository : IEmailNotificationLogRepository
    {
        public NotificationMessage? ExistingById { get; set; }
        public NotificationMessage? ExistingByCorrelation { get; set; }
        public NotificationMessage? ExistingByCorrelationOnSecondLookup { get; set; }
        public List<NotificationMessage> Added { get; } = new();
        private int _correlationLookups;

        public Task AddAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            Added.Add(message);
            return Task.CompletedTask;
        }

        public Task<NotificationMessage?> FindByIdAsync(LgymApi.Domain.ValueObjects.Id<NotificationMessage> id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingById);
        }

        public Task<NotificationMessage?> FindByCorrelationAsync(EmailNotificationType type, Id<CorrelationScope> correlationId, string recipient, CancellationToken cancellationToken = default)
        {
            _correlationLookups += 1;
            if (_correlationLookups >= 2 && ExistingByCorrelationOnSecondLookup != null)
            {
                return Task.FromResult<NotificationMessage?>(ExistingByCorrelationOnSecondLookup);
            }

            return Task.FromResult(ExistingByCorrelation);
        }

        public Task<List<NotificationMessage>> GetPendingUndispatchedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<NotificationMessage>());

        public Task<List<NotificationMessage>> GetFailedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<NotificationMessage>());

        public Task<List<NotificationMessage>> GetDeadLetteredAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<NotificationMessage>());

        public Task<int> CountByStatusAsync(EmailNotificationStatus status, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> DeleteSentOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<bool> TryTransitionToSendingAsync(Id<NotificationMessage> id, CancellationToken cancellationToken = default)
            => Task.FromResult(ExistingById is { Status: EmailNotificationStatus.Pending });

        public Task<List<NotificationMessage>> GetStuckSendingAsync(int emailSendLeaseSeconds, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<NotificationMessage>());
    }

    private sealed class FakeBackgroundScheduler : IEmailBackgroundScheduler
    {
        public List<Id<NotificationMessage>> EnqueuedNotificationIds { get; } = new();

        public string? Enqueue(Id<NotificationMessage> notificationId)
        {
            EnqueuedNotificationIds.Add(notificationId);
            return "test-email-job-id";
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCalls { get; private set; }
        public bool ThrowOnSave { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls += 1;
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("Simulated concurrent insert failure");
            }

            return Task.FromResult(1);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction());
        }

        public void DetachEntity<TEntity>(TEntity entity) where TEntity : class { }
    }

    private sealed class FakeTransaction : IUnitOfWorkTransaction
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingComposer : IEmailTemplateComposer
    {
        public EmailNotificationType NotificationType => EmailNotificationTypes.Welcome;

        public EmailMessage Compose(string payloadJson)
        {
            throw new InvalidOperationException("Template missing");
        }
    }

    private sealed class PassThroughComposer : IEmailTemplateComposer
    {
        public EmailNotificationType NotificationType => EmailNotificationTypes.Welcome;

        public EmailMessage Compose(string payloadJson)
        {
            return new EmailMessage
            {
                To = "alex@example.com",
                Subject = "x",
                Body = "y"
            };
        }
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public int SendCalls { get; private set; }

        public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            SendCalls += 1;
            return Task.FromResult(true);
        }
    }

    private sealed class EnabledFeature : IEmailNotificationsFeature
    {
        public bool Enabled => true;
    }

    private sealed class FakeEmailMetrics : IEmailMetrics
    {
        public int Enqueued { get; private set; }
        public int Sent { get; private set; }
        public int Failed { get; private set; }
        public int Retried { get; private set; }

        public void RecordEnqueued(EmailNotificationType notificationType) => Enqueued += 1;
        public void RecordSent(EmailNotificationType notificationType) => Sent += 1;
        public void RecordFailed(EmailNotificationType notificationType) => Failed += 1;
        public void RecordRetried(EmailNotificationType notificationType) => Retried += 1;
    }

    private sealed class FakeTemplateComposerFactory : IEmailTemplateComposerFactory
    {
        private readonly IEmailTemplateComposer _composer;

        public FakeTemplateComposerFactory(IEmailTemplateComposer composer)
        {
            _composer = composer;
        }

        public EmailMessage ComposeMessage(EmailNotificationType notificationType, string payloadJson)
        {
            return _composer.Compose(payloadJson);
        }
    }

    private sealed class DisabledFeature : IEmailNotificationsFeature
    {
        public bool Enabled => false;
    }
}
