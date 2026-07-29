using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Domain.Security;

namespace LgymApi.Application.Coaching.Access;

internal sealed class CoachingRelationshipAccessService : ICoachingRelationshipAccessService, IMarkerCoachingRelationshipAccessService
{
    private readonly IUserAccessReadService _userAccess;
    private readonly ICoachingActiveLinkPersistence _activeLinks;
    private readonly IAccountAccessReader _accountAccess;

    public CoachingRelationshipAccessService(
        IUserAccessReadService userAccess,
        ICoachingActiveLinkPersistence activeLinks,
        IAccountAccessReader accountAccess)
    {
        _userAccess = userAccess;
        _activeLinks = activeLinks;
        _accountAccess = accountAccess;
    }

    public async Task<CoachingRelationshipAccessDecision> GetAccessDecisionAsync(
        Id<UserEntity> trainerId,
        Id<UserEntity> traineeId,
        CancellationToken cancellationToken = default)
    {
        if (trainerId.IsEmpty)
        {
            return new CoachingRelationshipAccessDecision(false, false);
        }

        var isTrainer = await _userAccess.IsTrainerAsync(trainerId, cancellationToken);
        if (!isTrainer || traineeId.IsEmpty)
        {
            return new CoachingRelationshipAccessDecision(isTrainer, false);
        }

        var activeLink = await _activeLinks.FindByTrainerAndTraineeAsync(trainerId, traineeId, cancellationToken);
        return new CoachingRelationshipAccessDecision(true, activeLink is not null);
    }

    public async Task<CoachingRelationshipAccessDecision> GetAccessDecisionAsync(
        Id<AccountReference> trainerId,
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default)
    {
        if (trainerId.IsEmpty) return new CoachingRelationshipAccessDecision(false, false);
        var account = await _accountAccess.GetByIdAsync(trainerId, cancellationToken);
        var isTrainer = account?.Roles.Contains(AuthConstants.Roles.Trainer, StringComparer.Ordinal) == true;
        if (!isTrainer || traineeId.IsEmpty) return new CoachingRelationshipAccessDecision(isTrainer, false);
        return new CoachingRelationshipAccessDecision(true, await _activeLinks.HasActiveRelationshipAsync(trainerId, traineeId, cancellationToken));
    }
}
