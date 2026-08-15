using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Notifications.Models;
using LgymApi.Application.Notifications.Providers.Fcm;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Push;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.UnitOfWork;
using LgymApi.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PushNotificationPipelineTests
{
    [Test]
    public void PushNotificationDeliveryService_UsesTheSevenDirectDeliveryCollaborators()
    {
        var constructor = typeof(PushNotificationDeliveryService).GetConstructors().Should().ContainSingle().Which;

        constructor.GetParameters().Select(parameter => parameter.ParameterType).Should().Equal(
            typeof(IPushNotificationMessageRepository),
            typeof(IPushInstallationRepository),
            typeof(IPushProviderSender),
            typeof(IPushBackgroundScheduler),
            typeof(IPushNotificationDeliveryRetrySettings),
            typeof(IUnitOfWork),
            typeof(Microsoft.Extensions.Logging.ILogger<PushNotificationDeliveryService>));
    }

    [Test]
    public void PushEventPayload_InAppNotificationId_IsTypedInternally()
    {
        var property = typeof(PushEventPayload).GetProperty(nameof(PushEventPayload.InAppNotificationId));

        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(Id<InAppNotification>?));
    }

    [Test]
    public void PushEventPayload_InAppNotificationId_PreservesLegacyUuidStringJson()
    {
        var notificationId = Id<InAppNotification>.New();
        var payload = new PushEventPayload(1, "internal.test.push", "event-1", null, notificationId, null);
        var legacyJson = $$"""
            {"schemaVersion":1,"type":"internal.test.push","eventId":"event-1","entityId":null,"inAppNotificationId":"{{notificationId}}","deeplink":null}
            """;

        var json = JsonSerializer.Serialize(payload, SharedSerializationOptions.Current);
        using var document = JsonDocument.Parse(json);
        var roundtrip = JsonSerializer.Deserialize<PushEventPayload>(json, SharedSerializationOptions.Current);
        var legacyPayload = JsonSerializer.Deserialize<PushEventPayload>(legacyJson, SharedSerializationOptions.Current);

        document.RootElement.GetProperty("inAppNotificationId").GetString().Should().Be(notificationId.ToString());
        roundtrip.Should().NotBeNull();
        roundtrip!.InAppNotificationId.Should().Be(notificationId);
        legacyPayload.Should().NotBeNull();
        legacyPayload!.InAppNotificationId.Should().Be(notificationId);
    }

    [Test]
    public async Task EnqueueAsync_WhenRepeated_DoesNotCreateDuplicateRowsOrReschedule()
    {
        await using var db = CreateDbContext("push-enqueue-duplicate");
        var userId = Id<User>.New();
        var installation = CreateInstallation(userId, permissionStatus: "authorized");
        db.PushInstallations.Add(installation);
        await db.SaveChangesAsync();

        var scheduler = new FakePushBackgroundScheduler();
        var service = new PushNotificationService(
            new PushInstallationRepository(db),
            new PushNotificationMessageRepository(db),
            scheduler,
            new EfUnitOfWork(db),
            NullLogger<PushNotificationService>.Instance);

        var input = new EnqueuePushNotificationInput(
            userId,
            1,
            "trainer.note.updated",
            "event-1",
            "entity-1",
            null,
            "/notifications/event-1");

        await service.EnqueueAsync(input);
        await service.EnqueueAsync(input);

        db.PushNotificationMessages.Should().HaveCount(1);
        db.PushNotificationMessages.Single().Status.Should().Be(PushNotificationStatus.Pending);
        scheduler.EnqueuedNotificationIds.Should().HaveCount(1);
    }

    [Test]
    public async Task EnqueueAsync_WhenExistingPendingMessageHasNoSchedulerJob_ReschedulesExistingMessage()
    {
        await using var db = CreateDbContext("push-enqueue-reschedule-existing");
        var userId = Id<User>.New();
        var installation = CreateInstallation(userId, permissionStatus: "authorized");
        var existing = new PushNotificationMessage
        {
            Id = Id<PushNotificationMessage>.New(),
            UserId = userId,
            PushInstallationId = installation.Id,
            SchemaVersion = 1,
            Type = "trainer.note.updated",
            EventId = "event-1",
            PayloadJson = "{}",
            Status = PushNotificationStatus.Pending,
            SchedulerJobId = null
        };
        db.PushInstallations.Add(installation);
        db.PushNotificationMessages.Add(existing);
        await db.SaveChangesAsync();

        var scheduler = new FakePushBackgroundScheduler();
        var service = new PushNotificationService(
            new PushInstallationRepository(db),
            new PushNotificationMessageRepository(db),
            scheduler,
            new EfUnitOfWork(db),
            NullLogger<PushNotificationService>.Instance);

        await service.EnqueueAsync(new EnqueuePushNotificationInput(
            userId,
            1,
            "trainer.note.updated",
            "event-1",
            "entity-1",
            null,
            "/notifications/event-1"));

        db.PushNotificationMessages.Should().HaveCount(1);
        scheduler.EnqueuedNotificationIds.Should().ContainSingle().Which.Should().Be(existing.Id);
        existing.SchedulerJobId.Should().Be("push-job-id");
    }

    [Test]
    public async Task EnqueueAsync_WhenSchedulerReturnsNull_ClearsReservationForLaterEnqueue()
    {
        var userId = Id<User>.New();
        var installation = CreateInstallation(userId, permissionStatus: "authorized");
        var repository = new IndependentReservationPushNotificationMessageRepository();
        var scheduler = new NullThenRecordingPushBackgroundScheduler();
        var service = new PushNotificationService(
            new FakePushInstallationRepository(installation),
            repository,
            scheduler,
            new ReservationPersistingUnitOfWork(repository),
            NullLogger<PushNotificationService>.Instance);
        var input = new EnqueuePushNotificationInput(
            userId,
            1,
            "trainer.note.updated",
            "event-null-scheduler-job-id",
            null,
            null,
            null);

        await service.EnqueueAsync(input);

        await service.EnqueueAsync(input);

        repository.ClearReservationCalls.Should().Be(1);
        scheduler.EnqueueCalls.Should().Be(2);
        repository.Message.Should().NotBeNull();
        repository.Message!.SchedulerJobId.Should().Be("push-job-id");
        repository.ReservationId.Should().Be("push-job-id");
    }

    [Test]
    public async Task EnqueueAsync_WhenPermissionDenied_PersistsSkippedMessageWithoutScheduling()
    {
        await using var db = CreateDbContext("push-enqueue-skipped");
        var userId = Id<User>.New();
        db.PushInstallations.Add(CreateInstallation(userId, permissionStatus: "denied"));
        await db.SaveChangesAsync();

        var scheduler = new FakePushBackgroundScheduler();
        var service = new PushNotificationService(
            new PushInstallationRepository(db),
            new PushNotificationMessageRepository(db),
            scheduler,
            new EfUnitOfWork(db),
            NullLogger<PushNotificationService>.Instance);

        await service.EnqueueAsync(new EnqueuePushNotificationInput(
            userId,
            1,
            "trainer.note.updated",
            "event-2",
            null,
            null,
            null));

        db.PushNotificationMessages.Should().HaveCount(1);
        var message = db.PushNotificationMessages.Single();
        message.Status.Should().Be(PushNotificationStatus.Skipped);
        message.FailureKind.Should().Be(PushNotificationFailureKind.Preference);
        scheduler.EnqueuedNotificationIds.Should().BeEmpty();
    }

    [Test]
    public async Task EnqueueAsync_WhenNoActiveInstallation_DoesNotPersistOrSchedule()
    {
        await using var db = CreateDbContext("push-enqueue-no-active-installation");
        var scheduler = new FakePushBackgroundScheduler();
        var service = new PushNotificationService(
            new PushInstallationRepository(db),
            new PushNotificationMessageRepository(db),
            scheduler,
            new EfUnitOfWork(db),
            NullLogger<PushNotificationService>.Instance);

        await service.EnqueueAsync(new EnqueuePushNotificationInput(
            Id<User>.New(),
            1,
            "trainer.note.updated",
            "event-no-installation",
            null,
            null,
            null));

        db.PushNotificationMessages.Should().BeEmpty();
        scheduler.EnqueuedNotificationIds.Should().BeEmpty();
    }

    [Test]
    public async Task EnqueueAsync_WhenIndependentContenderWinsDeliveryKey_ResolvesWithoutSchedulingDuplicate()
    {
        var userId = Id<User>.New();
        var installation = CreateInstallation(userId, permissionStatus: "authorized");
        var winningMessage = CreateMessage(installation.Id);
        var repository = new RacePushNotificationMessageRepository(winningMessage);
        var scheduler = new FakePushBackgroundScheduler();
        var service = new PushNotificationService(
            new FakePushInstallationRepository(installation),
            repository,
            scheduler,
            new UniqueConflictUnitOfWork(() => repository.WinningMessageIsAvailable = true),
            NullLogger<PushNotificationService>.Instance);

        var act = () => service.EnqueueAsync(new EnqueuePushNotificationInput(
            userId,
            1,
            "trainer.note.updated",
            "event-race",
            null,
            null,
            null));

        await act.Should().NotThrowAsync();
        repository.DetachedMessages.Should().ContainSingle();
        repository.WinningMessageReads.Should().Be(1);
        scheduler.EnqueuedNotificationIds.Should().BeEmpty();
    }

    [Test]
    public async Task ProcessAsync_WhenInvalidToken_DisablesInstallationWithoutRetry()
    {
        var installation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        var message = CreateMessage(installation.Id);
        message.UserId = installation.UserId!.Value;
        var repository = new FakePushNotificationMessageRepository(message);
        var installationRepository = new FakePushInstallationRepository(installation);
        var scheduler = new FakePushBackgroundScheduler();
        var sender = new FakePushProviderSender(new PushSendAttemptResult(PushSendOutcome.InvalidToken, "BadRequest", null, "UNREGISTERED", "registration-token-not-registered"));
        var handler = CreateHandler(
            repository,
            installationRepository,
            sender,
            scheduler,
            new FakeUnitOfWork(),
            new PushNotificationOptions());

        await handler.ProcessAsync(message.Id);

        message.Status.Should().Be(PushNotificationStatus.Failed);
        message.FailureKind.Should().Be(PushNotificationFailureKind.InvalidToken);
        message.NextAttemptAt.Should().BeNull();
        installation.DisabledReason.Should().Be("InvalidFcmToken");
        installation.DisabledAt.Should().NotBeNull();
        sender.LastInstallationId.Should().Be(installation.Id);
        scheduler.ScheduledRetries.Should().BeEmpty();
    }

    [Test]
    public async Task ProcessAsync_WhenInstallationMissing_PersistsPermanentFailureWithoutSending()
    {
        var message = CreateMessage(Id<PushInstallation>.New());
        var repository = new FakePushNotificationMessageRepository(message);
        var sender = new FakePushProviderSender(new PushSendAttemptResult(PushSendOutcome.Sent, "OK", "message-1", null, "ok"));
        var unitOfWork = new FakeUnitOfWork();
        var handler = CreateHandler(
            repository,
            new FakePushInstallationRepository(),
            sender,
            new FakePushBackgroundScheduler(),
            unitOfWork,
            new PushNotificationOptions());

        await handler.ProcessAsync(message.Id);

        message.Status.Should().Be(PushNotificationStatus.Failed);
        message.FailureKind.Should().Be(PushNotificationFailureKind.Permanent);
        message.ProviderStatus.Should().Be("InstallationMissing");
        message.LastError.Should().Be("Push installation no longer exists.");
        message.Attempts.Should().Be(0);
        sender.SendCalls.Should().Be(0);
        unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task ProcessAsync_WhenProviderSends_PersistsSentDeliveryStateWithoutRetry()
    {
        var installation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        var message = CreateMessage(installation.Id);
        message.UserId = installation.UserId!.Value;
        message.LastError = "previous failure";
        message.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(1);
        var scheduler = new FakePushBackgroundScheduler();
        var handler = CreateHandler(
            new FakePushNotificationMessageRepository(message),
            new FakePushInstallationRepository(installation),
            new FakePushProviderSender(new PushSendAttemptResult(PushSendOutcome.Sent, "OK", "message-1", null, "accepted")),
            scheduler,
            new FakeUnitOfWork(),
            new PushNotificationOptions());

        await handler.ProcessAsync(message.Id);

        message.Status.Should().Be(PushNotificationStatus.Sent);
        message.FailureKind.Should().Be(PushNotificationFailureKind.None);
        message.SentAt.Should().NotBeNull();
        message.ProviderStatus.Should().Be("OK");
        message.ProviderMessageId.Should().Be("message-1");
        message.LastError.Should().BeNull();
        message.NextAttemptAt.Should().BeNull();
        scheduler.ScheduledRetries.Should().BeEmpty();
    }

    [Test]
    public async Task ProcessAsync_WhenProviderPermanentlyFails_PersistsPermanentFailureWithoutRetry()
    {
        var installation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        var message = CreateMessage(installation.Id);
        message.UserId = installation.UserId!.Value;
        var scheduler = new FakePushBackgroundScheduler();
        var handler = CreateHandler(
            new FakePushNotificationMessageRepository(message),
            new FakePushInstallationRepository(installation),
            new FakePushProviderSender(new PushSendAttemptResult(
                PushSendOutcome.PermanentFailure,
                "BadRequest",
                null,
                "INVALID_ARGUMENT",
                "non-retryable provider error")),
            scheduler,
            new FakeUnitOfWork(),
            new PushNotificationOptions());

        await handler.ProcessAsync(message.Id);

        message.Status.Should().Be(PushNotificationStatus.Failed);
        message.FailureKind.Should().Be(PushNotificationFailureKind.Permanent);
        message.ProviderStatus.Should().Be("BadRequest");
        message.ProviderErrorCode.Should().Be("INVALID_ARGUMENT");
        message.ProviderResponseSummary.Should().Be("non-retryable provider error");
        message.LastError.Should().Be("non-retryable provider error");
        message.NextAttemptAt.Should().BeNull();
        scheduler.ScheduledRetries.Should().BeEmpty();
    }

    [Test]
    public async Task ProcessAsync_WhenSendingDisabled_PersistsSkippedPreferenceStateWithoutRetryOrHttpCall()
    {
        var installation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        var message = CreateMessage(installation.Id);
        message.UserId = installation.UserId!.Value;
        var httpClientFactory = new FakeHttpClientFactory();
        var scheduler = new FakePushBackgroundScheduler();
        var handler = CreateHandler(
            new FakePushNotificationMessageRepository(message),
            new FakePushInstallationRepository(installation),
            new FcmPushSender(
                httpClientFactory,
                new FakePushInstallationRepository(installation),
                new PushNotificationOptions { SendEnabled = false },
                NullLogger<FcmPushSender>.Instance),
            scheduler,
            new FakeUnitOfWork(),
            new PushNotificationOptions());

        await handler.ProcessAsync(message.Id);

        message.Status.Should().Be(PushNotificationStatus.Skipped);
        message.FailureKind.Should().Be(PushNotificationFailureKind.Preference);
        message.ProviderStatus.Should().Be("Skipped");
        message.ProviderErrorCode.Should().Be("push-disabled");
        message.NextAttemptAt.Should().BeNull();
        scheduler.ScheduledRetries.Should().BeEmpty();
        httpClientFactory.CreateClientCalls.Should().Be(0);
    }

    [Test]
    public async Task ProcessAsync_WhenProviderThrows_DoesNotLeaveMessageSending()
    {
        var (handler, message, scheduler) = CreateThrowingHandler(new InvalidOperationException("token-1\r\ncredential content"));

        Func<Task> act = () => handler.ProcessAsync(message.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        AssertRetryableExceptionRecovery(message, scheduler, "InvalidOperationException");
        message.LastError.Should().NotContain("token-1");
        message.LastError.Should().NotContain("credential content");
    }

    [Test]
    public async Task ProcessAsync_WhenCredentialAcquisitionThrows_DoesNotLeaveMessageSending()
    {
        var installation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        var message = CreateMessage(installation.Id);
        message.UserId = installation.UserId!.Value;
        var httpClientFactory = new FakeHttpClientFactory();
        var scheduler = new FakePushBackgroundScheduler();
        var handler = CreateHandler(
            new FakePushNotificationMessageRepository(message),
            new FakePushInstallationRepository(installation),
            new FcmPushSender(
                httpClientFactory,
                new FakePushInstallationRepository(installation),
                new PushNotificationOptions { SendEnabled = true },
                NullLogger<FcmPushSender>.Instance),
            scheduler,
            new FakeUnitOfWork(),
            new PushNotificationOptions());

        Func<Task> act = () => handler.ProcessAsync(message.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        httpClientFactory.CreateClientCalls.Should().Be(0);
        AssertRetryableExceptionRecovery(message, scheduler, "InvalidOperationException");
    }

    [Test]
    public async Task ProcessAsync_WhenProviderHttpCallThrows_DoesNotLeaveMessageSending()
    {
        var (handler, message, scheduler) = CreateThrowingHandler(new HttpRequestException("provider HTTP failure"));

        Func<Task> act = () => handler.ProcessAsync(message.Id);

        await act.Should().ThrowAsync<HttpRequestException>();
        AssertRetryableExceptionRecovery(message, scheduler, "HttpRequestException");
    }

    [Test]
    public async Task ProcessAsync_WhenCancelledAfterClaim_PropagatesCancellationWithoutLeavingMessageSending()
    {
        var installation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        var message = CreateMessage(installation.Id);
        message.UserId = installation.UserId!.Value;
        using var cancellationTokenSource = new CancellationTokenSource();
        var repository = new FakePushNotificationMessageRepository(message)
        {
            OnClaimed = cancellationTokenSource.Cancel
        };
        var sender = new FakePushProviderSender((_, _, cancellationToken) => Task.FromCanceled<PushSendAttemptResult>(cancellationToken));
        var scheduler = new FakePushBackgroundScheduler();
        var handler = CreateHandler(
            repository,
            new FakePushInstallationRepository(installation),
            sender,
            scheduler,
            new FakeUnitOfWork(),
            new PushNotificationOptions());

        Func<Task> act = () => handler.ProcessAsync(message.Id, cancellationTokenSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sender.LastCancellationToken.Should().Be(cancellationTokenSource.Token);
        sender.LastCancellationToken.IsCancellationRequested.Should().BeTrue();
        AssertRetryableExceptionRecovery(message, scheduler, "OperationCanceledException");
    }

    [Test]
    public async Task ProcessAsync_WhenProviderThrowsAfterRetryAttemptsExhausted_PersistsTerminalTransientFailureWithoutRetry()
    {
        var (handler, message, scheduler) = CreateThrowingHandler(new HttpRequestException("provider HTTP failure"), new PushNotificationOptions { RetryDelaysSeconds = [5] });
        message.Attempts = 1;

        Func<Task> act = () => handler.ProcessAsync(message.Id);

        await act.Should().ThrowAsync<HttpRequestException>();
        message.Status.Should().Be(PushNotificationStatus.Failed);
        message.FailureKind.Should().Be(PushNotificationFailureKind.Transient);
        message.NextAttemptAt.Should().BeNull();
        scheduler.ScheduledRetries.Should().BeEmpty();
    }

    [Test]
    public async Task ProcessAsync_WhenTransientFailure_SchedulesRetry()
    {
        var installation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        var message = CreateMessage(installation.Id);
        message.UserId = installation.UserId!.Value;
        var repository = new FakePushNotificationMessageRepository(message);
        var scheduler = new FakePushBackgroundScheduler();
        var handler = CreateHandler(
            repository,
            new FakePushInstallationRepository(installation),
            new FakePushProviderSender(new PushSendAttemptResult(PushSendOutcome.TransientFailure, "ServiceUnavailable", null, "UNAVAILABLE", "temporary outage")),
            scheduler,
            new FakeUnitOfWork(),
            new PushNotificationOptions { RetryDelaysSeconds = [5] });

        await handler.ProcessAsync(message.Id);

        message.Status.Should().Be(PushNotificationStatus.Failed);
        message.FailureKind.Should().Be(PushNotificationFailureKind.Transient);
        message.NextAttemptAt.Should().NotBeNull();
        scheduler.ScheduledRetries.Should().ContainSingle();
        scheduler.ScheduledRetries[0].notificationId.Should().Be(message.Id);
        scheduler.ScheduledRetries[0].delay.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ProcessAsync_WhenRetrySchedulerThrows_ClearsStaleJobIdAndLeavesProviderTransientStateRecoverable()
    {
        var installation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        var message = CreateMessage(installation.Id);
        message.UserId = installation.UserId!.Value;
        message.SchedulerJobId = "completed-retry-job";
        var scheduler = new ThrowingRetryPushBackgroundScheduler();
        var handler = CreateHandler(
            new FakePushNotificationMessageRepository(message),
            new FakePushInstallationRepository(installation),
            new FakePushProviderSender(new PushSendAttemptResult(
                PushSendOutcome.TransientFailure,
                "ServiceUnavailable",
                null,
                "UNAVAILABLE",
                "temporary outage")),
            scheduler,
            new FakeUnitOfWork(),
            new PushNotificationOptions { RetryDelaysSeconds = [5] });

        var act = () => handler.ProcessAsync(message.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        message.Status.Should().Be(PushNotificationStatus.Failed);
        message.FailureKind.Should().Be(PushNotificationFailureKind.Transient);
        message.NextAttemptAt.Should().NotBeNull();
        message.SchedulerJobId.Should().BeNull();
        message.ProviderStatus.Should().Be("ServiceUnavailable");
        scheduler.ScheduleRetryCalls.Should().Be(1);
    }

    [Test]
    public async Task ProcessAsync_WhenPostClaimMessageLoadFails_RecoversByReloadingTheClaimedMessage()
    {
        var installation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        var message = CreateMessage(installation.Id);
        message.UserId = installation.UserId!.Value;
        var repository = new FakePushNotificationMessageRepository(message)
        {
            FindByIdFailure = call => call == 1 ? new InvalidOperationException("transient read failure") : null
        };
        var scheduler = new FakePushBackgroundScheduler();
        var handler = CreateHandler(
            repository,
            new FakePushInstallationRepository(installation),
            new FakePushProviderSender(new PushSendAttemptResult(PushSendOutcome.Sent, "OK", "message-1", null, "ok")),
            scheduler,
            new FakeUnitOfWork(),
            new PushNotificationOptions());

        var act = () => handler.ProcessAsync(message.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        repository.FindByIdCalls.Should().Be(2);
        AssertRetryableExceptionRecovery(message, scheduler, "InvalidOperationException");
    }

    [Test]
    public async Task ProcessAsync_WhenClaimFails_DoesNotSendAgain()
    {
        var installation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        var message = CreateMessage(installation.Id);
        message.UserId = installation.UserId!.Value;
        var repository = new FakePushNotificationMessageRepository(message) { TryClaimResult = false };
        var sender = new FakePushProviderSender(new PushSendAttemptResult(PushSendOutcome.Sent, "OK", "message-1", null, "ok"));
        var handler = CreateHandler(
            repository,
            new FakePushInstallationRepository(installation),
            sender,
            new FakePushBackgroundScheduler(),
            new FakeUnitOfWork(),
            new PushNotificationOptions());

        await handler.ProcessAsync(message.Id);

        sender.SendCalls.Should().Be(0);
        message.Attempts.Should().Be(0);
    }

    [Test]
    public async Task CleanupAsync_WhenInstallationIsStale_DisablesItWithoutDeletingAuditState()
    {
        var staleInstallation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        staleInstallation.LastSeenAt = DateTimeOffset.UtcNow.AddDays(-60);
        var recentInstallation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        recentInstallation.LastSeenAt = DateTimeOffset.UtcNow.AddDays(-5);

        var repository = new FakePushInstallationRepository(staleInstallation, recentInstallation);
        var unitOfWork = new FakeUnitOfWork();
        var service = new StalePushInstallationCleanupService(
            repository,
            unitOfWork,
            new FakeStalePushInstallationCleanupSettings
            {
                Enabled = true,
                InactivityDays = 30,
                BatchSize = 10
            },
            NullLogger<StalePushInstallationCleanupService>.Instance);

        var cleaned = await service.CleanupAsync();

        cleaned.Should().Be(1);
        staleInstallation.DisabledReason.Should().Be(StalePushInstallationCleanupService.StaleInstallationDisabledReason);
        staleInstallation.DisabledAt.Should().NotBeNull();
        recentInstallation.DisabledAt.Should().BeNull();
        unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Test]
    public async Task FcmSender_BuildsPrivacySafeNotificationPayloadForAndroidDisplay()
    {
        var installation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        var sender = new FcmPushSender(
            new FakeHttpClientFactory(),
            new FakePushInstallationRepository(installation),
            new PushNotificationOptions(),
            NullLogger<FcmPushSender>.Instance);
        var notificationId = Id<InAppNotification>.New();
        var payload = new PushEventPayload(
            1,
            "internal.test.push",
            "event-1",
            "entity-1",
            notificationId,
            "/notifications");

        var method = typeof(FcmPushSender).GetMethod("BuildRequestContent", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        using var content = (StringContent)method!.Invoke(sender, [installation.FcmToken, payload])!;
        var json = await content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var message = document.RootElement.GetProperty("message");

        message.GetProperty("token").GetString().Should().Be(installation.FcmToken);
        message.GetProperty("notification").GetProperty("title").GetString().Should().Be("LGYM");
        message.GetProperty("notification").GetProperty("body").GetString().Should().Be("You have a new notification.");
        message.GetProperty("android").GetProperty("priority").GetString().Should().Be("HIGH");
        message.GetProperty("data").GetProperty("deeplink").GetString().Should().Be("/notifications");
        message.GetProperty("data").GetProperty("inAppNotificationId").GetString().Should().Be(notificationId.ToString());
    }

    [Test]
    public async Task FcmSender_WhenSendingDisabled_ReturnsSkippedWithoutHttpCall()
    {
        var httpClientFactory = new FakeHttpClientFactory();
        var installation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        var installationRepository = new FakePushInstallationRepository(installation);
        var sender = new FcmPushSender(
            httpClientFactory,
            installationRepository,
            new PushNotificationOptions { SendEnabled = false },
            NullLogger<FcmPushSender>.Instance);
        var payload = new PushEventPayload(1, "internal.test.push", "event-1", null, null, null);

        var result = await sender.SendAsync(installation.Id, payload);

        result.Outcome.Should().Be(PushSendOutcome.Skipped);
        result.ProviderStatus.Should().Be("Skipped");
        result.ProviderErrorCode.Should().Be("push-disabled");
        installationRepository.FindByIdCalls.Should().Be(0);
        httpClientFactory.CreateClientCalls.Should().Be(0);
    }

    [Test]
    public async Task FcmSender_WhenInstallationLookupIsCancelled_PropagatesCancellationWithoutHttpCall()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();
        var httpClientFactory = new FakeHttpClientFactory();
        var installationRepository = new FakePushInstallationRepository
        {
            FindByIdAsyncOverride = (_, cancellationToken) => Task.FromCanceled<PushInstallation?>(cancellationToken)
        };
        var sender = new FcmPushSender(
            httpClientFactory,
            installationRepository,
            new PushNotificationOptions { SendEnabled = true },
            NullLogger<FcmPushSender>.Instance);
        var payload = new PushEventPayload(1, "internal.test.push", "event-1", null, null, null);

        var action = () => sender.SendAsync(Id<PushInstallation>.New(), payload, cancellationTokenSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        installationRepository.FindByIdCalls.Should().Be(1);
        httpClientFactory.CreateClientCalls.Should().Be(0);
    }

    [Test]
    public void FcmSender_BuildsProjectScopedSendUrl()
    {
        var sender = new FcmPushSender(
            new FakeHttpClientFactory(),
            new FakePushInstallationRepository(),
            new PushNotificationOptions
            {
                Fcm = { BaseUrl = "https://fcm.example.test", ProjectId = "lgym-test" }
            },
            NullLogger<FcmPushSender>.Instance);

        var url = InvokePrivate<string>(sender, "BuildSendUrl");

        url.Should().Be("https://fcm.example.test/v1/projects/lgym-test/messages:send");
    }

    [TestCase(HttpStatusCode.BadRequest, "registration-token-not-registered", PushSendOutcome.InvalidToken)]
    [TestCase(HttpStatusCode.ServiceUnavailable, "provider unavailable", PushSendOutcome.TransientFailure)]
    [TestCase(HttpStatusCode.BadGateway, "bad gateway", PushSendOutcome.TransientFailure)]
    [TestCase(HttpStatusCode.BadRequest, "invalid payload", PushSendOutcome.PermanentFailure)]
    public void FcmSender_ClassifiesProviderFailures(HttpStatusCode statusCode, string summary, PushSendOutcome expectedOutcome)
    {
        var outcome = InvokeStaticPrivate<PushSendOutcome>(
            typeof(FcmPushSender),
            "ClassifyFailure",
            statusCode,
            summary);

        outcome.Should().Be(expectedOutcome);
    }

    [Test]
    public void FcmSender_ExtractsProviderValuesFromDirectAndNestedJson()
    {
        InvokeStaticPrivate<string?>(typeof(FcmPushSender), "TryExtractValue", "{\"name\":\"projects/lgym/messages/1\"}", "name")
            .Should().Be("projects/lgym/messages/1");
        InvokeStaticPrivate<string?>(typeof(FcmPushSender), "TryExtractValue", "{\"error\":{\"status\":\"UNREGISTERED\"}}", "status")
            .Should().Be("UNREGISTERED");
        InvokeStaticPrivate<string?>(typeof(FcmPushSender), "TryExtractValue", "not-json", "status")
            .Should().BeNull();
        InvokeStaticPrivate<string?>(typeof(FcmPushSender), "TryExtractValue", string.Empty, "status")
            .Should().BeNull();
    }

    [Test]
    public void FcmSender_SummarizesProviderResponses()
    {
        InvokeStaticPrivate<string>(
                typeof(FcmPushSender),
                "Summarize",
                "{\"error\":{\"status\":\"UNREGISTERED\",\"message\":\"token-secret-not-for-storage\"}}")
            .Should().Be("status=UNREGISTERED");
        InvokeStaticPrivate<string>(typeof(FcmPushSender), "Summarize", "token-secret-not-for-storage")
            .Should().Be("provider-response-received");
        InvokeStaticPrivate<string>(typeof(FcmPushSender), "Summarize", (object?)null)
            .Should().BeEmpty();
    }

    [Test]
    public async Task FcmSender_WhenCredentialsAreInvalid_ThrowsWithoutCredentialContents()
    {
        const string credentialSentinel = "credential-secret-not-for-logs";
        var installation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        var sender = new FcmPushSender(
            new FakeHttpClientFactory(),
            new FakePushInstallationRepository(installation),
            new PushNotificationOptions
            {
                SendEnabled = true,
                Fcm =
                {
                    ProjectId = "lgym-test",
                    BaseUrl = "https://fcm.example.test",
                    CredentialsJson = credentialSentinel
                }
            },
            NullLogger<FcmPushSender>.Instance);
        var payload = new PushEventPayload(1, "internal.test.push", "event-1", null, null, null);

        var action = () => sender.SendAsync(installation.Id, payload);

        var exception = await action.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("FCM provider delivery failed.");
        exception.Which.Message.Should().NotContain(credentialSentinel);
    }

    private static AppDbContext CreateDbContext(string name)
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"{name}-{Id<PushNotificationPipelineTests>.New():N}")
            .Options);

    private static PushInstallation CreateInstallation(Id<User> userId, string permissionStatus)
        => new()
        {
            Id = Id<PushInstallation>.New(),
            UserId = userId,
            InstallationId = $"device-{Id<PushInstallation>.New():N}",
            Platform = "android",
            FcmToken = "token-1",
            Environment = "development",
            PermissionStatus = permissionStatus,
            LastSeenAt = DateTimeOffset.UtcNow
        };

    private static PushNotificationMessage CreateMessage(Id<PushInstallation> installationId)
        => new()
        {
            Id = Id<PushNotificationMessage>.New(),
            UserId = Id<User>.New(),
            PushInstallationId = installationId,
            SchemaVersion = 1,
            Type = "trainer.note.updated",
            EventId = "event-1",
            PayloadJson = "{\"schemaVersion\":1,\"type\":\"trainer.note.updated\",\"eventId\":\"event-1\"}",
            Status = PushNotificationStatus.Pending
        };

    private static (PushNotificationJobHandlerService handler, PushNotificationMessage message, FakePushBackgroundScheduler scheduler) CreateThrowingHandler(
        Exception exception,
        PushNotificationOptions? options = null)
    {
        var installation = CreateInstallation(Id<User>.New(), permissionStatus: "authorized");
        var message = CreateMessage(installation.Id);
        message.UserId = installation.UserId!.Value;
        var scheduler = new FakePushBackgroundScheduler();
        var handler = CreateHandler(
            new FakePushNotificationMessageRepository(message),
            new FakePushInstallationRepository(installation),
            new FakePushProviderSender(exception),
            scheduler,
            new FakeUnitOfWork(),
            options ?? new PushNotificationOptions());

        return (handler, message, scheduler);
    }

    private static void AssertRetryableExceptionRecovery(
        PushNotificationMessage message,
        FakePushBackgroundScheduler scheduler,
        string exceptionType)
    {
        message.Status.Should().Be(PushNotificationStatus.Failed);
        message.FailureKind.Should().Be(PushNotificationFailureKind.Transient);
        message.NextAttemptAt.Should().NotBeNull();
        message.ProviderStatus.Should().Be("Exception");
        message.ProviderErrorCode.Should().Be(exceptionType);
        message.ProviderResponseSummary.Should().Be($"Push delivery exception: {exceptionType}");
        message.LastError.Should().Be($"Push delivery exception: {exceptionType}");
        scheduler.ScheduledRetries.Should().ContainSingle();
        scheduler.ScheduledRetries[0].notificationId.Should().Be(message.Id);
    }

    private static PushNotificationJobHandlerService CreateHandler(
        IPushNotificationMessageRepository messageRepository,
        IPushInstallationRepository installationRepository,
        IPushProviderSender sender,
        IPushBackgroundScheduler scheduler,
        IUnitOfWork unitOfWork,
        PushNotificationOptions options)
    {
        return new PushNotificationJobHandlerService(
            new PushNotificationDeliveryService(
                messageRepository,
                installationRepository,
                sender,
                scheduler,
                new PushNotificationDeliveryRetrySettings(options),
                unitOfWork,
                NullLogger<PushNotificationDeliveryService>.Instance));
    }

    private static TResult InvokePrivate<TResult>(object instance, string methodName, params object?[] parameters)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (TResult)method!.Invoke(instance, parameters)!;
    }

    private static TResult InvokeStaticPrivate<TResult>(Type type, string methodName, params object?[] parameters)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (TResult)method!.Invoke(null, parameters)!;
    }

    private sealed class FakePushBackgroundScheduler : IPushBackgroundScheduler
    {
        public List<Id<PushNotificationMessage>> EnqueuedNotificationIds { get; } = [];
        public List<(Id<PushNotificationMessage> notificationId, TimeSpan delay)> ScheduledRetries { get; } = [];

        public string? Enqueue(string notificationId)
        {
            if (!Id<PushNotificationMessage>.TryParse(notificationId, out var parsedNotificationId))
            {
                throw new FormatException("Notification ID must be a valid ID.");
            }

            EnqueuedNotificationIds.Add(parsedNotificationId);
            return "push-job-id";
        }

        public string? ScheduleRetry(string notificationId, TimeSpan delay)
        {
            if (!Id<PushNotificationMessage>.TryParse(notificationId, out var parsedNotificationId))
            {
                throw new FormatException("Notification ID must be a valid ID.");
            }

            ScheduledRetries.Add((parsedNotificationId, delay));
            return "push-retry-job-id";
        }
    }

    private sealed class ThrowingRetryPushBackgroundScheduler : IPushBackgroundScheduler
    {
        public int ScheduleRetryCalls { get; private set; }

        public string? Enqueue(string notificationId) => "push-job-id";

        public string? ScheduleRetry(string notificationId, TimeSpan delay)
        {
            ScheduleRetryCalls += 1;
            throw new InvalidOperationException("scheduler unavailable");
        }
    }

    private sealed class NullThenRecordingPushBackgroundScheduler : IPushBackgroundScheduler
    {
        public int EnqueueCalls { get; private set; }

        public string? Enqueue(string notificationId)
        {
            EnqueueCalls += 1;
            return EnqueueCalls == 1 ? null : "push-job-id";
        }

        public string? ScheduleRetry(string notificationId, TimeSpan delay) => "push-retry-job-id";
    }

    private sealed class FakePushProviderSender : IPushProviderSender
    {
        private readonly Func<Id<PushInstallation>, PushEventPayload, CancellationToken, Task<PushSendAttemptResult>> _sendAsync;

        public FakePushProviderSender(PushSendAttemptResult result)
            : this((_, _, _) => Task.FromResult(result))
        {
        }

        public FakePushProviderSender(Exception exception)
            : this((_, _, _) => Task.FromException<PushSendAttemptResult>(exception))
        {
        }

        public FakePushProviderSender(Func<Id<PushInstallation>, PushEventPayload, CancellationToken, Task<PushSendAttemptResult>> sendAsync)
        {
            _sendAsync = sendAsync;
        }

        public int SendCalls { get; private set; }

        public Id<PushInstallation>? LastInstallationId { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<PushSendAttemptResult> SendAsync(Id<PushInstallation> installationId, PushEventPayload payload, CancellationToken cancellationToken = default)
        {
            SendCalls += 1;
            LastInstallationId = installationId;
            LastCancellationToken = cancellationToken;
            return _sendAsync(installationId, payload, cancellationToken);
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public int CreateClientCalls { get; private set; }

        public HttpClient CreateClient(string name)
        {
            CreateClientCalls += 1;
            return new HttpClient();
        }
    }

    private sealed class FakePushNotificationMessageRepository : IPushNotificationMessageRepository
    {
        private readonly PushNotificationMessage _message;

        public FakePushNotificationMessageRepository(PushNotificationMessage message)
        {
            _message = message;
        }

        public bool TryClaimResult { get; set; } = true;

        public Action? OnClaimed { get; init; }

        public Func<int, Exception?>? FindByIdFailure { get; init; }

        public int FindByIdCalls { get; private set; }

        public Task AddAsync(PushNotificationMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PushNotificationMessage?> FindByIdAsync(Id<PushNotificationMessage> id, CancellationToken cancellationToken = default)
        {
            FindByIdCalls += 1;
            var exception = FindByIdFailure?.Invoke(FindByIdCalls);
            return exception == null
                ? Task.FromResult<PushNotificationMessage?>(_message.Id == id ? _message : null)
                : Task.FromException<PushNotificationMessage?>(exception);
        }

        public Task<PushNotificationMessage?> FindByDeliveryKeyAsync(Id<PushInstallation> installationId, string type, string eventId, CancellationToken cancellationToken = default)
            => Task.FromResult<PushNotificationMessage?>(null);

        public Task<bool> TryReserveSchedulingAsync(Id<PushNotificationMessage> id, string reservationId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task ClearSchedulingReservationAsync(Id<PushNotificationMessage> id, string reservationId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> TryTransitionToSendingAsync(Id<PushNotificationMessage> id, CancellationToken cancellationToken = default)
        {
            if (TryClaimResult && _message.Id == id)
            {
                _message.Status = PushNotificationStatus.Sending;
                OnClaimed?.Invoke();
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public void Detach(PushNotificationMessage message) { }

        public Task UpdateAsync(PushNotificationMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<List<PushNotificationMessage>> GetByStatusAsync(PushNotificationStatus status, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<PushNotificationMessage>());
    }

    private sealed class RacePushNotificationMessageRepository : IPushNotificationMessageRepository
    {
        private readonly PushNotificationMessage _winningMessage;

        public RacePushNotificationMessageRepository(PushNotificationMessage winningMessage)
        {
            _winningMessage = winningMessage;
        }

        public List<PushNotificationMessage> DetachedMessages { get; } = [];

        public bool WinningMessageIsAvailable { get; set; }

        public int WinningMessageReads { get; private set; }

        public Task AddAsync(PushNotificationMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Detach(PushNotificationMessage message) => DetachedMessages.Add(message);

        public Task<PushNotificationMessage?> FindByIdAsync(Id<PushNotificationMessage> id, CancellationToken cancellationToken = default)
            => Task.FromResult<PushNotificationMessage?>(_winningMessage.Id == id ? _winningMessage : null);

        public Task<PushNotificationMessage?> FindByDeliveryKeyAsync(Id<PushInstallation> installationId, string type, string eventId, CancellationToken cancellationToken = default)
        {
            if (!WinningMessageIsAvailable)
            {
                return Task.FromResult<PushNotificationMessage?>(null);
            }

            WinningMessageReads += 1;
            return Task.FromResult<PushNotificationMessage?>(_winningMessage);
        }

        public Task<bool> TryReserveSchedulingAsync(Id<PushNotificationMessage> id, string reservationId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task ClearSchedulingReservationAsync(Id<PushNotificationMessage> id, string reservationId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> TryTransitionToSendingAsync(Id<PushNotificationMessage> id, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task UpdateAsync(PushNotificationMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<List<PushNotificationMessage>> GetByStatusAsync(PushNotificationStatus status, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<PushNotificationMessage>());
    }

    private sealed class IndependentReservationPushNotificationMessageRepository : IPushNotificationMessageRepository
    {
        public PushNotificationMessage? Message { get; private set; }

        public string? ReservationId { get; private set; }

        public int ClearReservationCalls { get; private set; }

        public void PersistSchedulerJobId()
        {
            if (Message?.SchedulerJobId is { } schedulerJobId)
            {
                ReservationId = schedulerJobId;
            }
        }

        public Task AddAsync(PushNotificationMessage message, CancellationToken cancellationToken = default)
        {
            Message = message;
            return Task.CompletedTask;
        }

        public void Detach(PushNotificationMessage message) { }

        public Task<PushNotificationMessage?> FindByIdAsync(Id<PushNotificationMessage> id, CancellationToken cancellationToken = default)
            => Task.FromResult(Message?.Id == id ? Message : null);

        public Task<PushNotificationMessage?> FindByDeliveryKeyAsync(Id<PushInstallation> installationId, string type, string eventId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Message is { PushInstallationId: var messageInstallationId, Type: var messageType, EventId: var messageEventId }
                && messageInstallationId == installationId
                && messageType == type
                && messageEventId == eventId
                    ? Message
                    : null);
        }

        public Task<bool> TryReserveSchedulingAsync(Id<PushNotificationMessage> id, string reservationId, CancellationToken cancellationToken = default)
        {
            if (Message?.Id != id || ReservationId != null)
            {
                return Task.FromResult(false);
            }

            ReservationId = reservationId;
            return Task.FromResult(true);
        }

        public Task ClearSchedulingReservationAsync(Id<PushNotificationMessage> id, string reservationId, CancellationToken cancellationToken = default)
        {
            if (Message?.Id == id && ReservationId == reservationId)
            {
                ReservationId = null;
                ClearReservationCalls += 1;
            }

            return Task.CompletedTask;
        }

        public Task<bool> TryTransitionToSendingAsync(Id<PushNotificationMessage> id, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task UpdateAsync(PushNotificationMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<List<PushNotificationMessage>> GetByStatusAsync(PushNotificationStatus status, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<PushNotificationMessage>());
    }

    private sealed class FakePushInstallationRepository : IPushInstallationRepository
    {
        private readonly List<PushInstallation> _installations;

        public FakePushInstallationRepository(params PushInstallation[] installations)
        {
            _installations = installations.ToList();
        }

        public int FindByIdCalls { get; private set; }

        public Func<Id<PushInstallation>, CancellationToken, Task<PushInstallation?>>? FindByIdAsyncOverride { get; init; }

        public Task<PushInstallation?> FindByIdAsync(Id<PushInstallation> id, CancellationToken cancellationToken = default)
        {
            FindByIdCalls += 1;
            if (FindByIdAsyncOverride != null)
            {
                return FindByIdAsyncOverride(id, cancellationToken);
            }

            return Task.FromResult<PushInstallation?>(_installations.FirstOrDefault(x => x.Id == id));
        }

        public Task<PushInstallation?> FindByInstallationIdAsync(string installationId, CancellationToken cancellationToken = default)
            => Task.FromResult<PushInstallation?>(_installations.FirstOrDefault(x => x.InstallationId == installationId));

        public Task<PushInstallation?> FindBoundToUserOrSessionAsync(string installationId, Id<User> userId, Id<UserSession> sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<PushInstallation?>(_installations.FirstOrDefault(x => x.InstallationId == installationId && (x.UserId == userId || x.SessionId == sessionId)));

        public Task<List<PushInstallation>> GetActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
            => Task.FromResult(_installations.Where(x => x.UserId == userId && x.DisabledAt == null).ToList());

        public Task<List<PushInstallation>> GetBySessionIdAsync(Id<UserSession> sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(_installations.Where(x => x.SessionId == sessionId).ToList());

        public Task<List<PushInstallation>> GetStaleActiveAsync(DateTimeOffset lastSeenBefore, int limit, CancellationToken cancellationToken = default)
            => Task.FromResult(_installations.Where(x => x.DisabledAt == null && x.LastSeenAt < lastSeenBefore).OrderBy(x => x.LastSeenAt).Take(limit).ToList());

        public Task AddAsync(PushInstallation installation, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(PushInstallation installation, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpsertForUserSessionAsync(PushInstallationRegistration registration, CancellationToken cancellationToken = default)
        {
            var installation = _installations.FirstOrDefault(entity => entity.InstallationId == registration.InstallationId);
            if (installation == null)
            {
                installation = new PushInstallation { Id = Id<PushInstallation>.New() };
                _installations.Add(installation);
            }

            installation.UserId = registration.UserId;
            installation.SessionId = registration.SessionId;
            installation.InstallationId = registration.InstallationId;
            installation.Platform = registration.Platform;
            installation.FcmToken = registration.FcmToken;
            installation.AppVersion = registration.AppVersion;
            installation.Environment = registration.Environment;
            installation.PermissionStatus = registration.PermissionStatus;
            installation.LastSeenAt = registration.LastSeenAt;
            installation.DisabledAt = null;
            installation.DisabledReason = null;
            return Task.CompletedTask;
        }

        public Task<bool> DisableBoundForUserOrSessionAsync(
            string installationId,
            Id<User> userId,
            Id<UserSession> sessionId,
            DateTimeOffset disabledAt,
            string disabledReason,
            CancellationToken cancellationToken = default)
        {
            var installation = _installations.FirstOrDefault(entity => entity.InstallationId == installationId && (entity.UserId == userId || entity.SessionId == sessionId));
            if (installation == null)
            {
                return Task.FromResult(false);
            }

            installation.DisabledAt = disabledAt;
            installation.DisabledReason = disabledReason;
            installation.LastSeenAt = disabledAt;
            return Task.FromResult(true);
        }

        public Task<bool> DisassociateBoundForUserOrSessionAsync(
            string installationId,
            Id<User> userId,
            Id<UserSession> sessionId,
            DateTimeOffset lastSeenAt,
            CancellationToken cancellationToken = default)
        {
            var installation = _installations.FirstOrDefault(entity => entity.InstallationId == installationId && (entity.UserId == userId || entity.SessionId == sessionId));
            if (installation == null)
            {
                return Task.FromResult(false);
            }

            installation.UserId = null;
            installation.SessionId = null;
            installation.LastSeenAt = lastSeenAt;
            return Task.FromResult(true);
        }

        public Task RemoveForAccountAsync(Id<User> userId, CancellationToken cancellationToken = default)
        {
            _installations.RemoveAll(installation => installation.UserId == userId);
            return Task.CompletedTask;
        }

        public Task DisassociateForSessionAsync(Id<AccountSessionReference> sessionId, DateTimeOffset lastSeenAt, CancellationToken cancellationToken = default)
        {
            foreach (var installation in _installations.Where(entity => entity.SessionId == sessionId.Rebind<UserSession>()))
            {
                installation.UserId = null;
                installation.SessionId = null;
                installation.LastSeenAt = lastSeenAt;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls += 1;
            return Task.FromResult(1);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IUnitOfWorkTransaction>(new FakeUnitOfWorkTransaction());
    }

    private sealed class UniqueConflictUnitOfWork : IUnitOfWork
    {
        private readonly Action _onConflict;

        public UniqueConflictUnitOfWork(Action onConflict)
        {
            _onConflict = onConflict;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _onConflict();
            return Task.FromException<int>(new DbUpdateException("duplicate IX_PushNotificationMessages_PushInstallationId_Type_EventId"));
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IUnitOfWorkTransaction>(new FakeUnitOfWorkTransaction());
    }

    private sealed class ReservationPersistingUnitOfWork : IUnitOfWork
    {
        private readonly IndependentReservationPushNotificationMessageRepository _repository;

        public ReservationPersistingUnitOfWork(IndependentReservationPushNotificationMessageRepository repository)
        {
            _repository = repository;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _repository.PersistSchedulerJobId();
            return Task.FromResult(1);
        }

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IUnitOfWorkTransaction>(new FakeUnitOfWorkTransaction());
    }

    private sealed class FakeUnitOfWorkTransaction : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeStalePushInstallationCleanupSettings : IStalePushInstallationCleanupSettings
    {
        public bool Enabled { get; init; }
        public int InactivityDays { get; init; }
        public int BatchSize { get; init; }
    }
}
