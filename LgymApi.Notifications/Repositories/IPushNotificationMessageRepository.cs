using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Repositories;

internal interface IPushNotificationMessageRepository
{
    Task AddAsync(PushNotificationMessage message, CancellationToken cancellationToken = default);
    void Detach(PushNotificationMessage message);
    Task<PushNotificationMessage?> FindByIdAsync(Id<PushNotificationMessage> id, CancellationToken cancellationToken = default);
    Task<PushNotificationMessage?> FindByDeliveryKeyAsync(Id<PushInstallation> installationId, string type, string eventId, CancellationToken cancellationToken = default);
    Task<bool> TryReserveSchedulingAsync(Id<PushNotificationMessage> id, string reservationId, CancellationToken cancellationToken = default);
    Task ClearSchedulingReservationAsync(Id<PushNotificationMessage> id, string reservationId, CancellationToken cancellationToken = default);
    Task<bool> TryTransitionToSendingAsync(Id<PushNotificationMessage> id, CancellationToken cancellationToken = default);
    Task UpdateAsync(PushNotificationMessage message, CancellationToken cancellationToken = default);
    Task<List<PushNotificationMessage>> GetByStatusAsync(PushNotificationStatus status, CancellationToken cancellationToken = default);
}
