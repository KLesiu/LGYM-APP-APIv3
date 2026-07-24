using LgymApi.Application.Common.Errors;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Nutrition.DietPlans;

internal static class DietPlanAccess
{
    public static AppError? GetTrainerAccessError(bool isTrainer, bool hasActiveRelationship)
    {
        if (!isTrainer)
        {
            return new TrainerRelationshipForbiddenError(Messages.TrainerRoleRequired);
        }

        return hasActiveRelationship ? null : new NotFoundError(Messages.DidntFind);
    }

    public static bool IsOwnedBy(DietPlan plan, Id<UserEntity> trainerId, Id<UserEntity> traineeId)
        => plan.TrainerId == trainerId
           && plan.TraineeId == traineeId
           && !plan.IsDeleted;
}
