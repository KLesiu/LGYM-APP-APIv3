using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Relationships.UnlinkTrainee;

public sealed record UnlinkTraineeCommand(
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId);
