using LgymApi.Application.Notifications.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Notifications;

internal interface IInAppNotificationRepository
{
    Task AddAsync(InAppNotification notification, CancellationToken cancellationToken = default);
    Task<InAppNotification?> FindByDeliveryKeyAsync(Id<User> recipientId, InAppNotificationType type, string deliveryKey, CancellationToken cancellationToken = default);
    void Detach(InAppNotification notification);
    Task<InAppNotification?> GetByIdAsync(Id<InAppNotification> id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InAppNotification>> GetPageAsync(Id<User> userId, int limit, DateTimeOffset? cursorCreatedAt, Id<InAppNotification>? cursorId, CancellationToken cancellationToken = default);
    Task MarkAsReadAsync(Id<InAppNotification> id, CancellationToken cancellationToken = default);
    Task MarkAllAsReadAsync(Id<User> userId, DateTimeOffset? before, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(Id<User> userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns no more than the positive candidate limit of notifications, including soft-deleted rows, created strictly before the caller-computed UTC cutoff, ordered oldest first.
    /// </summary>
    Task<IReadOnlyList<InAppNotification>> GetRetentionCandidatesCreatedBeforeAsync(
        DateTimeOffset cutoff,
        int candidateLimit,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    /// <summary>
    /// Stages deletion of the selected notifications; the caller owns the unit-of-work commit.
    /// </summary>
    void RemoveRange(IEnumerable<InAppNotification> notifications)
        => throw new NotSupportedException();
}
