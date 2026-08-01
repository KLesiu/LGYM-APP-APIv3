using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

internal sealed class PushNotificationMessageRepository : IPushNotificationMessageRepository
{
    private readonly INotificationsPersistenceContext _persistenceContext;

    public PushNotificationMessageRepository(INotificationsPersistenceContext persistenceContext)
    {
        _persistenceContext = persistenceContext;
    }

    public Task AddAsync(PushNotificationMessage message, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.PushNotificationMessages.AddAsync(message, cancellationToken).AsTask();
    }

    public void Detach(PushNotificationMessage message)
    {
        _persistenceContext.Entry(message).State = EntityState.Detached;
    }

    public Task<PushNotificationMessage?> FindByIdAsync(Id<PushNotificationMessage> id, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.PushNotificationMessages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public Task<PushNotificationMessage?> FindByDeliveryKeyAsync(
        Id<PushInstallation> installationId,
        string type,
        string eventId,
        CancellationToken cancellationToken = default)
    {
        return _persistenceContext.PushNotificationMessages.FirstOrDefaultAsync(
            x => x.PushInstallationId == installationId && x.Type == type && x.EventId == eventId,
            cancellationToken);
    }

    public async Task<bool> TryReserveSchedulingAsync(
        Id<PushNotificationMessage> id,
        string reservationId,
        CancellationToken cancellationToken = default)
    {
        var reserved = await _persistenceContext.PushNotificationMessages
            .Where(x => x.Id == id
                        && x.SchedulerJobId == null
                        && (x.Status == PushNotificationStatus.Pending
                            || (x.Status == PushNotificationStatus.Failed
                                && x.FailureKind == PushNotificationFailureKind.Transient)))
            .StageUpdateAsync(
                _persistenceContext.PushNotificationMessages,
                x => x.SchedulerJobId,
                _ => reservationId,
                cancellationToken);

        return reserved > 0;
    }

    public Task ClearSchedulingReservationAsync(
        Id<PushNotificationMessage> id,
        string reservationId,
        CancellationToken cancellationToken = default)
    {
        return _persistenceContext.PushNotificationMessages
            .Where(x => x.Id == id && x.SchedulerJobId == reservationId)
            .StageUpdateAsync(
                _persistenceContext.PushNotificationMessages,
                x => x.SchedulerJobId,
                _ => null,
                cancellationToken);
    }

    public async Task<bool> TryTransitionToSendingAsync(Id<PushNotificationMessage> id, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var claimed = await _persistenceContext.PushNotificationMessages
            .Where(x => x.Id == id
                        && (x.Status == PushNotificationStatus.Pending
                            || (x.Status == PushNotificationStatus.Failed
                                && x.FailureKind == PushNotificationFailureKind.Transient
                                && (x.NextAttemptAt == null || x.NextAttemptAt <= now))))
            .StageUpdateAsync(
                _persistenceContext.PushNotificationMessages,
                x => x.Status,
                _ => PushNotificationStatus.Sending,
                x => x.LastAttemptAt,
                _ => now,
                cancellationToken);

        return claimed > 0;
    }

    public Task UpdateAsync(PushNotificationMessage message, CancellationToken cancellationToken = default)
    {
        _persistenceContext.PushNotificationMessages.Update(message);
        return Task.CompletedTask;
    }

    public Task<List<PushNotificationMessage>> GetByStatusAsync(PushNotificationStatus status, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.PushNotificationMessages
            .Where(x => x.Status == status)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
