using System;
using System.Collections.Generic;
using System.Linq;
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
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Jobs;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class InvitationEmailJobTests
{
    [Test]
    public async Task ExecuteAsync_DelegatesToHandler()
    {
        var notificationId = Id<NotificationMessage>.New();
        var handler = new FakeEmailJobHandler();
        var job = new InvitationEmailJob(handler);

        await job.ExecuteAsync(notificationId.ToString());

        handler.Calls.Should().Be(1);
        handler.LastNotificationId.Should().Be(notificationId);
    }

    [Test]
    public async Task TryTransitionToSendingAsync_WhenPending_ReturnsTrueAndTransitionsToSending()
    {
        await using var db = CreateDbContext();
        var message = CreateMessage(EmailNotificationStatus.Pending);
        db.NotificationMessages.Add(message);
        await db.SaveChangesAsync();
        var repository = new EmailNotificationLogRepository(db);

        var claimed = await repository.TryTransitionToSendingAsync(message.Id);

        claimed.Should().BeTrue();
        var stored = await db.NotificationMessages.FirstOrDefaultAsync(x => x.Id == message.Id);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(EmailNotificationStatus.Sending);
        stored.LastAttemptAt.Should().NotBeNull();
    }

    [Test]
    public async Task TryTransitionToSendingAsync_WhenSending_ReturnsFalse()
    {
        await using var db = CreateDbContext();
        var message = CreateMessage(EmailNotificationStatus.Sending, DateTimeOffset.UtcNow);
        db.NotificationMessages.Add(message);
        await db.SaveChangesAsync();
        var repository = new EmailNotificationLogRepository(db);

        var claimed = await repository.TryTransitionToSendingAsync(message.Id);

        claimed.Should().BeFalse();
        var stored = await db.NotificationMessages.FirstOrDefaultAsync(x => x.Id == message.Id);
        stored!.Status.Should().Be(EmailNotificationStatus.Sending);
    }

    [Test]
    public async Task TryTransitionToSendingAsync_WhenSendingLeaseMissing_ReturnsTrueAndStampsLease()
    {
        await using var db = CreateDbContext();
        var message = CreateMessage(EmailNotificationStatus.Sending);
        db.NotificationMessages.Add(message);
        await db.SaveChangesAsync();
        var repository = new EmailNotificationLogRepository(db);

        var claimed = await repository.TryTransitionToSendingAsync(message.Id);

        claimed.Should().BeTrue();
        var stored = await db.NotificationMessages.FirstOrDefaultAsync(x => x.Id == message.Id);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(EmailNotificationStatus.Sending);
        stored.LastAttemptAt.Should().NotBeNull();
    }

    [Test]
    public async Task TryTransitionToSendingAsync_WhenSendingLeaseExpired_ReturnsTrueAndRefreshesLease()
    {
        await using var db = CreateDbContext();
        var staleLease = DateTimeOffset.UtcNow.AddMinutes(-5);
        var message = CreateMessage(EmailNotificationStatus.Sending, staleLease);
        db.NotificationMessages.Add(message);
        await db.SaveChangesAsync();
        var repository = new EmailNotificationLogRepository(db, new EmailNotificationLeaseSettings(30));

        var claimed = await repository.TryTransitionToSendingAsync(message.Id);

        claimed.Should().BeTrue();
        var stored = await db.NotificationMessages.FirstOrDefaultAsync(x => x.Id == message.Id);
        stored.Should().NotBeNull();
        stored!.LastAttemptAt.Should().BeAfter(staleLease);
    }

    [Test]
    public async Task TryTransitionToSendingAsync_WhenSent_ReturnsFalse()
    {
        await using var db = CreateDbContext();
        var message = CreateMessage(EmailNotificationStatus.Sent);
        db.NotificationMessages.Add(message);
        await db.SaveChangesAsync();
        var repository = new EmailNotificationLogRepository(db);

        var claimed = await repository.TryTransitionToSendingAsync(message.Id);

        claimed.Should().BeFalse();
    }

    [Test]
    public async Task TryTransitionToSendingAsync_WhenFailedAndNotDeadLettered_ReturnsTrueAndTransitionsToSending()
    {
        await using var db = CreateDbContext();
        var message = CreateMessage(EmailNotificationStatus.Failed);
        db.NotificationMessages.Add(message);
        await db.SaveChangesAsync();
        var repository = new EmailNotificationLogRepository(db);

        var claimed = await repository.TryTransitionToSendingAsync(message.Id);

        claimed.Should().BeTrue();
        var stored = await db.NotificationMessages.FirstOrDefaultAsync(x => x.Id == message.Id);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(EmailNotificationStatus.Sending);
    }

    [Test]
    public async Task TryTransitionToSendingAsync_WhenFailedAndDeadLettered_ReturnsFalse()
    {
        await using var db = CreateDbContext();
        var message = CreateMessage(EmailNotificationStatus.Failed);
        message.IsDeadLettered = true;
        db.NotificationMessages.Add(message);
        await db.SaveChangesAsync();
        var repository = new EmailNotificationLogRepository(db);

        var claimed = await repository.TryTransitionToSendingAsync(message.Id);

        claimed.Should().BeFalse();
    }

    [Test]
    public async Task GetStuckSendingAsync_ReturnsOnlySendingNotificationsOlderThanCutoff()
    {
        await using var db = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var stuck = CreateMessage(EmailNotificationStatus.Sending, now.AddMinutes(-10));
        var fresh = CreateMessage(EmailNotificationStatus.Sending, now);
        var missingLease = CreateMessage(EmailNotificationStatus.Sending);
        var sent = CreateMessage(EmailNotificationStatus.Sent, now.AddMinutes(-10));
        db.NotificationMessages.AddRange(stuck, fresh, missingLease, sent);
        await db.SaveChangesAsync();
        var repository = new EmailNotificationLogRepository(db);

        var result = await repository.GetStuckSendingAsync(emailSendLeaseSeconds: 60);

        result.Select(x => x.Id).Should().BeEquivalentTo(new[] { stuck.Id, missingLease.Id });
    }

    [Test]
    public async Task ProcessAsync_ConcurrentCalls_SendsExactlyOnce()
    {
        var notification = CreateMessage(EmailNotificationStatus.Pending);
        var repository = new ReservationFakeRepository(notification);
        var sender = new CountingEmailSender();
        var handler = new EmailJobHandlerService(
            repository,
            new PassThroughComposerFactory(),
            sender,
            new NoopUnitOfWork(),
            new NoopEmailMetrics(),
            NullLogger<EmailJobHandlerService>.Instance);

        await Task.WhenAll(handler.ProcessAsync(notification.Id.ToString()), handler.ProcessAsync(notification.Id.ToString()));

        sender.SendCalls.Should().Be(1);
    }

    private static AppDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"invitation-email-job-{Id<InvitationEmailJobTests>.New():N}")
            .Options);

    private static NotificationMessage CreateMessage(EmailNotificationStatus status, DateTimeOffset? lastAttemptAt = null)
        => new()
        {
            Id = Id<NotificationMessage>.New(),
            Status = status,
            Type = EmailNotificationTypes.TrainerInvitation,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = new Email("trainee@example.com"),
            PayloadJson = "{}",
            LastAttemptAt = lastAttemptAt
        };

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
        private int _transitionCalls;

        public ReservationFakeRepository(NotificationMessage notification) => _notification = notification;

        public Task<bool> TryTransitionToSendingAsync(Id<NotificationMessage> id, CancellationToken cancellationToken = default)
            => Task.FromResult(Interlocked.Increment(ref _transitionCalls) == 1);

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
        public EmailNotificationType NotificationType => EmailNotificationTypes.TrainerInvitation;

        public EmailMessage Compose(string payloadJson)
            => new() { To = "trainee@example.com", Subject = "Invitation", Body = "Body" };
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
