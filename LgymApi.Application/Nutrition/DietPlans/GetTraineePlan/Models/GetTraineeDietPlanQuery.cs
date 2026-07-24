using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Nutrition.DietPlans.GetTraineePlan;

public sealed record GetTraineeDietPlanQuery(
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId,
    Id<DietPlan> DietPlanId);
