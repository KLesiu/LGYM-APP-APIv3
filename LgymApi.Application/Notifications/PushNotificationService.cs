using System.Text.Json;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Notifications.Models;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications;

public sealed class PushNotificationService : IPushNotificationService
{
    private static readonly HashSet<string> SkippedPermissionStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "denied",
        "blocked",
        "disabled"
    };
    private const string DeliveryKeyIndexName = "IX_PushNotificationMessages_PushInstallationId_Type_EventId";

    private readonly IPushInstallationRepository _pushInstallationRepository;
    private readonly IPushNotificationMessageRepository _pushNotificationMessageRepository;
    private readonly IPushBackgroundScheduler _pushBackgroundScheduler;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PushNotificationService> _logger;

    public PushNotificationService(
        IPushInstallationRepository pushInstallationRepository,
        IPushNotificationMessageRepository pushNotificationMessageRepository,
        IPushBackgroundScheduler pushBackgroundScheduler,
        IUnitOfWork unitOfWork,
        ILogger<PushNotificationService> logger)
    {
        _pushInstallationRepository = pushInstallationRepository;
        _pushNotificationMessageRepository = pushNotificationMessageRepository;
        _pushBackgroundScheduler = pushBackgroundScheduler;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task EnqueueAsync(EnqueuePushNotificationInput input, CancellationToken cancellationToken = default)
    {
        var normalizedType = NormalizeRequired(input.Type);
        var normalizedEventId = NormalizeRequired(input.EventKey);
        if (normalizedType == null || normalizedEventId == null || input.SchemaVersion <= 0)
        {
            throw new InvalidOperationException("Push notification input is missing required values.");
        }

        var installations = await _pushInstallationRepository.GetActiveByUserIdAsync(input.UserId, cancellationToken);
        if (installations.Count == 0)
        {
            _logger.LogInformation(
                "Skipping push enqueue for user {UserId}, event {EventId}, category {Category}; no active installations.",
                input.UserId,
                normalizedEventId,
                normalizedType);
            return;
        }

        var scheduleCandidates = new List<(PushNotificationMessage Message, PushInstallation Installation)>();
        var stagedMessages = new List<PushNotificationMessage>();
        var hasStagedMessages = false;
        foreach (var installation in installations)
        {
            var existing = await _pushNotificationMessageRepository.FindByDeliveryKeyAsync(
                installation.Id,
                normalizedType,
                normalizedEventId,
                cancellationToken);

            if (existing != null)
            {
                if (ShouldScheduleExisting(existing))
                {
                    scheduleCandidates.Add((existing, installation));
                    _logger.LogInformation(
                        "Found existing unscheduled push notification {NotificationId} for installation {InstallationId}, event {EventId}, category {Category}; scheduling existing message.",
                        existing.Id,
                        installation.InstallationId,
                        normalizedEventId,
                        normalizedType);
                }
                else
                {
                    _logger.LogInformation(
                        "Found existing push notification {NotificationId} for installation {InstallationId}, event {EventId}, category {Category}; skipping duplicate enqueue.",
                        existing.Id,
                        installation.InstallationId,
                        normalizedEventId,
                        normalizedType);
                }

                continue;
            }

            var payload = new PushEventPayload(
                input.SchemaVersion,
                normalizedType,
                normalizedEventId,
                NormalizeOptional(input.EntityKey),
                input.InAppNotificationId,
                NormalizeOptional(input.Deeplink));

            var message = new PushNotificationMessage
            {
                Id = Id<PushNotificationMessage>.New(),
                UserId = input.UserId,
                PushInstallationId = installation.Id,
                SchemaVersion = payload.SchemaVersion,
                Type = payload.Type,
                EventId = payload.EventId,
                EntityId = payload.EntityId,
                InAppNotificationId = input.InAppNotificationId,
                Deeplink = payload.Deeplink,
                PayloadJson = JsonSerializer.Serialize(payload, SharedSerializationOptions.Current)
            };

            if (ShouldSkipForPreference(installation))
            {
                message.Status = PushNotificationStatus.Skipped;
                message.FailureKind = PushNotificationFailureKind.Preference;
                message.ProviderStatus = "Skipped";
                message.ProviderResponseSummary = $"Permission status '{installation.PermissionStatus}' is not eligible for push delivery.";
                _logger.LogInformation(
                    "Skipping push delivery for installation {InstallationId}, event {EventId}, category {Category} because permission status is {PermissionStatus}.",
                    installation.InstallationId,
                    normalizedEventId,
                    normalizedType,
                    installation.PermissionStatus);
            }
            else
            {
                scheduleCandidates.Add((message, installation));
            }

            await _pushNotificationMessageRepository.AddAsync(message, cancellationToken);
            stagedMessages.Add(message);
            hasStagedMessages = true;
        }

        if (hasStagedMessages)
        {
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (IsDeliveryKeyUniqueViolation(exception))
            {
                foreach (var stagedMessage in stagedMessages)
                {
                    _pushNotificationMessageRepository.Detach(stagedMessage);
                }

                foreach (var stagedMessage in stagedMessages)
                {
                    var winningMessage = await _pushNotificationMessageRepository.FindByDeliveryKeyAsync(
                        stagedMessage.PushInstallationId,
                        stagedMessage.Type,
                        stagedMessage.EventId,
                        cancellationToken);
                    if (winningMessage == null)
                    {
                        throw;
                    }
                }

                _logger.LogInformation(
                    "Resolved concurrent push enqueue conflict for user {UserId}, event {EventId}, category {Category}; the winning delivery remains scheduled by its creator.",
                    input.UserId,
                    normalizedEventId,
                    normalizedType);
                return;
            }
        }

        foreach (var (message, installation) in scheduleCandidates)
        {
            var reservationId = $"scheduling-{Id<PushNotificationMessage>.New().ToString().Replace("-", string.Empty, StringComparison.Ordinal)}";
            if (!await _pushNotificationMessageRepository.TryReserveSchedulingAsync(message.Id, reservationId, cancellationToken))
            {
                continue;
            }

            message.SchedulerJobId = reservationId;
            try
            {
                var schedulerJobId = Schedule(message);
                if (schedulerJobId == null)
                {
                    message.SchedulerJobId = null;
                    await _pushNotificationMessageRepository.ClearSchedulingReservationAsync(message.Id, reservationId, cancellationToken);
                    continue;
                }

                message.SchedulerJobId = schedulerJobId;
            }
            catch
            {
                await _pushNotificationMessageRepository.ClearSchedulingReservationAsync(message.Id, reservationId, cancellationToken);
                throw;
            }

            _logger.LogInformation(
                "Queued push notification {NotificationId} for installation {InstallationId}, event {EventId}, category {Category}.",
                message.Id,
                installation.InstallationId,
                message.EventId,
                message.Type);
        }

        if (scheduleCandidates.Count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool ShouldSkipForPreference(PushInstallation installation)
    {
        return installation.PermissionStatus != null
            && SkippedPermissionStatuses.Contains(installation.PermissionStatus.Trim());
    }

    private static bool ShouldScheduleExisting(PushNotificationMessage message)
    {
        return string.IsNullOrWhiteSpace(message.SchedulerJobId)
            && (message.Status == PushNotificationStatus.Pending
                || (message.Status == PushNotificationStatus.Failed && message.FailureKind == PushNotificationFailureKind.Transient));
    }

    private string? Schedule(PushNotificationMessage message)
    {
        if (message.Status == PushNotificationStatus.Failed
            && message.FailureKind == PushNotificationFailureKind.Transient
            && message.NextAttemptAt is { } nextAttemptAt
            && nextAttemptAt > DateTimeOffset.UtcNow)
        {
            return _pushBackgroundScheduler.ScheduleRetry(message.Id, nextAttemptAt - DateTimeOffset.UtcNow);
        }

        return _pushBackgroundScheduler.Enqueue(message.Id);
    }

    private static bool IsDeliveryKeyUniqueViolation(Exception exception)
    {
        return string.Equals(exception.GetType().Name, "DbUpdateException", StringComparison.Ordinal)
            && exception.ToString().Contains(DeliveryKeyIndexName, StringComparison.Ordinal);
    }

    private static string? NormalizeRequired(string? value)
    {
        var normalized = NormalizeOptional(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
