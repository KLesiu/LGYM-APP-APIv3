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
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using EmailNotificationType = LgymApi.Domain.Notifications.EmailNotificationType;
using EmailNotificationTypes = LgymApi.Domain.Notifications.EmailNotificationTypes;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class InvitationEmailServicesTests
{
    [Test]
    public async Task Scheduler_WhenNoExistingNotification_AddsAndEnqueues()
    {
        var repository = new FakeNotificationRepository();
        var scheduler = new FakeBackgroundScheduler();
        var unitOfWork = new FakeUnitOfWork();
        var metrics = new FakeEmailMetrics();

        var service = new EmailSchedulerService<InvitationEmailPayload>(
            repository,
            unitOfWork,
            new EnabledFeature(),
            metrics,
            NullLogger<EmailSchedulerService<InvitationEmailPayload>>.Instance);

        await service.ScheduleAsync(new InvitationEmailPayload
        {
            InvitationId = Id<TrainerInvitation>.New(),
            InvitationCode = "ABC123",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            TrainerName = "Coach",
            RecipientEmail = "trainee@example.com",
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
            Type = EmailNotificationTypes.TrainerInvitation,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "trainee@example.com",
            PayloadJson = "{}"
        };

        var repository = new FakeNotificationRepository { ExistingByCorrelation = existing };
        var scheduler = new FakeBackgroundScheduler();
        var unitOfWork = new FakeUnitOfWork();
        var metrics = new FakeEmailMetrics();

        var service = new EmailSchedulerService<InvitationEmailPayload>(
            repository,
            unitOfWork,
            new EnabledFeature(),
            metrics,
            NullLogger<EmailSchedulerService<InvitationEmailPayload>>.Instance);

        await service.ScheduleAsync(new InvitationEmailPayload
        {
            InvitationId = existing.CorrelationId.Rebind<LgymApi.Domain.Entities.TrainerInvitation>(),
            InvitationCode = "ABC123",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            TrainerName = "Coach",
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

        var service = new EmailSchedulerService<InvitationEmailPayload>(
            repository,
            unitOfWork,
            new DisabledFeature(),
            metrics,
            NullLogger<EmailSchedulerService<InvitationEmailPayload>>.Instance);

        await service.ScheduleAsync(new InvitationEmailPayload
        {
            InvitationId = Id<TrainerInvitation>.New(),
            InvitationCode = "ABC123",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            TrainerName = "Coach",
            RecipientEmail = "trainee@example.com",
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
            Type = EmailNotificationTypes.TrainerInvitation,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "trainee@example.com",
            PayloadJson = "{}"
        };

        var repository = new FakeNotificationRepository
        {
            ExistingByCorrelationOnSecondLookup = existing
        };
        var scheduler = new FakeBackgroundScheduler();
        var unitOfWork = new FakeUnitOfWork { ThrowOnSave = true };
        var metrics = new FakeEmailMetrics();

        var service = new EmailSchedulerService<InvitationEmailPayload>(
            repository,
            unitOfWork,
            new EnabledFeature(),
            metrics,
            NullLogger<EmailSchedulerService<InvitationEmailPayload>>.Instance);

        await service.ScheduleAsync(new InvitationEmailPayload
        {
            InvitationId = existing.CorrelationId.Rebind<LgymApi.Domain.Entities.TrainerInvitation>(),
            InvitationCode = "ABC123",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            TrainerName = "Coach",
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
            Type = EmailNotificationTypes.TrainerInvitation,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "trainee@example.com",
            PayloadJson = "{\"invitationId\":\"d75e53b9-2701-4cb0-b2d1-c02f0dbf8aa0\",\"invitationCode\":\"ABC123\",\"expiresAt\":\"2026-03-01T10:00:00+00:00\",\"trainerName\":\"Coach\",\"recipientEmail\":\"trainee@example.com\",\"cultureName\":\"en-US\"}"
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
            Type = EmailNotificationTypes.TrainerInvitation,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "trainee@example.com",
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

    [Test]
    public async Task JobHandler_WhenSaveFailsAfterSuccessfulSend_DoesNotThrowOrRetry()
    {
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Pending,
            Attempts = 0,
            Type = EmailNotificationTypes.TrainerInvitation,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "trainee@example.com",
            PayloadJson = "{}"
        };

        var repository = new FakeNotificationRepository { ExistingById = notification };
        var unitOfWork = new FakeUnitOfWork { ThrowOnSaveCallNumber = 1 };
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

        sender.SendCalls.Should().Be(1);
        unitOfWork.SaveChangesCalls.Should().Be(1);
        notification.Status.Should().Be(EmailNotificationStatus.Sent);
        notification.SentAt.Should().NotBeNull();
        metrics.Sent.Should().Be(0);
        metrics.Failed.Should().Be(0);
        metrics.Retried.Should().Be(0);
    }

    [Test]
    public async Task JobHandler_WhenSaveFailsAfterSuccessfulSend_LogsCriticalWhenEnabled()
    {
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Pending,
            Attempts = 0,
            Type = EmailNotificationTypes.TrainerInvitation,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "trainee@example.com",
            PayloadJson = "{}"
        };

        var repository = new FakeNotificationRepository { ExistingById = notification };
        var unitOfWork = new FakeUnitOfWork { ThrowOnSaveCallNumber = 1 };
        var sender = new FakeEmailSender();
        var metrics = new FakeEmailMetrics();
        var logger = new CapturingLogger<EmailJobHandlerService>();
        var handler = new EmailJobHandlerService(
            repository,
            new FakeTemplateComposerFactory(new PassThroughComposer()),
            sender,
            unitOfWork,
            metrics,
            logger);

        await handler.ProcessAsync(notification.Id);

        logger.CriticalMessages.Should().ContainSingle();
        logger.CriticalMessages[0].Should().Contain("delivered successfully but persisting Sent status failed");
        sender.SendCalls.Should().Be(1);
        metrics.Sent.Should().Be(0);
        metrics.Failed.Should().Be(0);
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
        public int? ThrowOnSaveCallNumber { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls += 1;
            if (ThrowOnSave || ThrowOnSaveCallNumber == SaveChangesCalls)
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
        public EmailNotificationType NotificationType => EmailNotificationTypes.TrainerInvitation;

        public EmailMessage Compose(string payloadJson)
        {
            throw new InvalidOperationException("Template missing");
        }
    }

    private sealed class PassThroughComposer : IEmailTemplateComposer
    {
        public EmailNotificationType NotificationType => EmailNotificationTypes.TrainerInvitation;

        public EmailMessage Compose(string payloadJson)
        {
            return new EmailMessage
            {
                To = "trainee@example.com",
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

    private sealed class DisabledFeature : IEmailNotificationsFeature
    {
        public bool Enabled => false;
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

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> CriticalMessages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel == LogLevel.Critical;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Critical)
            {
                CriticalMessages.Add(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    [Test]
    public async Task JobHandler_WhenRetrying_SuccessfulSendRecordsRetryAndLogs()
    {
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Pending,
            Attempts = 2,
            Type = EmailNotificationTypes.TrainerInvitation,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "trainee@example.com",
            PayloadJson = "{}"
        };

        var repository = new FakeNotificationRepository { ExistingById = notification };
        var unitOfWork = new FakeUnitOfWork();
        var sender = new FakeEmailSender();
        var metrics = new FakeEmailMetrics();
        var logger = new CapturingInfoLogger<EmailJobHandlerService>();
        var handler = new EmailJobHandlerService(
            repository,
            new FakeTemplateComposerFactory(new PassThroughComposer()),
            sender,
            unitOfWork,
            metrics,
            logger);

        await handler.ProcessAsync(notification.Id);

        sender.SendCalls.Should().Be(1);
        metrics.Retried.Should().Be(1);
        metrics.Sent.Should().Be(1);
        logger.InformationMessages.Should().Contain(m => m.Contains("Retrying"));
    }

    [Test]
    public async Task JobHandler_WhenSenderDisabled_MarksFailedAndRecordsFailure()
    {
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Pending,
            Attempts = 0,
            Type = EmailNotificationTypes.TrainerInvitation,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "trainee@example.com",
            PayloadJson = "{}"
        };

        var repository = new FakeNotificationRepository { ExistingById = notification };
        var unitOfWork = new FakeUnitOfWork();
        var sender = new DisabledEmailSender();
        var metrics = new FakeEmailMetrics();
        var handler = new EmailJobHandlerService(
            repository,
            new FakeTemplateComposerFactory(new PassThroughComposer()),
            sender,
            unitOfWork,
            metrics,
            NullLogger<EmailJobHandlerService>.Instance);

        await handler.ProcessAsync(notification.Id);

        notification.Status.Should().Be(EmailNotificationStatus.Failed);
        notification.LastError.Should().Be("Email sender is disabled.");
        sender.SendCalls.Should().Be(1);
        metrics.Failed.Should().Be(1);
        metrics.Sent.Should().Be(0);
    }

    [Test]
    public async Task JobHandler_WhenSecondSaveFailsAfterSent_LogsWarningAndDoesNotThrow()
    {
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Pending,
            Attempts = 0,
            Type = EmailNotificationTypes.TrainerInvitation,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "trainee@example.com",
            PayloadJson = "{}"
        };

        var repository = new FakeNotificationRepository { ExistingById = notification };
        var unitOfWork = new FakeUnitOfWork { ThrowOnSaveCallNumber = 2 };
        var sender = new FakeEmailSender();
        var metrics = new FakeEmailMetrics();
        var logger = new CapturingWarningLogger<EmailJobHandlerService>();
        var handler = new EmailJobHandlerService(
            repository,
            new FakeTemplateComposerFactory(new PassThroughComposer()),
            sender,
            unitOfWork,
            metrics,
            logger);

        await handler.ProcessAsync(notification.Id);

        sender.SendCalls.Should().Be(1);
        metrics.Sent.Should().Be(1);
        logger.WarningMessages.Should().ContainSingle();
    }

    [Test]
    public async Task JobHandler_WhenSenderThrows_MarksFailedAndRethrows()
    {
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Pending,
            Attempts = 0,
            Type = EmailNotificationTypes.TrainerInvitation,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "trainee@example.com",
            PayloadJson = "{}"
        };

        var repository = new FakeNotificationRepository { ExistingById = notification };
        var unitOfWork = new FakeUnitOfWork();
        var sender = new ThrowingEmailSender();
        var metrics = new FakeEmailMetrics();
        var handler = new EmailJobHandlerService(
            repository,
            new FakeTemplateComposerFactory(new PassThroughComposer()),
            sender,
            unitOfWork,
            metrics,
            NullLogger<EmailJobHandlerService>.Instance);

        await FluentActions.Invoking(() => handler.ProcessAsync(notification.Id)).Should().ThrowAsync<InvalidOperationException>();

        notification.Status.Should().Be(EmailNotificationStatus.Failed);
        notification.LastError.Should().StartWith("InvalidOperationException");
        metrics.Failed.Should().Be(1);
    }

    [Test]
    public async Task JobHandler_WhenComposerThrowsLongMultilineError_SanitizesAndTruncatesLastError()
    {
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Pending,
            Attempts = 0,
            Type = EmailNotificationTypes.TrainerInvitation,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = "trainee@example.com",
            PayloadJson = "{}"
        };

        var repository = new FakeNotificationRepository { ExistingById = notification };
        var unitOfWork = new FakeUnitOfWork();
        var metrics = new FakeEmailMetrics();
        var handler = new EmailJobHandlerService(
            repository,
            new FakeTemplateComposerFactory(new LongMessageComposer()),
            new FakeEmailSender(),
            unitOfWork,
            metrics,
            NullLogger<EmailJobHandlerService>.Instance);

        await FluentActions.Invoking(() => handler.ProcessAsync(notification.Id)).Should().ThrowAsync<InvalidOperationException>();

        notification.Status.Should().Be(EmailNotificationStatus.Failed);
        notification.LastError.Should().NotContain("\r").And.NotContain("\n");
        notification.LastError!.Length.Should().BeLessThanOrEqualTo(400);
        notification.LastError.Should().Contain("First line");
    }

    private sealed class DisabledEmailSender : IEmailSender
    {
        public int SendCalls { get; private set; }

        public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            SendCalls += 1;
            return Task.FromResult(false);
        }
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Smtp transport unavailable");
    }

    private sealed class LongMessageComposer : IEmailTemplateComposer
    {
        public EmailNotificationType NotificationType => EmailNotificationTypes.TrainerInvitation;

        public EmailMessage Compose(string payloadJson)
            => throw new InvalidOperationException("First line\r\nSecond line " + new string('x', 500));
    }

    private sealed class CapturingInfoLogger<T> : ILogger<T>
    {
        public List<string> InformationMessages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel == LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information)
            {
                InformationMessages.Add(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class CapturingWarningLogger<T> : ILogger<T>
    {
        public List<string> WarningMessages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel == LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningMessages.Add(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
