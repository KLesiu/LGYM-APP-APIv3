using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LgymApi.Application.Repositories;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Notifications.Email;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class WelcomeEmailJobTests
{
    [Test]
    public async Task ExecuteAsync_DelegatesToHandler()
    {
        var notificationId = Id<NotificationMessage>.New();
        var handler = new FakeEmailJobHandler();
        var job = new WelcomeEmailJob(handler);

        await job.ExecuteAsync(notificationId.ToString());

        handler.Calls.Should().Be(1);
        handler.LastNotificationId.Should().Be(notificationId);
    }

    [Test]
    public async Task ProcessAsync_WhenReservationFails_DoesNotSend()
    {
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Status = EmailNotificationStatus.Sending,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = new Email("trainee@example.com"),
            PayloadJson = "{}"
        };

        var repository = new ReservationFakeRepository(notification);
        var sender = new CountingEmailSender();
        var handler = new EmailJobHandlerService(
            repository,
            new PassThroughComposerFactory(),
            sender,
            new NoopUnitOfWork(),
            new NoopEmailMetrics(),
            NullLogger<EmailJobHandlerService>.Instance);

        await handler.ProcessAsync(notification.Id.ToString());

        sender.SendCalls.Should().Be(0);
        repository.TransitionCalls.Should().Be(1);
    }

    private sealed class FakeEmailJobHandler : IEmailJobHandler
    {
        public int Calls { get; private set; }
        public Id<NotificationMessage> LastNotificationId { get; private set; }

        public Task ProcessAsync(string notificationId, CancellationToken cancellationToken = default)
        {
            Calls += 1;
            if (!Id<NotificationMessage>.TryParse(notificationId, out var parsedNotificationId))
            {
                throw new FormatException("Notification ID must be a valid ID.");
            }

            LastNotificationId = parsedNotificationId;
            return Task.CompletedTask;
        }
    }

    private sealed class ReservationFakeRepository : IEmailNotificationLogRepository
    {
        private readonly NotificationMessage _notification;

        public ReservationFakeRepository(NotificationMessage notification) => _notification = notification;

        public int TransitionCalls { get; private set; }

        public Task<bool> TryTransitionToSendingAsync(Id<NotificationMessage> id, CancellationToken cancellationToken = default)
        {
            TransitionCalls += 1;
            return Task.FromResult(false);
        }

        public Task<NotificationMessage?> FindByIdAsync(Id<NotificationMessage> id, CancellationToken cancellationToken = default)
            => Task.FromResult<NotificationMessage?>(_notification);

        public Task AddAsync(NotificationMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<NotificationMessage?> FindByCorrelationAsync(EmailNotificationType type, Id<CorrelationScope> correlationId, string recipient, CancellationToken cancellationToken = default)
            => Task.FromResult<NotificationMessage?>(null);

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

        public Task<List<NotificationMessage>> GetStuckSendingAsync(int emailSendLeaseSeconds, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<NotificationMessage>());
    }

    private sealed class CountingEmailSender : IEmailSender
    {
        public int SendCalls { get; private set; }

        public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            SendCalls += 1;
            return Task.FromResult(true);
        }
    }

    private sealed class NoopUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IUnitOfWorkTransaction>(new NoopTransaction());
    }

    private sealed class NoopTransaction : IUnitOfWorkTransaction
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class PassThroughComposer : IEmailTemplateComposer
    {
        public EmailNotificationType NotificationType => EmailNotificationTypes.Welcome;

        public EmailMessage Compose(string payloadJson)
            => new() { To = "trainee@example.com", Subject = "Welcome", Body = "Body" };
    }

    private sealed class PassThroughComposerFactory : IEmailTemplateComposerFactory
    {
        public EmailMessage ComposeMessage(EmailNotificationType notificationType, string payloadJson)
            => new PassThroughComposer().Compose(payloadJson);
    }

    private sealed class NoopEmailMetrics : IEmailMetrics
    {
        public void RecordEnqueued(EmailNotificationType notificationType) { }
        public void RecordSent(EmailNotificationType notificationType) { }
        public void RecordFailed(EmailNotificationType notificationType) { }
        public void RecordRetried(EmailNotificationType notificationType) { }
    }
}
