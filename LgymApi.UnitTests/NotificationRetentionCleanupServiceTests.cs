using FluentAssertions;
using LgymApi.Application.Notifications;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class NotificationRetentionCleanupServiceTests
{
    [Test]
    public async Task CleanupAsync_WhenPushMessageCandidatesAreEmpty_LogsSanitizedStartAndSuccessWithOperationalFields()
    {
        await using var database = CreateDatabase();
        var repository = new PushNotificationMessageRepository(database);
        var unitOfWork = new RecordingUnitOfWork(database);
        var logger = new CapturingLogger<PushNotificationMessageRetentionCleanupService>();
        var service = new PushNotificationMessageRetentionCleanupService(repository, unitOfWork, new RetentionSettings(), logger);

        var removed = await service.CleanupAsync();

        removed.Should().Be(0);
        unitOfWork.SaveChangesCalls.Should().Be(0);
        logger.Entries.Should().HaveCount(2);
        var start = logger.Entries.Should().ContainSingle(entry => entry.Message.Contains("started", StringComparison.Ordinal)).Subject;
        start.Properties.Keys.Should().Contain(["Operation", "CutoffUtc", "BatchSize"]);
        var success = logger.Entries.Should().ContainSingle(entry => entry.Message.Contains("completed", StringComparison.Ordinal)).Subject;
        success.Properties.Keys.Should().Contain(["Operation", "CutoffUtc", "DeletedCount", "BatchCount", "Duration"]);
        success.Properties["DeletedCount"].Should().Be(0);
        success.Properties["BatchCount"].Should().Be(0);
    }

    [Test]
    public async Task CleanupAsync_WhenPushMessageCandidatesFillMultipleBatches_CommitsEachBatchAndDoesNotLogPayload()
    {
        await using var database = CreateDatabase();
        database.PushNotificationMessages.AddRange(CreateMessage("retention-secret-payload"), CreateMessage("retention-secret-payload"));
        await database.SaveChangesAsync();
        var repository = new PushNotificationMessageRepository(database);
        var unitOfWork = new RecordingUnitOfWork(database);
        var logger = new CapturingLogger<PushNotificationMessageRetentionCleanupService>();
        var service = new PushNotificationMessageRetentionCleanupService(repository, unitOfWork, new RetentionSettings(BatchSize: 1), logger);

        var removed = await service.CleanupAsync();

        removed.Should().Be(2);
        unitOfWork.SaveChangesCalls.Should().Be(2);
        (await database.PushNotificationMessages.AsNoTracking().CountAsync()).Should().Be(0);
        logger.Entries.Should().HaveCount(2);
        logger.Entries.Select(entry => entry.Message).Should().NotContain(message => message.Contains("retention-secret-payload", StringComparison.Ordinal));
        var success = logger.Entries.Should().ContainSingle(entry => entry.Message.Contains("completed", StringComparison.Ordinal)).Subject;
        success.Properties["DeletedCount"].Should().Be(2);
        success.Properties["BatchCount"].Should().Be(2);
    }

    [Test]
    public async Task CleanupAsync_WhenDisabledInstallationCleanupIsRerun_IsIdempotentAndDoesNotLogToken()
    {
        await using var database = CreateDatabase();
        database.PushInstallations.Add(CreateInstallation("retention-secret-token"));
        await database.SaveChangesAsync();
        var repository = new PushInstallationRepository(database);
        var unitOfWork = new RecordingUnitOfWork(database);
        var logger = new CapturingLogger<DisabledPushInstallationRetentionCleanupService>();
        var service = new DisabledPushInstallationRetentionCleanupService(repository, unitOfWork, new RetentionSettings(), logger);

        var firstRunRemoved = await service.CleanupAsync();
        var secondRunRemoved = await service.CleanupAsync();

        firstRunRemoved.Should().Be(1);
        secondRunRemoved.Should().Be(0);
        unitOfWork.SaveChangesCalls.Should().Be(1);
        (await database.PushInstallations.AsNoTracking().CountAsync()).Should().Be(0);
        logger.Entries.Should().HaveCount(4);
        logger.Entries.Select(entry => entry.Message).Should().NotContain(message => message.Contains("retention-secret-token", StringComparison.Ordinal));
    }

    [Test]
    public async Task CleanupAsync_WhenInAppNotificationBatchCommitFails_PropagatesFailureWithoutContinuing()
    {
        await using var database = CreateDatabase();
        database.InAppNotifications.Add(CreateNotification());
        await database.SaveChangesAsync();
        var repository = new InAppNotificationRepository(database);
        var unitOfWork = new RecordingUnitOfWork(database, new InvalidOperationException("retention commit failed"));
        var logger = new CapturingLogger<InAppNotificationRetentionCleanupService>();
        var service = new InAppNotificationRetentionCleanupService(repository, unitOfWork, new RetentionSettings(), logger);

        var action = async () => await service.CleanupAsync();

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("retention commit failed");
        unitOfWork.SaveChangesCalls.Should().Be(1);
        (await database.InAppNotifications.AsNoTracking().CountAsync()).Should().Be(1);
        logger.Entries.Should().HaveCount(2);
        var failure = logger.Entries.Should().ContainSingle(entry => entry.LogLevel == LogLevel.Error).Subject;
        failure.Exception.Should().BeOfType<InvalidOperationException>();
        failure.Properties.Keys.Should().Contain(["Operation", "CutoffUtc", "DeletedCount", "BatchCount", "Duration"]);
        failure.Properties["DeletedCount"].Should().Be(0);
        failure.Properties["BatchCount"].Should().Be(0);
        failure.Message.Should().NotContain("retention-secret-message");
    }

    private static AppDbContext CreateDatabase()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"retention-cleanup-{Id<NotificationRetentionCleanupServiceTests>.New():N}")
            .Options);

    private static PushNotificationMessage CreateMessage(string payload)
        => new()
        {
            Id = Id<PushNotificationMessage>.New(),
            UserId = Id<User>.New(),
            PushInstallationId = Id<PushInstallation>.New(),
            SchemaVersion = 1,
            Type = "retention.test",
            EventId = "retention-event",
            PayloadJson = payload,
            Status = PushNotificationStatus.Sent,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-31)
        };

    private static PushInstallation CreateInstallation(string token)
        => new()
        {
            Id = Id<PushInstallation>.New(),
            InstallationId = "retention-installation",
            Platform = "android",
            FcmToken = token,
            Environment = "test",
            DisabledAt = DateTimeOffset.UtcNow.AddDays(-31),
            DisabledReason = "InactiveStale"
        };

    private static InAppNotification CreateNotification()
        => new()
        {
            Id = Id<InAppNotification>.New(),
            RecipientId = Id<User>.New(),
            Type = InAppNotificationTypes.InvitationSent,
            DeliveryKey = "retention-key",
            Message = "retention-secret-message",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-91)
        };

    private sealed record RetentionSettings(int BatchSize = 50) : INotificationRetentionSettings
    {
        public int MessageHistoryDays => 30;
        public int DisabledInstallationDays => 30;
        public int InAppNotificationDays => 90;
    }

    private sealed class RecordingUnitOfWork(AppDbContext database, Exception? saveException = null) : IUnitOfWork
    {
        public int SaveChangesCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return saveException is null
                ? database.SaveChangesAsync(cancellationToken)
                : Task.FromException<int>(saveException);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromException<IUnitOfWorkTransaction>(new NotSupportedException());

        public void DetachEntity<TEntity>(TEntity entity)
            where TEntity : class
            => database.Entry(entity).State = EntityState.Detached;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                exception,
                state is IEnumerable<KeyValuePair<string, object?>> properties
                    ? properties.ToDictionary(property => property.Key, property => property.Value)
                    : []));
        }
    }

    private sealed record LogEntry(
        LogLevel LogLevel,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Properties);
}
