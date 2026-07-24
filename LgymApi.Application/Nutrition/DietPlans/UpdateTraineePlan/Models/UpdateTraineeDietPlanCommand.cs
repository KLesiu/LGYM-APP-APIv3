using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan;

public sealed record UpdateTraineeDietPlanCommand(
    Id<User> TrainerId,
    Id<User> TraineeId,
    Id<DietPlan> DietPlanId,
    DietPlanUpsertData Data);
