using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Nutrition.Supplementation.UnassignTraineePlan;

public sealed record UnassignTraineeSupplementPlanCommand(
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId);
