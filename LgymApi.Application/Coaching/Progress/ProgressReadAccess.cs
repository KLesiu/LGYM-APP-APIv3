using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Progress;

internal static class ProgressReadAccess
{
    public static AppError? GetError(
        CoachingRelationshipAccessDecision access,
        Id<UserEntity> traineeId)
    {
        if (!access.IsTrainer)
        {
            return new TrainerRelationshipForbiddenError(Messages.TrainerRoleRequired);
        }

        if (traineeId.IsEmpty)
        {
            return new InvalidTrainerRelationshipError(Messages.UserIdRequired);
        }

        return access.HasActiveRelationship
            ? null
            : new TrainerRelationshipNotFoundError(Messages.DidntFind);
    }
}
