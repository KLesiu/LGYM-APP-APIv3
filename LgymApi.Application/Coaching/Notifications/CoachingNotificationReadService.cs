using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Coaching.Notifications;

public sealed class CoachingNotificationReadService(
    ICoachingInvitationPersistence invitationPersistence,
    IMapper mapper) : ICoachingNotificationReadService
{
    public async Task<CoachingInvitationNotificationFact?> GetInvitationAsync(
        Id<TrainerInvitation> invitationId,
        CancellationToken cancellationToken = default)
    {
        var invitation = await invitationPersistence.FindByIdAsync(invitationId, cancellationToken);
        return invitation is null
            ? null
            : mapper.Map<CoachingInvitationFact, CoachingInvitationNotificationFact>(invitation, mapper.CreateContext());
    }
}
