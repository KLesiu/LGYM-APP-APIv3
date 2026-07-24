using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlan;

public sealed record GetCurrentDietPlanQuery(Id<User> TraineeId);
