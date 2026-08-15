using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Application.Notifications;
using LgymApi.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

internal sealed class InAppNotificationRepository : IInAppNotificationRepository
{
    private readonly INotificationsPersistenceContext _persistenceContext;

    public InAppNotificationRepository(INotificationsPersistenceContext persistenceContext)
    {
        _persistenceContext = persistenceContext;
    }

    public Task AddAsync(InAppNotification notification, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.InAppNotifications.AddAsync(notification, cancellationToken).AsTask();
    }

    public Task<InAppNotification?> FindByDeliveryKeyAsync(
        Id<User> recipientId,
        InAppNotificationType type,
        string deliveryKey,
        CancellationToken cancellationToken = default)
    {
        return _persistenceContext.InAppNotifications
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.RecipientId == recipientId
                     && x.Type == type
                     && x.DeliveryKey == deliveryKey
                     && !x.IsDeleted,
                cancellationToken);
    }

    public void Detach(InAppNotification notification)
    {
        _persistenceContext.Entry(notification).State = EntityState.Detached;
    }

    public Task<InAppNotification?> GetByIdAsync(Id<InAppNotification> id, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.InAppNotifications
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<InAppNotification>> GetPageAsync(
        Id<User> userId,
        int limit,
        DateTimeOffset? cursorCreatedAt,
        Id<InAppNotification>? cursorId,
        CancellationToken cancellationToken = default)
    {
        var query = _persistenceContext.InAppNotifications
            .AsNoTracking()
            .Where(x => x.RecipientId == userId && !x.IsDeleted);

        if (cursorCreatedAt.HasValue && cursorId.HasValue)
        {
            var cursorTs = cursorCreatedAt.Value;
            var cursorNotificationId = cursorId.Value;

            query = query.Where(x => x.CreatedAt < cursorTs || (x.CreatedAt == cursorTs && x.Id.CompareByValue(cursorNotificationId) < 0));
        }

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
    }

    public Task MarkAsReadAsync(Id<InAppNotification> id, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.InAppNotifications
            .Where(x => x.Id == id)
            .ForEachAsync(x => x.IsRead = true, cancellationToken);
    }

    public Task MarkAllAsReadAsync(Id<User> userId, DateTimeOffset? before, CancellationToken cancellationToken = default)
    {
        var query = _persistenceContext.InAppNotifications
            .Where(x => x.RecipientId == userId && !x.IsRead);

        if (before.HasValue)
        {
            query = query.Where(x => x.CreatedAt <= before.Value);
        }

        return query.ForEachAsync(x => x.IsRead = true, cancellationToken);
    }

    public Task<int> GetUnreadCountAsync(Id<User> userId, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.InAppNotifications
            .AsNoTracking()
            .CountAsync(x => x.RecipientId == userId && !x.IsRead && !x.IsDeleted, cancellationToken);
    }

    public async Task<IReadOnlyList<InAppNotification>> GetRetentionCandidatesCreatedBeforeAsync(
        DateTimeOffset cutoff,
        int candidateLimit,
        CancellationToken cancellationToken = default)
    {
        return await _persistenceContext.InAppNotifications
            .IgnoreQueryFilters()
            .Where(notification => notification.CreatedAt < cutoff)
            .OrderBy(notification => notification.CreatedAt)
            .ThenBy(notification => notification.Id)
            .Take(candidateLimit)
            .ToListAsync(cancellationToken);
    }

    public void RemoveRange(IEnumerable<InAppNotification> notifications)
    {
        _persistenceContext.InAppNotifications.RemoveRange(notifications);
    }
}
