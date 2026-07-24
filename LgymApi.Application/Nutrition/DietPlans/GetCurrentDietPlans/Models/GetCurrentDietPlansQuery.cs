using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlans;

public sealed record GetCurrentDietPlansQuery(Id<User> TraineeId);
