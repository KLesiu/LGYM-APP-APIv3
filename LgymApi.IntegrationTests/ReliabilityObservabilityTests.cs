using FluentAssertions;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LgymApi.IntegrationTests;

/// <summary>
/// T13 Observability Tests - Query and Visibility for Durable Intents
/// Tests observability methods for CommandEnvelope, NotificationMessage, and ApiIdempotencyRecord.
/// </summary>
[TestFixture]
public class ReliabilityObservabilityTests : IntegrationTestBase
{
    private ICommandEnvelopeRepository _commandEnvelopeRepository = null!;
    private IEmailNotificationLogRepository _notificationRepository = null!;
    private IApiIdempotencyRecordRepository _idempotencyRepository = null!;

    [SetUp]
    public void TestSetUp()
    {
        var scope = Factory.Services.CreateScope();
        _commandEnvelopeRepository = scope.ServiceProvider.GetRequiredService<ICommandEnvelopeRepository>();
        _notificationRepository = scope.ServiceProvider.GetRequiredService<IEmailNotificationLogRepository>();
        _idempotencyRepository = scope.ServiceProvider.GetRequiredService<IApiIdempotencyRecordRepository>();
    }

    [Test]
    public async Task GetPendingUndispatchedAsync_CommandEnvelope_WithPendingEnvelopes_ReturnsOnlyPendingUndispatched()
    {
        // Arrange: Create mixed command envelopes
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pendingUndispatched = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            Status = ActionExecutionStatus.Pending,
            DispatchedAt = null  // Key: not dispatched
        };

