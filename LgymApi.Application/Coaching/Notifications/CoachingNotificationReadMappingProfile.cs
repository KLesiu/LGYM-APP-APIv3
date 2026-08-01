using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Mapping.Core;

namespace LgymApi.Application.Coaching.Notifications;

public sealed class CoachingNotificationReadMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<CoachingInvitationFact, CoachingInvitationNotificationFact>((source, _) => new CoachingInvitationNotificationFact(
            source.Id,
            source.TrainerId,
            source.TraineeId,
            source.InviteeEmail,
            source.Code,
            source.ExpiresAt));
    }
}
