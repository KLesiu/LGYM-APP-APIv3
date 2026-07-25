using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

/// <summary>
/// Uniqueness verification tests for reliability test fixtures.
/// These tests demonstrate that the non-Postgres test fixture still lets
/// the reliability helpers detect duplicate durable records.
/// 
/// This is a critical prerequisite for T12: these tests MUST pass to guarantee
/// that reliability tests can actually verify uniqueness enforcement.
/// </summary>
[TestFixture]
public class ReliabilityUniquenessCheckTests : IntegrationTestBase
{
    /// <summary>
    /// Demonstrates the fixture can detect and assert on CommandEnvelope uniqueness.
    /// Verifies that the database correctly enforces one envelope per correlation ID.
    /// </summary>
    [Test]
    public async Task CommandEnvelopeUniquenessAssertion_CanDetectCorrectCount()
    {
        // Arrange
        var correlationId = Id<CorrelationScope>.New();

        // Create a CommandEnvelope directly in the database
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var envelope = new CommandEnvelope
        {
            CorrelationId = correlationId,
            PayloadJson = "{}",
            CommandTypeFullName = typeof(object).FullName!,
            Status = ActionExecutionStatus.Pending
        };

        db.CommandEnvelopes.Add(envelope);
        await db.SaveChangesAsync();

        // Act & Assert
        // The fixture should correctly count and assert uniqueness
        var count = await CountCommandEnvelopesByCorrelationIdAsync(correlationId);
        count.Should().Be(1, "Fixture should count exactly one envelope for the correlation ID");

        // This should succeed without throwing
        await AssertCommandEnvelopeUniquenessAsync(correlationId);
    }

    /// <summary>
    /// Demonstrates that duplicate CommandEnvelope records can be inserted in the
    /// non-Postgres test provider, and the fixture helper detects the violation.
    /// </summary>
    [Test]
    public async Task CommandEnvelopeUniquenessAssertion_DetectsDuplicates()
    {
        // Arrange
        var correlationId = Id<CorrelationScope>.New();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Insert first envelope
        var envelope1 = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CorrelationId = correlationId,
            PayloadJson = "{}",
            CommandTypeFullName = typeof(object).FullName!,
            Status = ActionExecutionStatus.Pending
        };

        db.CommandEnvelopes.Add(envelope1);
        await db.SaveChangesAsync();

