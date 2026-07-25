using FluentAssertions;
using LgymApi.Application.Repositories;
using LgymApi.BackgroundWorker;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

/// <summary>
/// T12 Reliability Tests - Crash Window Recovery
/// Tests committed-intent recovery for intents that were persisted but not yet dispatched to scheduler.
/// </summary>
[TestFixture]
public sealed class ReliabilityCrashWindowTests : IntegrationTestBase
{
    [Test]
    public async Task CommittedCommandEnvelope_NotYetDispatched_RecoveredByDispatcher()
    {
        // Arrange - Manually insert a CommandEnvelope that was committed but not yet dispatched
        // (simulates crash after DB commit but before scheduler enqueue)
        var correlationId = Id<CorrelationScope>.New();
        var envelope = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CorrelationId = correlationId,
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            PayloadJson = "{\"UserId\":\"00000000-0000-0000-0000-000000000001\",\"Email\":\"crash-test@example.com\"}",
            Status = ActionExecutionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            DispatchedAt = null, // NOT YET DISPATCHED (crash window)
            SchedulerJobId = null
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CommandEnvelopes.Add(envelope);
            await db.SaveChangesAsync();
        }

        // Act - Invoke committed-intent dispatcher (recovery job)
        using (var scope = Factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<ICommittedIntentDispatcher>();
            await dispatcher.DispatchCommittedIntentsAsync(CancellationToken.None);
        }

        // Assert - Envelope is now dispatched
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recovered = await db.CommandEnvelopes.FindAsync(envelope.Id);

