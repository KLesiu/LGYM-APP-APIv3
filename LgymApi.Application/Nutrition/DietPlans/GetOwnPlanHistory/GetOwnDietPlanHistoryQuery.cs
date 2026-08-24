using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Nutrition.DietPlans.GetOwnPlanHistory;

internal sealed record GetOwnDietPlanHistoryQuery(
    Id<User> TraineeId,
    Id<DietPlan> DietPlanId);
