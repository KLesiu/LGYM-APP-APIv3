using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Nutrition.Supplementation.GetTraineePlans;

public sealed record GetTraineeSupplementPlansQuery(
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId);
