using System.Text.Json;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Constants;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications;

internal sealed class PushNotificationDeliveryService : IPushNotificationDeliveryService
{
    private const int MaximumExceptionTypeLength = 128;

    private readonly IPushNotificationDeliveryServiceDependencies _dependencies;

    public PushNotificationDeliveryService(IPushNotificationDeliveryServiceDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public async Task ProcessAsync(
        string notificationId,
        CancellationToken cancellationToken = default)
    {
        if (!Id<PushNotificationMessage>.TryParse(notificationId, out var parsedNotificationId))
        {
            throw new ArgumentException("Push notification ID is invalid.", nameof(notificationId));
        }

        await ProcessAsync(parsedNotificationId, cancellationToken);
    }

    private async Task ProcessAsync(
        Id<PushNotificationMessage> notificationId,
        CancellationToken cancellationToken)
    {
        var claimed = await _dependencies.PushNotificationMessageRepository.TryTransitionToSendingAsync(notificationId, cancellationToken);
        if (!claimed)
        {
            _dependencies.Logger.LogInformation(
                "Push notification {NotificationId} could not be claimed for sending; skipping duplicate work.",
                notificationId);
            return;
        }

        PushNotificationMessage? message = null;
        var deliveryAttemptStarted = false;
        TimeSpan? retryDelay = null;
        try
        {
            message = await _dependencies.PushNotificationMessageRepository.FindByIdAsync(notificationId, cancellationToken);
            if (message == null)
            {
                _dependencies.Logger.LogWarning("Push notification {NotificationId} was not found after claim.", notificationId);
                return;
            }

            var installation = await _dependencies.PushInstallationRepository.FindByIdAsync(message.PushInstallationId, cancellationToken);
            if (installation == null)
            {
                message.Status = PushNotificationStatus.Failed;
                message.FailureKind = PushNotificationFailureKind.Permanent;
                message.ProviderStatus = "InstallationMissing";
                message.LastError = "Push installation no longer exists.";
                await _dependencies.UnitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }

            deliveryAttemptStarted = true;
            message.Attempts += 1;
            var payload = JsonSerializer.Deserialize<PushEventPayload>(message.PayloadJson, SharedSerializationOptions.Current)
                ?? throw new InvalidOperationException($"Failed to deserialize push payload for notification {notificationId}.");

            var result = await _dependencies.PushProviderSender.SendAsync(message.PushInstallationId, payload, cancellationToken);
            retryDelay = result.Outcome == PushSendOutcome.TransientFailure
                ? ResolveRetryDelay(message.Attempts)
                : null;
            ApplyProviderResult(message, installation, result, retryDelay);
            _dependencies.Logger.LogInformation(
                "Processed push notification {NotificationId} for installation {InstallationId}, event {EventId}, category {Category} with provider status {ProviderStatus} and outcome {Outcome}.",
                message.Id,
                installation.InstallationId,
                message.EventId,
                message.Type,
                result.ProviderStatus,
                result.Outcome);

            await _dependencies.UnitOfWork.SaveChangesAsync(cancellationToken);

        }
        catch (OperationCanceledException)
        {
            await RecoverFromPostClaimFailureAsync(notificationId, message, deliveryAttemptStarted, new OperationCanceledException());
            throw;
        }
        catch (Exception exception)
        {
            await RecoverFromPostClaimFailureAsync(notificationId, message, deliveryAttemptStarted, exception);
            throw;
        }

        if (message != null && retryDelay != null)
        {
            try
            {
                ScheduleRetryIfAvailable(message, retryDelay);
                await _dependencies.UnitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                message.SchedulerJobId = null;
                _dependencies.Logger.LogWarning(
                    "Retry scheduling failed for push notification {NotificationId} with {ExceptionType}; the due delivery remains unscheduled.",
                    message.Id,
                    GetSafeExceptionType(exception));
                throw;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void ApplyProviderResult(
        PushNotificationMessage message,
        PushInstallation installation,
        PushSendAttemptResult result,
        TimeSpan? retryDelay)
    {
        message.ProviderStatus = result.ProviderStatus;
        message.ProviderMessageId = result.ProviderMessageId;
        message.ProviderErrorCode = result.ProviderErrorCode;
        message.ProviderResponseSummary = result.ProviderResponseSummary;
        message.LastError = result.ProviderResponseSummary;

        switch (result.Outcome)
        {
            case PushSendOutcome.Sent:
                message.Status = PushNotificationStatus.Sent;
                message.FailureKind = PushNotificationFailureKind.None;
                message.SentAt = DateTimeOffset.UtcNow;
                message.NextAttemptAt = null;
                message.LastError = null;
                break;

            case PushSendOutcome.Skipped:
                message.Status = PushNotificationStatus.Skipped;
                message.FailureKind = PushNotificationFailureKind.Preference;
                message.NextAttemptAt = null;
                break;

            case PushSendOutcome.InvalidToken:
                message.Status = PushNotificationStatus.Failed;
                message.FailureKind = PushNotificationFailureKind.InvalidToken;
                message.NextAttemptAt = null;
                installation.DisabledAt = DateTimeOffset.UtcNow;
                installation.DisabledReason = PushInstallationDisabledReasons.InvalidToken;
                installation.LastSeenAt = DateTimeOffset.UtcNow;
                break;

            case PushSendOutcome.TransientFailure:
                message.Status = PushNotificationStatus.Failed;
                message.FailureKind = PushNotificationFailureKind.Transient;
                message.NextAttemptAt = retryDelay is { } delay ? DateTimeOffset.UtcNow.Add(delay) : null;
                message.SchedulerJobId = null;
                break;

            default:
                message.Status = PushNotificationStatus.Failed;
                message.FailureKind = PushNotificationFailureKind.Permanent;
                message.NextAttemptAt = null;
                break;
        }
    }

    private void ScheduleRetryIfAvailable(PushNotificationMessage message, TimeSpan? retryDelay)
    {
        if (retryDelay == null)
        {
            _dependencies.Logger.LogWarning(
                "Transient push notification {NotificationId} exhausted retry attempts at attempt {Attempt}.",
                message.Id,
                message.Attempts);
            return;
        }

        message.SchedulerJobId = _dependencies.PushBackgroundScheduler.ScheduleRetry(message.Id.ToString(), retryDelay.Value);
    }

    private async Task RecoverFromPostClaimFailureAsync(
        Id<PushNotificationMessage> notificationId,
        PushNotificationMessage? message,
        bool deliveryAttemptStarted,
        Exception exception)
    {
        message ??= await _dependencies.PushNotificationMessageRepository.FindByIdAsync(notificationId, CancellationToken.None);
        if (message == null)
        {
            _dependencies.Logger.LogWarning("Push notification {NotificationId} was not found during post-claim recovery.", notificationId);
            return;
        }

        RecordAttemptForRecoveryIfNeeded(message, deliveryAttemptStarted);
        var retryDelay = ResolveRetryDelay(message.Attempts);
        var exceptionType = GetSafeExceptionType(exception);
        var errorSummary = CreateSafeExceptionSummary(exceptionType);

        message.Status = PushNotificationStatus.Failed;
        message.FailureKind = PushNotificationFailureKind.Transient;
        message.ProviderStatus = "Exception";
        message.ProviderMessageId = null;
        message.ProviderErrorCode = exceptionType;
        message.ProviderResponseSummary = errorSummary;
        message.LastError = errorSummary;
        message.NextAttemptAt = retryDelay is { } delay ? DateTimeOffset.UtcNow.Add(delay) : null;
        message.SchedulerJobId = null;

        await _dependencies.UnitOfWork.SaveChangesAsync(CancellationToken.None);
        var retryScheduled = false;
        try
        {
            ScheduleRetryIfAvailable(message, retryDelay);
            if (retryDelay != null)
            {
                await _dependencies.UnitOfWork.SaveChangesAsync(CancellationToken.None);
                retryScheduled = true;
            }
        }
        catch (Exception schedulingException)
        {
            message.SchedulerJobId = null;
            _dependencies.Logger.LogWarning(
                "Retry scheduling failed during recovery for push notification {NotificationId} with {ExceptionType}; the due delivery remains unscheduled.",
                message.Id,
                GetSafeExceptionType(schedulingException));
        }

        _dependencies.Logger.LogWarning(
            "Push notification {NotificationId} failed after claim with {ExceptionType}; retry scheduled: {RetryScheduled}.",
            message.Id,
            exceptionType,
            retryScheduled);
    }

    private static string CreateSafeExceptionSummary(string exceptionType)
        => $"Push delivery exception: {exceptionType}";

    private static string GetSafeExceptionType(Exception exception)
    {
        var exceptionType = exception.GetType().Name;
        return exceptionType.Length <= MaximumExceptionTypeLength
            ? exceptionType
            : exceptionType[..MaximumExceptionTypeLength];
    }

    private static void RecordAttemptForRecoveryIfNeeded(PushNotificationMessage message, bool deliveryAttemptStarted)
    {
        if (!deliveryAttemptStarted)
        {
            message.Attempts += 1;
        }
    }

    private TimeSpan? ResolveRetryDelay(int attempts)
    {
        if (attempts <= 0 || attempts > _dependencies.RetrySettings.RetryDelaysSeconds.Count)
        {
            return null;
        }

        return TimeSpan.FromSeconds(_dependencies.RetrySettings.RetryDelaysSeconds[attempts - 1]);
    }
}
