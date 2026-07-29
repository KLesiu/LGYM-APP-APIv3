using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications.Email;

internal sealed class EmailJobHandlerService : Contracts.Email.IEmailJobExecutionPort
{
    private readonly IEmailNotificationLogRepository _notificationLogRepository;
    private readonly IEmailTemplateComposerFactory _templateComposerFactory;
    private readonly IEmailSender _emailSender;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailMetrics _metrics;
    private readonly ILogger<EmailJobHandlerService> _logger;

    public EmailJobHandlerService(
        IEmailNotificationLogRepository notificationLogRepository,
        IEmailTemplateComposerFactory templateComposerFactory,
        IEmailSender emailSender,
        IUnitOfWork unitOfWork,
        IEmailMetrics metrics,
        ILogger<EmailJobHandlerService> logger)
    {
        _notificationLogRepository = notificationLogRepository;
        _templateComposerFactory = templateComposerFactory;
        _emailSender = emailSender;
        _unitOfWork = unitOfWork;
        _metrics = metrics;
        _logger = logger;
    }

    public Task ProcessAsync(string notificationId, CancellationToken cancellationToken = default)
    {
        if (!Id<NotificationMessage>.TryParse(notificationId, out var parsedNotificationId))
        {
            throw new ArgumentException("Email notification ID is invalid.", nameof(notificationId));
        }

        return ProcessAsync(parsedNotificationId, cancellationToken);
    }

    private async Task ProcessAsync(Id<NotificationMessage> notificationId, CancellationToken cancellationToken)
    {
        // First step: atomically claim the notification for sending (Pending -> Sending).
        // The repository guards the transition by the current Pending status, so a concurrent
        // dispatcher cannot double-claim the same notification. If the claim fails the
        // notification is already claimed or in a terminal state, so we skip to avoid duplicates.
        var claimed = await _notificationLogRepository.TryTransitionToSendingAsync(notificationId, cancellationToken);
        if (!claimed)
        {
            _logger.LogInformation(
                "Email notification {NotificationId} could not be claimed for sending; skipping to avoid duplicate delivery.",
                notificationId);
            return;
        }

        var notification = await _notificationLogRepository.FindByIdAsync(notificationId, cancellationToken);
        if (notification == null)
        {
            _logger.LogWarning(
                "Email notification {NotificationId} was not found after claim. The job will be skipped.",
                notificationId);
            return;
        }

        notification.Attempts += 1;

        if (notification.Attempts > 1)
        {
            _metrics.RecordRetried(notification.Type);
            _logger.LogInformation(
                "Retrying email notification {NotificationId} (attempt {Attempt}).",
                notificationId,
                notification.Attempts);
        }

        EmailMessage message;
        try
        {
            message = _templateComposerFactory.ComposeMessage(notification.Type, notification.PayloadJson);
        }
        catch (Exception ex)
        {
            notification.Status = EmailNotificationStatus.Failed;
            notification.LastError = ToSafeError(ex);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _metrics.RecordFailed(notification.Type);
            _logger.LogError(ex, "Failed to compose email template for notification {NotificationId}.", notificationId);
            throw new InvalidOperationException($"Failed to compose email template for notification {notificationId}.", ex);
        }

        try
        {
            var delivered = await _emailSender.SendAsync(message, cancellationToken);
            if (!delivered)
            {
                notification.Status = EmailNotificationStatus.Failed;
                notification.LastError = "Email sender is disabled.";
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _metrics.RecordFailed(notification.Type);
                _logger.LogWarning(
                    "Email sender is disabled; notification {NotificationId} was not delivered.",
                    notificationId);
                return;
            }

            // Crash-safe: persist the Sent transition first so a crash between send and
            // persistence cannot cause a duplicate send on retry.
            notification.Status = EmailNotificationStatus.Sent;
            notification.SentAt = DateTimeOffset.UtcNow;
            notification.LastError = null;

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception persistenceException)
            {
                if (_logger.IsEnabled(LogLevel.Critical))
                {
                    _logger.LogCritical(
                        persistenceException,
                        "Email notification {NotificationId} was delivered successfully but persisting Sent status failed. Suppressing retry to avoid duplicate email delivery.",
                        notificationId);
                }
                return;
            }

            // Secondary delivery detail persisted in a separate save. A failure here does not
            // risk a duplicate send because the Sent transition above is already durable.
            notification.DeliveredAt = DateTimeOffset.UtcNow;
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception persistenceException)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(
                        persistenceException,
                        "Email notification {NotificationId} was delivered and marked Sent, but persisting DeliveredAt failed.",
                        notificationId);
                }
            }

            _metrics.RecordSent(notification.Type);
            _logger.LogInformation(
                "Email notification {NotificationId} sent successfully on attempt {Attempt}.",
                notificationId,
                notification.Attempts);
        }
        catch (Exception ex)
        {
            notification.Status = EmailNotificationStatus.Failed;
            notification.LastError = ToSafeError(ex);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _metrics.RecordFailed(notification.Type);
            _logger.LogError(ex, "Failed to send email for notification {NotificationId}.", notificationId);
            throw new InvalidOperationException($"Failed to send email for notification {notificationId}.", ex);
        }
    }

    private static string ToSafeError(Exception exception)
    {
        var message = exception.GetType().Name;
        if (!string.IsNullOrWhiteSpace(exception.Message))
        {
            message = $"{message}: {exception.Message}";
        }

        message = message.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return message.Length <= 400 ? message : message[..400];
    }
}
