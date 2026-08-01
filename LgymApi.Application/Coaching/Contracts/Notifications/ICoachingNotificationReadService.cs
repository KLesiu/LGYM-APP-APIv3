using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Coaching.Contracts.Notifications;

public interface ICoachingNotificationReadService
{
    Task<CoachingInvitationNotificationFact?> GetInvitationAsync(
        Id<TrainerInvitation> invitationId,
        CancellationToken cancellationToken = default);
}
