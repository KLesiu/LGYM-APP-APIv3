using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Coaching.Contracts.Access;

public sealed record CoachingRelationshipAccessDecision(
    bool IsTrainer,
    bool HasActiveRelationship);

public interface ICoachingRelationshipAccessService
{
    Task<CoachingRelationshipAccessDecision> GetAccessDecisionAsync(
        Id<UserEntity> trainerId,
        Id<UserEntity> traineeId,
        CancellationToken cancellationToken = default);
}

public interface IMarkerCoachingRelationshipAccessService
{
    Task<CoachingRelationshipAccessDecision> GetAccessDecisionAsync(
        Id<AccountReference> trainerId,
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default);
}
