using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Relationships.DetachFromTrainer;

public sealed record DetachFromTrainerCommand(Id<UserEntity> TraineeId);
