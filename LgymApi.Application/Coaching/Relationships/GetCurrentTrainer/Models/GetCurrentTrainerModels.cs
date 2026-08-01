using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Relationships.GetCurrentTrainer;

public sealed record GetCurrentTrainerQuery(Id<UserEntity> TraineeId);

public sealed record CurrentTrainerReadModel(
    Id<UserEntity> TrainerId,
    string Name,
    string Email,
    string? Avatar,
    DateTimeOffset LinkedAt);