            recovered.Should().NotBeNull();
            recovered!.DispatchedAt.Should().NotBeNull("recovery job should set DispatchedAt");
            recovered.SchedulerJobId.Should().NotBeNullOrEmpty("recovery job should assign scheduler job ID");
            recovered.Status.Should().Be(ActionExecutionStatus.Pending,
                "envelope should remain Pending until background worker processes it");
        }
    }

    [Test]
    public async Task CommittedNotificationMessage_NotYetDispatched_RecoveredByDispatcher()
    {
        // Arrange - Manually insert a NotificationMessage that was committed but not yet dispatched
        var correlationId = Id<CorrelationScope>.New();
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            CorrelationId = correlationId,
            Channel = NotificationChannel.Email,
            Type = EmailNotificationTypes.Welcome,
            Recipient = "crash-notification@example.com",
            PayloadJson = "{\"RecipientEmail\":\"crash-notification@example.com\",\"UserName\":\"CrashUser\",\"CultureName\":\"en\"}",
            Status = EmailNotificationStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            DispatchedAt = null, // NOT YET DISPATCHED (crash window)
            SchedulerJobId = null,
            Attempts = 0
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.NotificationMessages.Add(notification);
            await db.SaveChangesAsync();
        }

        // Act - Invoke committed-intent dispatcher (recovery job)
        using (var scope = Factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<ICommittedIntentDispatcher>();
            await dispatcher.DispatchCommittedIntentsAsync(CancellationToken.None);
        }

        // Assert - Notification is now dispatched
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recovered = await db.NotificationMessages.FindAsync(notification.Id);

            recovered.Should().NotBeNull();
            recovered!.DispatchedAt.Should().NotBeNull("recovery job should set DispatchedAt");
            recovered.SchedulerJobId.Should().NotBeNullOrEmpty("recovery job should assign scheduler job ID");
            recovered.Status.Should().Be(EmailNotificationStatus.Pending,
                "notification should remain Pending until background worker processes it");
        }
    }

    [Test]
    public async Task StaleSendingNotificationMessage_LeaseExpired_RedispatchedByDispatcher()
    {
        var originalDispatchedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var originalLastAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            CorrelationId = Id<CorrelationScope>.New(),
            Channel = NotificationChannel.Email,
            Type = EmailNotificationTypes.Welcome,
            Recipient = "stale-sending@example.com",
            PayloadJson = "{\"RecipientEmail\":\"stale-sending@example.com\",\"UserName\":\"CrashUser\",\"CultureName\":\"en\"}",
            Status = EmailNotificationStatus.Sending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-15),
            DispatchedAt = originalDispatchedAt,
            SchedulerJobId = "stale-sending-job-id",
            Attempts = 1,
            LastAttemptAt = originalLastAttemptAt,
            DeliveredAt = null
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.NotificationMessages.Add(notification);
            await db.SaveChangesAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<ICommittedIntentDispatcher>();
            await dispatcher.DispatchCommittedIntentsAsync(CancellationToken.None);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recovered = await db.NotificationMessages.FindAsync(notification.Id);

            recovered.Should().NotBeNull();
            recovered!.Status.Should().Be(EmailNotificationStatus.Sending,
                "redispatch should preserve the in-flight Sending state until a worker reclaims and completes it");
            recovered.DispatchedAt.Should().NotBeNull();
            recovered.DispatchedAt.Should().BeAfter(originalDispatchedAt,
                "redispatch should refresh DispatchedAt when a stale Sending notification is re-enqueued");
            recovered.SchedulerJobId.Should().NotBeNullOrEmpty();
            recovered.LastAttemptAt.Should().Be(originalLastAttemptAt,
                "dispatcher should re-enqueue stale Sending notifications without mutating the lease owned by the worker path");
        }
    }

    [Test]
    public async Task FreshSendingNotificationMessage_NotRedispatchedByDispatcher()
    {
        var originalDispatchedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var originalLastAttemptAt = DateTimeOffset.UtcNow;
        var notification = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            CorrelationId = Id<CorrelationScope>.New(),
            Channel = NotificationChannel.Email,
            Type = EmailNotificationTypes.Welcome,
            Recipient = "fresh-sending@example.com",
            PayloadJson = "{\"RecipientEmail\":\"fresh-sending@example.com\",\"UserName\":\"CrashUser\",\"CultureName\":\"en\"}",
            Status = EmailNotificationStatus.Sending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            DispatchedAt = originalDispatchedAt,
            SchedulerJobId = "fresh-sending-job-id",
            Attempts = 1,
            LastAttemptAt = originalLastAttemptAt,
            DeliveredAt = null
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.NotificationMessages.Add(notification);
            await db.SaveChangesAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<ICommittedIntentDispatcher>();
            await dispatcher.DispatchCommittedIntentsAsync(CancellationToken.None);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unchanged = await db.NotificationMessages.FindAsync(notification.Id);

            unchanged.Should().NotBeNull();
            unchanged!.DispatchedAt.Should().Be(originalDispatchedAt,
                "dispatcher should not redispatch a fresh Sending notification with an active lease");
            unchanged.SchedulerJobId.Should().Be("fresh-sending-job-id");
            unchanged.LastAttemptAt.Should().Be(originalLastAttemptAt);
        }
    }

    [Test]
    public async Task AlreadyDispatchedEnvelope_NotRedispatched()
    {
        // Arrange - Create an envelope that was already dispatched
        var correlationId = Id<CorrelationScope>.New();
        var envelope = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CorrelationId = correlationId,
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            PayloadJson = "{\"UserId\":\"00000000-0000-0000-0000-000000000002\",\"Email\":\"already-dispatched@example.com\"}",
            Status = ActionExecutionStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            DispatchedAt = DateTimeOffset.UtcNow.AddMinutes(-4), // ALREADY DISPATCHED
            SchedulerJobId = "existing-job-id-123"
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CommandEnvelopes.Add(envelope);
            await db.SaveChangesAsync();
        }

        var originalDispatchedAt = envelope.DispatchedAt;
        var originalJobId = envelope.SchedulerJobId;

        // Act - Invoke recovery dispatcher
        using (var scope = Factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<ICommittedIntentDispatcher>();
            await dispatcher.DispatchCommittedIntentsAsync(CancellationToken.None);
        }

        // Assert - Envelope is NOT re-dispatched (idempotent recovery)
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unchanged = await db.CommandEnvelopes.FindAsync(envelope.Id);

            unchanged.Should().NotBeNull();
            unchanged!.DispatchedAt.Should().Be(originalDispatchedAt,
                "recovery should skip already-dispatched envelopes");
            unchanged.SchedulerJobId.Should().Be(originalJobId,
                "recovery should not overwrite existing scheduler job ID");
        }
    }

    [Test]
    public async Task ProcessingEnvelope_NotRedispatched()
    {
        // Arrange - Create an envelope that is currently being processed
        var correlationId = Id<CorrelationScope>.New();
        var envelope = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CorrelationId = correlationId,
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            PayloadJson = "{\"UserId\":\"00000000-0000-0000-0000-000000000003\",\"Email\":\"processing@example.com\"}",
            Status = ActionExecutionStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            DispatchedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            SchedulerJobId = "processing-job-id-456"
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CommandEnvelopes.Add(envelope);
            await db.SaveChangesAsync();
        }

        // Act - Invoke recovery dispatcher
        using (var scope = Factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<ICommittedIntentDispatcher>();
            await dispatcher.DispatchCommittedIntentsAsync(CancellationToken.None);
        }

        // Assert - Processing envelope is not re-dispatched (at-least-once protection)
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unchanged = await db.CommandEnvelopes.FindAsync(envelope.Id);

            unchanged.Should().NotBeNull();
            unchanged!.Status.Should().Be(ActionExecutionStatus.Processing,
                "recovery should skip envelopes currently being processed");
        }
    }

    [Test]
    public async Task StaleProcessingEnvelope_LeaseTimeoutExceeded_RemainsProcessingUntilOperatorIntervention()
    {
        var correlationId = Id<CorrelationScope>.New();
        var processingStartedAt = DateTimeOffset.UtcNow.AddHours(-1);
        var envelope = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CorrelationId = correlationId,
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            PayloadJson = "{\"UserId\":\"00000000-0000-0000-0000-000000000005\",\"Email\":\"stale-processing@example.com\"}",
            Status = ActionExecutionStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1).AddMinutes(-5),
            DispatchedAt = DateTimeOffset.UtcNow.AddHours(-1).AddMinutes(-4),
            ProcessingStartedAtUtc = processingStartedAt,
            SchedulerJobId = "stale-processing-job-id"
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CommandEnvelopes.Add(envelope);
            await db.SaveChangesAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<ICommittedIntentDispatcher>();
            await dispatcher.DispatchCommittedIntentsAsync(CancellationToken.None);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unchanged = await db.CommandEnvelopes.FindAsync(envelope.Id);

            unchanged.Should().NotBeNull();
            unchanged!.Status.Should().Be(ActionExecutionStatus.Processing,
                "automatic stale-processing recovery is intentionally disabled until a safe lease-renewal strategy exists");
            unchanged.ProcessingStartedAtUtc.Should().Be(processingStartedAt,
                "dispatcher should not clear an active processing lease based only on age");
            unchanged.DispatchedAt.Should().NotBeNull();
            unchanged.SchedulerJobId.Should().Be("stale-processing-job-id");
        }
    }

    [Test]
    public async Task CompletedEnvelope_NotRedispatched()
    {
        // Arrange - Create an envelope that was already completed
        var correlationId = Id<CorrelationScope>.New();
        var envelope = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CorrelationId = correlationId,
            CommandTypeFullName = "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            PayloadJson = "{\"UserId\":\"00000000-0000-0000-0000-000000000004\",\"Email\":\"completed@example.com\"}",
            Status = ActionExecutionStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            DispatchedAt = DateTimeOffset.UtcNow.AddMinutes(-9),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-8),
            SchedulerJobId = "completed-job-id-789"
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CommandEnvelopes.Add(envelope);
            await db.SaveChangesAsync();
        }

        // Act - Invoke recovery dispatcher
        using (var scope = Factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<ICommittedIntentDispatcher>();
            await dispatcher.DispatchCommittedIntentsAsync(CancellationToken.None);
        }

        // Assert - Completed envelope is not redispatched
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unchanged = await db.CommandEnvelopes.FindAsync(envelope.Id);

            unchanged.Should().NotBeNull();
            unchanged!.Status.Should().Be(ActionExecutionStatus.Completed,
                "recovery should skip already-completed envelopes");
            unchanged.CompletedAt.Should().NotBeNull();
        }
    }

    [Test]
    public async Task DeadLetteredEnvelope_NotRedispatched()
    {
        // Arrange - Create a dead-lettered envelope
        var correlationId = Id<CorrelationScope>.New();
        var envelope = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CorrelationId = correlationId,
            CommandTypeFullName = "LgymApi.BackgroundWorker.Actions.UnknownCommand",
            PayloadJson = "{\"Invalid\":\"Poison Message\"}",
            Status = ActionExecutionStatus.DeadLettered,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            DispatchedAt = DateTimeOffset.UtcNow.AddMinutes(-9),
            SchedulerJobId = "dead-letter-job-id"
        };

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CommandEnvelopes.Add(envelope);
            await db.SaveChangesAsync();
        }

        // Act - Invoke recovery dispatcher
        using (var scope = Factory.Services.CreateScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<ICommittedIntentDispatcher>();
            await dispatcher.DispatchCommittedIntentsAsync(CancellationToken.None);
        }

        // Assert - Dead-lettered envelope is not redispatched
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unchanged = await db.CommandEnvelopes.FindAsync(envelope.Id);

            unchanged.Should().NotBeNull();
            unchanged!.Status.Should().Be(ActionExecutionStatus.DeadLettered,
                "recovery should skip dead-lettered envelopes");
        }
    }
}
