using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory.Models;

public sealed record GetTraineeDietPlanHistoryQuery(
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId,
    Id<DietPlan> DietPlanId);
