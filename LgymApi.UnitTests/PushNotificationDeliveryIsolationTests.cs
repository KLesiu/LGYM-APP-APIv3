using FluentAssertions;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Notifications.Providers.Fcm;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.UnitTests.Fakes;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PushNotificationDeliveryIsolationTests
{
    [Test]
    public async Task ProcessAsync_WhenInvalidTokenClearsRetryEligibility_DoesNotScheduleOrLogTheRawToken()
    {
        const string rawToken = "fcm-token-must-not-appear-in-logs";
        var installation = new PushInstallation
        {
            Id = Id<PushInstallation>.New(),
            UserId = Id<User>.New(),
            InstallationId = "installation-invalid-token",
            Platform = "android",
            FcmToken = rawToken,
            Environment = "testing",
            PermissionStatus = "authorized",
            LastSeenAt = DateTimeOffset.UtcNow
        };
        var message = new PushNotificationMessage
        {
            Id = Id<PushNotificationMessage>.New(),
            UserId = installation.UserId!.Value,
            PushInstallationId = installation.Id,
            SchemaVersion = 1,
            Type = "push.invalid-token",
            EventId = "event-invalid-token",
            PayloadJson = "{\"schemaVersion\":1,\"type\":\"push.invalid-token\",\"eventId\":\"event-invalid-token\"}",
            Status = PushNotificationStatus.Failed,
            FailureKind = PushNotificationFailureKind.Transient,
            NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            SchedulerJobId = "retry-job-to-clear"
        };
        var messageRepository = new TestPushNotificationMessageRepository(message);
        var installationRepository = new ConfigurablePushInstallationRepository
        {
            FindById = (_, _) => Task.FromResult<PushInstallation?>(installation)
        };
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        var sender = new RecordingInvalidTokenPushSender(rawToken);
        var scheduler = new RecordingPushScheduler();
        var logger = new RecordingLogger<PushNotificationDeliveryService>();
        var service = new PushNotificationDeliveryService(
            messageRepository,
            installationRepository,
            sender,
            scheduler,
            new PushNotificationDeliveryRetrySettings(new PushNotificationOptions { RetryDelaysSeconds = [5] }),
            unitOfWork,
            logger);

        await service.ProcessAsync(message.Id.ToString());

        sender.AttemptedInstallationIds.Should().ContainSingle().Which.Should().Be(installation.Id);
        message.Status.Should().Be(PushNotificationStatus.Failed);
        message.FailureKind.Should().Be(PushNotificationFailureKind.InvalidToken);
        message.NextAttemptAt.Should().BeNull();
        message.SchedulerJobId.Should().BeNull();
        installation.DisabledReason.Should().Be("InvalidFcmToken");
        installation.DisabledAt.Should().NotBeNull();
        scheduler.RetryNotificationIds.Should().BeEmpty();
        logger.Messages.Should().NotContain(messageText => messageText.Contains(rawToken, StringComparison.Ordinal));
    }

    private sealed class RecordingInvalidTokenPushSender(string rawToken) : IPushProviderSender
    {
        public List<Id<PushInstallation>> AttemptedInstallationIds { get; } = [];

        public Task<PushSendAttemptResult> SendAsync(
            Id<PushInstallation> installationId,
            PushEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            AttemptedInstallationIds.Add(installationId);
            return Task.FromResult(new PushSendAttemptResult(
                PushSendOutcome.InvalidToken,
                "BadRequest",
                null,
                "UNREGISTERED",
                $"provider rejected {rawToken}"));
        }
    }

    private sealed class TestPushNotificationMessageRepository(PushNotificationMessage message) : IPushNotificationMessageRepository
    {
        public Task AddAsync(PushNotificationMessage notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Detach(PushNotificationMessage notification) { }

        public Task<PushNotificationMessage?> FindByIdAsync(Id<PushNotificationMessage> id, CancellationToken cancellationToken = default)
            => Task.FromResult<PushNotificationMessage?>(message.Id == id ? message : null);

        public Task<PushNotificationMessage?> FindByDeliveryKeyAsync(
            Id<PushInstallation> installationId,
            string type,
            string eventId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<PushNotificationMessage?>(null);

        public Task<bool> TryReserveSchedulingAsync(Id<PushNotificationMessage> id, string reservationId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task ClearSchedulingReservationAsync(Id<PushNotificationMessage> id, string reservationId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> TryTransitionToSendingAsync(Id<PushNotificationMessage> id, CancellationToken cancellationToken = default)
            => Task.FromResult(message.Id == id);

        public Task UpdateAsync(PushNotificationMessage notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<List<PushNotificationMessage>> GetByStatusAsync(PushNotificationStatus status, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<PushNotificationMessage>());
    }

    private sealed class RecordingPushScheduler : IPushBackgroundScheduler
    {
        public List<Id<PushNotificationMessage>> RetryNotificationIds { get; } = [];

        public string? Enqueue(string notificationId) => "push-job";

        public string? ScheduleRetry(string notificationId, TimeSpan delay)
        {
            if (!Id<PushNotificationMessage>.TryParse(notificationId, out var parsedNotificationId))
            {
                throw new FormatException("Notification ID must be valid.");
            }

            RetryNotificationIds.Add(parsedNotificationId);
            return "retry-job";
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

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
            Messages.Add(formatter(state, exception));
        }
    }
}
