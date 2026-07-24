using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Nutrition.DietPlans.CreateTraineePlan;

public sealed record CreateTraineeDietPlanCommand(
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId,
    DietPlanUpsertData Data);
