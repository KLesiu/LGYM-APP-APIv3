using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Nutrition.DietPlans.GetTraineePlans;

public sealed record GetTraineeDietPlansQuery(
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId);
