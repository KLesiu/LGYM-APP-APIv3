using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

internal sealed class EmailNotificationLogRepository : IEmailNotificationLogRepository
{
    private readonly INotificationsPersistenceContext _persistenceContext;
    private readonly int _emailSendLeaseSeconds;

    public EmailNotificationLogRepository(
        INotificationsPersistenceContext persistenceContext,
        IEmailNotificationLeaseSettings? leaseSettings = null)
    {
        _persistenceContext = persistenceContext;
        _emailSendLeaseSeconds = leaseSettings?.EmailSendLeaseSeconds ?? 30;
    }

    public async Task AddAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        await _persistenceContext.NotificationMessages.AddAsync(message, cancellationToken);
    }

    public Task<NotificationMessage?> FindByIdAsync(Id<NotificationMessage> id, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.NotificationMessages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public Task<NotificationMessage?> FindByCorrelationAsync(EmailNotificationType type, Id<CorrelationScope> correlationId, string recipient, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.NotificationMessages.FirstOrDefaultAsync(
            x => x.Channel == NotificationChannel.Email && x.Type == type && x.CorrelationId == correlationId && x.Recipient == recipient,
            cancellationToken);
    }

    public async Task<List<NotificationMessage>> GetPendingUndispatchedAsync(CancellationToken cancellationToken = default)
    {
        return await _persistenceContext.NotificationMessages
            .Where(x => x.Status == EmailNotificationStatus.Pending && x.DispatchedAt == null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<NotificationMessage>> GetFailedAsync(CancellationToken cancellationToken = default)
    {
        return await _persistenceContext.NotificationMessages
            .Where(x => x.Status == EmailNotificationStatus.Failed)
            .OrderBy(x => x.LastAttemptAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<NotificationMessage>> GetDeadLetteredAsync(CancellationToken cancellationToken = default)
    {
        return await _persistenceContext.NotificationMessages
            .Where(x => x.IsDeadLettered)
            .OrderBy(x => x.LastAttemptAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountByStatusAsync(EmailNotificationStatus status, CancellationToken cancellationToken = default)
    {
        return await _persistenceContext.NotificationMessages
            .Where(x => x.Status == status)
            .CountAsync(cancellationToken);
    }

    public async Task<int> DeleteSentOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken = default)
    {
        var messagesToDelete = await _persistenceContext.NotificationMessages
            .Where(x => x.Status == EmailNotificationStatus.Sent && x.SentAt != null && x.SentAt < cutoffDate)
            .ToListAsync(cancellationToken);

        foreach (var message in messagesToDelete)
        {
            _persistenceContext.NotificationMessages.Remove(message);
        }

        return messagesToDelete.Count;
    }

    public async Task<bool> TryTransitionToSendingAsync(Id<NotificationMessage> id, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseCutoff = now.AddSeconds(-_emailSendLeaseSeconds);

        var claimed = await _persistenceContext.NotificationMessages
            .Where(x => x.Id == id
                        && (
                            x.Status == EmailNotificationStatus.Pending
                            || (x.Status == EmailNotificationStatus.Sending
                                && x.DeliveredAt == null
                                && (x.LastAttemptAt == null || x.LastAttemptAt < leaseCutoff))
                            || (x.Status == EmailNotificationStatus.Failed && !x.IsDeadLettered)))
            .StageUpdateAsync(
                _persistenceContext.NotificationMessages,
                x => x.Status,
                _ => EmailNotificationStatus.Sending,
                x => x.LastAttemptAt,
                _ => now,
                cancellationToken);

        return claimed > 0;
    }

    public async Task<List<NotificationMessage>> GetStuckSendingAsync(int emailSendLeaseSeconds, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-emailSendLeaseSeconds);

        return await _persistenceContext.NotificationMessages
            .Where(x => x.Status == EmailNotificationStatus.Sending
                        && (x.LastAttemptAt == null || x.LastAttemptAt < cutoff))
            .OrderBy(x => x.LastAttemptAt)
            .ToListAsync(cancellationToken);
    }
}