        // Act & Assert
        // Insert a second envelope with the same CorrelationId so the fixture helper
        // can detect the duplicate durable-intent state.
        var envelope2 = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CorrelationId = correlationId,
            PayloadJson = "{}",
            CommandTypeFullName = typeof(object).FullName!,
            Status = ActionExecutionStatus.Pending
        };

        db.CommandEnvelopes.Add(envelope2);
        await db.SaveChangesAsync();

        var count = await CountCommandEnvelopesByCorrelationIdAsync(correlationId);
        count.Should().Be(2, "Fixture should observe both duplicate envelopes before asserting uniqueness");

        var act = () => AssertCommandEnvelopeUniquenessAsync(correlationId).GetAwaiter().GetResult();
        act.Should().Throw<AssertionException>("Fixture uniqueness helper should fail when duplicate CommandEnvelope records exist");
    }

    /// <summary>
    /// Demonstrates the fixture can detect NotificationMessage uniqueness violations.
    /// Uses the (Type, CorrelationId, Recipient) composite key pattern.
    /// </summary>
    [Test]
    public async Task NotificationMessageUniquenessAssertion_CanDetectCorrectCount()
    {
        // Arrange
        var correlationId = Id<CorrelationScope>.New();
        var recipient = new Email("test@example.com");
        var notificationType = EmailNotificationTypes.Welcome;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var notification = new NotificationMessage
        {
            Type = notificationType,
            CorrelationId = correlationId,
            Recipient = recipient,
            PayloadJson = "{}",
            Status = EmailNotificationStatus.Pending,
            Channel = NotificationChannel.Email
        };

        db.NotificationMessages.Add(notification);
        await db.SaveChangesAsync();

        // Act & Assert
        var count = await CountNotificationMessagesByKeyAsync(notificationType, correlationId, recipient);
        count.Should().Be(1, "Fixture should count exactly one notification for the key tuple");

        // This should succeed without throwing
        await AssertNotificationMessageUniquenessAsync(notificationType, correlationId, recipient);
    }

    /// <summary>
    /// Demonstrates that duplicate NotificationMessage records can be inserted in the
    /// non-Postgres test provider, and the fixture helper detects the violation.
    /// </summary>
    [Test]
    public async Task NotificationMessageUniquenessAssertion_DetectsDuplicates()
    {
        // Arrange
        var correlationId = Id<CorrelationScope>.New();
        var recipient = new Email("test@example.com");
        var notificationType = EmailNotificationTypes.Welcome;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Insert first notification
        var notification1 = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Type = notificationType,
            CorrelationId = correlationId,
            Recipient = recipient,
            PayloadJson = "{}",
            Status = EmailNotificationStatus.Pending,
            Channel = NotificationChannel.Email
        };

        db.NotificationMessages.Add(notification1);
        await db.SaveChangesAsync();

        // Act & Assert
        // Insert a second notification with the same key tuple so the fixture helper
        // can detect the duplicate notification state.
        var notification2 = new NotificationMessage
        {
            Id = Id<NotificationMessage>.New(),
            Type = notificationType,
            CorrelationId = correlationId,
            Recipient = recipient,
            PayloadJson = "{}",
            Status = EmailNotificationStatus.Pending,
            Channel = NotificationChannel.Email
        };

        db.NotificationMessages.Add(notification2);
        await db.SaveChangesAsync();

        var count = await CountNotificationMessagesByKeyAsync(notificationType, correlationId, recipient);
        count.Should().Be(2, "Fixture should observe both duplicate notifications before asserting uniqueness");

        var act = () => AssertNotificationMessageUniquenessAsync(notificationType, correlationId, recipient).GetAwaiter().GetResult();
        act.Should().Throw<AssertionException>("Fixture uniqueness helper should fail when duplicate NotificationMessage records exist");
    }

    /// <summary>
    /// Demonstrates baseline state tracking for transaction isolation.
    /// Verifies that counts before and after an action can be compared.
    /// </summary>
    [Test]
    public async Task TransactionIsolationTracking_BaselineCounts_CanBeCompared()
    {
        // Arrange
        var baselineEnvelopes = await CountAllCommandEnvelopesAsync();
        var baselineNotifications = await CountAllNotificationMessagesAsync();

        // Act
        var correlationId = Id<CorrelationScope>.New();
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var envelope = new CommandEnvelope
        {
            CorrelationId = correlationId,
            PayloadJson = "{}",
            CommandTypeFullName = typeof(object).FullName!,
            Status = ActionExecutionStatus.Pending
        };
        db.CommandEnvelopes.Add(envelope);
        await db.SaveChangesAsync();

        // Assert
        var afterEnvelopes = await CountAllCommandEnvelopesAsync();
        afterEnvelopes.Should().Be(baselineEnvelopes + 1, "Envelope count should increment by 1");

        // Notification count should remain unchanged
        var afterNotifications = await CountAllNotificationMessagesAsync();
        afterNotifications.Should().Be(baselineNotifications, "Notification count should remain unchanged");
    }

    /// <summary>
    /// Demonstrates state history retrieval for crash-window scenarios.
    /// Verifies that full state sequences can be inspected.
    /// </summary>
    [Test]
    public async Task StateHistoryRetrieval_CanInspectAllStatuses()
    {
        // Arrange
        var correlationId = Id<CorrelationScope>.New();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var envelope = new CommandEnvelope
        {
            CorrelationId = correlationId,
            PayloadJson = "{}",
            CommandTypeFullName = typeof(object).FullName!,
            Status = ActionExecutionStatus.Pending
        };
        db.CommandEnvelopes.Add(envelope);
        await db.SaveChangesAsync();

        // Act
        var statuses = await GetCommandEnvelopeStatusesAsync(correlationId);

        // Assert
        statuses.Should().HaveCount(1, "Should retrieve exactly one status record");
        statuses[0].Item2.Should().Be(ActionExecutionStatus.Pending, "Status should be Pending");
    }
}
