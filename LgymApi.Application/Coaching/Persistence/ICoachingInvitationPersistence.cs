using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Coaching.Persistence;

public interface ICoachingInvitationPersistence
{
    Task AddAsync(CoachingInvitationWriteModel invitation, CancellationToken cancellationToken = default);
    Task<CoachingInvitationFact?> FindByIdAsync(Id<TrainerInvitation> invitationId, CancellationToken cancellationToken = default);
    Task<CoachingInvitationFact?> FindPendingAsync(Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default);
    Task<CoachingInvitationFact?> FindPendingByEmailAsync(Id<User> trainerId, string inviteeEmail, CancellationToken cancellationToken = default);
    Task<CoachingInvitationFact?> FindByIdAndCodeAsync(Id<TrainerInvitation> invitationId, string code, CancellationToken cancellationToken = default);
    Task ExpireAsync(Id<TrainerInvitation> invitationId, DateTimeOffset respondedAt, CancellationToken cancellationToken = default);
    Task UpdateResponseAsync(CoachingInvitationResponseUpdateModel update, CancellationToken cancellationToken = default);
}
