using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Coaching.Persistence;

public interface ICoachingActiveLinkPersistence
{
    Task AddAsync(CoachingActiveLinkWriteModel link, CancellationToken cancellationToken = default);
    Task RemoveAsync(Id<TrainerTraineeLink> linkId, CancellationToken cancellationToken = default);
    Task<bool> HasForTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default);
    Task<bool> HasForTraineeAsync(Id<AccountReference> traineeId, CancellationToken cancellationToken = default)
        => HasForTraineeAsync(traineeId.Rebind<User>(), cancellationToken);
    Task<CoachingActiveLinkFact?> FindByTrainerAndTraineeAsync(Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default);
    Task<CoachingActiveLinkFact?> FindByTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default);
    Task<bool> HasActiveRelationshipAsync(Id<AccountReference> trainerId, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
}
