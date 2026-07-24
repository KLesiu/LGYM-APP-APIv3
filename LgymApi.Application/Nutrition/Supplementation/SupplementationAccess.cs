using LgymApi.Application.Common.Errors;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Nutrition.Supplementation;

internal static class SupplementationAccess
{
    public static AppError? GetTrainerAccessError(
        bool isTrainer,
        bool hasActiveRelationship,
        Id<UserEntity> traineeId)
    {
        if (!isTrainer)
        {
            return new SupplementationForbiddenError(Messages.TrainerRoleRequired);
        }

        if (traineeId.IsEmpty)
        {
            return new InvalidSupplementationError(Messages.UserIdRequired);
        }

        return hasActiveRelationship
            ? null
            : new SupplementationNotFoundError(Messages.DidntFind);
    }

    public static bool IsOwnedBy(SupplementPlan plan, Id<UserEntity> trainerId, Id<UserEntity> traineeId)
        => plan.TrainerId == trainerId
           && plan.TraineeId == traineeId
           && !plan.IsDeleted;
}
