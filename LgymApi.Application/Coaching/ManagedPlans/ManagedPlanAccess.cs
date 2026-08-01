using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Resources;

namespace LgymApi.Application.Coaching.ManagedPlans;

internal static class ManagedPlanAccess
{
    public static AppError? GetError(
        CoachingRelationshipAccessDecision access,
        Id<AccountReference> traineeId)
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