        var pendingDispatched = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            Status = ActionExecutionStatus.Pending,
            DispatchedAt = DateTimeOffset.UtcNow  // Dispatched but still Pending
        };

        var completed = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            Status = ActionExecutionStatus.Completed,
            DispatchedAt = null
        };

        var failed = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            Status = ActionExecutionStatus.Failed,
            DispatchedAt = null
        };

        await db.CommandEnvelopes.AddAsync(pendingUndispatched);
        await db.CommandEnvelopes.AddAsync(pendingDispatched);
        await db.CommandEnvelopes.AddAsync(completed);
        await db.CommandEnvelopes.AddAsync(failed);
        await db.SaveChangesAsync();

        // Act
        var results = await _commandEnvelopeRepository.GetPendingUndispatchedAsync();

        // Assert
        results.Should().HaveCount(1);
        results.First().Id.Should().Be(pendingUndispatched.Id);
        results.First().Status.Should().Be(ActionExecutionStatus.Pending);
        results.First().DispatchedAt.Should().BeNull();
    }

    [Test]
    public async Task GetFailedAsync_CommandEnvelope_WithFailedEnvelopes_ReturnsAllFailed()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var failed1 = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            Status = ActionExecutionStatus.Failed,
            LastAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        var failed2 = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.TrainingCompletedCommand",
            Status = ActionExecutionStatus.Failed,
            LastAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        var completed = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            Status = ActionExecutionStatus.Completed
        };

        await db.CommandEnvelopes.AddAsync(failed1);
        await db.CommandEnvelopes.AddAsync(failed2);
        await db.CommandEnvelopes.AddAsync(completed);
        await db.SaveChangesAsync();

        // Act
        var results = await _commandEnvelopeRepository.GetFailedAsync();

        // Assert
        results.Should().HaveCount(2);
        results.Should().AllSatisfy(e => e.Status.Should().Be(ActionExecutionStatus.Failed));
        results.Should().Contain(e => e.Id == failed1.Id);
        results.Should().Contain(e => e.Id == failed2.Id);
    }

    [Test]
    public async Task GetDeadLetteredAsync_CommandEnvelope_WithDeadLetteredEnvelopes_ReturnsOnlyDeadLettered()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var deadLettered = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            Status = ActionExecutionStatus.DeadLettered,
            LastAttemptAt = DateTimeOffset.UtcNow
        };

        var failed = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            Status = ActionExecutionStatus.Failed
        };

        await db.CommandEnvelopes.AddAsync(deadLettered);
        await db.CommandEnvelopes.AddAsync(failed);
        await db.SaveChangesAsync();

        // Act
        var results = await _commandEnvelopeRepository.GetDeadLetteredAsync();

        // Assert
        results.Should().HaveCount(1);
        results.First().Id.Should().Be(deadLettered.Id);
        results.First().Status.Should().Be(ActionExecutionStatus.DeadLettered);
    }

    [Test]
    public async Task CountByStatusAsync_CommandEnvelope_WithMixedStatuses_ReturnsCorrectCounts()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Create 3 Pending, 2 Failed, 1 DeadLettered, 2 Completed
        for (int i = 0; i < 3; i++)
        {
            await db.CommandEnvelopes.AddAsync(new CommandEnvelope
            {
                Id = Id<CommandEnvelope>.New(),
                CorrelationId = Id<CorrelationScope>.New(),
                PayloadJson = "{}",
                CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
                Status = ActionExecutionStatus.Pending
            });
        }

        for (int i = 0; i < 2; i++)
        {
            await db.CommandEnvelopes.AddAsync(new CommandEnvelope
            {
                Id = Id<CommandEnvelope>.New(),
                CorrelationId = Id<CorrelationScope>.New(),
                PayloadJson = "{}",
                CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
                Status = ActionExecutionStatus.Failed
            });
        }

        await db.CommandEnvelopes.AddAsync(new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CorrelationId = Id<CorrelationScope>.New(),
            PayloadJson = "{}",
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            Status = ActionExecutionStatus.DeadLettered
        });

        for (int i = 0; i < 2; i++)
        {
            await db.CommandEnvelopes.AddAsync(new CommandEnvelope
            {
                Id = Id<CommandEnvelope>.New(),
                CorrelationId = Id<CorrelationScope>.New(),
                PayloadJson = "{}",
                CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
                Status = ActionExecutionStatus.Completed
            });
        }

        await db.SaveChangesAsync();

        // Act & Assert
        var pendingCount = await _commandEnvelopeRepository.CountByStatusAsync(ActionExecutionStatus.Pending);
        var failedCount = await _commandEnvelopeRepository.CountByStatusAsync(ActionExecutionStatus.Failed);
        var deadLetteredCount = await _commandEnvelopeRepository.CountByStatusAsync(ActionExecutionStatus.DeadLettered);
        var completedCount = await _commandEnvelopeRepository.CountByStatusAsync(ActionExecutionStatus.Completed);

        pendingCount.Should().Be(3);
        failedCount.Should().Be(2);
        deadLetteredCount.Should().Be(1);
        completedCount.Should().Be(2);
    }

    [Test]
    public async Task GetPendingUndispatchedAsync_NotificationMessage_WithPendingNotifications_ReturnsOnlyUndispatched()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var correlationId = Id<CorrelationScope>.New();

        var pendingUndispatched = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Channel = NotificationChannel.Email,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = correlationId,
            Recipient = new Email("user@example.com"),
            PayloadJson = "{}",
            Status = EmailNotificationStatus.Pending,
            DispatchedAt = null
        };

        var pendingDispatched = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Channel = NotificationChannel.Email,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = new Email("other@example.com"),
            PayloadJson = "{}",
            Status = EmailNotificationStatus.Pending,
            DispatchedAt = DateTimeOffset.UtcNow
        };

        var sent = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Channel = NotificationChannel.Email,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = new Email("sent@example.com"),
            PayloadJson = "{}",
            Status = EmailNotificationStatus.Sent
        };

        await db.NotificationMessages.AddAsync(pendingUndispatched);
        await db.NotificationMessages.AddAsync(pendingDispatched);
        await db.NotificationMessages.AddAsync(sent);
        await db.SaveChangesAsync();

        // Act
        var results = await _notificationRepository.GetPendingUndispatchedAsync();

        // Assert
        results.Should().HaveCount(1);
        results.First().Id.Should().Be(pendingUndispatched.Id);
        results.First().Status.Should().Be(EmailNotificationStatus.Pending);
        results.First().DispatchedAt.Should().BeNull();
    }

    [Test]
    public async Task GetDeadLetteredAsync_NotificationMessage_WithDeadLetteredNotifications_ReturnsOnlyDeadLettered()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var deadLettered = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Channel = NotificationChannel.Email,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = new Email("dead@example.com"),
            PayloadJson = "{}",
            Status = EmailNotificationStatus.Failed,
            IsDeadLettered = true,
            DeadLetterReason = "Invalid recipient"
        };

        var failed = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Channel = NotificationChannel.Email,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = new Email("retry@example.com"),
            PayloadJson = "{}",
            Status = EmailNotificationStatus.Failed,
            IsDeadLettered = false
        };

        await db.NotificationMessages.AddAsync(deadLettered);
        await db.NotificationMessages.AddAsync(failed);
        await db.SaveChangesAsync();

        // Act
        var results = await _notificationRepository.GetDeadLetteredAsync();

        // Assert
        results.Should().HaveCount(1);
        results.First().Id.Should().Be(deadLettered.Id);
        results.First().IsDeadLettered.Should().BeTrue();
    }

    [Test]
    public async Task CountInProgressAsync_ApiIdempotencyRecord_WithInProgressRecords_ReturnsCorrectCount()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // In-progress: ResponseStatusCode < 100 (typically 0)
        var inProgress1 = new ApiIdempotencyRecord
        {
            Id = Id<ApiIdempotencyRecord>.New(),
            ScopeTuple = "POST|/api/register|user@example.com",
            IdempotencyKey = "key-1",
            RequestFingerprint = "fingerprint-1",
            ResponseStatusCode = 0  // In-progress marker
        };

        var inProgress2 = new ApiIdempotencyRecord
        {
            Id = Id<ApiIdempotencyRecord>.New(),
            ScopeTuple = "POST|/api/training|user-id",
            IdempotencyKey = "key-2",
            RequestFingerprint = "fingerprint-2",
            ResponseStatusCode = 0  // In-progress marker
        };

        var completed = new ApiIdempotencyRecord
        {
            Id = Id<ApiIdempotencyRecord>.New(),
            ScopeTuple = "POST|/api/register|other@example.com",
            IdempotencyKey = "key-3",
            RequestFingerprint = "fingerprint-3",
            ResponseStatusCode = 200  // Completed
        };

        await db.ApiIdempotencyRecords.AddAsync(inProgress1);
        await db.ApiIdempotencyRecords.AddAsync(inProgress2);
        await db.ApiIdempotencyRecords.AddAsync(completed);
        await db.SaveChangesAsync();

        // Act
        var inProgressCount = await _idempotencyRepository.CountInProgressAsync();

        // Assert
        inProgressCount.Should().Be(2);
    }

    [Test]
    public async Task CountByStatusCodeAsync_ApiIdempotencyRecord_WithMixedStatusCodes_ReturnsCorrectCounts()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        for (int i = 0; i < 5; i++)
        {
            await db.ApiIdempotencyRecords.AddAsync(new ApiIdempotencyRecord
            {
                Id = Id<ApiIdempotencyRecord>.New(),
                ScopeTuple = "POST|/api/register|user@example.com",
                IdempotencyKey = $"key-200-{i}",
                RequestFingerprint = $"fp-{i}",
                ResponseStatusCode = 200
            });
        }

        for (int i = 0; i < 3; i++)
        {
            await db.ApiIdempotencyRecords.AddAsync(new ApiIdempotencyRecord
            {
                Id = Id<ApiIdempotencyRecord>.New(),
                ScopeTuple = "POST|/api/register|other@example.com",
                IdempotencyKey = $"key-409-{i}",
                RequestFingerprint = $"fp-409-{i}",
                ResponseStatusCode = 409
            });
        }

        for (int i = 0; i < 2; i++)
        {
            await db.ApiIdempotencyRecords.AddAsync(new ApiIdempotencyRecord
            {
                Id = Id<ApiIdempotencyRecord>.New(),
                ScopeTuple = "POST|/api/register|third@example.com",
                IdempotencyKey = $"key-400-{i}",
                RequestFingerprint = $"fp-400-{i}",
                ResponseStatusCode = 400
            });
        }

        await db.SaveChangesAsync();

        // Act & Assert
        var count200 = await _idempotencyRepository.CountByStatusCodeAsync(200);
        var count409 = await _idempotencyRepository.CountByStatusCodeAsync(409);
        var count400 = await _idempotencyRepository.CountByStatusCodeAsync(400);

        count200.Should().Be(5);
        count409.Should().Be(3);
        count400.Should().Be(2);
    }

    [Test]
    public async Task DeleteCompletedOlderThanAsync_CommandEnvelope_WithMixedAges_DeletesOnlyOldCompleted()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoffTime = DateTimeOffset.UtcNow.AddDays(-7);

        // Old completed (should be deleted)
        var oldCompleted1 = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            Status = ActionExecutionStatus.Completed,
            CompletedAt = cutoffTime.AddDays(-1)
        };

        var oldCompleted2 = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.TrainingCompletedCommand",
            Status = ActionExecutionStatus.Completed,
            CompletedAt = cutoffTime.AddDays(-10)
        };

        // Recent completed (should NOT be deleted)
        var recentCompleted = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            Status = ActionExecutionStatus.Completed,
            CompletedAt = cutoffTime.AddDays(1)
        };

        // Old but not completed (should NOT be deleted)
        var oldFailed = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            Status = ActionExecutionStatus.Failed,
            CompletedAt = cutoffTime.AddDays(-5)
        };

        var oldPending = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            Status = ActionExecutionStatus.Pending,
            CompletedAt = null
        };

        await db.CommandEnvelopes.AddAsync(oldCompleted1);
        await db.CommandEnvelopes.AddAsync(oldCompleted2);
        await db.CommandEnvelopes.AddAsync(recentCompleted);
        await db.CommandEnvelopes.AddAsync(oldFailed);
        await db.CommandEnvelopes.AddAsync(oldPending);
        await db.SaveChangesAsync();

        // Act
        using var actScope = Factory.Services.CreateScope();
        var unitOfWork = actScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = actScope.ServiceProvider.GetRequiredService<ICommandEnvelopeRepository>();
        var deleteCount = await repository.DeleteCompletedOlderThanAsync(cutoffTime);
        await unitOfWork.SaveChangesAsync();

        // Assert
        deleteCount.Should().Be(2, "two old completed envelopes should be deleted");
        // Refresh context to see persisted changes
        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var remaining = await verifyDb.CommandEnvelopes.ToListAsync();
        remaining.Should().HaveCount(3, "only non-old-completed should remain");
        remaining.Should().Contain(e => e.Id == recentCompleted.Id);
        remaining.Should().Contain(e => e.Id == oldFailed.Id);
        remaining.Should().Contain(e => e.Id == oldPending.Id);
        remaining.Should().NotContain(e => e.Id == oldCompleted1.Id);
        remaining.Should().NotContain(e => e.Id == oldCompleted2.Id);
    }

    [Test]
    public async Task DeleteSentOlderThanAsync_NotificationMessage_WithMixedAges_DeletesOnlyOldSent()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoffTime = DateTimeOffset.UtcNow.AddDays(-30);

        // Old sent (should be deleted)
        var oldSent1 = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Channel = NotificationChannel.Email,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = new Email("old1@example.com"),
            PayloadJson = "{}",
            Status = EmailNotificationStatus.Sent,
            SentAt = cutoffTime.AddDays(-5)
        };

        var oldSent2 = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Channel = NotificationChannel.Email,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = new Email("old2@example.com"),
            PayloadJson = "{}",
            Status = EmailNotificationStatus.Sent,
            SentAt = cutoffTime.AddDays(-45)
        };

        // Recent sent (should NOT be deleted)
        var recentSent = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Channel = NotificationChannel.Email,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = new Email("recent@example.com"),
            PayloadJson = "{}",
            Status = EmailNotificationStatus.Sent,
            SentAt = cutoffTime.AddDays(10)
        };

        // Old but not sent (should NOT be deleted)
        var oldPending = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Channel = NotificationChannel.Email,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = new Email("pending@example.com"),
            PayloadJson = "{}",
            Status = EmailNotificationStatus.Pending,
            SentAt = null
        };

        var oldFailed = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Channel = NotificationChannel.Email,
            Type = EmailNotificationTypes.Welcome,
            CorrelationId = Id<CorrelationScope>.New(),
            Recipient = new Email("failed@example.com"),
            PayloadJson = "{}",
            Status = EmailNotificationStatus.Failed,
            SentAt = null
        };

        await db.NotificationMessages.AddAsync(oldSent1);
        await db.NotificationMessages.AddAsync(oldSent2);
        await db.NotificationMessages.AddAsync(recentSent);
        await db.NotificationMessages.AddAsync(oldPending);
        await db.NotificationMessages.AddAsync(oldFailed);
        await db.SaveChangesAsync();

        // Act
        using var actScope = Factory.Services.CreateScope();
        var unitOfWork = actScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = actScope.ServiceProvider.GetRequiredService<IEmailNotificationLogRepository>();
        var deleteCount = await repository.DeleteSentOlderThanAsync(cutoffTime);
        await unitOfWork.SaveChangesAsync();

        // Assert
        deleteCount.Should().Be(2, "two old sent notifications should be deleted");
        // Refresh context to see persisted changes
        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var remaining = await verifyDb.NotificationMessages.ToListAsync();
        remaining.Should().HaveCount(3, "only non-old-sent should remain");
        remaining.Should().Contain(e => e.Id == recentSent.Id);
        remaining.Should().Contain(e => e.Id == oldPending.Id);
        remaining.Should().Contain(e => e.Id == oldFailed.Id);
        remaining.Should().NotContain(e => e.Id == oldSent1.Id);
        remaining.Should().NotContain(e => e.Id == oldSent2.Id);
    }

    [Test]
    public async Task DeleteOlderThanAsync_ApiIdempotencyRecord_WithMixedAges_DeletesOnlyOldRecords()
    {
        // Arrange
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoffTime = DateTimeOffset.UtcNow.AddDays(-14);

        // Old records (should be deleted) - includes both completed and in-progress
        var oldCompleted = new ApiIdempotencyRecord
        {
            Id = Id<ApiIdempotencyRecord>.New(),
            ScopeTuple = "POST|/api/register|user1@example.com",
            IdempotencyKey = "key-old-1",
            RequestFingerprint = "fp-old-1",
            ResponseStatusCode = 200,
            ProcessedAt = cutoffTime.AddDays(-5)
        };

        var oldFailed = new ApiIdempotencyRecord
        {
            Id = Id<ApiIdempotencyRecord>.New(),
            ScopeTuple = "POST|/api/training|user2",
            IdempotencyKey = "key-old-2",
            RequestFingerprint = "fp-old-2",
            ResponseStatusCode = 500,
            ProcessedAt = cutoffTime.AddDays(-20)
        };

        // Recent records (should NOT be deleted) - both completed and in-progress
        var recentCompleted = new ApiIdempotencyRecord
        {
            Id = Id<ApiIdempotencyRecord>.New(),
            ScopeTuple = "POST|/api/register|user3@example.com",
            IdempotencyKey = "key-recent-completed",
            RequestFingerprint = "fp-recent-completed",
            ResponseStatusCode = 201,
            ProcessedAt = cutoffTime.AddDays(5)
        };

        var recentInProgress = new ApiIdempotencyRecord
        {
            Id = Id<ApiIdempotencyRecord>.New(),
            ScopeTuple = "POST|/api/register|user4@example.com",
            IdempotencyKey = "key-recent-progress",
            RequestFingerprint = "fp-recent-progress",
            ResponseStatusCode = 0,  // In-progress marker
            ProcessedAt = cutoffTime.AddDays(2)
        };

        await db.ApiIdempotencyRecords.AddAsync(oldCompleted);
        await db.ApiIdempotencyRecords.AddAsync(oldFailed);
        await db.ApiIdempotencyRecords.AddAsync(recentCompleted);
        await db.ApiIdempotencyRecords.AddAsync(recentInProgress);
        await db.SaveChangesAsync();

        // Act
        using var actScope = Factory.Services.CreateScope();
        var unitOfWork = actScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repository = actScope.ServiceProvider.GetRequiredService<IApiIdempotencyRecordRepository>();
        var deleteCount = await repository.DeleteOlderThanAsync(cutoffTime);
        await unitOfWork.SaveChangesAsync();

        // Assert
        deleteCount.Should().Be(2, "two old idempotency records should be deleted");
        // Refresh context to see persisted changes
        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var remaining = await verifyDb.ApiIdempotencyRecords.ToListAsync();
        remaining.Should().HaveCount(2, "only non-old records should remain");
        remaining.Should().Contain(e => e.Id == recentCompleted.Id);
        remaining.Should().Contain(e => e.Id == recentInProgress.Id);
        remaining.Should().NotContain(e => e.Id == oldCompleted.Id);
        remaining.Should().NotContain(e => e.Id == oldFailed.Id);
    }
}
